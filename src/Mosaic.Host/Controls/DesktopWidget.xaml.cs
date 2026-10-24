using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Mosaic.Core.Models;
using Mosaic.Host.ViewModels;

namespace Mosaic.Host.Controls;

public partial class DesktopWidget : UserControl
{
    public WidgetKind Kind { get; }
    public bool IsEditing => EditHandles.Visibility == Visibility.Visible;
    public event EventHandler? PlacementChanged;
    public event EventHandler? PlacementStarted;
    private readonly DashboardViewModel _owner;

    public DesktopWidget(DashboardViewModel vm, DesktopPluginPlacement placement)
    {
        InitializeComponent(); Kind = placement.Kind;
        _owner = vm;
        WidgetTitle.Text = placement.Name;
        WidgetContent.Content = vm.DesktopWidget(Kind);
        Width = placement.Width; Height = placement.Height;
        Canvas.SetLeft(this, placement.Left); Canvas.SetTop(this, placement.Top);
        SaveStatusArea.Visibility = Kind is WidgetKind.Notes or WidgetKind.Tasks ? Visibility.Visible : Visibility.Collapsed;
        MoveHandle.DragStarted += (_, _) => PlacementStarted?.Invoke(this, EventArgs.Empty);
        SizeHandle.DragStarted += (_, _) => PlacementStarted?.Invoke(this, EventArgs.Empty);
        MoveHandle.DragCompleted += (_, _) => Snap(); SizeHandle.DragCompleted += (_, _) => Snap();
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

    private void Snap()
    {
        if (!IsEditing || !_owner.SnapToGrid || Parent is not Canvas canvas) return;
        Canvas.SetLeft(this, Math.Round(Canvas.GetLeft(this) / 12) * 12); Canvas.SetTop(this, Math.Round(Canvas.GetTop(this) / 12) * 12);
        Width = Math.Round(Width / 12) * 12; Height = Math.Round(Height / 12) * 12;
        Clamp(canvas.RenderSize); PlacementChanged?.Invoke(this, EventArgs.Empty);
    }
    private void OnPlacementKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsEditing || Keyboard.FocusedElement is TextBox) return;
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        var (x, y) = e.Key switch { Key.Left => (-step, 0), Key.Right => (step, 0), Key.Up => (0, -step), Key.Down => (0, step), _ => (0, 0) };
        if (x == 0 && y == 0) return;
        PlacementStarted?.Invoke(this, EventArgs.Empty); ChangePlacement(x, y, false); e.Handled = true;
    }

    public void Clamp(Size area)
    {
        var width = Math.Max(160, area.Width); var height = Math.Max(100, area.Height);
        var minimum = Kind switch { WidgetKind.Tasks => new Size(300, 320), WidgetKind.Notes => new Size(280, 220), WidgetKind.Clock => new Size(260, 190), WidgetKind.QuickAccess => new Size(360, 230), _ => new Size(240, 140) };
        Width = Math.Clamp(Width, Math.Min(minimum.Width, width), Math.Min(1600, width));
        Height = Math.Clamp(Height, Math.Min(minimum.Height, height), Math.Min(1200, height));
        Canvas.SetLeft(this, Math.Clamp(Canvas.GetLeft(this), 0, width - Width));
        Canvas.SetTop(this, Math.Clamp(Canvas.GetTop(this), 0, height - Height));
    }
}
