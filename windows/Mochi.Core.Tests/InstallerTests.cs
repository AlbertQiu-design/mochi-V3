namespace Mochi.Core.Tests;

public class InstallerTests
{
    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "mochi-" + Guid.NewGuid().ToString("N"));
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

    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs)
    {
        return pairs.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static bool Clean(Dictionary<string, object?> record)
    {
        foreach (var banned in new[] { "prompt", "command", "tool_input", "toolInput", "message", "initialPrompt", "initial_prompt", "error", "transcript_path", "last_assistant_message", "notification", "question" })
        {
            if (record.ContainsKey(banned)) return false;
        }
        foreach (var pair in record)
        {
            if (pair.Key == "summary") continue;
            if (pair.Value is string text && text.Contains("SECRET", StringComparison.Ordinal)) return false;
        }
        return true;
    }

    [Fact]
    public void CodexAndClaudeHooksInstallMarkedCommands()
    {
        var directory = Temp();
        var binary = Executable(directory);
        var config = Path.Combine(directory, "config.toml");
        File.WriteAllText(config, "[features]\njs_repl = false\n");
        var hooks = Path.Combine(directory, "hooks.json");
        File.WriteAllText(hooks, "{\"hooks\":{\"UserPromptSubmit\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"/bin/echo keep\"}]}]}}");
        var blocked = HookInstaller.InstallCodex(config, hooks, binary, false);
        Assert.True(blocked.NeedsPrivacy);
        Assert.False(blocked.Connected);
        var installed = HookInstaller.InstallCodex(config, hooks, binary, true);
        Assert.True(installed.Connected);
        var configText = File.ReadAllText(config);
        Assert.Contains("js_repl = false", configText);
        Assert.Contains("hooks = true", configText);
        var groups = Bags.Array(Bags.Object(JsonFile.Read(hooks)["hooks"])["UserPromptSubmit"]).Select(Bags.Object).ToList();
        var commands = groups.SelectMany(group => Bags.Array(group["hooks"]).Select(Bags.Object)).ToList();
        Assert.Contains(commands, entry => Bags.Text(entry["command"]).Contains("/bin/echo keep"));
        Assert.Contains(commands, entry => Bags.Whole(entry.GetValueOrDefault(AppConfig.HookMarker)) == AppConfig.HookVersion && Bags.Text(entry["command"]).Contains("--source codex"));
        var command = binary + " --source codex";
        var trusted = CodexHookTrust.IdentityHash("user_prompt_submit", command, 10);
        Assert.Contains("trusted_hash = \"" + trusted + "\"", configText);
        Assert.True(HookInstaller.CodexStatus(config, hooks, binary).Connected);
        var stale = configText.Replace(trusted, "sha256:0000000000000000000000000000000000000000000000000000000000000000", StringComparison.Ordinal);
        var key = FileBits.ResolveSymlinks(hooks) + ":user_prompt_submit:0:0";
        Assert.NotEqual(stale, CodexHookTrust.ConfigByTrusting(stale, [(key, trusted)]));
        var settings = Path.Combine(directory, "settings.json");
        File.WriteAllText(settings, "{\"theme\":\"dark\",\"hooks\":{}}");
        var claude = HookInstaller.InstallClaude(settings, binary, true);
        Assert.True(claude.Connected);
        Assert.Equal("dark", JsonFile.Read(settings)["theme"]);
        Assert.False(HookInstaller.RemoveNested(hooks, binary).Connected);
        const string ambiguous = "features.hooks = false\n\n[features]\njs_repl = false\n";
        Assert.Null(CodexFeatureFlag.EnablingHooks(ambiguous));
        Assert.True(CodexFeatureFlag.IsEnabled("features.hooks = true\n"));
        Assert.False(CodexFeatureFlag.IsEnabled(ambiguous));
        const string sample = "/tmp/MochiHookCLI --source codex";
        Assert.Equal("sha256:e0a1444cb415753d008f86138a8ae766b73742b68bc011351c0945341092eca4", CodexHookTrust.IdentityHash("permission_request", sample, 300));
        Assert.Equal("sha256:ec9f0f6f204d083b20d1a5e387cf986cd849ae75dea4124e209d86141ec35f0c", CodexHookTrust.IdentityHash("session_end", sample, 10));
        Assert.Equal(3, CodexHookTrust.NormalizedTimeout("session_end", 10));
        Assert.Equal(10, CodexHookTrust.NormalizedTimeout("user_prompt_submit", 10));
    }

    [Fact]
    public void HookRetargetPointsAtTheOpenApp()
    {
        var directory = Temp();
        var current = Executable(directory);
        var hooks = Path.Combine(directory, "hooks.json");
        const string stale = "/Users/albertsmacbookpro/Projects/PythonTemplate/island/MochiIsland.app/Contents/MacOS/MochiHookCLI";
        JsonFile.Write(new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["hooks"] = new Dictionary<string, object?>
            {
                ["beforeSubmitPrompt"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["command"] = stale, [AppConfig.HookMarker] = AppConfig.HookVersion }
                },
                ["stop"] = new List<object?> { new Dictionary<string, object?> { ["command"] = "/bin/echo keep" } }
            }
        }, hooks);
        Assert.True(HookInstaller.RetargetCursor(hooks, current));
        var saved = Bags.Object(JsonFile.Read(hooks)["hooks"]);
        Assert.Equal(current, Bags.Object(Bags.Array(saved["beforeSubmitPrompt"])[0])["command"]);
        Assert.Equal("/bin/echo keep", Bags.Object(Bags.Array(saved["stop"])[0])["command"]);
        var outcome = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeSubmitPrompt"),
            ("conversation_id", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ("prompt", "do not keep this")))!;
        Assert.Equal("running", outcome.Status);
        Assert.Equal("cursor", outcome.Source);
        var tasks = new Dictionary<string, object?>();
        HookEvents.Merge(outcome, tasks, 5);
        Assert.False(Bags.Object(tasks[outcome.Id]).ContainsKey("prompt"));
        Assert.Equal("running", Bags.Object(tasks[outcome.Id])["status"]);
    }

    [Fact]
    public void PassiveHooksReportTheShortlist()
    {
        var home = Path.Combine("Users", "example");
        if (OperatingSystem.IsWindows()) home = Path.Combine("C:", home);
        else home = "/" + home;
        var copilotHome = PassiveHooks.CopilotDirectory(home, new Dictionary<string, string>());
        Assert.Equal("hooks", Path.GetFileName(copilotHome));
        Assert.Equal(".copilot", Path.GetFileName(Path.GetDirectoryName(copilotHome)));
        Assert.DoesNotContain(".github", copilotHome);
        var customRoot = OperatingSystem.IsWindows() ? Path.Combine("C:", "var", "copilot-home") : "/var/copilot-home";
        var custom = PassiveHooks.CopilotDirectory(home, new Dictionary<string, string> { ["COPILOT_HOME"] = customRoot });
        Assert.Equal(Path.Combine(customRoot, "hooks"), custom);
        var blank = PassiveHooks.CopilotDirectory(home, new Dictionary<string, string> { ["COPILOT_HOME"] = "  " });
        Assert.Equal(".copilot", Path.GetFileName(Path.GetDirectoryName(blank)));
        Assert.Equal(Path.Combine(home, ".grok", "hooks"), PassiveHooks.GrokDirectory(home));
        Assert.Equal(Path.Combine(home, ".config", "opencode", "plugins", "mochi-island.js"), PassiveHooks.OpenCodePluginURL(home));
        Assert.Equal(["cursor", "codex", "claude", "gemini", "grok-bot", "gpt"], IslandPrefs.PickerOrder);
        Assert.DoesNotContain("copilot", IslandPrefs.PickerOrder);
        Assert.DoesNotContain(PassiveHooks.GrokEvents, name => name == "SubagentStop");

        foreach (var name in new[] { "sessionStart", "userPromptSubmitted", "preToolUse", "postToolUse", "subagentStart", "postToolUseFailure" })
        {
            var item = HookEvents.Interpret("copilot", Bag(
                ("hook_event_name", name), ("sessionId", "copilot-1"), ("prompt", "SECRET"), ("toolArgs", "SECRET"), ("initialPrompt", "SECRET")))!;
            Assert.Equal("running", item.Status);
            Assert.Equal("copilot", item.Source);
            Assert.False(item.WaitsForPermission);
            var tasks = new Dictionary<string, object?>();
            HookEvents.Merge(item, tasks, 8);
            Assert.True(Clean(Bags.Object(tasks[item.Id])));
        }
        var permission = HookEvents.Interpret("copilot", Bag(("sessionId", "copilot-1"), ("prompt", "SECRET")), eventFlag: "permissionRequest")!;
        Assert.Equal("wait", permission.Status);
        Assert.False(permission.WaitsForPermission);
        Assert.Empty(permission.Options);
        Assert.Null(HookEvents.Interpret("copilot", Bag(("hook_event_name", "notification"), ("notification_type", "shell_completion"), ("sessionId", "copilot-1"))));
        Assert.Equal("running", HookEvents.Interpret("copilot", new Dictionary<string, object?>
        {
            ["hook_event_name"] = "errorOccurred",
            ["errorContext"] = "tool_execution",
            ["sessionId"] = "copilot-1",
            ["error"] = new Dictionary<string, object?> { ["message"] = "SECRET" }
        })!.Status);
        Assert.Equal("fail", HookEvents.Interpret("copilot", Bag(("hook_event_name", "ErrorOccurred"), ("error_context", "model_call"), ("sessionId", "copilot-1")))!.Status);
        Assert.Equal("done", HookEvents.Interpret("copilot", Bag(("hook_event_name", "agentStop"), ("sessionId", "copilot-1")))!.Status);
        var copilotTasks = new Dictionary<string, object?> { ["copilot-1"] = new Dictionary<string, object?> { ["id"] = "copilot-1", ["status"] = "running", ["source"] = "copilot" } };
        var end = HookEvents.Interpret("copilot", Bag(("hook_event_name", "sessionEnd"), ("sessionId", "copilot-1"), ("error", "SECRET")))!;
        Assert.Equal("closed", end.Status);
        HookEvents.Merge(end, copilotTasks, 9);
        Assert.False(copilotTasks.ContainsKey("copilot-1"));

        Assert.Equal("running", HookEvents.Interpret("opencode", new Dictionary<string, object?>
        {
            ["hook_event_name"] = "session.status",
            ["sessionID"] = "oc-1",
            ["status"] = new Dictionary<string, object?> { ["type"] = "retry", ["message"] = "SECRET retry" }
        })!.Status);
        Assert.Equal("done", HookEvents.Interpret("opencode", Bag(("hook_event_name", "session.status"), ("session_id", "oc-1"), ("status_type", "idle")))!.Status);
        Assert.Equal("wait", HookEvents.Interpret("opencode", Bag(("hook_event_name", "question.asked"), ("session_id", "oc-1"), ("question", "SECRET question")))!.Status);
        Assert.Equal("fail", HookEvents.Interpret("opencode", Bag(("hook_event_name", "session.error"), ("session_id", "oc-1")))!.Status);
        Assert.Null(HookEvents.Interpret("opencode", Bag(("hook_event_name", "message.updated"), ("session_id", "oc-1"), ("text", "SECRET"))));

        foreach (var name in new[] { "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "SubagentStart", "PermissionDenied", "PostToolUseFailure" })
        {
            var item = HookEvents.Interpret("grok", Bag(("hook_event_name", name), ("sessionId", "grok-1"), ("prompt", "SECRET")))!;
            Assert.Equal("grok-bot", item.Source);
            Assert.Equal("running", item.Status);
        }
        var turn = HookEvents.Interpret("cursor", Bag(("hook_event_name", "Stop"), ("cursor_version", "2.4.0"), ("sessionId", "grok-1")), new Dictionary<string, string> { ["GROK_SESSION_ID"] = "sess" })!;
        Assert.Equal("grok-bot", turn.Source);
        Assert.Equal("done", turn.Status);
        Assert.Equal("fail", HookEvents.Interpret("grok", Bag(("hook_event_name", "StopFailure"), ("sessionId", "grok-1"), ("error", "rate_limit")))!.Status);
        var imported = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeSubmitPrompt"),
            ("cursor_version", "2.4.0"),
            ("conversation_id", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ("prompt", "SECRET prompt")), new Dictionary<string, string> { ["GROK_HOOK_EVENT"] = "user_prompt_submit" })!;
        Assert.Equal("grok-bot", imported.Source);
        Assert.Equal("running", imported.Status);
        var heartbeat = HookEvents.Interpret(null, Bag(("hook_event_name", "stop"), ("cursor_version", "1"), ("sessionId", "grok-1")), new Dictionary<string, string> { ["GROK_SESSION_ID"] = "sess" })!;
        Assert.Equal("heartbeat", heartbeat.Status);
        var grokTasks = new Dictionary<string, object?>();
        var failed = HookEvents.Interpret("grok", Bag(("hook_event_name", "StopFailure"), ("sessionId", "grok-1")))!;
        HookEvents.Merge(failed, grokTasks, 1);
        HookEvents.Merge(heartbeat, grokTasks, 2);
        Assert.Equal("fail", Bags.Object(grokTasks["grok-1"])["status"]);
        Assert.Null(HookEvents.Interpret("grok", Bag(("hook_event_name", "Notification"), ("notificationType", "idle_prompt"), ("sessionId", "grok-1"))));
        var plain = HookEvents.Interpret(null, Bag(("hook_event_name", "stop"), ("cursor_version", "1")), new Dictionary<string, string> { ["GROK_SESSION_ID"] = "   ", ["GROK_HOOK_EVENT"] = "" })!;
        Assert.Equal("cursor", plain.Source);
        Assert.Equal("done", plain.Status);
        Assert.Equal(["claude", "copilot", "opencode"], AgentIconList.Rows([
            new AgentIconCandidate("c", "copilot", "Copilot", "running", false, 1),
            new AgentIconCandidate("o", "opencode", "OpenCode", "wait", false, 2),
            new AgentIconCandidate("k", "claude", "Claude", "running", false, 3)
        ]).Select(row => row.Source));
        Assert.Equal("https://github.com/features/copilot", AgentLaunch.Plan("github copilot", fileExists: _ => false).Url);
        Assert.Equal("https://opencode.ai", AgentLaunch.Plan("open-code", fileExists: _ => false).Url);

        var directory = Temp();
        var binary = Executable(directory);
        var blockedDir = Path.Combine(directory, "blocked");
        Directory.CreateDirectory(blockedDir);
        var blocked = PassiveHooks.InstallCopilot(blockedDir, binary, false);
        Assert.True(blocked.NeedsPrivacy);
        Assert.False(File.Exists(Path.Combine(blockedDir, "mochi-island.json")));
        var missing = PassiveHooks.InstallGrok(blockedDir, Path.Combine(directory, "missing"), true);
        Assert.Equal("hook program missing", missing.Error);
        var copilotDir = Path.Combine(directory, "copilot-hooks");
        Directory.CreateDirectory(copilotDir);
        var copilotFile = Path.Combine(copilotDir, "mochi-island.json");
        File.WriteAllText(copilotFile, "keep-user");
        Assert.True(PassiveHooks.InstallCopilot(copilotDir, binary, true).Connected);
        Assert.Equal("keep-user", File.ReadAllText(copilotFile + ".mochi-island.bak"));
        var copilotJSON = JsonFile.Read(copilotFile);
        Assert.Equal(1, Bags.Whole(copilotJSON["version"]));
        var copilotHooks = Bags.Object(copilotJSON["hooks"]);
        Assert.Equal(PassiveHooks.CopilotEvents.OrderBy(name => name), copilotHooks.Keys.OrderBy(name => name));
        foreach (var name in PassiveHooks.CopilotEvents)
        {
            var entry = Bags.Object(Bags.Array(copilotHooks[name])[0]);
            var command = Bags.Text(entry["command"]);
            Assert.Equal(command, Bags.Text(entry["bash"]));
            Assert.Contains(binary, command);
            Assert.Contains("--source copilot", command);
            Assert.Contains("--event " + name, command);
            Assert.Equal(10, Bags.Whole(entry["timeoutSec"]));
            Assert.Equal(AppConfig.HookVersion, Bags.Whole(entry[AppConfig.HookMarker]));
        }
        var other = Executable(directory, "OtherCLI");
        Assert.False(PassiveHooks.CopilotStatus(copilotDir, other).Connected);
        var grokDir = Path.Combine(directory, "grok-hooks");
        Assert.True(PassiveHooks.InstallGrok(grokDir, binary, true).Connected);
        var grokFile = Path.Combine(grokDir, "mochi-island.json");
        var grokJSON = JsonFile.Read(grokFile);
        Assert.False(grokJSON.ContainsKey("version"));
        var grokHooks = Bags.Object(grokJSON["hooks"]);
        Assert.False(grokHooks.ContainsKey("SubagentStop"));
        Assert.Equal(PassiveHooks.GrokEvents.OrderBy(name => name), grokHooks.Keys.OrderBy(name => name));
        Assert.Equal(["mochi-island.json"], Directory.GetFileSystemEntries(grokDir).Select(Path.GetFileName).OrderBy(name => name));
        var moved = Executable(directory, "next-hook");
        Assert.True(HookInstaller.RetargetNested(grokFile, moved, "grok"));
        Assert.True(PassiveHooks.GrokStatus(grokDir, moved).Connected);
        Assert.False(PassiveHooks.GrokStatus(grokDir, binary).Connected);
        var plugin = Path.Combine(directory, "plugins", "mochi-island.js");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
        File.WriteAllText(plugin, "old-plugin");
        Assert.True(PassiveHooks.InstallOpenCode(plugin, binary, true).Connected);
        Assert.Equal("old-plugin", File.ReadAllText(plugin + ".mochi-island.bak"));
        var script = File.ReadAllText(plugin);
        foreach (var name in new[] { "session.status", "tool.execute.before", "permission.asked", "question.asked", "session.error" })
        {
            Assert.Contains(name, script);
        }
        Assert.Contains("spawnSync", script);
        Assert.DoesNotContain("message.updated", script);
        Assert.DoesNotContain("http://", script);
        Assert.DoesNotContain("https://", script);
        Assert.False(PassiveHooks.Remove(plugin, binary).Connected);
        Assert.False(File.Exists(plugin));
    }

    [Fact]
    public void GeminiHookMapsTheShortlist()
    {
        foreach (var name in new[] { "SessionStart", "BeforeAgent", "BeforeModel", "AfterModel", "BeforeToolSelection", "BeforeTool", "AfterTool" })
        {
            var item = HookEvents.Interpret("gemini", Bag(
                ("hook_event_name", name), ("session_id", "gem-1"), ("prompt", "SECRET"), ("transcript_path", "/tmp/SECRET/transcript.json")))!;
            Assert.Equal("gemini", item.Source);
            Assert.Equal("running", item.Status);
            Assert.Equal("gem-1", item.Id);
            var tasks = new Dictionary<string, object?>();
            HookEvents.Merge(item, tasks, 3);
            var record = Bags.Object(tasks["gem-1"]);
            Assert.False(record.ContainsKey("prompt"));
            Assert.Equal("running", record["status"]);
            Assert.DoesNotContain("SECRET", string.Join(" ", record.Values.OfType<string>()));
        }
        var pathOnly = HookEvents.Interpret("gemini", Bag(("hook_event_name", "SessionStart"), ("transcript_path", "/tmp/SECRET/transcript.json"), ("prompt", "SECRET prompt")))!;
        Assert.Equal("gemini-session", pathOnly.Id);
        var permission = HookEvents.Interpret("gemini", Bag(("hook_event_name", "Notification"), ("notification_type", "ToolPermission"), ("session_id", "gem-1")))!;
        Assert.Equal("wait", permission.Status);
        Assert.False(permission.WaitsForPermission);
        Assert.Empty(permission.Options);
        Assert.Null(HookEvents.Interpret("gemini", Bag(("hook_event_name", "Notification"), ("type", "Info"), ("session_id", "gem-1"))));
        var tasksLeft = new Dictionary<string, object?>();
        var finished = HookEvents.Interpret("gemini", Bag(("hook_event_name", "AfterAgent"), ("session_id", "gem-1"), ("error", "model failed")))!;
        Assert.Equal("done", finished.Status);
        HookEvents.Merge(finished, tasksLeft, 4);
        Assert.False(Bags.Object(tasksLeft["gem-1"]).ContainsKey("error"));
        var ended = HookEvents.Interpret("gemini", Bag(("hook_event_name", "SessionEnd"), ("session_id", "gem-1")))!;
        HookEvents.Merge(ended, tasksLeft, 5);
        Assert.False(tasksLeft.ContainsKey("gem-1"));
        Assert.DoesNotContain(HookInstaller.GeminiEvents, name => name.Contains("fail", StringComparison.OrdinalIgnoreCase));
        Assert.Null(HookEvents.Interpret("gemini", Bag(("hook_event_name", "StopFailure"), ("session_id", "gem-1"))));
        Assert.Equal("https://geminicli.com", AgentLaunch.Plan("gemini cli", fileExists: _ => false).Url);
        Assert.Equal("agent-gemini", SlotFace.AgentMark("gemini"));

        var directory = Temp();
        var binary = Executable(directory);
        var settings = Path.Combine(directory, "settings.json");
        Assert.True(HookInstaller.InstallGemini(settings, binary, false).NeedsPrivacy);
        Assert.False(File.Exists(settings));
        Assert.Equal("hook program missing", HookInstaller.InstallGemini(settings, Path.Combine(directory, "missing"), true).Error);
        JsonFile.Write(new Dictionary<string, object?>
        {
            ["theme"] = "dark",
            ["hooks"] = new Dictionary<string, object?>
            {
                ["BeforeTool"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["hooks"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "command", ["command"] = "/bin/echo keep" } }
                    }
                }
            }
        }, settings);
        Assert.True(HookInstaller.InstallGemini(settings, binary, true).Connected);
        var backup = File.ReadAllText(settings + ".mochi-island.bak");
        Assert.Contains("keep", backup);
        Assert.Contains("dark", backup);
        var saved = JsonFile.Read(settings);
        Assert.Equal("dark", saved["theme"]);
        var hooks = Bags.Object(saved["hooks"]);
        Assert.Equal(HookInstaller.GeminiEvents.OrderBy(name => name), hooks.Keys.OrderBy(name => name));
        foreach (var name in HookInstaller.GeminiEvents)
        {
            var commands = Bags.Array(hooks[name]).Select(Bags.Object).SelectMany(group => Bags.Array(group["hooks"]).Select(Bags.Object));
            Assert.Contains(commands, entry =>
                Bags.Text(entry["command"]).Contains(binary)
                && Bags.Text(entry["command"]).Contains("--source gemini")
                && Bags.Whole(entry[AppConfig.HookMarker]) == AppConfig.HookVersion
                && Bags.Whole(entry["timeout"]) == 10000);
        }
        var before = Bags.Array(hooks["BeforeTool"]).Select(Bags.Object).SelectMany(group => Bags.Array(group["hooks"]).Select(Bags.Object));
        Assert.Contains(before, entry => Bags.Text(entry["command"]) == "/bin/echo keep");
        var other = Executable(directory, "OtherCLI");
        Assert.False(HookInstaller.GeminiStatus(settings, other).Connected);
        Assert.DoesNotContain(Directory.GetFileSystemEntries(directory).Select(Path.GetFileName)!, name => name!.Contains(".tmp"));
        HookInstaller.RemoveNested(settings, binary);
        Assert.False(HookInstaller.GeminiStatus(settings, binary).Connected);
        var removed = JsonFile.Read(settings);
        Assert.Equal("dark", removed["theme"]);
        var kept = Bags.Array(Bags.Object(removed["hooks"])["BeforeTool"]).Select(Bags.Object).SelectMany(group => Bags.Array(group["hooks"]).Select(Bags.Object));
        Assert.Contains(kept, entry => Bags.Text(entry["command"]) == "/bin/echo keep");
        Assert.DoesNotContain(kept, entry => entry.ContainsKey(AppConfig.HookMarker));
    }

    [Fact]
    public void EmptyFileHasNoSeeds()
    {
        var disk = new DialogueSlotDisk(Temp());
        var store = disk.Load(10, new HashSet<string> { "live-1" });
        Assert.Empty(store.Slots);
        Assert.Equal("", store.Selected);
        Assert.False(File.Exists(disk.FilePath));
        Assert.Equal("kitten", SlotFace.Pack("mochi"));
        Assert.Equal("kitten-wake", SlotFace.ResourceName("小猫", ""));
        Assert.Equal("kitten-wait", SlotFace.ResourceName("mochi", "wait"));
        Assert.Equal("小猫", SlotFace.DisplayName("mochi"));
        Assert.Equal("小猫", SlotFace.Label("New chat", "mochi"));
    }

    [Fact]
    public void MergeKeepsSkinAndLeavesSlotsWhenTasksLeave()
    {
        var kept = new DialogueSlotStore("keep", [new DialogueSlot("keep", "cursor", "Stay", "cursor", 1)]);
        Assert.Contains(kept.Merged([]).Slots, slot => slot.Id == "keep");
        var store = new DialogueSlotStore("abc", [new DialogueSlot("abc", "cursor", "Keep", "blob-3", 1)]);
        var merged = store.Merged([new CursorTask("abc", "From bus", "running", source: "codex", updated: 5)]);
        Assert.Single(merged.Slots);
        Assert.Equal("codex", merged.Slots[0].Skin);
        Assert.Equal("codex", merged.Slots[0].Source);
        Assert.Equal("From bus", merged.Slots[0].Title);
    }

    [Fact]
    public void CreateSlotAppendsAFaceWithoutATaskFile()
    {
        var store = new DialogueSlotStore("one", [new DialogueSlot("one", "cursor", "One", "cursor", 1)]);
        var (next, slot) = store.Created("Grok Bot", 20);
        Assert.Equal(store.Slots.Count + 1, next.Slots.Count);
        Assert.Equal(slot.Id, next.Selected);
        Assert.Equal("grok-bot", slot.Source);
        Assert.Equal("grok-bot", slot.Skin);
        Assert.Equal("New chat", slot.Title);
        Assert.Contains(slot.Skin, DialogueSlotStore.Skins);
        var cat = store.Created("小猫", 21).Slot;
        Assert.Equal("mochi", cat.Source);
        Assert.Equal("mochi", cat.Skin);
    }

    [Fact]
    public void OldSeedMigrationKeepsActiveTasks()
    {
        var directory = Temp();
        var file = Path.Combine(directory, "dialogue-slots.json");
        File.WriteAllText(file, """
        {"selected":"seed-cursor","slots":[
          {"id":"seed-cursor","source":"cursor","title":"New chat","skin":"cursor","created":1},
          {"id":"seed-mochi","source":"mochi","title":"小猫","skin":"mochi","created":1},
          {"id":"seed-codex","source":"codex","title":"New chat","skin":"codex","created":1},
          {"id":"live-1","source":"cursor","title":"Keep me","skin":"cursor","created":2},
          {"id":"cat-1","source":"mochi","title":"小猫","skin":"mochi","created":3}
        ]}
        """);
        var disk = new DialogueSlotDisk(directory);
        var store = disk.Load(activeTaskIDs: new HashSet<string> { "live-1", "cat-1", "seed-codex" });
        Assert.Equal(new HashSet<string> { "live-1", "cat-1", "seed-codex" }, store.Slots.Select(slot => slot.Id).ToHashSet());
        Assert.False(store.Slots.Single(slot => slot.Id == "live-1").Read);
        Assert.Equal("mochi", store.Slots.Single(slot => slot.Id == "cat-1").Source);
        var idle = disk.Load(activeTaskIDs: new HashSet<string>());
        Assert.Equal(["live-1"], idle.Slots.Select(slot => slot.Id));
    }

    [Fact]
    public void RemoveWhenReadAndDone()
    {
        var store = new DialogueSlotStore("a",
        [
            new DialogueSlot("a", "cursor", "Done", "cursor", 1, true, "done"),
            new DialogueSlot("b", "cursor", "Wait", "cursor", 1, true, "wait"),
            new DialogueSlot("c", "cursor", "Unread", "cursor", 1, false, "done"),
            new DialogueSlot("d", "gpt", "Run", "gpt", 1, true, "running"),
            new DialogueSlot("e", "codex", "Fail", "codex", 1, true, "fail")
        ]);
        var live = store.RemovingReadDone([
            new CursorTask("a", "Done", "done"),
            new CursorTask("b", "Wait", "wait"),
            new CursorTask("c", "Unread", "done"),
            new CursorTask("d", "Run", "running", source: "gpt"),
            new CursorTask("e", "Fail", "fail", source: "codex")
        ]);
        Assert.Equal(new HashSet<string> { "a", "b", "c", "d", "e" }, live.Slots.Select(slot => slot.Id).ToHashSet());
        var aged = store.RemovingReadDone([
            new CursorTask("b", "Wait", "wait"),
            new CursorTask("c", "Unread", "done"),
            new CursorTask("d", "Run", "running", source: "gpt")
        ]);
        Assert.Equal(new HashSet<string> { "b", "c", "d" }, aged.Slots.Select(slot => slot.Id).ToHashSet());
        Assert.True(aged.MarkingRead("c").Slots.Single(slot => slot.Id == "c").Read);
    }

    [Fact]
    public void SelectingKeepsOnlyKnownIds()
    {
        var store = new DialogueSlotStore("a",
        [
            new DialogueSlot("a", "cursor", "A", "cursor", 1),
            new DialogueSlot("b", "codex", "B", "codex", 1)
        ]);
        var next = store.Selecting(store.Slots[1].Id);
        Assert.Equal(store.Slots[1].Id, next.Selected);
        Assert.Equal(store.Slots.Select(slot => slot.Id), next.Slots.Select(slot => slot.Id));
        Assert.Equal(store.Selected, store.Selecting("missing").Selected);
    }

    [Fact]
    public void NewWaitSelectsOnlyAfterTheFirstSnapshot()
    {
        var store = new DialogueSlotStore("seeded", [new DialogueSlot("seeded", "cursor", "Old", "cursor", 1)]);
        var known = new HashSet<string>();
        var primed = false;
        var first = store.SelectingNewWaits([new CursorTask("w", "Waiting", "wait", updated: 9)], ref known, ref primed);
        Assert.True(primed);
        Assert.Equal("seeded", first.Selected);
        Assert.Contains(first.Slots, slot => slot.Id == "w");
        var second = first.SelectingNewWaits([
            new CursorTask("w", "Waiting", "wait", updated: 9),
            new CursorTask("w2", "Newer", "wait", source: "codex", updated: 12)
        ], ref known, ref primed);
        Assert.Equal("w2", second.Selected);
        Assert.Equal("codex", second.Slots.Single(slot => slot.Id == "w2").Source);
    }

    [Fact]
    public void KittenV1FaceFilesFollowSource()
    {
        Assert.Equal("cursor", SlotFace.Pack("cursor"));
        Assert.Equal("grok", SlotFace.Pack("grok-bot"));
        Assert.Equal("kitten", SlotFace.Pack("小猫"));
        Assert.Equal("kitten-work", SlotFace.ResourceName("mochi", "running"));
        Assert.Equal("kitten-done", SlotFace.ResourceName("mochi", "done"));
        Assert.Equal("Fix tabs", SlotFace.Label("Fix tabs", "cursor"));
        Assert.Equal("Grok Bot", SlotFace.DisplayName("grok-bot"));
    }
}
