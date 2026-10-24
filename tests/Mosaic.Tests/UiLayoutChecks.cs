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
                CheckDesktopPlugins(services);
                CheckDesktopScene();
                CheckDockAutoHide(services);
                CheckMenuBarAutoHide(services);
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
        var settings = services.CopySettings(); settings.Desktop.MenuBarAutoHide = true; Complete(services.SaveSettingsAsync(settings));
        var bar = new DesktopBarWindow(services, _ => Task.CompletedTask, System.Windows.Forms.Screen.PrimaryScreen!);
        bar.SetAutoHideAllowed(true);
        try
        {
            bar.Show(); PumpUntil(() => bar.IsLoaded && bar.IsAutoHidden);
            if (bar.IsVisible) throw new Exception("Collapsed top bar left a visible activation strip.");
            bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            if (bar.IsAutoHidden || !bar.IsVisible || bar.Height < 32) throw new Exception("Top menu bar did not expand on pointer entry.");
            var menuButton = (Button)bar.FindName("MenuButton");
            menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            Complete(Task.Delay(1100));
            if (bar.IsAutoHidden || menuButton.ContextMenu?.IsOpen != true) throw new Exception("Top menu bar hid while its menu was in use.");
            menuButton.ContextMenu.IsOpen = false;
            PumpUntil(() => bar.IsAutoHidden);
            if (bar.IsVisible) throw new Exception("Top bar did not fully hide after its menu closed.");
            bar.SetAutoHideAllowed(false);
            bar.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            Complete(Task.Delay(1100));
            if (bar.IsAutoHidden || !bar.IsVisible) throw new Exception("Top menu hid on the desktop or without a maximized app.");
            bar.SetAutoHideAllowed(true);
            PumpUntil(() => bar.IsAutoHidden);
            settings = services.CopySettings(); settings.Desktop.MenuBarAutoHide = false; Complete(services.SaveSettingsAsync(settings)); bar.RefreshConfiguration();
            if (bar.IsAutoHidden || bar.Height < 32) throw new Exception("Always-visible top menu setting did not apply.");
            settings = services.CopySettings(); settings.Desktop.MenuBarAutoHide = true; Complete(services.SaveSettingsAsync(settings)); bar.RefreshConfiguration();
            PumpUntil(() => bar.IsAutoHidden);
            Console.WriteLine("  top menu: desktop/no-maximized stay visible, maximized auto-hide, edge reveal and open-menu hold verified");
        }
        finally { bar.Close(); }
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
