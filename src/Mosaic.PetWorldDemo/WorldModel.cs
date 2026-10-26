using Mosaic.Core;
using Mosaic.Core.Models;

namespace Mosaic.PetWorldDemo;

public readonly record struct FloorPoint(double X, double Y)
{
    public double Distance(FloorPoint other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
    public static FloorPoint Lerp(FloorPoint a, FloorPoint b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}
public sealed record RoomObject(string Kind, double X, double Y, double Width, double Depth, double Height)
{
    public bool Contains(FloorPoint p, double margin = .28) => p.X > X - margin && p.X < X + Width + margin && p.Y > Y - margin && p.Y < Y + Depth + margin;
}
public enum SceneAction { Idle, Greet, Wave, Goodbye, Work, Signature, Cheer, Eat, Pat, Pout, Sleep, Wake, Tired, AskToken, TokenOops, Surprise, Drag }
public sealed record Direction(int Actor, SceneAction Action, string Activity, FloorPoint? Destination = null);
public sealed record StoryBeat(int Chapter, double Seconds, string Summary, Direction[] Directions, int? Speaker = null, string? Line = null);

public sealed class WorldActor(int id, string name, PetCharacter character, string color, FloorPoint home)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public PetCharacter Character { get; } = character;
    public string Color { get; } = color;
    public FloorPoint Home { get; } = home;
    public FloorPoint Position { get; internal set; } = home;
    public FloorPoint Destination { get; internal set; } = home;
    public Queue<FloorPoint> Route { get; internal set; } = new();
    public SceneAction Action { get; internal set; }
    public string Activity { get; internal set; } = "各自安顿";
    public double ActionStarted { get; internal set; }
    public double BlockedSeconds { get; internal set; }
    public bool Dragging { get; internal set; }
    public bool Moving => Route.Count > 0;
    public HashSet<PetPose> SeenPoses { get; } = [];

    public PetPose Pose(double time)
    {
        var elapsed = Math.Max(0, time - ActionStarted);
        if (Dragging) return PetPose.Drag;
        if (Moving) return PetPose.Idle;
        return Action switch
        {
            SceneAction.Greet => (int)(elapsed / .45) % 2 == 0 ? PetPose.GreetA : PetPose.GreetB,
            SceneAction.Wave => PetPose.Wave,
            SceneAction.Goodbye => PetPose.Goodbye,
            SceneAction.Work => (int)(elapsed / .22) % 2 == 0 ? PetPose.TypingLeft : PetPose.TypingRight,
            SceneAction.Signature => (int)(elapsed / 1.2) % 2 == 0 ? PetPose.RiceHat : PetPose.RiceHatTilt,
            SceneAction.Cheer => PetPose.Cheer,
            SceneAction.Eat => PetPose.Eat,
            SceneAction.Pat => PetPose.Pat,
            SceneAction.Pout => PetPose.Pout,
            SceneAction.Sleep => PetPose.Sleep,
            SceneAction.Wake => PetPose.Wake,
            SceneAction.Tired => PetExpressionKeyframes.At(PetPose.Tired, (long)(elapsed * 1000)),
            SceneAction.AskToken => PetExpressionKeyframes.At(PetPose.BegTokens, (long)(elapsed * 1000)),
            SceneAction.TokenOops => PetExpressionKeyframes.At(PetPose.TokenDazed, (long)(elapsed * 1000)),
            SceneAction.Surprise => PetPose.Surprise,
            SceneAction.Drag => PetPose.Drag,
            _ => IdlePose(time)
        };
    }
    private PetPose IdlePose(double time)
    {
        var local = time + Id * 2.3;
        if (local % 4.7 < .16) return PetPose.Blink;
        return ((int)(local / 3) % 7) switch
        { 1 => PetPose.LookLeft, 2 => PetPose.LookRight, 3 => PetPose.LookUp, 4 => PetPose.LookDown, _ => PetPose.Idle };
    }
}

public static class WorldRoom
{
    public static readonly RoomObject[] Objects =
    [
        new("shelf", .15, .5, .6, 2.2, 170),
        new("shelf", .15, 3.2, .6, 1.5, 135),
        new("tea", 8.7, .45, 2.4, 1.0, 57),
        new("desk", 4.4, 2.65, 2.3, 1.25, 48),
        new("table", 2.0, 5.25, 1.8, .85, 32),
        new("plant", 10.9, 7.9, .6, .6, 78),
        new("plant", 10.8, 2.5, .6, .6, 91)
    ];
    public static bool Walkable(FloorPoint p) => p.X >= 1 && p.X <= 11.5 && p.Y >= 1 && p.Y <= 8.5 && !Objects.Any(o => o.Contains(p));
    public static FloorPoint ClosestFree(FloorPoint requested, IEnumerable<WorldActor> actors, int except)
    {
        var others = actors.Where(a => a.Id != except).ToArray();
        return Cells().Where(p => Walkable(p) && others.All(a => a.Position.Distance(p) >= .85 && a.Destination.Distance(p) >= .85))
            .OrderBy(p => p.Distance(requested)).First();
    }
    private static IEnumerable<FloorPoint> Cells()
    { for (var x = 2; x <= 23; x++) for (var y = 2; y <= 17; y++) yield return new(x / 2d, y / 2d); }

    // Four-connected navigation keeps every segment outside the expanded furniture bounds.
    public static Queue<FloorPoint> FindRoute(FloorPoint from, FloorPoint to, IEnumerable<FloorPoint>? occupied = null)
    {
        var blockers = occupied?.ToArray() ?? [];
        var goal = ((int)Math.Round(to.X * 2), (int)Math.Round(to.Y * 2));
        FloorPoint Point((int x, int y) cell) => new(cell.x / 2d, cell.y / 2d);
        bool Free((int x, int y) cell) => Walkable(Point(cell)) && blockers.All(p => p.Distance(Point(cell)) >= .77);
        bool ClearSegment(FloorPoint a, FloorPoint b)
        {
            var samples = Math.Max(1, (int)Math.Ceiling(a.Distance(b) / .06));
            for (var i = 1; i <= samples; i++)
            {
                var sample = FloorPoint.Lerp(a, b, i / (double)samples);
                if (!Walkable(sample) || blockers.Any(p => p.Distance(sample) < .735)) return false;
            }
            return true;
        }
        // A blocked actor can be between grid cells. Choose a reachable entry cell;
        // blindly snapping to the nearest cell can put the first segment inside its neighbour.
        var entry = Cells().Where(p => p.Distance(from) <= 1.5 && Free(((int)(p.X * 2), (int)(p.Y * 2))) && ClearSegment(from, p))
            .OrderBy(p => p.Distance(from)).Cast<FloorPoint?>().FirstOrDefault();
        if (entry is null || !Free(goal)) return new();
        var start = ((int)(entry.Value.X * 2), (int)(entry.Value.Y * 2));
        var frontier = new PriorityQueue<(int x, int y), double>();
        var cost = new Dictionary<(int, int), int> { [start] = 0 };
        var parent = new Dictionary<(int, int), (int, int)>();
        frontier.Enqueue(start, 0);
        while (frontier.TryDequeue(out var node, out _))
        {
            if (node == goal)
            {
                var route = new List<FloorPoint>();
                for (var c = goal; c != start; c = parent[c]) route.Add(Point(c));
                route.Reverse();
                // The snapped start may be a short diagonal away after user dragging.
                if (from.Distance(Point(start)) > .01) route.Insert(0, Point(start));
                return new(route);
            }
            foreach (var delta in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (node.x + delta.Item1, node.y + delta.Item2);
                var nextCost = cost[node] + 1;
                if (!Free(next) || !ClearSegment(Point(node), Point(next)) || cost.TryGetValue(next, out var known) && known <= nextCost) continue;
                cost[next] = nextCost; parent[next] = node;
                frontier.Enqueue(next, nextCost + Math.Abs(next.Item1 - goal.Item1) + Math.Abs(next.Item2 - goal.Item2));
            }
        }
        return new();
    }
}

public sealed class WorldDirector
{
    public WorldActor[] Actors { get; } =
    [
        new(0, "DeepSeek", PetCharacter.DeepSeekChibi, "#80BFC8", new(4.5, 7)),
        new(1, "GPT", PetCharacter.Gpt, "#C8C3DC", new(6.0, 1.5)),
        new(2, "Gemini", PetCharacter.Gemini, "#A396DB", new(10, 4.5)),
        new(3, "Claude", PetCharacter.Claude, "#DFAB7C", new(2, 3.5)),
        new(4, "Qwen", PetCharacter.Qwen, "#8EABD4", new(8.5, 6.5))
    ];
    public static readonly string[] Chapters = ["开馆 · 五个座位", "共写 · 一页就好", "茶点 · 饭碗验收", "书签 · 风从哪里来", "Token · 预算小事故", "午后 · 休息也算计划"];
    public IReadOnlyList<StoryBeat> Story { get; }
    public int BeatIndex { get; private set; }
    public StoryBeat Current => Story[BeatIndex];
    public double Time { get; private set; }
    public double BeatElapsed { get; private set; }
    public bool Paused { get; set; }
    public double Speed { get; set; } = 1;
    public int Cycle { get; private set; }
    public int? Speaker { get; private set; }
    public string Speech { get; private set; } = "";
    public string Summary { get; private set; } = "窗边留了五个座位，茶还温着。";
    public bool Gathering { get; private set; }
    public int SelectedActor { get; set; } = 3;
    public List<string> Journal { get; } = [];
    private bool _started;
    private double _interruption;
    private int? _interruptedActor;
    private SceneAction _savedAction;
    private string _savedActivity = "";
    private bool _resumeRoute;
    public WorldDirector() { Story = WorldStory.Create(Actors); Prepare(); }

    public void Advance(double seconds)
    {
        if (Paused || seconds <= 0) return;
        var remaining = Math.Min(seconds, 1) * Math.Clamp(Speed, .25, 4);
        while (remaining > .00001) { var dt = Math.Min(.04, remaining); Tick(dt); remaining -= dt; }
    }
    private void Tick(double dt)
    {
        Time += dt;
        foreach (var actor in Actors)
        {
            if (!actor.Dragging) Move(actor, dt);
            actor.SeenPoses.Add(actor.Pose(Time));
        }
        if (Actors.Any(a => a.Dragging)) return;
        if (_interruption > 0)
        {
            _interruption -= dt;
            if (_interruption <= 0)
            {
                if (_interruptedActor is int id)
                {
                    SetAction(Actors[id], _savedAction, _savedActivity);
                    if (_resumeRoute) Actors[id].Route = WorldRoom.FindRoute(Actors[id].Position, Actors[id].Destination);
                }
                _interruptedActor = null; Speaker = _started ? Current.Speaker : null; Speech = _started ? Current.Line ?? "" : "";
            }
            return;
        }
        Gathering = Actors.Any(a => a.Moving);
        if (Gathering) return;
        if (!_started)
        {
            _started = true; BeatElapsed = 0;
            foreach (var d in Current.Directions) SetAction(Actors[d.Actor], d.Action, d.Activity);
            Speaker = Current.Speaker; Speech = Current.Line ?? ""; Summary = Current.Summary;
            if (Speaker is int s && Speech.Length > 0)
            { Journal.Insert(0, $"{Actors[s].Name}：{Speech}"); if (Journal.Count > 40) Journal.RemoveAt(40); }
        }
        BeatElapsed += dt;
        if (BeatElapsed >= Current.Seconds) NextBeat();
    }
    private void Move(WorldActor actor, double dt)
    {
        if (!actor.Route.TryPeek(out var next)) return;
        var distance = actor.Position.Distance(next);
        var candidate = FloorPoint.Lerp(actor.Position, next, Math.Min(1, dt * 1.25 / Math.Max(.001, distance)));
        if (Actors.Any(a => a.Id != actor.Id && !a.Dragging && a.Position.Distance(candidate) < .72))
        {
            actor.BlockedSeconds += dt;
            if (actor.BlockedSeconds > 1.25)
            {
                var revised = WorldRoom.FindRoute(actor.Position, actor.Destination, Actors.Where(a => a.Id != actor.Id).Select(a => a.Position));
                if (revised.Count > 0) actor.Route = revised;
                actor.BlockedSeconds = 0;
            }
            return;
        }
        actor.BlockedSeconds = 0; actor.Position = candidate;
        if (candidate.Distance(next) < .001) actor.Route.Dequeue();
    }
    private void SetAction(WorldActor actor, SceneAction action, string activity)
    { actor.Action = action; actor.Activity = activity; actor.ActionStarted = Time; }
    private void Prepare()
    {
        _started = false; BeatElapsed = 0; Speaker = null; Speech = ""; Summary = Current.Summary;
        // Every beat explicitly assigns the five actors, so stale sleeping/eating poses cannot leak into a new scene.
        foreach (var d in Current.Directions)
        {
            var actor = Actors[d.Actor];
            if (d.Destination is FloorPoint desired)
            {
                actor.Destination = desired;
                actor.Route = WorldRoom.FindRoute(actor.Position, desired);
                actor.Activity = "前往" + d.Activity;
                actor.Action = SceneAction.Idle;
            }
        }
        Gathering = Actors.Any(a => a.Moving);
    }
    public void NextBeat()
    {
        CancelInterruption();
        if (++BeatIndex >= Story.Count) { BeatIndex = 0; Cycle++; }
        Prepare();
    }
    public void JumpChapter(int chapter)
    {
        if (chapter < 0 || chapter >= Chapters.Length) throw new ArgumentOutOfRangeException(nameof(chapter));
        CancelInterruption();
        foreach (var actor in Actors) { actor.Route.Clear(); actor.Dragging = false; SetAction(actor, SceneAction.Idle, "换一段故事"); }
        BeatIndex = Story.ToList().FindIndex(b => b.Chapter == chapter); Prepare();
    }
    private void CancelInterruption()
    { _interruption = 0; _interruptedActor = null; _resumeRoute = false; }
    public void Interact(int actorId, SceneAction action)
    {
        if ((uint)actorId >= Actors.Length || Actors.Any(a => a.Dragging)) return;
        CancelAndRestore();
        var actor = Actors[actorId]; SelectedActor = actorId;
        _savedAction = actor.Action; _savedActivity = actor.Activity; _interruptedActor = actorId;
        _resumeRoute = actor.Moving;
        actor.Route.Clear(); SetAction(actor, action, action switch { SceneAction.Pat => "被你摸摸头", SceneAction.Eat => "收到你的茶点", SceneAction.TokenOops => "接住一枚演出 token", _ => "和你打招呼" });
        Speaker = actorId;
        var trigger = action switch { SceneAction.Pat => PetTrigger.Pat, SceneAction.Eat => PetTrigger.Feed, SceneAction.TokenOops => PetTrigger.TokenDazed, _ => PetTrigger.Wave };
        Speech = DesktopPetDialogue.Line(actor.Character, trigger, action == SceneAction.Pat ? "嘿嘿，再摸一下嘛～" : action == SceneAction.Eat ? "白米饭！我的那一份也在！" : "鱼片，你回来啦！");
        _interruption = 5;
        actor.SeenPoses.Add(actor.Pose(Time));
    }
    private void CancelAndRestore()
    {
        if (_interruptedActor is int old)
        {
            SetAction(Actors[old], _savedAction, _savedActivity);
            if (_resumeRoute) Actors[old].Route = WorldRoom.FindRoute(Actors[old].Position, Actors[old].Destination);
        }
        CancelInterruption();
    }
    public void BeginDrag(int id)
    {
        CancelAndRestore(); SelectedActor = id;
        var actor = Actors[id]; _savedAction = actor.Action; _savedActivity = actor.Activity; _interruptedActor = id;
        actor.Dragging = true; actor.Route.Clear(); Speaker = id;
        Speech = DesktopPetDialogue.Line(actor.Character, PetTrigger.Drag, "呜哇，被鱼片捞起来了！");
        actor.SeenPoses.Add(PetPose.Drag);
    }
    public bool Drop(int id, FloorPoint requested)
    {
        var actor = Actors[id];
        actor.Position = WorldRoom.ClosestFree(requested, Actors, id); actor.Destination = actor.Position; actor.Dragging = false;
        SetAction(actor, SceneAction.Surprise, "稳稳落地"); _interruption = 2;
        Speaker = id; Speech = DesktopPetDialogue.Line(actor.Character, PetTrigger.Release, "安全着陆！");
        return true;
    }
}
