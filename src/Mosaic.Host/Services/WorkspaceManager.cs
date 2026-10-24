using System.Text.Json;
using Mosaic.Core;
using Mosaic.Core.Models;
using Mosaic.Storage.Json;

namespace Mosaic.Host.Services;

public sealed record WorkspaceSwitchResult(bool Committed, string Message);

public sealed class WorkspaceManager(AppServices services)
{
    private readonly string _prefix = "profiles/" + services.Profile.ProfileId + "/";
    private WorkspaceSummary[] _items = [];
    private WorkspaceSwitchJournal? _journal;
    public IReadOnlyList<WorkspaceSummary> Items => Volatile.Read(ref _items);
    public bool IsSwitching { get; private set; }
    public string? Notice { get; private set; }
    public WorkspaceSwitchResult? LastResult { get; private set; }
    public event Action? Changed;

    internal async Task InitializeAsync()
    {
        var directory = Path.Combine(services.Store.Root, _prefix, "workspaces");
        var loaded = new List<WorkspaceSummary>();
        long bytes = 0;
        if (Directory.Exists(directory))
            foreach (var id in Directory.EnumerateFiles(directory, "*.json*", SearchOption.TopDirectoryOnly)
                .Where(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase))
                .Select(x => Path.GetFileName(x).Split('.')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            {
                DocumentRules.Identifier(id);
                var path = Path.Combine(directory, id + ".json");
                bytes += new FileInfo(File.Exists(path) ? path : path + ".bak").Length;
                if (loaded.Count >= 100 || bytes > 16 * 1024 * 1024) { Notice = "工作空间目录超过 100 个或 16 MiB 读取预算，保留原文件并显示部分。"; break; }
                try { loaded.Add(Summarize(id == services.Workspace.Id ? services.Workspace : await ReadAsync(id))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { Notice = "部分工作空间无法读取，原文件已保留：" + ex.Message; }
            }
        if (loaded.All(x => x.Id != services.Workspace.Id)) loaded.Add(Summarize(services.Workspace));
        Volatile.Write(ref _items, loaded.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        _journal = await services.Store.LoadAsync(_prefix + "workspace-switch.json", MosaicJsonContext.Configured.WorkspaceSwitchJournal, () => new());
        if (_journal.Stage is "Preflight" or "Snapshot" or "ApplyMosaicState")
            Notice = services.Settings.CurrentWorkspaceId == _journal.To ? "上次切换已提交，已按最后成功配置恢复工作空间。" : "上次切换未提交，继续使用最后成功的工作空间。";
    }

    private static WorkspaceSummary Summarize(WorkspaceDocument document) => new(document.Id, document.Name, document.ResourceIds.Count,
        document.Dock?.Items.Count ?? 0, document.DesktopWidgets?.Count(x => x.Enabled) ?? 0);
    internal void Publish(WorkspaceDocument document)
    {
        Volatile.Write(ref _items, Items.Where(x => x.Id != document.Id).Append(Summarize(document)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        Changed?.Invoke();
    }
    private async Task<WorkspaceDocument> ReadAsync(string id, CancellationToken token = default)
    {
        DocumentRules.Identifier(id);
        var value = await services.Store.LoadAsync(_prefix + "workspaces/" + id + ".json", MosaicJsonContext.Configured.WorkspaceDocument,
            () => throw new FileNotFoundException("工作空间文件不存在，未切换。"), token);
        if (value.Id != id) throw new InvalidDataException("工作空间 ID 与文件不一致。");
        return value;
    }
    private static WorkspaceDocument Copy(WorkspaceDocument value) => JsonSerializer.Deserialize(
        JsonSerializer.SerializeToUtf8Bytes(value, MosaicJsonContext.Configured.WorkspaceDocument), MosaicJsonContext.Configured.WorkspaceDocument)!;
    public static DockSettings CopyDock(DockSettings dock) => JsonSerializer.Deserialize(
        JsonSerializer.SerializeToUtf8Bytes(new AppSettings { Dock = dock }, MosaicJsonContext.Configured.AppSettings), MosaicJsonContext.Configured.AppSettings)!.Dock;
    public static List<DesktopPluginPlacement> CopyDesktop(IEnumerable<DesktopPluginPlacement> widgets) => widgets.Select(x => new DesktopPluginPlacement
        { Kind = x.Kind, Enabled = x.Enabled, Left = x.Left, Top = x.Top, Width = x.Width, Height = x.Height }).ToList();

    public async Task<WorkspaceDocument> GetCopyAsync(string id)
    {
        await services.StateGate.WaitAsync();
        try { return id == services.Workspace.Id ? services.CopyWorkspace() : Copy(await ReadAsync(id)); }
        finally { services.StateGate.Release(); }
    }

    public async Task<WorkspaceDocument> CreateAsync(string name, string preset = "clone")
    {
        await services.StateGate.WaitAsync();
        try
        {
            if (Notice?.Contains("读取预算", StringComparison.Ordinal) == true || Items.Count >= 100) throw new InvalidOperationException("工作空间达到容量限制。");
            var candidate = preset == "clone" ? services.CopyWorkspace() : Defaults.Workspace();
            candidate.Id = Guid.NewGuid().ToString("N"); candidate.Name = name.Trim(); candidate.Revision = 0;
            candidate.Dock = CopyDock(services.Settings.Dock);
            candidate.DesktopWidgets = CopyDesktop(services.Settings.Desktop.Widgets);
            if (preset is "writing" or "ai")
                foreach (var widget in candidate.DesktopWidgets)
                    widget.Enabled = widget.Kind is WidgetKind.Clock or WidgetKind.Notes or WidgetKind.Tasks or WidgetKind.QuickAccess ||
                        preset == "ai" && widget.Kind is WidgetKind.Cpu or WidgetKind.Memory or WidgetKind.Gpu;
            if (preset == "writing")
            {
                candidate.Layouts = candidate.Layouts.ToDictionary(x => x.Key,
                    x => GridLayout.Adapt(x.Value.Where(p => p.Kind is WidgetKind.QuickAccess or WidgetKind.Notes or WidgetKind.Tasks or WidgetKind.Clock), int.Parse(x.Key)));
                foreach (var layout in candidate.Layouts.Values)
                {
                    var firstRow = layout.Count == 0 ? 0 : layout.Min(x => x.Row);
                    foreach (var widget in layout) widget.Row -= firstRow;
                }
            }
            candidate.Validate();
            var path = _prefix + "workspaces/" + candidate.Id + ".json";
            await services.Store.LoadAsync(path, MosaicJsonContext.Configured.WorkspaceDocument, () => candidate);
            await services.Store.SaveAsync(path, candidate, MosaicJsonContext.Configured.WorkspaceDocument);
            Publish(candidate);
            return Copy(candidate);
        }
        finally { services.StateGate.Release(); }
    }

    public Task RenameAsync(string id, string name) => MutateAsync(id, document => document.Name = name.Trim());
    public Task SetResourceAsync(string id, string resourceId, bool include) => MutateAsync(id, document =>
    {
        if (include) { if (!document.ResourceIds.Contains(resourceId)) document.ResourceIds.Add(resourceId); }
        else document.ResourceIds.Remove(resourceId);
    });
    private async Task MutateAsync(string id, Action<WorkspaceDocument> update)
    {
        await services.StateGate.WaitAsync();
        try
        {
            var candidate = id == services.Workspace.Id ? services.CopyWorkspace() : Copy(await ReadAsync(id));
            update(candidate);
            await services.Store.SaveAsync(_prefix + "workspaces/" + id + ".json", candidate, MosaicJsonContext.Configured.WorkspaceDocument);
            if (id == services.Workspace.Id) services.SetWorkspaceSnapshot(candidate);
            Publish(candidate);
        }
        finally { services.StateGate.Release(); }
    }

    public async Task<WorkspaceSwitchResult> SwitchAsync(string id, CancellationToken token = default)
    {
        await services.StateGate.WaitAsync(token);
        var committed = false;
        try
        {
            if (id == services.Workspace.Id) return LastResult = new(true, "已经在“" + services.Workspace.Name + "”工作空间。");
            IsSwitching = true; Changed?.Invoke();
            _journal = new() { Revision = _journal!.Revision, From = services.Workspace.Id, To = id };
            var target = await ReadAsync(id, token); target.Validate();
            await WriteStageAsync("Preflight", "已验证目标配置；不启动应用或恢复外部窗口。", token);
            var snapshot = services.CopyWorkspace(); snapshot.Dock = CopyDock(services.Settings.Dock);
            snapshot.DesktopWidgets = CopyDesktop(services.Settings.Desktop.Widgets);
            await WriteStageAsync("Snapshot", "保存当前 Dock、Dashboard 和资源入口。", token);
            await services.Store.SaveAsync(_prefix + "workspaces/" + snapshot.Id + ".json", snapshot, MosaicJsonContext.Configured.WorkspaceDocument, token);
            services.SetWorkspaceSnapshot(snapshot); Publish(snapshot);
            var settings = services.CopySettings(); settings.CurrentWorkspaceId = id;
            settings.Dock = CopyDock(target.Dock ?? services.Settings.Dock);
            if (target.DesktopWidgets is not null)
            {
                settings.Desktop.Widgets = CopyDesktop(target.DesktopWidgets);
                foreach (var placement in DesktopPluginPlacement.Defaults())
                    if (settings.Desktop.Widgets.All(x => x.Kind != placement.Kind)) { placement.Enabled = false; settings.Desktop.Widgets.Add(placement); }
                settings.Modules.DesktopClock = settings.Desktop.Widgets.Any(x => x.Kind == WidgetKind.Clock && x.Enabled);
            }
            await WriteStageAsync("ApplyMosaicState", "准备提交当前工作空间指针。", token);
            token.ThrowIfCancellationRequested();
            // Once pointer commit begins, finish it and publish matching in-memory state.
            await services.Store.SaveAsync(_prefix + "settings.json", settings, MosaicJsonContext.Configured.AppSettings, CancellationToken.None);
            committed = true;
            services.ActivateWorkspace(settings, target); Publish(target);
            await WriteStageAsync("Complete", "已应用 Mosaic 布局、Dock 和资源入口。", CancellationToken.None);
            return LastResult = new(true, "已切换到“" + target.Name + "” · 未启动外部应用");
        }
        catch (Exception ex)
        {
            var stage = committed ? "PartialFailure" : ex is OperationCanceledException ? "Cancelled" : "Failed";
            try { if (_journal is not null) await WriteStageAsync(stage, committed ? "配置已提交；部分界面或日志步骤失败。" : "未提交工作空间指针，保留当前配置。", CancellationToken.None); }
            catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            if (committed) return LastResult = new(true, "工作空间已提交，部分步骤未完成：" + ex.Message);
            throw;
        }
        finally { IsSwitching = false; services.StateGate.Release(); Changed?.Invoke(); }
    }

    private async Task WriteStageAsync(string stage, string message, CancellationToken token)
    {
        var candidate = new WorkspaceSwitchJournal { Revision = _journal!.Revision, ExecutionId = _journal.ExecutionId, From = _journal.From, To = _journal.To,
            Stage = stage, Message = message, UpdatedUtc = DateTime.UtcNow };
        await services.Store.SaveAsync(_prefix + "workspace-switch.json", candidate, MosaicJsonContext.Configured.WorkspaceSwitchJournal, token);
        _journal = candidate;
    }
}
