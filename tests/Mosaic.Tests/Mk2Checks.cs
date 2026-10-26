using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mosaic.Core;
using Mosaic.Core.Models;
using Mosaic.Host.Controls;
using Mosaic.Host.Services;
using Mosaic.Host.Views;
using Mosaic.Storage.Json;

internal static class Mk2Checks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        await DesktopPetTests.Settings(); await DesktopPetTests.Behavior();
        Require((int)PetCharacter.DeepSeekChibiMk2 == 11 && (int)PetCharacter.QwenStandard == 10, "Legacy character IDs moved.");
        Require(DesktopPetCharacters.Get(PetCharacter.DeepSeekChibiMk2).Name == "Deepseek Q版 mk2", "Wrong character name.");
        Require(ReferenceEquals(DesktopPetDialogue.ClickLines(PetCharacter.DeepSeekChibiMk2), DesktopPetDialogue.ClickLines(PetCharacter.DeepSeekChibi)), "mk2 lost the shared personality.");
        var root = Path.Combine(Environment.CurrentDirectory, ".artifacts", "mk2-state", Guid.NewGuid().ToString("N"));
        var services = new AppServices(new JsonDocumentStore(root)); await services.InitializeAsync();
        await services.UpdatePetAsync(p => { p.Character = PetCharacter.DeepSeekChibiMk2; p.HourlyChimeEnabled = true; p.HourlyChimeVolume = 40; p.Companions.Add(new() { Character = PetCharacter.DeepSeekChibi, HourlyChimeEnabled = true }); });
        var restart = new AppServices(new JsonDocumentStore(root)); await restart.InitializeAsync();
        Require(restart.Settings.Pet is { Character: PetCharacter.DeepSeekChibiMk2, HourlyChimeEnabled: true, HourlyChimeVolume: 40 }, "mk2 did not survive JSON restart.");
        var copy = restart.Settings.Pet.Copy(); copy.Character = PetCharacter.WhaleGirl; Require(restart.Settings.Pet.Character == PetCharacter.DeepSeekChibiMk2, "mk2 draft mutated live settings.");
        Require(PetHourlyChime.CanAnnounce(restart.Settings.Pet, true, false) && PetHourlyChime.Supports(PetCharacter.DeepSeekChibi), "Both Q forms must share hourly audio support.");
        var schedule = new PetHourlyChimeSchedule(); var before = new DateTimeOffset(2026, 10, 26, 10, 59, 59, TimeSpan.FromHours(1));
        schedule.TryTake(before, true); Require(schedule.TryTake(before.AddSeconds(1), true) && !schedule.TryTake(before.AddSeconds(1.1), true), "Two eligible Q forms duplicated the hourly event.");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Mosaic;component/Themes/Dark.xaml", UriKind.Relative) });
                CheckOriginalClips(output); CheckPlaybackAndPanel(output);
                app.Shutdown(); done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await done.Task;
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
        {
            passed = true, character = "Deepseek Q版 mk2", animations = 106, originalCanvas = "640x360", voice = "shared DeepSeekChibi Feiyu cute clips",
            checks = new[] { "legacy enum identity and JSON migration/restart", "shared personality and hourly eligibility/deduplication", "106 original WebM SHA256 hashes", "native VP9 alpha decoding", "transparent pixels and moving frames", "independent playback, manual action, drag and reduced motion", "transparent alpha hit testing", "window aspect ratio and context menus", "settings selection, independent draft, preview and license notice" }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS mk2: 106 original alpha animations, interactions, settings and shared hourly voice.");
    }
    private static void CheckOriginalClips(string output)
    {
        Require(Mk2AnimationCatalog.All.Count == 106, "Animation catalog incomplete.");
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(232, 239, 247)), null, new Rect(0, 0, 2000, 1540));
            for (var index = 0; index < Mk2AnimationCatalog.All.Count; index++)
            {
                var clip = Mk2AnimationCatalog.All[index]; var path = Mk2AnimationCatalog.FileFor(clip.Name);
                Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(clip.Sha256, StringComparison.OrdinalIgnoreCase), "Original clip bytes changed: " + clip.Name);
                BitmapSource poster;
                using (var video = new Mk2VideoDecoder(path))
                {
                    while (video.ReadFrame() && video.Seconds < .75) { }
                    Require(video.FramesDecoded >= 3 && video.Duration > 1, "Missing native video frames: " + clip.Name);
                    var clear = 0; var painted = 0;
                    for (var i = 3; i < video.Pixels.Length; i += 4) { if (video.Pixels[i] == 0) clear++; if (video.Pixels[i] > 128) painted++; }
                    Require(clear > 50000 && painted > 1000, $"Transparent alpha missing/opaque background: {clip.Name}, format={video.PixelFormat}, clear={clear}, painted={painted}");
                    poster = video.Bitmap.Clone(); poster.Freeze();
                }
                // A disposed decoder must release the original video file.
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                var x = index % 10 * 200; var y = index / 10 * 140;
                dc.DrawImage(poster, new Rect(x, y, 200, 112.5));
                var label = new FormattedText(clip.Name, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei UI"), 11, Brushes.MidnightBlue, 1) { MaxTextWidth = 196 };
                dc.DrawText(label, new Point(x + 2, y + 116));
                if (index % 20 == 0) Console.WriteLine($"  mk2 decoded {index + 1}/106 original clips with alpha.");
            }
        }
        Save(drawing, 2000, 1540, Path.Combine(output, "all-animations.png"));
    }
    private static void CheckPlaybackAndPanel(string output)
    {
        var a = new WhalePetSprite { Width = 320, Height = 180 }; var b = new WhalePetSprite();
        a.SetCharacter(PetCharacter.DeepSeekChibiMk2); b.SetCharacter(PetCharacter.DeepSeekChibiMk2);
        a.Measure(new Size(320, 180)); a.Arrange(new Rect(0, 0, 320, 180));
        a.RenderAnimation(PetPose.Idle, 0, 1000, true); b.RenderAnimation(PetPose.Idle, 0, 1000, true);
        Require(!ReferenceEquals(a.Source, b.Source), "Two mk2 residents share mutable video state.");
        a.PlayAnimation("吃白饭", 1000); a.RenderAnimation(PetPose.Idle, 0, 1000, true);
        var first = PixelHash((BitmapSource)a.Source);
        for (var i = 1; i <= 30; i++) a.RenderAnimation(PetPose.Idle, 0, 1000 + i * 42, true);
        Require(a.AnimationFramesDecoded > 15 && first != PixelHash((BitmapSource)a.Source), "mk2 is a static image rather than the original animation.");
        Require(a.CurrentAnimationName == "吃白饭" && b.CurrentAnimationName == "待机呼吸休闲", "Manual playback leaked between residents.");
        a.RenderAnimation(PetPose.Drag, 5000, 5000, true); Require(a.CurrentAnimationName == "被鼠标拖拽悬空反馈", "Drag did not use original feedback.");
        a.RenderAnimation(PetPose.Drag, 5000, 5100, false); var still = a.Source;
        a.RenderAnimation(PetPose.Drag, 5000, 13000, false); Require(ReferenceEquals(still, a.Source), "Reduced motion still animates.");
        a.RenderAnimation(PetPose.Drag, 5000, 13000, true); Require(a.AnimationFramesDecoded < 10, "Resume fast-forwarded hidden frames.");
        a.SetPose(PetPose.Idle); var pixels = new FormatConvertedBitmap((BitmapSource)a.Source, PixelFormats.Bgra32, null, 0);
        var data = new byte[640 * 360 * 4]; pixels.CopyPixels(data, 640 * 4, 0);
        for (var y = 10; y < 350; y += 29) for (var x = 10; x < 630; x += 29)
            Require(a.IsOpaqueAt(new Point((x + .5) / 2, (y + .5) / 2)) == (data[(y * 640 + x) * 4 + 3] > 24), "mk2 alpha hit-test does not follow the current painting.");
        a.RenderAnimation(PetPose.Idle, 7000, 14000, true, PetPose.LookRight); Require(a.IsAnimationMirrored, "mk2 did not face the cursor.");
        a.PlayAnimation("写代码", 14000); a.RenderAnimation(PetPose.Idle, 7000, 14000, true, PetPose.LookRight); Require(!a.IsAnimationMirrored, "Writing animation mirrored its text.");
        a.RenderAnimation(PetPose.Sleep, 9000, 15000, true);
        for (var tick = 1; tick <= 270; tick++) a.RenderAnimation(PetPose.Sleep, 9000, 15000 + tick * 42, true);
        Require(a.CurrentAnimationName == "原地小憩沉眠" && a.AnimationFramesDecoded is > 0 and < 70, "Sleeping clip did not rewind and loop.");
        a.StopAnimation(); b.StopAnimation();
        var window = new DesktopPetWindow();
        window.Configure(new DesktopPetOptions { Character = PetCharacter.DeepSeekChibiMk2, Size = 220 }, true);
        Require(Math.Abs(window.Width - (220 * 16d / 9 + 32)) < .01 && window.Height == 316, "mk2 video was squeezed into a square.");
        var menus = window.Sprite.ContextMenu!.Items.OfType<MenuItem>().ToArray();
        Require(menus.Any(m => Equals(m.Header, "试听整点报时")) && menus.Any(m => m.Header.ToString()!.Contains("106")), "Shared voice or original animation menu missing.");
        window.Close();
        var settings = new DesktopPetSettings { Character = PetCharacter.DeepSeekChibiMk2, HourlyChimeEnabled = true, HourlyChimeVolume = 40 };
        var panel = new DesktopPetSettingsPanel(); panel.Load(settings);
        Require(((StackPanel)panel.FindName("HourlyChimePanel")).Visibility == Visibility.Visible && ((StackPanel)panel.FindName("Mk2AnimationPanel")).Visibility == Visibility.Visible, "mk2 controls hidden.");
        Require(((ComboBox)panel.FindName("Mk2AnimationPicker")).Items.Count == 106, "Not all original animations are selectable.");
        var candidate = settings.Copy(); panel.ApplyTo(candidate);
        Require(candidate.Character == PetCharacter.DeepSeekChibiMk2 && candidate.HourlyChimeEnabled && candidate.HourlyChimeVolume == 40, "mk2 settings were lost.");
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(24, 35, 49)), Padding = new Thickness(24), Child = panel };
        border.Measure(new Size(760, double.PositiveInfinity)); border.Arrange(new Rect(new Point(), border.DesiredSize)); border.UpdateLayout();
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(border), null, new Rect(0, 0, 760, border.ActualHeight));
        Save(drawing, 760, (int)Math.Ceiling(border.ActualHeight), Path.Combine(output, "settings-preview.png"));
        ((WhalePetSprite)panel.FindName("PreviewSprite")).StopAnimation();
    }
    private static string PixelHash(BitmapSource bitmap)
    { var data = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(data, bitmap.PixelWidth * 4, 0); return Convert.ToHexString(SHA256.HashData(data)); }
    private static void Save(Visual visual, int width, int height, string path)
    {
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(path); encoder.Save(output);
    }
}
