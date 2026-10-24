using System.Windows;
using Mosaic.Host.ViewModels;

namespace Mosaic.Host.Controls;

// Shared templates work in both the settings window and the native desktop canvas.
public static class WidgetContext
{
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached(
        "Owner", typeof(DashboardViewModel), typeof(WidgetContext), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static void SetOwner(DependencyObject element, DashboardViewModel value) => element.SetValue(OwnerProperty, value);
    public static DashboardViewModel GetOwner(DependencyObject element) => (DashboardViewModel)element.GetValue(OwnerProperty);
}
