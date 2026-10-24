using System.Text.Json;
using Mosaic.Core;
using Mosaic.Core.Models;
using Mosaic.Core.Search;
using Mosaic.Host.Services;
using Mosaic.Storage.Json;

internal static class LibraryWorkspaceTests
{
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Root() { var path = Path.Combine(Path.GetTempPath(), "Mosaic.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static async Task Throws<T>(Func<Task> operation) where T : Exception
    { try { await operation(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

    public static Task LegacyWorkspace()
    {
        var legacy = Defaults.Workspace(); var json = JsonSerializer.Serialize(legacy, MosaicJsonContext.Configured.WorkspaceDocument);
        using var document = JsonDocument.Parse(json);
        var old = "{" + string.Join(",", document.RootElement.EnumerateObject().Where(x => x.Name is not ("dock" or "resourceIds")).Select(x => JsonSerializer.Serialize(x.Name) + ":" + x.Value.GetRawText())) + "}";
        var copy = JsonSerializer.Deserialize(old, MosaicJsonContext.Configured.WorkspaceDocument)!; copy.Validate();
        Assert(copy.Dock is null && copy.ResourceIds.Count == 0 && copy.Layouts["12"].Count == 8, "V0.2 workspace did not retain its layout.");
        copy.ResourceIds = ["../../bad"]; return Throws<InvalidDataException>(() => { copy.Validate(); return Task.CompletedTask; });
    }
    public static async Task Collections()
    {
        var root = Root(); var file = root + ".gguf"; await File.WriteAllTextAsync(file, "original bytes");
        var store = new JsonDocumentStore(root); var library = new SearchLibrary(store, "default");
        var registered = await library.UpdateAsync(new("Qwen 本地模型", SearchKind.Model, "resource.open", file, "test"), true, ["AI Models"]);
        var again = await library.RegisterAsync(registered with { IsFavorite = false, Tags = [] });
        Assert(again.ResourceId == registered.ResourceId && again.IsFavorite && again.Tags.Contains("AI Models"), "Registration reset existing metadata.");
        var second = await library.RegisterAsync(new("记事本", SearchKind.App, "app.builtin", "notepad", "test"));
        var repository = new CollectionRepository(store, "default");
        var manual = new CollectionDocument { Name = "AI Models" }; await repository.SaveAsync(manual);
        await Task.WhenAll(repository.SetMemberAsync(manual.Id, registered.ResourceId!, true), repository.SetMemberAsync(manual.Id, second.ResourceId!, true));
        var saved = repository.Collections.Single(); Assert(saved.ResourceIds.Count == 2 && saved.Includes(registered), "Concurrent collection additions lost a member.");
        await repository.SetMemberAsync(manual.Id, registered.ResourceId!, false);
        Assert(!repository.Collections.Single().Includes(registered) && await File.ReadAllTextAsync(file) == "original bytes", "Removing membership altered the physical file.");
        var smart = new CollectionDocument { Name = "AI 自动集合", Kind = CollectionKind.Smart, Rule = new() { Type = SearchKind.Model, Tag = "AI Models", Root = Path.GetDirectoryName(file)!, Text = "qwen" } };
        await repository.SaveAsync(smart); Assert(repository.Collections.Single(x => x.Id == smart.Id).Includes(registered), "Smart AND predicates did not match.");
        Assert(!repository.Collections.Single(x => x.Id == smart.Id).Includes(second), "Smart type predicate leaked applications.");
        Assert(new CollectionRule { Type = SearchKind.App, Tag = "AI Models", MatchAny = true }.Matches(registered), "OR collection predicate did not match the tag alternative.");
        await Throws<InvalidOperationException>(() => repository.SetMemberAsync(smart.Id, second.ResourceId!, true));
        var project = new CollectionDocument { Name = "Research", Kind = CollectionKind.Project, Rule = new() { Root = Path.GetDirectoryName(file)! }, ResourceIds = [second.ResourceId!] };
        await repository.SaveAsync(project); Assert(project.Includes(registered) && project.Includes(second), "Project did not combine registered descendants and explicit references.");
        var reopened = new CollectionRepository(new JsonDocumentStore(root), "default"); await reopened.EnsureLoadedAsync();
        Assert(reopened.Collections.Count == 3 && reopened.Collections.Single(x => x.Id == manual.Id).ResourceIds.Single() == second.ResourceId, "Collection JSON did not survive restart.");
        var invalid = reopened.Copy(reopened.Collections[0]); invalid.ResourceIds = ["bad-id"];
        await Throws<InvalidDataException>(() => reopened.SaveAsync(invalid));
        var original = reopened.Collections.Single(x => x.Id == manual.Id); var candidate = reopened.Copy(original); candidate.Name = "Changed";
        await File.AppendAllTextAsync(Path.Combine(root, "profiles/default/collections/" + manual.Id + ".json"), " ");
        await Throws<JsonConflictException>(() => reopened.SaveAsync(candidate));
        Assert(reopened.Collections.Single(x => x.Id == manual.Id).Name == original.Name, "Failed collection write published uncommitted metadata.");
    }
    public static async Task WorkspaceRoundTrip()
    {
        var root = Root(); var services = new AppServices(new JsonDocumentStore(root)); await services.InitializeAsync();
        var library = new SearchLibrary(services.Store, "default");
        var resource = await library.UpdateAsync(new("模型", SearchKind.Model, "resource.open", Path.Combine(root, "model.gguf"), "test"), true, ["AI"]);
        await services.SaveContentAsync(new() { Notes = "所有工作空间共享的笔记" });
        var ai = await services.Workspaces.CreateAsync("AI Lab", "ai"); var writing = await services.Workspaces.CreateAsync("Writing", "writing");
        Assert(writing.Layouts["12"].Min(x => x.Row) == 0, "Writing preset left an empty monitoring row.");
        await services.Workspaces.SetResourceAsync(ai.Id, resource.ResourceId!, true);
        var personal = services.CopySettings(); personal.Dock.Items.Add(new() { Id = "personal-only", Title = "个人入口", Target = root }); await services.SaveSettingsAsync(personal);
        var layout = services.CopyWorkspace(); layout.Layouts["12"] = GridLayout.Move(layout.Layouts["12"], "cpu", 3, 0, 12); await services.SaveWorkspaceAsync(layout);
        Assert((await services.Workspaces.SwitchAsync(ai.Id)).Committed && services.Workspace.ResourceIds.Contains(resource.ResourceId!), "AI workspace did not activate stable references.");
        Assert(!services.Settings.Dock.Items.Any(x => x.Id == "personal-only"), "Personal Dock leaked into a new preset workspace.");
        var aiSettings = services.CopySettings(); aiSettings.Dock.Items.Add(new() { Id = "ai-only", Title = "AI 入口", Target = root }); await services.SaveSettingsAsync(aiSettings);
        var aiLayout = services.CopyWorkspace(); aiLayout.Layouts["12"] = aiLayout.Layouts["12"].Where(x => x.Kind != WidgetKind.Network).ToList(); await services.SaveWorkspaceAsync(aiLayout);
        await services.Workspaces.SwitchAsync(writing.Id); Assert(services.Workspace.Layouts["12"].All(x => x.Kind is not (WidgetKind.Cpu or WidgetKind.Memory)), "Writing preset retained monitoring widgets.");
        await services.Workspaces.SwitchAsync("personal");
        Assert(services.Settings.Dock.Items.Any(x => x.Id == "personal-only") && !services.Settings.Dock.Items.Any(x => x.Id == "ai-only") && services.Workspace.Layouts["12"].Single(x => x.Id == "cpu").Column == 3, "Returning did not restore the personal Dock/layout snapshot.");
        await services.Workspaces.SwitchAsync(ai.Id);
        Assert(services.Settings.Dock.Items.Any(x => x.Id == "ai-only") && services.Workspace.Layouts["12"].All(x => x.Kind != WidgetKind.Network), "AI changes did not survive the round trip.");
        var reopened = new AppServices(new JsonDocumentStore(root)); await reopened.InitializeAsync();
        Assert(reopened.Workspace.Id == ai.Id && reopened.Settings.CurrentWorkspaceId == ai.Id && reopened.Workspaces.Items.Count == 3 && reopened.Content.Notes == "所有工作空间共享的笔记", "Restart did not restore the committed workspace and shared notes.");
        Assert((await new SearchLibrary(reopened.Store, "default").RegisterAsync(resource)).ResourceId == resource.ResourceId, "Workspace switching duplicated the resource identity.");
        var journal = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(Path.Combine(root, "profiles/default/workspace-switch.json")), MosaicJsonContext.Configured.WorkspaceSwitchJournal)!;
        Assert(journal.Stage == "Complete" && journal.To == ai.Id, "Completed switch did not finalize its JSON journal.");
    }
    public static async Task SwitchCancellationAndConflicts()
    {
        var root = Root(); var services = new AppServices(new JsonDocumentStore(root)); await services.InitializeAsync();
        var target = await services.Workspaces.CreateAsync("Target"); var cancelled = new CancellationTokenSource();
        var events = 0;
        void CancelAfterSnapshot() { if (services.Workspaces.IsSwitching && ++events == 2) cancelled.Cancel(); }
        services.Workspaces.Changed += CancelAfterSnapshot;
        await Throws<OperationCanceledException>(() => services.Workspaces.SwitchAsync(target.Id, cancelled.Token));
        services.Workspaces.Changed -= CancelAfterSnapshot;
        Assert(services.Workspace.Id == "personal" && services.Settings.CurrentWorkspaceId == "personal", "Cancelled switch committed the pointer.");
        var settingsFile = Path.Combine(root, "profiles/default/settings.json"); await File.AppendAllTextAsync(settingsFile, " ");
        await Throws<JsonConflictException>(() => services.Workspaces.SwitchAsync(target.Id));
        Assert(services.Workspace.Id == "personal", "Conflicted pointer write changed in-memory state.");
        var reopened = new AppServices(new JsonDocumentStore(root)); await reopened.InitializeAsync();
        Assert(reopened.Workspace.Id == "personal", "Failed switch affected the next startup.");
        await Throws<FileNotFoundException>(() => reopened.Workspaces.SwitchAsync("missing"));
        await Throws<InvalidDataException>(() => reopened.Workspaces.SwitchAsync("../../outside"));
    }
    public static async Task InterruptedCommitRecovery()
    {
        var root = Root(); var services = new AppServices(new JsonDocumentStore(root)); await services.InitializeAsync();
        var target = await services.Workspaces.CreateAsync("Committed target");
        services.WorkspaceActivated += (_, _) => File.AppendAllText(Path.Combine(root, "profiles/default/workspace-switch.json"), " ");
        var result = await services.Workspaces.SwitchAsync(target.Id);
        Assert(result.Committed && services.Workspace.Id == target.Id && result.Message.Contains("部分"), "Committed pointer was rolled back after journal failure.");
        var reopened = new AppServices(new JsonDocumentStore(root)); await reopened.InitializeAsync();
        Assert(reopened.Workspace.Id == target.Id && reopened.Workspaces.Notice?.Contains("已提交") == true, "Interrupted commit was not reconciled against the settings pointer.");
    }
    public static async Task PreviewBounds()
    {
        var root = Root(); var path = Path.Combine(root, "preview.md"); await File.WriteAllTextAsync(path, new string('中', 6000));
        var item = new SearchItem("preview.md", SearchKind.File, "resource.open", path, "test");
        var preview = ResourcePreview.Read(item, CancellationToken.None);
        Assert(preview.Text.Length < 2100 && preview.Text.Contains("2,048"), "Text preview exceeded its bound.");
        var missing = ResourcePreview.Read(item with { Target = Path.Combine(root, "missing.md") }, CancellationToken.None);
        Assert(missing.Information.Contains("离线"), "Unavailable resource lost its metadata state.");
        var network = ResourcePreview.Read(item with { Target = "\\\\example\\share\\secret.txt" }, CancellationToken.None);
        Assert(network.Text.Length == 0 && network.Information.Contains("不自动预览"), "Network preview attempted implicit reads.");
        var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Throws<OperationCanceledException>(() => { ResourcePreview.Read(item, cancelled.Token); return Task.CompletedTask; });
    }
}
