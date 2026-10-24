using System.Text.Json;
using Mosaic.Core.Commands;
using Mosaic.Core.Models;
using Mosaic.Storage.Json;

namespace Mosaic.Host.Services;

public sealed class AppServices(JsonDocumentStore store)
{
    public JsonDocumentStore Store { get; } = store;
    public ActiveProfile Profile { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;
    public WorkspaceDocument Workspace { get; private set; } = null!;
    public WidgetContent Content { get; private set; } = null!;
    public CommandRegistry Commands { get; } = new();
    public bool FirstRun { get; private set; }
    public WorkspaceManager Workspaces { get; private set; } = null!;
    internal SemaphoreSlim StateGate { get; } = new(1, 1);
    public event EventHandler? SettingsChanged;
    public event EventHandler? WorkspaceActivated;
    public Func<AppSettings, NativeShell.HotkeyReservation>? ReserveHotkeys { get; set; }
    private string ProfilePath => "profiles/" + Profile.ProfileId + "/";

    public async Task InitializeAsync()
    {
        Profile = await Store.LoadAsync("active-profile.json", MosaicJsonContext.Configured.ActiveProfile, () => new());
        FirstRun = !File.Exists(Path.Combine(Store.Root, ProfilePath + "settings.json"));
        Settings = await Store.LoadAsync(ProfilePath + "settings.json", MosaicJsonContext.Configured.AppSettings,
            () => Defaults.Settings(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        // Add new built-in components without changing existing positions or enabled states.
        foreach (var placement in DesktopPluginPlacement.Defaults())
            if (Settings.Desktop.Widgets.All(x => x.Kind != placement.Kind))
            { placement.Enabled = false; Settings.Desktop.Widgets.Add(placement); }
        Workspace = await Store.LoadAsync(ProfilePath + "workspaces/" + Settings.CurrentWorkspaceId + ".json", MosaicJsonContext.Configured.WorkspaceDocument,
            () => { var workspace = Defaults.Workspace(); workspace.Id = Settings.CurrentWorkspaceId; return workspace; });
        if (Workspace.Id != Settings.CurrentWorkspaceId) throw new InvalidDataException("工作空间文件 ID 与设置引用不一致，原文件已保留。");
        Content = await Store.LoadAsync(ProfilePath + "widget-data/local-content.json", MosaicJsonContext.Configured.WidgetContent, () => new());
        if (FirstRun)
        {
            await Store.SaveAsync("active-profile.json", Profile, MosaicJsonContext.Configured.ActiveProfile);
            await Store.SaveAsync(ProfilePath + "workspaces/" + Workspace.Id + ".json", Workspace, MosaicJsonContext.Configured.WorkspaceDocument);
            await Store.SaveAsync(ProfilePath + "widget-data/local-content.json", Content, MosaicJsonContext.Configured.WidgetContent);
            await Store.SaveAsync(ProfilePath + "settings.json", Settings, MosaicJsonContext.Configured.AppSettings);
        }
        Workspaces = new(this);
        await Workspaces.InitializeAsync();
    }

    public AppSettings CopySettings() => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(Settings, MosaicJsonContext.Configured.AppSettings), MosaicJsonContext.Configured.AppSettings)!;
    public WorkspaceDocument CopyWorkspace() => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(Workspace, MosaicJsonContext.Configured.WorkspaceDocument), MosaicJsonContext.Configured.WorkspaceDocument)!;

    public async Task SaveSettingsAsync(AppSettings candidate)
    {
        await StateGate.WaitAsync();
        try
        {
            if (candidate.CurrentWorkspaceId != Settings.CurrentWorkspaceId) throw new InvalidOperationException("请使用工作空间切换入口修改当前工作空间。");
            candidate.Validate();
            using var hotkeys = ReserveHotkeys?.Invoke(candidate);
            await Store.SaveAsync(ProfilePath + "settings.json", candidate, MosaicJsonContext.Configured.AppSettings);
            hotkeys?.Commit();
            Settings = candidate;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { StateGate.Release(); }
    }
    public async Task SaveWorkspaceAsync(WorkspaceDocument candidate)
    {
        await StateGate.WaitAsync();
        try
        {
            if (candidate.Id != Workspace.Id) throw new InvalidOperationException("当前工作空间已变化，请重新打开布局。");
            await Store.SaveAsync(ProfilePath + "workspaces/" + Workspace.Id + ".json", candidate, MosaicJsonContext.Configured.WorkspaceDocument);
            Workspace = candidate;
            Workspaces?.Publish(candidate);
        }
        finally { StateGate.Release(); }
    }
    public async Task SaveContentAsync(WidgetContent candidate)
    {
        candidate.Revision = Content.Revision;
        await Store.SaveAsync(ProfilePath + "widget-data/local-content.json", candidate, MosaicJsonContext.Configured.WidgetContent);
        Content = candidate;
    }

    internal void SetWorkspaceSnapshot(WorkspaceDocument workspace) => Workspace = workspace;
    internal void ActivateWorkspace(AppSettings settings, WorkspaceDocument workspace)
    {
        Settings = settings; Workspace = workspace;
        WorkspaceActivated?.Invoke(this, EventArgs.Empty);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task WaitForWritesAsync()
    {
        await StateGate.WaitAsync(); StateGate.Release();
    }
}
