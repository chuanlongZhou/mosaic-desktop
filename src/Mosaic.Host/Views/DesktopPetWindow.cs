using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Mosaic.Core;
using Mosaic.Core.Models;
using Mosaic.Host.Controls;
using Mosaic.Host.Services;
using Forms = System.Windows.Forms;

namespace Mosaic.Host.Views;

public sealed class DesktopPetWindow : Window
{
    public WhalePetSprite Sprite { get; } = new();
    private readonly Border _bubble;
    private readonly TextBlock _message;
    private readonly ScaleTransform _scale = new();
    private readonly RotateTransform _rotation = new();
    private readonly TranslateTransform _offset = new();
    private Point? _pressScreen;
    private Point _pressWindow;
    private bool _dragging, _placing;
    private string _announcement = "";
    private long _announcementUntil;
    private bool _hoverConsumed;
    private long? _hoverSince, _outsideSince;
    public event Action<PetTrigger>? Interacted;
    public event Action<double, double>? PositionCommitted;
    public event Action? SettingsRequested, DisableRequested, ChimePreviewRequested, ClockReset;

    public DesktopPetWindow()
    {
        Title = "Mosaic · 鲸鱼娘"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        BorderThickness = new(0); BorderBrush = Brushes.Transparent;
        ShowActivated = false; Topmost = true; Focusable = false; UseLayoutRounding = true;
        _message = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(39, 67, 105)), TextAlignment = TextAlignment.Center };
        _bubble = new Border { Background = new SolidColorBrush(Color.FromArgb(245, 242, 249, 255)), BorderBrush = new SolidColorBrush(Color.FromRgb(174, 210, 244)), BorderThickness = new(1), CornerRadius = new(14, 14, 4, 14), Padding = new(12, 9, 12, 9), Margin = new(12, 4, 12, 0), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, Child = _message, IsHitTestVisible = false };
        Sprite.Margin = new(16, 78, 16, 18); Sprite.RenderTransformOrigin = new(.5, .85);
        Sprite.RenderTransform = new TransformGroup { Children = { _scale, _rotation, _offset } };
        Sprite.Cursor = Cursors.Hand;
        AutomationProperties.SetName(Sprite, "鲸鱼娘，悬停摸头或打招呼，单击随机聊天，双击投喂，拖动移动，右键打开菜单");
        var grid = new Grid(); grid.Children.Add(Sprite); grid.Children.Add(_bubble); Content = grid;
        Closed += (_, _) => Sprite.StopAnimation();
        IsVisibleChanged += (_, _) => { if (!IsVisible) Sprite.StopAnimation(); };
        Sprite.MouseLeftButtonDown += OnPress; Sprite.MouseMove += OnMove; Sprite.MouseLeftButtonUp += OnRelease;
        Sprite.LostMouseCapture += (_, _) => FinishDrag();
        Sprite.ContextMenu = BuildMenu();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeShell.StyleWindow(handle, true, true, transparent: true);
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        };
    }

    private ContextMenu BuildMenu(PetCharacter character = PetCharacter.WhaleGirl)
    {
        var menu = new ContextMenu();
        var info = DesktopPetCharacters.Get(character);
        foreach (var (label, action) in new[] { ("摸摸头", PetTrigger.Pat), (info.FeedLabel, PetTrigger.Feed), (info.SignatureLabel, PetTrigger.RiceHat), ("打个招呼", PetTrigger.Wave), ("聊一句", PetTrigger.Click), ("疲惫", PetTrigger.Tired), ("求 token", PetTrigger.BegTokens), ("被 token 砸晕", PetTrigger.TokenDazed), (info.TeaseLabel, PetTrigger.Pout) })
        {
            var item = new MenuItem { Header = label }; item.Click += (_, _) => Interacted?.Invoke(action); menu.Items.Add(item);
        }
        if (PetHourlyChime.Supports(character))
        {
            var chime = new MenuItem { Header = "试听整点报时" };
            chime.Click += (_, _) => ChimePreviewRequested?.Invoke(); menu.Items.Add(chime);
        }
        if (DesktopPetCharacters.IsAnimated(character))
        {
            var actions = new MenuItem { Header = "mk2 原版动作库（106）" };
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var category in Mk2AnimationCatalog.Categories)
            {
                var group = new MenuItem { Header = category.Key };
                foreach (var name in category.Value)
                { var item = new MenuItem { Header = name }; item.Click += (_, _) => { Sprite.PlayAnimation(name); Announce(name); }; group.Items.Add(item); assigned.Add(name); }
                actions.Items.Add(group);
            }
            var others = new MenuItem { Header = "其他动作与互动" };
            foreach (var clip in Mk2AnimationCatalog.All.Where(c => !assigned.Contains(c.Name)))
            { var name = clip.Name; var item = new MenuItem { Header = name }; item.Click += (_, _) => { Sprite.PlayAnimation(name); Announce(name); }; others.Items.Add(item); }
            actions.Items.Add(others); menu.Items.Add(actions);
        }
        menu.Items.Add(new Separator());
        foreach (var (label, action) in new[] { ("想一想", PetTrigger.Think), ("读一读", PetTrigger.Read), ("找线索", PetTrigger.Search), ("画一画", PetTrigger.Draw), ("伸懒腰", PetTrigger.Stretch), ("跑起来", PetTrigger.Run), ("哼首歌", PetTrigger.Hum) })
        {
            var item = new MenuItem { Header = label }; item.Click += (_, _) => Interacted?.Invoke(action); menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = "桌宠设置…" }; settings.Click += (_, _) => SettingsRequested?.Invoke(); menu.Items.Add(settings);
        var hide = new MenuItem { Header = "收起这位伙伴" }; hide.Click += (_, _) => DisableRequested?.Invoke(); menu.Items.Add(hide);
        return menu;
    }

    public void Configure(DesktopPetOptions settings, bool initial)
    {
        var changedCharacter = Sprite.Character != settings.Character;
        Sprite.SetCharacter(settings.Character);
        if (changedCharacter) Sprite.ContextMenu = BuildMenu(settings.Character);
        var character = DesktopPetCharacters.Get(settings.Character);
        var name = character.Name;
        Title = "Mosaic · " + name;
        AutomationProperties.SetName(Sprite, name + "，悬停摸头或打招呼，单击随机聊天，双击投喂，拖动移动，右键打开菜单");
        var size = settings.Size * character.SizeMultiplier;
        Width = size * DesktopPetCharacters.CanvasAspectRatio(settings.Character) + 32; Height = size + 96;
        if (initial)
        {
            Left = settings.Left ?? SystemParameters.WorkArea.Right - Width - 32;
            Top = settings.Top ?? SystemParameters.WorkArea.Bottom - Height - 70;
        }
        ClampToScreen();
    }

    public void Announce(string text)
    { _announcement = text; _announcementUntil = Environment.TickCount64 + 15000; }
    public void ClearAnnouncement() { _announcementUntil = 0; }

    public void Render(DesktopPetBehavior behavior, long now, bool animate, bool bubbles)
    {
        var pose = !animate && behavior.IsExpression ? behavior.StillPose : !animate && behavior.IsTyping ? PetPose.TypingLeft : !animate && behavior.IsGreeting ? PetPose.GreetA : !animate && behavior.Pose == PetPose.RiceHatTilt ? PetPose.RiceHat : behavior.Pose;
        var elapsed = Math.Max(0, now - behavior.StartedAt) / 1000d;
        if (!Sprite.RenderAnimation(behavior.ActionPose, behavior.StartedAt, now, animate, behavior.Pose)) Sprite.SetPose(pose);
        double y = 0, rotation = 0, scaleY = 1;
        if (animate && !behavior.IsExpression && !Sprite.HasNativeAnimation)
        {
            var phase = elapsed * Math.PI * 2;
            switch (behavior.Pose)
            {
                case PetPose.Cheer: y = -18 * Math.Abs(Math.Sin(phase * 1.8)); rotation = Math.Sin(phase * 1.8) * 4; break;
                case PetPose.Pat: rotation = Math.Sin(phase * 1.5) * 4; scaleY = 1 - .025 * Math.Abs(Math.Sin(phase)); break;
                case PetPose.Wave: case PetPose.Goodbye: rotation = Math.Sin(phase * 1.4) * 5; break;
                case PetPose.GreetA: case PetPose.GreetB: rotation = Math.Sin(phase) * 2; break;
                case PetPose.Eat: y = Math.Sin(phase * 2) * 2; rotation = Math.Sin(phase) * 2; break;
                case PetPose.Drag: rotation = Math.Sin(phase * 1.2) * 9; break;
                case PetPose.Wake: scaleY = 1 + Math.Sin(Math.Min(elapsed / 2.6, 1) * Math.PI) * .045; break;
                case PetPose.Surprise: y = -8 * Math.Abs(Math.Sin(phase)) * Math.Max(0, 1 - elapsed); break;
                case PetPose.Sleep: scaleY = 1 + Math.Sin(phase * .3) * .018; break;
                case PetPose.Tired: case PetPose.BegTokens: case PetPose.TokenDazed: break;
                case PetPose.TypingLeft: case PetPose.TypingRight: break; // Only the two hand frames change while typing.
                case PetPose.RiceHat: case PetPose.RiceHatTilt: rotation = Math.Sin(phase * .65) * 2; break;
                case PetPose.Run: y = -5 * Math.Abs(Math.Sin(phase * 2)); rotation = Math.Sin(phase * 2) * 3; break;
                case PetPose.Stretch: scaleY = 1 + Math.Sin(Math.Min(elapsed / 2.5, 1) * Math.PI) * .04; break;
                case PetPose.Hum: rotation = Math.Sin(phase * .5) * 3; break;
                default: y = Math.Sin(now / 900d) * 2; break;
            }
        }
        // Apply each final transform once, rather than resetting it and setting it again.
        var facing = Sprite.IsAnimationMirrored ? -1d : 1d; if (_scale.ScaleX != facing) _scale.ScaleX = facing;
        if (_offset.Y != y) _offset.Y = y;
        if (_rotation.Angle != rotation) _rotation.Angle = rotation;
        if (_scale.ScaleY != scaleY) _scale.ScaleY = scaleY;
        var bubble = !animate ? behavior.StillBubble : behavior.Bubble;
        if (now < _announcementUntil) bubble = _announcement;
        if (_message.Text != bubble) _message.Text = bubble;
        var visibility = bubbles && bubble.Length > 0 && (!behavior.IsSleeping || elapsed < 5) ? Visibility.Visible : Visibility.Collapsed;
        if (_bubble.Visibility != visibility) _bubble.Visibility = visibility;
    }

    public Point CursorDirection(Point screen)
    {
        var local = PointFromScreen(screen);
        return new((local.X - Width / 2) / Math.Max(Width, 1), (local.Y - (78 + (Height - 96) * .42)) / Math.Max(Height - 96, 1));
    }

    // Share the service's existing pointer sample; no extra timer or mouse hook.
    public bool PollHover(Point? screen, long now)
    {
        if (_dragging || _pressScreen is not null || Sprite.ContextMenu?.IsOpen == true || Mouse.LeftButton == MouseButtonState.Pressed)
        { SuppressHover(); return false; }
        var inside = IsVisible && screen is { } point && Sprite.IsOpaqueAt(Sprite.PointFromScreen(point));
        if (!inside)
        {
            _hoverSince = null; _outsideSince ??= now;
            // Brief gaps caused by a moving hand or tail do not count as re-entry.
            if (now - _outsideSince.Value >= 300) _hoverConsumed = false;
            return false;
        }
        _outsideSince = null;
        if (_hoverConsumed) return false;
        _hoverSince ??= now;
        if (now - _hoverSince.Value < 250) return false;
        _hoverConsumed = true;
        return true;
    }

    private void SuppressHover()
    { _hoverConsumed = true; _hoverSince = null; _outsideSince = null; }

    public bool EnsureVisible()
    {
        var restored = WindowState == WindowState.Minimized || !IsVisible;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) Show();
        if (restored) EnsureTopmost();
        return restored;
    }

    public void EnsureTopmost()
    {
        if (IsVisible && Sprite.ContextMenu?.IsOpen != true)
            NativeShell.KeepAbove(new WindowInteropHelper(this).Handle);
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        SuppressHover();
        if (e.ClickCount == 2) { Interacted?.Invoke(PetTrigger.Feed); e.Handled = true; return; }
        if (!DesktopPetMonitor.TryCursor(out var screen)) return;
        _pressScreen = screen; _pressWindow = new(Left, Top); Sprite.CaptureMouse(); e.Handled = true;
    }
    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_pressScreen is not { } start || e.LeftButton != MouseButtonState.Pressed || !DesktopPetMonitor.TryCursor(out var screen)) return;
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var delta = fromDevice.Transform(screen - start);
        if (!_dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < 6) return;
        if (!_dragging) { _dragging = true; Interacted?.Invoke(PetTrigger.Drag); }
        Left = _pressWindow.X + delta.X; Top = _pressWindow.Y + delta.Y;
    }
    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (_pressScreen is null) return;
        var dragged = _dragging;
        FinishDrag(); Sprite.ReleaseMouseCapture();
        if (!dragged) Interacted?.Invoke(PetTrigger.Click);
        e.Handled = true;
    }
    private void FinishDrag()
    {
        _pressScreen = null;
        if (!_dragging) return;
        _dragging = false; ClampToScreen();
        Interacted?.Invoke(PetTrigger.Release); PositionCommitted?.Invoke(Left, Top);
    }

    public void ClampToScreen()
    {
        if (_placing || _dragging) return;
        _placing = true;
        try
        {
            var work = WorkingArea();
            Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        }
        finally { _placing = false; }
    }

    private Rect WorkingArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var work = handle == 0 ? Forms.Screen.PrimaryScreen!.WorkingArea : Forms.Screen.FromHandle(handle).WorkingArea;
        var scale = handle == 0 ? 1 : VisualTreeHelper.GetDpi(this).DpiScaleX;
        return new Rect(work.Left / scale, work.Top / scale, work.Width / scale, work.Height / scale);
    }

    private nint WindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Top-level pet windows receive power and clock broadcasts without another listener.
        if (message == 0x1E || message == 0x218 && ((long)wParam is 4 or 7 or 18)) ClockReset?.Invoke();
        if (message == 0x21) { handled = true; return 3; } // MA_NOACTIVATE
        if (message == 0x84 && !_dragging)
        {
            var screen = new Point(unchecked((short)(long)lParam), unchecked((short)((long)lParam >> 16)));
            if (!Sprite.IsOpaqueAt(Sprite.PointFromScreen(screen))) { handled = true; return -1; }
        }
        return 0;
    }
}
