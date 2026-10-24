using System.Collections.ObjectModel;
using System.Windows.Threading;
using Mosaic.Core.Commands;
using Mosaic.Core.Models;
using Mosaic.Core.Search;
using Mosaic.Host.Services;

namespace Mosaic.Host.ViewModels;

public sealed class PaletteViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly SearchRuntime _runtime;
    private readonly ResourceActions _actions;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _queryCancellation;
    private int _generation, _selectionGeneration;
    private bool _open, _disposed, _isSearching, _partial, _isError, _busy;
    private string _query = "", _status = "搜索应用、文件与操作", _tags = "", _availability = "";
    private SearchFilter _filter;
    private RankedResult? _selected;
    private Task _pendingSave = Task.CompletedTask;
    private Task _pendingMetadata = Task.CompletedTask;
    public bool IsComposing { get; set; }
    public ObservableCollection<RankedResult> Results { get; } = [];
    public ObservableCollection<FilterOption> Filters { get; } = new(Enum.GetValues<SearchFilter>().Select(x => new FilterOption(x)));
    public RelayCommand FilterCommand { get; }
    public string Query { get => _query; set { if (Set(ref _query, value)) RequestSearch(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string IndexStatus => _runtime.IndexStatus;
    public bool IsSearching { get => _isSearching; private set { if (Set(ref _isSearching, value)) Raise(nameof(IsEmpty)); } }
    public bool Partial { get => _partial; private set => Set(ref _partial, value); }
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public bool IsEmpty => Results.Count == 0 && !IsSearching;
    public bool HasSelection => Selected is not null;
    public bool CanReveal => Selected?.Item.Command == "resource.open";
    public string FavoriteLabel => Selected?.Item.IsFavorite == true ? "取消收藏" : "收藏资源";
    public string OpenLabel => Selected?.Item.Kind == SearchKind.Model ? "打开所在位置" : "打开 / 执行";
    public string Availability { get => _availability; private set => Set(ref _availability, value); }
    public string TagsText { get => _tags; set => Set(ref _tags, value); }
    public RankedResult? Selected
    {
        get => _selected;
        set
        {
            var changedKey = _selected?.Item.Key != value?.Item.Key;
            if (!Set(ref _selected, value)) return;
            if (changedKey) TagsText = string.Join(", ", value?.Item.Tags ?? []);
            foreach (var name in new[] { nameof(HasSelection), nameof(CanReveal), nameof(FavoriteLabel), nameof(OpenLabel) }) Raise(name);
            if (changedKey) _ = LoadDetailsAsync(value);
        }
    }

    public PaletteViewModel(AppServices services, SearchRuntime runtime)
    {
        _services = services; _runtime = runtime; _dispatcher = Dispatcher.CurrentDispatcher;
        _actions = new(services, runtime.Library);
        _debounce = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RunSearchAsync(false); };
        FilterCommand = new(parameter =>
        {
            if (parameter is not FilterOption option) return;
            _filter = option.Value;
            foreach (var item in Filters) item.IsSelected = item.Value == _filter;
            RequestSearch();
        });
        Filters[0].IsSelected = true;
        _runtime.Changed += OnRuntimeChanged;
    }

    public void Activate()
    {
        _open = true; _runtime.Activate();
        _ = RunSearchAsync(false);
    }
    public void Deactivate()
    {
        _open = false; SuspendQueries(); _runtime.Deactivate();
        Results.Clear(); Selected = null; Raise(nameof(IsEmpty));
    }
    public void SuspendQueries()
    {
        _generation++;
        _debounce.Stop();
        _queryCancellation?.Cancel();
        IsSearching = false;
    }
    public void RequestSearch()
    {
        SuspendQueries();
        if (_open && !IsComposing && !_disposed) { IsSearching = true; _debounce.Start(); }
    }
    public Task ContinueAsync() => RunSearchAsync(true);

    private async Task RunSearchAsync(bool continueSearch)
    {
        if (!_open || IsComposing || _disposed) return;
        SuspendQueries();
        var generation = _generation;
        _queryCancellation?.Dispose();
        _queryCancellation = new();
        var token = _queryCancellation.Token;
        IsSearching = true; IsError = false;
        var progress = new Progress<SearchUpdate>(update =>
        {
            if (_disposed || !_open || generation != _generation || token.IsCancellationRequested) return;
            var previous = Selected?.Item.Key;
            Results.Clear();
            foreach (var result in update.Results) Results.Add(result);
            Selected = Results.FirstOrDefault(x => x.Item.Key == previous) ?? Results.FirstOrDefault();
            Status = update.Status;
            IsSearching = update.IsSearching;
            Partial = update.Partial;
            Raise(nameof(IsEmpty));
        });
        var query = new SearchQuery(Query, _filter, _services.Settings.Search.IgnoreAccents, continueSearch);
        try { await Task.Run(() => _runtime.SearchAsync(query, progress, token), token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { if (generation == _generation) SetStatus("搜索失败：" + ex.Message, true); }
        finally { if (generation == _generation && !_disposed) IsSearching = false; }
    }

    private async Task LoadDetailsAsync(RankedResult? result)
    {
        var generation = ++_selectionGeneration;
        if (result is null) { Availability = ""; return; }
        if (result.Item.Command != "resource.open") { Availability = result.Item.Kind == SearchKind.App ? "由 Windows 应用启动接口打开" : "Mosaic 内置操作"; return; }
        Availability = "正在检查资源状态…";
        var description = await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(result.Item.Target)) return "本地文件夹 · 可打开";
                var file = new FileInfo(result.Item.Target);
                return file.Exists ? $"{file.Length / 1048576.0:0.##} MB · 修改于 {file.LastWriteTime:g}" : "资源不存在或磁盘离线 · 已保存的标签仍保留";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "当前无法访问这个资源"; }
        });
        if (!_disposed && generation == _selectionGeneration) Availability = description;
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        var index = Selected is null ? 0 : Results.IndexOf(Selected);
        Selected = Results[Math.Clamp(index + delta, 0, Results.Count - 1)];
    }

    public Task<bool> ExecuteSelectedAsync(bool reveal = false)
    {
        if (Busy) return Task.FromResult(false);
        var operation = ExecuteSelectedCoreAsync(reveal);
        _pendingSave = operation;
        return operation;
    }

    private async Task<bool> ExecuteSelectedCoreAsync(bool reveal)
    {
        if (Selected is null || Busy) return false;
        var item = Selected.Item;
        Busy = true;
        try
        {
            SetStatus(await _actions.OpenAsync(item, reveal));
            return true;
        }
        catch (Exception ex) { SetStatus(ex.Message, true); return false; }
        finally { Busy = false; }
    }

    public Task SaveMetadataAsync(bool toggleFavorite) => Busy ? Task.CompletedTask : _pendingSave = _pendingMetadata = SaveMetadataCoreAsync(toggleFavorite);

    private async Task SaveMetadataCoreAsync(bool toggleFavorite)
    {
        if (Selected is null || Busy) return;
        Busy = true;
        try
        {
            var item = Selected.Item;
            var tags = TagsText.Split([',', '，'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var save = _actions.SaveMetadataAsync(item, toggleFavorite ? !item.IsFavorite : item.IsFavorite, toggleFavorite ? item.Tags : tags);
            var saved = await save;
            if (Selected?.Item.Key == item.Key) { Selected = Selected with { Item = saved }; TagsText = string.Join(", ", saved.Tags); }
            SetStatus("收藏与标签已保存到资源 JSON");
            RequestSearch();
        }
        catch (Exception ex) { SetStatus("保存失败：" + ex.Message, true); }
        finally { Busy = false; }
    }

    public Task PinToDockAsync() => Busy ? Task.CompletedTask : _pendingSave = _pendingMetadata = PinToDockCoreAsync();

    private async Task PinToDockCoreAsync()
    {
        if (Selected is null || Busy) return;
        Busy = true;
        try
        {
            await _actions.PinToDockAsync(Selected.Item);
            SetStatus("已固定到 Dock");
        }
        catch (Exception ex) { SetStatus("固定失败：" + ex.Message, true); }
        finally { Busy = false; }
    }

    public Task AddToWorkspaceAsync() => Busy ? Task.CompletedTask : _pendingSave = _pendingMetadata = OrganizeAsync(false);
    public Task OpenLibraryAsync() => Busy ? Task.CompletedTask : _pendingSave = OrganizeAsync(true);
    private async Task OrganizeAsync(bool openLibrary)
    {
        if (Selected is null) return;
        Busy = true;
        try
        {
            if (openLibrary)
            {
                var registered = await _actions.RegisterAsync(Selected.Item);
                await _services.Commands.ExecuteAsync(new("library.open", registered.ResourceId));
            }
            else
            {
                await _actions.AddToWorkspaceAsync(Selected.Item, _services.Workspace.Id);
                SetStatus("已加入当前工作空间：" + _services.Workspace.Name);
            }
        }
        catch (Exception ex) { SetStatus("整理失败：" + ex.Message, true); }
        finally { Busy = false; }
    }

    // Track the complete action: pinning writes both resource metadata and Dock settings.
    // Action failures are reported in the UI without modifying the committed snapshot.
    public Task FlushAsync() => _pendingSave;
    public Task FlushMetadataAsync() => _pendingMetadata;
    public void SetStatus(string text, bool error = false) { Status = text; IsError = error; }
    private void OnRuntimeChanged()
    {
        if (_disposed) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            Raise(nameof(IndexStatus));
            if (_open && !_runtime.IsScanning) RequestSearch();
        });
    }
    public void Dispose()
    {
        _disposed = true; SuspendQueries(); _queryCancellation?.Dispose();
        _runtime.Changed -= OnRuntimeChanged;
    }
}

public sealed class FilterOption(SearchFilter value) : ObservableObject
{
    private bool _selected;
    public SearchFilter Value { get; } = value;
    public string Label => Value switch { SearchFilter.All => "全部", SearchFilter.Apps => "应用", SearchFilter.Files => "文件", SearchFilter.Folders => "文件夹", SearchFilter.Models => "模型", _ => "命令" };
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}
