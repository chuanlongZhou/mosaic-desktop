namespace Mosaic.Core.Models;

public static class Defaults
{
    public static AppSettings Settings(string home) => new()
    {
        Dock = new DockSettings
        {
            Items =
            [
                new() { Id = "dashboard", Title = "工作台", Glyph = "\uE80F", Command = "dashboard.toggle" },
                new() { Id = "search", Title = "全局搜索", Glyph = "\uE721", Command = "palette.toggle" },
                new() { Id = "library", Title = "资源库", Glyph = "\uE8A9", Command = "library.open" },
                new() { Id = "workspaces", Title = "工作空间", Glyph = "\uE81E", Command = "workspaces.open" },
                new() { Id = "files", Title = "文件资源管理器", Glyph = "\uE8B7", Target = home },
                new() { Id = "notepad", Title = "记事本", Glyph = "\uE70B", Command = "app.builtin", Target = "notepad" },
                new() { Id = "calculator", Title = "计算器", Glyph = "\uE8EF", Command = "app.builtin", Target = "calculator" },
                new() { Id = "settings", Title = "Mosaic 设置", Glyph = "\uE713", Command = "settings.open" }
            ]
        }
    };

    public static WorkspaceDocument Workspace() => new()
    {
        Layouts = new()
        {
            ["12"] =
            [
                At("cpu", WidgetKind.Cpu, 0, 0, 3, 1),
                At("memory", WidgetKind.Memory, 3, 0, 3, 1),
                At("network", WidgetKind.Network, 6, 0, 3, 1),
                At("storage", WidgetKind.Storage, 9, 0, 3, 1),
                At("quick-access", WidgetKind.QuickAccess, 0, 1, 8, 2),
                At("tasks", WidgetKind.Tasks, 8, 1, 4, 4),
                At("notes", WidgetKind.Notes, 0, 3, 4, 2),
                At("clock", WidgetKind.Clock, 4, 3, 4, 2)
            ]
        }
    };

    public static WidgetPlacement At(string id, WidgetKind kind, int column, int row, int width, int height) =>
        new() { Id = id, Kind = kind, Column = column, Row = row, ColumnSpan = width, RowSpan = height };
}
