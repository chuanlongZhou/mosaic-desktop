using System.Windows;
using System.Windows.Controls;
using Mosaic.Core.Models;
using Mosaic.Host.ViewModels;

namespace Mosaic.Host.Controls;

public sealed class DashboardPanel : Panel
{
    public const double RowHeight = 106;
    public const double Gap = 14;
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(nameof(Columns), typeof(int), typeof(DashboardPanel),
        new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public double ColumnWidth => (ActualWidth + Gap) / Columns;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        var cell = (width + Gap) / Columns;
        var rows = 0;
        foreach (UIElement child in InternalChildren)
        {
            var placement = Placement(child);
            if (placement is null) continue;
            child.Measure(new Size(Math.Max(0, placement.ColumnSpan * cell - Gap), placement.RowSpan * RowHeight - Gap));
            rows = Math.Max(rows, placement.Row + placement.RowSpan);
        }
        return new(width, Math.Max(0, rows * RowHeight - Gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cell = (finalSize.Width + Gap) / Columns;
        foreach (UIElement child in InternalChildren)
        {
            var p = Placement(child);
            if (p is not null) child.Arrange(new Rect(p.Column * cell, p.Row * RowHeight,
                Math.Max(0, p.ColumnSpan * cell - Gap), p.RowSpan * RowHeight - Gap));
        }
        return finalSize;
    }

    private static WidgetPlacement? Placement(UIElement element) => (element as FrameworkElement)?.DataContext is WidgetViewModel vm ? vm.Placement : null;
}
