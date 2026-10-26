using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFmpeg.AutoGen;

namespace Mosaic.Host.Controls;

/// <summary>Owns one original VP9-alpha stream and one reusable WPF bitmap.</summary>
public sealed unsafe class Mk2VideoDecoder : IDisposable
{
    private AVFormatContext* _format;
    private AVCodecContext* _codec;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private SwsContext* _conversion;
    private int _stream;
    private bool _draining, _disposed;
    public const int Width = 640, Height = 360;
    public byte[] Pixels { get; } = new byte[Width * Height * 4];
    public WriteableBitmap Bitmap { get; } = new(Width, Height, 96, 96, PixelFormats.Bgra32, null);
    public int FramesDecoded { get; private set; }
    public double Seconds { get; private set; }
    public double Duration { get; private set; }
    public string PixelFormat { get; private set; } = "";

    public Mk2VideoDecoder(string file)
    {
        ffmpeg.RootPath = AppContext.BaseDirectory;
        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
        try
        {
            var format = ffmpeg.avformat_alloc_context();
            Check(ffmpeg.avformat_open_input(&format, file, null, null)); _format = format;
            Check(ffmpeg.avformat_find_stream_info(_format, null));
            _stream = ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0); Check(_stream);
            // The default FFmpeg VP9 decoder drops alpha. Select libvpx explicitly.
            var codec = ffmpeg.avcodec_find_decoder_by_name("libvpx-vp9");
            if (codec == null) throw new InvalidDataException("mk2 透明动画需要 libvpx-vp9 解码器。");
            _codec = ffmpeg.avcodec_alloc_context3(codec);
            if (_codec == null) throw new OutOfMemoryException();
            Check(ffmpeg.avcodec_parameters_to_context(_codec, _format->streams[_stream]->codecpar));
            _codec->thread_count = 1; Check(ffmpeg.avcodec_open2(_codec, codec, null));
            _frame = ffmpeg.av_frame_alloc(); _packet = ffmpeg.av_packet_alloc();
            if (_frame == null || _packet == null) throw new OutOfMemoryException();
            Duration = _format->duration > 0 ? _format->duration / 1000000d : 10;
        }
        catch { Dispose(); throw; }
    }
    private static void Check(int error)
    { if (error < 0) throw new InvalidDataException($"mk2 动画解码失败（FFmpeg {error}）。"); }

    public bool ReadFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            var result = ffmpeg.avcodec_receive_frame(_codec, _frame);
            if (result >= 0)
            {
                try { ConvertFrame(); FramesDecoded++; return true; }
                finally { ffmpeg.av_frame_unref(_frame); }
            }
            if (result == ffmpeg.AVERROR_EOF) return false;
            if (result != ffmpeg.AVERROR(ffmpeg.EAGAIN)) Check(result);
            if (_draining) return false;
            while (true)
            {
                result = ffmpeg.av_read_frame(_format, _packet);
                if (result == ffmpeg.AVERROR_EOF)
                { _draining = true; Check(ffmpeg.avcodec_send_packet(_codec, null)); break; }
                Check(result);
                try
                {
                    if (_packet->stream_index != _stream) continue;
                    Check(ffmpeg.avcodec_send_packet(_codec, _packet)); break;
                }
                finally { ffmpeg.av_packet_unref(_packet); }
            }
        }
    }
    private void ConvertFrame()
    {
        PixelFormat = ((AVPixelFormat)_frame->format).ToString();
        if (_frame->width != Width || _frame->height != Height) throw new InvalidDataException("mk2 动画画布必须为 640×360。");
        if (_conversion == null)
        {
            _conversion = ffmpeg.sws_getContext(Width, Height, (AVPixelFormat)_frame->format,
                Width, Height, AVPixelFormat.AV_PIX_FMT_BGRA, 2, null, null, null);
            if (_conversion == null) throw new InvalidDataException("无法转换 mk2 透明画面。");
        }
        var input = new byte*[4]; var lines = new int[4];
        for (var i = 0; i < 4; i++) { input[i] = _frame->data[(uint)i]; lines[i] = _frame->linesize[(uint)i]; }
        fixed (byte* target = Pixels)
        {
            var output = new byte*[4]; output[0] = target;
            var stride = new[] { Width * 4, 0, 0, 0 };
            Check(ffmpeg.sws_scale(_conversion, input, lines, 0, Height, output, stride));
        }
        // VP9 may quantize fully transparent background to alpha 1 or 2.
        // Remove that compression haze while retaining the character's soft edges.
        for (var i = 3; i < Pixels.Length; i += 4) if (Pixels[i] <= 2) Pixels[i] = 0;
        Bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), Pixels, Width * 4, 0);
        Seconds = _frame->pts == ffmpeg.AV_NOPTS_VALUE ? FramesDecoded / 24d : _frame->pts * ffmpeg.av_q2d(_format->streams[_stream]->time_base);
    }
    public void Rewind()
    {
        Check(ffmpeg.av_seek_frame(_format, _stream, 0, ffmpeg.AVSEEK_FLAG_BACKWARD));
        ffmpeg.avcodec_flush_buffers(_codec); _draining = false; FramesDecoded = 0; Seconds = 0;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_conversion != null) { ffmpeg.sws_freeContext(_conversion); _conversion = null; }
        var frame = _frame; if (frame != null) ffmpeg.av_frame_free(&frame); _frame = null;
        var packet = _packet; if (packet != null) ffmpeg.av_packet_free(&packet); _packet = null;
        var codec = _codec; if (codec != null) ffmpeg.avcodec_free_context(&codec); _codec = null;
        var format = _format; if (format != null) ffmpeg.avformat_close_input(&format); _format = null;
    }
}
