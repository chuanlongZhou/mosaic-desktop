using System.Text.Json;
using Mosaic.Core;
using Mosaic.Core.Models;
using Mosaic.Host.Services;
using Mosaic.Storage.Json;

internal static class DesktopPetTests
{
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task Settings()
    {
        var legacy = JsonSerializer.Deserialize("{\"schemaVersion\":1}", MosaicJsonContext.Configured.AppSettings)!;
        legacy.Validate();
        Assert(!legacy.Pet.Enabled && legacy.Pet.Character == PetCharacter.WhaleGirl && legacy.Pet.IdleSeconds == 90 && legacy.Pet.FollowCursor && legacy.Pet.ReactToTyping, "Old settings lost their original character or safe defaults.");
        var oldWalking = JsonSerializer.Deserialize("{\"pet\":{\"enabled\":true,\"walkWhenIdle\":true,\"character\":\"deepSeekStandard\",\"size\":256}}", MosaicJsonContext.Configured.AppSettings)!;
        oldWalking.Validate();
        Assert(oldWalking.Pet.Enabled && oldWalking.Pet.Character == PetCharacter.DeepSeekStandard && oldWalking.Pet.Size == 256, "Retired walk setting broke an existing configuration.");
        Assert(!JsonSerializer.Serialize(oldWalking, MosaicJsonContext.Configured.AppSettings).Contains("walkWhenIdle"), "Retired walk setting was saved again.");
        var root = Path.Combine(Path.GetTempPath(), "Mosaic.Tests", Guid.NewGuid().ToString("N"));
        var services = new AppServices(new JsonDocumentStore(root)); await services.InitializeAsync();
        await Task.WhenAll(services.UpdatePetAsync(pet => pet.Enabled = true), services.UpdatePetAsync(pet => { pet.Left = -800; pet.Top = 150; pet.Size = 256; pet.ReactToTyping = false; }));
        var loaded = new AppServices(new JsonDocumentStore(root)); await loaded.InitializeAsync();
        Assert(loaded.Settings.Pet is { Enabled: true, Left: -800, Top: 150, Size: 256, ReactToTyping: false }, "Concurrent pet updates or restart lost pet settings.");
        Assert(loaded.Settings.Dock.Items.Count == services.Settings.Dock.Items.Count, "Pet update changed unrelated settings.");
        foreach (var character in Enum.GetValues<PetCharacter>())
        {
            await services.UpdatePetAsync(pet => pet.Character = character);
            var restarted = new AppServices(new JsonDocumentStore(root)); await restarted.InitializeAsync();
            Assert(restarted.Settings.Pet.Character == character && restarted.Settings.Pet.Size == 256 && restarted.Settings.Pet.Left == -800, "Character selection did not persist or changed size / position.");
        }
        foreach (var bad in new DesktopPetSettings[] { new() { Character = (PetCharacter)999 }, new() { Size = 0 }, new() { IdleSeconds = 29 }, new() { Left = double.NaN }, new() { Top = double.PositiveInfinity } })
        {
            try { bad.Validate(); throw new Exception("Invalid pet settings were accepted."); } catch (InvalidDataException) { }
        }
        var copy = services.CopySettings(); copy.Pet.Enabled = false;
        Assert(services.Settings.Pet.Enabled, "Pet draft changed committed settings.");
        await MultipleSettings(services);

    }
    public static Task Behavior()
    {
        CheckExpressionsAndGreetings();
        CheckPersonalities();
        CheckStandardCharacters();
        CheckCommunityCharacters();
        CheckHoverAndClick();
        var pet = new DesktopPetBehavior();
        Assert(pet.Trigger(PetTrigger.Enter, 10000) && pet.Pose == PetPose.Cheer, "Enter did not cheer.");
        Assert(!pet.Trigger(PetTrigger.Enter, 11000), "Repeated Enter bypassed cooldown.");
        pet.Trigger(PetTrigger.Pat, 11100);
        Assert(!pet.Trigger(PetTrigger.AppClosed, 11500) && pet.Pose == PetPose.Pat, "Background action interrupted a direct interaction.");
        pet.Trigger(PetTrigger.Drag, 12000); pet.Tick(999999, 600, true, 90, true, 1, 0);
        Assert(pet.IsDragging && !pet.Trigger(PetTrigger.Enter, 1000000), "Drag was interrupted by sleep or a key.");
        pet.Trigger(PetTrigger.Release, 1000100); pet.Tick(1001500, 100, true, 90, true, 1, 0);
        Assert(pet.IsSleeping && pet.Pose == PetPose.Sleep, "Idle pet did not sleep after landing.");
        Assert(!pet.Trigger(PetTrigger.AppClosed, 1001600), "App closing woke a sleeping pet.");
        pet.Tick(1001700, 0, true, 90, true, 1, 0);
        Assert(pet.Pose == PetPose.Wake && !pet.IsSleeping, "Activity did not wake the pet.");
        pet.Tick(1005000, 0, true, 90, true, -1, 0);
        pet.Tick(1005170, 0, true, 90, true, -1, 0);
        Assert(pet.Pose == PetPose.LookLeft, "Pet did not look toward the pointer.");
        pet.Tick(1005200, 0, true, 90, false, 1, 0);
        Assert(pet.Pose == PetPose.Idle, "Disabled gaze still tracked the cursor.");
        Assert(DesktopPetBehavior.Look(0, -1) == PetPose.LookUp && DesktopPetBehavior.Look(0, 1) == PetPose.LookDown && DesktopPetBehavior.Look(1, 0) == PetPose.LookRight, "Gaze directions are inverted.");
        var keyboard = new PetKeyboardFilter();
        Assert(keyboard.Process(0x0D, true) == PetTrigger.Enter && keyboard.Process(0x0D, true) is null, "Key repeats caused duplicate actions.");
        keyboard.Process(0x0D, false); Assert(keyboard.Process(0x0D, true) == PetTrigger.Enter, "Enter release did not reset.");
        Assert(keyboard.Process(0x44, true) == PetTrigger.Typing, "Plain D did not signal typing."); keyboard.Process(0x44, false);
        keyboard.Process(0x5B, true); keyboard.Process(0x5C, true); keyboard.Process(0x5B, false);
        Assert(keyboard.Process(0x44, true) == PetTrigger.ShowDesktop && keyboard.Process(0x44, true) is null, "Right Win+D or repeat filtering failed.");
        keyboard.Process(0x44, false); keyboard.Process(0x5C, false);
        Assert(keyboard.Process(0x44, true) == PetTrigger.Typing, "Released Windows key remained pressed.");
        keyboard.Process(0x11, true); Assert(keyboard.Process(0x43, true) is null, "Ctrl+C triggered typing."); keyboard.Process(0x11, false);
        keyboard.Process(0x12, true); Assert(keyboard.Process(0x41, true) is null, "Alt shortcut triggered typing."); keyboard.Process(0x12, false);
        Assert(keyboard.Process(0x41, true) == PetTrigger.Typing && keyboard.Process(0x41, false) is null && keyboard.Process(0x20, true) == PetTrigger.Typing, "Text key / key-up / space classification failed.");
        var typing = new DesktopPetBehavior();
        Assert(typing.Trigger(PetTrigger.Typing, 10000), "Typing did not start.");
        typing.Tick(10140, 0, false, 90, false, 0, 0);
        Assert(typing.Pose == PetPose.TypingRight, "Typing did not alternate hands.");
        typing.Trigger(PetTrigger.Typing, 10700);
        Assert(typing.StartedAt == 10000 && typing.Bubble.Length == 0, "Keystroke reset the typing animation or spammed a bubble.");
        typing.Tick(11300, 0, false, 90, false, 0, 0); Assert(typing.IsTyping, "Continuous typing stopped too early.");
        typing.Tick(11700, 0, false, 90, false, 0, 0); Assert(!typing.IsTyping, "Typing did not settle after input stopped.");
        typing.Trigger(PetTrigger.Typing, 12000); typing.Tick(12100, 0, false, 90, false, 0, 0, typingEnabled: false);
        Assert(!typing.IsTyping, "Turning off typing reactions left its animation running.");
        typing.Trigger(PetTrigger.RiceHat, 13000); typing.Tick(13700, 0, false, 90, false, 0, 0);
        Assert(typing.Pose == PetPose.RiceHatTilt && !typing.Trigger(PetTrigger.Typing, 13800), "Rice bowl did not balance or lost priority over typing.");
        var input = new PetInputBuffer();
        Parallel.Invoke(
            () => { for (var i = 0; i < 100000; i++) input.Record(PetTrigger.Typing); },
            () => { input.Record(PetTrigger.Enter); input.Record(PetTrigger.ShowDesktop); input.Record(PetTrigger.AppClosed); });
        Assert(input.Take() == (PetInputActivity.Typing | PetInputActivity.Enter | PetInputActivity.ShowDesktop | PetInputActivity.AppClosed), "Input burst lost a meaningful event or failed to coalesce.");
        Assert(input.Take() == PetInputActivity.None, "Input activity backlog remained after draining.");
        return Task.CompletedTask;
    }
    private static async Task MultipleSettings(AppServices services)
    {
        var ids = new[] { PetCharacter.Gpt, PetCharacter.Gemini, PetCharacter.Claude, PetCharacter.Qwen };
        await services.UpdatePetAsync(pet => pet.Companions = ids.Select(character => new DesktopPetCompanion { Character = character }).ToList());
        var first = services.Settings.Pet.Companions[0].Id; var second = services.Settings.Pet.Companions[1].Id;
        await Task.WhenAll(services.UpdatePetAsync(pet => pet.GetPet(first).Left = 321), services.UpdatePetAsync(pet => pet.GetPet(second).Top = 234));
        var loaded = new AppServices(new JsonDocumentStore(services.Store.Root));
        await loaded.InitializeAsync();
        Assert(loaded.Settings.Pet.Slots().Count() == 5 && loaded.Settings.Pet.GetPet(first).Left == 321 && loaded.Settings.Pet.GetPet(second).Top == 234, "Independent companion updates did not survive restart.");
        Assert(loaded.Settings.Pet.Left == -800 && loaded.Settings.Pet.Size == 256, "Additional pets overwrote legacy primary settings.");
        var copy = loaded.Settings.Pet.Copy(); copy.Companions[0].Size = 128;
        Assert(loaded.Settings.Pet.Companions[0].Size == 220, "Companion draft shared mutable settings.");
        copy = loaded.Settings.Pet.Copy(); copy.Companions.Add(new());
        try { copy.Validate(); throw new Exception("A sixth pet was accepted."); } catch (InvalidDataException) { }
        copy = loaded.Settings.Pet.Copy(); copy.Companions[1].Id = copy.Companions[0].Id;
        try { copy.Validate(); throw new Exception("Duplicate companion identity was accepted."); } catch (InvalidDataException) { }
        copy = loaded.Settings.Pet.Copy(); copy.Companions[1].Character = copy.Companions[0].Character; copy.Validate();
        copy.PrimaryVisible = false; foreach (var item in copy.Companions) item.Enabled = false; copy.Enabled = false; copy.Enable();
        Assert(copy.Enabled && copy.PrimaryVisible, "Enabling an empty visible roster did not restore a companion.");
    }
    private static void CheckPersonalities()
    {
        var characters = new[] { PetCharacter.Gpt, PetCharacter.Gemini, PetCharacter.Claude, PetCharacter.Qwen };
        foreach (var trigger in new[] { PetTrigger.Wave, PetTrigger.Enter, PetTrigger.Pat, PetTrigger.Feed, PetTrigger.RiceHat, PetTrigger.Pout, PetTrigger.Tired, PetTrigger.BegTokens, PetTrigger.TokenDazed })
        {
            var lines = new HashSet<string>();
            foreach (var character in characters)
            {
                var pet = new DesktopPetBehavior(character: character); pet.Trigger(trigger, 10000);
                var line = pet.StillBubble;
                Assert(line.Length > 0 && !line.Contains("鱼片") && !line.Contains("小鲸鱼") && !pet.Bubble.Contains("鱼片") && !pet.Bubble.Contains("小鲸鱼"), "New AI reused whale-specific dialogue.");
                lines.Add(line);
            }
            Assert(lines.Count == 4, $"{trigger}: personalities do not have distinct dialogue.");
        }
        Assert(characters.Select(character => DesktopPetCharacters.Get(character).SignatureLabel).Distinct().Count() == 4, "Signature action labels are shared.");
    }
    private static void CheckExpressionsAndGreetings()
    {
        var pat = new DesktopPetBehavior(_ => 0); pat.Trigger(PetTrigger.Click, 10000);
        var greeting = new DesktopPetBehavior(_ => 1); greeting.Trigger(PetTrigger.Click, 10000);
        Assert(pat.Pose == PetPose.Pat && greeting.Pose == PetPose.GreetA, "Single click did not select both random reactions.");
        greeting.Tick(10250, 0, false, 90, false, 0, 0);
        Assert(greeting.Pose == PetPose.GreetB, "Greeting did not animate its waving hand.");
        greeting.Tick(14500, 0, false, 90, false, 0, 0);
        Assert(!greeting.IsGreeting, "Greeting did not finish.");
        foreach (var (trigger, pose, duration) in new[] { (PetTrigger.Tired, PetPose.Tired, 4000), (PetTrigger.BegTokens, PetPose.BegTokens, 4500), (PetTrigger.TokenDazed, PetPose.TokenDazed, 5000) })
        {
            var pet = new DesktopPetBehavior();
            pet.Tick(9000, 100, true, 90, false, 0, 0);
            Assert(pet.Trigger(trigger, 10000) && pet.IsExpression && pet.Pose == PetPose.Idle && pet.StillPose == pose && pet.Bubble.Length > 0 && !pet.IsSleeping, "New expression could not interrupt sleep or had no dialogue.");
            Assert(!pet.Trigger(PetTrigger.Typing, 10100), "Typing interrupted a direct expression.");
            var steps = pose switch
            {
                PetPose.Tired => new[] { (449, PetPose.Idle), (450, PetPose.TiredYawn), (1049, PetPose.TiredYawn), (1050, PetPose.Tired) },
                PetPose.BegTokens => new[] { (449, PetPose.Idle), (450, PetPose.BegReach), (1099, PetPose.BegReach), (1100, PetPose.BegTokens) },
                _ => new[] { (449, PetPose.Idle), (450, PetPose.TokenFallHigh), (799, PetPose.TokenFallHigh), (800, PetPose.TokenFallLow), (1149, PetPose.TokenFallLow), (1150, PetPose.TokenDazed) }
            };
            foreach (var (elapsed, expected) in steps)
            {
                pet.Tick(10000 + elapsed, 0, true, 90, false, 0, 0);
                Assert(pet.Pose == expected && pet.IsExpression, $"{trigger}: incorrect keyframe at {elapsed}ms.");
            }
            Assert(pet.Bubble == pet.StillBubble, "Final keyframe did not show its final dialogue.");

            pet.Tick(10000 + duration - 1, 0, true, 90, false, 0, 0);
            Assert(pet.Pose == pose, "Expression ended before its advertised duration.");
            pet.Tick(10000 + duration, 0, true, 90, false, 0, 0);
            Assert(pet.Pose != pose && pet.Bubble.Length == 0, "Expression did not settle after its duration.");
            pet.Trigger(trigger, 20000); pet.Trigger(PetTrigger.Drag, 20100);
            Assert(pet.IsDragging, "New expression prevented dragging.");
        }
        var idle = new DesktopPetBehavior(_ => 0);
        idle.Tick(10000, 89, true, 90, false, 0, 0);
        Assert(!idle.IsSleeping, "Sleep started before its configured delay.");
        idle.Tick(10100, 90, true, 90, false, 0, 0);
        Assert(idle.IsSleeping, "Idle did not sleep after removing walking.");
        idle.Tick(10200, 0, true, 90, false, 0, 0);
        Assert(idle.Pose == PetPose.Wake, "Input did not wake an idle pet.");
        idle.Tick(14000, 1000, false, 90, false, 0, 0);
        Assert(!idle.IsSleeping, "Disabled idle sleep still triggered.");
        Assert(!Enum.GetNames<PetTrigger>().Contains("Walk") && !Enum.GetNames<PetPose>().Any(name => name.StartsWith("Walk")), "Walking remains a runtime action.");
    }

    private static void CheckStandardCharacters()
    {
        Assert(DesktopPetCharacters.All.Count == 21 && DesktopPetCharacters.All.Select(character => character.Id).Distinct().Count() == 21, "Community variants replaced or duplicated an existing character.");
        Assert(DesktopPetCharacters.All.Take(11).Select(item => item.Id).SequenceEqual(Enum.GetValues<PetCharacter>().Take(11)), "The original character roster changed order.");
        Assert((int)PetCharacter.WhaleGirl == 0 && (int)PetCharacter.DeepSeekStandard == 2 && (int)PetCharacter.Gpt == 3 && (int)PetCharacter.Qwen == 6, "Existing persisted character IDs changed.");
        foreach (var (chibi, standard) in new[]
        {
            (PetCharacter.Gpt, PetCharacter.GptStandard), (PetCharacter.Gemini, PetCharacter.GeminiStandard),
            (PetCharacter.Claude, PetCharacter.ClaudeStandard), (PetCharacter.Qwen, PetCharacter.QwenStandard)
        })
        {
            var small = DesktopPetCharacters.Get(chibi); var normal = DesktopPetCharacters.Get(standard);
            Assert(small.SizeMultiplier == 1 && normal.SizeMultiplier == 2 && normal.Name.Contains("正常比例"), "Standard variant did not double its display size.");
            Assert(small.FeedLabel == normal.FeedLabel && small.SignatureLabel == normal.SignatureLabel && small.TeaseLabel == normal.TeaseLabel, "A new body type lost its character's interactions.");
            Assert(ReferenceEquals(DesktopPetDialogue.ClickLines(chibi), DesktopPetDialogue.ClickLines(standard)), "Standard dialogue did not reuse the same personality pool.");
            Assert(DesktopPetDialogue.Sleep(chibi) == DesktopPetDialogue.Sleep(standard) && DesktopPetDialogue.Wake(chibi) == DesktopPetDialogue.Wake(standard), "Standard sleep/wake used whale dialogue.");
            foreach (var pose in Enum.GetValues<PetPose>())
                Assert(DesktopPetDialogue.Preparation(chibi, pose, "fallback") == DesktopPetDialogue.Preparation(standard, pose, "fallback"), "Standard keyframe caption lost its personality.");
            foreach (var trigger in Enum.GetValues<PetTrigger>())
            {
                var a = new DesktopPetBehavior(_ => 0, chibi); var b = new DesktopPetBehavior(_ => 0, standard);
                a.Trigger(trigger, 10000); b.Trigger(trigger, 10000);
                Assert(a.Pose == b.Pose && a.Bubble == b.Bubble && a.StillBubble == b.StillBubble, "Standard body changed an action or its dialogue.");
            }
        }
        Console.WriteLine("  Standard: four independent adult identities; 2x size, legacy IDs, all actions/keyframe captions and shared personality dialogue verified.");
    }

    private static void CheckCommunityCharacters()
    {
        Assert((int)PetCharacter.DeepSeekChibiMk2 == 11 && (int)PetCharacter.GrokCommunity == 12 && (int)PetCharacter.CrossPetGlm == 20, "Community characters changed persisted IDs.");
        foreach (var (variant, original) in new[]
        {
            (PetCharacter.CrossPetGpt, PetCharacter.Gpt), (PetCharacter.CrossPetClaude, PetCharacter.Claude),
            (PetCharacter.CrossPetGemini, PetCharacter.Gemini), (PetCharacter.CrossPetQwen, PetCharacter.Qwen)
        })
            Assert(ReferenceEquals(DesktopPetDialogue.ClickLines(variant), DesktopPetDialogue.ClickLines(original)), "CrossPet appearance lost its matching dialogue.");
        foreach (var character in new[] { PetCharacter.GrokCommunity, PetCharacter.KimiCommunity, PetCharacter.ZhipuCommunity })
            Assert(!DesktopPetDialogue.ClickLines(character).Any(line => line.Contains("小鲸鱼") || line.Contains("鱼片")), "New model reused whale dialogue.");
        foreach (var (trigger, pose) in new[]
        {
            (PetTrigger.Think, PetPose.Think), (PetTrigger.Read, PetPose.Read),
            (PetTrigger.Search, PetPose.Search), (PetTrigger.Draw, PetPose.Draw),
            (PetTrigger.Stretch, PetPose.Stretch), (PetTrigger.Run, PetPose.Run),
            (PetTrigger.Hum, PetPose.Hum)
        })
        {
            var pet = new DesktopPetBehavior(character: PetCharacter.CrossPetGlm);
            Assert(pet.Trigger(trigger, 10000) && pet.Pose == pose && pet.Bubble.Length > 0, $"{trigger}: action did not start.");
            pet.Tick(20000, 0, false, 90, false, 0, 0);
            Assert((pet.Pose is PetPose.Idle or PetPose.Blink) && pet.Bubble.Length == 0, $"{trigger}: action did not settle.");
        }
    }

    private static void CheckHoverAndClick()
    {
        foreach (var choice in new[] { 0, 1 })
        {
            var pet = new DesktopPetBehavior(_ => choice);
            Assert(pet.Trigger(PetTrigger.Hover, 10000) && pet.Bubble.Length == 0, "Hover did not start a silent interaction.");
            var pose = pet.Pose; var started = pet.StartedAt;
            pet.Tick(10250, 0, false, 90, false, 0, 0);
            var frame = pet.Pose;
            Assert(!pet.Trigger(PetTrigger.Hover, 10250) && pet.StartedAt == started, "Hover restarted its animation.");
            Assert(pet.Trigger(PetTrigger.Click, 10250) && pet.StartedAt == started && pet.Pose == frame && pet.Bubble.Length > 0, "Click did not add speech to the same hover action/phase.");
            Assert(pose is PetPose.Pat or PetPose.GreetA, "Hover used a different action from click.");
            var line = pet.Bubble;
            Assert(!pet.Trigger(PetTrigger.Hover, 14000) && pet.Bubble == line, "Hover interrupted a spoken line.");
            pet.Tick(14749, 0, false, 90, false, 0, 0);
            Assert(pet.Bubble == line, "Click speech disappeared before its reading time.");
            pet.Tick(14750, 0, false, 90, false, 0, 0);
            Assert(pet.Bubble.Length == 0 && pet.Trigger(PetTrigger.Hover, 14800), "Finished interaction did not settle or allow a new hover.");
        }
        foreach (var trigger in new[] { PetTrigger.Feed, PetTrigger.Wave, PetTrigger.Drag, PetTrigger.Release, PetTrigger.RiceHat, PetTrigger.Tired, PetTrigger.BegTokens, PetTrigger.TokenDazed })
        {
            var pet = new DesktopPetBehavior(); pet.Trigger(trigger, 10000);
            Assert(!pet.Trigger(PetTrigger.Hover, 10010), "Passive hover interrupted a direct action.");
            if (trigger == PetTrigger.Drag) Assert(!pet.Trigger(PetTrigger.Click, 10020), "Click interrupted a drag.");
        }
        var sleeper = new DesktopPetBehavior();
        sleeper.Tick(10000, 100, true, 90, false, 0, 0);
        Assert(sleeper.IsSleeping && sleeper.Trigger(PetTrigger.Hover, 10010) && !sleeper.IsSleeping, "Hover could not wake the pet for interaction.");
        foreach (var character in Enum.GetValues<PetCharacter>())
        {
            var lines = DesktopPetDialogue.ClickLines(character);
            Assert(lines.Count == 20 && lines.Distinct().Count() == 20 && lines.All(line => line.Length is > 0 and <= 60), "Click dialogue pool is incomplete, duplicated or too long for a bubble.");
            var random = new Random(47);
            var pet = new DesktopPetBehavior(random.Next, character);
            var previous = ""; var heard = new HashSet<string>();
            for (var i = 0; i < 80; i++)
            {
                Assert(pet.Trigger(PetTrigger.Click, 10000 + i * 100), "A direct click was dropped.");
                Assert(lines.Contains(pet.Bubble) && pet.Bubble != previous, "Click reused its last line or another character's voice.");
                previous = pet.Bubble; heard.Add(previous);
            }
            Assert(heard.Count >= 15, "Repeated clicks failed to explore the character dialogue pool.");
        }
        Assert(DesktopPetDialogue.ClickLines(PetCharacter.DeepSeekChibi).SequenceEqual(DesktopPetDialogue.ClickLines(PetCharacter.DeepSeekStandard)), "Two forms of the same whale personality lost their shared voice.");
        Console.WriteLine("  Interaction: silent hover and click share actions/phase; 160 persona lines, no consecutive repeats, action priority and wake verified.");
    }
}
