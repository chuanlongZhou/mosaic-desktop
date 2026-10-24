using System.IO;
using System.Text.Json;
using System.Windows;
using Mosaic.Core.Commands;
using Mosaic.Host.Services;
using Mosaic.Host.Views;
using Mosaic.Host.ViewModels;
using Mosaic.Core.Models;
using Mosaic.Platform.Windows;
using Mosaic.Storage.Json;
using Forms = System.Windows.Forms;

namespace Mosaic.Host;

public partial class App : Application
{
    private SingleInstance? _instance;
    private NativeShell? _native;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private AppServices? _services;
    private DesktopOverlay? _overlay;
    private Forms.ToolStripMenuItem? _overlayMenu;
    private DockWindow? _dock;
    private DashboardViewModel? _desktopVm;
    private DesktopSurface? _desktopSurface;
    private DesktopSceneMonitor? _desktopScene;
    private readonly List<DesktopBarWindow> _bars = [];
    private DashboardWindow? _dashboard;
    private PaletteWindow? _palette;
    private SearchSettingsWindow? _searchSettings;
    private EverythingClient? _everything;
    private SearchRuntime? _search;
    private Forms.ToolStripItem? _searchMenu;
    private Forms.ToolStripItem? _libraryMenu, _workspacesMenu;
    private Task _workspaceSwitch = Task.CompletedTask;
    private bool _switching;
    private bool _fullscreen;
    private bool _exiting;
    private bool _exitRequested;
    private string _dataRoot = "";
    private Task? _listener;
    private readonly System.Windows.Threading.DispatcherTimer _shellLayoutTimer = new(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 4 && e.Args[0] == "--shell-guard")
        {
            try { DesktopOverlay.RunGuard(int.Parse(e.Args[1]), e.Args[2], e.Args[3]); }
            catch (Exception) { /* Owner exited before the guard could attach; no shell changes were made. */ }
            Shutdown(); return;
        }
        _dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mosaic");
        var dataIndex = Array.IndexOf(e.Args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < e.Args.Length) _dataRoot = Path.GetFullPath(e.Args[dataIndex + 1]);
        try
        {
            _instance = new SingleInstance(_dataRoot);
            if (!_instance.IsPrimary) { await _instance.NotifyPrimaryAsync(e.Args.Contains("--exit") ? "shutdown" : e.Args.Contains("--library") ? "library" : e.Args.Contains("--workspaces") ? "workspaces" : e.Args.Contains("--dashboard") ? "dashboard" : "palette"); Shutdown(); return; }
            if (e.Args.Contains("--exit")) { Shutdown(); return; }
            _services = new(new JsonDocumentStore(_dataRoot));
            await _services.InitializeAsync();
            if (e.Args.Contains("--overlay"))
            {
                var candidate = _services.CopySettings(); candidate.Desktop.OverlayEnabled = true;
                candidate.Dock.AutoHide = true; candidate.Dock.SyncTaskbarPins = true; candidate.Modules.Dock = true;
                await _services.SaveSettingsAsync(candidate);
            }
            RegisterCommands();
            _services.SettingsChanged += (_, _) =>
            {
                ApplyModules();
                if (!_services.Settings.Search.Enabled) _palette?.Dismiss(false);
                if (_searchMenu is not null) { _searchMenu.Enabled = _services.Settings.Search.Enabled; _searchMenu.Text = "全局搜索  " + NativeShell.DescribeHotkey(_services.Settings.Search); }
                if (_libraryMenu is not null) _libraryMenu.Enabled = _services.Settings.Modules.Library;
                if (_workspacesMenu is not null) _workspacesMenu.Enabled = _services.Settings.Modules.Workspaces;
            };
            _native = new(_services.Settings.Search);
            _native.DesktopModeRequested += async () => await ExecuteAsync(new("desktop.toggle"));
            _native.DashboardRequested += () => ShowDashboard();
            _native.PaletteRequested += ShowPalette;
            _everything = new(_native.Handle);
            _native.CopyDataReceiver = _everything.Receive;
            _search = new(_services, _everything);
            _desktopVm = new(_services, ExecuteAsync, _search.Library);
            _desktopScene = new();
            _desktopScene.Changed += ApplyShellScene;
            _shellLayoutTimer.Tick += (_, _) =>
            {
                _shellLayoutTimer.Stop(); _overlay?.RefreshLayout(); _desktopSurface?.RefreshAttachment();
                if (_services.Settings.Desktop.OverlayEnabled) _desktopScene.Refresh();
                _dock?.EnsureTopmost(); foreach (var bar in _bars) bar.EnsureTopmost();
            };
            _native.ShellLayoutChanged += () => { if (!_shellLayoutTimer.IsEnabled) _shellLayoutTimer.Start(); };
            _native.FullscreenChanged += full => Dispatcher.BeginInvoke(() => { if (_fullscreen != full) { _fullscreen = full; ApplyVisibility(); } });
            CreateTray();
            ApplyModules();
            _listener = _instance.ListenAsync(action => Dispatcher.BeginInvoke(new Action(async () =>
            { if (action == "shutdown") await ExitAsync(); else if (action == "palette") ShowPalette(); else if (action == "library") await ExecuteAsync(new("library.open")); else if (action == "workspaces") await ExecuteAsync(new("workspaces.open")); else ShowDashboard(); })));
            if ((!_services.Settings.Desktop.OverlayEnabled && _services.FirstRun) || _services.Settings.OpenDashboardOnStartup || e.Args.Contains("--dashboard")) ShowDashboard();
            if (e.Args.Contains("--palette")) ShowPalette();
            if (e.Args.Contains("--library")) await ExecuteAsync(new("library.open"));
            if (e.Args.Contains("--workspaces")) await ExecuteAsync(new("workspaces.open"));
            if (_services.Store.Notices.Count > 0)
                MessageBox.Show(string.Join("\n", _services.Store.Notices.Select(x => x.RelativePath + "：" + x.Message)), "Mosaic 配置恢复", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WriteFailure(ex);
            MessageBox.Show(ex.Message, "Mosaic 无法启动", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void RegisterCommands()
    {
        _services!.Commands.Register("desktop.edit", _ =>
        {
            if (_desktopSurface is null) throw new InvalidOperationException("请先启用 Mosaic 桌面。");
            _desktopSurface.BeginEdit();
            if (_dashboard is not null) _dashboard.WindowState = WindowState.Minimized;
            return Task.CompletedTask;
        });
        _services!.Commands.Register("app.exit", _ => ExitAsync());
        _services.Commands.Register("desktop.toggle", async _ =>
        {
            if (_desktopSurface is not null) await _desktopSurface.FlushLayoutAsync();
            var candidate = _services.CopySettings(); candidate.Desktop.OverlayEnabled = !candidate.Desktop.OverlayEnabled;
            if (candidate.Desktop.OverlayEnabled) candidate.Modules.Dock = true;
            await _services.SaveSettingsAsync(candidate);

        });
        _services!.Commands.Register("dashboard.toggle", _ => { if (_dashboard?.IsActive == true) _dashboard.Close(); else ShowDashboard(); return Task.CompletedTask; });
        _services.Commands.Register("settings.open", _ => { ShowDashboard(true); return Task.CompletedTask; });
        _services.Commands.Register("dashboard.open", _ => { ShowDashboard(); return Task.CompletedTask; });
        _services.Commands.Register("palette.toggle", _ => { ShowPalette(); return Task.CompletedTask; });
        _services.Commands.Register("search.settings", _ => { ShowSearchSettings(); return Task.CompletedTask; });
        _services.Commands.Register("search.rebuild", _ => { _search?.RequestRebuild(); return Task.CompletedTask; });
        _services.Commands.Register("resource.open", target => { ShellLauncher.OpenResource(target); return Task.CompletedTask; });
        _services.Commands.Register("resource.reveal", target => { ShellLauncher.Reveal(target); return Task.CompletedTask; });
        _services.Commands.Register("app.builtin", target => { ShellLauncher.OpenBuiltIn(target); return Task.CompletedTask; });
        _services.Commands.Register("app.packaged", target => { ShellLauncher.OpenPackaged(target); return Task.CompletedTask; });
        _services.Commands.Register("library.open", target => { ShowDashboard(section: "library", resourceId: target); return Task.CompletedTask; });
        _services.Commands.Register("library.workspace", target => { ShowDashboard(section: "library", workspaceId: target); return Task.CompletedTask; });
        _services.Commands.Register("workspaces.open", _ => { ShowDashboard(section: "workspaces"); return Task.CompletedTask; });
        _services.Commands.Register("workspace.switch", target =>
        {
            if (_switching) throw new InvalidOperationException("工作空间正在切换，请稍候。");
            var operation = SwitchWorkspaceCoreAsync(target);
            _workspaceSwitch = ObserveWorkspaceSwitchAsync(operation);
            return operation;
        });
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        _overlayMenu = new Forms.ToolStripMenuItem("启用 Mosaic 桌面  Ctrl+Alt+M");
        _overlayMenu.Click += async (_, _) => await ExecuteAsync(new("desktop.toggle")); menu.Items.Add(_overlayMenu);
        _searchMenu = menu.Items.Add("全局搜索  " + NativeShell.DescribeHotkey(_services!.Settings.Search), null, (_, _) => ShowPalette());
        _searchMenu.Enabled = _services.Settings.Search.Enabled;
        menu.Items.Add("搜索目录与快捷键…", null, (_, _) => ShowSearchSettings());
        menu.Items.Add("打开设置中心  Ctrl+Alt+D", null, (_, _) => ShowDashboard());
        _libraryMenu = menu.Items.Add("资源库", null, async (_, _) => await ExecuteAsync(new("library.open")));
        _libraryMenu.Enabled = _services.Settings.Modules.Library;
        _workspacesMenu = menu.Items.Add("工作空间", null, async (_, _) => await ExecuteAsync(new("workspaces.open")));
        _workspacesMenu.Enabled = _services.Settings.Modules.Workspaces;
        menu.Items.Add("Mosaic 设置", null, (_, _) => ShowDashboard(true));
        menu.Items.Add("打开 JSON 配置目录", null, (_, _) => ShellLauncher.OpenResource(_dataRoot));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 Mosaic", null, async (_, _) => await ExitAsync());
        _trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _tray = new Forms.NotifyIcon { Text = "Mosaic Desktop", Icon = _trayIcon, Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => ShowDashboard();
    }

    public void ShowDashboard(bool settings = false, string section = "plugins", string? resourceId = null, string? workspaceId = null)
    {
        if (_services is null || _exiting || _exitRequested) return;
        if (_dashboard is null)
        {
            _dashboard = new(_services, ExecuteAsync, () => _native?.HotKeyRegistered == true, _search, _desktopVm, () => _desktopSurface?.FlushLayoutAsync() ?? Task.CompletedTask);
            _dashboard.Closed += (_, _) => _dashboard = null;
            _dashboard.Show();
        }
        if (_dashboard.WindowState == WindowState.Minimized) _dashboard.WindowState = WindowState.Normal;
        _dashboard.Activate();
        if (!_dashboard.ShowSection(section, resourceId, workspaceId)) throw new InvalidOperationException("入口已关闭，或布局仍在编辑；请检查模块设置并保存或取消布局。");
        if (settings) _dashboard.ShowSettings();
    }

    public void ShowPalette()
    {
        if (_services is null || _search is null || _exiting || _exitRequested || !_services.Settings.Search.Enabled) return;
        if (_searchSettings?.IsVisible == true) { _searchSettings.Activate(); return; }
        if (_palette is null)
        {
            _palette = new(_services, _search, ShowSearchSettings, () => _native?.PaletteHotkeyError);
            _palette.Closed += (_, _) => _palette = null;
        }
        if (_palette.IsVisible && _palette.IsActive) _palette.Dismiss(true);
        else _palette.Open();
    }

    public void ShowSearchSettings()
    {
        if (_services is null || _search is null || _native is null || _exiting || _exitRequested) return;
        _palette?.Dismiss(false);
        if (_searchSettings is not null) { _searchSettings.Activate(); return; }
        var window = new SearchSettingsWindow(_services, _search, async candidate =>
        {
            using var reservation = _native.ReservePaletteHotkey(candidate.Search);
            await _services.SaveSettingsAsync(candidate);
            reservation.Commit();
        }, _native.PaletteHotkeyError);
        _searchSettings = window;
        window.Closed += (_, _) => { _searchSettings = null; if (window.Saved && !_exiting) ShowPalette(); };
        window.Show();
    }

    public async Task ExecuteAsync(CommandRequest request)
    {
        if (_exiting || _exitRequested) return;
        try { await _services!.Commands.ExecuteAsync(request); }
        catch (Exception ex)
        {
            if (_dashboard?.IsVisible == true) _dashboard.SetStatus(ex.Message, true);
            else _tray?.ShowBalloonTip(4000, "Mosaic", ex.Message, Forms.ToolTipIcon.Warning);
        }
    }

    private async Task SwitchWorkspaceCoreAsync(string? target)
    {
        if (_services is null || !_services.Settings.Modules.Workspaces) throw new InvalidOperationException("工作空间入口已关闭，请在设置中启用。");
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("请选择一个工作空间。");
        _switching = true;
        try
        {
            if (_desktopSurface is not null) await _desktopSurface.FlushLayoutAsync();
            if (_dashboard is not null) await _dashboard.PrepareWorkspaceSwitchAsync();
            if (_palette is not null) await _palette.FlushMetadataAsync();
            _palette?.Dismiss(false);
            if (_dashboard is not null) _dashboard.IsEnabled = false;
            var result = await _services.Workspaces.SwitchAsync(target);
            _dashboard?.SetStatus(result.Message, false);
        }
        finally { if (_dashboard is not null) _dashboard.IsEnabled = !_exitRequested; _switching = false; }
    }
    private static async Task ObserveWorkspaceSwitchAsync(Task operation)
    {
        try { await operation; }
        catch (Exception) { /* The invoking command reports the failure; shutdown waits for completion only. */ }
    }

    private void ApplyModules()
    {
        if (_services is null) return;
        var overlay = _services.Settings.Desktop.OverlayEnabled;
        if (overlay)
        {
            _overlay ??= new DesktopOverlay();
            _overlay.Enable();
            _overlay.ConfigureDock(_services.Settings.Modules.Dock, _services.Settings.Dock.AutoHide, _services.Settings.Dock.IconSize, _services.Settings.Desktop.MenuBarAutoHide);
        }
        else { _overlay?.Dispose(); _overlay = null; }
        if (_overlayMenu is not null) { _overlayMenu.Checked = overlay; _overlayMenu.Text = overlay ? "切回 Windows 桌面  Ctrl+Alt+M" : "启用 Mosaic 桌面  Ctrl+Alt+M"; }
        _dashboard?.ApplyDesktopMode(overlay);
        if (overlay && _services.Settings.Modules.Dock)
        {
            if (_dock is null) { _dock = new(_services, ExecuteAsync); _dock.Show(); }
            else _dock.RefreshItems();
        }
        else { _dock?.Close(); _dock = null; }
        if (overlay && _desktopVm is not null)
        {
            _desktopSurface ??= new DesktopSurface(_services, _desktopVm);
            _desktopSurface.Refresh();
        }
        else { _desktopSurface?.Dispose(); _desktopSurface = null; }
        if (overlay && _bars.Count == 0)
            foreach (var screen in Forms.Screen.AllScreens) { var bar = new DesktopBarWindow(_services, ExecuteAsync, screen); _bars.Add(bar); bar.Show(); }
        else if (!overlay) { foreach (var bar in _bars) bar.Close(); _bars.Clear(); }
        foreach (var bar in _bars) bar.RefreshConfiguration();
        _desktopScene?.SetEnabled(overlay);
        if (overlay && _desktopScene is not null) ApplyShellScene(_desktopScene.Current);
        ApplyVisibility();
    }

    private void ApplyShellScene(DesktopScene scene)
    {
        _dock?.SetAutoHideAllowed(scene.AllowsAutoHide);
        foreach (var bar in _bars) bar.SetAutoHideAllowed(scene.AllowsAutoHide);
    }

    private void ApplyVisibility()
    {
        foreach (var window in new Window?[] { _dock })
        {
            if (window is null) continue;
            if (_fullscreen && _services?.Settings.Desktop.OverlayEnabled != true) window.Hide(); else if (!window.IsVisible) window.Show();
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting || _exitRequested) return;
        _exitRequested = true;
        if (_dashboard is not null) _dashboard.IsEnabled = false;
        if (_palette is not null) _palette.IsEnabled = false;
        if (_searchSettings is not null) _searchSettings.IsEnabled = false;
        try { await _workspaceSwitch; if (_desktopSurface is not null) await _desktopSurface.FlushLayoutAsync(); if (_desktopVm is not null) await _desktopVm.FlushAsync(); if (_dashboard is not null) await _dashboard.FlushAsync(); if (_palette is not null) await _palette.FlushAsync(); if (_services is not null) await _services.WaitForWritesAsync(); }
        catch (Exception ex)
        {
            _exitRequested = false;
            if (_dashboard is not null) _dashboard.IsEnabled = true;
            if (_palette is not null) _palette.IsEnabled = true;
            if (_searchSettings is not null) _searchSettings.IsEnabled = true;
            ShowDashboard(); _dashboard?.SetStatus("退出前保存失败：" + ex.Message, true); return;
        }
        _exiting = true;
        _palette?.ClosePermanently();
        _searchSettings?.Close();
        _dashboard?.CloseAfterFlush();
        _dock?.Close();
        _desktopSurface?.Dispose(); _desktopSurface = null;
        foreach (var bar in _bars) bar.Close(); _bars.Clear();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shellLayoutTimer.Stop();
        _desktopScene?.Dispose();
        _desktopSurface?.Dispose();
        _overlay?.Dispose();
        _desktopVm?.Dispose();
        _search?.Dispose();
        _everything?.Dispose();
        _native?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _trayIcon?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void WriteFailure(Exception ex)
    {
        try
        {
            var directory = Path.Combine(_dataRoot, "logs");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "startup.jsonl");
            if (File.Exists(file) && new FileInfo(file).Length > 262144) File.Move(file, file + ".previous", true);
            File.AppendAllText(file, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, error = ex.GetType().Name, message = ex.Message }) + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
