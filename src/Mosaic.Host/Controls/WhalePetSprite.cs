using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using Mosaic.Core;
using Mosaic.Core.Models;

namespace Mosaic.Host.Controls;

public sealed class WhalePetSprite : Image
{
    private sealed class SpriteAtlas
    {
        public BitmapSource[] Frames { get; }
        public SpriteAtlas(PetCharacter character) => Frames = Load(character);
        private readonly Dictionary<PetPose, byte[]> _alpha = [];
        public byte[] Alpha(PetPose pose)
        {
            if (_alpha.TryGetValue(pose, out var alpha)) return alpha;
            var frame = Frames[(int)pose];
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
            alpha = new byte[converted.PixelWidth * converted.PixelHeight];
            for (var i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
            _alpha.Add(pose, alpha);
            return alpha;
        }
    }
    private static readonly IReadOnlyDictionary<PetCharacter, Lazy<SpriteAtlas>> Atlases =
        Enum.GetValues<PetCharacter>().ToDictionary(character => character, character => new Lazy<SpriteAtlas>(() => new(character)));
    private SpriteAtlas Atlas => Atlases[Character].Value;
    private PetPose _pose;
    public PetCharacter Character { get; private set; }
    public WhalePetSprite()
    {
        Source = Atlas.Frames[0]; Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }
    public void SetCharacter(PetCharacter character)
    {
        if (Character == character) return;
        // Load before committing so a broken resource cannot leave a half-switched sprite.
        var atlas = Atlases[character].Value;
        Character = character; Source = atlas.Frames[(int)_pose];
    }
    public void SetPose(PetPose pose) { if (_pose == pose && Source is not null) return; _pose = pose; Source = Atlas.Frames[(int)pose]; }
    public bool IsOpaqueAt(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight) return false;
        var frame = (BitmapSource)Source;
        var scale = Math.Min(ActualWidth / frame.PixelWidth, ActualHeight / frame.PixelHeight);
        var x = (point.X - (ActualWidth - frame.PixelWidth * scale) / 2) / scale;
        var y = (point.Y - (ActualHeight - frame.PixelHeight * scale) / 2) / scale;
        if (x < 0 || y < 0 || x >= frame.PixelWidth || y >= frame.PixelHeight) return false;
        return Atlas.Alpha(_pose)[(int)y * frame.PixelWidth + (int)x] > 24;
    }
    private static BitmapSource[] Load(PetCharacter character)
    {
        const string root = "pack://application:,,,/Mosaic;component/Assets/Pet/";
        var manifestPath = character switch
        {
            PetCharacter.DeepSeekChibi => "DeepSeekChibi/frames.json",
            PetCharacter.DeepSeekStandard => "DeepSeekStandard/frames.json",
            _ => "frames-v2.json"
        };
        var images = new Dictionary<string, BitmapImage>();
        var frames = new Dictionary<PetPose, BitmapSource>();
        var normalized = new Dictionary<(string File, int X, int Y, int Width, int Height), BitmapSource>();
        var manifestPaths = new[] { manifestPath, character switch
        {
            PetCharacter.DeepSeekChibi => "DeepSeekChibi/motion-frames.json",
            PetCharacter.DeepSeekStandard => "DeepSeekStandard/motion-frames.json",
            _ => "whale-motion-frames.json"
        }, character switch
        {
            PetCharacter.DeepSeekChibi => "DeepSeekChibi/emotion-frames.json",
            PetCharacter.DeepSeekStandard => "DeepSeekStandard/emotion-frames.json",
            _ => "whale-emotion-frames.json"
        }, character switch
        {
            PetCharacter.DeepSeekChibi => "DeepSeekChibi/keyframes.json",
            PetCharacter.DeepSeekStandard => "DeepSeekStandard/keyframes.json",
            _ => "whale-keyframes.json"
        } };
        if (character is not (PetCharacter.WhaleGirl or PetCharacter.DeepSeekChibi or PetCharacter.DeepSeekStandard))
            manifestPaths = [character + "/frames.json"];
        foreach (var path in manifestPaths)
        {
          using var stream = Application.GetResourceStream(new Uri(root + path)).Stream;
          using var manifest = JsonDocument.Parse(stream);
          foreach (var item in manifest.RootElement.EnumerateArray())
          {
            // Keep the archived walking artwork intact, without loading removed poses.
            if (!Enum.TryParse<PetPose>(item.GetProperty("pose").GetString(), out var pose)) continue;
            var file = item.GetProperty("image").GetString()!;
            if (!images.TryGetValue(file, out var bitmap))
            { bitmap = new BitmapImage(new Uri(root + file)); bitmap.Freeze(); images.Add(file, bitmap); }
            var box = item.GetProperty("rect").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var key = (file, box[0], box[1], box[2], box[3]);
            if (normalized.TryGetValue(key, out var reused)) { frames.Add(pose, reused); continue; }
            var crop = new CroppedBitmap(bitmap, new Int32Rect(box[0], box[1], box[2], box[3])); crop.Freeze();
            // Explicit per-pose bounds avoid assuming that generated art is a perfect grid.
            // Render into a common transparent canvas with a fixed foot baseline and safe gutters.
            var scale = 448d / Math.Max(crop.PixelWidth, crop.PixelHeight);
            var width = crop.PixelWidth * scale; var height = crop.PixelHeight * scale;
            var left = (512 - width) / 2; var top = 480 - height;
            var drawing = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.HighQuality);
            using (var context = drawing.RenderOpen()) context.DrawImage(crop, new Rect(left, top, width, height));
            var frame = new RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32);
            frame.Render(drawing); frame.Freeze();
            frames.Add(pose, frame); normalized.Add(key, frame);
          }
        }
        // Falling-token keyframes reuse the exact existing idle painting. Only
        // the single token's discrete position changes; the character is not redrawn.
        using var tokenStream = Application.GetResourceStream(new Uri(root + "token-chip.json")).Stream;
        using var tokenManifest = JsonDocument.Parse(tokenStream);
        var rect = tokenManifest.RootElement.GetProperty("rect").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var token = new CroppedBitmap(new BitmapImage(new Uri(root + "token-chip.png")), new Int32Rect(rect[0], rect[1], rect[2], rect[3])); token.Freeze();
        foreach (var (pose, top) in new[] { (PetPose.TokenFallHigh, 18d), (PetPose.TokenFallLow, 64d) })
        {
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawImage(frames[PetPose.Idle], new Rect(0, 0, 512, 512));
                context.DrawImage(token, new Rect(232, top, 48, 48));
            }
            var frame = new RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32); frame.Render(drawing); frame.Freeze(); frames.Add(pose, frame);
        }
        // Existing Mosaic artwork keeps its own poses. New manual actions use
        // the closest original pose when that artwork has no dedicated frame.
        foreach (var (pose, fallback) in new[]
        {
            (PetPose.Think, PetPose.LookDown), (PetPose.Read, PetPose.TypingLeft),
            (PetPose.Search, PetPose.LookRight), (PetPose.Draw, PetPose.TypingRight),
            (PetPose.Stretch, PetPose.Wake), (PetPose.Run, PetPose.Cheer),
            (PetPose.Hum, PetPose.Wave)
        }) frames.TryAdd(pose, frames[fallback]);
        return Enum.GetValues<PetPose>().Select(pose => frames[pose]).ToArray();
    }
}
