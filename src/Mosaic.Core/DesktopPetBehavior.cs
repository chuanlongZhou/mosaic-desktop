using Mosaic.Core.Models;

namespace Mosaic.Core;

public enum PetPose { Idle, LookLeft, LookRight, LookUp, LookDown, Blink, Pat, Cheer, Wave, Goodbye, Sleep, Wake, Eat, Drag, Surprise, Pout, TypingLeft, TypingRight, RiceHat, RiceHatTilt, GreetA, GreetB, Tired, BegTokens, TokenDazed, TiredYawn, BegReach, TokenFallHigh, TokenFallLow }
public enum PetTrigger { Enter, ShowDesktop, AppClosed, Pat, Feed, Wave, Drag, Release, Pout, Typing, RiceHat, Click, Tired, BegTokens, TokenDazed, Hover }

public sealed class DesktopPetBehavior
{
    private readonly Dictionary<PetTrigger, long> _lastTrigger = [];
    private readonly Func<int, int> _choose;
    private readonly PetCharacter _character;
    private long _until, _nextBlink = 4200, _blinkUntil;
    private int _priority;
    private PetPose _pose;
    private string _finalBubble = "";
    private bool _hoverPreview;
    private int _lastClickLine = -1;
    public DesktopPetBehavior(Func<int, int>? choose = null, PetCharacter character = PetCharacter.WhaleGirl)
        => (_choose, _character) = (choose ?? Random.Shared.Next, character);
    public PetPose ActionPose => _pose;
    public PetPose Pose { get; private set; }
    public string Bubble { get; private set; } = "";
    public long StartedAt { get; private set; }
    public bool IsSleeping => _pose == PetPose.Sleep;
    public bool IsDragging => _pose == PetPose.Drag;
    public bool IsTyping => _pose == PetPose.TypingLeft;
    public bool IsGreeting => _pose == PetPose.GreetA;
    public bool IsExpression => PetExpressionKeyframes.Supports(_pose);
    public PetPose StillPose => IsExpression ? _pose : Pose;
    public string StillBubble => IsExpression ? _finalBubble : Bubble;

    public bool Trigger(PetTrigger trigger, long now)
    {
        if (trigger is PetTrigger.Hover or PetTrigger.Click) return Interact(trigger, now);
        var direct = trigger is PetTrigger.Pat or PetTrigger.Feed or PetTrigger.Wave or PetTrigger.Drag or PetTrigger.Release or PetTrigger.Pout or PetTrigger.RiceHat or PetTrigger.Tired or PetTrigger.BegTokens or PetTrigger.TokenDazed;
        var cooldown = trigger switch { PetTrigger.Enter => 2200, PetTrigger.ShowDesktop => 4000, PetTrigger.AppClosed => 6000, _ => 0 };
        if (!direct && (_pose is PetPose.Sleep or PetPose.Drag || _priority > 1 && now < _until)) return false;
        if (trigger == PetTrigger.Typing)
        {
            // Continue the animation without resetting its phase on every keystroke.
            if (IsTyping && now < _until) _until = now + 900;
            else Start(PetPose.TypingLeft, "", now, 900, 1);
            return true;
        }
        if (cooldown > 0 && _lastTrigger.TryGetValue(trigger, out var last) && now - last < cooldown) return false;
        _lastTrigger[trigger] = now;
        var (pose, bubble, duration) = trigger switch
        {
            PetTrigger.Enter => (PetPose.Cheer, "收到！给你一点鲸力气～", 1600),
            PetTrigger.ShowDesktop => (PetPose.GreetA, "欢迎回到桌面，鱼片！", 2600),
            PetTrigger.AppClosed => (PetPose.Goodbye, "忙完一个啦，辛苦了～", 2400),
            PetTrigger.Pat => (PetPose.Pat, "嘿嘿，再摸一下嘛～", 2400),
            PetTrigger.Feed => (PetPose.Eat, "白米饭！我可以再来一碗！", 4600),
            PetTrigger.Wave => (PetPose.GreetA, "我在这里陪你呀～", 2600),
            PetTrigger.Drag => (PetPose.Drag, "呜哇，被鱼片捞起来了！", int.MaxValue),
            PetTrigger.Release => (PetPose.Surprise, "安全着陆！", 1300),
            PetTrigger.RiceHat => (PetPose.RiceHat, "饭盆稳稳的！这可是我的口粮～", 6000),
            PetTrigger.Tired => (PetPose.Tired, "算力快见底了…让我缓一缓。", 4000),
            PetTrigger.BegTokens => (PetPose.BegTokens, "鱼片，投喂一点 token 好不好？", 4500),
            PetTrigger.TokenDazed => (PetPose.TokenDazed, "Token 太多啦！小鲸鱼要晕过去了…", 5000),
            _ => (PetPose.Pout, "才不是大肥鱼呢，哼～", 2200)
        };
        Start(pose, DesktopPetDialogue.Line(_character, trigger, bubble), now, duration, direct ? 3 : trigger == PetTrigger.Enter ? 1 : 2);
        return true;
    }

    private bool Interact(PetTrigger trigger, long now)
    {
        if (IsDragging) return false;
        if (trigger == PetTrigger.Hover)
        {
            // A passive hover must not restart a greeting, meal, expression or click.
            if (now < _until && _priority >= 2 || _lastTrigger.TryGetValue(trigger, out var last) && now - last < 3000) return false;
            _lastTrigger[trigger] = now;
            var pose = _choose(2) == 0 ? PetPose.Pat : PetPose.GreetA;
            Start(pose, "", now, pose == PetPose.Pat ? 2400 : 2600, 2);
            _hoverPreview = true;
            return true;
        }

        var keepPreview = _hoverPreview && now < _until;
        var reaction = keepPreview ? _pose : _choose(2) == 0 ? PetPose.Pat : PetPose.GreetA;
        var lines = DesktopPetDialogue.ClickLines(_character);
        // Select uniformly from every line except the one most recently spoken.
        var index = _choose(lines.Count - (_lastClickLine >= 0 ? 1 : 0));
        if (_lastClickLine >= 0 && index >= _lastClickLine) index++;
        _lastClickLine = index;
        if (keepPreview)
        {
            _hoverPreview = false; Bubble = _finalBubble = lines[index];
            _until = now + 4500; _priority = 3;
        }
        else Start(reaction, lines[index], now, 4500, 3);
        return true;
    }

    public void Tick(long now, double idleSeconds, bool sleepEnabled, int sleepAfterSeconds, bool followCursor, double cursorX, double cursorY, bool typingEnabled = true)
    {
        if (IsTyping && !typingEnabled) _until = 0;
        if (IsSleeping)
        {
            if (!sleepEnabled || idleSeconds < sleepAfterSeconds)
                Start(PetPose.Wake, DesktopPetDialogue.Wake(_character), now, 2600, 2);
            return;
        }
        if (_pose == PetPose.Drag) return;
        if (now < _until)
        {
            if (IsTyping) Pose = (now - StartedAt) / 130 % 2 == 0 ? PetPose.TypingLeft : PetPose.TypingRight;
            else if (_pose == PetPose.RiceHat) Pose = (now - StartedAt) / 650 % 2 == 0 ? PetPose.RiceHat : PetPose.RiceHatTilt;
            else if (IsGreeting) Pose = (now - StartedAt) / 240 % 2 == 0 ? PetPose.GreetA : PetPose.GreetB;
            else if (IsExpression)
            {
                Pose = PetExpressionKeyframes.At(_pose, now - StartedAt);
                Bubble = DesktopPetDialogue.Preparation(_character, Pose, PetExpressionKeyframes.Caption(_pose, now - StartedAt, _finalBubble));
            }
            return;
        }
        if (sleepEnabled && idleSeconds >= sleepAfterSeconds)
        {
            Start(PetPose.Sleep, DesktopPetDialogue.Sleep(_character), now, long.MaxValue - now, 0);
            return;
        }
        _pose = PetPose.Idle; _priority = 0; Bubble = ""; _hoverPreview = false;
        if (now >= _nextBlink) { _blinkUntil = now + 160; _nextBlink = now + 4200; }
        Pose = now < _blinkUntil ? PetPose.Blink : followCursor ? Look(cursorX, cursorY) : PetPose.Idle;
    }

    public static PetPose Look(double x, double y)
    {
        if (Math.Abs(x) < .18 && Math.Abs(y) < .18) return PetPose.Idle;
        return Math.Abs(x) >= Math.Abs(y) ? x < 0 ? PetPose.LookLeft : PetPose.LookRight : y < 0 ? PetPose.LookUp : PetPose.LookDown;
    }

    private void Start(PetPose pose, string bubble, long now, long duration, int priority)
    {
        _pose = pose; _finalBubble = bubble; StartedAt = now; _until = now + duration; _priority = priority; _hoverPreview = false;
        Pose = PetExpressionKeyframes.At(pose, 0);
        Bubble = IsExpression ? DesktopPetDialogue.Preparation(_character, Pose, PetExpressionKeyframes.Caption(pose, 0, bubble)) : bubble;
    }
}

// Retains modifier state only, never characters or text. Typing is a transient activity signal.
public sealed class PetKeyboardFilter
{
    private bool _leftWin, _rightWin, _enter, _d;
    private readonly HashSet<uint> _modifiers = [];
    public PetTrigger? Process(uint key, bool down)
    {
        if (key is 0x11 or 0x12 or >= 0xA2 and <= 0xA5)
        { if (down) _modifiers.Add(key); else _modifiers.Remove(key); return null; }
        switch (key)
        {
            case 0x5B: _leftWin = down; break;
            case 0x5C: _rightWin = down; break;
            case 0x0D:
                var enter = down && !_enter; _enter = down;
                if (enter) return PetTrigger.Enter;
                break;
            case 0x44:
                var desktop = down && !_d && (_leftWin || _rightWin); _d = down;
                if (desktop) return PetTrigger.ShowDesktop;
                break;
        }
        if (down && !_leftWin && !_rightWin && _modifiers.Count == 0 &&
            (key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x60 and <= 0x6F or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0x20 or 0x08 or 0x2E or 0xE2))
            return PetTrigger.Typing;
        return null;
    }
}
