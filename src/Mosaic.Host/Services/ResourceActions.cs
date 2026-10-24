using Mosaic.Core.Commands;
using Mosaic.Core.Search;
using Mosaic.Storage.Json;

namespace Mosaic.Host.Services;

public sealed class ResourceActions(AppServices services, SearchLibrary library)
{
    public Task<SearchItem> RegisterAsync(SearchItem item) => library.RegisterAsync(item);
    public Task<SearchItem> SaveMetadataAsync(SearchItem item, bool favorite, IReadOnlyList<string> tags) => library.UpdateAsync(item, favorite, tags);
    public async Task<string> OpenAsync(SearchItem item, bool reveal = false)
    {
        await services.Commands.ExecuteAsync(new CommandRequest(reveal || item.Kind == SearchKind.Model ? "resource.reveal" : item.Command, item.Target));
        try { await library.RecordUseAsync(item); return "已打开“" + item.Name + "”"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "已打开；近期记录保存失败：" + ex.Message; }
    }
    public async Task PinToDockAsync(SearchItem item)
    {
        var registered = await RegisterAsync(item);
        var settings = services.CopySettings();
        if (settings.Dock.Items.Any(x => SearchIdentity.Key(x.Command, x.Target) == item.Key)) return;
        if (settings.Dock.Items.Count >= 24) throw new InvalidOperationException("Dock 最多保留 24 个入口。");
        settings.Dock.Items.Add(new() { Title = item.Name[..Math.Min(80, item.Name.Length)], Glyph = item.Glyph,
            Command = item.Command, Target = item.Target, ResourceId = registered.ResourceId });
        await services.SaveSettingsAsync(settings);
    }
    public async Task AddToWorkspaceAsync(SearchItem item, string workspaceId)
    {
        var registered = await RegisterAsync(item);
        await services.Workspaces.SetResourceAsync(workspaceId, registered.ResourceId!, true);
    }
}
