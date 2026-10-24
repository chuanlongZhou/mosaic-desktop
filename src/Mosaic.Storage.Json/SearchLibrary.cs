using System.Text.Json;
using Mosaic.Core.Models;
using Mosaic.Core.Search;

namespace Mosaic.Storage.Json;

public sealed class SearchLibrary(JsonDocumentStore store, string profileId)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ResourceShard> _shards = [];
    private SearchHistoryDocument? _history;
    private bool _loaded;
    private SearchItem[] _resources = [], _recent = [];
    private readonly string _prefix = "profiles/" + profileId + "/";
    public string? Warning { get; private set; }
    public IReadOnlyList<SearchItem> Resources => Volatile.Read(ref _resources);
    public IReadOnlyList<SearchItem> Recent => Volatile.Read(ref _recent);
    public event Action? Changed;

    public async Task EnsureLoadedAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_loaded) return;
            _history = await store.LoadAsync(_prefix + "recent.json", MosaicJsonContext.Configured.SearchHistoryDocument, () => new(), token).ConfigureAwait(false);
            var count = 0;
            long estimatedBytes = 0;
            for (var i = 0; i < 64; i++)
            {
                token.ThrowIfCancellationRequested();
                var name = i.ToString("x2");
                var relative = _prefix + "resources/" + name + ".json";
                if (!File.Exists(Path.Combine(store.Root, relative)) && !File.Exists(Path.Combine(store.Root, relative + ".bak"))) continue;
                var shard = await store.LoadAsync(relative, MosaicJsonContext.Configured.ResourceShard, () => new(), token).ConfigureAwait(false);
                if (shard.Resources.Any(x => Shard(x.Id) != name)) throw new InvalidDataException("资源位于错误的分片，原文件已保留。");
                count += shard.Resources.Count;
                estimatedBytes += shard.Resources.Sum(x => 256L + 2L * (x.Name.Length + x.Target.Length + x.Tags.Sum(t => t.Length)));
                if (count > 10000 || estimatedBytes > 32 * 1024 * 1024) { Warning = "元数据超过热读取范围，仅展示部分；原文件保留。"; break; }
                _shards[name] = shard;
            }
            _loaded = true;
            Publish();
        }
        finally { _gate.Release(); }
    }

    public Task<SearchItem> RegisterAsync(SearchItem item, CancellationToken token = default) => UpdateAsync(item, null, null, token);

    public async Task<SearchItem> UpdateAsync(SearchItem item, bool? favorite, IReadOnlyList<string>? tags, CancellationToken token = default)
    {
        await EnsureLoadedAsync(token).ConfigureAwait(false);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Warning is not null) throw new InvalidOperationException("元数据超出容量，请先整理已有记录。");
            var existing = _shards.Values.SelectMany(x => x.Resources).FirstOrDefault(x => SearchIdentity.Key(x.Command, x.Target) == item.Key);
            var promoteProject = existing?.Kind == SearchKind.Folder && item.Kind == SearchKind.Project;
            if (existing is not null && favorite is null && tags is null && !promoteProject) return existing.ToItem();
            if (existing is null && _shards.Values.Sum(x => x.Resources.Count) >= 10000) throw new InvalidOperationException("已登记资源达到 1 万项。");
            var id = existing?.Id ?? Guid.NewGuid().ToString("N");
            var shardId = Shard(id);
            var relative = _prefix + "resources/" + shardId + ".json";
            if (!_shards.TryGetValue(shardId, out var original))
                original = await store.LoadAsync(relative, MosaicJsonContext.Configured.ResourceShard, () => new(), token).ConfigureAwait(false);
            var candidate = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(original, MosaicJsonContext.Configured.ResourceShard), MosaicJsonContext.Configured.ResourceShard)!;
            var resource = candidate.Resources.FirstOrDefault(x => x.Id == id);
            if (resource is null)
            {
                resource = new() { Id = id, Name = item.Name, Kind = item.Kind, Command = item.Command, Target = item.Target };
                candidate.Resources.Add(resource);
            }
            if (favorite.HasValue) resource.IsFavorite = favorite.Value;
            if (promoteProject) resource.Kind = SearchKind.Project;
            if (tags is not null) resource.Tags = tags.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            static long EstimatedBytes(RegisteredResource value) => 256L + 2L * (value.Name.Length + value.Target.Length + value.Tags.Sum(t => t.Length));
            if (_shards.Where(x => x.Key != shardId).SelectMany(x => x.Value.Resources).Sum(EstimatedBytes) + candidate.Resources.Sum(EstimatedBytes) > 32 * 1024 * 1024)
                throw new InvalidOperationException("资源元数据达到估算 32 MiB 预算，未保存。");
            await store.SaveAsync(relative, candidate, MosaicJsonContext.Configured.ResourceShard, token).ConfigureAwait(false);
            _shards[shardId] = candidate;
            Publish();
            Changed?.Invoke();
            return resource.ToItem();
        }
        finally { _gate.Release(); }
    }

    public async Task RecordUseAsync(SearchItem item, CancellationToken token = default)
    {
        await EnsureLoadedAsync(token).ConfigureAwait(false);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var candidate = new SearchHistoryDocument { Revision = _history!.Revision, Items = _history.Items.Where(x => SearchIdentity.Key(x.Command, x.Target) != item.Key).Take(29).ToList() };
            candidate.Items.Insert(0, new() { Name = item.Name, Kind = item.Kind, Command = item.Command, Target = item.Target, UsedUtc = DateTime.UtcNow });
            await store.SaveAsync(_prefix + "recent.json", candidate, MosaicJsonContext.Configured.SearchHistoryDocument, token).ConfigureAwait(false);
            _history = candidate;
            Publish();
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }
    private static string Shard(string id) => (Convert.ToByte(id[..2], 16) & 63).ToString("x2");
    private void Publish()
    {
        Volatile.Write(ref _resources, _shards.Values.SelectMany(x => x.Resources).Select(x => x.ToItem()).ToArray());
        Volatile.Write(ref _recent, _history?.Items.OrderByDescending(x => x.UsedUtc).Select(x => x.ToItem()).ToArray() ?? []);
    }
}
