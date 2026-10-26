using System.IO;
using System.Windows.Media;
using Mosaic.Core;

namespace Mosaic.Host.Services;

/// <summary>One shared player prevents desktop and preview voices from overlapping.</summary>
public sealed class PetChimeAudio
{
    private static PetChimeAudio? _shared;
    public static PetChimeAudio Shared => _shared ??= new();
    private readonly MediaPlayer _player = new();
    private Action<string?>? _completed;
    public bool IsPlaying { get; private set; }
    public bool IsPreview { get; private set; }
    public string? LastError { get; private set; }

    private PetChimeAudio()
    {
        _player.MediaEnded += (_, _) => Finish(null);
        _player.MediaFailed += (_, e) => Finish("语音播放失败，请检查音频输出设备：" + e.ErrorException.Message);
    }
    public static string AssetPath(int hour)
    {
        _ = PetHourlyChime.GetLine(hour);
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Voice", "DeepSeekChibi", $"hour-{hour:00}.wav");
    }
    public bool Play(int hour, int volume, bool preview, Action<string?>? completed = null)
    {
        Stop(); LastError = null;
        if (volume is <= 0 or > 100) { LastError = "请将报时音量设为 1–100 后试听。"; return false; }
        var file = AssetPath(hour);
        if (!File.Exists(file)) { LastError = "缺少报时配音文件，请重新解压完整运行包。"; return false; }
        try
        {
            _completed = completed; IsPreview = preview; IsPlaying = true;
            _player.Volume = volume / 100d;
            _player.Open(new Uri(file)); _player.Play();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { LastError = "语音暂不可用：" + ex.Message; Stop(); return false; }
    }
    private void Finish(string? error)
    {
        var completed = _completed; LastError = error; Stop(); completed?.Invoke(error);
    }
    public void StopAutomatic() { if (!IsPreview) Stop(); }
    public void StopPreview() { if (IsPreview) Stop(); }
    public void Stop() { _completed = null; _player.Close(); IsPlaying = false; IsPreview = false; }
}
