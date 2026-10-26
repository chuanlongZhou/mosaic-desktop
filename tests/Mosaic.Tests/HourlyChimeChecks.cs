using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mosaic.Core;
using Mosaic.Core.Models;
using Mosaic.Host.Controls;
using Mosaic.Host.Services;
using Mosaic.Storage.Json;

internal static class HourlyChimeChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static DateTimeOffset At(int day, int hour, int minute = 0, int second = 0) => new(2026, 10, day, hour, minute, second, TimeSpan.FromHours(1));
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        CheckSchedule(); await CheckSettings(); CheckClips();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Mosaic;component/Themes/Dark.xaml", UriKind.Relative) });
                CheckPanel(output); CheckMedia(); PetChimeAudio.Shared.Stop(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await completion.Task;
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
        {
            passed = true, clips = 24, voice = "Feiyu / CosyVoice3 cute F6, offline synthesized clips",
            checks = new[] { "hour boundary and duplicate suppression", "midnight", "disabled and hidden hours are skipped", "resume does not catch up", "clock rollback and timezone change", "settings migration, restart and independent companion copies", "DeepSeek Q-only eligibility and fullscreen policy", "24 PCM clips, non-silent audio and text catalog", "WPF media decoder", "settings draft isolation, character gating and render" }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS hourly chime: scheduler, settings, 24 clips, WPF decoder and settings panel.");
    }
    private static void CheckSchedule()
    {
        var s = new PetHourlyChimeSchedule();
        Require(!s.TryTake(At(26, 9, 59, 59), true), "Startup chimed.");
        Require(s.TryTake(At(26, 10), true), "Hour boundary missed.");
        Require(!s.TryTake(At(26, 10, 0, 1), true), "Duplicate hour chimed.");
        s.TryTake(At(26, 10, 59, 59), true); Require(s.TryTake(At(26, 11), true), "Next hour missed.");
        s = new(); s.TryTake(At(26, 23, 59, 59), true); Require(s.TryTake(At(27, 0), true), "Midnight missed.");
        s = new(); s.TryTake(At(26, 9, 59, 59), true); Require(!s.TryTake(At(26, 10), false), "Disabled/hidden pet chimed.");
        Require(!s.TryTake(At(26, 10, 0, 1), true), "Enabling after boundary caught up.");
        s = new(); s.TryTake(At(26, 9, 50), true); Require(!s.TryTake(At(26, 10, 0, 1), true), "Resume caught up.");
        s = new(); s.TryTake(At(26, 9, 59, 59), true); Require(s.TryTake(At(26, 10, 0, 20), true), "Short dispatcher delay missed hour.");
        s = new(); s.TryTake(At(26, 9, 59, 59), true); Require(!s.TryTake(At(26, 10, 0, 30), true), "Late boundary chimed.");
        s = new(); s.TryTake(At(26, 9, 59, 59), true); s.TryTake(At(26, 10), true);
        Require(!s.TryTake(At(26, 9, 59, 59), true) && !s.TryTake(At(26, 10), true), "Clock rollback repeated an hour.");
        s = new(); s.TryTake(At(26, 9, 59, 59), true);
        Require(!s.TryTake(new DateTimeOffset(2026, 10, 26, 11, 0, 0, TimeSpan.FromHours(2)), true), "Timezone change chimed.");
        s = new(); Require(!s.TryTake(At(26, 10), true), "Launch at hour chimed.");
        s = new(); s.TryTake(At(26, 9, 59, 59), true); s.ResetObservation();
        Require(!s.TryTake(At(26, 10, 0, 1), true), "Short suspend/resume caught up.");
        s.TryTake(At(26, 10, 59, 59), true); Require(s.TryTake(At(26, 11), true), "Resume suppressed future hours.");
        var options = new DesktopPetOptions { Character = PetCharacter.DeepSeekChibi, HourlyChimeEnabled = true };
        Require(PetHourlyChime.CanAnnounce(options, true, false), "DeepSeek Q not eligible.");
        Require(!PetHourlyChime.CanAnnounce(options, false, false) && !PetHourlyChime.CanAnnounce(options, true, true), "Hidden/fullscreen pet eligible.");
        options.HideInFullscreen = false; Require(PetHourlyChime.CanAnnounce(options, true, true), "Fullscreen preference ignored.");
        options.HourlyChimeVolume = 0; Require(!PetHourlyChime.CanAnnounce(options, true, false), "Muted pet eligible.");
        options.HourlyChimeVolume = 70;
        foreach (var character in Enum.GetValues<PetCharacter>())
        { options.Character = character; Require(PetHourlyChime.CanAnnounce(options, true, false) == (character is PetCharacter.DeepSeekChibi or PetCharacter.DeepSeekChibiMk2), "Other character voiced."); }
    }
    private static async Task CheckSettings()
    {
        var legacy = JsonSerializer.Deserialize("{\"pet\":{\"enabled\":true}}", MosaicJsonContext.Configured.AppSettings)!;
        legacy.Validate(); Require(!legacy.Pet.HourlyChimeEnabled && legacy.Pet.HourlyChimeVolume == 70, "Legacy defaults changed.");
        var root = Path.Combine(Environment.CurrentDirectory, ".artifacts", "hourly-chime-settings", Guid.NewGuid().ToString("N"));
        var services = new AppServices(new JsonDocumentStore(root)); await services.InitializeAsync();
        await services.UpdatePetAsync(p =>
        {
            p.Character = PetCharacter.DeepSeekChibi; p.HourlyChimeEnabled = true; p.HourlyChimeVolume = 45;
            p.Companions.Add(new() { Character = PetCharacter.DeepSeekChibi, HourlyChimeEnabled = true, HourlyChimeVolume = 30 });
        });
        var restarted = new AppServices(new JsonDocumentStore(root)); await restarted.InitializeAsync();
        Require(restarted.Settings.Pet is { HourlyChimeEnabled: true, HourlyChimeVolume: 45 } && restarted.Settings.Pet.Companions[0].HourlyChimeVolume == 30, "Chime settings did not survive restart.");
        var copy = restarted.Settings.Pet.Copy(); copy.HourlyChimeVolume = 15; copy.Companions[0].HourlyChimeEnabled = false;
        Require(restarted.Settings.Pet.HourlyChimeVolume == 45 && restarted.Settings.Pet.Companions[0].HourlyChimeEnabled, "Chime copies share mutable state.");
        foreach (var invalid in new[] { -1, 101 })
        {
            try { new DesktopPetOptions { HourlyChimeVolume = invalid }.Validate(); throw new Exception("Invalid volume accepted."); }
            catch (InvalidDataException) { }
        }
    }
    private static void CheckClips()
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(PetChimeAudio.AssetPath(0))!, "lines.json")));
        Require(manifest.RootElement.GetArrayLength() == 24, "Wrong clip count.");
        for (var hour = 0; hour < 24; hour++)
        {
            var entry = manifest.RootElement[hour];
            Require(entry.GetProperty("hour").GetInt32() == hour && entry.GetProperty("text").GetString() == PetHourlyChime.GetLine(hour), "Audio/script text mismatch.");
            Require(entry.GetProperty("voice").GetString() == "Feiyu / CosyVoice3 cute F6" && entry.GetProperty("style").GetString() == "cute", "Wrong voice or style.");
            Require(entry.GetProperty("checkpoint_sha256").GetString() == "31a87a0f83ae9987285e115dd0d2048a0f52db44fa40d48e8da1cfefea12affc", "Wrong Feiyu checkpoint.");
            Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(PetChimeAudio.AssetPath(hour)))).Equals(entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Audio differs from its Feiyu generation receipt.");
            using var stream = File.OpenRead(PetChimeAudio.AssetPath(hour)); using var reader = new BinaryReader(stream);
            Require(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "RIFF", "Invalid WAV container."); reader.ReadInt32();
            Require(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "WAVE", "Invalid WAV format.");
            var pcm = false; var signal = false;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunk = Encoding.ASCII.GetString(reader.ReadBytes(4)); var length = reader.ReadInt32(); var next = stream.Position + length + (length & 1);
                Require(length >= 0 && next <= stream.Length, "Truncated audio.");
                if (chunk == "fmt ")
                {
                    pcm = reader.ReadInt16() == 1 && reader.ReadInt16() == 1 && reader.ReadInt32() == 22050;
                    reader.ReadInt32(); reader.ReadInt16(); pcm &= reader.ReadInt16() == 16;
                }
                if (chunk == "data")
                {
                    Require(length > 44100 * 2 && length < 44100 * 25, "Unexpected clip duration.");
                    for (var sample = 0; sample < length / 2; sample++) if (Math.Abs((int)reader.ReadInt16()) > 500) signal = true;
                }
                stream.Position = next;
            }
            Require(pcm && signal, "Audio is silent or not 16-bit mono PCM.");
        }
    }
    private static void CheckPanel(string output)
    {
        var saved = new DesktopPetSettings { Character = PetCharacter.DeepSeekChibi, HourlyChimeEnabled = true, HourlyChimeVolume = 45 };
        var panel = new DesktopPetSettingsPanel(); panel.Load(saved);
        Require(((StackPanel)panel.FindName("HourlyChimePanel")).Visibility == Visibility.Visible, "DeepSeek chime controls hidden.");
        ((Slider)panel.FindName("ChimeVolumeSlider")).Value = 25;
        var candidate = saved.Copy(); panel.ApplyTo(candidate);
        Require(candidate.HourlyChimeVolume == 25 && candidate.HourlyChimeEnabled && saved.HourlyChimeVolume == 45, "Settings draft isolation failed.");
        ((ComboBox)panel.FindName("CharacterPicker")).SelectedValue = PetCharacter.Gpt;
        Require(((StackPanel)panel.FindName("HourlyChimePanel")).Visibility == Visibility.Collapsed, "Other character has chime controls.");
        ((ComboBox)panel.FindName("CharacterPicker")).SelectedValue = PetCharacter.DeepSeekChibi;
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(24, 35, 49)), Padding = new Thickness(24), Child = panel };
        border.Measure(new Size(760, double.PositiveInfinity)); border.Arrange(new Rect(new Point(), border.DesiredSize)); border.UpdateLayout();
        var bitmap = new RenderTargetBitmap(760, (int)Math.Ceiling(border.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(border);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, "settings-preview.png")); encoder.Save(file);
    }
    private static void CheckMedia()
    {
        var media = new MediaPlayer { Volume = 0 }; var frame = new DispatcherFrame(); var opened = false; string? error = null;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        media.MediaOpened += (_, _) => { opened = media.NaturalDuration.HasTimeSpan && media.NaturalDuration.TimeSpan.TotalSeconds > 2; frame.Continue = false; };
        media.MediaFailed += (_, e) => { error = e.ErrorException.Message; frame.Continue = false; };
        timeout.Tick += (_, _) => { error = "Decoder timeout"; frame.Continue = false; };
        timeout.Start(); media.Open(new Uri(PetChimeAudio.AssetPath(12))); Dispatcher.PushFrame(frame); timeout.Stop(); media.Close();
        Require(opened && error is null, "WPF audio decoder failed: " + error);
    }
}
