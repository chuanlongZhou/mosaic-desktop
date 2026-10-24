using System.Text.Json.Serialization;
using Mosaic.Core.Search;

namespace Mosaic.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<CollectionKind>))]
public enum CollectionKind { Static, Smart, Project }

public sealed class CollectionDocument : VersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新集合";
    public CollectionKind Kind { get; set; }
    public List<string> ResourceIds { get; set; } = [];
    public CollectionRule Rule { get; set; } = new();
    public override void Validate()
    {
        ValidateVersion(); DocumentRules.Identifier(Id); ResourceReferences.Validate(ResourceIds);
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || !Enum.IsDefined(Kind) || Rule is null)
            throw new InvalidDataException("集合名称或规则无效。");
        Rule.Validate();
        if (Kind == CollectionKind.Smart && ResourceIds.Count != 0) throw new InvalidDataException("智能集合使用规则，不保存手动成员。");
    }
    public bool Includes(SearchItem resource) => Kind switch
    {
        CollectionKind.Smart => Rule.Matches(resource),
        CollectionKind.Project => ResourceIds.Contains(resource.ResourceId ?? "") ||
            (Rule.Root.Length > 0 && resource.Command == "resource.open" && SearchIdentity.IsWithin(resource.Target, Rule.Root)),
        _ => ResourceIds.Contains(resource.ResourceId ?? "")
    };
    [JsonIgnore] public string KindLabel => Kind switch { CollectionKind.Smart => "智能集合", CollectionKind.Project => "项目集合", _ => "普通集合" };
}

// Deliberately bounded predicates over registered metadata. No code, regex, file reads or scripts.
public sealed class CollectionRule
{
    public SearchKind? Type { get; set; }
    public string Tag { get; set; } = "";
    public string Root { get; set; } = "";
    public string Text { get; set; } = "";
    public bool MatchAny { get; set; }
    public void Validate()
    {
        if (Type.HasValue && !Enum.IsDefined(Type.Value) || Tag is null || Tag.Length > 40 || Root is null || Root.Length > 4096 ||
            (Root.Length > 0 && !Path.IsPathFullyQualified(Root)) || Text is null || Text.Length > 256)
            throw new InvalidDataException("集合规则无效：标签最多 40 字，目录须为完整路径。");
    }
    public bool Matches(SearchItem item)
    {
        var active = 0; var matched = 0;
        void Condition(bool enabled, bool result) { if (!enabled) return; active++; if (result) matched++; }
        Condition(Type.HasValue, item.Kind == Type);
        Condition(Tag.Length > 0, item.Tags.Contains(Tag, StringComparer.OrdinalIgnoreCase));
        Condition(Root.Length > 0, Root.Length > 0 && item.Command == "resource.open" && SearchIdentity.IsWithin(item.Target, Root));
        Condition(Text.Length > 0, Text.Length > 0 && SearchRanking.Match(item, new SearchQuery(Text)) is not null);
        return active == 0 || (MatchAny ? matched > 0 : matched == active);
    }
}

public static class ResourceReferences
{
    public static void Validate(List<string>? references)
    {
        if (references is null || references.Count > 10000 || references.Any(x => !Guid.TryParseExact(x, "N", out _)) || references.Distinct(StringComparer.Ordinal).Count() != references.Count)
            throw new InvalidDataException("资源引用须为唯一稳定 ID，最多 1 万项。");
    }
}

public sealed record WorkspaceSummary(string Id, string Name, int ResourceCount, int DockCount, int WidgetCount);

public sealed class WorkspaceSwitchJournal : VersionedDocument
{
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "personal";
    public string To { get; set; } = "personal";
    public string Stage { get; set; } = "Complete";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = "";
    public override void Validate()
    {
        ValidateVersion(); DocumentRules.Identifier(From); DocumentRules.Identifier(To);
        if (!Guid.TryParseExact(ExecutionId, "N", out _) || Stage is not ("Preflight" or "Snapshot" or "ApplyMosaicState" or "Complete" or "PartialFailure" or "Failed" or "Cancelled") || Message is null || Message.Length > 1000)
            throw new InvalidDataException("工作空间切换日志无效。");
    }
}
