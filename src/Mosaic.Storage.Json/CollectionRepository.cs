using System.Text.Json;
using Mosaic.Core.Models;

namespace Mosaic.Storage.Json;

public sealed class CollectionRepository(JsonDocumentStore store, string profileId)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _prefix = "profiles/" + profileId + "/collections/";
    private CollectionDocument[] _collections = [];
    private bool _loaded;
    public string? Warning { get; private set; }
    public IReadOnlyList<CollectionDocument> Collections => Volatile.Read(ref _collections);
    public event Action? Changed;

    public async Task EnsureLoadedAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_loaded) return;
            var directory = Path.Combine(store.Root, _prefix);
            var found = new List<CollectionDocument>();
            long bytes = 0;
            if (Directory.Exists(directory))
                foreach (var id in Directory.EnumerateFiles(directory, "*.json*", SearchOption.TopDirectoryOnly)
                    .Where(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase))
                    .Select(x => Path.GetFileName(x).Split('.')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    DocumentRules.Identifier(id);
                    var path = Path.Combine(directory, id + ".json");
                    bytes += new FileInfo(File.Exists(path) ? path : path + ".bak").Length;
                    if (found.Count >= 200 || bytes > 8 * 1024 * 1024) { Warning = "集合超过 200 个或 8 MiB 读取预算；已保留原文件，当前只读展示部分。"; break; }
                    var collection = await store.LoadAsync(_prefix + id + ".json", MosaicJsonContext.Configured.CollectionDocument,
                        () => throw new InvalidDataException("集合文件不存在。"), token).ConfigureAwait(false);
                    if (collection.Id != id) throw new InvalidDataException("集合 ID 与文件名不一致，原文件已保留。");
                    found.Add(collection);
                }
            Volatile.Write(ref _collections, found.ToArray());
            _loaded = true;
        }
        finally { _gate.Release(); }
    }

    public CollectionDocument Copy(CollectionDocument source) => JsonSerializer.Deserialize(
        JsonSerializer.SerializeToUtf8Bytes(source, MosaicJsonContext.Configured.CollectionDocument), MosaicJsonContext.Configured.CollectionDocument)!;

    public async Task SaveAsync(CollectionDocument candidate, CancellationToken token = default)
    {
        await EnsureLoadedAsync(token).ConfigureAwait(false);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Warning is not null) throw new InvalidOperationException(Warning);
            candidate = Copy(candidate); candidate.Validate();
            var existing = Collections.FirstOrDefault(x => x.Id == candidate.Id);
            if (existing is null)
            {
                if (Collections.Count >= 200) throw new InvalidOperationException("最多保留 200 个集合。");
                var baseline = await store.LoadAsync(_prefix + candidate.Id + ".json", MosaicJsonContext.Configured.CollectionDocument, () => new() { Id = candidate.Id }, token).ConfigureAwait(false);
                if (baseline.Revision != 0) throw new JsonConflictException("集合 ID 已存在，请重新打开集合。");
            }
            var updated = Collections.Where(x => x.Id != candidate.Id).Append(candidate).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (updated.Sum(x => JsonSerializer.SerializeToUtf8Bytes(x, MosaicJsonContext.Configured.CollectionDocument).LongLength) > 8 * 1024 * 1024)
                throw new InvalidOperationException("集合元数据达到 8 MiB 预算，未保存。");
            await store.SaveAsync(_prefix + candidate.Id + ".json", candidate, MosaicJsonContext.Configured.CollectionDocument, token).ConfigureAwait(false);
            Volatile.Write(ref _collections, updated);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public async Task SetMemberAsync(string collectionId, string resourceId, bool include, CancellationToken token = default)
    {
        // Use the same gate for read-modify-write, so rapid additions cannot lose a member.
        await EnsureLoadedAsync(token).ConfigureAwait(false);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Warning is not null) throw new InvalidOperationException(Warning);
            var original = Collections.SingleOrDefault(x => x.Id == collectionId) ?? throw new InvalidOperationException("集合不存在。");
            if (original.Kind == CollectionKind.Smart) throw new InvalidOperationException("智能集合由规则决定成员，请编辑规则或资源标签。");
            var candidate = Copy(original);
            if (include) { if (!candidate.ResourceIds.Contains(resourceId)) candidate.ResourceIds.Add(resourceId); }
            else candidate.ResourceIds.Remove(resourceId);
            var updated = Collections.Select(x => x.Id == candidate.Id ? candidate : x).ToArray();
            if (updated.Sum(x => JsonSerializer.SerializeToUtf8Bytes(x, MosaicJsonContext.Configured.CollectionDocument).LongLength) > 8 * 1024 * 1024)
                throw new InvalidOperationException("集合元数据达到 8 MiB 预算，未保存。");
            await store.SaveAsync(_prefix + candidate.Id + ".json", candidate, MosaicJsonContext.Configured.CollectionDocument, token).ConfigureAwait(false);
            Volatile.Write(ref _collections, updated);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }
}
