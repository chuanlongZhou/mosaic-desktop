using System.Text.Json;
using Microsoft.Win32;
using Mosaic.Core.Models;
using Mosaic.Core.Search;
using Mosaic.Platform.Windows;
using Mosaic.Storage.Json;

namespace Mosaic.Host.Services;

public sealed class SearchRuntime : IDisposable
{
    private readonly AppServices _services;
    private readonly EverythingClient _everything;
    private readonly JsonFileIndex _index;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Threading.Timer _refresh;
    private readonly System.Threading.Timer _release;
    private SearchSettings _options;
    private Task<IReadOnlyList<SearchItem>>? _apps;
    private Task? _scan;
    private CancellationTokenSource? _scanCancellation;
    private bool _rescan, _active, _disposed, _suspended;
    private string _signature = "";
    private string _indexStatus = "尚未选择文件夹，仅搜索应用、命令与已登记资源";
    public SearchLibrary Library { get; }
    public string IndexStatus => Volatile.Read(ref _indexStatus);
    public bool IsScanning { get { lock (_sync) return _scan is not null; } }
    public event Action? Changed;

    public SearchRuntime(AppServices services, EverythingClient everything)
    {
        _services = services; _everything = everything;
        _options = services.Settings.Search.Copy();
        _index = new(services.Store.Root);
        Library = new(services.Store, services.Profile.ProfileId);
        Library.Changed += OnMetadataChanged;
        services.Workspaces.Changed += OnMetadataChanged;
        _refresh = new(_ => RequestRebuild(), null, Timeout.Infinite, Timeout.Infinite);
        _release = new(_ => { lock (_sync) { if (!_active) _index.ReleaseHot(); } }, null, Timeout.Infinite, Timeout.Infinite);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        services.SettingsChanged += OnSettingsChanged;
        Configure();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Configure();
    private void OnMetadataChanged() => Changed?.Invoke();
    private void Configure()
    {
        var options = _services.Settings.Search.Copy();
        var signature = $"{options.Enabled}|{options.IndexEnabled}|{options.HotEntryLimit}|{options.HotMemoryMiB}|{options.CacheMiB}|" + string.Join("\n", options.Roots);
        lock (_sync)
        {
            _options = options;
            if (_signature == signature || _disposed) return;
            _signature = signature;
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
            _scanCancellation?.Cancel();
            _index.ReleaseHot();
            if (options.Enabled && options.IndexEnabled)
            {
                foreach (var root in options.Roots)
                {
                    try
                    {
                        FileDiscovery.ValidateRoot(root, _services.Store.Root);
                        var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 8192 };
                        watcher.Changed += FileChanged; watcher.Created += FileChanged; watcher.Deleted += FileChanged; watcher.Renamed += FileChanged;
                        watcher.Error += (_, _) => QueueRefresh();
                        watcher.EnableRaisingEvents = true;
                        _watchers.Add(watcher);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    { SetIndexStatus("目录不可用：" + ex.Message); }
                }
            }
            if (!options.Enabled || !options.IndexEnabled || options.Roots.Count == 0)
                SetIndexStatus(options.Roots.Count == 0 ? "未选择文件夹 · 应用和命令可用" : "本地文件索引已关闭");
        }
        RequestRebuild();
    }

    public void Activate() { lock (_sync) { _active = true; _release.Change(Timeout.Infinite, Timeout.Infinite); } }
    public void Deactivate() { lock (_sync) { _active = false; if (!_disposed) _release.Change(30000, Timeout.Infinite); } }
    public void CancelRebuild() { lock (_sync) { _rescan = false; _scanCancellation?.Cancel(); } }
    private void FileChanged(object sender, FileSystemEventArgs e)
    {
        if (sender is FileSystemWatcher watcher && FileDiscovery.IgnoreChange(watcher.Path, e.FullPath) &&
            (e is not RenamedEventArgs renamed || FileDiscovery.IgnoreChange(watcher.Path, renamed.OldFullPath))) return;
        QueueRefresh();
    }
    private void QueueRefresh()
    {
        lock (_sync)
        {
            if (_disposed || _suspended) return;
            if (_scan is not null) { _rescan = true; return; }
            _refresh.Change(3000, Timeout.Infinite);
        }
    }

    public void RequestRebuild()
    {
        lock (_sync)
        {
            if (_disposed || _suspended || !_options.Enabled || !_options.IndexEnabled || _options.Roots.Count == 0) return;
            if (_scan is not null) { _rescan = true; return; }
            _scanCancellation?.Dispose();
            _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var token = _scanCancellation.Token;
            var options = _options.Copy();
            SetIndexStatus("正在建立文件索引…");
            _scan = Task.Run(async () =>
            {
                try
                {
                    foreach (var root in options.Roots) FileDiscovery.ValidateRoot(root, _services.Store.Root);
                    await _index.LoadAsync(options, token).ConfigureAwait(false);
                    await _index.RebuildAsync(options, FileDiscovery.Enumerate, SetIndexStatus, token).ConfigureAwait(false);
                    SetIndexStatus($"索引就绪 · {_index.State.TotalCount:N0} 项" + (_index.State.Partial ? " · 仅覆盖部分目录" : ""));
                }
                catch (OperationCanceledException) { SetIndexStatus("扫描已取消，保留上一代可用索引"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                { SetIndexStatus("索引暂不可用：" + ex.Message); }
                finally
                {
                    lock (_sync)
                    {
                        _scan = null;
                        if (!_active) _index.ReleaseHot();
                        if (_rescan && !_disposed) { _rescan = false; _refresh.Change(3000, Timeout.Infinite); }
                    }
                    Changed?.Invoke();
                }
            });
        }
    }

    public async Task SearchAsync(SearchQuery query, IProgress<SearchUpdate> progress, CancellationToken token)
    {
        SearchSettings options;
        Task<IReadOnlyList<SearchItem>> apps;
        lock (_sync)
        {
            options = _options.Copy();
            apps = _apps ??= ApplicationDiscovery.DiscoverAsync(_lifetime.Token);
        }
        var commands = Commands().ToArray();
        var dock = _services.Settings.Dock.Items.ToArray();
        var initial = new TopResults();
        foreach (var item in commands) initial.Add(SearchRanking.Match(item, query));
        progress.Report(new(initial.Sorted(), "正在发现应用与资源…", true, false));
        await Library.EnsureLoadedAsync(token).ConfigureAwait(false);
        var registered = Library.Resources;
        var recent = Library.Recent;
        var metadata = registered.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var history = recent.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var workspaceResources = _services.Workspace.ResourceIds.ToHashSet(StringComparer.Ordinal);
        SearchItem Enrich(SearchItem item) => item with
        { ResourceId = metadata.GetValueOrDefault(item.Key)?.ResourceId, IsFavorite = metadata.GetValueOrDefault(item.Key)?.IsFavorite ?? false,
          Tags = metadata.GetValueOrDefault(item.Key)?.Tags ?? [], LastUsedUtc = history.GetValueOrDefault(item.Key)?.LastUsedUtc,
          IsInCurrentWorkspace = workspaceResources.Contains(metadata.GetValueOrDefault(item.Key)?.ResourceId ?? "") };
        var sources = new List<ISearchSource>
        {
            new Source("资源与命令", (_, ct) =>
            {
                ct.ThrowIfCancellationRequested(); var top = new TopResults();
                foreach (var item in commands.Concat(registered).Concat(recent)) top.Add(SearchRanking.Match(Enrich(item), query));
                return Task.FromResult(new SearchSourceResult("资源与命令", top.Sorted(), Library.Warning ?? "资源与命令"));
            }),
            new Source("Dock", (_, ct) => Task.Run(() =>
            {
                var top = new TopResults();
                foreach (var item in dock)
                {
                    ct.ThrowIfCancellationRequested();
                    if (item.Command is not ("resource.open" or "app.builtin" or "app.packaged")) continue;
                    var kind = item.Command == "resource.open" ? SearchIdentity.FileKind(item.Target, Directory.Exists(item.Target)) : SearchKind.App;
                    top.Add(SearchRanking.Match(Enrich(new(item.Title, kind, item.Command, item.Target, "Dock")), query));
                }
                return new SearchSourceResult("Dock", top.Sorted(), "");
            }, ct)),
            new Source("应用", async (_, ct) =>
            {
                var found = await apps.WaitAsync(ct).ConfigureAwait(false); var top = new TopResults();
                foreach (var item in found) { ct.ThrowIfCancellationRequested(); top.Add(SearchRanking.Match(Enrich(item), query)); }
                return new("应用", top.Sorted(), $"已发现 {found.Count} 个应用入口");
            })
        };
        if (options.IndexEnabled && options.Roots.Count > 0)
        {
            var loaded = _index.State.HotLoaded ? Task.CompletedTask : Task.Run(() => _index.LoadAsync(options, token), token);
            sources.Add(new Source("本地热索引", async (_, ct) => { await loaded.WaitAsync(ct).ConfigureAwait(false); return await Task.Run(() => _index.SearchHot(query, options, ct), ct).ConfigureAwait(false); }));
            sources.Add(new Source("本地分块", async (_, ct) => { await loaded.WaitAsync(ct).ConfigureAwait(false); return await _index.SearchColdAsync(query, options, ct).ConfigureAwait(false); }));
        }
        sources.Add(new Source("Everything", (_, ct) => _everything.SearchAsync(query, options, ct)));
        await new SearchCoordinator(sources).SearchAsync(query, progress, token).ConfigureAwait(false);
    }

    private IEnumerable<SearchItem> Commands()
    {
        yield return new("打开资源库", SearchKind.Command, "library.open", "", "Mosaic", "resource library 资源 收藏 标签 集合");
        yield return new("管理工作空间", SearchKind.Command, "workspaces.open", "", "Mosaic", "workspace 工作空间 切换");
        foreach (var workspace in _services.Workspaces.Items)
            yield return new("切换至 " + workspace.Name, SearchKind.Workspace, "workspace.switch", workspace.Id, "Mosaic 工作空间", workspace.Name);
        yield return new("打开工作台", SearchKind.Command, "dashboard.open", "", "Mosaic", "dashboard widgets 工作台 组件");
        yield return new("Mosaic 设置", SearchKind.Setting, "settings.open", "", "Mosaic", "settings 定制 模块");
        yield return new("搜索设置与目录", SearchKind.Setting, "search.settings", "", "Mosaic", "index scope 索引 everything");
        yield return new("重建文件索引", SearchKind.Command, "search.rebuild", "", "Mosaic", "reindex refresh 扫描 刷新");
        yield return new("打开配置目录", SearchKind.Command, "resource.open", _services.Store.Root, "Mosaic", "json config 数据");
    }

    private void SetIndexStatus(string value) { Volatile.Write(ref _indexStatus, value); Changed?.Invoke(); }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) { lock (_sync) { _suspended = true; _scanCancellation?.Cancel(); _refresh.Change(Timeout.Infinite, Timeout.Infinite); } }
        else if (e.Mode == PowerModes.Resume) { lock (_sync) _suspended = false; RequestRebuild(); }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true; _lifetime.Cancel(); _scanCancellation?.Cancel();
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear(); _refresh.Dispose(); _release.Dispose();
        }
        _services.SettingsChanged -= OnSettingsChanged;
        _services.Workspaces.Changed -= OnMetadataChanged;
        Library.Changed -= OnMetadataChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
    private sealed class Source(string name, Func<SearchQuery, CancellationToken, Task<SearchSourceResult>> search) : ISearchSource
    { public string Name => name; public Task<SearchSourceResult> SearchAsync(SearchQuery query, CancellationToken token) => search(query, token); }
}
