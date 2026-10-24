using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Data;
using Mosaic.Core;
using Mosaic.Core.Commands;
using Mosaic.Core.Models;
using Mosaic.Host.Services;
using Mosaic.Platform.Windows;
using Mosaic.Storage.Json;
using Mosaic.Core.Search;

namespace Mosaic.Host.ViewModels;

public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly SearchLibrary? _library;
    private readonly Dispatcher _uiDispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _contentTimer;
    private readonly DispatcherTimer _metricsTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly SystemMetrics _metrics = new();
    private readonly HardwareMonitor _hardware = new();
    private readonly SemaphoreSlim _contentGate = new(1, 1);
    private readonly Dictionary<WidgetKind, MetricViewModel> _metricCards;
    private readonly Stack<List<WidgetPlacement>> _undo = new();
    private readonly Stack<List<WidgetPlacement>> _redo = new();
    private WorkspaceDocument? _draft;
    private bool _isEditing, _sampleRunning, _disposed, _isError, _monitoringRequested;
    private string _notes, _newTask = "", _status = "所有数据仅保存在本机", _time = "", _date = "", _greeting = "";
    private long _contentVersion, _savedVersion;
    private int _columns = 12;
    private int _pluginColumns = 3;
    private string _contentSaveStatus = "已保存到本机";
    private bool _contentSaveError, _hideCompleted;
    private (TaskViewModel Task, int Index)? _removedTask;

    public ObservableCollection<WidgetViewModel> Widgets { get; } = [];
    public ObservableCollection<TaskViewModel> Tasks { get; } = [];
    public ObservableCollection<QuickLink> QuickLinks { get; } = [];
    public IEnumerable<TaskViewModel> TaskPreview => Tasks.Take(3);
    public int PluginColumns { get => _pluginColumns; private set => Set(ref _pluginColumns, value); }
    public void SetPluginColumns(double width) => PluginColumns = width >= 1080 ? 4 : width >= 790 ? 3 : width >= 520 ? 2 : 1;
    public string WorkspaceName => _services.Workspace.Name;
    public bool SnapToGrid => _services.Settings.Desktop.SnapToGrid;
    public string Today => DateTime.Now.ToString("M 月 d 日 · dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
    public string Greeting { get => _greeting; private set => Set(ref _greeting, value); }
    public string Time { get => _time; private set => Set(ref _time, value); }
    public string Date { get => _date; private set => Set(ref _date, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }
    public bool IsEditing { get => _isEditing; private set { if (Set(ref _isEditing, value)) Raise(nameof(IsBrowsing)); } }
    public bool IsBrowsing => !IsEditing;
    public int Columns { get => _columns; private set => Set(ref _columns, value); }
    public int RemainingTasks => Tasks.Count(x => !x.IsCompleted);
    public string TaskSummary => Tasks.Count == 0 ? "从一件小事开始" : $"{RemainingTasks} 项待办 · {Tasks.Count(x => x.IsCompleted)} 项已完成";
    public bool HasNoTasks => Tasks.Count == 0;
    public string Notes { get => _notes; set { if (Set(ref _notes, value)) MarkContentDirty(); } }
    public string NewTask { get => _newTask; set => Set(ref _newTask, value); }
    public RelayCommand AddTaskCommand { get; }
    public RelayCommand RemoveTaskCommand { get; }
    public RelayCommand OpenQuickLinkCommand { get; }
    public RelayCommand RetryContentSaveCommand { get; }
    public RelayCommand UndoRemoveTaskCommand { get; }
    public RelayCommand ToggleCompletedCommand { get; }
    public string ContentSaveStatus { get => _contentSaveStatus; private set => Set(ref _contentSaveStatus, value); }
    public bool ContentSaveError { get => _contentSaveError; private set => Set(ref _contentSaveError, value); }
    public bool CanUndoRemove => _removedTask is not null;
    public System.ComponentModel.ICollectionView VisibleTasks { get; }
    public string CompletedToggleLabel => _hideCompleted ? "显示已完成" : "隐藏已完成";

    public DashboardViewModel(AppServices services, Func<CommandRequest, Task> execute, SearchLibrary? library = null)
    {
        _services = services;
        _library = library;
        services.WorkspaceActivated += OnWorkspaceActivated;
        services.Workspaces.Changed += OnWorkspaceMetadataChanged;
        if (library is not null) library.Changed += OnWorkspaceMetadataChanged;
        _notes = services.Content.Notes;
        _metricCards = new()
        {
            [WidgetKind.Cpu] = new("CPU", "系统总使用率", "#79C2FF"),
            [WidgetKind.Memory] = new("内存", "物理内存", "#B19AFF"),
            [WidgetKind.Network] = new("网络", "当前连接 · 下载", "#73D6B3"),
            [WidgetKind.Storage] = new("存储", "系统磁盘", "#E3BF80"),
            [WidgetKind.Gpu] = new("GPU", "启用后读取 GPU 引擎负载", "#8BCCFF"),
            [WidgetKind.GpuMemory] = new("显存", "显卡专用内存", "#B19AFF"),
            [WidgetKind.Temperature] = new("温度", "启用后检测硬件温度传感器", "#F2CF8C"),
            [WidgetKind.Fan] = new("风扇转速", "启用后检测 RPM 传感器", "#83DCC0")
        };
        foreach (var task in services.Content.Tasks)
        {
            var item = new TaskViewModel(task.Id, task.Title, task.IsCompleted);
            item.PropertyChanged += TaskChanged;
            Tasks.Add(item);
        }
        AddTaskCommand = new(_ => AddTask());
        VisibleTasks = CollectionViewSource.GetDefaultView(Tasks);
        VisibleTasks.Filter = value => value is TaskViewModel task && (!_hideCompleted || !task.IsCompleted);
        RemoveTaskCommand = new(parameter =>
        {
            if (parameter is not TaskViewModel task) return;
            _removedTask = (task, Tasks.IndexOf(task)); task.PropertyChanged -= TaskChanged; Tasks.Remove(task);
            Raise(nameof(CanUndoRemove)); MarkContentDirty(); UpdateTasks();
        });
        UndoRemoveTaskCommand = new(_ =>
        {
            if (_removedTask is not { } removed || Tasks.Count >= 200) return;
            removed.Task.PropertyChanged += TaskChanged; Tasks.Insert(Math.Clamp(removed.Index, 0, Tasks.Count), removed.Task);
            _removedTask = null; Raise(nameof(CanUndoRemove)); MarkContentDirty(); UpdateTasks();
        });
        ToggleCompletedCommand = new(_ => { _hideCompleted = !_hideCompleted; VisibleTasks.Refresh(); Raise(nameof(CompletedToggleLabel)); });
        RetryContentSaveCommand = new(async _ => { try { await FlushAsync(); } catch (Exception ex) { SetStatus("保存失败：" + ex.Message, true); } });
        OpenQuickLinkCommand = new(async parameter => { if (parameter is QuickLink link) await execute(new(link.Command, link.Path)); });
        AddQuickLink("用户目录", "文件与日常资料", "\uE77B", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddQuickLink("文档", "你的文档入口", "\uE8A5", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddQuickLink("桌面", "Windows 桌面文件", "\uE7F4", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddQuickLink("项目目录", "当前启动位置", "\uE943", Environment.CurrentDirectory);
        _ = RefreshWorkspaceLinksAsync();
        LoadLayout();
        _contentTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _contentTimer.Tick += async (_, _) => { _contentTimer.Stop(); try { await FlushAsync(); } catch (Exception ex) { SetStatus("保存失败：" + ex.Message, true); } };
        _metricsTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _metricsTimer.Tick += async (_, _) => await SampleAsync();
        _clockTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
    }

    private void AddQuickLink(string title, string description, string glyph, string path)
    {
        if (Directory.Exists(path)) QuickLinks.Add(new(title, description, glyph, path));
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Time = now.ToString("HH:mm");
        Date = now.ToString("yyyy 年 M 月 d 日  dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        _clockTimer.Interval = TimeSpan.FromSeconds(60 - now.Second);
        Greeting = now.Hour switch { < 6 => "夜深了，给自己一点休息时间。", < 12 => "早上好，开启有序的一天。", < 18 => "下午好，让事情各就其位。", _ => "晚上好，留一点空间给自己。" };
        Raise(nameof(Today));
    }

    private void TaskChanged(object? sender, PropertyChangedEventArgs e) { MarkContentDirty(); UpdateTasks(); if (e.PropertyName == nameof(TaskViewModel.IsCompleted)) VisibleTasks.Refresh(); }
    private void UpdateTasks() { Raise(nameof(TaskSummary)); Raise(nameof(HasNoTasks)); Raise(nameof(RemainingTasks)); Raise(nameof(TaskPreview)); }
    private void AddTask()
    {
        var title = NewTask.Trim();
        if (title.Length == 0) return;
        if (Tasks.Count >= 200) { SetStatus("任务已达 200 项，请先整理现有任务。", true); return; }
        var item = new TaskViewModel(Guid.NewGuid().ToString("N"), title, false);
        item.PropertyChanged += TaskChanged;
        Tasks.Add(item);
        NewTask = "";
        MarkContentDirty();
        UpdateTasks();
    }

    private void MarkContentDirty()
    {
        _contentVersion++;
        if (!ContentSaveError) ContentSaveStatus = "正在保存…";
        SetStatus("正在等待保存…");
        _contentTimer.Stop();
        _contentTimer.Start();
    }

    public async Task FlushAsync()
    {
        _contentTimer.Stop();
        await _contentGate.WaitAsync();
        try
        {
            while (_savedVersion < _contentVersion)
            {
                var version = _contentVersion;
                var content = new WidgetContent
                {
                    Notes = Notes,
                    Tasks = Tasks.Select(x => new LocalTask { Id = x.Id, Title = x.Title, IsCompleted = x.IsCompleted }).ToList()
                };
                await _services.SaveContentAsync(content);
                _savedVersion = version;
            }
            SetStatus("已保存到本机 JSON");
            ContentSaveError = false; ContentSaveStatus = "✓ 已保存到本机";
        }
        catch (Exception ex)
        {
            ContentSaveError = true; ContentSaveStatus = "保存失败：" + ex.Message;
            throw;
        }
        finally { _contentGate.Release(); }
    }

    public void SetStatus(string text, bool error = false) { Status = text; IsError = error; }

    private readonly HashSet<object> _monitorOwners = [];
    public void SetMonitoringFor(object owner, bool active)
    {
        if (active) _monitorOwners.Add(owner); else _monitorOwners.Remove(owner);
        SetMonitoring(_monitorOwners.Count > 0);
    }
    public WidgetViewModel DesktopWidget(WidgetKind kind) => new(new WidgetPlacement { Id = "desktop-" + kind, Kind = kind }, _metricCards.GetValueOrDefault(kind));
    public void SetMonitoring(bool active)
    {
        if (_disposed) return;
        _monitoringRequested = active;
        if (active)
        {
            UpdateClock();
            _clockTimer.Start();
            if (HasMetrics()) { _metricsTimer.Start(); _ = SampleAsync(); }
        }
        else { _metricsTimer.Stop(); _clockTimer.Stop(); }
    }

    private async Task SampleAsync()
    {
        if (_disposed || _sampleRunning) return;
        _sampleRunning = true;
        try
        {
            var extended = _services.Settings.Desktop.Widgets.Any(x => x.Enabled && x.Kind is WidgetKind.Gpu or WidgetKind.GpuMemory or WidgetKind.Temperature or WidgetKind.Fan) ||
                Widgets.Any(x => x.Kind is WidgetKind.Gpu or WidgetKind.GpuMemory or WidgetKind.Temperature or WidgetKind.Fan);
            var result = await Task.Run(() => (_metrics.Read(), extended ? _hardware.Read() : null));
            var sample = result.Item1;
            if (_disposed) return;
            _metricCards[WidgetKind.Cpu].Update(sample.CpuPercent is double cpu ? $"{cpu:0}%" : "—", "每 2 秒更新 · 实时", sample.CpuPercent ?? 0);
            _metricCards[WidgetKind.Memory].Update($"{sample.MemoryPercent:0}%", $"{GiB(sample.UsedMemory):0.0} / {GiB(sample.TotalMemory):0.0} GB", sample.MemoryPercent);
            _metricCards[WidgetKind.Network].Update(Rate(sample.DownloadBytesPerSecond), $"↑ {Rate(sample.UploadBytesPerSecond)} 上传", Math.Min(sample.DownloadBytesPerSecond / 1024, 100000));
            var diskPercent = sample.DiskTotal > 0 ? 100.0 * sample.DiskUsed / sample.DiskTotal : 0;
            _metricCards[WidgetKind.Storage].Update($"{diskPercent:0}%", $"{sample.DiskName} · {GiB((ulong)sample.DiskUsed):0} / {GiB((ulong)sample.DiskTotal):0} GB", diskPercent);
            if (result.Item2 is { } hardware)
            {
                if (hardware.GpuPercent is double gpu) _metricCards[WidgetKind.Gpu].Update($"{gpu:0}%", hardware.GpuName == "GPU" ? "最忙 GPU 引擎 · Windows 性能计数器" : hardware.GpuName, gpu);
                else _metricCards[WidgetKind.Gpu].Unavailable("GPU 驱动未提供使用率，或正在等待首个样本");
                if (hardware.GpuMemoryUsed is double used)
                    _metricCards[WidgetKind.GpuMemory].Update($"{used / 1073741824:0.0} GB", hardware.GpuMemoryTotal is double total ? $"共 {total / 1073741824:0.0} GB · {hardware.GpuName}" : "所有适配器的专用显存合计", hardware.GpuMemoryTotal is > 0 ? used / hardware.GpuMemoryTotal.Value * 100 : used / 1073741824);
                else _metricCards[WidgetKind.GpuMemory].Unavailable("显卡驱动未提供专用显存数据");
                foreach (var (kind, type, unit) in new[] { (WidgetKind.Temperature, "Temperature", "°C"), (WidgetKind.Fan, "Fan", "RPM") })
                {
                    var sensors = hardware.Sensors.Where(x => x.Type == type).OrderByDescending(x => x.Value).ToArray();
                    if (sensors.Length == 0) _metricCards[kind].Unavailable(hardware.SensorStatus);
                    else _metricCards[kind].Update($"{sensors[0].Value:0} {unit}", string.Join("\n", sensors.Select(x => $"{x.Name} · {x.Value:0} {unit} · {x.Source}")), sensors[0].Value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.NetworkInformation.NetworkInformationException or InvalidOperationException)
        { if (!_disposed) SetStatus("部分系统数据暂不可用：" + ex.Message, true); }
        finally { _sampleRunning = false; }
    }

    private static double GiB(ulong bytes) => bytes / 1073741824.0;
    private static string Rate(double bytes) => bytes >= 1048576 ? $"{bytes / 1048576:0.0} MB/s" : $"{bytes / 1024:0.0} KB/s";

    public void SetColumns(int columns)
    {
        if (Columns == columns) return;
        if (IsEditing) CaptureDraft();
        Columns = columns;
        _undo.Clear(); _redo.Clear();
        LoadLayout();
    }

    private void LoadLayout()
    {
        var workspace = _draft ?? _services.Workspace;
        var layout = workspace.Layouts.TryGetValue(Columns.ToString(), out var stored) ? stored :
            GridLayout.Adapt(workspace.Layouts.Values.FirstOrDefault() ?? [], Columns);
        Apply(layout);
    }

    private void Apply(IEnumerable<WidgetPlacement> placements)
    {
        Widgets.Clear();
        foreach (var p in placements) Widgets.Add(new(p.Copy(), _metricCards.GetValueOrDefault(p.Kind)));
        if (_metricsTimer is not null)
        {
            if (_monitoringRequested && HasMetrics()) _metricsTimer.Start();
            else _metricsTimer.Stop();
        }
    }

    private List<WidgetPlacement> Snapshot() => Widgets.Select(x => x.Placement.Copy()).ToList();
    private bool HasMetrics() => Widgets.Any(x => x.IsMetric) || _services.Settings.Desktop.Widgets.Any(x => x.Enabled && _metricCards.ContainsKey(x.Kind));
    private void CaptureDraft() { if (_draft is not null) _draft.Layouts[Columns.ToString()] = Snapshot(); }
    public void BeginEdit() { _draft = _services.CopyWorkspace(); IsEditing = true; _undo.Clear(); _redo.Clear(); SetStatus("拖动卡片右上角移动位置；保存后生效。"); }
    public void CancelEdit() { _draft = null; IsEditing = false; LoadLayout(); SetStatus("已取消布局修改"); }
    public async Task SaveLayoutAsync()
    {
        if (_draft is null) return;
        CaptureDraft();
        await _services.SaveWorkspaceAsync(_draft);
        _draft = null;
        IsEditing = false;
        SetStatus("布局已保存到工作空间 JSON");
    }

    private void Checkpoint()
    {
        if (_undo.Count >= 20)
        {
            var retained = _undo.Reverse().Skip(1).ToArray();
            _undo.Clear();
            foreach (var item in retained) _undo.Push(item);
        }
        _undo.Push(Snapshot());
        _redo.Clear();
    }
    public void MoveWidget(string id, int column, int row)
    {
        if (!IsEditing) return;
        Checkpoint();
        Apply(GridLayout.Move(Snapshot(), id, column, row, Columns));
    }
    public void RemoveWidget(string id) { if (!IsEditing) return; Checkpoint(); Apply(Snapshot().Where(x => x.Id != id)); }
    public void AddWidget(WidgetKind kind)
    {
        if (!IsEditing || Widgets.Any(x => x.Kind == kind)) return;
        Checkpoint();
        var item = Defaults.Workspace().Layouts["12"].FirstOrDefault(x => x.Kind == kind)?.Copy() ??
            Defaults.At(kind.ToString().ToLowerInvariant(), kind, 0, Snapshot().Select(x => x.Row + x.RowSpan).DefaultIfEmpty(0).Max(), Math.Min(3, Columns), 1);
        Apply(GridLayout.Adapt(Snapshot().Append(item), Columns));
    }
    public void Undo() { if (!IsEditing || _undo.Count == 0) return; _redo.Push(Snapshot()); Apply(_undo.Pop()); }
    public void Redo() { if (!IsEditing || _redo.Count == 0) return; _undo.Push(Snapshot()); Apply(_redo.Pop()); }

    public void Dispose()
    {
        _disposed = true;
        _services.WorkspaceActivated -= OnWorkspaceActivated;
        _services.Workspaces.Changed -= OnWorkspaceMetadataChanged;
        if (_library is not null) _library.Changed -= OnWorkspaceMetadataChanged;
        _contentTimer.Stop(); _metricsTimer.Stop(); _clockTimer.Stop();
        _hardware.Dispose();
        foreach (var task in Tasks) task.PropertyChanged -= TaskChanged;
    }

    private void OnWorkspaceActivated(object? sender, EventArgs e)
    {
        _draft = null; IsEditing = false; _undo.Clear(); _redo.Clear(); LoadLayout();
        Raise(nameof(WorkspaceName));
        SetStatus("当前工作空间：" + _services.Workspace.Name);
    }
    private void OnWorkspaceMetadataChanged()
    {
        if (_disposed) return;
        _uiDispatcher.BeginInvoke(async () => { if (_disposed) return; Raise(nameof(WorkspaceName)); await RefreshWorkspaceLinksAsync(); });
    }
    private async Task RefreshWorkspaceLinksAsync()
    {
        if (_library is null) return;
        try
        {
            if (_services.Workspace.ResourceIds.Count > 0) await _library.EnsureLoadedAsync(CancellationToken.None);
            if (_disposed) return;
            var byId = _library.Resources.Where(x => x.ResourceId is not null).ToDictionary(x => x.ResourceId!, StringComparer.Ordinal);
            var entries = _services.Workspace.ResourceIds.Select(id => byId.GetValueOrDefault(id)).OfType<SearchItem>().Take(4).ToArray();
            QuickLinks.Clear();
            if (entries.Length == 0)
            {
                AddQuickLink("用户目录", "文件与日常资料", "\uE77B", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                AddQuickLink("文档", "你的文档入口", "\uE8A5", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                AddQuickLink("桌面", "Windows 桌面文件", "\uE7F4", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                AddQuickLink("项目目录", "当前启动位置", "\uE943", Environment.CurrentDirectory);
            }
            else foreach (var entry in entries) QuickLinks.Add(new(entry.Name, entry.TypeLabel + " · " + _services.Workspace.Name, entry.Glyph, entry.Target,
                entry.Kind == SearchKind.Model ? "resource.reveal" : entry.Command));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { if (!_disposed) SetStatus("工作空间资源暂不可读：" + ex.Message, true); }
    }
}

public sealed record QuickLink(string Title, string Description, string Glyph, string Path, string Command = "resource.open");

public sealed class TaskViewModel(string id, string title, bool isCompleted) : ObservableObject
{
    private bool _isCompleted = isCompleted;
    private string _title = title;
    public string Id { get; } = id;
    public string Title { get => _title; set => Set(ref _title, value); }
    public bool IsCompleted { get => _isCompleted; set => Set(ref _isCompleted, value); }
}

public sealed class MetricViewModel(string title, string subtitle, string color) : ObservableObject
{
    private string _value = "—", _detail = subtitle;
    private double[] _history = [];
    public string Title { get; } = title;
    public string Color { get; } = color;
    public string Value { get => _value; private set => Set(ref _value, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public double[] History { get => _history; private set => Set(ref _history, value); }
    public void Update(string value, string detail, double sample)
    {
        Value = value; Detail = detail;
        History = History.TakeLast(59).Append(sample).ToArray();
    }
    public void Unavailable(string detail) { Value = "—"; Detail = detail; History = []; }
}

public sealed class WidgetViewModel(WidgetPlacement placement, MetricViewModel? metric)
{
    public WidgetPlacement Placement { get; } = placement;
    public string Id => Placement.Id;
    public WidgetKind Kind => Placement.Kind;
    public MetricViewModel? Metric { get; } = metric;
    public bool IsMetric => Metric is not null;
    public string Title => Kind switch
    {
        WidgetKind.Cpu => "CPU", WidgetKind.Memory => "内存", WidgetKind.Network => "网络", WidgetKind.Storage => "存储",
        WidgetKind.Gpu => "GPU", WidgetKind.GpuMemory => "显存", WidgetKind.Temperature => "温度", WidgetKind.Fan => "风扇转速",
        WidgetKind.QuickAccess => "快捷访问", WidgetKind.Tasks => "待办", WidgetKind.Notes => "随手记", _ => "此时此刻"
    };
}

public sealed class PluginCardViewModel(DesktopPluginPlacement placement, DashboardViewModel owner)
{
    public DesktopPluginPlacement Placement { get; } = placement;
    public string Name => Placement.Name;
    public string Description => Placement.Description;
    public string Glyph => Placement.Glyph;
    public bool Enabled => Placement.Enabled;
    public WidgetViewModel Preview { get; } = owner.DesktopWidget(placement.Kind);
}
