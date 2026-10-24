using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Mosaic.Core.Models;
using Mosaic.Core.Search;

namespace Mosaic.Storage.Json;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    UseStringEnumConverter = true, MaxDepth = 32)]
[JsonSerializable(typeof(ActiveProfile))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(WorkspaceDocument))]
[JsonSerializable(typeof(WidgetContent))]
[JsonSerializable(typeof(ResourceShard))]
[JsonSerializable(typeof(SearchHistoryDocument))]
[JsonSerializable(typeof(IndexEntry))]
[JsonSerializable(typeof(IndexManifest))]
[JsonSerializable(typeof(SearchSettings))]
[JsonSerializable(typeof(CollectionDocument))]
[JsonSerializable(typeof(WorkspaceSwitchJournal))]
public partial class MosaicJsonContext : JsonSerializerContext
{
    public static MosaicJsonContext Configured { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });
}
