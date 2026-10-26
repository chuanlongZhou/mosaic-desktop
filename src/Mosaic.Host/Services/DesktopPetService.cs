using System.Windows;
using System.Windows.Threading;
using Mosaic.Core;
using Mosaic.Core.Models;
using Mosaic.Host.Views;

namespace Mosaic.Host.Services;

public sealed class DesktopPetService : IDisposable
{
    private sealed class Resident(string id, DesktopPetOptions options, DesktopPetWindow window, DesktopPetBehavior behavior)
    {
        public string Id { get; } = id;
        public DesktopPetOptions Options { get; set; } = options;
        public DesktopPetWindow Window { get; } = window;
        public DesktopPetBehavior Behavior { get; set; } = behavior;
    }
    private readonly AppServices _services;
    private readonly Action _openSettings;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly PetHourlyChimeSchedule _chimeSchedule = new();
    private string? _announcingPetId;
    private readonly PetInputBuffer _input = new();
    private readonly Dictionary<string, Resident> _residents = new(StringComparer.Ordinal);
    private readonly DesktopPetBehavior _inactive = new();
    private DesktopPetMonitor? _monitor;
    private bool _keyboard, _closeEvents, _fullscreen, _disposed;
    private long _lastSample;
    private double _idle;
    public string Status { get; private set; } = "桌宠已收起";
    private Resident? Primary => _residents.GetValueOrDefault(DesktopPetSettings.PrimaryId) ?? _residents.Values.FirstOrDefault();
    public DesktopPetWindow? Window => Primary?.Window;
    public DesktopPetBehavior Behavior => Primary?.Behavior ?? _inactive;
    public IReadOnlyList<(string Id, DesktopPetWindow Window, DesktopPetBehavior Behavior)> Pets => _residents.Values.Select(pet => (pet.Id, pet.Window, pet.Behavior)).ToArray();
    public event Action<string>? SaveFailed;

    public DesktopPetService(AppServices services, Action openSettings)
    {
        _services = services; _openSettings = openSettings; _timer.Tick += (_, _) => Tick();
    }
    public void ApplySettings()
    {
        if (_disposed) return;
        var settings = _services.Settings.Pet;
        if (!settings.Enabled) { Stop(); Status = "桌宠已收起"; return; }
        var desired = settings.Slots().Where(slot => slot.Visible).ToArray();
        if (desired.Length == 0) { Stop(); Status = "同伴都已收起，可在桌宠设置中重新显示"; return; }
        var ids = desired.Select(slot => slot.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _residents.Keys.Where(id => !ids.Contains(id)).ToArray())
        { _residents[id].Window.Close(); _residents.Remove(id); }
        var place = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in desired)
        {
            var options = slot.Options; var id = slot.Id;
            var created = !_residents.TryGetValue(id, out var resident);
            if (created)
            {
                var window = new DesktopPetWindow();
                resident = new(id, options, window, new(character: options.Character)); _residents.Add(id, resident);
                window.ClockReset += ResetHourlyChime;
                window.ChimePreviewRequested += () => PreviewChime(id);
                window.Interacted += action => TriggerFor(id, action);
                window.PositionCommitted += async (left, top) => await SaveAsync(() => _services.UpdatePetAsync(pet =>
                { var own = pet.GetPet(id); own.Left = left; own.Top = top; }));
                window.SettingsRequested += _openSettings;
                window.DisableRequested += async () => await SaveAsync(() => _services.UpdatePetAsync(pet =>
                { if (id == DesktopPetSettings.PrimaryId) pet.PrimaryVisible = false; else pet.GetPet(id).Enabled = false; }));
                resident.Behavior.Trigger(PetTrigger.Wave, Environment.TickCount64);
            }
            ArgumentNullException.ThrowIfNull(resident);
            var positionChanged = created || resident.Options.Left != options.Left || resident.Options.Top != options.Top;
            if (resident.Window.Sprite.Character != options.Character)
            { resident.Behavior = new(character: options.Character); resident.Behavior.Trigger(PetTrigger.Wave, Environment.TickCount64); }
            resident.Options = options; resident.Window.Configure(options, positionChanged);
            if (positionChanged && options.Left is null && options.Top is null) place.Add(id);
        }
        PlaceUnpositionedPets(place);
        var keyboard = desired.Any(slot => slot.Options.ReactToEnter || slot.Options.ReactToDesktop || slot.Options.ReactToTyping);
        var closeEvents = desired.Any(slot => slot.Options.ReactToAppClose);
        if (_monitor is null || _keyboard != keyboard || _closeEvents != closeEvents)
        {
            _monitor?.Dispose(); _monitor = new(keyboard, closeEvents);
            _monitor.Triggered += RecordInput; _keyboard = keyboard; _closeEvents = closeEvents;
        }
        Status = keyboard && !_monitor.KeyboardAvailable ? "键盘响应暂不可用，可继续直接互动" :
            closeEvents && !_monitor.WindowEventsAvailable ? "应用关闭响应暂不可用，其他互动正常" : $"{desired.Length} 位伙伴正在陪你 · 悬停互动，单击聊天，双击投喂，右键更多";
        foreach (var resident in _residents.Values) resident.Window.Sprite.ToolTip = DesktopPetCharacters.Get(resident.Options.Character).Name + " · " + Status;
        _timer.Start(); Tick();
    }
    private void PlaceUnpositionedPets(HashSet<string> pending)
    {
        var work = SystemParameters.WorkArea;
        var occupied = _residents.Values.Where(pet => !pending.Contains(pet.Id)).Select(pet => new Rect(pet.Window.Left, pet.Window.Top, pet.Window.Width, pet.Window.Height)).ToList();
        foreach (var pet in _residents.Values.Where(pet => pending.Contains(pet.Id)))
        {
            var window = pet.Window; var right = work.Right - 24; var bottom = work.Bottom - 52;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var candidate = new Rect(right - window.Width, bottom - window.Height, window.Width, window.Height);
                if (candidate.Left < work.Left)
                { right = work.Right - 24; bottom -= window.Height + 12; continue; }
                var collisions = occupied.Where(rect => rect.IntersectsWith(candidate)).ToArray();
                if (collisions.Length == 0 || candidate.Top < work.Top)
                { window.Left = candidate.Left; window.Top = candidate.Top; break; }
                right = collisions.Min(rect => rect.Left) - 12;
            }
            window.ClampToScreen(); occupied.Add(new(window.Left, window.Top, window.Width, window.Height));
        }
    }
    public void SetFullscreen(bool full) { _fullscreen = full; if (_residents.Count > 0) Tick(); }
    public void EnsureTopmost() { foreach (var pet in _residents.Values) pet.Window.EnsureTopmost(); }
    // One raw-input listener and one bounded activity buffer serve all five pets.
    private void RecordInput(PetTrigger trigger) { if (!_disposed) _input.Record(trigger); }
    private void Trigger(PetTrigger trigger) { if (Primary is { } primary) TriggerFor(primary.Id, trigger); }
    private void TriggerFor(string id, PetTrigger trigger)
    {
        if (_disposed || !_residents.TryGetValue(id, out var pet) || _fullscreen && pet.Options.HideInFullscreen) return;
        ApplyTrigger(pet, trigger, Environment.TickCount64); Tick();
    }
    private static void ApplyTrigger(Resident pet, PetTrigger trigger, long now)
    {
        var options = pet.Options;
        if (trigger == PetTrigger.Enter && !options.ReactToEnter || trigger == PetTrigger.ShowDesktop && !options.ReactToDesktop || trigger == PetTrigger.AppClosed && !options.ReactToAppClose || trigger == PetTrigger.Typing && !options.ReactToTyping) return;
        if (pet.Behavior.IsSleeping && trigger is PetTrigger.Enter or PetTrigger.ShowDesktop or PetTrigger.Typing)
            pet.Behavior.Tick(now, 0, options.SleepWhenIdle, options.IdleSeconds, false, 0, 0);
        pet.Behavior.Trigger(trigger, now);
    }
    private void ResetHourlyChime()
    {
        _chimeSchedule.ResetObservation(); PetChimeAudio.Shared.StopAutomatic(); _announcingPetId = null;
        foreach (var pet in _residents.Values) pet.Window.ClearAnnouncement();
    }
    private bool CanChime(Resident pet) => PetHourlyChime.CanAnnounce(pet.Options, true, _fullscreen) && !pet.Behavior.IsDragging;
    private void PreviewChime(string id)
    {
        if (_residents.TryGetValue(id, out var pet) && PetHourlyChime.Supports(pet.Options.Character) && !(_fullscreen && pet.Options.HideInFullscreen))
            PlayChime(pet, DateTimeOffset.Now.Hour, true);
    }
    private void PlayChime(Resident pet, int hour, bool preview)
    {
        var audio = PetChimeAudio.Shared;
        if (audio.Play(hour, pet.Options.HourlyChimeVolume, preview, error =>
            { if (error is not null) pet.Window.Announce(error); }))
        {
            if (!preview) _announcingPetId = pet.Id;
            pet.Behavior.Trigger(PetTrigger.Wave, Environment.TickCount64);
            pet.Window.Announce(PetHourlyChime.GetLine(hour));
        }
        else pet.Window.Announce(audio.LastError ?? "语音暂不可用。");
    }
    private void CheckHourlyChime()
    {
        var audio = PetChimeAudio.Shared;
        if (_announcingPetId is { } id && (!_residents.TryGetValue(id, out var speaking) || !CanChime(speaking)))
        {
            audio.StopAutomatic();
            if (_residents.TryGetValue(id, out speaking)) speaking.Window.ClearAnnouncement();
            _announcingPetId = null;
        }
        var now = DateTimeOffset.Now;
        var pet = _residents.Values.FirstOrDefault(CanChime);
        if (_chimeSchedule.TryTake(now, pet is not null && !audio.IsPlaying)) PlayChime(pet!, now.Hour, false);
    }
    private void Tick()
    {
        if (_residents.Count == 0) return;
        CheckHourlyChime();
        var pending = _input.Take(); var now = Environment.TickCount64;
        var sample = now - _lastSample >= 500;
        if (sample) { _idle = DesktopPetMonitor.IdleSeconds(); _lastSample = now; }
        if (pending != PetInputActivity.None) _idle = 0;
        var hasCursor = DesktopPetMonitor.TryCursor(out var cursor);
        var animate = _services.Settings.Appearance.AnimationsEnabled; var interval = 250;
        foreach (var pet in _residents.Values)
        {
            var options = pet.Options; var window = pet.Window; var behavior = pet.Behavior;
            if (_fullscreen && options.HideInFullscreen) { if (window.IsVisible) window.Hide(); continue; }
            if (window.EnsureVisible()) behavior.Trigger(PetTrigger.Wave, now);
            if (pending.HasFlag(PetInputActivity.ShowDesktop) && options.ReactToDesktop) ApplyTrigger(pet, PetTrigger.ShowDesktop, now);
            else if (pending.HasFlag(PetInputActivity.AppClosed) && options.ReactToAppClose) ApplyTrigger(pet, PetTrigger.AppClosed, now);
            else if (pending.HasFlag(PetInputActivity.Enter) && options.ReactToEnter) ApplyTrigger(pet, PetTrigger.Enter, now);
            else if (pending.HasFlag(PetInputActivity.Typing) && options.ReactToTyping) ApplyTrigger(pet, PetTrigger.Typing, now);
            if (sample) { window.ClampToScreen(); window.EnsureTopmost(); }
            var direction = hasCursor ? window.CursorDirection(cursor) : new Point();
            if (window.PollHover(hasCursor ? cursor : null, now)) ApplyTrigger(pet, PetTrigger.Hover, now);
            behavior.Tick(now, _idle, options.SleepWhenIdle, options.IdleSeconds, options.FollowCursor, direction.X, direction.Y, options.ReactToTyping);
            window.Render(behavior, now, animate, options.ShowBubbles);
            var ownInterval = animate && window.Sprite.HasNativeAnimation ? 40 : !animate || behavior.IsSleeping ? 200 : behavior.IsTyping ? 130 : behavior.Pose is PetPose.Idle or PetPose.Blink or PetPose.LookLeft or PetPose.LookRight or PetPose.LookUp or PetPose.LookDown ? 100 : 40;
            interval = Math.Min(interval, ownInterval);
        }
        var next = TimeSpan.FromMilliseconds(interval); if (_timer.Interval != next) _timer.Interval = next;
    }
    private async Task SaveAsync(Func<Task> save)
    {
        try { await save(); } catch (Exception ex) { SaveFailed?.Invoke("桌宠设置未保存：" + ex.Message); }
    }
    private void Stop()
    {
        _chimeSchedule.ResetObservation(); PetChimeAudio.Shared.StopAutomatic(); _announcingPetId = null;
        _timer.Stop(); _monitor?.Dispose(); _monitor = null; _input.Take();
        foreach (var pet in _residents.Values) pet.Window.Close(); _residents.Clear();
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }
}
