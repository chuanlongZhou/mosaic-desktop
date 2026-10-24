using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mosaic.Core.Models;
using Mosaic.Core.Search;
using Mosaic.Host.Services;
using Mosaic.Storage.Json;

namespace Mosaic.Host.ViewModels;

public sealed record ResourceTypeOption(SearchKind? Kind, string Label);
public sealed record LibraryNavigation(string Id, string Title, string Glyph);

public sealed class LibraryViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly SearchRuntime _runtime;
    private readonly ResourceActions _actions;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _debounce;
    private readonly SemaphoreSlim _previewGate = new(1, 1);
    private CancellationTokenSource? _queryCancellation, _previewCancellation;
    private SearchItem[] _matches = [];
    private SearchItem? _selected;
    private CollectionDocument? _targetCollection;
    private WorkspaceSummary? _targetWorkspace;
    public CollectionDocument? TargetCollection { get => _targetCollection; set => Set(ref _targetCollection, value); }
    public WorkspaceSummary? TargetWorkspace { get => _targetWorkspace; set => Set(ref _targetWorkspace, value); }
    private LibraryNavigation? _navigation;
    private ResourceTypeOption _type;
    private string _query = "", _tagFilter = "", _tags = "", _status = "正在读取资源元数据…", _information = "", _previewText = "", _viewMode;
    private BitmapSource? _previewImage;
    private bool _busy, _loading, _disposed, _error, _active = true;
    private int _page, _generation;
    private Task _pendingOperation = Task.CompletedTask;
    private Task _pendingMetadata = Task.CompletedTask;
    private string? _requestedSelection;
    public CollectionRepository Collections { get; }
    public ObservableCollection<SearchItem> Items { get; } = [];
    private SearchItem[] _selection = [];
    public void SetSelection(IEnumerable<SearchItem> selection)
    {
        _selection = selection.Take(100).ToArray(); Raise(nameof(FavoriteLabel)); Raise(nameof(SelectionLabel)); Raise(nameof(SaveTagsLabel)); Raise(nameof(CanRemoveMember));
    }
    public string SelectionLabel => _selection.Length > 1 ? $"已选 {_selection.Length} 项 · 收藏、标签与集合操作作用于所选项" : "Ctrl / Shift 可多选当前页";
    public string SaveTagsLabel => _selection.Length > 1 ? "将这些标签保存到所选项" : "保存标签";
    private SearchItem[] ActionSelection => _selection.Length > 0 ? _selection : Selected is { } item ? [item] : [];
    public ObservableCollection<LibraryNavigation> Navigation { get; } = [];
    public ObservableCollection<CollectionDocument> ManualCollections { get; } = [];
    public ObservableCollection<WorkspaceSummary> Workspaces { get; } = [];
    public IReadOnlyList<ResourceTypeOption> Types { get; } = new[] { new ResourceTypeOption(null, "全部类型") }.Concat(Enum.GetValues<SearchKind>().Select(x => new ResourceTypeOption(x, new SearchItem("", x, "", "", "").TypeLabel))).ToArray();
    public string Query { get => _query; set { if (Set(ref _query, value)) QueueRefresh(); } }
    public string TagFilter { get => _tagFilter; set { if (Set(ref _tagFilter, value)) QueueRefresh(); } }
    public ResourceTypeOption Type { get => _type; set { if (Set(ref _type, value)) QueueRefresh(); } }
    public LibraryNavigation? SelectedNavigation { get => _navigation; set { if (Set(ref _navigation, value)) { Raise(nameof(Title)); Raise(nameof(CanEditCollection)); Raise(nameof(CanRemoveMember)); QueueRefresh(); } } }
    public string Title => SelectedNavigation?.Title ?? "全部资源";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsError { get => _error; private set => Set(ref _error, value); }
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) Raise(nameof(IsIdle)); } }
    public bool IsIdle => !Busy;
    public bool IsLoading { get => _loading; private set => Set(ref _loading, value); }
    public bool IsEmpty => _matches.Length == 0 && !IsLoading;
    public bool HasSelection => Selected is not null;
    public bool CanReveal => Selected?.Command == "resource.open";
    public bool CanEditCollection => SelectedNavigation?.Id.StartsWith("collection:", StringComparison.Ordinal) == true;
    public bool CanRemoveMember => CurrentCollection is { Kind: not CollectionKind.Smart } collection && ActionSelection.Any(x => collection.ResourceIds.Contains(x.ResourceId!));
    public CollectionDocument? CurrentCollection => Collections.Collections.FirstOrDefault(x => "collection:" + x.Id == SelectedNavigation?.Id);
    public string FavoriteLabel => _selection.Length > 1 ? _selection.All(x => x.IsFavorite) ? "取消所选收藏" : "收藏所选" : Selected?.IsFavorite == true ? "取消收藏" : "收藏资源";
    public string OpenLabel => Selected?.Kind == SearchKind.Model ? "打开模型所在位置" : "打开 / 执行";
    public string TagsText { get => _tags; set => Set(ref _tags, value); }
    public string Information { get => _information; private set => Set(ref _information, value); }
    public string PreviewText { get => _previewText; private set => Set(ref _previewText, value); }
    public BitmapSource? PreviewImage { get => _previewImage; private set => Set(ref _previewImage, value); }
    public string ViewMode { get => _viewMode; private set { if (Set(ref _viewMode, value)) { Raise(nameof(IsCards)); Raise(nameof(IsList)); } } }
    public bool IsCards => ViewMode == "cards";
    public bool IsList => !IsCards;
    private int PageSize => IsCards ? 36 : 100;
    public string CountLabel => $"{_matches.Length:N0} 个资源 · 第 {_page + 1} / {Math.Max(1, (_matches.Length + PageSize - 1) / PageSize)} 页";
    public bool CanPrevious => _page > 0;
    public bool CanNext => (_page + 1) * PageSize < _matches.Length;
    public SearchItem? Selected
    {
        get => _selected;
        set
        {
            var changedKey = _selected?.Key != value?.Key;
            if (!Set(ref _selected, value)) return;
            foreach (var name in new[] { nameof(HasSelection), nameof(CanReveal), nameof(FavoriteLabel), nameof(CanRemoveMember), nameof(OpenLabel) }) Raise(name);
            if (changedKey) { TagsText = string.Join(", ", value?.Tags ?? []); _ = LoadPreviewAsync(value); }
        }
    }

    public LibraryViewModel(AppServices services, SearchRuntime runtime)
    {
        _services = services; _runtime = runtime; _actions = new(services, runtime.Library);
        Collections = new(services.Store, services.Profile.ProfileId);
        _viewMode = services.Settings.Library.ViewMode; _type = Types[0];
        _debounce = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RefreshAsync(); };
        runtime.Library.Changed += OnSourcesChanged; services.Workspaces.Changed += OnSourcesChanged; Collections.Changed += OnSourcesChanged;
    }
    public async Task InitializeAsync(string? selectResourceId = null)
    {
        try
        {
            await _runtime.Library.EnsureLoadedAsync(CancellationToken.None);
            await Collections.EnsureLoadedAsync();
            if (_disposed) return;
            RebuildNavigation();
            await RefreshAsync();
            if (selectResourceId is not null) SelectResource(selectResourceId);
            SetStatus(_runtime.Library.Warning ?? Collections.Warning ?? "收藏、标签和资源 ID 与全局搜索共用 · 原文件保留在原位置");
        }
        catch (Exception ex) { if (!_disposed) SetStatus("资源库读取失败：" + ex.Message, true); }
    }
    private void RebuildNavigation()
    {
        var selectedId = SelectedNavigation?.Id ?? "all";
        var collectionTargetId = TargetCollection?.Id;
        var workspaceTargetId = TargetWorkspace?.Id ?? _services.Workspace.Id;
        Navigation.Clear();
        Navigation.Add(new("all", "全部资源", "\uE8A9")); Navigation.Add(new("favorites", "收藏", "\uE734"));
        Navigation.Add(new("current", "当前工作空间", "\uE81E"));
        foreach (var item in Collections.Collections) Navigation.Add(new("collection:" + item.Id, item.Name, item.Kind == CollectionKind.Smart ? "\uE945" : item.Kind == CollectionKind.Project ? "\uE943" : "\uE8B7"));
        foreach (var item in _services.Workspaces.Items) Navigation.Add(new("workspace:" + item.Id, item.Name, "\uE81E"));
        SelectedNavigation = Navigation.FirstOrDefault(x => x.Id == selectedId) ?? Navigation[0];
        ManualCollections.Clear(); foreach (var item in Collections.Collections.Where(x => x.Kind != CollectionKind.Smart)) ManualCollections.Add(item);
        Workspaces.Clear(); foreach (var item in _services.Workspaces.Items) Workspaces.Add(item);
        TargetCollection = ManualCollections.FirstOrDefault(x => x.Id == collectionTargetId) ?? ManualCollections.FirstOrDefault();
        TargetWorkspace = Workspaces.FirstOrDefault(x => x.Id == workspaceTargetId) ?? Workspaces.FirstOrDefault();
    }
    public void SelectResource(string resourceId)
    {
        var index = Array.FindIndex(_matches, x => x.ResourceId == resourceId);
        if (index < 0) return;
        _page = index / PageSize; PublishPage(resourceId);
    }
    public void FocusResource(string resourceId)
    {
        _requestedSelection = resourceId;
        SelectedNavigation = Navigation.FirstOrDefault(x => x.Id == "all");
        Query = ""; TagFilter = ""; Type = Types[0]; QueueRefresh();
    }
    public void FocusWorkspace(string workspaceId)
    {
        _requestedSelection = null; Query = ""; TagFilter = ""; Type = Types[0];
        SelectedNavigation = Navigation.FirstOrDefault(x => x.Id == "workspace:" + workspaceId);
        QueueRefresh();
    }
    private void QueueRefresh()
    {
        if (_disposed || !_active) return;
        _generation++; _queryCancellation?.Cancel(); _debounce.Stop(); _debounce.Start();
    }
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        _debounce.Stop(); _queryCancellation?.Cancel(); _queryCancellation?.Dispose(); _queryCancellation = new();
        var token = _queryCancellation.Token; var generation = ++_generation;
        IsLoading = true; Raise(nameof(IsEmpty));
        try
        {
            var nav = SelectedNavigation?.Id ?? "all"; var collection = CurrentCollection;
            var workspaceId = nav == "current" ? _services.Workspace.Id : nav.StartsWith("workspace:", StringComparison.Ordinal) ? nav[10..] : null;
            var members = workspaceId is null ? null : (await _services.Workspaces.GetCopyAsync(workspaceId)).ResourceIds.ToHashSet(StringComparer.Ordinal);
            token.ThrowIfCancellationRequested();
            var currentMembers = _services.Workspace.ResourceIds.ToHashSet(StringComparer.Ordinal);
            var type = Type.Kind; var tag = TagFilter.Trim(); var query = new SearchQuery(Query, IgnoreAccents: _services.Settings.Search.IgnoreAccents);
            var source = _runtime.Library.Resources;
            var matches = await Task.Run(() => source.Where(item =>
            {
                token.ThrowIfCancellationRequested();
                return (nav != "favorites" || item.IsFavorite) && (!type.HasValue || item.Kind == type) &&
                    (tag.Length == 0 || item.Tags.Any(x => x.Contains(tag, StringComparison.OrdinalIgnoreCase))) &&
                    (collection is null || collection.Includes(item)) && (members is null || members.Contains(item.ResourceId!)) && SearchRanking.Match(item, query) is not null;
            }).Select(x => x with { IsInCurrentWorkspace = currentMembers.Contains(x.ResourceId!) })
              .OrderByDescending(x => query.Normalized.Length > 0 ? SearchRanking.Match(x, query)!.Score : 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ResourceId, StringComparer.Ordinal).ToArray(), token);
            if (_disposed || generation != _generation) return;
            var selection = _requestedSelection ?? Selected?.ResourceId;
            _matches = matches; _page = Math.Min(_page, Math.Max(0, (matches.Length - 1) / PageSize)); PublishPage(selection);
            if (_requestedSelection is { } requested) { SelectResource(requested); _requestedSelection = null; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed && generation == _generation) SetStatus("筛选失败：" + ex.Message, true); }
        finally { if (!_disposed && generation == _generation) { IsLoading = false; Raise(nameof(IsEmpty)); } }
    }
    private void PublishPage(string? selection = null)
    {
        SetSelection([]);
        Items.Clear(); foreach (var item in _matches.Skip(_page * PageSize).Take(PageSize)) Items.Add(item);
        Selected = Items.FirstOrDefault(x => x.ResourceId == selection) ?? Items.FirstOrDefault();
        foreach (var name in new[] { nameof(CountLabel), nameof(CanPrevious), nameof(CanNext), nameof(IsEmpty) }) Raise(name);
    }
    public void MovePage(int delta) { if (delta < 0 && CanPrevious || delta > 0 && CanNext) { _page += delta; PublishPage(); } }
    public Task SetViewModeAsync(string mode) => RunActionAsync(async () =>
    {
        var settings = _services.CopySettings(); settings.Library.ViewMode = mode; await _services.SaveSettingsAsync(settings);
        ViewMode = mode; _page = 0; PublishPage();
    }, "视图已保存");

    private async Task LoadPreviewAsync(SearchItem? item)
    {
        _previewCancellation?.Cancel(); _previewCancellation?.Dispose(); _previewCancellation = new();
        var token = _previewCancellation.Token;
        PreviewImage = null; PreviewText = ""; Information = item is null ? "" : "正在读取基础信息…";
        if (item is null) return;
        var entered = false;
        try
        {
            await _previewGate.WaitAsync(token); entered = true;
            var preview = await Task.Run(() => ResourcePreview.Read(item, token), token);
            if (!_disposed && !token.IsCancellationRequested && Selected?.Key == item.Key)
            { Information = preview.Information; PreviewText = preview.Text; PreviewImage = preview.Image; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        { if (!_disposed && !token.IsCancellationRequested) Information = "预览暂不可用，请使用关联应用打开。"; }
        finally { if (entered) _previewGate.Release(); }
    }
    public Task OpenAsync(bool reveal = false) => Selected is not { } item ? Task.CompletedTask : RunActionAsync(async () => SetStatus(await _actions.OpenAsync(item, reveal)), isMetadata: false);
    public Task SaveMetadataAsync(bool toggleFavorite) => Selected is not { } item ? Task.CompletedTask : RunActionAsync(async () =>
    {
        var targets = ActionSelection; var favorite = !targets.All(x => x.IsFavorite);
        var tags = TagsText.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var target in targets)
        {
            var saved = await _actions.SaveMetadataAsync(target, toggleFavorite ? favorite : target.IsFavorite, toggleFavorite ? target.Tags : tags);
            if (Selected?.Key == saved.Key) TagsText = string.Join(", ", saved.Tags);
        }
    }, "收藏与标签已保存");
    public Task PinToDockAsync() => Selected is not { } item ? Task.CompletedTask : RunActionAsync(() => _actions.PinToDockAsync(item), "已固定到当前 Dock");
    public Task AddToWorkspaceAsync(string workspaceId) => Selected is not { } item ? Task.CompletedTask : RunActionAsync(() => _actions.AddToWorkspaceAsync(item, workspaceId), "已加入工作空间");
    public Task RemoveFromWorkspaceAsync() => Selected?.ResourceId is not { } id ? Task.CompletedTask : RunActionAsync(() => _services.Workspaces.SetResourceAsync(
        SelectedNavigation?.Id.StartsWith("workspace:", StringComparison.Ordinal) == true ? SelectedNavigation.Id[10..] : _services.Workspace.Id, id, false), "已从工作空间移除入口，原文件保留");
    public Task SetCollectionMemberAsync(string collectionId, bool include) => Selected is null ? Task.CompletedTask : RunActionAsync(async () =>
    {
        foreach (var item in ActionSelection) await Collections.SetMemberAsync(collectionId, item.ResourceId!, include);
    }, include ? "所选资源已加入集合" : "已从集合移除，原文件保留");
    public Task AddPathsAsync(IEnumerable<string> paths, bool project = false) => RunActionAsync(async () =>
    {
        foreach (var path in paths.Take(100))
        {
            var full = Path.GetFullPath(path); var directory = Directory.Exists(full);
            if (!directory && !File.Exists(full)) throw new FileNotFoundException("资源已不存在：" + full);
            await _actions.RegisterAsync(new(Path.GetFileName(Path.TrimEndingDirectorySeparator(full)), project ? SearchKind.Project : SearchIdentity.FileKind(full, directory), "resource.open", full, "手动添加"));
        }
    }, "资源已登记，原文件未移动");
    public Task RunActionAsync(Func<Task> action, string? success = null, bool isMetadata = true)
    {
        if (Busy || _disposed) return Task.CompletedTask;
        var operation = RunCoreAsync(action, success);
        if (isMetadata) _pendingMetadata = operation;
        return _pendingOperation = operation;
    }
    private async Task RunCoreAsync(Func<Task> action, string? success)
    {
        Busy = true;
        try { await action(); if (_disposed) return; if (success is not null) SetStatus(success); RebuildNavigation(); if (_active) await RefreshAsync(); }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                SetStatus("操作未全部完成，已保存的变更保留：" + ex.Message, true);
                RebuildNavigation(); if (_active) await RefreshAsync();
            }
        }
        finally { if (!_disposed) Busy = false; }
    }
    public Task FlushAsync() => _pendingOperation;
    public Task FlushMetadataAsync() => _pendingMetadata;
    public void SetActive(bool active)
    {
        _active = active;
        if (!active) { _generation++; _debounce.Stop(); _queryCancellation?.Cancel(); _previewCancellation?.Cancel(); PreviewImage = null; }
        else { RebuildNavigation(); QueueRefresh(); if (Selected is { } item) _ = LoadPreviewAsync(item); }
    }
    public void SetStatus(string text, bool error = false) { Status = text; IsError = error; }
    private void OnSourcesChanged()
    {
        if (_disposed) return;
        _dispatcher.BeginInvoke(() => { if (_disposed || !_active || Busy) return; RebuildNavigation(); QueueRefresh(); });
    }
    public void Dispose()
    {
        _disposed = true; _generation++; _debounce.Stop(); _queryCancellation?.Cancel(); _previewCancellation?.Cancel();
        _runtime.Library.Changed -= OnSourcesChanged; _services.Workspaces.Changed -= OnSourcesChanged; Collections.Changed -= OnSourcesChanged;
        Items.Clear(); PreviewImage = null; PreviewText = "";
    }
}
