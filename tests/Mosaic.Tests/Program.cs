using System.Text.Json;
using Mosaic.Core;
using Mosaic.Core.Commands;
using Mosaic.Core.Models;
using Mosaic.Storage.Json;

if (args.Contains("--desktop-native-status")) { DesktopNativeProbe.Print(); return 0; }
if (args.Contains("--ui-check")) { await UiLayoutChecks.RunAsync(); return 0; }

if (args.Contains("--dock-probe") || args.Contains("--window-probe-max"))
{
    var maximized = args.Contains("--window-probe-max");
    var thread = new Thread(() => new System.Windows.Application().Run(new System.Windows.Window { Title = "Mosaic Dock test fixture", Width = 260, Height = 120, WindowState = maximized ? System.Windows.WindowState.Maximized : System.Windows.WindowState.Normal }));
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Defaults validate and source-generated JSON round-trips", DefaultsRoundTrip),
    ("Dock imports taskbar pins, deduplicates and keeps running windows transient", DockChecks),
    ("Desktop Start-menu filter preserves Windows combinations and releases modifiers", StartMenuKeys),
    ("Overlay restores Windows taskbar on normal exit and forced termination", OverlayRecoveryChecks.RunAsync),
    ("Responsive grids and moves preserve every widget without overlap", GridMoves),
    ("JSON replacement keeps the previous good revision", BackupAndRevision),
    ("Truncated primary restores backup and preserves corrupt evidence", Recovery),
    ("Invalid schema restores validated backup", SchemaRecovery),
    ("Invalid primary and invalid backup never reset user data", Unrecoverable),
    ("External edits and stale revisions are rejected", Conflict),
    ("Concurrent writers cannot silently overwrite a newer revision", ConcurrentWrite),
    ("Document bounds and path traversal are rejected", Bounds),
    ("Unknown commands cannot reach a process launcher", UnknownCommand),
    ("Cancelled save preserves the previous primary", CancelledSave),
    ("V0.1 JSON settings receive safe V0.2 defaults", SearchTests.LegacySettings),
    ("Search ranking, Chinese, accents, filters and top-50 bounds", SearchTests.Ranking),
    ("File discovery respects directory boundaries and exclusions", SearchTests.DiscoveryScope),
    ("JSONL chunking, cold search and cancelled-generation recovery", SearchTests.JsonlIndex),
    ("Index disk and estimated hot-memory budgets", SearchTests.CacheBudget),
    ("Favorite identity, tags and recent entries survive restart", SearchTests.LibraryPersistence),
    ("Provider deduplication, cancellation and offline fallback", SearchTests.Aggregation),
    ("Everything Unicode IPC format, literal escaping and scope", SearchTests.EverythingPackets),
    ("V0.2 workspace JSON gains safe additive defaults", LibraryWorkspaceTests.LegacyWorkspace),
    ("Collections share stable metadata, rules and conflict-safe membership", LibraryWorkspaceTests.Collections),
    ("Three workspaces restore Dock, layout, resources and shared notes", LibraryWorkspaceTests.WorkspaceRoundTrip),
    ("Cancelled and conflicting switches preserve the committed workspace", LibraryWorkspaceTests.SwitchCancellationAndConflicts),
    ("Interrupted switch recovery follows the committed settings pointer", LibraryWorkspaceTests.InterruptedCommitRecovery),
    ("Resource previews bound text and avoid unavailable/network reads", LibraryWorkspaceTests.PreviewBounds),
    ("WPF Dashboard, Palette, Library, bulk edits and workspace activation", UiLayoutChecks.RunAsync)
};
var failed = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS  " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL  " + name + "\n" + ex); }
}
Console.WriteLine($"\n{tests.Length - failed}/{tests.Length} passed.");
return failed == 0 ? 0 : 1;

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static Task StartMenuKeys()
{
    const uint left = 0x5B, right = 0x5C;
    var filter = new Mosaic.Host.Services.StartMenuKeyFilter();
    var pass = Mosaic.Host.Services.StartMenuKeyAction.Pass;
    var mask = Mosaic.Host.Services.StartMenuKeyAction.MaskWindowsRelease;
    var block = Mosaic.Host.Services.StartMenuKeyAction.Block;
    foreach (var win in new[] { left, right })
    {
        Assert(filter.Process(win, true) == pass && filter.Process(win, true) == pass, "Win down or repeat was swallowed.");
        Assert(filter.Process(win, false) == mask, "Bare Win did not suppress Start on release.");
        foreach (var key in new uint[] { 0x44, 0x45, 0x25, 0x26, 0x20 })
        {
            Assert(filter.Process(win, true) == pass, "Combination Win-down blocked.");
            Assert(filter.Process(key, true) == pass && filter.Process(key, false) == pass, "Windows shortcut key blocked.");
            Assert(filter.Process(win, false) == pass, "Windows shortcut release changed.");
        }
    }
    filter.Process(left, true); filter.Process(right, true);
    Assert(filter.Process(left, false) == pass && filter.Process(right, false) == mask, "Two Windows keys leave an unmatched release.");
    Assert(filter.Process(left, false) == pass, "Unmatched Win release was swallowed.");
    Assert(filter.Process(0x1B, true, control: true) == block && filter.Process(0x1B, false) == block, "Ctrl+Esc did not suppress Start.");
    Assert(filter.Process(0x1B, true, control: true, shift: true) == pass && filter.Process(0x1B, false) == pass, "Ctrl+Shift+Esc was blocked.");
    Assert(filter.Process(0x1B, true) == pass && filter.Process(0x1B, false) == pass, "Ordinary Escape was blocked.");
    using var guard = new Mosaic.Host.Services.StartMenuKeyGuard();
    guard.SetEnabled(true); Assert(guard.IsEnabled, "Native key hook did not start.");
    guard.SetEnabled(false); Assert(!guard.IsEnabled, "Native key hook did not stop.");
    guard.SetEnabled(true); guard.Dispose(); Assert(!guard.IsEnabled, "Native key hook did not dispose after re-enabling.");
    return Task.CompletedTask;
}
static async Task DockChecks()
{
    var source = new Mosaic.Host.Services.DockApplications();
    var home = new DockItem { Title = "Home", Command = "dashboard.open" };
    var duplicate = new DockItem { Title = "Duplicate home", Command = "dashboard.open" };
    var entries = await source.ReadAsync([home, duplicate], true).WaitAsync(TimeSpan.FromSeconds(25));
    Assert(entries.Count(x => x.Identity == "mosaic-home") == 1, "Duplicate home entry.");
    Assert(entries.Select(x => x.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count() == entries.Count, "Duplicate application identity.");
    Assert(entries.Where(x => x.IsRunning).All(x => x.Windows.Length > 0), "Missing activation handles.");
    Assert(home.Title == "Home", "Discovery mutated persistent configuration.");
    Console.WriteLine("  Dock applications: " + string.Join(", ", entries.Select(x => x.Title + (x.IsRunning ? " [running]" : ""))));
    var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
    if (Directory.Exists(folder))
        foreach (var shortcut in Directory.EnumerateFiles(folder, "*.lnk").Take(128))
            Assert(entries.Any(x => x.Item.Target == shortcut || x.Title == Path.GetFileNameWithoutExtension(shortcut)), "Missing taskbar pin: " + shortcut);
    var withoutPins = await source.ReadAsync([], false);
    Assert(withoutPins.All(x => x.Identity == "mosaic-home" || x.IsRunning), "Imported pins persisted after synchronization was disabled.");
    var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    start.ArgumentList.Add("--dock-probe");
    start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
    using var probe = System.Diagnostics.Process.Start(start)!;
    try
    {
        for (var i = 0; i < 30 && probe.MainWindowHandle == 0; i++) { await Task.Delay(100); probe.Refresh(); }
        var handle = probe.MainWindowHandle;
        Assert(handle != 0, "Dock probe window did not open.");
        var opened = await source.ReadAsync([], false);
        Assert(opened.Any(x => x.Windows.Contains(handle)), "New application window was not discovered.");
        probe.CloseMainWindow(); await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var closed = await source.ReadAsync([], false);
        Assert(closed.All(x => !x.Windows.Contains(handle)), "Closed application remained in Dock.");
    }
    finally { if (!probe.HasExited) { probe.Kill(); await probe.WaitForExitAsync(); } }
}
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static string TestRoot() { var path = Path.Combine(Path.GetTempPath(), "Mosaic.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
static Task DefaultsRoundTrip()
{
    var settings = Defaults.Settings(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    settings.Validate();
    Defaults.Workspace().Validate();
    var json = JsonSerializer.Serialize(settings, MosaicJsonContext.Configured.AppSettings);
    var copy = JsonSerializer.Deserialize(json, MosaicJsonContext.Configured.AppSettings)!;
    Assert(copy.Dock.Items.Count == 8 && copy.Modules.Dock, "Defaults were not preserved.");
    Assert(json.Contains("schemaVersion") && !json.Contains("SchemaVersion"), "JSON must use camelCase.");
    return Task.CompletedTask;
}
static Task GridMoves()
{
    foreach (var columns in new[] { 12, 8, 4 })
    {
        var layout = GridLayout.Adapt(Defaults.Workspace().Layouts["12"], columns);
        var random = new Random(42);
        for (var i = 0; i < 250; i++)
        {
            var id = layout[random.Next(layout.Count)].Id;
            layout = GridLayout.Move(layout, id, random.Next(-3, columns + 3), random.Next(-3, 12), columns);
            var workspace = new WorkspaceDocument { Layouts = new() { [columns.ToString()] = layout } };
            workspace.Validate();
            Assert(layout.Select(x => x.Id).Distinct().Count() == 8, "Moving lost or duplicated a widget.");
        }
    }
    return Task.CompletedTask;
}
static async Task<(JsonDocumentStore Store, AppSettings Settings, string Path)> Seed()
{
    var store = new JsonDocumentStore(TestRoot());
    var settings = await store.LoadAsync("settings.json", MosaicJsonContext.Configured.AppSettings, () => Defaults.Settings("C:\\"));
    settings.Dock.AutoHide = false; // Storage tests need a known before/after value independent of product defaults.
    await store.SaveAsync("settings.json", settings, MosaicJsonContext.Configured.AppSettings);
    return (store, settings, System.IO.Path.Combine(store.Root, "settings.json"));
}
static async Task BackupAndRevision()
{
    var (store, settings, path) = await Seed();
    settings.Dock.AutoHide = true;
    await store.SaveAsync("settings.json", settings, MosaicJsonContext.Configured.AppSettings);
    Assert(settings.Revision == 2, "Revision did not advance after a successful replacement.");
    var backup = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(path + ".bak"), MosaicJsonContext.Configured.AppSettings)!;
    Assert(!backup.Dock.AutoHide && backup.Revision == 1, "Backup is not the preceding good document.");
    Assert(!Directory.EnumerateFiles(store.Root, "*.tmp").Any(), "Temporary files leaked.");
}
static async Task Recovery()
{
    var (store, settings, path) = await Seed();
    settings.Dock.AutoHide = true;
    await store.SaveAsync("settings.json", settings, MosaicJsonContext.Configured.AppSettings);
    await File.WriteAllTextAsync(path, "{ truncated");
    var recoveredStore = new JsonDocumentStore(store.Root);
    var recovered = await recoveredStore.LoadAsync("settings.json", MosaicJsonContext.Configured.AppSettings, () => throw new Exception("No reset allowed"));
    Assert(recovered.Revision == 1 && !recovered.Dock.AutoHide, "Did not restore the backup.");
    Assert(Directory.GetFiles(store.Root, "*.corrupt-*").Length == 1 && recoveredStore.Notices.Count == 1, "Recovery was silent or corrupt evidence was lost.");
    await recoveredStore.SaveAsync("settings.json", recovered, MosaicJsonContext.Configured.AppSettings);
}
static async Task SchemaRecovery()
{
    var (store, settings, path) = await Seed();
    await store.SaveAsync("settings.json", settings, MosaicJsonContext.Configured.AppSettings);
    await File.WriteAllTextAsync(path, "{\"schemaVersion\":999}");
    var recovered = await new JsonDocumentStore(store.Root).LoadAsync("settings.json", MosaicJsonContext.Configured.AppSettings, () => new());
    Assert(recovered.SchemaVersion == 1 && recovered.Revision == 1, "Unsupported schema bypassed recovery.");
}
static async Task Unrecoverable()
{
    var (store, _, path) = await Seed();
    await File.WriteAllTextAsync(path, "invalid original");
    await File.WriteAllTextAsync(path + ".bak", "invalid backup");
    await Throws<InvalidDataException>(() => new JsonDocumentStore(store.Root).LoadAsync("settings.json", MosaicJsonContext.Configured.AppSettings, () => new()));
    Assert(await File.ReadAllTextAsync(path) == "invalid original", "A broken original was overwritten.");
}
static async Task Conflict()
{
    var (store, settings, path) = await Seed();
    var stale = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(settings, MosaicJsonContext.Configured.AppSettings), MosaicJsonContext.Configured.AppSettings)!;
    await store.SaveAsync("settings.json", settings, MosaicJsonContext.Configured.AppSettings);
    await Throws<JsonConflictException>(() => store.SaveAsync("settings.json", stale, MosaicJsonContext.Configured.AppSettings));
    await File.AppendAllTextAsync(path, " ");
    await Throws<JsonConflictException>(() => store.SaveAsync("settings.json", settings, MosaicJsonContext.Configured.AppSettings));
    Assert((await File.ReadAllTextAsync(path)).EndsWith(' '), "External edit was overwritten.");
}
static async Task ConcurrentWrite()
{
    var (store, settings, _) = await Seed();
    var copies = Enumerable.Range(0, 6).Select(_ => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(settings, MosaicJsonContext.Configured.AppSettings), MosaicJsonContext.Configured.AppSettings)!).ToArray();
    var outcomes = await Task.WhenAll(copies.Select(async copy =>
    {
        try { await store.SaveAsync("settings.json", copy, MosaicJsonContext.Configured.AppSettings); return true; }
        catch (JsonConflictException) { return false; }
    }));
    Assert(outcomes.Count(x => x) == 1, "A stale concurrent writer overwrote the committed version.");
}
static async Task Bounds()
{
    var store = new JsonDocumentStore(TestRoot());
    await Throws<InvalidDataException>(() => store.LoadAsync("../outside.json", MosaicJsonContext.Configured.AppSettings, () => new()));
    var content = await store.LoadAsync("notes.json", MosaicJsonContext.Configured.WidgetContent, () => new());
    content.Notes = new string('x', 20001);
    await Throws<InvalidDataException>(() => store.SaveAsync("notes.json", content, MosaicJsonContext.Configured.WidgetContent));
    var settings = await store.LoadAsync("settings.json", MosaicJsonContext.Configured.AppSettings, () => new());
    settings.Dock.Items.Add(null!);
    await Throws<InvalidDataException>(() => store.SaveAsync("settings.json", settings, MosaicJsonContext.Configured.AppSettings));
}
static async Task UnknownCommand() => await Throws<InvalidOperationException>(() => new CommandRegistry().ExecuteAsync(new("shell.execute", "untrusted")));
static async Task CancelledSave()
{
    var (store, settings, path) = await Seed();
    var before = await File.ReadAllTextAsync(path);
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    await Throws<OperationCanceledException>(() => store.SaveAsync("settings.json", settings, MosaicJsonContext.Configured.AppSettings, cancelled.Token));
    Assert(await File.ReadAllTextAsync(path) == before && settings.Revision == 1, "Cancellation changed the saved version.");
}
