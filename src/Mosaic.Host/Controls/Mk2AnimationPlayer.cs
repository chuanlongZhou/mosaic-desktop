using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Mosaic.Core;

namespace Mosaic.Host.Controls;

public sealed record Mk2AnimationClip(string Name, string File, string Sha256);
public static class Mk2AnimationCatalog
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Assets", "Pet", "DeepSeekChibiMk2");
    public static IReadOnlyList<Mk2AnimationClip> All { get; }
    public static IReadOnlyDictionary<string, string[]> Categories { get; }
    public static IReadOnlySet<string> NoMirror { get; }
    private static readonly Dictionary<string, BitmapSource> Posters = [];
    static Mk2AnimationCatalog()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "animations.json")));
        All = json.RootElement.GetProperty("clips").EnumerateArray().Select(c => new Mk2AnimationClip(
            c.GetProperty("name").GetString()!, c.GetProperty("file").GetString()!, c.GetProperty("sha256").GetString()!)).ToArray();
        if (All.Count != 106 || All.Select(c => c.Name).Distinct().Count() != All.Count) throw new InvalidDataException("mk2 动画清单不完整。");
        NoMirror = json.RootElement.GetProperty("noMirror").EnumerateArray().Select(a => a.GetString()!).ToHashSet(StringComparer.Ordinal);
        Categories = json.RootElement.GetProperty("categories").EnumerateArray().ToDictionary(
            c => c.GetProperty("id").GetString()!, c => c.GetProperty("actions").EnumerateArray().Select(a => a.GetString()!).ToArray());
    }
    public static string FileFor(string name)
    {
        var file = All.Single(c => c.Name == name).File;
        if (Path.GetFileName(file) != file || !file.EndsWith(".webm", StringComparison.Ordinal)) throw new InvalidDataException("mk2 动画文件名无效。");
        return Path.Combine(Root, file);
    }
    public static BitmapSource Poster(string name)
    {
        if (Posters.TryGetValue(name, out var poster)) return poster;
        using var video = new Mk2VideoDecoder(FileFor(name));
        while (video.ReadFrame() && video.Seconds < .75) { }
        poster = video.Bitmap.Clone(); poster.Freeze();
        if (Posters.Count >= 24) Posters.Remove(Posters.Keys.First());
        Posters.Add(name, poster); return poster;
    }
    public static string ForPose(PetPose pose) => pose switch
    {
        PetPose.LookLeft or PetPose.LookRight or PetPose.LookUp or PetPose.LookDown => "东张西望",
        PetPose.Pat => "点击回应-挠痒咯咯笑",
        PetPose.Cheer => "点击回应-开心跃动",
        PetPose.GreetA or PetPose.GreetB or PetPose.Wave => "点击回应-元气挥手",
        PetPose.Goodbye => "女仆屈膝礼仪",
        PetPose.Sleep => "原地小憩沉眠",
        PetPose.Wake => "超大伸懒腰",
        PetPose.Eat => "吃白饭",
        PetPose.Drag => "被鼠标拖拽悬空反馈",
        PetPose.Surprise => "被吓一跳",
        PetPose.Pout => "点击回应-傲娇生气",
        PetPose.TypingLeft or PetPose.TypingRight => "写代码",
        PetPose.RiceHat or PetPose.RiceHatTilt => "是啊，吃什么",
        PetPose.Tired or PetPose.TiredYawn => "哈欠连天",
        PetPose.BegTokens or PetPose.BegReach => "余额-袋空如洗",
        PetPose.TokenDazed or PetPose.TokenFallHigh or PetPose.TokenFallLow => "余额-钱袋满溢",
        _ => "待机呼吸休闲"
    };
}

internal sealed class Mk2AnimationPlayer
{
    private Mk2VideoDecoder? _video;
    private long _started, _lastTick, _observedStart = long.MinValue;
    private bool _looping, _manual, _paused;
    private string _name = "待机呼吸休闲";
    private int _ambientCount;
    private bool _facingRight;
    public bool Mirrored => _facingRight && !Mk2AnimationCatalog.NoMirror.Contains(_name);
    public string Name => _name;
    public byte[]? Pixels => _video?.Pixels;
    public BitmapSource? CurrentBitmap => _video?.Bitmap;
    public int FramesDecoded => _video?.FramesDecoded ?? 0;

    public void Play(string name, long now)
    { SetClip(name, now); _manual = true; _looping = false; }
    private void SetClip(string name, long now)
    {
        _video?.Dispose(); _video = null;
        _name = name; _started = now;
        _video = new Mk2VideoDecoder(Mk2AnimationCatalog.FileFor(name));
        if (!_video.ReadFrame()) throw new InvalidDataException("mk2 动画没有可播放帧。");
    }
    private void Ambient(long now)
    {
        // Insert a breathing loop between original ambient actions; never repeat an action consecutively.
        var next = "待机呼吸休闲";
        if (++_ambientCount % 2 == 0)
        {
            var actions = Mk2AnimationCatalog.Categories.Values.SelectMany(a => a).Distinct().Where(a => a != _name).ToArray();
            next = actions[Random.Shared.Next(actions.Length)];
        }
        SetClip(next, now); _manual = _looping = false;
    }
    public BitmapSource Render(PetPose action, long actionStarted, long now, bool animate, PetPose gaze)
    {
        if (gaze == PetPose.LookRight) _facingRight = true; else if (gaze == PetPose.LookLeft) _facingRight = false;
        var active = action is not (PetPose.Idle or PetPose.Blink or PetPose.LookLeft or PetPose.LookRight or PetPose.LookUp or PetPose.LookDown);
        if (actionStarted != _observedStart)
        {
            _observedStart = actionStarted;
            if (active && (!_manual || action is not (PetPose.TypingLeft or PetPose.TypingRight)))
            {
                _manual = false;
                _looping = action is PetPose.Sleep or PetPose.Drag or PetPose.TypingLeft or PetPose.TypingRight;
                SetClip(Mk2AnimationCatalog.ForPose(action), now);
            }
        }
        if (_looping && !active) { Ambient(now); }
        if (!animate)
        {
            _paused = true; _lastTick = now;
            return Mk2AnimationCatalog.Poster(_manual ? _name : Mk2AnimationCatalog.ForPose(action));
        }
        if (_video is null) SetClip(_name, now);
        if (_paused || _lastTick > 0 && now - _lastTick > 1500)
        { _started = now - (long)(_video!.Seconds * 1000); _paused = false; }
        _lastTick = now;
        var seconds = Math.Max(0, now - _started) / 1000d;
        if (seconds >= _video!.Duration)
        {
            if (_looping && active) { _video.Rewind(); _video.ReadFrame(); _started = now; }
            else Ambient(now);
            seconds = 0;
        }
        for (var count = 0; count < 4 && _video!.Seconds < seconds; count++)
        {
            if (!_video.ReadFrame())
            {
                if (_looping && active) { _video.Rewind(); _video.ReadFrame(); _started = now; }
                else Ambient(now);
                break;
            }
        }
        return _video!.Bitmap;
    }
    public void Stop() { _video?.Dispose(); _video = null; _lastTick = 0; }
}
