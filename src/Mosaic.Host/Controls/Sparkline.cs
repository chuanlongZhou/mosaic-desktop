using System.Windows;
using System.Windows.Media;

namespace Mosaic.Host.Controls;

public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(IEnumerable<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.LightSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<double> Values { get => (IEnumerable<double>)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var values = Values.ToArray();
        if (values.Length < 2 || ActualWidth < 1 || ActualHeight < 1) return;
        var max = Math.Max(10, values.Max() * 1.2);
        var points = values.Select((value, i) => new Point(i * ActualWidth / (values.Length - 1), ActualHeight - 2 - value / max * (ActualHeight - 4))).ToArray();
        var line = new StreamGeometry();
        using (var drawing = line.Open()) { drawing.BeginFigure(points[0], false, false); drawing.PolyLineTo(points.AsSpan(1).ToArray(), true, false); }
        line.Freeze();
        var pen = new Pen(Stroke, 1.5); pen.Freeze();
        context.DrawGeometry(null, pen, line);
    }
}
