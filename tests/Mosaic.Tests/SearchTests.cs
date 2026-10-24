using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mosaic.Core.Models;
using Mosaic.Core.Search;
using Mosaic.Platform.Windows;
using Mosaic.Storage.Json;

internal static class SearchTests
{
    public static Task LegacySettings()
    {
        var old = JsonSerializer.Deserialize("{\"schemaVersion\":1,\"revision\":4,\"dock\":{\"items\":[]}}", MosaicJsonContext.Configured.AppSettings)!;
        old.Validate();
        Assert(old.Search.Enabled && old.Search.Roots.Count == 0 && old.Search.HotkeyVirtualKey == 0x20, "V0.1 config must get safe search defaults without scanning user folders.");
        return Task.CompletedTask;
    }

    public static Task Ranking()
    {
        var exact = new SearchItem("photo", SearchKind.File, "resource.open", "C:\\Allowed\\photo", "test");
        var prefix = new SearchItem("photography", SearchKind.File, "resource.open", "C:\\Allowed\\photography", "test");
        var substring = new SearchItem("my-photo", SearchKind.File, "resource.open", "C:\\Allowed\\my-photo", "test");
        var query = new SearchQuery("photo");
        Assert(SearchRanking.Match(exact, query)!.Score > SearchRanking.Match(prefix, query)!.Score && SearchRanking.Match(prefix, query)!.Score > SearchRanking.Match(substring, query)!.Score, "Exact / prefix / substring order changed.");
        Assert(SearchRanking.Match(new("项目笔记.txt", SearchKind.File, "resource.open", "C:\\Allowed\\项目笔记.txt", "test"), new("笔记")) is not null, "Chinese substring failed.");
        Assert(SearchRanking.Match(new("café.txt", SearchKind.File, "resource.open", "C:\\Allowed\\café.txt", "test"), new("cafe", IgnoreAccents: true)) is not null, "Optional accent folding failed.");
        Assert(SearchRanking.Match(exact, new("photo", SearchFilter.Apps)) is null, "Type filter did not apply.");
        Assert(SearchIdentity.FileKind("model.GGUF", false) == SearchKind.Model, "Model extension detection failed.");
        var top = new TopResults();
        for (var i = 0; i < 1000; i++) top.Add(new(new("photo" + i, SearchKind.File, "resource.open", "C:\\Allowed\\" + i, "test"), i, "test"));
        Assert(top.Sorted().Count == 50 && top.Sorted()[0].Score == 999 && top.Sorted()[^1].Score == 950, "Top results are not bounded to the strongest 50.");
        return Task.CompletedTask;
    }

    public static async Task DiscoveryScope()
    {
        var root = NewRoot();
        var files = Path.Combine(root, "files"); var data = Path.Combine(root, "data");
        Directory.CreateDirectory(files); Directory.CreateDirectory(data);
        Directory.CreateDirectory(Path.Combine(files, "node_modules")); Directory.CreateDirectory(Path.Combine(files, ".git"));
        await File.WriteAllTextAsync(Path.Combine(files, "项目说明.txt"), "name-only indexing");
        await File.WriteAllTextAsync(Path.Combine(files, "node_modules", "excluded.txt"), "excluded");
        await File.WriteAllTextAsync(Path.Combine(files, ".git", "excluded.txt"), "excluded");
        Assert(FileDiscovery.ValidateRoot(files, data) == files, "Allowed folder rejected.");
        var entries = FileDiscovery.Enumerate(files, CancellationToken.None).ToArray();
        Assert(entries.Length == 1 && entries[0].Name == "项目说明.txt", "Excluded build directories were scanned.");
        Assert(SearchIdentity.IsWithin(Path.Combine(files, "项目说明.txt"), files) && !SearchIdentity.IsWithin(files + "-other\\secret.txt", files), "Sibling prefix escaped the search scope.");
        Assert(FileDiscovery.IgnoreChange(files, Path.Combine(files, "node_modules", "changed.js")), "Excluded watcher events should not trigger scans.");
        await Throws<InvalidDataException>(() => Task.Run(() => FileDiscovery.ValidateRoot(root, data)));
        await Throws<InvalidDataException>(() => Task.Run(() => FileDiscovery.ValidateRoot(Path.GetPathRoot(root)!, data)));
    }

    public static async Task JsonlIndex()
    {
        var root = NewRoot(); var files = Path.Combine(root, "files"); Directory.CreateDirectory(files);
        var options = new SearchSettings { Roots = [files], HotEntryLimit = 100 };
        var index = new JsonFileIndex(Path.Combine(root, "data"));
        IEnumerable<IndexEntry> Entries(string _, CancellationToken token)
        {
            for (var i = 0; i < 2505; i++) { token.ThrowIfCancellationRequested(); yield return new() { Name = $"样本-{i:D4}.txt", Path = Path.Combine(files, $"样本-{i:D4}.txt") }; }
        }
        await index.RebuildAsync(options, Entries, _ => { }, CancellationToken.None);
        Assert(index.State.Hot.Length == 100 && index.State.TotalCount == 2505 && index.State.Manifests[0].Chunks.Count == 2, "Chunking or hot-entry budget failed.");
        var cold = await index.SearchColdAsync(new("样本-2504", SearchFilter.Files), options, CancellationToken.None);
        Assert(cold.Results.Count == 1 && cold.Results[0].Item.Name == "样本-2504.txt", "Cold chunk search lost a result outside the hot set.");
        var generation = index.State.Manifests[0].Generation;
        using var cancelled = new CancellationTokenSource();
        IEnumerable<IndexEntry> Interrupted(string _, CancellationToken token)
        {
            yield return new() { Name = "temporary.txt", Path = Path.Combine(files, "temporary.txt") };
            cancelled.Cancel(); token.ThrowIfCancellationRequested();
        }
        await Throws<OperationCanceledException>(() => index.RebuildAsync(options, Interrupted, _ => { }, cancelled.Token));
        await index.LoadAsync(options, CancellationToken.None);
        Assert(index.State.Manifests[0].Generation == generation && index.State.TotalCount == 2505, "A cancelled scan replaced the preceding complete generation.");
        index.ReleaseHot();
        Assert(index.State.Hot.Length == 0 && !index.State.HotLoaded, "Hot index was not releasable.");
    }

    public static async Task CacheBudget()
    {
        var root = NewRoot(); var files = Path.Combine(root, "files"); Directory.CreateDirectory(files);
        var options = new SearchSettings { Roots = [files], CacheMiB = 5, HotEntryLimit = 20000, HotMemoryMiB = 1 };
        var index = new JsonFileIndex(Path.Combine(root, "data"));
        IEnumerable<IndexEntry> Entries(string _, CancellationToken token)
        {
            for (var i = 0; i < 3000; i++) { token.ThrowIfCancellationRequested(); yield return new() { Name = $"item-{i}.txt", Path = Path.Combine(files, new string('x', 3000), $"item-{i}.txt") }; }
        }
        await index.RebuildAsync(options, Entries, _ => { }, CancellationToken.None);
        Assert(index.State.Partial && index.State.TotalCount > 0 && index.State.TotalCount < 3000, "Disk budget was not reported as partial coverage.");
        Assert(index.State.Hot.Sum(x => x.EstimatedBytes) <= 1024 * 1024, "Estimated hot-memory budget exceeded.");
        var bytes = Directory.EnumerateFiles(Path.Combine(root, "data", "cache"), "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length);
        Assert(bytes <= 5 * 1024 * 1024, "Total cache disk budget exceeded.");
    }

    public static async Task LibraryPersistence()
    {
        var root = NewRoot();
        var library = new SearchLibrary(new JsonDocumentStore(root), "default");
        var item = new SearchItem("模型.gguf", SearchKind.Model, "resource.open", "C:\\Models\\模型.gguf", "test");
        var saved = await library.UpdateAsync(item, true, ["AI", "测试"]);
        await library.RecordUseAsync(saved);
        var reopened = new SearchLibrary(new JsonDocumentStore(root), "default");
        await reopened.EnsureLoadedAsync(CancellationToken.None);
        Assert(reopened.Resources.Single().ResourceId == saved.ResourceId && reopened.Resources.Single().IsFavorite && reopened.Resources.Single().Tags.Contains("测试"), "Favorite ID or tags were not preserved.");
        Assert(reopened.Recent.Single().Key == item.Key, "Recent resource was not preserved.");
        var removed = await reopened.UpdateAsync(saved, false, ["AI"]);
        Assert(removed.ResourceId == saved.ResourceId && !removed.IsFavorite, "Unfavoriting changed resource identity.");
    }

    public static async Task Aggregation()
    {
        var item = new SearchItem("needle", SearchKind.File, "resource.open", "C:\\scope\\needle.txt", "test");
        var updates = new List<SearchUpdate>();
        var coordinator = new SearchCoordinator([
            new FakeSource("good", (_, _) => Task.FromResult(new SearchSourceResult("good", [new(item, 1000, "exact")], "local ready"))),
            new FakeSource("bad", (_, _) => throw new IOException("offline")),
            new FakeSource("duplicate", async (_, token) => { await Task.Delay(15, token); return new("duplicate", [new(item with { Source = "other" }, 300, "path")], "other ready"); })]);
        await coordinator.SearchAsync(new("needle"), new InlineProgress(updates.Add), CancellationToken.None);
        Assert(updates[^1].Results.Count == 1 && updates[^1].Results[0].Score == 1000 && updates[^1].Partial, "Deduplication or provider fallback failed.");
        using var cancelled = new CancellationTokenSource(15);
        var slow = new SearchCoordinator([new FakeSource("slow", async (_, token) => { await Task.Delay(1000, token); return new("slow", [], ""); })]);
        await Throws<OperationCanceledException>(() => slow.SearchAsync(new("needle"), new InlineProgress(_ => { }), cancelled.Token));
    }

    public static Task EverythingPackets()
    {
        var settings = new SearchSettings { Roots = ["C:\\Allowed"] };
        var query = new SearchQuery("report|ext:exe");
        var bytes = EverythingProtocol.BuildQuery(123, 456, query, settings);
        Assert(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0)) == 123 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)) == 200, "Packed IPC query layout changed.");
        var pattern = Encoding.Unicode.GetString(bytes, 20, bytes.Length - 22);
        Assert(Regex.IsMatch("C:\\Allowed\\report|ext:exe.txt", pattern) && !Regex.IsMatch("C:\\Allowed-other\\report|ext:exe.txt", pattern) && !Regex.IsMatch("C:\\Allowed\\report.exe", pattern), "Literal query or scoped regex escaping failed.");
        var name = Encoding.Unicode.GetBytes("中文.txt\0"); var path = Encoding.Unicode.GetBytes("C:\\Allowed\0");
        var reply = new byte[40 + name.Length + path.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(8), 1); BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(32), 40); BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(36), (uint)(40 + name.Length));
        name.CopyTo(reply, 40); path.CopyTo(reply, 40 + name.Length);
        var decoded = EverythingProtocol.ParseReply(reply);
        Assert(decoded.Items.Single().Target == "C:\\Allowed\\中文.txt" && !decoded.Partial, "Unicode reply parsing failed.");
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(32), uint.MaxValue);
        try { EverythingProtocol.ParseReply(reply); throw new Exception("Invalid packet offset accepted."); } catch (InvalidDataException) { }
        return Task.CompletedTask;
    }

    private static string NewRoot() { var path = Path.Combine(Path.GetTempPath(), "Mosaic.Search.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Throws<T>(Func<Task> run) where T : Exception
    { try { await run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class InlineProgress(Action<SearchUpdate> report) : IProgress<SearchUpdate> { public void Report(SearchUpdate value) => report(value); }
    private sealed class FakeSource(string name, Func<SearchQuery, CancellationToken, Task<SearchSourceResult>> execute) : ISearchSource
    { public string Name => name; public Task<SearchSourceResult> SearchAsync(SearchQuery query, CancellationToken token) => execute(query, token); }
}
