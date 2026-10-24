using System.Drawing;
using System.Windows.Forms;

namespace Mosaic.Host.Services;

// The application's NotifyIcon menu uses WinForms rather than the WPF theme.
internal sealed class DarkTrayMenuColors : ProfessionalColorTable
{
    public static Color Surface => ThemeColor("PopupBrush", Color.FromArgb(28, 41, 58));
    public static Color Text => ThemeColor("TextBrush", Color.FromArgb(243, 246, 250));
    private static Color Hover => ThemeColor("HoverBrush", Color.FromArgb(49, 68, 92));
    private static Color Line => ThemeColor("BorderBrush", Color.FromArgb(43, 58, 76));
    private static Color ThemeColor(string key, Color fallback)
    {
        var application = System.Windows.Application.Current;
        if (application?.Dispatcher.CheckAccess() == true && application.TryFindResource(key) is System.Windows.Media.SolidColorBrush brush)
            return Color.FromArgb(brush.Color.R, brush.Color.G, brush.Color.B);
        return fallback;
    }
    public override Color ToolStripDropDownBackground => Surface;
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;
    public override Color MenuBorder => Line;
    public override Color MenuItemBorder => Hover;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Hover;
    public override Color MenuItemPressedGradientMiddle => Hover;
    public override Color MenuItemPressedGradientEnd => Hover;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Line;
    public override Color CheckBackground => Hover;
    public override Color CheckSelectedBackground => Hover;
    public override Color CheckPressedBackground => Hover;
}
