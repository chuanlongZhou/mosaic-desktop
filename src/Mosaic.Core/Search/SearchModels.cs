using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Mosaic.Core.Search;

[JsonConverter(typeof(JsonStringEnumConverter<SearchKind>))]
public enum SearchKind { App, File, Folder, Model, Command, Setting, Workspace, Project }
public enum SearchFilter { All, Apps, Files, Folders, Models, Commands }

public sealed class SearchSettings
{
    public bool Enabled { get; set; } = true;
    public bool HotkeyEnabled { get; set; } = true;
    public bool IndexEnabled { get; set; } = true;
    public bool UseEverything { get; set; } = true;
    public bool EverythingGlobalScope { get; set; }
    public bool IgnoreAccents { get; set; }
    public uint HotkeyModifiers { get; set; } = 3;
    public uint HotkeyVirtualKey { get; set; } = 0x20;
    public List<string> Roots { get; set; } = [];
    public int HotEntryLimit { get; set; } = 20000;
    public int HotMemoryMiB { get; set; } = 32;
    public int CacheMiB { get; set; } = 100;
    public void Validate()
    {
        if (Roots is null || Roots.Count > 8 || Roots.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 4096 || !Path.IsPathFullyQualified(x)) ||
            HotEntryLimit is < 100 or > 20000 || HotMemoryMiB is < 1 or > 32 || CacheMiB is < 5 or > 100 ||
            HotkeyModifiers is 0 or > 7 || !(HotkeyVirtualKey == 0x20 || HotkeyVirtualKey is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 or >= 0x70 and <= 0x7B))
            throw new InvalidDataException("搜索配置超出范围：最多 8 个完整目录、2 万条热索引、32 MiB 内存和 100 MiB 缓存。");
    }
    public SearchSettings Copy() => new()
    {
        Enabled = Enabled, HotkeyEnabled = HotkeyEnabled, IndexEnabled = IndexEnabled, UseEverything = UseEverything, EverythingGlobalScope = EverythingGlobalScope,
        IgnoreAccents = IgnoreAccents, HotkeyModifiers = HotkeyModifiers, HotkeyVirtualKey = HotkeyVirtualKey,
        Roots = [.. Roots], HotEntryLimit = HotEntryLimit, HotMemoryMiB = HotMemoryMiB, CacheMiB = CacheMiB
    };
}

public sealed record SearchItem(string Name, SearchKind Kind, string Command, string Target, string Source, string Aliases = "")
{
    public string Key => SearchIdentity.Key(Command, Target);
    public string? ResourceId { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsInCurrentWorkspace { get; init; }
    public string[] Tags { get; init; } = [];
    public DateTime? LastUsedUtc { get; init; }
    public string TypeLabel => Kind switch { SearchKind.App => "应用", SearchKind.File => "文件", SearchKind.Folder => "文件夹", SearchKind.Model => "AI 模型", SearchKind.Command => "命令", SearchKind.Setting => "设置", SearchKind.Project => "项目", _ => "工作空间" };
    public string Glyph => Kind switch { SearchKind.App => "\uE8A5", SearchKind.Folder => "\uE8B7", SearchKind.Model => "\uE950", SearchKind.Command => "\uE756", SearchKind.Setting => "\uE713", SearchKind.Workspace => "\uE81E", SearchKind.Project => "\uE943", _ => "\uE8A5" };
}

public sealed record SearchQuery(string Text, SearchFilter Filter = SearchFilter.All, bool IgnoreAccents = false, bool ContinueSearch = false)
{
    public string Normalized { get; } = Normalize(Text);
    private static string Normalize(string text) { var normalized = text.Trim().Normalize(NormalizationForm.FormKC); return normalized[..Math.Min(normalized.Length, 256)]; }
    public bool Accepts(SearchKind kind) => Filter switch
    {
        SearchFilter.Apps => kind == SearchKind.App, SearchFilter.Files => kind is SearchKind.File or SearchKind.Model,
        SearchFilter.Folders => kind == SearchKind.Folder, SearchFilter.Models => kind == SearchKind.Model,
        SearchFilter.Commands => kind is SearchKind.Command or SearchKind.Setting or SearchKind.Workspace, _ => true
    };
}

public sealed record RankedResult(SearchItem Item, int Score, string MatchReason);
public sealed record SearchSourceResult(string Source, IReadOnlyList<RankedResult> Results, string Status, bool Partial = false);
public sealed record SearchUpdate(IReadOnlyList<RankedResult> Results, string Status, bool IsSearching, bool Partial);
public interface ISearchSource
{
    string Name { get; }
    Task<SearchSourceResult> SearchAsync(SearchQuery query, CancellationToken token);
}

public static class SearchIdentity
{
    public static string Key(string command, string target) => command + ":" + target.TrimEnd('\\', '/').ToUpperInvariant();
    public static string RootId(string root) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\', '/').ToUpperInvariant())))[..16].ToLowerInvariant();
    public static bool IsWithin(string path, string root)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.Equals(directory, StringComparison.OrdinalIgnoreCase) || full.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
    public static SearchKind FileKind(string path, bool directory) => directory ? SearchKind.Folder : Path.GetExtension(path).ToLowerInvariant() switch
    { ".gguf" or ".safetensors" or ".ckpt" or ".onnx" => SearchKind.Model, _ => SearchKind.File };
}

public static class SearchRanking
{
    public static RankedResult? Match(SearchItem item, SearchQuery query)
    {
        if (!query.Accepts(item.Kind)) return null;
        var text = query.Normalized;
        var score = 0;
        var reason = "最近与收藏";
        if (text.Length > 0)
        {
            var comparison = query.IgnoreAccents ? CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace : CompareOptions.OrdinalIgnoreCase;
            var comparer = CultureInfo.InvariantCulture.CompareInfo;
            bool Contains(string value, string term) => comparer.IndexOf(value, term, comparison) >= 0;
            if (comparer.Compare(item.Name, text, comparison) == 0) { score = 1000; reason = "名称精确匹配"; }
            else if (comparer.IsPrefix(item.Name, text, comparison)) { score = 800; reason = "名称前缀匹配"; }
            else if (Contains(item.Name, text)) { score = 600; reason = "名称包含关键词"; }
            else if (Contains(item.Aliases, text) || item.Tags.Any(x => Contains(x, text))) { score = 450; reason = "别名或标签匹配"; }
            else if (Contains(item.Target, text)) { score = 300; reason = "路径匹配"; }
            else
            {
                var terms = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8).ToArray();
                if (terms.Length < 2 || !terms.All(term => Contains(item.Name, term) || Contains(item.Target, term) || Contains(item.Aliases, term))) return null;
                score = 250; reason = "多个关键词匹配";
            }
        }
        if (item.IsFavorite) score += 80;
        if (item.IsInCurrentWorkspace) score += 40;
        if (item.LastUsedUtc.HasValue) score += 20;
        return new(item, score, reason);
    }
}

public sealed class TopResults(int limit = 50)
{
    private readonly Dictionary<string, RankedResult> _items = new(StringComparer.OrdinalIgnoreCase);
    private RankedResult? _worst;
    public void Add(RankedResult? result)
    {
        if (result is null) return;
        if (_items.TryGetValue(result.Item.Key, out var existing) && Compare(existing, result) <= 0) return;
        if (existing is null && _items.Count >= limit && _worst is not null && Compare(result, _worst) >= 0) return;
        _items[result.Item.Key] = result;
        if (_items.Count > limit)
        {
            var worst = _items.Values.MaxBy(x => x, Comparer<RankedResult>.Create(Compare))!;
            _items.Remove(worst.Item.Key);
        }
        if (_items.Count >= limit) _worst = _items.Values.MaxBy(x => x, Comparer<RankedResult>.Create(Compare));
    }
    public IReadOnlyList<RankedResult> Sorted() => _items.Values.Order(Comparer<RankedResult>.Create(Compare)).ToArray();
    private static int Compare(RankedResult a, RankedResult b)
    {
        var score = b.Score.CompareTo(a.Score);
        if (score != 0) return score;
        var name = StringComparer.OrdinalIgnoreCase.Compare(a.Item.Name, b.Item.Name);
        return name != 0 ? name : StringComparer.OrdinalIgnoreCase.Compare(a.Item.Key, b.Item.Key);
    }
}

public sealed class SearchCoordinator(IEnumerable<ISearchSource> sources)
{
    public async Task SearchAsync(SearchQuery query, IProgress<SearchUpdate> progress, CancellationToken token)
    {
        var pending = sources.Select(async source =>
        {
            try { return await source.SearchAsync(query, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            { return new SearchSourceResult(source.Name, [], source.Name + " 暂不可用：" + ex.Message, true); }
        }).ToList();
        var results = new TopResults();
        var statuses = new List<string>();
        var partial = false;
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(completed);
            var batch = await completed.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            foreach (var item in batch.Results) results.Add(item);
            if (!string.IsNullOrWhiteSpace(batch.Status)) statuses.Add(batch.Status);
            partial |= batch.Partial;
            progress.Report(new(results.Sorted(), string.Join(" · ", statuses), pending.Count > 0, partial));
        }
    }
}
