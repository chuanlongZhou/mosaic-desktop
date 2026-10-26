using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Mosaic.Core.Commands;
using Mosaic.Core.Models;
using Mosaic.Host.Controls;
using Mosaic.Host.Services;
using Mosaic.Host.ViewModels;
using Mosaic.Platform.Windows;

namespace Mosaic.Host.Views;

public partial class DashboardWindow : Window
{
    private readonly AppServices _services;
    private readonly Func<bool> _hasHotkey;
    private readonly Func<CommandRequest, Task> _execute;
    private readonly DashboardViewModel _vm;
    private readonly SearchRuntime? _search;
    private readonly Func<Task> _saveDesktopLayout;
    private LibraryView? _library;
    private WorkspacesView? _workspaces;
    private bool _allowClose, _closing, _closed;
    private readonly bool _ownsViewModel;
    private FrameworkElement? _dragCard;
    private double _dragX, _dragY;
    private IInputElement? _settingsReturnFocus;
    private bool _loadingSettings, _settingsDirty;
    public Func<string>? ShortcutStatus { get; set; }

    public DashboardWindow(AppServices services, Func<CommandRequest, Task> execute, Func<bool> hasHotkey, SearchRuntime? search = null, DashboardViewModel? sharedViewModel = null, Func<Task>? saveDesktopLayout = null)
    {
        _services = services;
        _hasHotkey = hasHotkey;
        _execute = execute;
        _search = search;
        _saveDesktopLayout = saveDesktopLayout ?? (() => Task.CompletedTask);
        _ownsViewModel = sharedViewModel is null;
        _vm = sharedViewModel ?? new(services, execute, search?.Library);
        InitializeComponent();
        BuildVersionLabel.Text = $"MOSAIC · V{typeof(App).Assembly.GetName().Version?.ToString(3)}";
        PetSettingsPanel.Changed += MarkSettingsDirty;
        ThemeCombo.ItemsSource = new[] { new Choice("Midnight", "午夜蓝"), new Choice("Graphite", "石墨灰"), new Choice("Forest", "森林绿"), new Choice("Plum", "暮光紫"), new Choice("Light", "晨光浅色") };
        AccentCombo.ItemsSource = new[] { new Choice("Blue", "天空蓝"), new Choice("Mint", "薄荷绿"), new Choice("Violet", "鸢尾紫"), new Choice("Rose", "玫瑰粉"), new Choice("Amber", "琥珀金") };
        SettingsOverlay.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => MarkSettingsDirty()));
        SettingsOverlay.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => MarkSettingsDirty()));
        SettingsOverlay.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => MarkSettingsDirty()));
        DataContext = _vm;
        WidgetContext.SetOwner(this, _vm);
        SourceInitialized += (_, _) => NativeShell.StyleWindow(new WindowInteropHelper(this).Handle, false, false);
        services.SettingsChanged += OnModuleSettingsChanged;
        Closed += (_, _) => { _closed = true; services.SettingsChanged -= OnModuleSettingsChanged; _library?.Dispose(); _workspaces?.Dispose(); _vm.SetMonitoringFor(this, false); if (_ownsViewModel) _vm.Dispose(); };
        Width = Math.Min(1280, SystemParameters.WorkArea.Width - 48);
        Height = Math.Min(850, SystemParameters.WorkArea.Height - 48);
        OnModuleSettingsChanged(this, EventArgs.Empty);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDesktopMode(_services.Settings.Desktop.OverlayEnabled);
        _vm.SetMonitoringFor(this, WindowState != WindowState.Minimized);
    }
    public void ApplyDesktopMode(bool enabled)
    {
        DesktopModeButton.Content = enabled ? "切回 Windows" : "启用 Mosaic 桌面";
        EditDesktopButton.Content = enabled ? "编辑桌面布局" : "启用并编辑桌面";
        DesktopStateLabel.Text = enabled ? "Mosaic 正在运行。选择需要的组件，进入编辑模式移动、缩放或恢复布局。" :
            "当前使用 Windows 桌面。启用 Mosaic 后，所选组件将显示在桌面，并隐藏系统任务栏与桌面图标；可随时切回 Windows。";
        ResizeMode = ResizeMode.CanResizeWithGrip;
    }

    private async void OnDesktopModeClick(object sender, RoutedEventArgs e) => await _execute(new("desktop.toggle"));
    private void OnStateChanged(object? sender, EventArgs e) => _vm.SetMonitoringFor(this, WindowState != WindowState.Minimized);
    private void OnWidgetItemsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width < 1) return;
        _vm.SetColumns(e.NewSize.Width >= 900 ? 12 : e.NewSize.Width >= 600 ? 8 : 4);
    }
    private void OnPluginItemsSizeChanged(object sender, SizeChangedEventArgs e) => _vm.SetPluginColumns(e.NewSize.Width);
    private void OnDesktopClick(object sender, RoutedEventArgs e) => Close();
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeClick(object sender, RoutedEventArgs e) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnEditClick(object sender, RoutedEventArgs e) => _vm.BeginEdit();
    private async void OnEditDesktopClick(object sender, RoutedEventArgs e)
    {
        if (!_services.Settings.Desktop.OverlayEnabled) await _execute(new("desktop.toggle"));
        if (_services.Settings.Desktop.OverlayEnabled) await _execute(new("desktop.edit"));
    }
    private void OnCancelClick(object sender, RoutedEventArgs e) => _vm.CancelEdit();
    private void OnUndoClick(object sender, RoutedEventArgs e) => _vm.Undo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => _vm.Redo();
    private async void OnSaveLayoutClick(object sender, RoutedEventArgs e)
    {
        SaveLayoutButton.IsEnabled = false;
        try { await _vm.SaveLayoutAsync(); }
        catch (Exception ex) { _vm.SetStatus("布局保存失败：" + ex.Message, true); }
        finally { SaveLayoutButton.IsEnabled = true; }
    }

    private void OnRemoveWidgetClick(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.DataContext is WidgetViewModel item) _vm.RemoveWidget(item.Id); }

    private void OnAddWidgetClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            var item = new MenuItem { Header = new WidgetViewModel(new WidgetPlacement { Kind = kind }, null).Title,
                IsEnabled = !_vm.Widgets.Any(x => x.Kind == kind) };
            item.Click += (_, _) => _vm.AddWidget(kind);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (Button)sender;
        menu.IsOpen = true;
    }

    private void OnWidgetDragStarted(object sender, DragStartedEventArgs e)
    {
        if (!_vm.IsEditing) return;
        _dragX = _dragY = 0;
        _dragCard = FindAncestor<ContentPresenter>((DependencyObject)sender);
        if (_dragCard is not null) { Panel.SetZIndex(_dragCard, 100); _dragCard.Opacity = 0.8; }
    }
    private void OnWidgetDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_vm.IsEditing || _dragCard is null) return;
        _dragX += e.HorizontalChange; _dragY += e.VerticalChange;
        _dragCard.RenderTransform = new TranslateTransform(_dragX, _dragY);
    }
    private void OnWidgetDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_dragCard is not null) { _dragCard.RenderTransform = Transform.Identity; _dragCard.Opacity = 1; Panel.SetZIndex(_dragCard, 0); }
        if (_vm.IsEditing && !e.Canceled && (sender as FrameworkElement)?.DataContext is WidgetViewModel widget)
        {
            var cellWidth = (WidgetItems.ActualWidth + DashboardPanel.Gap) / _vm.Columns;
            _vm.MoveWidget(widget.Id, widget.Placement.Column + (int)Math.Round(_dragX / cellWidth),
                widget.Placement.Row + (int)Math.Round(_dragY / DashboardPanel.RowHeight));
        }
        _dragCard = null;
    }
    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(child); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    private void OnTaskInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.ImeProcessedKey != Key.None) return;
        _vm.AddTaskCommand.Execute(null);
        e.Handled = true;
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (SettingsOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { _ = TryDismissSettingsAsync(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { _ = _execute(new("palette.toggle")); e.Handled = true; return; }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && _vm.IsEditing) { _vm.Undo(); e.Handled = true; }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control && _vm.IsEditing) { _vm.Redo(); e.Handled = true; }
    }

    public void ShowSettings()
    {
        if (SettingsOverlay.Visibility == Visibility.Visible) return;
        _loadingSettings = true;
        _settingsReturnFocus = Keyboard.FocusedElement;
        OverlayEnabledCheck.IsChecked = _services.Settings.Desktop.OverlayEnabled;
        MenuBarPinnedCheck.IsChecked = !_services.Settings.Desktop.MenuBarAutoHide;
        SyncPinsCheck.IsChecked = _services.Settings.Dock.SyncTaskbarPins;
        DockEnabledCheck.IsChecked = _services.Settings.Modules.Dock;
        ClockEnabledCheck.IsChecked = _services.Settings.Modules.DesktopClock;
        LibraryEnabledCheck.IsChecked = _services.Settings.Modules.Library;
        WorkspacesEnabledCheck.IsChecked = _services.Settings.Modules.Workspaces;
        DockSizeSlider.Value = _services.Settings.Dock.IconSize;
        DockSizeLabel.Text = $"当前 {_services.Settings.Dock.IconSize} · 24–80，可拖动滑块调整后保存";
        AutoHideCheck.IsChecked = _services.Settings.Dock.AutoHide;
        StartupDashboardCheck.IsChecked = _services.Settings.OpenDashboardOnStartup;
        SnapGridCheck.IsChecked = _services.Settings.Desktop.SnapToGrid;
        PetSettingsPanel.Load(_services.Settings.Pet);
        ThemeCombo.SelectedItem = ThemeCombo.Items.Cast<Choice>().Single(x => x.Id == _services.Settings.Appearance.Theme);
        AccentCombo.SelectedItem = AccentCombo.Items.Cast<Choice>().Single(x => x.Id == _services.Settings.Appearance.Accent);
        TransparencyCheck.IsChecked = _services.Settings.Appearance.TransparencyEnabled;
        AnimationsCheck.IsChecked = _services.Settings.Appearance.AnimationsEnabled;
        SurfaceOpacitySlider.Value = _services.Settings.Appearance.SurfaceOpacity * 100;
        SurfaceOpacitySlider.IsEnabled = TransparencyCheck.IsChecked == true;
        SetShortcutInputs(_services.Settings.Shortcuts, new() { Enabled = _services.Settings.Search.HotkeyEnabled, Modifiers = _services.Settings.Search.HotkeyModifiers, Key = _services.Settings.Search.HotkeyVirtualKey });
        DataPathLabel.Text = _services.Store.Root;
        HotkeyLabel.Text = ShortcutStatus?.Invoke() ?? (_hasHotkey() ? "" : "设置中心快捷键已被占用，可重新绑定。");
        SettingsErrorLabel.Text = "";
        SettingsOverlay.Visibility = Visibility.Visible;
        SidebarChrome.IsEnabled = false; MainContent.IsEnabled = false;
        SettingsTabs.SelectedIndex = 0;
        _settingsDirty = false; _loadingSettings = false;
        UiEffects.Enter(SettingsTabs);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (SettingsOverlay.Visibility != Visibility.Visible) return;
            SettingsTabs.UpdateLayout(); Activate(); ThemeCombo.Focus(); Keyboard.Focus(ThemeCombo);
        }));
    }
    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettings();
    private void OnPetSettingsClick(object sender, RoutedEventArgs e) => ShowPetSettings();
    public void ShowPetSettings() { ShowSettings(); SettingsTabs.SelectedItem = PetSettingsTab; }
    private async void OnSearchClick(object sender, RoutedEventArgs e) => await _execute(new("palette.toggle"));
    private void OnDashboardClick(object sender, RoutedEventArgs e) => ShowSection("plugins");
    private void OnLibraryClick(object sender, RoutedEventArgs e) => ShowSection("library");
    private void OnWorkspacesClick(object sender, RoutedEventArgs e) => ShowSection("workspaces");
    public bool ShowSection(string section, string? resourceId = null, string? workspaceId = null)
    {
        if (_vm.IsEditing && section != "dashboard") { _vm.SetStatus("请先保存或取消正在编辑的布局，再打开其他页面。", true); return false; }
        if (section == "library" && (!_services.Settings.Modules.Library || _search is null)) return false;
        if (section == "workspaces" && !_services.Settings.Modules.Workspaces) return false;
        PluginManagementPage.Visibility = section == "plugins" ? Visibility.Visible : Visibility.Collapsed;
        DashboardPage.Visibility = section == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        SectionContent.Visibility = section is "dashboard" or "plugins" ? Visibility.Collapsed : Visibility.Visible;
        if (section == "library")
        {
            _library ??= new(_services, _search!, resourceId);
            SectionContent.Content = _library; _library.ViewModel.SetActive(true);
            if (resourceId is not null) _library.ViewModel.FocusResource(resourceId);
            if (workspaceId is not null) _library.SelectWorkspace(workspaceId);
        }
        else if (section == "workspaces") { _workspaces ??= new(_services); SectionContent.Content = _workspaces; }
        else SectionContent.Content = null;
        if (section != "library") _library?.ViewModel.SetActive(false);
        _vm.SetMonitoringFor(this, WindowState != WindowState.Minimized);
        foreach (var (button, name) in new[] { (DashboardNav, "plugins"), (LibraryNav, "library"), (WorkspacesNav, "workspaces") })
            if (name == section) button.SetResourceReference(BackgroundProperty, "SelectionBrush"); else button.Background = Brushes.Transparent;
        if (IsLoaded) UiEffects.Enter(section == "plugins" ? PluginManagementPage : section == "dashboard" ? DashboardPage : SectionContent);
        return true;
    }
    private void OnModuleSettingsChanged(object? sender, EventArgs e)
    {
        if (SettingsOverlay.Visibility == Visibility.Visible)
        {
            var loading = _loadingSettings; _loadingSettings = true;
            MenuBarPinnedCheck.IsChecked = !_services.Settings.Desktop.MenuBarAutoHide;
            _loadingSettings = loading;
        }
        PluginManagerItems.ItemsSource = _services.Settings.Desktop.Widgets.Select(x => new PluginCardViewModel(x, _vm)).ToArray();
        ApplyDesktopMode(_services.Settings.Desktop.OverlayEnabled);
        LibraryNav.Visibility = _services.Settings.Modules.Library ? Visibility.Visible : Visibility.Collapsed;
        WorkspacesNav.Visibility = _services.Settings.Modules.Workspaces ? Visibility.Visible : Visibility.Collapsed;
        if (!_services.Settings.Modules.Library && SectionContent.Content is LibraryView || !_services.Settings.Modules.Workspaces && SectionContent.Content is WorkspacesView) ShowSection("plugins");
        if (IsLoaded) _vm.SetMonitoringFor(this, WindowState != WindowState.Minimized);
    }
    private async void OnPluginToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox check || check.DataContext is not PluginCardViewModel card) return;
        var widget = card.Placement;
        var enabled = check.IsChecked == true;
        try
        {
            await _saveDesktopLayout();
            var candidate = _services.CopySettings();
            candidate.Desktop.Widgets.Single(x => x.Kind == widget.Kind).Enabled = enabled;
            if (widget.Kind == WidgetKind.Clock) candidate.Modules.DesktopClock = enabled;
            await _services.SaveSettingsAsync(candidate);
        }
        catch (Exception ex) { _vm.SetStatus(ex.Message, true); OnModuleSettingsChanged(this, EventArgs.Empty); }
    }
    private async void OnDismissSettingsClick(object sender, RoutedEventArgs e) => await TryDismissSettingsAsync();
    private void OnCancelSettingsClick(object sender, RoutedEventArgs e) => CloseSettings();
    private async Task<bool> TryDismissSettingsAsync()
    {
        if (_settingsDirty)
        {
            var result = MessageBox.Show(this, "保存本次设置修改吗？选择“否”将恢复之前的设置和外观。", "尚未保存的设置", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes) return await SaveSettingsAsync();
        }
        CloseSettings(); return true;
    }
    private void CloseSettings()
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        SidebarChrome.IsEnabled = true; MainContent.IsEnabled = true;
        _settingsDirty = false;
        ThemeManager.Apply(_services.Settings.Appearance);
        if (_settingsReturnFocus is { } focus) Keyboard.Focus(focus);
    }
    private void OnOpenDataClick(object sender, RoutedEventArgs e)
    {
        try { ShellLauncher.OpenResource(_services.Store.Root); }
        catch (Exception ex) { SettingsErrorLabel.Text = ex.Message; }
    }
    private void OnDockSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DockSizeLabel is not null) DockSizeLabel.Text = $"{(int)e.NewValue} · 保存后应用";
        MarkSettingsDirty();
    }
    private async void OnSaveSettingsClick(object sender, RoutedEventArgs e) => await SaveSettingsAsync();
    private async Task<bool> SaveSettingsAsync()
    {
        SaveSettingsButton.IsEnabled = false;
        try
        {
            await _saveDesktopLayout();
            var candidate = _services.CopySettings();
            candidate.Desktop.OverlayEnabled = OverlayEnabledCheck.IsChecked == true;
            candidate.Desktop.MenuBarAutoHide = MenuBarPinnedCheck.IsChecked != true;
            candidate.Dock.SyncTaskbarPins = SyncPinsCheck.IsChecked == true;
            candidate.Modules.Dock = DockEnabledCheck.IsChecked == true;
            candidate.Modules.DesktopClock = ClockEnabledCheck.IsChecked == true;
            var clockPlugin = candidate.Desktop.Widgets.SingleOrDefault(x => x.Kind == WidgetKind.Clock);
            if (clockPlugin is not null) clockPlugin.Enabled = candidate.Modules.DesktopClock;
            candidate.Modules.Library = LibraryEnabledCheck.IsChecked == true;
            candidate.Modules.Workspaces = WorkspacesEnabledCheck.IsChecked == true;
            candidate.Dock.IconSize = (int)DockSizeSlider.Value;
            candidate.Dock.AutoHide = AutoHideCheck.IsChecked == true;
            candidate.OpenDashboardOnStartup = StartupDashboardCheck.IsChecked == true;
            candidate.Desktop.SnapToGrid = SnapGridCheck.IsChecked == true;
            candidate.Appearance = AppearanceDraft();
            PetSettingsPanel.ApplyTo(candidate.Pet);
            candidate.Shortcuts = new()
            {
                Desktop = DesktopHotkeyInput.Value.Copy(), Dashboard = DashboardHotkeyInput.Value.Copy(),
                Library = LibraryHotkeyInput.Value.Copy(), Workspaces = WorkspacesHotkeyInput.Value.Copy(), EditLayout = EditHotkeyInput.Value.Copy(),
                PinMenuBar = PinMenuHotkeyInput.Value.Copy()
            };
            candidate.Search.HotkeyEnabled = SearchHotkeyInput.Value.Enabled;
            candidate.Search.HotkeyModifiers = SearchHotkeyInput.Value.Modifiers;
            candidate.Search.HotkeyVirtualKey = SearchHotkeyInput.Value.Key;
            await _services.SaveSettingsAsync(candidate);
            CloseSettings();
            _vm.SetStatus("模块设置已保存");
            return true;
        }
        catch (Exception ex) { SettingsErrorLabel.Text = ex.Message; return false; }
        finally { SaveSettingsButton.IsEnabled = true; }
    }

    private void MarkSettingsDirty() { if (!_loadingSettings && SettingsOverlay is not null && SettingsOverlay.Visibility == Visibility.Visible) _settingsDirty = true; }
    private AppearanceSettings AppearanceDraft() => new()
    {
        Theme = (ThemeCombo.SelectedItem as Choice)?.Id ?? _services.Settings.Appearance.Theme,
        Accent = (AccentCombo.SelectedItem as Choice)?.Id ?? _services.Settings.Appearance.Accent,
        TransparencyEnabled = TransparencyCheck.IsChecked == true, AnimationsEnabled = AnimationsCheck.IsChecked == true,
        SurfaceOpacity = SurfaceOpacitySlider.Value / 100
    };
    private void PreviewAppearance()
    {
        if (_loadingSettings || SettingsOverlay?.Visibility != Visibility.Visible) return;
        MarkSettingsDirty(); SurfaceOpacitySlider.IsEnabled = TransparencyCheck.IsChecked == true;
        ThemeManager.Apply(AppearanceDraft());
    }
    private void OnAppearanceChanged(object sender, SelectionChangedEventArgs e) => PreviewAppearance();
    private void OnAppearanceToggle(object sender, RoutedEventArgs e) => PreviewAppearance();
    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel is not null) OpacityLabel.Text = $"表面不透明度 · {e.NewValue:0}%";
        PreviewAppearance();
    }
    private void SetShortcutInputs(ShortcutSettings shortcuts, ShortcutBinding search)
    {
        DesktopHotkeyInput.Value = shortcuts.Desktop.Copy(); DashboardHotkeyInput.Value = shortcuts.Dashboard.Copy();
        LibraryHotkeyInput.Value = shortcuts.Library.Copy(); WorkspacesHotkeyInput.Value = shortcuts.Workspaces.Copy();
        EditHotkeyInput.Value = shortcuts.EditLayout.Copy(); SearchHotkeyInput.Value = search;
        PinMenuHotkeyInput.Value = shortcuts.PinMenuBar.Copy();
    }
    private void OnResetHotkeysClick(object sender, RoutedEventArgs e) { SetShortcutInputs(new(), new() { Modifiers = 3, Key = 0x20 }); MarkSettingsDirty(); }
    private sealed record Choice(string Id, string Label);

    public async Task FlushAsync()
    {
        await _vm.FlushAsync();
        if (_library is not null) await _library.FlushAsync();
        if (_workspaces is not null) await _workspaces.FlushAsync();
    }
    public async Task PrepareWorkspaceSwitchAsync()
    {
        if (_vm.IsEditing) throw new InvalidOperationException("请先保存或取消正在编辑的 Dashboard 布局，再切换工作空间。");
        await _vm.FlushAsync();
        if (_library is not null) await _library.ViewModel.FlushMetadataAsync();
    }
    public void CloseAfterFlush() { _allowClose = true; if (!_closed) Close(); }
    public void SetStatus(string status, bool error) => _vm.SetStatus(status, error);
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            if (SettingsOverlay.Visibility == Visibility.Visible && !await TryDismissSettingsAsync()) return;
            await FlushAsync();
            _allowClose = true;
            // Even an already-completed save must leave the original Closing callback before Close is called again.
            _ = Dispatcher.BeginInvoke(new Action(() => { if (!_closed) Close(); }));
        }
        catch (Exception ex) { _vm.SetStatus("关闭前保存失败：" + ex.Message, true); }
        finally { _closing = false; }
    }
}
