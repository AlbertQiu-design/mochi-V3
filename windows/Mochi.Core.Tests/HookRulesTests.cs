namespace Mochi.Core.Tests;

public class HookRulesTests
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

    private static List<Dictionary<string, object?>> Rows(object? value)
    {
        return Bags.Array(value).Select(Bags.Object).Where(row => row.Count > 0).ToList();
    }

    [Fact]
    public void HookInstallBacksUpAndMarksEntries()
    {
        var directory = Temp();
        var binary = Executable(directory);
        var hooks = Path.Combine(directory, "hooks.json");
        File.WriteAllText(hooks, "{\"version\":1,\"hooks\":{\"sessionStart\":[{\"command\":\"/bin/echo keep\"}],\"stop\":[{\"command\":\"/tmp/vibe-island-bridge --source cursor\"}]}}");
        var tasks = Path.Combine(directory, "tasks.json");
        var blocked = HookInstaller.Install(hooks, binary, tasks, false);
        Assert.True(blocked.NeedsPrivacy);
        var installed = HookInstaller.Install(hooks, binary, tasks, true);
        Assert.True(installed.Connected);
        Assert.True(File.Exists(hooks + ".mochi-island.bak"));
        var table = Bags.Object(JsonFile.Read(hooks)["hooks"]);
        var entries = Rows(table["sessionStart"]);
        Assert.Equal(2, entries.Count);
        Assert.Equal(AppConfig.HookVersion, Bags.Whole(entries[0][AppConfig.HookMarker]));
        Assert.Equal("/bin/echo keep", entries[1]["command"]);
        var stop = Rows(table["stop"]);
        Assert.Equal(2, stop.Count);
        Assert.Equal(AppConfig.HookVersion, Bags.Whole(stop[0][AppConfig.HookMarker]));
        Assert.Contains("vibe-island-bridge", Bags.Text(stop[1]["command"]));
        foreach (var name in new[] { "afterAgentResponse", "afterFileEdit" })
        {
            Assert.Equal(AppConfig.HookVersion, Bags.Whole(Rows(table[name])[0][AppConfig.HookMarker]));
        }
        Assert.Equal(300, Bags.Whole(Rows(table["beforeShellExecution"])[0]["timeout"]));
        var removed = HookInstaller.Remove(hooks, binary, tasks);
        Assert.False(removed.Connected);
        var after = Bags.Object(JsonFile.Read(hooks)["hooks"]);
        var left = Rows(after["sessionStart"]);
        Assert.Single(left);
        Assert.Equal("/bin/echo keep", left[0]["command"]);
        var leftStop = Rows(after["stop"]);
        Assert.Single(leftStop);
        Assert.Contains("vibe-island-bridge", Bags.Text(leftStop[0]["command"]));
    }

    [Fact]
    public void ShellCommandQuotesPathsWithSpaces()
    {
        var command = HookInstaller.ShellCommand(Path.Combine("C:", "Users", "A B", "MochiHookCLI.exe"), "codex");
        Assert.StartsWith("\"", command);
        Assert.Contains("--source codex", command);
    }

    [Fact]
    public void ReplyWriterOnlyAllowsPermissionActions()
    {
        var url = Path.Combine(Temp(), "reply.json");
        ReplyWriter.WritePermission("t", "r", "allow", url);
        var saved = JsonFile.Read(url);
        Assert.Equal("allow", saved["action"]);
        Assert.Equal("permission", saved["kind"]);
        Assert.Throws<ReplyWriter.ReplyError>(() => ReplyWriter.WritePermission("t", "r", "approve", url));
        var choice = Path.Combine(Temp(), "choice.json");
        ReplyWriter.WriteChoice("t2", "r2", "approve", "plan", choice);
        var body = JsonFile.Read(choice);
        Assert.Equal("approve", body["action"]);
        Assert.Equal("plan", body["kind"]);
        Assert.Equal("approve", body["option_id"]);
        Assert.True(ReplyWriter.ClosesBubble("permission", "allow"));
        Assert.True(ReplyWriter.ClosesBubble("permission", "deny"));
        Assert.True(ReplyWriter.ClosesBubble("", "Allow"));
        Assert.True(ReplyWriter.ClosesBubble("mode", "deny"));
        Assert.False(ReplyWriter.ClosesBubble("plan", "approve"));
    }

    [Fact]
    public void TaskReaderKeepsSource()
    {
        var url = Path.Combine(Temp(), "tasks.json");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        JsonFile.Write(new Dictionary<string, object?>
        {
            ["tasks"] = new Dictionary<string, object?>
            {
                ["c"] = new Dictionary<string, object?> { ["id"] = "c", ["title"] = "Cursor", ["status"] = "running", ["updated"] = now, ["source"] = "cursor" },
                ["x"] = new Dictionary<string, object?> { ["id"] = "x", ["title"] = "Codex", ["status"] = "running", ["updated"] = now, ["source"] = "codex" },
                ["g"] = new Dictionary<string, object?> { ["id"] = "g", ["title"] = "Grok", ["status"] = "running", ["updated"] = now, ["source"] = "grok-bot" },
                ["m"] = new Dictionary<string, object?> { ["id"] = "m", ["title"] = "Cat", ["status"] = "running", ["updated"] = now, ["source"] = "cat" },
                ["z"] = new Dictionary<string, object?> { ["id"] = "z", ["title"] = "Other", ["status"] = "running", ["updated"] = now, ["source"] = "nope" },
                ["p"] = new Dictionary<string, object?> { ["id"] = "p", ["title"] = "GPT", ["status"] = "running", ["updated"] = now, ["source"] = "chatgpt" }
            }
        }, url);
        var tasks = TaskReader.Load(url, now);
        var byID = tasks.ToDictionary(task => task.Id, task => task.Source);
        Assert.Equal("cursor", byID["c"]);
        Assert.Equal("codex", byID["x"]);
        Assert.Equal("grok-bot", byID["g"]);
        Assert.Equal("mochi", byID["m"]);
        Assert.Equal("cursor", byID["z"]);
        Assert.Equal("gpt", byID["p"]);
        Assert.Equal("codex", tasks.Single(task => task.Id == "x").Dictionary()["source"]);
    }

    [Fact]
    public void AbandonedApprovalDoesNotStay()
    {
        var url = Path.Combine(Temp(), "tasks.json");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        const string old = "2026-10-22T05:59:43Z";
        JsonFile.Write(new Dictionary<string, object?>
        {
            ["tasks"] = new Dictionary<string, object?>
            {
                ["pm"] = new Dictionary<string, object?>
                {
                    ["id"] = "pm", ["status"] = "pending", ["kind"] = "question", ["source"] = "cursor",
                    ["title"] = "Cursor", ["updated"] = old, ["summary"] = "请点 Allow 或 Deny 验证审批热区",
                    ["options"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["id"] = "allow", ["label"] = "Allow" },
                        new Dictionary<string, object?> { ["id"] = "deny", ["label"] = "Deny" }
                    }
                },
                ["live"] = new Dictionary<string, object?>
                {
                    ["id"] = "live", ["status"] = "wait", ["kind"] = "permission", ["source"] = "cursor",
                    ["title"] = "Shell", ["updated"] = now,
                    ["options"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["id"] = "allow", ["label"] = "Allow" },
                        new Dictionary<string, object?> { ["id"] = "deny", ["label"] = "Deny" }
                    }
                }
            }
        }, url);
        var tasks = TaskReader.Load(url, now);
        Assert.DoesNotContain(tasks, task => task.Id == "pm");
        Assert.Contains(tasks, task => task.Id == "live");
        var parsed = TaskReader.Timestamp(old);
        Assert.True(parsed > 1_700_000_000);
        Assert.True(now - parsed > AppConfig.IconMaxAge);
        Assert.False(AgentIconList.ShouldShow(new AgentIconCandidate("stale-run", "cursor", "Cursor", "running", false, parsed, true), now: now));
        Assert.True(AgentIconList.ShouldShow(new AgentIconCandidate("live", "cursor", "Shell", "wait", false, now, true), now: now));
    }

    [Fact]
    public void AgentLaunchFindsInstalledAppsAndCodexFallback()
    {
        var local = Path.Combine("C:", "Users", "me", "AppData", "Local");
        var cursor = Path.Combine(local, "Programs", "cursor", "Cursor.exe");
        var found = AgentLaunch.Plan("Cursor", home: Path.Combine("C:", "Users", "me"), localAppData: local, fileExists: path => path == cursor);
        Assert.Equal(cursor, found.AppPath);
        Assert.Null(found.Url);
        var missing = AgentLaunch.Plan("codex", fileExists: _ => false);
        Assert.Null(missing.AppPath);
        Assert.Equal("https://chatgpt.com/codex", missing.Url);
        Assert.Contains("Codex", missing.Note);
        var grokPath = Path.Combine(local, "Programs", "Grok", "Grok.exe");
        var grok = AgentLaunch.Plan("grok bot", localAppData: local, fileExists: path => path == grokPath);
        Assert.Equal(grokPath, grok.AppPath);
        Assert.Equal("mochi", AgentLaunch.Normalize("cat"));
        Assert.Equal("mochi", AgentLaunch.Normalize("小猫"));
        var cat = AgentLaunch.Plan("mochi", fileExists: _ => true);
        Assert.Null(cat.AppPath);
        Assert.Null(cat.Url);
        Assert.Contains("小猫", cat.Note);
        Assert.Contains(".cursor", AppConfig.CursorDirectory);
        Assert.Contains("MochiIsland", AppConfig.SupportDirectory);
        Assert.Equal("https://cursor.com", AgentLaunch.Plan("cursor", fileExists: _ => false).Url);
        Assert.Equal("gpt", AgentLaunch.Normalize("ChatGPT"));
        var gptPath = Path.Combine(local, "Programs", "ChatGPT", "ChatGPT.exe");
        Assert.Equal(gptPath, AgentLaunch.Plan("gpt", localAppData: local, fileExists: path => path == gptPath).AppPath);
        var gptMissing = AgentLaunch.Plan("chatgpt", fileExists: _ => false);
        Assert.Equal("https://chatgpt.com", gptMissing.Url);
        Assert.Contains("ChatGPT", gptMissing.Note);
        var created = new DialogueSlotStore().Created("openai", 3).Slot;
        Assert.Equal("gpt", created.Source);
        Assert.Equal("gpt", created.Skin);
        Assert.False(created.Read);
        Assert.Equal("GPT", SlotFace.DisplayName("gpt"));
        Assert.Equal("agent-gpt", SlotFace.AgentMark("gpt"));
        Assert.Equal("agent-cursor", SlotFace.AgentMark("cursor"));
        Assert.Equal("agent-codex", SlotFace.AgentMark("Codex"));
        Assert.Equal("agent-grok", SlotFace.AgentMark("grok"));
        Assert.Equal("Claude", SlotFace.DisplayName("claude"));
    }

    [Fact]
    public void AgentIconsStayOnePerConversation()
    {
        var rows = AgentIconList.Rows([
            new AgentIconCandidate("c-run", "cursor", "Run", "running", false, 1),
            new AgentIconCandidate("c-done", "cursor", "Done", "done", false, 5),
            new AgentIconCandidate("c-old", "cursor", "Old", "done", true, 9),
            new AgentIconCandidate("w-old", "cursor", "Old wait", "wait", true, 1),
            new AgentIconCandidate("w-new", "cursor", "New wait", "wait", false, 4),
            new AgentIconCandidate("g-wait", "chatgpt", "Wait", "wait", false, 2),
            new AgentIconCandidate("g-run", "gpt", "Run", "running", false, 8),
            new AgentIconCandidate("x-done", "codex", "Done", "done", true, 3),
            new AgentIconCandidate("fail", "grok", "Fail", "fail", false, 4),
            new AgentIconCandidate("cat", "mochi", "Cat", "running", false, 9)
        ]);
        Assert.Equal(["c-done", "w-new", "w-old", "c-run", "fail", "g-run", "g-wait"], rows.Select(row => row.Id));
        Assert.Equal(["cursor", "cursor", "cursor", "cursor", "grok-bot", "gpt", "gpt"], rows.Select(row => row.Source));
        Assert.True(rows[0].ShowsDot);
        Assert.Equal(["c-done"], rows[0].MemberIDs);
        Assert.DoesNotContain(rows, row => row.Id == "c-old");
        Assert.True(rows.Single(row => row.Id == "fail").ShowsDot);
        Assert.Empty(AgentIconList.Rows([new AgentIconCandidate("d2", "gpt", "D", "fail", true, 3)]));
        Assert.Single(AgentIconList.Rows([new AgentIconCandidate("d2", "gpt", "D", "fail", true, 3)], new HashSet<string> { "d2" }));
        Assert.Equal(["ask", "chat"], AgentIconList.Rows([
            new AgentIconCandidate("chat", "cursor", "Cursor", "running", false, 5),
            new AgentIconCandidate("ask", "cursor", "Shell", "wait", false, 9)
        ]).Select(row => row.Id));
        Assert.True(AgentIconList.PermissionStaysOpen("wait", true));
        Assert.False(AgentIconList.PermissionStaysOpen("wait", false));
        Assert.False(AgentIconList.PermissionStaysOpen("running", true));
        var clock = 10_000.0 + AppConfig.IconMaxAge;
        var quiet = clock - AppConfig.ActiveTaskWindow - 1;
        Assert.True(AgentIconList.IsWorking(new AgentIconCandidate("fresh", "cursor", "R", "running", true, clock), clock));
        Assert.False(AgentIconList.IsWorking(new AgentIconCandidate("quiet", "cursor", "R", "running", false, quiet), clock));
        Assert.True(AgentIconList.ShouldShow(new AgentIconCandidate("seen-running", "cursor", "R", "running", true, quiet), new HashSet<string> { "seen-running" }, clock));
        Assert.False(AgentIconList.ShouldShow(new AgentIconCandidate("read-done", "cursor", "D", "done", true, clock), now: clock));
        Assert.Equal(["fresh", "unread-done", "ask"], AgentIconList.Rows([
            new AgentIconCandidate("fresh", "cursor", "R", "running", true, clock),
            new AgentIconCandidate("quiet", "cursor", "R", "running", false, quiet),
            new AgentIconCandidate("unread-done", "cursor", "D", "done", false, quiet - 10),
            new AgentIconCandidate("read-done", "cursor", "D", "done", true, quiet),
            new AgentIconCandidate("ask", "codex", "W", "wait", true, quiet)
        ], now: clock).Select(row => row.Id));
        var stale = clock - AppConfig.IconMaxAge - 1;
        Assert.True(AgentIconList.IsExpired(new AgentIconCandidate("old", "cursor", "W", "wait", false, stale), clock));
        Assert.False(AgentIconList.ShouldShow(new AgentIconCandidate("old-wait", "cursor", "W", "wait", false, stale, true), new HashSet<string> { "old-wait" }, clock));
        Assert.Equal(["fresh"], AgentIconList.Rows([
            new AgentIconCandidate("fresh", "cursor", "R", "running", true, clock),
            new AgentIconCandidate("old-ask", "cursor", "W", "wait", false, stale, true)
        ], now: clock).Select(row => row.Id));
    }

    [Fact]
    public void QuestionBlurbShowsInEveryStatus()
    {
        const string question = "把状态环加回来";
        foreach (var status in new[] { "running", "wait", "done", "fail" })
        {
            Assert.Equal(question, PermissionCopy.CardBlurb(question, "Chat", "", status, []));
            Assert.Equal(question, PermissionCopy.CardBlurb(question, "Shell", "permission", status, ["allow", "deny"]));
        }
        Assert.Equal(question, PermissionCopy.CardBlurb("", "Chat", "", "running", [], question));
        Assert.Equal("", PermissionCopy.CardBlurb("", "Chat", "", "done", [], "Cursor"));
        Assert.Equal("", PermissionCopy.CardBlurb("", "Shell", "permission", "wait", ["allow", "deny"]));
        Assert.Equal("Shell needs Allow", PermissionCopy.ChoiceQuestion("permission", "Shell", "", "wait", ["allow", "deny"]));
        Assert.Equal("MCP needs Allow", PermissionCopy.ChoiceQuestion("permission", "MCP", "", "wait", ["allow", "deny"]));
        Assert.Equal("Running", PermissionCopy.StatusLine("running"));
        Assert.Equal("Waiting", PermissionCopy.StatusLine("wait"));
        Assert.Equal("Done", PermissionCopy.StatusLine("done"));
        Assert.Equal("Failed", PermissionCopy.StatusLine("fail"));
    }

    [Fact]
    public void CardQAShowsAnswerAndChoice()
    {
        const string id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var live = new Dictionary<string, object?>();
        var submit = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeSubmitPrompt"),
            ("conversation_id", id),
            ("prompt", "全部的连接方式")))!;
        HookEvents.Merge(submit, live, 1);
        Assert.False(Bags.Object(live[id]).ContainsKey("answer"));
        var thought = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "afterAgentThought"),
            ("conversation_id", id),
            ("text", "thinking stays out of the answer line")))!;
        Assert.Equal("", thought.Answer);
        HookEvents.Merge(thought, live, 2);
        Assert.False(Bags.Object(live[id]).ContainsKey("answer"));
        var reply = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "afterAgentResponse"),
            ("conversation_id", id),
            ("text", "代码在 /Users/example/Mochi-V3")))!;
        Assert.Equal("代码在 /Users/example/Mochi-V3", reply.Answer);
        Assert.Equal("heartbeat", reply.Status);
        HookEvents.Merge(reply, live, 3);
        Assert.Equal("全部的连接方式", Bags.Object(live[id])["summary"]);
        Assert.Equal("代码在 /Users/example/Mochi-V3", Bags.Object(live[id])["answer"]);
        Assert.Equal("running", Bags.Object(live[id])["status"]);
        var next = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeSubmitPrompt"),
            ("conversation_id", id),
            ("prompt", "再问一句")))!;
        HookEvents.Merge(next, live, 4);
        Assert.Equal("再问一句", Bags.Object(live[id])["summary"]);
        Assert.False(Bags.Object(live[id]).ContainsKey("answer"));
        var asked = HookEvents.Interpret(null, new Dictionary<string, object?>
        {
            ["hook_event_name"] = "preToolUse",
            ["conversation_id"] = id,
            ["tool_name"] = "AskQuestion",
            ["tool_input"] = new Dictionary<string, object?>
            {
                ["questions"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "q1",
                        ["prompt"] = "用哪种连接？",
                        ["options"] = new List<object?>
                        {
                            new Dictionary<string, object?> { ["id"] = "wifi", ["label"] = "Wi-Fi" },
                            new Dictionary<string, object?> { ["id"] = "eth", ["label"] = "Ethernet" }
                        }
                    }
                }
            }
        })!;
        Assert.Equal("wait", asked.Status);
        Assert.Equal("choice", asked.Kind);
        Assert.Equal("用哪种连接？", asked.ChoicePrompt);
        Assert.Equal(["wifi", "eth"], asked.Options.Select(option => option["id"]));
        HookEvents.Merge(asked, live, 5);
        var record = Bags.Object(live[id]);
        Assert.Equal("wait", record["status"]);
        Assert.Equal("用哪种连接？", record["choice_prompt"]);
        Assert.Equal("再问一句", record["summary"]);
        Assert.Equal("Approve this plan?", PermissionCopy.ChoiceQuestion("plan", "", "", "wait", ["approve", "keep"]));
    }

    [Fact]
    public void TranscriptHintsStayOffTheHotPath()
    {
        Assert.True(TranscriptHints.RefreshInterval >= 5);
        Assert.False(TranscriptHints.Due(0, 10, false));
        Assert.True(TranscriptHints.Due(0, 10, true));
        Assert.False(TranscriptHints.Due(10, 10 + TranscriptHints.RefreshInterval - 0.1, true));
        Assert.True(TranscriptHints.Due(10, 10 + TranscriptHints.RefreshInterval, true));
        var merged = TranscriptHints.Merge(
            [new CursorTask("a", "A", "running")],
            [new CursorTask("b", "B", "running"), new CursorTask("a", "other", "running")],
            true);
        Assert.Equal(["a", "b"], merged.Select(task => task.Id));
        Assert.Equal(["a"], TranscriptHints.Merge([new CursorTask("a", "A", "running")], [new CursorTask("b", "B", "running")], false).Select(task => task.Id));
    }

    [Fact]
    public void TaskWatchFiresWhenTheFileIsReplaced()
    {
        var directory = Temp();
        var file = Path.Combine(directory, "tasks.json");
        var gate = new TaskWatchGate();
        gate.Arm(TaskWatchGate.Stamp(file));
        File.WriteAllText(Path.Combine(directory, "other.json"), "[]");
        Assert.False(gate.Note(TaskWatchGate.Stamp(file)));
        AtomicFile.WriteText("{\"tasks\":{\"once\":{\"id\":\"once\"}}}", file);
        var stamp = TaskWatchGate.Stamp(file);
        Assert.True(gate.Note(stamp));
        Assert.Equal(WatchPublish.Fire, gate.Publish(stamp, true));
        Assert.Equal(16, TaskWatchGate.CoalesceMilliseconds);
        Assert.False(gate.Note(stamp));
        File.Delete(file);
        Assert.Equal(WatchPublish.Retry, gate.Publish(TaskWatchGate.Stamp(file), true));
        Assert.Equal(WatchPublish.Fire, gate.Publish(TaskWatchGate.Stamp(file), false));
    }

    [Fact]
    public void ConnectorRegistryNamesCursorAndCodex()
    {
        Assert.Equal(["cursor", "codex", "grok-bot"], AgentConnectorRegistry.Primary.Select(item => item.Id));
        Assert.Equal("Cursor", AgentConnectorRegistry.Connector("cursor")!.DisplayName);
        Assert.Equal("Codex", AgentConnectorRegistry.Connector("codex")!.DisplayName);
        Assert.Equal("Grok Bot", AgentConnectorRegistry.Connector("grok")!.DisplayName);
        Assert.Equal("grok-bot", AgentConnectorRegistry.Connector("grok-bot")!.Id);
        Assert.True(AgentConnectorRegistry.Connector("cursor")!.NeedsPrivacyAck);
        Assert.False(AgentConnectorRegistry.Connector("codex")!.NeedsPrivacyAck);
        Assert.False(AgentConnectorRegistry.Connector("grok-bot")!.NeedsPrivacyAck);
        Assert.Null(AgentConnectorRegistry.Connector("claude"));
    }
}
