using System.Collections.ObjectModel;
using System.Windows.Threading;
using Mosaic.Core.Models;
using Mosaic.Host.Services;

namespace Mosaic.Host.ViewModels;

public sealed class WorkspacesViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private WorkspaceSummary? _selected;
    private string _newName = "", _rename = "", _status = "切换会保存当前 Dock 和布局；笔记与待办在工作空间之间共享。";
    private bool _busy, _error, _disposed;
    private Task _pending = Task.CompletedTask;
    public ObservableCollection<WorkspaceSummary> Items { get; } = [];
    public string CurrentName => _services.Workspace.Name;
    public string NewName { get => _newName; set => Set(ref _newName, value); }
    public string Rename { get => _rename; set => Set(ref _rename, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsError { get => _error; private set => Set(ref _error, value); }
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) Raise(nameof(IsIdle)); } }
    public bool IsIdle => !Busy && !_services.Workspaces.IsSwitching;
    public bool HasSelection => Selected is not null;
    public bool CanSwitch => Selected is not null && Selected.Id != _services.Workspace.Id && IsIdle;
    public string SelectionSummary => Selected is null ? "选择一个工作空间" : $"{Selected.ResourceCount} 个资源入口 · {Selected.DockCount} 个已保存 Dock 入口 · {Selected.WidgetCount} 个已保存桌面组件";
    public string SwitchLabel => Selected?.Id == _services.Workspace.Id ? "当前工作空间" : "切换到此工作空间";
    public WorkspaceSummary? Selected
    {
        get => _selected;
        set
        {
            var different = _selected?.Id != value?.Id;
            if (!Set(ref _selected, value)) return;
            if (different) Rename = value?.Name ?? "";
            Raise(nameof(SelectionSummary)); Raise(nameof(HasSelection)); Raise(nameof(SwitchLabel)); Raise(nameof(CanSwitch));
        }
    }
    public WorkspacesViewModel(AppServices services)
    {
        _services = services; services.Workspaces.Changed += OnChanged; Refresh();
        if (services.Workspaces.Notice is { } notice) Status = notice;
    }
    private void Refresh()
    {
        var selected = Selected?.Id ?? _services.Workspace.Id;
        Items.Clear(); foreach (var item in _services.Workspaces.Items) Items.Add(item);
        Selected = Items.FirstOrDefault(x => x.Id == selected) ?? Items.FirstOrDefault();
        Raise(nameof(CurrentName)); Raise(nameof(IsIdle)); Raise(nameof(SwitchLabel)); Raise(nameof(CanSwitch));
    }
    public Task CreateAsync(string preset) => RunAsync(async () =>
    {
        var name = NewName.Trim();
        if (name.Length == 0) name = preset == "writing" ? "Writing" : preset == "ai" ? "AI Lab" : "新工作空间";
        var created = await _services.Workspaces.CreateAsync(name, preset);
        Refresh(); Selected = Items.Single(x => x.Id == created.Id); NewName = "";
        Status = "已创建“" + created.Name + "”，点击切换即可使用。";
    });
    public Task RenameAsync() => Selected is not { } selected ? Task.CompletedTask : RunAsync(async () =>
    {
        await _services.Workspaces.RenameAsync(selected.Id, Rename); Refresh(); Status = "工作空间名称已保存";
    });
    public Task SwitchAsync() => Selected is not { } selected ? Task.CompletedTask : RunAsync(async () =>
    {
        await _services.Commands.ExecuteAsync(new("workspace.switch", selected.Id)); Refresh();
        Status = _services.Workspace.Id == selected.Id ? _services.Workspaces.LastResult?.Message ?? "已切换到“" + _services.Workspace.Name + "”" : "工作空间没有变化";
    });
    public Task OpenResourcesAsync() => Selected is not { } selected ? Task.CompletedTask : RunAsync(() => _services.Commands.ExecuteAsync(new("library.workspace", selected.Id)));
    private Task RunAsync(Func<Task> operation)
    {
        if (Busy || _disposed) return Task.CompletedTask;
        return _pending = RunCoreAsync(operation);
    }
    private async Task RunCoreAsync(Func<Task> operation)
    {
        Busy = true; IsError = false;
        try { await operation(); }
        catch (Exception ex) { if (!_disposed) { Status = "操作未完成：" + ex.Message; IsError = true; } }
        finally { if (!_disposed) Busy = false; }
    }
    private void OnChanged()
    {
        if (_disposed) return;
        _dispatcher.BeginInvoke(() => { if (!_disposed) Refresh(); });
    }
    public Task FlushAsync() => _pending;
    public void Dispose() { _disposed = true; _services.Workspaces.Changed -= OnChanged; }
}
