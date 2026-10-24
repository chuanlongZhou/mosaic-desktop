using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Mosaic.Core.Models;
using Mosaic.Host.ViewModels;

namespace Mosaic.Host.Controls;

public partial class DesktopWidget : UserControl
{
    public WidgetKind Kind { get; }
    public bool IsEditing => EditHandles.Visibility == Visibility.Visible;
    public event EventHandler? PlacementChanged;

    public DesktopWidget(DashboardViewModel vm, DesktopPluginPlacement placement)
    {
        InitializeComponent(); Kind = placement.Kind;
        WidgetTitle.Text = placement.Name;
        WidgetContent.Content = vm.DesktopWidget(Kind);
        Width = placement.Width; Height = placement.Height;
        Canvas.SetLeft(this, placement.Left); Canvas.SetTop(this, placement.Top);
    }

    public void SetEditing(bool editing)
    {
        EditHandles.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        Card.BorderThickness = new Thickness(editing ? 1 : 0);
    }
    private void OnMove(object sender, DragDeltaEventArgs e) => ChangePlacement(e.HorizontalChange, e.VerticalChange, false);
    private void OnResize(object sender, DragDeltaEventArgs e) => ChangePlacement(e.HorizontalChange, e.VerticalChange, true);
    private void ChangePlacement(double x, double y, bool resize)
    {
        if (!IsEditing || Parent is not Canvas canvas) return;
        if (resize) { Width = Math.Max(160, Width + x); Height = Math.Max(100, Height + y); }
        else { Canvas.SetLeft(this, Canvas.GetLeft(this) + x); Canvas.SetTop(this, Canvas.GetTop(this) + y); }
        Clamp(canvas.RenderSize); PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clamp(Size area)
    {
        var width = Math.Max(160, area.Width); var height = Math.Max(100, area.Height);
        Width = Math.Clamp(Width, 160, Math.Min(1600, width));
        Height = Math.Clamp(Height, 100, Math.Min(1200, height));
        Canvas.SetLeft(this, Math.Clamp(Canvas.GetLeft(this), 0, width - Width));
        Canvas.SetTop(this, Math.Clamp(Canvas.GetTop(this), 0, height - Height));
    }
}
