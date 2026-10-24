using Mosaic.Core.Search;

namespace Mosaic.Core.Models;

public sealed class ResourceShard : VersionedDocument
{
    public List<RegisteredResource> Resources { get; set; } = [];
    public override void Validate()
    {
        ValidateVersion();
        if (Resources is null || Resources.Count > 1000) throw new InvalidDataException("资源分片过大。");
        var ids = new HashSet<string>();
        foreach (var resource in Resources)
        {
            if (resource is null) throw new InvalidDataException("资源记录为空。");
            DocumentRules.Identifier(resource.Id);
            if (!Guid.TryParseExact(resource.Id, "N", out _) || !ids.Add(resource.Id) || string.IsNullOrWhiteSpace(resource.Name) || resource.Name.Length > 256 ||
                resource.Target is null || resource.Target.Length > 4096 || string.IsNullOrWhiteSpace(resource.Command) || resource.Command.Length > 80 ||
                !Enum.IsDefined(resource.Kind) || resource.Tags is null || resource.Tags.Count > 16 || resource.Tags.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 40))
                throw new InvalidDataException("资源名称、命令、路径或标签无效。");
        }
    }
}

public sealed class RegisteredResource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public SearchKind Kind { get; set; }
    public string Command { get; set; } = "resource.open";
    public string Target { get; set; } = "";
    public bool IsFavorite { get; set; }
    public List<string> Tags { get; set; } = [];
    public SearchItem ToItem() => new(Name, Kind, Command, Target, "资源元数据") { ResourceId = Id, IsFavorite = IsFavorite, Tags = Tags.ToArray() };
}

public sealed class SearchHistoryDocument : VersionedDocument
{
    public List<RecentResource> Items { get; set; } = [];
    public override void Validate()
    {
        ValidateVersion();
        if (Items is null || Items.Count > 30) throw new InvalidDataException("搜索历史最多 30 项。");
        foreach (var item in Items)
            if (item is null || item.Name is null || item.Name.Length > 256 || item.Target is null || item.Target.Length > 4096 ||
                item.Command is null || item.Command.Length > 80 || !Enum.IsDefined(item.Kind)) throw new InvalidDataException("近期资源无效。");
    }
}

public sealed class RecentResource
{
    public string Name { get; set; } = "";
    public SearchKind Kind { get; set; }
    public string Command { get; set; } = "";
    public string Target { get; set; } = "";
    public DateTime UsedUtc { get; set; }
    public SearchItem ToItem() => new(Name, Kind, Command, Target, "最近使用") { LastUsedUtc = UsedUtc };
}

public sealed class IndexEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Length { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public SearchItem ToItem(string source = "本地索引") => new(Name, SearchIdentity.FileKind(Path, IsDirectory), "resource.open", Path, source);
    [System.Text.Json.Serialization.JsonIgnore]
    public long EstimatedBytes => 160L + (Name.Length + Path.Length) * 2L;
}

public sealed class IndexManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Root { get; set; } = "";
    public string Generation { get; set; } = "";
    public List<string> Chunks { get; set; } = [];
    public int Count { get; set; }
    public bool Partial { get; set; }
    public DateTime ScannedUtc { get; set; }
}
