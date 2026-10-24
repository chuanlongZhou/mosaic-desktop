using System.Text.Json;
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
        if (settings.Appearance.Theme != "Midnight" || !settings.Appearance.AnimationsEnabled || !settings.Search.HotkeyEnabled || settings.Shortcuts.Desktop.Key != 0x4D)
            throw new Exception("Legacy settings did not receive appearance and shortcut defaults.");
        settings.Appearance = new() { Theme = "Light", Accent = "Rose", TransparencyEnabled = true, SurfaceOpacity = .73, AnimationsEnabled = false };
        settings.Shortcuts.Library = new() { Modifiers = 6, Key = 0x79 };
        settings.Search.HotkeyEnabled = false;
        var copy = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(settings, MosaicJsonContext.Configured.AppSettings), MosaicJsonContext.Configured.AppSettings)!;
        copy.Validate();
        if (copy.Appearance.Theme != "Light" || copy.Appearance.Accent != "Rose" || copy.Appearance.SurfaceOpacity != .73 || copy.Appearance.AnimationsEnabled || copy.Search.HotkeyEnabled || copy.Shortcuts.Library.Key != 0x79)
            throw new Exception("Experience settings failed to persist.");
        copy.Shortcuts.Library = copy.Shortcuts.Desktop.Copy();
        ExpectInvalid(copy.Validate, "Duplicate shortcut was accepted.");
        copy.Shortcuts.Library.Enabled = false; copy.Validate();
        copy.Appearance.SurfaceOpacity = double.NaN;
        ExpectInvalid(copy.Validate, "Non-finite opacity was accepted.");
        foreach (var kind in Enum.GetValues<WidgetKind>())
            if (DesktopPluginPlacement.Defaults().Count(x => x.Kind == kind) != 1) throw new Exception("A component is absent or duplicated in defaults.");
        return Task.CompletedTask;
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
        using (var reservation = owner.ReserveHotkeys(first)) reservation.Commit();
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
}
