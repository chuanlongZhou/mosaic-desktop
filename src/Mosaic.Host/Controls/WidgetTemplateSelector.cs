using System.Windows;
using System.Windows.Controls;
using Mosaic.Host.ViewModels;
using Mosaic.Core.Models;

namespace Mosaic.Host.Controls;

public sealed class WidgetTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not WidgetViewModel widget || container is not FrameworkElement element) return null;
        var key = widget.Kind switch
        {
            WidgetKind.QuickAccess => "QuickAccessTemplate", WidgetKind.Tasks => "TasksTemplate",
            WidgetKind.Notes => "NotesTemplate", WidgetKind.Clock => "ClockTemplate", _ => "MetricTemplate"
        };
        return element.FindResource(key) as DataTemplate;
    }
}
