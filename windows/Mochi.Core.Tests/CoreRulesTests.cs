using System.Text.Json;

namespace Mochi.Core.Tests;

public class MochiCoreChecks
{
    private static string Temp(string name = "mochi")
    {
        var path = Path.Combine(Path.GetTempPath(), name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Executable(string directory, string name = "MochiHookCLI")
    {
        Directory.CreateDirectory(directory);
        if (OperatingSystem.IsWindows())
        {
            var ext = Path.GetExtension(name);
            if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            {
                name += ".exe";
            }
        }
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private static Dictionary<string, object?> Json(string path) => JsonFile.Read(path);

    private static List<Dictionary<string, object?>> Rows(object? value)
    {
        return Bags.Array(value).Select(Bags.Object).Where(row => row.Count > 0).ToList();
    }

    [Fact]
    public void PlayReducesEnergy()
    {
        var state = new PetState(hunger: 10, energy: 50, happiness: 40, lastDecayTs: 1_000_000);
        PetEngine.Play(state, 1_000_000);
        Assert.Equal(50 + PetRules.PlayEnergy, state.Energy);
        Assert.Equal("play", state.LastAction);
    }

    [Fact]
    public void DecayPutsThePetToSleepWithoutDeath()
    {
        var state = new PetState(hunger: 94, energy: 50, happiness: 40, lastDecayTs: 0);
        PetEngine.Tick(state, PetRules.DecayInterval);
        Assert.True(state.Asleep);
        Assert.True(state.Hunger >= PetRules.AsleepHunger);
        var frozenHunger = state.Hunger;
        var frozenEnergy = state.Energy;
        PetEngine.Tick(state, PetRules.DecayInterval * 4);
        Assert.Equal(frozenHunger, state.Hunger);
        Assert.Equal(frozenEnergy, state.Energy);
    }

    [Fact]
    public void PlayDoesNothingWhileAsleep()
    {
        var state = new PetState(energy: 40, asleep: true, lastDecayTs: 10);
        PetEngine.Play(state, 10);
        Assert.Equal(40, state.Energy);
        Assert.True(state.Asleep);
        Assert.NotEqual("play", state.LastAction);
    }

    [Fact]
    public void FeedFromAIWakesAndDedupes()
    {
        var state = new PetState(hunger: 90, energy: 0, happiness: 20, asleep: true, lastDecayTs: 50);
        Assert.True(PetEngine.FeedFromAI(state, "task-a", 50));
        Assert.False(state.Asleep);
        Assert.True(state.Hunger < 90);
        var after = new PetState(
            state.Name, state.Hunger, state.Energy, state.Happiness, state.Asleep, state.LastDecayTs,
            state.LastAction, state.LastActionTs, state.FedTaskIDs.ToList());
        Assert.False(PetEngine.FeedFromAI(state, "task-a", 50));
        Assert.Equal(after, state);
    }

    [Fact]
    public void FedTaskIDsStayBounded()
    {
        var state = new PetState(lastDecayTs: 10);
        for (var index = 0; index < AppConfig.FedTaskLimit + 5; index++)
        {
            PetEngine.FeedFromAI(state, "id-" + index, 10);
        }
        Assert.Equal(AppConfig.FedTaskLimit, state.FedTaskIDs.Count);
        Assert.DoesNotContain("id-0", state.FedTaskIDs);
        Assert.Contains("id-" + (AppConfig.FedTaskLimit + 4), state.FedTaskIDs);
    }

    [Fact]
    public void LowHappinessDoesNotSleep()
    {
        var state = new PetState(hunger: 10, energy: 80, happiness: 0, lastDecayTs: 5);
        PetEngine.Tick(state, 5);
        Assert.False(state.Asleep);
        Assert.Equal("angry", PetEngine.Mood(state, 5));
    }

    [Fact]
    public void MoodsMatchTheOldThresholds()
    {
        const double now = 1_000_000;
        Assert.Equal("happy", PetEngine.Mood(new PetState(lastDecayTs: now), now));
        Assert.Equal("playful", PetEngine.Mood(new PetState(hunger: 30, energy: 50, lastDecayTs: now, lastAction: "play", lastActionTs: now), now));
        Assert.Equal("sleepy", PetEngine.Mood(new PetState(hunger: 30, energy: 20, lastDecayTs: now), now));
        Assert.Equal("hungry", PetEngine.Mood(new PetState(hunger: 60, energy: 70, lastDecayTs: now), now));
        Assert.Equal("angry", PetEngine.Mood(new PetState(hunger: 60, energy: 50, lastDecayTs: now, lastAction: "play", lastActionTs: now), now));
    }

    [Fact]
    public void HoverLinesMatchEachState()
    {
        const double now = 1_000_000;
        Assert.Equal("心情正好。", PetEngine.HoverLine(new PetState(lastDecayTs: now), now));
        Assert.Equal("心情正好。", PetEngine.Snapshot(new PetState(lastDecayTs: now), now)["hover"]);
        Assert.Equal("想玩一会儿。", PetEngine.HoverLine(new PetState(hunger: 30, energy: 50, lastDecayTs: now, lastAction: "play", lastActionTs: now), now));
        Assert.Equal("有点困了。", PetEngine.HoverLine(new PetState(hunger: 30, energy: 20, lastDecayTs: now), now));
        Assert.Equal("肚子有点空。", PetEngine.HoverLine(new PetState(hunger: 60, energy: 70, lastDecayTs: now), now));
        Assert.Equal("想自己待一会儿。", PetEngine.HoverLine(new PetState(hunger: 60, energy: 50, lastDecayTs: now, lastAction: "play", lastActionTs: now), now));
        Assert.Equal("sleepy", PetEngine.Mood(new PetState(hunger: 30, energy: 20, asleep: true, lastDecayTs: now), now));
        Assert.Equal("在睡觉。", PetEngine.HoverLine(new PetState(hunger: 30, energy: 20, asleep: true, lastDecayTs: now), now));
        var fed = new PetState(hunger: 30, energy: 70, lastDecayTs: now, lastAction: "feed", lastActionTs: now);
        Assert.Equal("吃到啦。", PetEngine.HoverLine(fed, now));
        Assert.Equal("心情正好。", PetEngine.HoverLine(fed, now + PetRules.MoodActionHold));
        foreach (var line in new[] { "心情正好。", "想玩一会儿。", "有点困了。", "肚子有点空。", "想自己待一会儿。", "在睡觉。", "吃到啦。" })
        {
            Assert.DoesNotContain("新宠物", line);
            Assert.DoesNotContain("死", line);
            Assert.DoesNotContain("对不起", line);
        }
    }

    [Fact]
    public void LegacyDeadSaveBecomesAsleep()
    {
        var directory = Temp();
        var legacy = Path.Combine(directory, "old.json");
        File.WriteAllText(legacy, "{\"name\":\"Mochi\",\"hunger\":99,\"energy\":10,\"happiness\":10,\"is_alive\":false,\"last_decay_ts\":1,\"last_action\":\"\",\"last_action_ts\":0}");
        var store = new PetStore(Path.Combine(directory, "live"));
        var loaded = store.Load([legacy]);
        Assert.True(loaded.Asleep);
        Assert.Equal(99, loaded.Hunger);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void SaveClampsHunger()
    {
        var store = new PetStore(Temp());
        store.Save(new PetState(name: "TestCat", hunger: 150));
        var loaded = store.Load();
        Assert.Equal("TestCat", loaded.Name);
        Assert.Equal(100, loaded.Hunger);
    }

    [Fact]
    public void OnlyRunningToDoneFeeds()
    {
        var tracker = new TaskTransitionTracker();
        Assert.Empty(tracker.NewlyCompleted([
            new CursorTask("a", "A", "running"),
            new CursorTask("b", "B", "done")
        ]));
        Assert.Equal(["a"], tracker.NewlyCompleted([
            new CursorTask("a", "A", "done"),
            new CursorTask("b", "B", "done"),
            new CursorTask("c", "C", "fail")
        ]));
    }

    [Fact]
    public void TaskCuesPlayOncePerTransition()
    {
        var tracker = new TaskCueTracker();
        var running = new TaskCueEvent("a", "running", 0, 1);
        Assert.Empty(tracker.Observe([running]));
        Assert.True(tracker.Primed);
        Assert.Empty(tracker.Observe([running]));
        Assert.Equal([TaskCue.Submit], tracker.Observe([new TaskCueEvent("a", "running", 1, 2)]));
        Assert.Empty(tracker.Observe([new TaskCueEvent("a", "running", 1, 2)]));
        Assert.Equal([TaskCue.Stuck], tracker.Observe([new TaskCueEvent("a", "wait", 1, 3)]));
        Assert.Empty(tracker.Observe([new TaskCueEvent("a", "running", 1, 4)]));
        Assert.Equal([TaskCue.Complete], tracker.Observe([new TaskCueEvent("a", "done", 1, 5)]));
        Assert.Empty(tracker.Observe([new TaskCueEvent("a", "done", 1, 5)]));
        Assert.Equal([TaskCue.Stuck], tracker.Observe([new TaskCueEvent("b", "fail", 0, 6)]));
        Assert.Empty(tracker.Observe([new TaskCueEvent("c", "running", 0, 7)]));
        Assert.Equal([TaskCue.Submit], tracker.Observe([new TaskCueEvent("d", "running", 1, 8)]));
        Assert.Equal([TaskCue.Submit], tracker.Observe([new TaskCueEvent("d", "running", 2, 9)]));
        Assert.Equal([TaskCue.Complete, TaskCue.Stuck], tracker.Observe([
            new TaskCueEvent("early", "wait", 0, 10),
            new TaskCueEvent("late", "done", 1, 20)
        ]));

        const string id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var submit = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeSubmitPrompt"),
            ("conversation_id", id),
            ("prompt", "ship the cue")))!;
        Assert.True(submit.PromptSubmitted);
        var tasks = new Dictionary<string, object?>();
        HookEvents.Merge(submit, tasks, 1);
        Assert.Equal(1, Bags.Whole(Bags.Object(tasks[id])["submit_gen"]));
        var empty = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeSubmitPrompt"),
            ("conversation_id", id),
            ("prompt", "")))!;
        Assert.False(empty.PromptSubmitted);
        HookEvents.Merge(empty, tasks, 2);
        Assert.Equal(1, Bags.Whole(Bags.Object(tasks[id])["submit_gen"]));
        var shell = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeShellExecution"),
            ("conversation_id", id),
            ("command", "echo hi")))!;
        Assert.False(shell.PromptSubmitted);
        HookEvents.Merge(shell, tasks, 4);
        Assert.Equal("wait", Bags.Text(Bags.Object(tasks[id])["status"]));
        Assert.Equal(1, Bags.Whole(Bags.Object(tasks[id])["submit_gen"]));
        var follow = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "UserPromptSubmit"),
            ("conversation_id", id),
            ("prompt", "once more")))!;
        HookEvents.Merge(follow, tasks, 5);
        Assert.Equal(2, Bags.Whole(Bags.Object(tasks[id])["submit_gen"]));
        Assert.False(Bags.Object(tasks[id]).ContainsKey("prompt"));
        var url = Path.Combine(Temp(), "tasks.json");
        HookEvents.Commit(submit, url, 7);
        Assert.Equal(1, TaskReader.Load(url, 8).Single(task => task.Id == id).SubmitGen);
        var glass = ToneSynth.Render("glass");
        var chime = ToneSynth.Render("chime");
        var drop = ToneSynth.Render("drop");
        Assert.NotEqual(glass, chime);
        Assert.NotEqual(chime, drop);
        Assert.True(glass.Length < 44100 && chime.Length < 44100 && drop.Length < 44100);
        Assert.True(ToneSynth.Peak("glass") > 0.05);
        Assert.Equal(["boop", "chirp", "mew", "pop", "twinkle"], IslandPrefs.SoundIDs);
    }

    [Fact]
    public void SpecConstantsAndShellGeometry()
    {
        Assert.Equal("glassEffect.regular", AppConfig.MaterialAPI);
        Assert.Equal(12, AppConfig.BubbleRadius);
        Assert.Equal(10, AppConfig.BeakWidth);
        Assert.Equal(16, AppConfig.BeakHeight);
        Assert.Equal(10, AppConfig.BeakGap);
        Assert.True(Math.Abs(AppConfig.BeakTipRadius - 2.8) < 0.001);
        Assert.Equal(160, AppConfig.BubbleMinWidth);
        Assert.Equal(280, AppConfig.BubbleMaxWidth);
        Assert.Equal(56, AppConfig.KittenSize);
        Assert.Equal(56, AppConfig.CapsuleWidth);
        Assert.Equal(28, AppConfig.AgentIcon);
        Assert.Equal(3, AppConfig.DragThreshold);
        Assert.Equal(420, AppConfig.SettingsWidth);
        Assert.Equal(169, ShellGeometry.ContentHeight(0));
        Assert.Equal(217, ShellGeometry.ContentHeight(1));
        Assert.Equal(361, ShellGeometry.ContentHeight(4));
        Assert.Equal(553, ShellGeometry.ContentHeight(8));
        var screen = new YupRect(0, 0, 1440, 900);
        var anchor = ShellGeometry.DefaultAnchor(screen, screen);
        Assert.True(Math.Abs(anchor.X - (1440 - 8 - 28)) < 0.1);
        Assert.True(Math.Abs(anchor.Y - 450) < 0.1);
        var ratios = ShellGeometry.Ratios(anchor, screen);
        Assert.True(ratios.X > 0.9 && ratios.X <= 1);
        var clamped = ShellGeometry.Ratios(new YupPoint(-40, 2000), screen);
        Assert.Equal(0, clamped.X);
        Assert.Equal(1, clamped.Y);
        var visible = new YupRect(0, 0, 800, 500);
        var collapsed = ShellGeometry.PlaceCapsule(new YupPoint(400, 40), false, 4, visible);
        Assert.Equal(56, collapsed.Frame.Height);
        Assert.Equal(12, collapsed.Frame.MinY);
        var opened = ShellGeometry.PlaceCapsule(new YupPoint(400, 40), true, 4, visible);
        Assert.True(opened.Upward);
        Assert.False(opened.Scrolls);
        Assert.True(Math.Abs(opened.Frame.MinY - collapsed.Frame.MinY) < 0.1);
        Assert.True(Math.Abs(opened.Frame.Height - ShellGeometry.ContentHeight(4)) < 0.1);
        var roomy = new YupRect(0, 0, 800, 900);
        var grew = ShellGeometry.PlaceCapsule(new YupPoint(400, 600), true, 4, roomy);
        var grewClosed = ShellGeometry.PlaceCapsule(new YupPoint(400, 600), false, 4, roomy);
        Assert.False(grew.Upward);
        Assert.True(Math.Abs(grew.Frame.MaxY - grewClosed.Frame.MaxY) < 0.1);
        Assert.Equal(AppConfig.CapsuleWidth, grew.Frame.Width);
        Assert.Equal(ShellGeometry.ContentHeight(4), grew.Frame.Height);
        var crowded = ShellGeometry.PlaceCapsule(new YupPoint(400, 250), true, 20, visible);
        Assert.True(crowded.Scrolls);
        Assert.False(crowded.Upward);
        Assert.True(crowded.Frame.Height <= visible.Height);
        var low = new YupRect(0, 0, 800, 140);
        var lowClosed = ShellGeometry.PlaceCapsule(new YupPoint(400, 40), false, 4, low);
        var lowOpen = ShellGeometry.PlaceCapsule(new YupPoint(400, 40), true, 4, low);
        Assert.True(lowOpen.Upward);
        Assert.True(lowOpen.Scrolls);
        Assert.True(Math.Abs(lowOpen.Frame.MinY - lowClosed.Frame.MinY) < 0.1);
        var down = ShellGeometry.AgentChrome(4, 1, false);
        var up = ShellGeometry.AgentChrome(4, 1, true);
        var block = 4 * AppConfig.AgentRow + 3 * AppConfig.RowGap;
        Assert.True(Math.Abs(down.AgentsTop + block + down.Tail - ShellGeometry.ContentHeight(4)) < 0.1);
        Assert.True(Math.Abs(up.AgentsTop + block + up.Tail - ShellGeometry.ContentHeight(4)) < 0.1);
        var rightCapsule = new YupRect(1000, 200, AppConfig.CapsuleWidth, 361);
        var rightAnchor = new YupRect(rightCapsule.MidX - 14, 280, 28, 28);
        var rightBubble = BubblePlacer.Place(rightAnchor, rightCapsule, new YupSize(220, 120), screen);
        Assert.True(rightBubble.PointsRight);
        Assert.True(Math.Abs(rightBubble.Frame.MaxX - (rightCapsule.MinX - AppConfig.BeakGap)) < 0.1);
        Assert.False(rightBubble.Frame.Intersects(rightCapsule));
        var leftCapsule = new YupRect(16, 200, AppConfig.CapsuleWidth, 361);
        var leftAnchor = new YupRect(leftCapsule.MidX - 14, 280, 28, 28);
        var leftBubble = BubblePlacer.Place(leftAnchor, leftCapsule, new YupSize(220, 120), screen);
        Assert.False(leftBubble.PointsRight);
        Assert.True(Math.Abs(leftBubble.Frame.MinX - (leftCapsule.MaxX + AppConfig.BeakGap)) < 0.1);
        Assert.False(leftBubble.Frame.Intersects(leftCapsule));
        var openH = ShellGeometry.ContentHeight(1);
        Assert.False(CapsuleOutline.Contains(new YupPoint(1, 1), AppConfig.CapsuleWidth, openH));
        Assert.True(CapsuleOutline.Contains(new YupPoint(AppConfig.CapsuleWidth / 2, openH / 2), AppConfig.CapsuleWidth, openH));
        Assert.Equal("", BubbleAge.Relative(0, 100));
        Assert.Equal("now", BubbleAge.Relative(100, 130));
        Assert.Equal("1m", BubbleAge.Relative(100, 160));
        Assert.Equal("1h", BubbleAge.Relative(100, 100 + 3600));
        Assert.Equal("47h", BubbleAge.Relative(100, 100 + 47 * 3600));
        Assert.Equal("2d", BubbleAge.Relative(100, 100 + 50 * 3600));
        Assert.True(Math.Abs(ShellScale.Factor(1.37) - 1.37) < 0.0001);
        Assert.Equal(1.5, IslandPrefs.Parse(new Dictionary<string, object?> { ["pet_scale"] = 1.5 }).PetScale);
        Assert.Equal(2, IslandPrefs.Parse(new Dictionary<string, object?> { ["pet_scale"] = 4 }).PetScale);
        Assert.Equal(1, IslandPrefs.Parse(new Dictionary<string, object?> { ["pet_scale"] = 0 }).PetScale);
        Assert.Equal(["a", "b", "c", "d", "e", "f"], IslandPrefs.PetLooks);
        Assert.Equal("b", IslandPrefs.Parse(new Dictionary<string, object?> { ["look"] = "b" }).Look);
        Assert.Equal("a", IslandPrefs.Parse(new Dictionary<string, object?> { ["look"] = "mochi" }).Look);
        Assert.Equal("c", IslandPrefs.Parse(new Dictionary<string, object?> { ["look"] = "!!!" }, new IslandPrefs { Look = "c" }).Look);
        Assert.Equal("f", IslandPrefs.NormalizeLook("F"));
        Assert.Equal("D", IslandPrefs.PetLetter("d"));
        var gate = new AlertSoundGate();
        Assert.True(gate.ShouldPlay(10));
        Assert.False(gate.ShouldPlay(10.4));
        Assert.True(gate.ShouldPlay(11));
        foreach (var name in IslandPrefs.SoundIDs) Assert.True(ToneSynth.Peak(name) > 0.05);
        var fresh = new IslandPrefs();
        Assert.Equal("glass", fresh.SoundSubmit);
        Assert.Equal("chime", fresh.SoundDone);
        Assert.Equal("drop", fresh.SoundStuck);
        var chosen = IslandPrefs.Parse(new Dictionary<string, object?>
        {
            ["sound_submit"] = "ripple",
            ["sound_done"] = "spark",
            ["sound_stuck"] = "hush"
        });
        Assert.Equal("ripple", chosen.SoundSubmit);
        Assert.Equal("spark", chosen.SoundDone);
        Assert.Equal("hush", chosen.SoundStuck);
        var kept = IslandPrefs.Parse(new Dictionary<string, object?> { ["sound_submit"] = "nope" }, new IslandPrefs { SoundSubmit = "ping" });
        Assert.Equal("ping", kept.SoundSubmit);
        Assert.Equal("ripple", chosen.Dictionary()["sound_submit"]);
        Assert.Equal("spark", IslandPrefs.Parse(chosen.Dictionary()).SoundDone);
        foreach (var name in IslandPrefs.SubmitSounds.Concat(IslandPrefs.DoneSounds).Concat(IslandPrefs.StuckSounds))
        {
            Assert.NotEmpty(ToneSynth.Recipe(name));
            var once = ToneSynth.Render(name);
            var cue = ToneSynth.RenderCue(name);
            var silence = (int)(ToneSynth.CueGap * ToneSynth.SampleRate);
            Assert.Equal(3, ToneSynth.CueRepeats);
            Assert.Equal(once.Length * 3 + silence * 2, cue.Length);
            Assert.True(cue.Max(Math.Abs) > 0.05);
        }
        var legacy = IslandPrefs.Decode("{\"sound\":\"pop\"}");
        Assert.Equal("pop", legacy.Sound);
        Assert.Equal("glass", legacy.SoundSubmit);
        var flipped = ShellGeometry.ToTopLeft(new YupRect(10, 12, 56, 56), 900);
        Assert.Equal(900 - 68, flipped.Y);
    }

    [Fact]
    public void KittenPositionParsesAndClamps()
    {
        var high = IslandPrefs.Parse(new Dictionary<string, object?>
        {
            ["kitten_x"] = 1.4,
            ["kitten_y"] = -0.2,
            ["kitten_placed"] = true
        });
        Assert.Equal(1, high.KittenX);
        Assert.Equal(0, high.KittenY);
        Assert.True(high.KittenPlaced);
        Assert.Equal("right", high.Dock);
        Assert.Equal(1d, high.Dictionary()["kitten_x"]);
        var left = IslandPrefs.Parse(new Dictionary<string, object?>
        {
            ["kitten_x"] = 0.2,
            ["kitten_y"] = 0.4,
            ["kitten_placed"] = true
        });
        Assert.Equal("left", left.Dock);
        var fresh = IslandPrefs.Parse(new Dictionary<string, object?>());
        Assert.False(fresh.KittenPlaced);
        Assert.Equal("right", fresh.Dock);
        var text = high.Encode();
        Assert.Contains("kitten_x", text);
        Assert.Contains("kitten_placed", text);
        var decoded = IslandPrefs.Decode(text);
        Assert.Equal(1, decoded.KittenX);
        Assert.Equal(0, decoded.KittenY);
        Assert.True(decoded.KittenPlaced);
        var legacy = IslandPrefs.Decode("{\"dx\":2,\"dock\":\"left\"}");
        Assert.Equal(2, legacy.Dx);
        Assert.Equal("left", legacy.Dock);
        Assert.False(legacy.KittenPlaced);
    }

    [Fact]
    public void DockPrefDefaultsToRight()
    {
        Assert.Equal("right", IslandPrefs.Parse(new Dictionary<string, object?>()).Dock);
        var left = IslandPrefs.Parse(new Dictionary<string, object?> { ["dock"] = "left" });
        Assert.Equal("left", left.Dock);
        Assert.Equal("left", left.Dictionary()["dock"]);
        Assert.Equal("right", IslandPrefs.Parse(new Dictionary<string, object?> { ["dock"] = "right" }, new IslandPrefs { Dock = "left" }).Dock);
        Assert.Equal("left", IslandPrefs.Parse(new Dictionary<string, object?> { ["dock"] = "middle" }, new IslandPrefs { Dock = "left" }).Dock);
    }

    [Fact]
    public void PickerFlagsPersistAcrossPrefs()
    {
        Assert.Equal(IslandPrefs.PickerOrder, new IslandPrefs().PickerSources());
        var hidden = IslandPrefs.Parse(new Dictionary<string, object?>
        {
            ["picker_cursor"] = false,
            ["picker_gpt"] = false
        });
        Assert.Equal(["codex", "claude", "gemini", "grok-bot"], hidden.PickerSources());
        Assert.Equal(hidden.PickerSources(), IslandPrefs.Parse(hidden.Dictionary()).PickerSources());
        var text = hidden.Encode();
        Assert.Contains("picker_cursor", text);
        Assert.Equal(["codex", "claude", "gemini", "grok-bot"], IslandPrefs.Decode(text).PickerSources());
        Assert.Equal(IslandPrefs.PickerOrder, IslandPrefs.Decode("{\"sound\":\"pop\"}").PickerSources());
        var numbers = IslandPrefs.Parse(new Dictionary<string, object?>
        {
            ["picker_cursor"] = 0,
            ["picker_codex"] = 1,
            ["picker_grok"] = 0,
            ["picker_gpt"] = 1
        });
        Assert.Equal(["codex", "claude", "gemini", "gpt"], numbers.PickerSources());
    }

    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs)
    {
        return pairs.ToDictionary(pair => pair.Key, pair => pair.Value);
    }
}
