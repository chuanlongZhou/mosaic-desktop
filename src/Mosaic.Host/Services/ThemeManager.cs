using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Mosaic.Core.Models;

namespace Mosaic.Host.Services;

public static class ThemeManager
{
    private static AppServices? _services;
    private static bool _initialized;
    public static AppearanceSettings Current { get; private set; } = new();
    public static void Initialize(AppServices services)
    {
        _services = services;
        services.SettingsChanged += (_, _) => Apply(services.Settings.Appearance);
        if (!_initialized)
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) =>
            { if (ReferenceEquals(e.OriginalSource, sender) && sender is Window window) { ApplyWindow(window); UiEffects.Enter(window.Content as FrameworkElement); } }));
            _initialized = true;
        }
        Apply(services.Settings.Appearance);
    }
    public static void RestoreSaved() { if (_services is not null) Apply(_services.Settings.Appearance); }
    public static void Apply(AppearanceSettings settings)
    {
        settings.Validate(); Current = settings;
        UiEffects.Enabled = settings.AnimationsEnabled && SystemParameters.ClientAreaAnimation;
        var palette = settings.Theme switch
        {
            "Light" => new[] { "#F3F6FA", "#FFFFFF", "#E8EEF6", "#D2DCE8", "#172A40", "#52677F", "#EAF0F7", "#FFFFFF" },
            "Graphite" => new[] { "#16181D", "#22252C", "#30343E", "#424751", "#F3F4F7", "#B3BAC7", "#1A1D23", "#1C1F26" },
            "Forest" => new[] { "#101E1B", "#192C27", "#254037", "#35554A", "#EEF8F2", "#B1CABF", "#13231F", "#142720" },
            "Plum" => new[] { "#1C1727", "#2A2338", "#3C3050", "#544363", "#F6F0FC", "#C8B8D7", "#211B2E", "#241D33" },
            _ => new[] { "#101722", "#182331", "#223145", "#34485F", "#F3F6FA", "#B0C1D4", "#131E2B", "#111C29" }
        };
        var light = settings.Theme == "Light";
        var accent = settings.Accent switch
        {
            "Mint" => light ? "#15735B" : "#83DCC0", "Violet" => light ? "#7043AE" : "#C5A8FF",
            "Rose" => light ? "#AC3E6A" : "#F5AEC9", "Amber" => light ? "#926010" : "#F2CF8C",
            _ => light ? "#1764A7" : "#8BCCFF"
        };
        var background = ColorOf(palette[0]); var panel = ColorOf(palette[1]); var emphasis = ColorOf(accent);
        var glass = settings.TransparencyEnabled && !SystemParameters.HighContrast;
        SetBrush("BackgroundBrush", WithOpacity(background, glass ? settings.SurfaceOpacity : 1));
        SetBrush("PanelBrush", panel); SetBrush("RaisedBrush", ColorOf(palette[2])); SetBrush("BorderBrush", ColorOf(palette[3]));
        SetBrush("TextBrush", ColorOf(palette[4])); SetBrush("MutedBrush", ColorOf(palette[5]));
        SetBrush("SidebarBrush", WithOpacity(ColorOf(palette[6]), glass ? .88 : 1));
        SetBrush("InputBrush", ColorOf(palette[7])); SetBrush("AccentBrush", emphasis); SetBrush("AccentTextBrush", emphasis);
        SetBrush("SelectionBrush", Mix(panel, emphasis, light ? .13 : .22));
        SetBrush("HoverBrush", Mix(panel, emphasis, light ? .09 : .16)); SetBrush("PopupBrush", panel);
        SetBrush("WidgetBrush", WithOpacity(panel, glass ? settings.SurfaceOpacity : 1));
        SetBrush("BarBrush", WithOpacity(background, glass ? settings.SurfaceOpacity : 1));
        SetBrush("WarningBrush", ColorOf(light ? "#9D4F16" : "#FFD09F"));
        SetBrush("GreenBrush", ColorOf(light ? "#21765B" : "#83DCC0"));
        Application.Current.Resources["HeroBrush"] = new LinearGradientBrush(Mix(panel, emphasis, .14), panel, 20);
        foreach (Window window in Application.Current.Windows) ApplyWindow(window);
        if (!UiEffects.Enabled) UiEffects.StopAll();
    }
    private static void SetBrush(string key, Color color)
    {
        var resources = Application.Current.Resources;
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            var from = brush.Color; brush.BeginAnimation(SolidColorBrush.ColorProperty, null); brush.Color = color;
            if (UiEffects.Enabled) brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, color, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop });
        }
        else resources[key] = new SolidColorBrush(color);
    }
    private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color WithOpacity(Color color, double opacity) => Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B);
    private static Color Mix(Color a, Color b, double ratio) => Color.FromRgb((byte)(a.R + (b.R - a.R) * ratio), (byte)(a.G + (b.G - a.G) * ratio), (byte)(a.B + (b.B - a.B) * ratio));
    public static void ApplyWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var dark = Current.Theme == "Light" ? 0 : 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (window.AllowsTransparency) return;
        var glass = Current.TransparencyEnabled && !SystemParameters.HighContrast;
        var backdrop = glass ? 2 : 1;
        var result = DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
        var margins = new Margins { Left = glass && result >= 0 ? -1 : 0 };
        DwmExtendFrameIntoClientArea(handle, ref margins);
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
            target.BackgroundColor = glass && result >= 0 ? Colors.Transparent : ((SolidColorBrush)Application.Current.Resources["BackgroundBrush"]).Color with { A = 255 };
    }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);
}
