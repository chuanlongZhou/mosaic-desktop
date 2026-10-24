using System.Text.Json.Serialization;
using Mosaic.Core.Search;

namespace Mosaic.Core.Models;

public abstract class VersionedDocument
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public abstract void Validate();

    protected void ValidateVersion()
    {
        if (SchemaVersion != 1 || Revision < 0)
            throw new InvalidDataException("配置版本暂不支持，或 revision 无效。请保留原文件。");
    }
}

public sealed class ActiveProfile : VersionedDocument
{
    public string ProfileId { get; set; } = "default";
    public override void Validate()
    {
        ValidateVersion();
        DocumentRules.Identifier(ProfileId);
    }
}

public sealed class AppSettings : VersionedDocument
{
    public string CurrentWorkspaceId { get; set; } = "personal";
    public ModuleSettings Modules { get; set; } = new();
    public DockSettings Dock { get; set; } = new();
    public DesktopSettings Desktop { get; set; } = new();
    public bool OpenDashboardOnStartup { get; set; }
    public SearchSettings Search { get; set; } = new();
    public LibrarySettings Library { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public ShortcutSettings Shortcuts { get; set; } = new();

    public override void Validate()
    {
        ValidateVersion();
        DocumentRules.Identifier(CurrentWorkspaceId);
        if (Search is null) throw new InvalidDataException("搜索设置无效。");
        Search.Validate();
        if (Appearance is null || Shortcuts is null) throw new InvalidDataException("外观或快捷键设置无效。");
        Appearance.Validate();
        Shortcuts.Validate(Search.HotkeyModifiers, Search.HotkeyVirtualKey, Search.Enabled && Search.HotkeyEnabled);
        if (Library is null || Library.ViewMode is not ("cards" or "list")) throw new InvalidDataException("资源库视图无效。");
        if (Modules is null || Dock is null || Desktop is null || Dock.Items is null || Dock.Items.Count > 24)
            throw new InvalidDataException("模块或 Dock 配置无效（最多 24 个入口）。");
        if (!double.IsFinite(Desktop.Left) || !double.IsFinite(Desktop.Top))
            throw new InvalidDataException("桌面位置无效。");
        if (Dock.IconSize is < 24 or > 80) throw new InvalidDataException("Dock 图标大小须为 24–80。");
        if (Desktop.Widgets is null || Desktop.Widgets.Count > Enum.GetValues<WidgetKind>().Length || Desktop.Widgets.Any(x => x is null) || Desktop.Widgets.Select(x => x.Kind).Distinct().Count() != Desktop.Widgets.Count)
            throw new InvalidDataException("桌面插件配置无效。");
        foreach (var widget in Desktop.Widgets)
            if (!Enum.IsDefined(widget.Kind) || !double.IsFinite(widget.Left) || !double.IsFinite(widget.Top) ||
                !double.IsFinite(widget.Width) || !double.IsFinite(widget.Height) || widget.Width is < 160 or > 1600 || widget.Height is < 100 or > 1200)
                throw new InvalidDataException("桌面插件的位置或尺寸无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Dock.Items)
        {
            if (item is null) throw new InvalidDataException("Dock 入口不能为空。");
            DocumentRules.Identifier(item.Id);
            if (!ids.Add(item.Id) || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 80 ||
                item.Command is null || item.Command.Length > 80 || item.Target is null || item.Target.Length > 4096 ||
                item.Glyph is null || item.Glyph.Length > 4)
                throw new InvalidDataException("Dock 入口包含重复 ID 或无效字段。");
        }
    }
}

public sealed class ModuleSettings
{
    public bool Dock { get; set; } = true;
    public bool DesktopClock { get; set; } = true;
    public bool Library { get; set; } = true;
    public bool Workspaces { get; set; } = true;
}

public sealed class LibrarySettings
{
    public string ViewMode { get; set; } = "cards";
}

public sealed class DesktopSettings
{
    public bool OverlayEnabled { get; set; }
    public bool MenuBarAutoHide { get; set; } = true;
    public double Left { get; set; } = 44;
    public double Top { get; set; } = 48;
    public List<DesktopPluginPlacement> Widgets { get; set; } = DesktopPluginPlacement.Defaults();
    public bool SnapToGrid { get; set; } = true;
}

public sealed class DesktopPluginPlacement
{
    public WidgetKind Kind { get; set; }
    public bool Enabled { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 220;
    [JsonIgnore] public string Name => Kind switch
    {
        WidgetKind.Notes => "随手记", WidgetKind.Tasks => "待办", WidgetKind.Cpu => "CPU", WidgetKind.Memory => "内存",
        WidgetKind.Gpu => "GPU", WidgetKind.GpuMemory => "显存", WidgetKind.Temperature => "温度", WidgetKind.Fan => "风扇转速",
        WidgetKind.Network => "网络", WidgetKind.Storage => "存储", WidgetKind.QuickAccess => "快捷访问", _ => "时钟"
    };
    [JsonIgnore] public string Description => Kind switch
    {
        WidgetKind.Clock => "时间与日期，让一天有序开始", WidgetKind.Notes => "随手记录想法，自动保存到本机",
        WidgetKind.Tasks => "添加、编辑和完成待办，删除后可撤销", WidgetKind.Cpu => "处理器总使用率与实时趋势",
        WidgetKind.Memory => "物理内存使用量与占用比例", WidgetKind.Gpu => "GPU 引擎负载与实时趋势",
        WidgetKind.GpuMemory => "显卡专用内存使用量", WidgetKind.Temperature => "硬件温度；取决于设备传感器接口",
        WidgetKind.Fan => "风扇 RPM；需要可用的转速传感器", WidgetKind.Network => "当前连接的下载与上传速率",
        WidgetKind.Storage => "系统磁盘空间使用情况", _ => "常用文件夹与工作空间资源入口"
    };
    [JsonIgnore] public string Glyph => Kind switch
    {
        WidgetKind.Clock => "\uE917", WidgetKind.Notes => "\uE70B", WidgetKind.Tasks => "\uE73E",
        WidgetKind.Cpu or WidgetKind.Gpu => "\uE950", WidgetKind.Memory or WidgetKind.GpuMemory => "\uE964",
        WidgetKind.Temperature => "\uE9CA", WidgetKind.Fan => "\uE895", WidgetKind.Network => "\uE701",
        WidgetKind.Storage => "\uEDA2", _ => "\uE8B7"
    };
    public static List<DesktopPluginPlacement> Defaults() =>
    [
        new() { Kind = WidgetKind.Clock, Enabled = true, Left = 44, Top = 64, Height = 192 },
        new() { Kind = WidgetKind.Notes, Enabled = true, Left = 44, Top = 278, Height = 260 },
        new() { Kind = WidgetKind.Tasks, Enabled = true, Left = 386, Top = 64, Height = 474 },
        new() { Kind = WidgetKind.Cpu, Enabled = true, Left = 44, Top = 560, Width = 320, Height = 130 },
        new() { Kind = WidgetKind.Memory, Left = 386, Top = 560, Width = 320, Height = 130 },
        new() { Kind = WidgetKind.Network, Left = 728, Top = 560, Width = 320, Height = 130 },
        new() { Kind = WidgetKind.Storage, Left = 1070, Top = 560, Width = 320, Height = 130 },
        new() { Kind = WidgetKind.QuickAccess, Left = 728, Top = 64, Width = 480, Height = 240 },
        new() { Kind = WidgetKind.Gpu, Left = 44, Top = 710, Height = 150 },
        new() { Kind = WidgetKind.GpuMemory, Left = 386, Top = 710, Height = 150 },
        new() { Kind = WidgetKind.Temperature, Left = 728, Top = 710, Height = 150 },
        new() { Kind = WidgetKind.Fan, Left = 1070, Top = 710, Height = 150 }
    ];
}

public sealed class DockSettings
{
    public int IconSize { get; set; } = 40;
    public bool AutoHide { get; set; } = true;
    public bool SyncTaskbarPins { get; set; } = true;
    public List<DockItem> Items { get; set; } = [];
}

public sealed class DockItem
{
    public string? ResourceId { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新入口";
    public string Glyph { get; set; } = "\uE8A5";
    public string Command { get; set; } = "resource.open";
    public string Target { get; set; } = "";
}

[JsonConverter(typeof(JsonStringEnumConverter<WidgetKind>))]
public enum WidgetKind { Cpu, Memory, Network, Storage, QuickAccess, Tasks, Notes, Clock, Gpu, GpuMemory, Temperature, Fan }

public sealed class WidgetPlacement
{
    public string Id { get; set; } = "";
    public WidgetKind Kind { get; set; }
    public int Column { get; set; }
    public int Row { get; set; }
    public int ColumnSpan { get; set; } = 3;
    public int RowSpan { get; set; } = 1;
    public WidgetPlacement Copy() => (WidgetPlacement)MemberwiseClone();
}

public sealed class WorkspaceDocument : VersionedDocument
{
    public string Id { get; set; } = "personal";
    public string Name { get; set; } = "个人桌面";
    public string Theme { get; set; } = "Dark";
    public Dictionary<string, List<WidgetPlacement>> Layouts { get; set; } = [];
    public List<string> ResourceIds { get; set; } = [];
    public DockSettings? Dock { get; set; }
    public List<DesktopPluginPlacement>? DesktopWidgets { get; set; }
    public override void Validate()
    {
        ValidateVersion();
        DocumentRules.Identifier(Id);
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Theme != "Dark" || Layouts is null || Layouts.Count > 3)
            throw new InvalidDataException("工作空间名称、主题或布局无效。");
        ResourceReferences.Validate(ResourceIds);
        if (Dock is not null) new AppSettings { Dock = Dock }.Validate();
        if (DesktopWidgets is not null) new AppSettings { Desktop = new() { Widgets = DesktopWidgets } }.Validate();
        foreach (var (breakpoint, widgets) in Layouts)
        {
            if (!int.TryParse(breakpoint, out var columns) || columns is not (4 or 8 or 12) || widgets is null || widgets.Count > 24)
                throw new InvalidDataException("Dashboard 断点或组件数量无效。");
            var ids = new HashSet<string>();
            foreach (var widget in widgets)
            {
                if (widget is null) throw new InvalidDataException("组件配置不能为空。");
                DocumentRules.Identifier(widget.Id);
                if (!ids.Add(widget.Id) || !Enum.IsDefined(widget.Kind) || widget.Column < 0 || widget.Row is < 0 or > 100 ||
                    widget.ColumnSpan < 1 || widget.Column + widget.ColumnSpan > columns || widget.RowSpan is < 1 or > 6)
                    throw new InvalidDataException("组件布局超出网格边界或包含重复 ID。");
            }
            if (widgets.Any(a => widgets.Any(b => a != b && GridLayout.Overlaps(a, b))))
                throw new InvalidDataException("Dashboard 布局包含相互重叠的组件。");
        }
    }
}

public sealed class WidgetContent : VersionedDocument
{
    public string WidgetId { get; set; } = "local-content";
    public string Notes { get; set; } = "";
    public List<LocalTask> Tasks { get; set; } = [];
    public override void Validate()
    {
        ValidateVersion();
        DocumentRules.Identifier(WidgetId);
        if (Notes is null || Notes.Length > 20000 || Tasks is null || Tasks.Count > 200)
            throw new InvalidDataException("笔记最多 20,000 字符，任务最多 200 项。");
        var ids = new HashSet<string>();
        foreach (var task in Tasks)
        {
            if (task is null) throw new InvalidDataException("任务不能为空。");
            DocumentRules.Identifier(task.Id);
            if (!ids.Add(task.Id) || string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 200)
                throw new InvalidDataException("任务 ID 或标题无效。");
        }
    }
}

public sealed class LocalTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
}

public static class DocumentRules
{
    public static void Identifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 80 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new InvalidDataException("配置标识符只能包含英文字母、数字、下划线和短横线。");
    }
}
