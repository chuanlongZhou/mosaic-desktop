using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Mosaic.Core.Models;
using Mosaic.Core.Search;

namespace Mosaic.Storage.Json;

public sealed record IndexState(IndexEntry[] Hot, IndexManifest[] Manifests, bool HotLoaded)
{
    public int TotalCount => Manifests.Sum(x => x.Count);
    public bool Partial => Manifests.Any(x => x.Partial);
}

public sealed class JsonFileIndex(string dataRoot)
{
    private readonly string _cacheRoot = Path.Combine(Path.GetFullPath(dataRoot), "cache", "index");
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private IndexState _state = new([], [], false);
    public IndexState State => Volatile.Read(ref _state);
    public void ReleaseHot() { var state = State; Volatile.Write(ref _state, new([], state.Manifests, false)); }

    public async Task LoadAsync(SearchSettings options, CancellationToken token)
    {
        await _loadGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var manifests = new List<IndexManifest>();
            var hot = new List<IndexEntry>();
            long memory = 0;
            foreach (var root in options.Roots)
            {
                var path = Path.Combine(_cacheRoot, SearchIdentity.RootId(root), "manifest.json");
                if (!File.Exists(path)) continue;
                try
                {
                    if (new FileInfo(path).Length > 1024 * 1024) continue;
                    var manifest = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(path, token).ConfigureAwait(false), MosaicJsonContext.Configured.IndexManifest);
                    if (manifest is null || !ValidManifest(manifest, root)) continue;
                    manifests.Add(manifest);
                    foreach (var entry in ReadEntries(manifest, token))
                    {
                        if (hot.Count >= options.HotEntryLimit || memory + entry.EstimatedBytes > options.HotMemoryMiB * 1024L * 1024) break;
                        hot.Add(entry); memory += entry.EstimatedBytes;
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException) { /* Rebuildable cache, never user content. */ }
            }
            Volatile.Write(ref _state, new(hot.ToArray(), manifests.ToArray(), true));
        }
        finally { _loadGate.Release(); }
    }

    public async Task RebuildAsync(SearchSettings options, Func<string, CancellationToken, IEnumerable<IndexEntry>> enumerate,
        Action<string> progress, CancellationToken token)
    {
        Directory.CreateDirectory(_cacheRoot);
        foreach (var root in options.Roots)
        {
            token.ThrowIfCancellationRequested();
            var rootDirectory = Path.Combine(_cacheRoot, SearchIdentity.RootId(root));
            var generation = Guid.NewGuid().ToString("N");
            var generationDirectory = Path.Combine(rootDirectory, generation);
            var existingBytes = CacheSize();
            var remaining = options.CacheMiB * 1024L * 1024 - existingBytes;
            if (remaining < 4096) throw new IOException("索引缓存空间不足，保留旧索引；请减少目录或清理可重建缓存。");
            var manifest = new IndexManifest { Root = root, Generation = generation, ScannedUtc = DateTime.UtcNow };
            Directory.CreateDirectory(generationDirectory);
            FileStream? stream = null;
            var chunkRows = 0;
            var chunkBytes = 0;
            long written = 0;
            var committed = false;
            try
            {
                foreach (var entry in enumerate(root, token))
                {
                    token.ThrowIfCancellationRequested();
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, CompactContext.IndexEntry);
                    if (bytes.Length > 16384) continue;
                    if (written + bytes.Length + 1 > remaining - 4096) { manifest.Partial = true; break; }
                    if (stream is null || chunkRows >= 2000 || chunkBytes + bytes.Length + 1 > 2 * 1024 * 1024)
                    {
                        if (stream is not null) { await stream.FlushAsync(token).ConfigureAwait(false); await stream.DisposeAsync().ConfigureAwait(false); }
                        if (manifest.Chunks.Count >= 512) { manifest.Partial = true; break; }
                        var chunk = "chunk-" + manifest.Chunks.Count.ToString("D4") + ".jsonl";
                        stream = new FileStream(Path.Combine(generationDirectory, chunk), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
                        manifest.Chunks.Add(chunk);
                        chunkRows = chunkBytes = 0;
                    }
                    await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                    await stream.WriteAsync("\n"u8.ToArray(), token).ConfigureAwait(false);
                    written += bytes.Length + 1; chunkBytes += bytes.Length + 1; chunkRows++; manifest.Count++;
                    if (manifest.Count % 1000 == 0) progress($"正在扫描 {Path.GetFileName(root)} · {manifest.Count:N0} 项");
                }
                if (stream is not null) { await stream.FlushAsync(token).ConfigureAwait(false); await stream.DisposeAsync().ConfigureAwait(false); stream = null; }
                token.ThrowIfCancellationRequested();
                var manifestPath = Path.Combine(rootDirectory, "manifest.json");
                var temp = manifestPath + ".tmp";
                await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(manifest, MosaicJsonContext.Configured.IndexManifest), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (File.Exists(manifestPath)) File.Replace(temp, manifestPath, null); else File.Move(temp, manifestPath);
                committed = true;
                // Only flat directories generated by this cache writer are eligible for cleanup.
                foreach (var old in Directory.EnumerateDirectories(rootDirectory))
                    if (Path.GetFileName(old) != generation && Guid.TryParseExact(Path.GetFileName(old), "N", out _)) DeleteGeneration(old);
            }
            finally
            {
                if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
                if (!committed) DeleteGeneration(generationDirectory);
            }
        }
        await LoadAsync(options, token).ConfigureAwait(false);
    }

    public SearchSourceResult SearchHot(SearchQuery query, SearchSettings options, CancellationToken token = default)
    {
        var top = new TopResults();
        if (query.Normalized.Length > 0)
            foreach (var entry in State.Hot)
            {
                token.ThrowIfCancellationRequested();
                if (options.Roots.Any(root => SearchIdentity.IsWithin(entry.Path, root))) top.Add(SearchRanking.Match(entry.ToItem(), query));
            }
        return new("本地热索引", top.Sorted(), $"本地热索引 {State.Hot.Length:N0} 项", State.Partial);
    }

    public Task<SearchSourceResult> SearchColdAsync(SearchQuery query, SearchSettings options, CancellationToken token) => Task.Run(() =>
    {
        var state = State;
        if (query.Normalized.Length == 0 || state.TotalCount <= state.Hot.Length) return new SearchSourceResult("本地分块", [], "");
        var top = new TopResults();
        var watch = Stopwatch.StartNew();
        var timeout = query.ContinueSearch ? 8000 : 1000;
        foreach (var manifest in state.Manifests.Where(m => options.Roots.Any(root => SearchIdentity.IsWithin(m.Root, root))))
        {
            try
            {
                foreach (var entry in ReadEntries(manifest, token))
                {
                    token.ThrowIfCancellationRequested();
                    if (watch.ElapsedMilliseconds > timeout) return new SearchSourceResult("本地分块", top.Sorted(), "较大目录仅返回部分结果，可继续搜索", true);
                    top.Add(SearchRanking.Match(entry.ToItem("本地分块"), query));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
            { return new SearchSourceResult("本地分块", top.Sorted(), "缓存不可读，请重建索引", true); }
        }
        return new SearchSourceResult("本地分块", top.Sorted(), $"已查询 {state.TotalCount:N0} 项缓存", state.Partial);
    }, token);

    private IEnumerable<IndexEntry> ReadEntries(IndexManifest manifest, CancellationToken token)
    {
        foreach (var chunk in manifest.Chunks)
        {
            var path = Path.Combine(_cacheRoot, SearchIdentity.RootId(manifest.Root), manifest.Generation, chunk);
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("索引块超限。");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                token.ThrowIfCancellationRequested();
                if (line.Length > 16384) throw new InvalidDataException("索引行超限。");
                var entry = JsonSerializer.Deserialize(line, CompactContext.IndexEntry);
                if (entry is not null && entry.Name is { Length: > 0 and <= 256 } && entry.Path is { Length: > 0 and <= 4096 } && SearchIdentity.IsWithin(entry.Path, manifest.Root)) yield return entry;
            }
        }
    }

    private static bool ValidManifest(IndexManifest manifest, string root) => manifest.SchemaVersion == 1 &&
        string.Equals(manifest.Root, root, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(manifest.Generation, "N", out _) &&
        manifest.Count >= 0 && manifest.Chunks is { Count: <= 512 } && manifest.Chunks.Select((name, i) => name == "chunk-" + i.ToString("D4") + ".jsonl").All(x => x);

    private long CacheSize()
    {
        long size = 0;
        foreach (var path in Directory.EnumerateFiles(_cacheRoot, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System })) size += new FileInfo(path).Length;
        return size;
    }

    private void DeleteGeneration(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!full.StartsWith(_cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
            throw new InvalidOperationException("拒绝清理非索引代目录。");
        if (!Directory.Exists(full)) return;
        for (var current = new DirectoryInfo(full); current is not null && SearchIdentity.IsWithin(current.FullName, _cacheRoot); current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return;
        foreach (var file in Directory.EnumerateFiles(full, "chunk-*.jsonl")) File.Delete(file);
        if (!Directory.EnumerateFileSystemEntries(full).Any()) Directory.Delete(full, recursive: false);
    }

    private static readonly MosaicJsonContext CompactContext = new(new JsonSerializerOptions
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 32, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) });
}
