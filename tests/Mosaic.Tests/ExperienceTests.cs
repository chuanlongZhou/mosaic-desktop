using System.Text.Json;
using System.Runtime.InteropServices;
using Mosaic.Core.Models;
using Mosaic.Core.Search;
using Mosaic.Host.Services;
using Mosaic.Platform.Windows;
using Mosaic.Storage.Json;

internal static class ExperienceTests
{
    public static Task SettingsCompatibility()
    {
        var settings = JsonSerializer.Deserialize("{\"revision\":2,\"desktop\":{\"widgets\":[]}}", MosaicJsonContext.Configured.AppSettings)!;
        settings.Validate();
        if (settings.Appearance.Theme != "Midnight" || !settings.Appearance.AnimationsEnabled || !settings.Search.HotkeyEnabled || settings.Shortcuts.Desktop.Key != 0x4D || settings.Shortcuts.PinMenuBar.Key != 0x54 || !settings.Shortcuts.PinMenuBar.Enabled)
            throw new Exception("Legacy settings did not receive appearance and shortcut defaults.");
        settings.Appearance = new() { Theme = "Light", Accent = "Rose", TransparencyEnabled = true, SurfaceOpacity = .73, AnimationsEnabled = false };
        settings.Shortcuts.Library = new() { Modifiers = 6, Key = 0x79 };
        settings.Shortcuts.PinMenuBar = new() { Modifiers = 6, Key = 0x77 };
        settings.Search.HotkeyEnabled = false;
        var copy = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(settings, MosaicJsonContext.Configured.AppSettings), MosaicJsonContext.Configured.AppSettings)!;
        copy.Validate();
        if (copy.Appearance.Theme != "Light" || copy.Appearance.Accent != "Rose" || copy.Appearance.SurfaceOpacity != .73 || copy.Appearance.AnimationsEnabled || copy.Search.HotkeyEnabled || copy.Shortcuts.Library.Key != 0x79 || copy.Shortcuts.PinMenuBar.Key != 0x77)
            throw new Exception("Experience settings failed to persist.");
        copy.Shortcuts.Library = copy.Shortcuts.Desktop.Copy();
        ExpectInvalid(copy.Validate, "Duplicate shortcut was accepted.");
        copy.Shortcuts.Library.Enabled = false; copy.Validate();
        copy.Appearance.SurfaceOpacity = double.NaN;
        ExpectInvalid(copy.Validate, "Non-finite opacity was accepted.");
        foreach (var kind in Enum.GetValues<WidgetKind>())
            if (DesktopPluginPlacement.Defaults().Count(x => x.Kind == kind) != 1) throw new Exception("A component is absent or duplicated in defaults.");
        foreach (var json in new[]
        {
            "{\"search\":{\"hotkeyModifiers\":3,\"hotkeyVirtualKey\":84}}",
            "{\"shortcuts\":{\"editLayout\":{\"modifiers\":3,\"key\":84}}}"
        })
        {
            var legacy = JsonSerializer.Deserialize(json, MosaicJsonContext.Configured.AppSettings)!; legacy.Validate();
            if (legacy.Shortcuts.PinMenuBar.Enabled) throw new Exception("New pin shortcut invalidated an existing Ctrl+Alt+T binding.");
        }
        var duplicatePin = new AppSettings(); duplicatePin.Shortcuts.PinMenuBar = duplicatePin.Shortcuts.Desktop.Copy();
        ExpectInvalid(duplicatePin.Validate, "An explicitly duplicated pin shortcut was accepted.");
        return Task.CompletedTask;
    }
    public static async Task MenuPinPersistence()
    {
        var root = Path.Combine(Path.GetTempPath(), "Mosaic.Tests", "menu-pin-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new JsonDocumentStore(root)); await services.InitializeAsync();
        await services.ToggleMenuBarPinAsync();
        if (services.Settings.Desktop.MenuBarAutoHide) throw new Exception("Pin toggle did not keep the top menu visible.");
        await Task.WhenAll(services.ToggleMenuBarPinAsync(), services.ToggleMenuBarPinAsync());
        if (services.Settings.Desktop.MenuBarAutoHide) throw new Exception("Rapid pin toggles lost an update.");
        var restarted = new AppServices(new JsonDocumentStore(root)); await restarted.InitializeAsync();
        if (restarted.Settings.Desktop.MenuBarAutoHide) throw new Exception("Pinned mode did not survive restart.");
        await restarted.ToggleMenuBarPinAsync();
        if (!restarted.Settings.Desktop.MenuBarAutoHide) throw new Exception("Unpin did not restore automatic hiding.");
        var path = Path.Combine(root, "profiles/default/settings.json");
        var changed = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(path))!;
        changed["revision"] = restarted.Settings.Revision + 10;
        File.WriteAllText(path, changed.ToJsonString());
        try { await restarted.ToggleMenuBarPinAsync(); throw new Exception("Conflicting pin save was accepted."); }
        catch (JsonConflictException) { }
        if (!restarted.Settings.Desktop.MenuBarAutoHide) throw new Exception("A rejected save changed the active pin mode.");
    }
    public static async Task WorkspaceDesktop()
    {
        var root = Path.Combine(Path.GetTempPath(), "Mosaic.Tests", "experience-" + Guid.NewGuid().ToString("N"));
        var services = new AppServices(new JsonDocumentStore(root)); await services.InitializeAsync();
        var settings = services.CopySettings();
        settings.Desktop.Widgets.Single(x => x.Kind == WidgetKind.Notes).Left = 132;
        await services.SaveSettingsAsync(settings);
        var writing = await services.Workspaces.CreateAsync("写作", "writing");
        await services.Workspaces.SwitchAsync(writing.Id);
        var writingSettings = services.CopySettings();
        writingSettings.Desktop.Widgets.Single(x => x.Kind == WidgetKind.Notes).Left = 456;
        writingSettings.Desktop.Widgets.Single(x => x.Kind == WidgetKind.Gpu).Enabled = true;
        await services.SaveSettingsAsync(writingSettings);
        await services.Workspaces.SwitchAsync("personal");
        if (services.Settings.Desktop.Widgets.Single(x => x.Kind == WidgetKind.Notes).Left != 132 || services.Settings.Desktop.Widgets.Single(x => x.Kind == WidgetKind.Gpu).Enabled)
            throw new Exception("Desktop layout leaked into the previous workspace.");
        await services.Workspaces.SwitchAsync(writing.Id);
        if (services.Settings.Desktop.Widgets.Single(x => x.Kind == WidgetKind.Notes).Left != 456 || !services.Settings.Desktop.Widgets.Single(x => x.Kind == WidgetKind.Gpu).Enabled)
            throw new Exception("Workspace did not restore desktop positions and enabled components.");
        var reloaded = new AppServices(new JsonDocumentStore(root)); await reloaded.InitializeAsync();
        if (reloaded.Settings.Desktop.Widgets.Single(x => x.Kind == WidgetKind.Notes).Left != 456) throw new Exception("Desktop layout was not durable after restart.");
    }
    public static Task HotkeyTransactions()
    {
        static ShortcutSettings Disabled() { var shortcuts = new ShortcutSettings(); foreach (var entry in shortcuts.Entries()) entry.Binding.Enabled = false; return shortcuts; }
        using var owner = new NativeShell(new() { Enabled = false }, Disabled());
        using var blocker = new NativeShell(new() { Enabled = false }, Disabled());
        AppSettings Config(uint key) => new() { Shortcuts = Disabled(), Search = new() { HotkeyModifiers = 7, HotkeyVirtualKey = key } };
        var first = Config(0x78); var occupied = Config(0x79);
        first.Shortcuts.PinMenuBar = new() { Modifiers = 7, Key = 0x77 };
        using (var reservation = owner.ReserveHotkeys(first)) reservation.Commit();
        var pinRequests = 0; owner.MenuBarPinRequested += () => pinRequests++;
        var actions = (Dictionary<int, string>)typeof(NativeShell).GetField("_actions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
        var pinId = actions.Single(x => x.Value == "menu-pin").Key;
        SendMessage(owner.Handle, 0x312, new nint(pinId), 0);
        if (pinRequests != 1) throw new Exception("Native pin shortcut did not dispatch its action.");
        owner.CaptureHotkey = binding => binding.Identity == first.Shortcuts.PinMenuBar.Identity;
        SendMessage(owner.Handle, 0x312, new nint(pinId), 0);
        if (pinRequests != 1) throw new Exception("Recording a pin shortcut triggered pin mode.");
        owner.CaptureHotkey = null;
        using (var reservation = blocker.ReserveHotkeys(occupied)) reservation.Commit();
        var failed = Config(0x79); failed.Shortcuts.Library = new() { Modifiers = 7, Key = 0x7A };
        try { using var reservation = owner.ReserveHotkeys(failed); throw new Exception("Occupied shortcut unexpectedly succeeded."); }
        catch (InvalidOperationException) { }
        if (!owner.PaletteHotkeyRegistered) throw new Exception("Rejected changes removed the previous shortcut.");
        using var verifier = new NativeShell(new() { Enabled = false }, Disabled());
        using (var reservation = verifier.ReserveHotkeys(Config(0x7A))) reservation.Commit();
        using (var rolledBack = owner.ReserveHotkeys(Config(0x7B))) { }
        using var verifier2 = new NativeShell(new() { Enabled = false }, Disabled());
        using (var reservation = verifier2.ReserveHotkeys(Config(0x7B))) reservation.Commit();
        return Task.CompletedTask;
    }
    public static async Task HardwareAvailability()
    {
        using var monitor = new HardwareMonitor();
        var first = monitor.Read(); await Task.Delay(150); var second = monitor.Read();
        foreach (var value in new[] { first.GpuPercent, second.GpuPercent })
            if (value is double load && (!double.IsFinite(load) || load is < 0 or > 100)) throw new Exception("GPU sample is outside its valid range.");
        foreach (var sensor in second.Sensors)
            if (!double.IsFinite(sensor.Value) || sensor.Type is not ("Temperature" or "Fan")) throw new Exception("Invalid hardware sensor reading.");
        Console.WriteLine($"  hardware: GPU={second.GpuPercent?.ToString("0") ?? "unavailable"}, sensors={second.Sensors.Count}; {second.SensorStatus}");
        monitor.Dispose();
        if (monitor.Read().GpuPercent is not null) throw new Exception("Disposed hardware sampler returned live readings.");
    }
    private static void ExpectInvalid(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception(message);
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nint parameter, nint value);
}
