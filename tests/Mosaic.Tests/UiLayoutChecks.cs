using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Mosaic.Host.Services;
using Mosaic.Host.ViewModels;
using Mosaic.Host.Views;
using Mosaic.Storage.Json;
using Mosaic.Core.Search;
using Mosaic.Core.Models;

internal static class UiLayoutChecks
{
    public static Task RunAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            DashboardWindow? window = null;
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Mosaic;component/Themes/Dark.xaml", UriKind.Relative) });
                CheckMenuTheme();
                DesktopPetChecks.Check();
                var root = Path.Combine(Path.GetTempPath(), "Mosaic.Tests", Guid.NewGuid().ToString("N"));
                var services = new AppServices(new JsonDocumentStore(root));
                services.InitializeAsync().GetAwaiter().GetResult();
                window = new DashboardWindow(services, _ => Task.CompletedTask, () => true);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1280, 850));
                content.Arrange(new Rect(0, 0, 1280, 850));
                content.UpdateLayout();
                var editors = Descendants(content).OfType<TextBox>().ToArray();
                foreach (var editor in editors)
                    Console.WriteLine($"  editor {AutomationProperties.GetName(editor)}: {editor.ActualWidth:0} x {editor.ActualHeight:0}");
                var notes = editors.Single(x => AutomationProperties.GetName(x) == "随手记");
                if (notes.ActualWidth < 80 || notes.ActualHeight < 70)
                    throw new Exception("Notes editor has no usable hit area.");
                notes.Text = "原生笔记输入检查";
                notes.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (((DashboardViewModel)window.DataContext).Notes != "原生笔记输入检查")
                    throw new Exception("Notes did not bind back to the view model.");
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var vm = (DashboardViewModel)window.DataContext;
                Complete(vm.FlushAsync());
                if (services.Content.Notes != "原生笔记输入检查") throw new Exception("Editor content did not persist.");
                vm.BeginEdit();
                vm.MoveWidget("cpu", 3, 0);
                Complete(vm.SaveLayoutAsync());
                if (services.Workspace.Layouts["12"].Single(x => x.Id == "cpu").Column != 3)
                    throw new Exception("Layout save did not commit the moved widget.");
                window.ShowSettings();
                ((CheckBox)window.FindName("AutoHideCheck")).IsChecked = true;
                ((Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => services.Settings.Dock.AutoHide);
                var persisted = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(root, "profiles/default/settings.json")), MosaicJsonContext.Configured.AppSettings)!;
                if (!persisted.Dock.AutoHide) throw new Exception("Settings did not persist the module edit.");
                new WindowInteropHelper(window).EnsureHandle();
                var closed = false;
                window.Closed += (_, _) => closed = true;
                window.Close();
                PumpUntil(() => closed);
                CheckExperience(root + ".experience");
                CheckDesktopPlugins(services);
                CheckDesktopScene();
                CheckDockAutoHide(services);
                CheckMenuBarAutoHide(services);
                CheckMenuBarMotion(services);
                CheckNotificationArea(services);
                CheckPalette(services, root);
                CheckLibraryPaging(root + ".paging");
                CheckImagePreview(root);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally { window?.CloseAfterFlush(); Application.Current?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return completion.Task;
    }

    private static void CheckExperience(string root)
    {
        var services = new AppServices(new JsonDocumentStore(root)); Complete(services.InitializeAsync());
        var window = new DashboardWindow(services, _ => Task.CompletedTask, () => true);
        try
        {
            window.ShowSection("plugins"); window.Show(); PumpUntil(() => window.IsLoaded);
            var pluginItems = (ItemsControl)window.FindName("PluginManagerItems");
            if (Descendants(pluginItems).OfType<TextBox>().Any()) throw new Exception("Component previews expose editable text boxes.");
            window.Width = 720; window.Height = 560; window.UpdateLayout();
            PumpUntil(() => ((DashboardViewModel)window.DataContext).PluginColumns == 2);
            window.ShowSettings();
            var theme = (ComboBox)window.FindName("ThemeCombo");
            PumpUntil(() => theme.IsKeyboardFocusWithin);
            if (((FrameworkElement)window.FindName("MainContent")).IsEnabled || ((FrameworkElement)window.FindName("SidebarChrome")).IsEnabled)
                throw new Exception("Settings overlay did not isolate background keyboard input.");
            theme.SelectedIndex = 4;
            ((ComboBox)window.FindName("AccentCombo")).SelectedIndex = 3;
            ((CheckBox)window.FindName("AnimationsCheck")).IsChecked = false;
            ((CheckBox)window.FindName("TransparencyCheck")).IsChecked = true;
            ((Slider)window.FindName("SurfaceOpacitySlider")).Value = 75;
            ((Mosaic.Host.Controls.HotkeyInput)window.FindName("PinMenuHotkeyInput")).Value = new() { Modifiers = 6, Key = 0x77 };
            if (ThemeManager.Current.Theme != "Light" || UiEffects.Enabled || ((SolidColorBrush)Application.Current.Resources["WidgetBrush"]).Color.A >= 255)
                throw new Exception("Live appearance preview did not apply theme, opacity and motion settings.");
            ((Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => ((Grid)window.FindName("SettingsOverlay")).Visibility == Visibility.Collapsed);
            if (services.Settings.Appearance.Theme != "Light" || services.Settings.Appearance.Accent != "Rose" || services.Settings.Appearance.AnimationsEnabled || !services.Settings.Appearance.TransparencyEnabled || services.Settings.Shortcuts.PinMenuBar.Key != 0x77)
                throw new Exception("Appearance controls did not persist the chosen combination.");
            window.ShowSettings(); theme.SelectedIndex = 2;
            ((Button)window.FindName("CancelSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (ThemeManager.Current.Theme != "Light" || services.Settings.Appearance.Theme != "Light") throw new Exception("Cancelling a preview did not restore the saved appearance.");
            foreach (var name in AppearanceSettings.Themes)
            {
                ThemeManager.Apply(new() { Theme = name, AnimationsEnabled = false });
                if (((SolidColorBrush)Application.Current.Resources["WidgetBrush"]).Color.A != 255) throw new Exception("Opaque mode retained transparent component surfaces.");
            }
            ThemeManager.Apply(new() { AnimationsEnabled = true });
            if (!UiEffects.Enabled) throw new Exception("The enabled Mosaic animation setting did not enable UI motion.");
            ThemeManager.Apply(new() { AnimationsEnabled = false });
            var vm = (DashboardViewModel)window.DataContext;
            vm.NewTask = "待办编辑检查"; vm.AddTaskCommand.Execute(null);
            var task = vm.Tasks.Last(); task.Title = "已编辑的待办";
            vm.RemoveTaskCommand.Execute(task); vm.UndoRemoveTaskCommand.Execute(null);
            if (!vm.Tasks.Contains(task) || task.Title != "已编辑的待办") throw new Exception("Task undo lost edited content.");
            Complete(vm.FlushAsync());
            var contentPath = Path.Combine(root, "profiles/default/widget-data/local-content.json");
            var original = File.ReadAllBytes(contentPath);
            var external = System.Text.Json.Nodes.JsonNode.Parse(original)!;
            external["revision"] = services.Content.Revision + 10;
            File.WriteAllText(contentPath, external.ToJsonString());
            vm.Notes = "保存冲突检查";
            try { Complete(vm.FlushAsync()); throw new Exception("Externally changed content unexpectedly saved."); }
            catch (JsonConflictException) { }
            if (!vm.ContentSaveError || !vm.ContentSaveStatus.Contains("保存失败")) throw new Exception("Desktop content did not expose a save conflict.");
            File.WriteAllBytes(contentPath, original); Complete(vm.FlushAsync());
            if (vm.ContentSaveError || services.Content.Notes != "保存冲突检查") throw new Exception("Content save did not recover after retry.");
            Console.WriteLine("  experience: live theme/opacity/motion, save/cancel, isolated focus, task edit/undo and content-conflict retry verified");
        }
        finally { window.CloseAfterFlush(); ThemeManager.Apply(new() { AnimationsEnabled = false }); }
    }

    private static void CheckDesktopPlugins(AppServices services)
    {
        using var vm = new DashboardViewModel(services, _ => Task.CompletedTask);
        var management = new DashboardWindow(services, _ => Task.CompletedTask, () => true, sharedViewModel: vm);
        using var surface = new DesktopSurface(services, vm);
        var plugin = surface.Widgets[WidgetKind.Notes];
        try
        {
            management.ApplyDesktopMode(true);
            if (management.ResizeMode != ResizeMode.CanResizeWithGrip || management.WindowState == WindowState.Maximized)
                throw new Exception("Overlay mode forced the management window to fullscreen or disabled resizing.");
            management.ShowSection("plugins");
            management.Show();
            PumpUntil(() => plugin.IsLoaded);
            Console.WriteLine($"  desktop actual layout: root {surface.Content.ActualWidth} x {surface.Content.ActualHeight}, notes {plugin.ActualWidth} x {plugin.ActualHeight}, visible {plugin.IsVisible}");
            if (surface.Content.ActualWidth < 600 || surface.Content.ActualHeight < 400 || plugin.ActualWidth < 160 || plugin.ActualHeight < 100)
                throw new Exception("Desktop canvas or widget has no usable rendered area.");
            if (surface.Handle == 0 || Window.GetWindow(plugin) is not null) throw new Exception("Desktop controls are still hosted in ordinary WPF windows.");
            var move = (System.Windows.Controls.Primitives.Thumb)plugin.FindName("MoveHandle");
            var size = (System.Windows.Controls.Primitives.Thumb)plugin.FindName("SizeHandle");
            void Drag(System.Windows.Controls.Primitives.Thumb thumb, double x, double y) => thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(x, y) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            var oldLeft = Canvas.GetLeft(plugin); var oldWidth = plugin.Width;
            Drag(move, 50, 50); Drag(size, 50, 50);
            if (plugin.IsEditing || move.IsVisible || size.IsVisible || Canvas.GetLeft(plugin) != oldLeft || plugin.Width != oldWidth) throw new Exception("Locked desktop controls can be moved or resized.");
            surface.BeginEdit();
            Drag(move, 100 - Canvas.GetLeft(plugin), 120 - Canvas.GetTop(plugin));
            Drag(size, 410 - plugin.Width, 285 - plugin.Height);
            Complete(surface.FlushLayoutAsync());
            var saved = services.Settings.Desktop.Widgets.Single(x => x.Kind == WidgetKind.Notes);
            if (Math.Abs(saved.Width - 410) > 1 || Math.Abs(saved.Height - 285) > 1 || saved.Left != 100 || saved.Top != 120 || surface.IsEditing) throw new Exception("Desktop layout did not persist or lock on completion.");
            surface.BeginEdit(); Drag(move, 30, 40); Drag(size, 20, 10); surface.CancelEdit();
            move.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(30, 40, true) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            if (Canvas.GetLeft(plugin) != saved.Left || plugin.Width != saved.Width || plugin.IsEditing) throw new Exception("Cancel did not restore the saved desktop layout.");
            var editor = Descendants(plugin).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "随手记");
            if (PresentationSource.FromVisual(editor) is not HwndSource source || source.Handle != surface.Handle || !source.UsesPerPixelOpacity)
                throw new Exception("Notes are not rendered in the transparent desktop child canvas.");
            editor.Focus();
            PumpUntil(() => editor.IsKeyboardFocused);
            var beforeInput = editor.Text;
            SendMessage(source.Handle, 0x0102, new nint('Z'), new nint(1));
            PumpUntil(() => editor.Text != beforeInput);
            editor.Text = "桌面插件与设置窗口共享内容";
            editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); Complete(vm.FlushAsync());
            management.CloseAfterFlush();
            vm.Notes += "，关闭管理窗口后继续编辑"; Complete(vm.FlushAsync());
            if (!services.Content.Notes.EndsWith("关闭管理窗口后继续编辑", StringComparison.Ordinal)) throw new Exception("Closing management disposed the shared desktop content model.");
            var dock = new DockWindow(services, _ => Task.CompletedTask);
            try
            {
                if (Math.Abs(dock.DockHeight - Math.Ceiling(60 * services.Settings.Dock.IconSize / 40.0 + 24)) > 1) throw new Exception("Dock size did not follow settings.");
                var settings = services.CopySettings(); settings.Dock.IconSize = 72; Complete(services.SaveSettingsAsync(settings));
                if (dock.DockHeight <= 100) throw new Exception("Dock did not grow with icon size.");
            }
            finally { dock.Close(); }
            surface.Dispose();
            using var restored = new DesktopSurface(services, vm);
            PumpUntil(() => restored.Widgets[WidgetKind.Notes].IsLoaded);
            if (restored.IsEditing || Canvas.GetLeft(restored.Widgets[WidgetKind.Notes]) != 100 || restored.Widgets[WidgetKind.Notes].Width != 410)
                throw new Exception("Recreating the desktop did not restore a locked, saved layout.");
            Console.WriteLine("  desktop canvas: child controls, locked placement, edit/save/cancel and shared notes after management close; Dock sizing verified");
        }
        finally { management.CloseAfterFlush(); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    private static void CheckMenuBarAutoHide(AppServices services)
    {
        var originalAreas = ShellWorkArea.Capture();
        var fixture = new Window { Title = "Mosaic menu work-area fixture", WindowState = WindowState.Maximized };
        fixture.Show();
        var settings = services.CopySettings(); settings.Desktop.MenuBarAutoHide = true; Complete(services.SaveSettingsAsync(settings));
        var bar = new DesktopBarWindow(services, request => request.Id == "menubar.pin.toggle" ? services.ToggleMenuBarPinAsync() : Task.CompletedTask, System.Windows.Forms.Screen.PrimaryScreen!);
        bar.SetAutoHideAllowed(true);
        void CheckWorkArea(bool reserved)
        {
            var handle = new WindowInteropHelper(bar).Handle;
            var fixtureHandle = new WindowInteropHelper(fixture).Handle;
            PumpUntil(() =>
            {
                GetWindowRect(handle, out var bounds);
                var area = ShellWorkArea.Capture().First(a => a.Left == bounds.Left && a.Right == bounds.Right);
                var original = originalAreas.First(a => a.Left == bounds.Left && a.Right == bounds.Right);
                DwmGetWindowAttribute(fixtureHandle, 9, out var frame, Marshal.SizeOf<ShellWorkArea.Rect>());
                return bar.ReservesWorkArea == reserved && area.Top == (reserved ? bounds.Bottom : original.Top) && Math.Abs(frame.Top - area.Top) <= 2;
            });
        }
        try
        {
            bar.Show(); PumpUntil(() => bar.IsLoaded && bar.IsAutoHidden);
            if (bar.IsVisible) throw new Exception("Collapsed top bar left a visible activation strip.");
            CheckWorkArea(false);
            bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            if (bar.IsAutoHidden || !bar.IsVisible || bar.Height < 32) throw new Exception("Top menu bar did not expand on pointer entry.");
            CheckWorkArea(true);
            var menuButton = (Button)bar.FindName("MenuButton");
            menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            Complete(Task.Delay(1100));
            if (bar.IsAutoHidden || menuButton.ContextMenu?.IsOpen != true) throw new Exception("Top menu bar hid while its menu was in use.");
            menuButton.ContextMenu.IsOpen = false;
            PumpUntil(() => bar.IsAutoHidden);
            if (bar.IsVisible) throw new Exception("Top bar did not fully hide after its menu closed.");
            CheckWorkArea(false);
            bar.SetAutoHideAllowed(false);
            bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            Complete(Task.Delay(1100));
            if (bar.IsAutoHidden || !bar.IsVisible) throw new Exception("Top menu hid on the desktop or without a maximized app.");
            CheckWorkArea(true);
            bar.SetAutoHideAllowed(true);
            PumpUntil(() => bar.IsAutoHidden);
            var pinButton = (Button)bar.FindName("PinMenuButton");
            pinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => !services.Settings.Desktop.MenuBarAutoHide && pinButton.IsEnabled);
            if (bar.IsAutoHidden || bar.Height < 32) throw new Exception("Always-visible top menu setting did not apply.");
            if (!Equals(pinButton.Content, "已固定")) throw new Exception("Pinned mode indicator did not update.");
            CheckWorkArea(true);
            bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            Complete(Task.Delay(1100));
            if (bar.IsAutoHidden) throw new Exception("Pinned menu auto-hid after pointer exit.");
            pinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => bar.IsAutoHidden);
            if (!services.Settings.Desktop.MenuBarAutoHide || !Equals(pinButton.Content, "固定")) throw new Exception("Unpin did not restore automatic mode and indicator.");
            CheckWorkArea(false);
            Console.WriteLine("  top menu: visible/edge-revealed bar reserves work area below its bottom, existing maximized window resizes, hidden bar releases space; menu hold and setting changes verified");
        }
        finally { bar.Close(); fixture.Close(); ShellWorkArea.Restore(originalAreas); }
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out ShellWorkArea.Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out ShellWorkArea.Rect rect, int size);

    private static void CheckMenuBarMotion(AppServices services)
    {
        var originalAreas = ShellWorkArea.Capture();
        var originalMotion = UiEffects.Enabled;
        var screen = System.Windows.Forms.Screen.PrimaryScreen!;
        var originalTop = originalAreas.First(a => a.Left == screen.Bounds.Left && a.Right == screen.Bounds.Right).Top;
        var fixture = new Window { Title = "Mosaic animated work-area fixture", WindowState = WindowState.Maximized };
        var bar = new DesktopBarWindow(services, _ => Task.CompletedTask, screen);
        var heights = new List<double>();
        bar.WorkAreaChanged += () => heights.Add(bar.ReservedHeight);
        void Enter() => bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
        void Leave() => bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
        void CheckBoundary(bool visible)
        {
            PumpUntil(() =>
            {
                var area = ShellWorkArea.Capture().First(a => a.Left == screen.Bounds.Left && a.Right == screen.Bounds.Right);
                GetWindowRect(new WindowInteropHelper(bar).Handle, out var bounds);
                DwmGetWindowAttribute(new WindowInteropHelper(fixture).Handle, 9, out var frame, Marshal.SizeOf<ShellWorkArea.Rect>());
                return area.Top == (visible ? bounds.Bottom : originalTop) && Math.Abs(frame.Top - area.Top) <= 2;
            });
        }
        try
        {
            UiEffects.Enabled = true;
            fixture.Show();
            GetWindowRect(new WindowInteropHelper(fixture).Handle, out var initialFixtureBounds);
            bar.SetAutoHideAllowed(true); bar.Show();
            PumpUntil(() => bar.IsAutoHidden);
            Enter();
            if (!bar.IsTransitioning) throw new Exception("Enabled menu motion jumped directly to the expanded state.");
            PumpUntil(() => !bar.IsTransitioning);
            if (heights.Where(h => h > 0 && h < ShellWorkArea.BarHeight).Distinct().Count() < 2)
                throw new Exception("Menu expansion did not animate intermediate work-area heights.");
            CheckBoundary(true);
            var paintedMenu = (FrameworkElement)bar.Content;
            paintedMenu.UpdateLayout();
            if (paintedMenu.ActualHeight < 31.9) throw new Exception("Expanded menu content is still clipped to a thin strip.");
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(paintedMenu.ActualWidth), (int)Math.Ceiling(paintedMenu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(paintedMenu);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var visiblePixels = 0; var textPixels = 0;
            for (var pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                if (pixels[pixel + 3] > 128) visiblePixels++;
                if (pixels[pixel] > 180 && pixels[pixel + 1] > 180 && pixels[pixel + 2] > 180) textPixels++;
            }
            if (visiblePixels < bitmap.PixelWidth * 20 || textPixels < 30) throw new Exception("Expanded menu did not paint its surface and labels.");
            var imageDirectory = Path.GetFullPath(".artifacts/menu-recovery"); Directory.CreateDirectory(imageDirectory);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(imageDirectory, "expanded-menu.png"))) encoder.Save(output);
            // Reproduce an already-maximized Mosaic window that did not process
            // the shell's appbar resize. Explicit fitting must include our own
            // ordinary windows while continuing to exclude shell tool windows.
            SetWindowPos(new WindowInteropHelper(fixture).Handle, 0, initialFixtureBounds.Left, initialFixtureBounds.Top,
                initialFixtureBounds.Right - initialFixtureBounds.Left, initialFixtureBounds.Bottom - initialFixtureBounds.Top, 0x34);
            ShellWorkArea.Apply(0, true, new Dictionary<System.Drawing.Rectangle, double> { [screen.Bounds] = ShellWorkArea.BarHeight });
            CheckBoundary(true);
            Leave();
            try { PumpUntil(() => bar.IsTransitioning && bar.ReservedHeight > 0 && bar.ReservedHeight < ShellWorkArea.BarHeight); }
            catch (TimeoutException ex) { throw new Exception($"Menu collapse did not reach an intermediate frame: transitioning={bar.IsTransitioning}, hidden={bar.IsAutoHidden}, hover={bar.IsMouseOver}, height={bar.ReservedHeight}, frames={string.Join(',', heights.Select(h => h.ToString("F1")))}", ex); }
            var beforeReverse = bar.ReservedHeight;
            Enter();
            if (Math.Abs(beforeReverse - bar.ReservedHeight) > .01) throw new Exception("Reversing menu motion jumped to an endpoint.");
            PumpUntil(() => !bar.IsTransitioning); CheckBoundary(true);
            Leave(); PumpUntil(() => bar.IsTransitioning && bar.ReservedHeight < ShellWorkArea.BarHeight);
            var menuButton = (Button)bar.FindName("MenuButton");
            menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => !bar.IsTransitioning); Complete(Task.Delay(1100));
            if (bar.IsAutoHidden || menuButton.ContextMenu?.IsOpen != true)
                throw new Exception("Opening a menu during collapse did not reverse and hold the bar.");
            menuButton.ContextMenu.IsOpen = false;
            PumpUntil(() => bar.IsTransitioning && bar.ReservedHeight < ShellWorkArea.BarHeight);
            UiEffects.Enabled = false;
            if (bar.IsTransitioning || !bar.IsAutoHidden || bar.IsVisible || bar.ReservesWorkArea)
                throw new Exception("Disabling motion during collapse did not finish and release space immediately.");
            CheckBoundary(false);
            UiEffects.Enabled = true; Enter();
            PumpUntil(() => bar.ReservedHeight > 0 && bar.ReservedHeight < ShellWorkArea.BarHeight);
            UiEffects.Enabled = false;
            if (bar.IsTransitioning || bar.ReservedHeight != ShellWorkArea.BarHeight || !bar.IsVisible)
                throw new Exception("Disabling motion during expansion did not finish immediately.");
            CheckBoundary(true);
            UiEffects.Enabled = true; Leave();
            PumpUntil(() => bar.IsTransitioning && bar.ReservedHeight < ShellWorkArea.BarHeight);
        }
        finally
        {
            bar.Close();
            PumpUntil(() => ShellWorkArea.Capture().SequenceEqual(originalAreas));
            fixture.Close(); UiEffects.Enabled = originalMotion;
            ShellWorkArea.Restore(originalAreas);
        }
        Console.WriteLine($"  menu motion: {heights.Count} intermediate/endpoint updates, smooth reversal/menu hold, live animation-disable, maximized-window fitting and close during transition verified");
    }

    private static void CheckNotificationArea(AppServices services)
    {
        using var fixture = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "Mosaic tray callback fixture", Visible = true };
        var clicks = new List<System.Windows.Forms.MouseButtons>();
        var doubleClicks = 0;
        fixture.MouseClick += (_, e) => clicks.Add(e.Button);
        fixture.MouseDoubleClick += (_, _) => doubleClicks++;
        // Start after the fixture: TaskbarCreated must import an already running
        // application, including icons that Explorer had placed in overflow.
        var bar = new DesktopBarWindow(services, _ => Task.CompletedTask, System.Windows.Forms.Screen.PrimaryScreen!, true);
        bar.SetAutoHideAllowed(false);
        try
        {
            bar.Show(); PumpUntil(() => bar.IsLoaded);
            var field = typeof(DesktopBarWindow).GetField("_systemTray", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var tray = (SystemTrayService)field.GetValue(bar)!;
            if (tray.Area.IsFailed) throw new Exception("Notification area failed to initialize.");
            PumpUntil(() => tray.Area.TrayIcons.Any(i => i.Title == fixture.Text));
            var icon = tray.Area.TrayIcons.Single(i => i.Title == fixture.Text);
            if (icon.Icon is null || icon.HWnd == 0 || icon.CallbackMessage == 0) throw new Exception("Tray fixture lost its image or application callback.");
            var overflow = (Button)bar.FindName("HiddenIconsButton");
            overflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var popupField = typeof(DesktopBarWindow).GetField("_statusPopup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var popup = (System.Windows.Controls.Primitives.Popup)popupField.GetValue(bar)!;
            PumpUntil(() => popup.IsOpen && Descendants(popup.Child).OfType<Button>().Any(b => ReferenceEquals(b.DataContext, icon)));
            var button = Descendants(popup.Child).OfType<Button>().Single(b => ReferenceEquals(b.DataContext, icon));
            Complete(Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 50));
            void Click(System.Windows.Input.MouseButton mouseButton)
            {
                foreach (var routedEvent in new[] { UIElement.PreviewMouseDownEvent, UIElement.PreviewMouseUpEvent })
                    button.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, mouseButton) { RoutedEvent = routedEvent });
            }
            Click(System.Windows.Input.MouseButton.Left);
            PumpUntil(() => clicks.Contains(System.Windows.Forms.MouseButtons.Left));
            Click(System.Windows.Input.MouseButton.Right);
            PumpUntil(() => clicks.Contains(System.Windows.Forms.MouseButtons.Right));
            Click(System.Windows.Input.MouseButton.Left);
            PumpUntil(() => doubleClicks > 0);
            Complete(Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 50));
            var clicksBeforeKeyboard = clicks.Count;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => clicks.Count > clicksBeforeKeyboard);
            bar.SetAutoHideAllowed(true);
            bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
            Complete(Task.Delay(1100));
            if (bar.IsAutoHidden || !popup.IsOpen) throw new Exception("Top bar hid while hidden tray applications were in use.");
            popup.IsOpen = false;
            var clock = (Button)bar.FindName("ClockButton");
            clock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            popup = (System.Windows.Controls.Primitives.Popup)popupField.GetValue(bar)!;
            if (!popup.IsOpen || !Descendants(popup.Child).OfType<System.Windows.Controls.Calendar>().Any()) throw new Exception("Clock did not open the calendar.");
            popup.IsOpen = false;
            fixture.Visible = false;
            PumpUntil(() => !tray.Area.TrayIcons.Contains(icon));
            Console.WriteLine("  notification area: pre-existing hidden app imported, actual image, left/right callbacks, live removal, popup auto-hide hold and calendar verified");
        }
        finally { bar.Close(); }
    }

    private static void CheckMenuTheme()
    {
        var menu = new ContextMenu { Width = 300 };
        foreach (var title in new[] { "桌面模式与 Dock 设置", "添加应用或文件…", "添加文件夹…", "添加工作台入口", "添加全局搜索入口" })
            menu.Items.Add(new MenuItem { Header = title });
        menu.Items.Add(new Separator());
        var parent = new MenuItem { Header = "更多操作" };
        var child = new MenuItem { Header = "显示菜单栏", IsCheckable = true, IsChecked = true, InputGestureText = "Ctrl+M" };
        var clicked = false;
        child.Click += (_, _) => clicked = true;
        parent.Items.Add(child);
        parent.Items.Add(new MenuItem { Header = "当前不可用", IsEnabled = false });
        menu.Items.Add(parent);
        var directory = Path.GetFullPath(".artifacts/menu-theme");
        Directory.CreateDirectory(directory);
        void Capture(FrameworkElement root, string name)
        {
            root.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(output);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            // Sample the former light icon gutter, away from text and icons.
            var sample = (Math.Min(18, bitmap.PixelHeight - 1) * bitmap.PixelWidth + 6) * 4;
            if (pixels[sample] > 160 && pixels[sample + 1] > 160 && pixels[sample + 2] > 160)
                throw new Exception("Menu retained the default white icon gutter.");
        }
        try
        {
            menu.IsOpen = true;
            PumpUntil(() => menu.ActualHeight > 100);
            Capture(menu, "context-menu");
            parent.IsSubmenuOpen = true;
            var popup = (System.Windows.Controls.Primitives.Popup)parent.Template.FindName("PART_Popup", parent);
            PumpUntil(() => popup.IsOpen && child.IsVisible);
            Capture((FrameworkElement)popup.Child, "submenu");
            child.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (!clicked) throw new Exception("The themed submenu lost its command callback.");
            Console.WriteLine("  dark menus: popup and nested submenu rendered without light gutters; checkmarks, disabled items and callbacks verified");
        }
        finally { parent.IsSubmenuOpen = false; menu.IsOpen = false; }
    }

    private static void CheckDesktopScene()
    {
        if (new DesktopScene(true, true).AllowsAutoHide || new DesktopScene(true, false).AllowsAutoHide || new DesktopScene(false, false).AllowsAutoHide || !new DesktopScene(false, true).AllowsAutoHide)
            throw new Exception("Desktop/maximum-window auto-hide policy is incorrect.");
        var fixture = new Window { Title = "Mosaic scene fixture", Width = 360, Height = 240 };
        try
        {
            fixture.Show(); var handle = new WindowInteropHelper(fixture).Handle;
            if (DesktopSceneMonitor.IsMaximizedApplication(handle)) throw new Exception("Ordinary window counted as maximized.");
            fixture.WindowState = WindowState.Maximized;
            PumpUntil(() => DesktopSceneMonitor.IsMaximizedApplication(handle));
            if (!DesktopSceneMonitor.Capture().HasMaximizedApplication) throw new Exception("Native scene did not discover the maximized fixture.");
            fixture.WindowState = WindowState.Minimized;
            PumpUntil(() => !DesktopSceneMonitor.IsMaximizedApplication(handle));
            fixture.WindowState = WindowState.Normal; fixture.Hide();
            if (DesktopSceneMonitor.IsMaximizedApplication(handle)) throw new Exception("Hidden window counted as maximized.");
            Console.WriteLine("  scene: desktop override, no-maximized policy and native maximize/minimize/hidden-window detection verified");
        }
        finally { fixture.Close(); }
    }

    private static void CheckDockAutoHide(AppServices services)
    {
        var settings = services.CopySettings(); settings.Dock.AutoHide = true; Complete(services.SaveSettingsAsync(settings));
        var dock = new DockWindow(services, _ => Task.CompletedTask);
        try
        {
            dock.Show(); PumpUntil(() => dock.IsLoaded);
            dock.SetAutoHideAllowed(false);
            dock.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            Complete(Task.Delay(1100));
            if (dock.IsAutoHidden || !dock.IsVisible) throw new Exception("Dock hid without a maximized app.");
            // Keep the user's pointer position out of this timer check. Hiding
            // this fixture also suspends its edge-reveal polling until restored.
            dock.Hide(); PumpUntil(() => !dock.IsMouseOver);
            dock.SetAutoHideAllowed(true); PumpUntil(() => dock.IsAutoHidden);
            dock.SetAutoHideAllowed(false);
            if (dock.IsAutoHidden || !dock.IsVisible) throw new Exception("Returning to desktop did not expand Dock.");
            Complete(Task.Delay(1100));
            if (dock.IsAutoHidden) throw new Exception("A stale hide timer collapsed the desktop Dock.");
            Console.WriteLine("  Dock: no-maximized stay visible, maximized hide, desktop return immediately expands and cancels stale timers");
        }
        finally { dock.Close(); }
    }

    private static void CheckPalette(AppServices services, string root)
    {
        var files = root + ".files";
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "搜索甲.txt"), "alpha");
        File.WriteAllText(Path.Combine(files, "搜索乙.txt"), "beta");
        var settings = services.CopySettings();
        settings.Search.Roots = [files];
        settings.Search.UseEverything = false;
        Complete(services.SaveSettingsAsync(settings));
        using var native = new NativeShell();
        using var everything = new EverythingClient(native.Handle);
        native.CopyDataReceiver = everything.Receive;
        var externalOptions = settings.Search.Copy();
        externalOptions.UseEverything = true;
        var externalQuery = everything.SearchAsync(new("搜索乙"), externalOptions, CancellationToken.None);
        Complete(externalQuery);
        if (externalQuery.Result.Results.Any(x => !SearchIdentity.IsWithin(x.Item.Target, files))) throw new Exception("Everything escaped the selected roots.");
        Console.WriteLine("  optional IPC: " + externalQuery.Result.Status);
        CheckHotkeyReservation(native);
        using var runtime = new SearchRuntime(services, everything);
        var palette = new PaletteWindow(services, runtime, () => { });
        try
        {
            new WindowInteropHelper(palette).EnsureHandle();
            var content = (FrameworkElement)palette.Content;
            content.Measure(new Size(940, 620));
            content.Arrange(new Rect(0, 0, 940, 620));
            content.UpdateLayout();
            var input = (TextBox)palette.FindName("QueryInput");
            var results = (ListBox)palette.FindName("ResultList");
            if (input.ActualWidth < 300 || results.ActualHeight < 150) throw new Exception("Palette has unusable query/results geometry.");
            var vm = (PaletteViewModel)palette.DataContext;
            vm.Activate();
            vm.Query = "搜索甲";
            vm.Query = "搜索乙";
            PumpUntil(() => !vm.IsSearching && vm.Results.Any(x => x.Item.Name == "搜索乙.txt"));
            if (vm.Results.Any(x => x.Item.Name == "搜索甲.txt")) throw new Exception("Stale query replaced the latest results.");
            vm.Selected = vm.Results.Single(x => x.Item.Name == "搜索乙.txt");
            var selectedKey = vm.Selected.Item.Key;
            vm.TagsText = "中文, test";
            Complete(vm.SaveMetadataAsync(false));
            Complete(vm.SaveMetadataAsync(true));
            PumpUntil(() => !vm.IsSearching && vm.Results.Any(x => x.Item.Key == selectedKey && x.Item.IsFavorite));
            if (vm.Selected?.Item.Key != selectedKey) throw new Exception("Progressive results lost the selected resource.");
            var pin = vm.PinToDockAsync();
            Complete(vm.FlushAsync());
            if (!pin.IsCompletedSuccessfully) throw new Exception("Exit flush returned before the complete Dock pin action.");
            var pinned = services.Settings.Dock.Items.Single(x => x.Target == Path.Combine(files, "搜索乙.txt"));
            if (pinned.ResourceId is null || pinned.ResourceId != vm.Selected.Item.ResourceId) throw new Exception("Dock did not share the stable resource ID.");
            vm.IsComposing = true;
            vm.SuspendQueries();
            vm.Query = "搜索甲";
            if (vm.IsSearching) throw new Exception("IME composition dispatched an intermediate query.");
            vm.IsComposing = false;
            vm.RequestSearch();
            PumpUntil(() => !vm.IsSearching && vm.Results.Any(x => x.Item.Name == "搜索甲.txt"));
            vm.FilterCommand.Execute(vm.Filters.Single(x => x.Value == SearchFilter.Apps));
            PumpUntil(() => !vm.IsSearching);
            if (vm.Results.Any(x => x.Item.Kind != SearchKind.App)) throw new Exception("Apps filter leaked other resource kinds.");
            vm.Query = "notepad";
            PumpUntil(() => !vm.IsSearching && vm.Results.Any(x => x.Item.Command == "app.builtin" && x.Item.Target == "notepad"));
            var notepad = vm.Results.First(x => x.Item.Command == "app.builtin" && x.Item.Target == "notepad");
            vm.Selected = notepad;
            string? executed = null;
            services.Commands.Register("app.builtin", target => { executed = target; return Task.CompletedTask; });
            var execute = vm.ExecuteSelectedAsync();
            Complete(execute);
            if (!execute.Result || executed != "notepad" || !runtime.Library.Recent.Any(x => x.Key == notepad.Item.Key)) throw new Exception("Selected application did not use the shared command/history path.");
            Complete(vm.FlushAsync());
            vm.Deactivate();
            if (vm.Results.Count != 0) throw new Exception("Hidden palette retained result views.");
            Console.WriteLine($"  palette query {input.ActualWidth:0}px, results {results.ActualHeight:0}px; latest query / IME / favorite / tags / Dock verified");
            CheckLibraryAndWorkspaces(services, runtime, files);
        }
        finally { palette.ClosePermanently(); }
    }

    private static void CheckLibraryAndWorkspaces(AppServices services, SearchRuntime runtime, string files)
    {
        var modelPath = Path.Combine(files, "Qwen_资源库.gguf");
        File.WriteAllText(modelPath, "fixture only");
        using var library = new LibraryView(services, runtime);
        var vm = library.ViewModel;
        Complete(vm.InitializeAsync());
        Complete(vm.AddPathsAsync([modelPath]));
        vm.Query = "Qwen";
        Complete(vm.RefreshAsync());
        if (vm.Items.Count != 1 || vm.Selected?.Kind != SearchKind.Model) throw new Exception("Library did not use the existing resource search/type model.");
        vm.TagsText = "AI Models, 中文"; Complete(vm.SaveMetadataAsync(true));
        vm.TagsText = "AI Models, 中文"; Complete(vm.SaveMetadataAsync(false));
        var resourceId = vm.Selected!.ResourceId!;
        if (!runtime.Library.Resources.Single(x => x.ResourceId == resourceId).IsFavorite) throw new Exception("Library favorite was not visible to Palette's shared source.");
        var editor = new CollectionEditorWindow(vm.Collections, runtime.Library.Resources);
        ((TextBox)editor.FindName("NameInput")).Text = "UI AI Models";
        ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => editor.Saved);
        var collection = vm.Collections.Collections.Single(x => x.Name == "UI AI Models");
        Complete(vm.SetCollectionMemberAsync(collection.Id, true));
        vm.SelectedNavigation = vm.Navigation.Single(x => x.Id == "collection:" + collection.Id);
        Complete(vm.RefreshAsync());
        if (vm.Items.Single().ResourceId != resourceId) throw new Exception("Collection navigation did not show shared resource IDs.");
        Complete(vm.SetViewModeAsync("list"));
        library.Measure(new Size(1160, 710)); library.Arrange(new Rect(0, 0, 1160, 710)); library.UpdateLayout();
        var query = (TextBox)library.FindName("LibraryQuery"); var list = (ListBox)library.FindName("ResourcesList");
        if (query.ActualWidth < 200 || list.ActualHeight < 200) throw new Exception("Resource Library query/list geometry is unusable.");
        Complete(vm.AddPathsAsync([Path.Combine(files, "搜索甲.txt")]));
        vm.Query = ""; vm.SelectedNavigation = vm.Navigation.Single(x => x.Id == "all"); Complete(vm.RefreshAsync());
        list.SelectedItems.Clear();
        list.SelectedItems.Add(vm.Items.Single(x => x.ResourceId == resourceId));
        list.SelectedItems.Add(vm.Items.Single(x => x.Name == "搜索甲.txt"));
        vm.TagsText = "批量标签"; Complete(vm.SaveMetadataAsync(false));
        if (runtime.Library.Resources.Count(x => x.Tags.Contains("批量标签")) != 2) throw new Exception("Bulk tags did not update both selected resources.");
        vm.FocusResource(resourceId); Complete(vm.RefreshAsync());
        var ai = services.Workspaces.CreateAsync("UI AI Lab", "ai"); Complete(ai);
        Complete(vm.AddToWorkspaceAsync(ai.Result.Id));
        var aiCopy = services.Workspaces.GetCopyAsync(ai.Result.Id); Complete(aiCopy);
        if (!aiCopy.Result.ResourceIds.Contains(resourceId)) throw new Exception("Library workspace pin lost the resource ID.");
        vm.Query = "does-not-exist"; vm.FocusWorkspace(ai.Result.Id); Complete(vm.RefreshAsync());
        if (vm.Query.Length != 0 || vm.Items.Single().ResourceId != resourceId) throw new Exception("Opening workspace resources retained an unrelated filter.");
        var workbench = new DashboardWindow(services, _ => Task.CompletedTask, () => true, runtime);
        var dashboard = (DashboardViewModel)workbench.DataContext;
        dashboard.BeginEdit();
        try { Complete(workbench.PrepareWorkspaceSwitchAsync()); throw new Exception("Switch accepted an unsaved layout draft."); }
        catch (InvalidOperationException) { }
        dashboard.CancelEdit();
        services.Commands.Register("workspace.switch", async id => { await workbench.PrepareWorkspaceSwitchAsync(); await services.Workspaces.SwitchAsync(id!); });
        using var workspaceView = new WorkspacesView(services);
        workspaceView.ViewModel.Selected = workspaceView.ViewModel.Items.Single(x => x.Id == ai.Result.Id);
        Complete(workspaceView.ViewModel.SwitchAsync());
        if (services.Workspace.Id != ai.Result.Id || dashboard.WorkspaceName != "UI AI Lab") throw new Exception("Workspace switch did not update the existing Dashboard.");
        PumpUntil(() => dashboard.QuickLinks.Any(x => x.Title == "Qwen_资源库.gguf"));
        var palette = new PaletteViewModel(services, runtime);
        palette.Activate(); palette.Query = "UI AI Lab";
        PumpUntil(() => !palette.IsSearching && palette.Results.Any(x => x.Item.Command == "workspace.switch" && x.Item.Target == ai.Result.Id));
        palette.Dispose();
        vm.SelectedNavigation = vm.Navigation.Single(x => x.Id == "current"); vm.Query = "";
        Complete(vm.RefreshAsync());
        if (vm.Items.Single().ResourceId != resourceId || !vm.Items.Single().IsInCurrentWorkspace) throw new Exception("Current workspace library filter did not follow activation.");
        Complete(vm.RemoveFromWorkspaceAsync());
        if (!File.Exists(modelPath) || vm.Items.Count != 0) throw new Exception("Removing workspace membership affected the file or filter.");
        workbench.CloseAfterFlush();
        Console.WriteLine($"  library query {query.ActualWidth:0}px, list {list.ActualHeight:0}px; editor / bulk tags / shared metadata / workspace activation / search verified");
    }

    private static void CheckHotkeyReservation(NativeShell native)
    {
        using var blocker = new NativeShell();
        var options = new SearchSettings { HotkeyModifiers = 7 };
        var bound = false;
        foreach (var key in new uint[] { 0x78, 0x79, 0x7A, 0x7B })
        {
            options.HotkeyVirtualKey = key;
            try { using var reservation = blocker.ReservePaletteHotkey(options); reservation.Commit(); bound = true; break; }
            catch (InvalidOperationException) { }
        }
        if (!bound) { Console.WriteLine("  hotkey conflict test skipped: all fixture shortcuts are occupied"); return; }
        try { using var conflict = native.ReservePaletteHotkey(options); throw new Exception("Occupied hotkey unexpectedly registered twice."); }
        catch (InvalidOperationException) { }
        if (native.PaletteHotkeyRegistered) throw new Exception("Rejected hotkey changed the original registration.");
        Console.WriteLine("  native hotkey collision rejected without changing the original binding");
    }

    private static void CheckLibraryPaging(string root)
    {
        var services = new AppServices(new JsonDocumentStore(root)); Complete(services.InitializeAsync());
        const string relative = "profiles/default/resources/00.json";
        var baseline = services.Store.LoadAsync(relative, MosaicJsonContext.Configured.ResourceShard, () => new()); Complete(baseline);
        var shard = new ResourceShard { Resources = Enumerable.Range(0, 142).Select(i => new RegisteredResource
        {
            Id = "00" + i.ToString("x30"), Name = "Resource " + i.ToString("D3"), Kind = SearchKind.File, Target = Path.Combine(root, "file-" + i + ".txt")
        }).ToList() };
        Complete(services.Store.SaveAsync(relative, shard, MosaicJsonContext.Configured.ResourceShard));
        using var native = new NativeShell(); using var everything = new EverythingClient(native.Handle); using var runtime = new SearchRuntime(services, everything);
        using var vm = new LibraryViewModel(services, runtime); Complete(vm.InitializeAsync());
        if (vm.Items.Count != 36 || !vm.CanNext) throw new Exception("Card page rendered an unbounded resource list.");
        vm.MovePage(1);
        if (vm.Items.Count != 36 || !vm.CanPrevious) throw new Exception("Card paging lost its bounds.");
        Complete(vm.SetViewModeAsync("list"));
        if (vm.Items.Count != 100 || !vm.CanNext) throw new Exception("List page exceeded 100 resources.");
        vm.MovePage(1);
        if (vm.Items.Count != 42 || vm.CanNext) throw new Exception("Last list page contains the wrong resources.");
        vm.Query = "Resource 141"; Complete(vm.RefreshAsync());
        if (vm.Items.Single().Name != "Resource 141") throw new Exception("Filtering omitted metadata outside the first page.");
        Console.WriteLine("  library paging: 142 resources, cards <= 36, virtual list <= 100, off-page filtering verified");
    }

    private static void CheckImagePreview(string root)
    {
        foreach (var width in new[] { 12, 12001 })
        {
            var file = Path.Combine(root, "preview-" + width + ".png");
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, 1, 96, 96, PixelFormats.Bgra32, null, new byte[width * 4], width * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file)) encoder.Save(stream);
            var preview = ResourcePreview.Read(new("preview", SearchKind.File, "resource.open", file, "test"), CancellationToken.None);
            if (width == 12 && (preview.Image is null || !preview.Image.IsFrozen || preview.Image.PixelWidth > 256 || preview.Image.PixelHeight > 180)) throw new Exception("Image preview did not produce a bounded frozen bitmap.");
            if (width > 12000 && (preview.Image is not null || !preview.Text.Contains("尺寸"))) throw new Exception("Oversized source dimensions were not rejected before preview decode.");
        }
        Console.WriteLine("  image previews: frozen bitmap, bounded output and oversized-source rejection verified");
    }

    private static void Complete(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> completed)
    {
        if (completed()) return;
        var frame = new DispatcherFrame();
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (completed() || (DateTime.UtcNow - started).TotalSeconds > 5) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (!completed()) throw new TimeoutException("WPF operation did not complete within 5 seconds.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
