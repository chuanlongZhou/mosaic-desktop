using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Mosaic.Host.Services;

public static class UiEffects
{
    private static bool _enabled = true;
    public static bool Enabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; MotionPreferenceChanged?.Invoke(); }
    }
    public static event Action? MotionPreferenceChanged;
    private static readonly List<WeakReference<FrameworkElement>> Transitions = [];
    private static readonly Dictionary<UIElement, GradientAdorner> Overlays = [];
    public static readonly DependencyProperty HoverProperty = DependencyProperty.RegisterAttached("Hover", typeof(bool), typeof(UiEffects), new PropertyMetadata(false, OnHoverChanged));
    public static void SetHover(DependencyObject target, bool value) => target.SetValue(HoverProperty, value);
    public static bool GetHover(DependencyObject target) => (bool)target.GetValue(HoverProperty);
    private static void OnHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control) return;
        if ((bool)e.NewValue) { control.MouseEnter += OnEnter; control.MouseLeave += OnLeave; control.Unloaded += OnUnloaded; }
        else { control.MouseEnter -= OnEnter; control.MouseLeave -= OnLeave; control.Unloaded -= OnUnloaded; Remove(control); }
    }
    private static void OnEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!Enabled || sender is not Control control || !control.IsEnabled || AdornerLayer.GetAdornerLayer(control) is not { } layer) return;
        Remove(control);
        var overlay = new GradientAdorner(control, control.TryFindResource("AccentBrush") as SolidColorBrush);
        Overlays[control] = overlay; layer.Add(overlay);
        overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        overlay.Shift();
    }
    private static void OnLeave(object sender, System.Windows.Input.MouseEventArgs e) => Remove((UIElement)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs e) => Remove((UIElement)sender);
    private static void Remove(UIElement element)
    {
        if (!Overlays.Remove(element, out var overlay)) return;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        AdornerLayer.GetAdornerLayer(element)?.Remove(overlay);
    }
    public static void Enter(FrameworkElement? element)
    {
        if (element is null) return;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (!Enabled) return;
        Transitions.RemoveAll(x => !x.TryGetTarget(out _)); Transitions.Add(new(element));
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.55, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
    }
    public static void StopAll()
    {
        foreach (var reference in Transitions) if (reference.TryGetTarget(out var element)) element.BeginAnimation(UIElement.OpacityProperty, null);
        Transitions.Clear(); foreach (var element in Overlays.Keys.ToArray()) Remove(element);
    }
    private sealed class GradientAdorner : Adorner
    {
        private readonly LinearGradientBrush _brush;
        public GradientAdorner(UIElement target, SolidColorBrush? accent) : base(target)
        {
            IsHitTestVisible = false;
            var tint = accent?.Color ?? Colors.Transparent; tint.A = 35;
            _brush = new LinearGradientBrush(tint, Colors.Transparent, 15);
        }
        public void Shift() => _brush.GradientStops[0].BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0, .75, TimeSpan.FromMilliseconds(320)) { FillBehavior = FillBehavior.Stop });
        protected override void OnRender(DrawingContext context) => context.DrawRoundedRectangle(_brush, null, new Rect(AdornedElement.RenderSize), 7, 7);
    }
}
