using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mosaic.Core;
using Mosaic.Core.Models;
using Mosaic.Host.Controls;
using Mosaic.Host.Services;
using Mosaic.Host.Views;
using Mosaic.Storage.Json;

internal static class DesktopPetChecks
{
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static Task RunAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Mosaic;component/Themes/Dark.xaml", UriKind.Relative) });
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                Check(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    public static void Check()
    {
        var previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            CheckCore();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void CheckCore()
    {
        var root = Path.Combine(Path.GetTempPath(), "Mosaic.Tests", Guid.NewGuid().ToString("N"));
        var services = new AppServices(new JsonDocumentStore(root)); Complete(services.InitializeAsync());
        using (var monitor = new DesktopPetMonitor(true, true))
            Assert(monitor.KeyboardAvailable && monitor.WindowEventsAvailable, "Pet native listeners did not register.");
        using var pet = new DesktopPetService(services, () => { });
        services.SettingsChanged += (_, _) => pet.ApplySettings();
        var before = NativeShell.CaptureForeground();
        Complete(services.UpdatePetAsync(p => p.Enabled = true));
        var window = pet.Window!; window.UpdateLayout();
        Assert(window.IsVisible && !window.ShowActivated && !window.ShowInTaskbar, "Pet window is not a passive floating tool window.");
        Assert(pet.Behavior.IsGreeting, "Appearing pet did not greet.");
        Assert(before == NativeShell.CaptureForeground(), "Showing the pet stole input focus.");
        Assert(!services.Settings.Desktop.OverlayEnabled, "Standalone pet enabled the desktop overlay.");
        CheckNoWindowFrame(window);
        ThemeManager.ApplyWindow(window);
        CheckNoWindowFrame(window);
        CheckTopmostAndFullscreen(window);
        CheckHoverInput(pet, window);
        CheckBatchedInput(pet, window, services);
        CheckExpressionMenu(pet, window);
        CheckEmotionKeyframes();
        var characterIdles = new List<BitmapSource>();
        foreach (var character in Enum.GetValues<PetCharacter>())
        {
            Complete(services.UpdatePetAsync(p => p.Character = character));
            window.UpdateLayout();
            Assert(ReferenceEquals(window, pet.Window) && window.Sprite.Character == character && window.Title.Contains(DesktopPetCharacters.Get(character).Name), "Character change failed or recreated the desktop window.");
            var expectedSize = services.Settings.Pet.Size * (character is PetCharacter.DeepSeekStandard or PetCharacter.GptStandard or PetCharacter.GeminiStandard or PetCharacter.ClaudeStandard or PetCharacter.QwenStandard ? 2 : 1);
            Assert(window.Width == expectedSize + 32 && window.Height == expectedSize + 96 && window.Sprite.ActualWidth == expectedSize && window.Sprite.ActualHeight == expectedSize, "Normal-proportion pet did not double its canvas size or another character changed size.");
            foreach (var pose in Enum.GetValues<PetPose>())
            {
                window.Sprite.SetPose(pose);
                var source = (BitmapSource)window.Sprite.Source;
                Assert(source.PixelWidth == 512 && source.PixelHeight == 512, "Pose canvas was not normalized.");
                CheckPoseMargins(source, pose);
                CheckCharacterHitTest(window.Sprite, source);
            }
            SavePoseSheet(window.Sprite, character);
            window.Sprite.SetPose(PetPose.Idle); characterIdles.Add((BitmapSource)window.Sprite.Source);
        }
        Assert(characterIdles.Distinct().Count() == Enum.GetValues<PetCharacter>().Length, "A character silently reused another character's artwork.");
        Complete(services.UpdatePetAsync(p => p.Character = PetCharacter.WhaleGirl));
        window.Sprite.SetPose(PetPose.Idle);
        // Returning to the original must reuse its own cached frame and alpha mask.
        Assert(ReferenceEquals(window.Sprite.Source, characterIdles[0]), "Switching back lost the original atlas cache.");
        SaveCharacterOverview(characterIdles);
        Assert(!window.Sprite.IsOpaqueAt(new Point(0, 0)) && window.Sprite.IsOpaqueAt(new Point(window.Sprite.ActualWidth * .42, window.Sprite.ActualHeight * .5)), "Character alpha hit testing is wrong.");
        var point = window.PointToScreen(new Point(2, 2));
        var packed = new nint(unchecked((int)(((uint)(ushort)(int)point.Y << 16) | (ushort)(int)point.X)));
        Assert(SendMessage(new WindowInteropHelper(window).Handle, 0x84, 0, packed) == -1, "Transparent window margin blocked the desktop.");
        pet.SetFullscreen(true); Assert(!window.IsVisible, "Pet did not hide during fullscreen.");
        pet.SetFullscreen(false); Assert(window.IsVisible, "Pet did not return after fullscreen.");
        Assert(pet.Behavior.IsGreeting, "Reappearing pet did not greet.");
        Assert((GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64() & 8) != 0, "Returning from fullscreen lost native topmost status.");
        Complete(services.UpdatePetAsync(p => { p.Size = 160; p.Left = -100000; p.Top = -100000; }));
        Assert(window.Width == 192 && window.Left > -100000 && window.Top > -100000, "Resizing or off-screen recovery failed.");
        var panel = new DesktopPetSettingsPanel(); panel.Load(services.Settings.Pet);
        ((CheckBox)panel.FindName("EnabledCheck")).IsChecked = false;
        var draft = services.CopySettings(); panel.ApplyTo(draft.Pet);
        Assert(!draft.Pet.Enabled && services.Settings.Pet.Enabled, "Pet settings preview mutated the live settings.");
        var picker = (ComboBox)panel.FindName("CharacterPicker");
        picker.SelectedValue = PetCharacter.DeepSeekStandard; panel.ApplyTo(draft.Pet);
        Assert(draft.Pet.Character == PetCharacter.DeepSeekStandard && services.Settings.Pet.Character == PetCharacter.WhaleGirl && window.Sprite.Character == PetCharacter.WhaleGirl, "Character preview mutated the live pet before saving.");
        Assert(((TextBlock)panel.FindName("SizeLabel")).Text.Contains("320") && draft.Pet.Size == 160, "Standard pet size label did not show the doubled size or changed the saved base size.");
        panel.Load(services.Settings.Pet);
        Assert((PetCharacter)picker.SelectedValue == PetCharacter.WhaleGirl && ((WhalePetSprite)panel.FindName("PreviewSprite")).Character == PetCharacter.WhaleGirl, "Reloading / cancelling the draft did not restore the character preview.");
        Complete(services.UpdatePetAsync(p => p.Enabled = false));
        Assert(pet.Window is null && !window.IsVisible, "Disabling the pet left its window alive.");
        Complete(services.UpdatePetAsync(p => p.Enabled = true)); Assert(pet.Window is { IsVisible: true }, "Pet could not be re-enabled.");
        // Render a reproducible preview for visual review; this is the real WPF control.
        var preview = new DesktopPetSettingsPanel(); preview.Load(services.Settings.Pet);
        var host = new Window { Content = new ScrollViewer { Content = preview }, Width = 530, Height = 820, Background = (Brush)Application.Current.Resources["PanelBrush"] };
        host.Show(); host.UpdateLayout();
        CheckEmotionPreview(preview, host);
        CheckMultiplePets(pet, services, preview);
        CheckMultiplePets(pet, services, preview, standard: true);
        var bitmap = new RenderTargetBitmap(530, 820, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(".artifacts/pet-check"); Directory.CreateDirectory(directory);
        using (var stream = File.Create(Path.Combine(directory, "pet-settings.png"))) encoder.Save(stream);
        host.Close();
        Console.WriteLine("  Pet: 319 cached poses; eleven identities, keyframe sequences from neutral, preview/menu, retired walking, alpha hit tests, input, topmost and fullscreen verified.");
    }
    private static void CheckHoverInput(DesktopPetService service, DesktopPetWindow window)
    {
        service.Behavior.Tick(Environment.TickCount64 + 10000, 0, false, 90, false, 0, 0);
        window.Render(service.Behavior, Environment.TickCount64, false, true); window.UpdateLayout();
        var foreground = NativeShell.CaptureForeground();
        var opaque = new Point(window.Sprite.ActualWidth * .42, window.Sprite.ActualHeight * .5);
        Assert(window.Sprite.IsOpaqueAt(opaque), "Hover fixture does not target painted pixels.");
        var screen = window.Sprite.PointToScreen(opaque);
        var margin = window.PointToScreen(new Point(2, 2));
        window.PollHover(null, 9000); window.PollHover(null, 9300);
        Assert(!window.PollHover(margin, 10000) && !window.PollHover(margin, 10400), "Transparent padding triggered hover.");
        Assert(!window.PollHover(screen, 11000) && !window.PollHover(screen, 11249) && window.PollHover(screen, 11250), "Hover did not wait for a deliberate dwell.");
        Assert(!window.PollHover(screen, 12000) && !window.PollHover(screen, 50000), "A stationary pointer repeatedly triggered hover.");
        window.PollHover(null, 50010);
        Assert(!window.PollHover(screen, 50020), "A brief silhouette gap retriggered hover.");
        window.PollHover(null, 51000); window.PollHover(null, 51300);
        Assert(!window.PollHover(screen, 51400) && window.PollHover(screen, 51650), "Leaving and returning did not re-arm hover.");
        var interact = (Action<PetTrigger>)typeof(DesktopPetWindow).GetField("Interacted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        interact(PetTrigger.Hover);
        Assert(service.Behavior.Bubble.Length == 0 && service.Behavior.Pose is PetPose.Pat or PetPose.GreetA, "Window hover did not reach the service.");
        var started = service.Behavior.StartedAt;
        window.Sprite.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Assert(!window.PollHover(screen, 52000), "Pressing or dragging still allowed hover.");
        window.Sprite.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Assert(service.Behavior.StartedAt == started && DesktopPetDialogue.ClickLines(window.Sprite.Character).Contains(service.Behavior.Bubble), "Actual click did not speak during the existing hover animation.");
        Assert(!window.PollHover(screen, 56000), "Hover restarted after a click without leaving the pet.");
        var previous = service.Behavior.Bubble;
        window.Sprite.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "聊一句")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert(service.Behavior.Bubble != previous, "Chat menu immediately repeated its last line.");
        Assert(NativeShell.CaptureForeground() == foreground, "Hover or clicking the passive pet stole focus.");
        Console.WriteLine("  Hover UI: alpha-only dwell, one entry/one reaction, silhouette-gap grace, re-entry, press suppression, actual click/menu speech and focus verified.");
    }
    private static void CheckMultiplePets(DesktopPetService service, AppServices services, DesktopPetSettingsPanel panel, bool standard = false)
    {
        var original = services.Settings.Pet.Copy();
        try
        {
            panel.Load(original);
            var add = (Button)panel.FindName("AddPetButton"); var remove = (Button)panel.FindName("RemovePetButton");
            var picker = (ComboBox)panel.FindName("CharacterPicker"); var instances = (ComboBox)panel.FindName("InstancePicker");
            if (standard) picker.SelectedValue = PetCharacter.DeepSeekStandard;
            var characters = standard ? new[] { PetCharacter.GptStandard, PetCharacter.GeminiStandard, PetCharacter.ClaudeStandard, PetCharacter.QwenStandard } : new[] { PetCharacter.Gpt, PetCharacter.Gemini, PetCharacter.Claude, PetCharacter.Qwen };
            foreach (var character in characters)
            {
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); picker.SelectedValue = character;
                ((Slider)panel.FindName("SizeSlider")).Value = 160;
            }
            var draft = original.Copy(); panel.ApplyTo(draft);
            Assert(draft.Companions.Count == 4 && draft.Character == (standard ? PetCharacter.DeepSeekStandard : original.Character) && services.Settings.Pet.Companions.Count == 0 && services.Settings.Pet.Character == original.Character, "Roster draft leaked to live pets or changed the primary character.");
            Assert(!add.IsEnabled, "Five-pet cap did not disable adding.");
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); panel.ApplyTo(draft);
            Assert(draft.Companions.Count == 4, "An extra UI action exceeded the five-pet cap.");
            foreach (var slot in draft.Slots())
            {
                slot.Options.SleepWhenIdle = false; slot.Options.ReactToAppClose = false;
                slot.Options.ReactToEnter = false; slot.Options.ReactToDesktop = false; slot.Options.FollowCursor = false;
            }
            Complete(services.UpdatePetAsync(pet => draft.CopyAllTo(pet)));
            Assert(service.Pets.Count == 5 && service.Pets.Select(pet => pet.Window).Distinct().Count() == 5 && service.Pets.Select(pet => pet.Behavior).Distinct().Count() == 5 && service.Pets.All(pet => pet.Window.IsVisible), "Five independent visible pets were not created.");
            Assert(service.Pets.Select(pet => new Point(pet.Window.Left, pet.Window.Top)).Distinct().Count() == 5, "New pets all spawned at one position.");
            var claudeCharacter = standard ? PetCharacter.ClaudeStandard : PetCharacter.Claude;
            var claudeId = draft.Companions.Single(pet => pet.Character == claudeCharacter).Id;
            var qwenId = draft.Companions.Single(pet => pet.Character == (standard ? PetCharacter.QwenStandard : PetCharacter.Qwen)).Id;
            var actor = service.Pets.Single(pet => pet.Id == claudeId);
            foreach (var pet in service.Pets) pet.Behavior.Tick(Environment.TickCount64 + 10000, 0, false, 90, false, 0, 0);
            var interact = (Action<PetTrigger>)typeof(DesktopPetWindow).GetField("Interacted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actor.Window)!;
            interact(PetTrigger.Hover);
            var hoverStarted = actor.Behavior.StartedAt;
            interact(PetTrigger.Click);
            Assert(actor.Behavior.StartedAt == hoverStarted && DesktopPetDialogue.ClickLines(claudeCharacter).Contains(actor.Behavior.Bubble) && service.Pets.Where(pet => pet.Id != claudeId).All(pet => pet.Behavior.Bubble.Length == 0), "Hover/click dialogue leaked to another companion or reset the gesture.");
            var signature = actor.Window.Sprite.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "翻一页书"));
            signature.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(actor.Behavior.Pose == PetPose.RiceHat && actor.Behavior.Bubble.Contains("一起读") && service.Pets.Where(pet => pet.Id != claudeId).All(pet => pet.Behavior.Pose != PetPose.RiceHat), "A personal gesture affected other pets or lost its persona.");
            var positions = service.Pets.Where(pet => pet.Id != claudeId).ToDictionary(pet => pet.Id, pet => new Point(pet.Window.Left, pet.Window.Top));
            var commit = (Action<double, double>)typeof(DesktopPetWindow).GetField("PositionCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actor.Window)!;
            commit(120, 150);
            WaitFor(() => services.Settings.Pet.GetPet(claudeId).Left == 120, "Independent drag position did not save.");
            Assert(service.Pets.Where(pet => pet.Id != claudeId).All(pet => positions[pet.Id] == new Point(pet.Window.Left, pet.Window.Top)), "Dragging one companion reflowed other live pets.");
            var restarted = new AppServices(new JsonDocumentStore(services.Store.Root)); Complete(restarted.InitializeAsync());
            Assert(restarted.Settings.Pet.GetPet(claudeId).Left == 120 && restarted.Settings.Pet.GetPet(claudeId).Top == 150 && restarted.Settings.Pet.Companions.Count == 4, "Companion identity / position did not survive restart.");
            Complete(services.UpdatePetAsync(pet => { pet.GetPet(claudeId).ReactToTyping = false; pet.GetPet(qwenId).HideInFullscreen = false; }));
            foreach (var pet in service.Pets) pet.Behavior.Tick(Environment.TickCount64 + 10000, 0, false, 90, false, 0, 0);
            var record = (Action<PetTrigger>)typeof(DesktopPetService).GetMethod("RecordInput", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(Action<PetTrigger>), service);
            for (var i = 0; i < 10000; i++) record(PetTrigger.Typing);
            Assert(service.Pets.All(pet => !pet.Behavior.IsTyping), "Raw input synchronously updated one of five pets.");
            Complete(Task.Delay(250));
            Assert(service.Pets.Where(pet => pet.Id != claudeId).All(pet => pet.Behavior.IsTyping) && !service.Pets.Single(pet => pet.Id == claudeId).Behavior.IsTyping, $"Shared input settings/states: {string.Join(", ", service.Pets.Select(pet => $"{pet.Window.Sprite.Character}:{pet.Behavior.Pose}:typing={services.Settings.Pet.GetPet(pet.Id).ReactToTyping}"))}.");
            service.SetFullscreen(true);
            Assert(service.Pets.Single(pet => pet.Id == qwenId).Window.IsVisible && service.Pets.Where(pet => pet.Id != qwenId).All(pet => !pet.Window.IsVisible), "Fullscreen preference was not independent.");
            service.SetFullscreen(false); Assert(service.Pets.All(pet => pet.Window.IsVisible), "Not all companions returned after fullscreen.");
            var hide = actor.Window.Sprite.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "收起这位伙伴"));
            hide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitFor(() => service.Pets.Count == 4, "Hiding one companion did not remove just its window.");
            Assert(!services.Settings.Pet.GetPet(claudeId).Enabled && services.Settings.Pet.Enabled && service.Pets.All(pet => pet.Id != claudeId), "Hiding one companion disabled the entire roster.");
            panel.Load(services.Settings.Pet); instances.SelectedValue = claudeId;
            Assert(remove.IsEnabled, "Selecting an existing companion did not enable its remove button.");
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var removal = services.Settings.Pet.Copy(); panel.ApplyTo(removal);
            Assert(removal.Companions.Count == 3 && !removal.Companions.Any(pet => pet.Id == claudeId), "Removing the chosen companion deleted the wrong entry.");
            Complete(services.UpdatePetAsync(pet => removal.CopyAllTo(pet)));
            Complete(services.UpdatePetAsync(pet => pet.Enabled = false)); Assert(service.Pets.Count == 0, "Global off left companion windows alive.");
            Complete(services.UpdatePetAsync(pet => pet.Enable())); Assert(service.Pets.Count == 4, "Global re-enable did not restore the roster.");
        }
        finally { Complete(services.UpdatePetAsync(pet => original.CopyAllTo(pet))); panel.Load(original); }
        Console.WriteLine($"  Multi-pet ({(standard ? "normal proportions" : "chibi")}): five windows/states, sixth-pet cap, independent drag/restart/menu/fullscreen/settings, shared input and global off/on verified.");
    }
    private static void CheckEmotionPreview(DesktopPetSettingsPanel panel, Window host)
    {
        Assert(FindVisual<Button>(panel, item => Equals(item.Tag, "Walk")) is null && panel.FindName("WalkCheck") is null, "Walking remains in settings.");
        var sprite = (WhalePetSprite)panel.FindName("PreviewSprite");
        var probe = new WhalePetSprite();
        var picker = (ComboBox)panel.FindName("CharacterPicker");
        foreach (var character in Enum.GetValues<PetCharacter>())
        {
            picker.SelectedValue = character; probe.SetCharacter(character);
            var chat = FindVisual<Button>(panel, item => Equals(item.Tag, "Click"));
            Assert(chat is not null, "Missing random chat preview.");
            chat!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var spoken = ((TextBlock)panel.FindName("PreviewCaption")).Text;
            chat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(DesktopPetDialogue.ClickLines(character).Contains(spoken) && ((TextBlock)panel.FindName("PreviewCaption")).Text != spoken, "Chat preview lost its voice or repeated consecutively.");
            foreach (var (tag, pose) in new[] { ("Tired", PetPose.Tired), ("BegTokens", PetPose.BegTokens), ("TokenDazed", PetPose.TokenDazed) })
            {
                var button = FindVisual<Button>(panel, item => Equals(item.Tag, tag));
                Assert(button is not null, $"Missing expression preview: {tag}.");
                button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); host.UpdateLayout();
                probe.SetPose(pose);
                var first = sprite.Source;
                probe.SetPose(PetPose.Idle);
                Assert(sprite.Character == character && ReferenceEquals(first, probe.Source), $"Preview did not start with existing neutral frame: {character}/{pose}.");
                Assert(((TextBlock)panel.FindName("PreviewCaption")).Text.Length > 0, "Expression dialogue missing from preview.");
                WaitFor(() => !ReferenceEquals(sprite.Source, first), "Preview did not advance from neutral.");
                probe.SetPose(pose switch { PetPose.Tired => PetPose.TiredYawn, PetPose.BegTokens => PetPose.BegReach, _ => PetPose.TokenFallHigh });
                Assert(ReferenceEquals(sprite.Source, probe.Source), $"Preview skipped its preparation keyframe: {character}/{pose}.");
                Complete(Task.Delay(800)); probe.SetPose(pose);
                Assert(ReferenceEquals(sprite.Source, probe.Source), $"Preview did not reach its final expression: {character}/{pose}.");
            }
        }
    }
    private static T? FindVisual<T>(DependencyObject parent, Func<T, bool> matches) where T : DependencyObject
    {
        if (parent is T item && matches(item)) return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindVisual(VisualTreeHelper.GetChild(parent, i), matches) is { } found) return found;
        return null;
    }
    private static void CheckExpressionMenu(DesktopPetService pet, DesktopPetWindow window)
    {
        var items = window.Sprite.ContextMenu!.Items.OfType<MenuItem>().ToArray();
        Assert(!items.Any(item => item.Header.ToString()!.Contains("走")), "Walking remains in the desktop menu.");
        var foreground = NativeShell.CaptureForeground(); var left = window.Left; var top = window.Top;
        var probe = new WhalePetSprite(); probe.SetCharacter(window.Sprite.Character);
        foreach (var (label, pose) in new[] { ("疲惫", PetPose.Tired), ("求 token", PetPose.BegTokens), ("被 token 砸晕", PetPose.TokenDazed) })
        {
            items.Single(item => Equals(item.Header, label)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            probe.SetPose(pose);
            probe.SetPose(PetPose.Idle);
            Assert(pet.Behavior.IsExpression && pet.Behavior.Pose == PetPose.Idle && ReferenceEquals(window.Sprite.Source, probe.Source), $"Menu did not start from neutral: {label}.");
            var first = window.Sprite.Source;
            WaitFor(() => !ReferenceEquals(window.Sprite.Source, first), "Desktop did not advance from neutral.");
            Assert(!ReferenceEquals(window.Sprite.Source, first), $"Desktop timer did not advance expression: {label}.");
            Assert(window.Left == left && window.Top == top, "Expression moved the pet's saved location.");
        }
        Assert(foreground == NativeShell.CaptureForeground(), "Expression stole input focus.");
    }
    private static void CheckEmotionKeyframes()
    {
        var sprite = new WhalePetSprite { Width = 512, Height = 512 };
        sprite.Measure(new(512, 512)); sprite.Arrange(new(0, 0, 512, 512));
        var directory = Path.GetFullPath(".artifacts/pet-check/keyframes"); Directory.CreateDirectory(directory);
        var window = new DesktopPetWindow();
        try
        {
            foreach (var character in Enum.GetValues<PetCharacter>())
            {
                sprite.SetCharacter(character); window.Configure(new DesktopPetSettings { Character = character }, true);
                var sequences = new[]
                {
                    new[] { PetPose.Idle, PetPose.TiredYawn, PetPose.Tired },
                    new[] { PetPose.Idle, PetPose.BegReach, PetPose.BegTokens },
                    new[] { PetPose.Idle, PetPose.TokenFallHigh, PetPose.TokenFallLow, PetPose.TokenDazed }
                };
                var drawing = new DrawingVisual();
                using (var dc = drawing.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1024, 880));
                    var title = new FormattedText(DesktopPetCharacters.Get(character).Name + " · 关键帧顺序", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 20, Brushes.MidnightBlue, 1);
                    dc.DrawText(title, new Point(20, 10));
                    for (var row = 0; row < sequences.Length; row++)
                    {
                        for (var column = 0; column < sequences[row].Length; column++)
                        {
                            var pose = sequences[row][column]; sprite.SetPose(pose);
                            dc.DrawImage(sprite.Source, new Rect(column * 256, 40 + row * 280, 256, 256));
                            var label = pose switch { PetPose.Idle => "正常（复用原图）", PetPose.TiredYawn => "打哈欠", PetPose.Tired => "疲惫", PetPose.BegReach => "伸手", PetPose.BegTokens => "求 token", PetPose.TokenFallHigh => "一枚 token 出现", PetPose.TokenFallLow => "token 下落", _ => "被砸晕" };
                            var caption = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 14, Brushes.SlateGray, 1);
                            dc.DrawText(caption, new Point(column * 256 + 16, 40 + row * 280 + 258));
                        }
                    }
                }
                var sheet = new RenderTargetBitmap(1024, 880, 96, 96, PixelFormats.Pbgra32); sheet.Render(drawing);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(sheet));
                using (var output = File.Create(Path.Combine(directory, $"keyframes-{character}.png"))) png.Save(output);
                sprite.SetPose(PetPose.Idle); var neutral = Pixels((BitmapSource)sprite.Source);
                foreach (var (falling, top) in new[] { (PetPose.TokenFallHigh, 18), (PetPose.TokenFallLow, 64) })
                {
                    sprite.SetPose(falling); var pixels = Pixels((BitmapSource)sprite.Source);
                    for (var y = 0; y < 512; y++)
                        for (var x = 0; x < 512; x++)
                        {
                            if (x >= 232 && x < 280 && y >= top && y < top + 48) continue;
                            var offset = (y * 512 + x) * 4;
                            Assert(neutral.AsSpan(offset, 4).SequenceEqual(pixels.AsSpan(offset, 4)), "Falling token redrew or warped the character outside the token rectangle.");
                        }
                }
                foreach (var (trigger, final) in new[] { (PetTrigger.Tired, PetPose.Tired), (PetTrigger.BegTokens, PetPose.BegTokens), (PetTrigger.TokenDazed, PetPose.TokenDazed) })
                {
                    var behavior = new DesktopPetBehavior(character: character); behavior.Trigger(trigger, 10000);
                    window.Render(behavior, 10000, false, true); sprite.SetPose(final);
                    Assert(ReferenceEquals(window.Sprite.Source, sprite.Source), "Disabled animation started the preparation sequence.");
                    foreach (var elapsed in new[] { 0, 600, 950, 1400, 2200 })
                    {
                        behavior.Tick(10000 + elapsed, 0, false, 90, false, 0, 0);
                        window.Render(behavior, 10000 + elapsed, true, true); sprite.SetPose(behavior.Pose);
                        Assert(ReferenceEquals(window.Sprite.Source, sprite.Source), "Desktop did not render the selected frozen keyframe.");
                    }
                    window.Render(behavior, 12200, false, true); sprite.SetPose(final);
                    Assert(ReferenceEquals(window.Sprite.Source, sprite.Source), "Disabled animation did not show the final still.");
                    Assert(window.Sprite.RenderTransform.Value.IsIdentity, "Expression retained procedural deformation or particle motion.");
                }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("  Expressions: neutral → preparation → final; one falling token over the unchanged neutral painting; no GIF or deformation renderer.");
    }
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[512 * 512 * 4]; converted.CopyPixels(pixels, 512 * 4, 0); return pixels;
    }
    private static void WaitFor(Func<bool> ready, string failure)
    {
        var deadline = Environment.TickCount64 + 3000;
        while (!ready() && Environment.TickCount64 < deadline) Complete(Task.Delay(20));
        Assert(ready(), failure);
    }
    private static void CheckBatchedInput(DesktopPetService pet, DesktopPetWindow window, AppServices services)
    {
        var record = (Action<PetTrigger>)typeof(DesktopPetService).GetMethod("RecordInput", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(Action<PetTrigger>), pet);
        pet.Behavior.Tick(Environment.TickCount64 + 10000, 0, false, 90, false, 0, 0);
        var previous = pet.Behavior.Pose;
        var changes = 0; EventHandler changed = (_, _) => changes++;
        var descriptor = DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(WhalePetSprite));
        descriptor.AddValueChanged(window.Sprite, changed); window.Sprite.RenderTransform.Changed += changed;
        try
        {
            for (var i = 0; i < 10000; i++) record(PetTrigger.Typing);
            Assert(changes == 0 && pet.Behavior.Pose == previous, "Raw input callback synchronously changed the pet UI or animation state.");
            Complete(Task.Delay(350));
            Assert(pet.Behavior.IsTyping && changes is > 0 and <= 8, $"Input burst was lost or produced too many visual changes: {changes}.");
        }
        finally { descriptor.RemoveValueChanged(window.Sprite, changed); window.Sprite.RenderTransform.Changed -= changed; }
        Complete(services.UpdatePetAsync(p => p.ReactToTyping = false));
        Assert(!pet.Behavior.IsTyping, "Disabling typing did not stop the batched animation.");
        Complete(services.UpdatePetAsync(p => p.ReactToTyping = true));
    }
    private static void CheckPoseMargins(BitmapSource source, PetPose pose)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[512 * 512 * 4]; image.CopyPixels(pixels, 512 * 4, 0);
        var count = 0;
        for (var y = 0; y < 512; y++)
            for (var x = 0; x < 512; x++)
            {
                var alpha = pixels[(y * 512 + x) * 4 + 3];
                if (alpha > 24) count++;
                if (x < 16 || y < 16 || x >= 496 || y >= 496)
                    Assert(alpha == 0, $"{pose}: a neighboring pixel or clipped silhouette reached the transparent gutter.");
            }
        Assert(count > 15000, $"{pose}: character is missing or too small.");
    }
    private static void CheckCharacterHitTest(WhalePetSprite sprite, BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[512 * 512 * 4]; converted.CopyPixels(pixels, 512 * 4, 0);
        for (var y = 24; y < 512; y += 31)
            for (var x = 24; x < 512; x += 31)
                Assert(sprite.IsOpaqueAt(new Point((x + .5) / 512 * sprite.ActualWidth, (y + .5) / 512 * sprite.ActualHeight)) == (pixels[(y * 512 + x) * 4 + 3] > 24), "Alpha hit testing used another character or pose's mask.");
    }
    private static void SaveCharacterOverview(IReadOnlyList<BitmapSource> idles)
    {
        var height = (int)Math.Ceiling(idles.Count / 4d) * 360;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(236, 242, 250)), null, new Rect(0, 0, 1200, height));
            for (var i = 0; i < idles.Count; i++)
            {
                context.DrawImage(idles[i], new Rect(i % 4 * 300 + 12, i / 4 * 360 + 10, 276, 276));
                var caption = new FormattedText(DesktopPetCharacters.All[i].Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 17, Brushes.MidnightBlue, 1);
                context.DrawText(caption, new Point(i % 4 * 300 + (300 - caption.Width) / 2, i / 4 * 360 + 310));
            }
        }
        var bitmap = new RenderTargetBitmap(1200, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(".artifacts/pet-check"); Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, "pet-characters.png")); encoder.Save(stream);
        var normal = new DrawingVisual();
        using (var context = normal.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(241, 245, 252)), null, new Rect(0, 0, 1440, 600));
            var characters = new[] { PetCharacter.GptStandard, PetCharacter.GeminiStandard, PetCharacter.ClaudeStandard, PetCharacter.QwenStandard };
            for (var i = 0; i < characters.Length; i++)
            {
                context.DrawImage(idles[(int)characters[i]], new Rect(i * 360 - 36, 35, 432, 432));
                var caption = new FormattedText(DesktopPetCharacters.Get(characters[i]).Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 18, Brushes.MidnightBlue, 1);
                context.DrawText(caption, new Point(i * 360 + (360 - caption.Width) / 2, 550));
            }
        }
        var normalBitmap = new RenderTargetBitmap(1440, 600, 96, 96, PixelFormats.Pbgra32); normalBitmap.Render(normal);
        var normalEncoder = new PngBitmapEncoder(); normalEncoder.Frames.Add(BitmapFrame.Create(normalBitmap));
        using var normalStream = File.Create(Path.Combine(directory, "pet-standard-characters.png")); normalEncoder.Save(normalStream);
    }
    private static void SavePoseSheet(WhalePetSprite sprite, PetCharacter character)
    {
        var height = (int)Math.Ceiling(Enum.GetValues<PetPose>().Length / 5d) * 280;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1280, height));
            foreach (var pose in Enum.GetValues<PetPose>())
            {
                var i = (int)pose; sprite.SetPose(pose);
                context.DrawImage(sprite.Source, new Rect(i % 5 * 256, i / 5 * 280, 256, 256));
                var caption = new FormattedText(pose.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.SlateGray, 1);
                context.DrawText(caption, new Point(i % 5 * 256 + 16, i / 5 * 280 + 258));
            }
        }
        var bitmap = new RenderTargetBitmap(1280, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(".artifacts/pet-check"); Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, character == PetCharacter.WhaleGirl ? "pet-poses-v2.png" : $"pet-poses-{character}.png")); encoder.Save(stream);
    }
    private static void CheckTopmostAndFullscreen(DesktopPetWindow pet)
    {
        var handle = new WindowInteropHelper(pet).Handle;
        var before = NativeShell.CaptureForeground();
        GetWindowRect(handle, out var original);
        SetWindowPos(handle, new nint(-2), 0, 0, 0, 0, 0x213);
        Assert(pet.Topmost && (GetWindowLongPtr(handle, -20).ToInt64() & 8) == 0, "Native demotion fixture did not reproduce stale WPF Topmost state.");
        Complete(Task.Delay(650));
        Assert((GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0, "Pet failed to recover its native topmost state.");
        GetWindowRect(handle, out var restored);
        Assert(original.Equals(restored) && before == NativeShell.CaptureForeground(), "Topmost recovery moved the pet or stole input focus.");
        var maximized = new Window { Title = "Mosaic pet maximized test", Width = 300, Height = 200, ShowInTaskbar = false, WindowState = WindowState.Maximized };
        var fullscreen = new Window { Title = "Mosaic pet fullscreen test", Width = 300, Height = 200, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
        try
        {
            maximized.Show(); maximized.UpdateLayout();
            var maxHandle = new WindowInteropHelper(maximized).Handle;
            var maxMonitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Assert(GetMonitorInfo(MonitorFromWindow(maxHandle, 2), ref maxMonitor), "Maximized fixture monitor unavailable.");
            // Reproduce a hidden-taskbar desktop: the maximized frame spans the monitor.
            SetWindowPos(maxHandle, 0, maxMonitor.Monitor.Left, maxMonitor.Monitor.Top, maxMonitor.Monitor.Right - maxMonitor.Monitor.Left, maxMonitor.Monitor.Bottom - maxMonitor.Monitor.Top, 0x14);
            Assert(IsZoomed(maxHandle) && !NativeShell.IsFullscreenApplication(maxHandle), "Ordinary maximized app was mistaken for fullscreen.");
            pet.EnsureTopmost();
            var order = new List<nint>(); EnumWindows((window, _) => { order.Add(window); return true; }, 0);
            Assert(order.IndexOf(handle) >= 0 && order.IndexOf(handle) < order.IndexOf(maxHandle), "Maximized app covered the pet in the native Z order.");
            fullscreen.Show(); fullscreen.UpdateLayout();
            var fullHandle = new WindowInteropHelper(fullscreen).Handle;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Assert(GetMonitorInfo(MonitorFromWindow(fullHandle, 2), ref monitor), "Fullscreen fixture monitor unavailable.");
            SetWindowPos(fullHandle, 0, monitor.Monitor.Left, monitor.Monitor.Top, monitor.Monitor.Right - monitor.Monitor.Left, monitor.Monitor.Bottom - monitor.Monitor.Top, 0x14);
            Assert(NativeShell.IsFullscreenApplication(fullHandle), "Borderless fullscreen app was not detected.");
            Assert(!NativeShell.IsFullscreenApplication(GetShellWindow()), "Desktop was mistaken for fullscreen.");
        }
        finally { fullscreen.Close(); maximized.Close(); }
    }
    private static void CheckNoWindowFrame(DesktopPetWindow window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Assert(window.BorderThickness == new Thickness(0) && window.Background is SolidColorBrush { Color.A: 0 }, "Pet has a WPF frame or opaque background.");
        Assert(DwmGetWindowAttribute(handle, 33, out var corners, sizeof(int)) >= 0 && corners == 1, "Pet opted into rounded system window corners.");
        // DWMWA_BORDER_COLOR is setter-only on this Windows build. Verify the
        // effective frame policy instead of querying that unsupported attribute.
        Assert(DwmGetWindowAttribute(handle, 38, out var backdrop, sizeof(int)) >= 0 && backdrop == 1, "Pet has a rectangular system backdrop.");
        Assert(DwmGetWindowAttribute(handle, 1, out var nonClient, sizeof(int)) >= 0 && nonClient == 0, "Pet has a system non-client frame or shadow.");
    }
    private static void Complete(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher=Dispatcher.CurrentDispatcher;
            var frame=new DispatcherFrame();
            // One frame lets native WM_TIMER messages run. Repeatedly posting
            // immediate dispatcher exits can starve the animation timer itself.
            _=task.ContinueWith(_=>dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)),TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    private delegate bool EnumCallback(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint data);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
}
