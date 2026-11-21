namespace Mochi.Core.Tests;

public class PromptRulesTests
{
    private static string TempFile(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "mochi-" + Guid.NewGuid().ToString("N") + "-" + name);
        return path;
    }

    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs)
    {
        return pairs.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static List<string> IconSources(HookOutcome? outcome)
    {
        if (outcome == null) return [];
        return AgentIconList.Rows([
            new AgentIconCandidate(outcome.Id, outcome.Source, outcome.Title, outcome.Status, false, 20)
        ]).Select(row => row.Source).ToList();
    }

    [Fact]
    public void ChatSummarySurvivesLaterStatus()
    {
        const string fixture = "Show this card blurb";
        const string id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var nested = HookEvents.Interpret(null, new Dictionary<string, object?>
        {
            ["hook_event_name"] = "BeforeSubmitPrompt",
            ["conversation_id"] = "nested-chat",
            ["prompt"] = new Dictionary<string, object?> { ["text"] = fixture }
        });
        Assert.Equal("running", nested!.Status);
        Assert.Equal("Chat", nested.Detail);
        Assert.Equal(fixture, nested.Summary);
        Assert.True(nested.PromptSubmitted);
        var parts = HookEvents.Interpret(null, new Dictionary<string, object?>
        {
            ["hook_event_name"] = "beforeSubmitPrompt",
            ["conversation_id"] = "parts-chat",
            ["prompt"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "text", ["text"] = fixture } }
        });
        Assert.Equal(fixture, parts!.Summary);
        var wrapped = HookEvents.Interpret(null, new Dictionary<string, object?>
        {
            ["hook_event_name"] = "before_submit_prompt",
            ["conversation_id"] = "wrapped-chat",
            ["input"] = new Dictionary<string, object?> { ["prompt"] = new Dictionary<string, object?> { ["content"] = fixture } }
        });
        Assert.Equal(fixture, wrapped!.Summary);
        Assert.Equal("Chat", wrapped.Detail);

        var longFixture = string.Concat(Enumerable.Repeat("blurb ", 12));
        var submit = HookEvents.Interpret(null, Bag(("hook_event_name", "beforeSubmitPrompt"), ("conversation_id", id), ("prompt", longFixture)))!;
        var stored = PermissionCopy.Abbreviate(longFixture);
        Assert.Equal(stored, submit.Summary);
        Assert.True(submit.Summary.Length <= 40);
        Assert.DoesNotContain(longFixture, submit.Summary);
        var url = TempFile("summary.json");
        HookEvents.Commit(submit, url, 10);
        var longReply = string.Concat(Enumerable.Repeat("assistant reply stays out ", 24));
        var heartbeat = HookEvents.Interpret(null, Bag(("hook_event_name", "afterAgentResponse"), ("conversation_id", id), ("text", longReply)))!;
        Assert.Equal("", heartbeat.Summary);
        Assert.False(heartbeat.PromptSubmitted);
        Assert.Equal("heartbeat", heartbeat.Status);
        var storedAnswer = PermissionCopy.CardAnswer(longReply);
        Assert.Equal(storedAnswer, heartbeat.Answer);
        Assert.True(heartbeat.Answer.Length <= PermissionCopy.AnswerLimit);
        Assert.DoesNotContain(longReply, heartbeat.Answer);
        HookEvents.Commit(heartbeat, url, 11);
        var done = HookEvents.Interpret(null, Bag(("hook_event_name", "sessionEnd"), ("conversation_id", id), ("prompt", "should not replace the blurb")))!;
        Assert.Equal("done", done.Status);
        Assert.Equal("", done.Summary);
        Assert.Equal("", done.Answer);
        HookEvents.Commit(done, url, 12);
        var root = JsonFile.Read(url);
        var record = Bags.Object(Bags.Object(root["tasks"])[id]);
        Assert.Equal("done", record["status"]);
        Assert.Equal(stored, record["summary"]);
        Assert.Equal(storedAnswer, record["answer"]);
        Assert.False(record.ContainsKey("prompt"));
        var text = File.ReadAllText(url);
        Assert.DoesNotContain(longFixture, text);
        Assert.DoesNotContain(longReply, text);
        Assert.DoesNotContain("should not replace the blurb", text);
        var shown = TaskReader.Load(url, 13, true);
        Assert.Equal(stored, shown.Single(task => task.Id == id).Summary);
        Assert.Equal(storedAnswer, shown.Single(task => task.Id == id).Answer);
        Assert.Equal(stored, PermissionCopy.CardBlurb(stored, "Chat", "", "done", []));

        const string parent = "11111111-2222-3333-4444-555555555555";
        const string child = "22222222-3333-4444-5555-666666666666";
        var live = new Dictionary<string, object?>();
        var parentSubmit = HookEvents.Interpret(null, Bag(("hook_event_name", "beforeSubmitPrompt"), ("conversation_id", parent), ("prompt", fixture)))!;
        HookEvents.Merge(parentSubmit, live, 1);
        var childStart = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "subagentStart"),
            ("conversation_id", parent),
            ("subagent_id", child),
            ("task", "Subagent fixture task"),
            ("summary", "Agent output fixture stays out")))!;
        Assert.Equal(child, childStart.Id);
        Assert.Equal("Subagent fixture task", childStart.Summary);
        Assert.False(childStart.PromptSubmitted);
        HookEvents.Merge(childStart, live, 2);
        Assert.Equal(fixture, Bags.Object(live[parent])["summary"]);
        Assert.Equal("Subagent fixture task", Bags.Object(live[child])["summary"]);
        var longChildOutput = string.Concat(Enumerable.Repeat("Agent output fixture stays out ", 6));
        var childDone = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "subagentStop"),
            ("conversation_id", parent),
            ("subagent_id", child),
            ("task", ""),
            ("summary", longChildOutput)))!;
        Assert.Equal("", childDone.Summary);
        Assert.Equal(PermissionCopy.CardAnswer(longChildOutput), childDone.Answer);
        Assert.Equal("done", childDone.Status);
        HookEvents.Merge(childDone, live, 3);
        Assert.Equal("done", Bags.Object(live[child])["status"]);
        Assert.Equal("Subagent fixture task", Bags.Object(live[child])["summary"]);
        Assert.Equal(PermissionCopy.CardAnswer(longChildOutput), Bags.Object(live[child])["answer"]);
        Assert.Equal(fixture, Bags.Object(live[parent])["summary"]);
        var blob = JsonFile.Serialize(live);
        Assert.DoesNotContain(longChildOutput, blob);

        const string codexID = "01a1cfbd-fd7f-7001-b887-2f0b0f96df9e";
        const string codexReply = "收到，测试正常。";
        var codexStop = HookEvents.Interpret("codex", Bag(
            ("hook_event_name", "Stop"),
            ("session_id", codexID),
            ("last_assistant_message", codexReply),
            ("transcript_path", "/tmp/SECRET-transcript"),
            ("prompt", "SECRET prompt")))!;
        Assert.Equal("codex", codexStop.Source);
        Assert.Equal("done", codexStop.Status);
        Assert.Equal("", codexStop.Summary);
        Assert.Equal(codexReply, codexStop.Answer);
        var codexTasks = new Dictionary<string, object?>
        {
            [codexID] = new Dictionary<string, object?>
            {
                ["id"] = codexID, ["source"] = "codex", ["status"] = "running", ["summary"] = "测试", ["title"] = "Codex"
            }
        };
        HookEvents.Merge(codexStop, codexTasks, 4);
        var codexRecord = Bags.Object(codexTasks[codexID]);
        Assert.Equal("done", codexRecord["status"]);
        Assert.Equal("测试", codexRecord["summary"]);
        Assert.Equal(codexReply, codexRecord["answer"]);
        Assert.False(codexRecord.ContainsKey("last_assistant_message"));
        Assert.False(codexRecord.ContainsKey("prompt"));
        var longCodex = string.Concat(Enumerable.Repeat("codex reply stays capped ", 40));
        var capped = HookEvents.Interpret("codex", Bag(("hook_event_name", "Stop"), ("session_id", codexID), ("last_assistant_message", longCodex)))!;
        Assert.Equal(PermissionCopy.CardAnswer(longCodex), capped.Answer);
        Assert.True(capped.Answer.Length <= PermissionCopy.AnswerLimit);
        Assert.DoesNotContain(longCodex, capped.Answer);
    }

    [Fact]
    public void SubmittedChatKeepsTheAgentAndDropsThePrompt()
    {
        const string secret = "合并到一个文件";
        var longPrompt = string.Concat(Enumerable.Repeat("长提示词内容不要整段留下", 4));
        var outcome = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeSubmitPrompt"),
            ("conversation_id", "11111111-2222-3333-4444-555555555555"),
            ("prompt", secret),
            ("command", "rm -rf /tmp/nope"),
            ("message", "secret message"),
            ("tool_input", new Dictionary<string, object?> { ["command"] = "ls" })))!;
        Assert.Equal("cursor", outcome.Source);
        Assert.Equal("Cursor", outcome.Title);
        Assert.Equal("running", outcome.Status);
        Assert.False(outcome.WaitsForPermission);
        Assert.Equal(secret, outcome.Summary);
        var longOutcome = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeSubmitPrompt"),
            ("conversation_id", "22222222-3333-4444-5555-666666666666"),
            ("prompt", longPrompt)))!;
        Assert.Equal(PermissionCopy.Abbreviate(longPrompt), longOutcome.Summary);
        Assert.True(longOutcome.Summary.Length <= 40);
        Assert.DoesNotContain(longPrompt, longOutcome.Summary);
        var url = TempFile("hook.json");
        HookEvents.Commit(outcome, url, 20);
        var text = File.ReadAllText(url);
        Assert.DoesNotContain("rm -rf", text);
        Assert.DoesNotContain("secret message", text);
        Assert.DoesNotContain("\"prompt\"", text);
        Assert.DoesNotContain("\"command\"", text);
        Assert.Contains("\"source\" : \"cursor\"", text);
        Assert.DoesNotContain("transcript", text);
        var savedChat = Bags.Object(Bags.Object(JsonFile.Read(url)["tasks"])["11111111-2222-3333-4444-555555555555"]);
        Assert.Equal("Chat", savedChat["detail"]);
        Assert.Equal("cursor", savedChat["source"]);
        Assert.Equal(secret, savedChat["summary"]);
        Assert.False(savedChat.ContainsKey("prompt"));
        Assert.Equal(["cursor"], IconSources(outcome));
        var codex = HookEvents.Interpret("codex", Bag(("hook_event_name", "UserPromptSubmit"), ("session_id", "codex-session"), ("prompt", secret)))!;
        Assert.Equal("codex", codex.Source);
        Assert.Equal("Codex", codex.Title);
        Assert.Equal("running", codex.Status);
        Assert.Equal("Chat", codex.Detail);
        Assert.Equal(["codex"], IconSources(codex));
        var claudeChat = HookEvents.Interpret("claude", Bag(
            ("hook_event_name", "UserPromptSubmit"), ("session_id", "claude-chat"), ("prompt", secret), ("command", "echo hi")))!;
        Assert.Equal("claude", claudeChat.Source);
        Assert.Equal("running", claudeChat.Status);
        Assert.Equal("Chat", claudeChat.Detail);
        Assert.Equal(["claude"], IconSources(claudeChat));
        var permission = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeShellExecution"),
            ("conversation_id", "11111111-2222-3333-4444-555555555555"),
            ("command", "echo hi")))!;
        Assert.Equal("wait", permission.Status);
        Assert.True(permission.WaitsForPermission);
        Assert.Equal("cursor", permission.PermissionStyle);
        Assert.Equal(["allow", "deny"], permission.Options.Select(option => option["id"]));
        Assert.Equal("Shell", permission.Detail);
        Assert.Equal("Shell", permission.Title);
        Assert.Equal("", permission.Summary);
        Assert.DoesNotContain(secret, permission.Title);
        var mergeURL = TempFile("merge.json");
        HookEvents.Commit(outcome, mergeURL, 20);
        HookEvents.Commit(permission, mergeURL, 21);
        var merged = Bags.Object(Bags.Object(JsonFile.Read(mergeURL)["tasks"])["11111111-2222-3333-4444-555555555555"]);
        Assert.Equal("wait", merged["status"]);
        Assert.Equal(secret, merged["summary"]);
        Assert.Equal("Shell", merged["detail"]);
        Assert.Equal("Shell", merged["title"]);
        var mergedTask = TaskReader.Load(mergeURL, 22, true).Single(task => task.Id == "11111111-2222-3333-4444-555555555555");
        Assert.Equal("wait", mergedTask.Status);
        Assert.Equal("permission", mergedTask.Kind);
        Assert.Equal(secret, mergedTask.Summary);
        Assert.Equal(secret, PermissionCopy.CardBlurb(mergedTask.Summary, "Shell", "permission", "wait", ["allow", "deny"]));
        Assert.Equal("", PermissionCopy.CardBlurb("", "Shell", "permission", "wait", ["allow", "deny"]));
        Assert.Equal("Shell needs Allow", PermissionCopy.ChoiceQuestion("permission", "Shell", "", "wait", ["allow", "deny"]));
        var shellURL = TempFile("shell.json");
        HookEvents.Commit(permission, shellURL, 21);
        Assert.DoesNotContain("echo hi", File.ReadAllText(shellURL));
        var shellRecord = Bags.Object(Bags.Object(JsonFile.Read(shellURL)["tasks"])["11111111-2222-3333-4444-555555555555"]);
        Assert.Equal("Shell", shellRecord["detail"]);
        Assert.Equal("Shell", shellRecord["title"]);
        Assert.False(shellRecord.ContainsKey("command"));
        Assert.False(shellRecord.ContainsKey("prompt"));
        var claude = HookEvents.Interpret("claude", Bag(("hook_event_name", "PermissionRequest"), ("session_id", "claude-1"), ("command", "echo hi")))!;
        Assert.Equal("claude", claude.Source);
        Assert.Equal("running", claude.Status);
        Assert.False(claude.WaitsForPermission);
        Assert.Equal("Permission", claude.Detail);
        var cursorAgain = HookEvents.Interpret("claude", Bag(
            ("hook_event_name", "UserPromptSubmit"), ("cursor_version", "1.0"), ("session_id", "from-cursor"), ("prompt", secret)))!;
        Assert.Equal("cursor", cursorAgain.Source);
        Assert.Equal("Chat", cursorAgain.Detail);
        Assert.Contains("beforeSubmitPrompt", HookInstaller.Events);
        Assert.Contains("UserPromptSubmit", HookInstaller.CommandEvents);
        Assert.DoesNotContain(HookInstaller.Events, name => name.Contains("gpt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(HookInstaller.CommandEvents, name => name.Contains("gpt", StringComparison.OrdinalIgnoreCase));
        var stdout = HookEvents.PermissionStdout("codex", "deny");
        Assert.Equal("deny", Bags.Object(Bags.Object(stdout["hookSpecificOutput"])["decision"])["behavior"]);
        Assert.Equal("allow", HookEvents.PermissionStdout("cursor", "allow")["permission"]);
    }

    [Fact]
    public void AnsweredPermissionIsNotShownAgain()
    {
        const string secret = "合并到一个文件";
        const string debug = "PM手点：请选 Allow 或 Deny";
        const string command = "SECRET-COMMAND";
        var url = TempFile("permission.json");
        JsonFile.Write(new Dictionary<string, object?>
        {
            ["tasks"] = new Dictionary<string, object?>
            {
                ["old"] = new Dictionary<string, object?>
                {
                    ["id"] = "old",
                    ["title"] = secret,
                    ["prompt"] = secret,
                    ["command"] = command,
                    ["detail"] = debug,
                    ["status"] = "wait",
                    ["kind"] = "permission",
                    ["source"] = "cursor",
                    ["request_id"] = "req-old",
                    ["updated"] = 10,
                    ["options"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["id"] = "allow", ["label"] = "Allow" },
                        new Dictionary<string, object?> { ["id"] = "deny", ["label"] = "Deny" }
                    }
                }
            }
        }, url);
        var shown = TaskReader.Load(url, 20);
        Assert.Single(shown);
        Assert.Equal("Permission", shown[0].Title);
        Assert.Equal("", shown[0].Detail);
        Assert.Equal("", shown[0].Prompt);
        Assert.Equal("wait", shown[0].Status);
        Assert.Equal("permission", shown[0].Kind);
        Assert.Equal(["allow", "deny"], shown[0].Options.Select(option => option["id"]));
        Assert.Equal("", PermissionCopy.VisibleDetail("permission", "wait", debug, ["allow", "deny"]));
        Assert.Equal("", PermissionCopy.VisibleDetail("permission", "wait", "Shell", ["allow", "deny"]));
        Assert.Equal("Shell", PermissionCopy.VisibleTitle("permission", "wait", secret, "Shell", "Cursor", ["allow", "deny"]));
        Assert.Equal("Permission", PermissionCopy.VisibleTitle("permission", "wait", secret, debug, "Cursor", ["allow", "deny"]));
        Assert.True(PermissionCopy.ContainsDebug(debug));
        HookEvents.ScrubStored(url);
        var scrubbed = File.ReadAllText(url);
        Assert.DoesNotContain(secret, scrubbed);
        Assert.DoesNotContain(debug, scrubbed);
        Assert.DoesNotContain(command, scrubbed);
        Assert.DoesNotContain("\"prompt\"", scrubbed);
        var chat = HookEvents.Interpret(null, Bag(("hook_event_name", "beforeSubmitPrompt"), ("conversation_id", "new-chat"), ("prompt", "1")))!;
        Assert.Equal("running", chat.Status);
        Assert.Equal("1", chat.Summary);
        Assert.Equal("Cursor", chat.Title);
        HookEvents.Commit(chat, url, 25);
        var cleaned = File.ReadAllText(url);
        Assert.DoesNotContain(secret, cleaned);
        Assert.DoesNotContain(debug, cleaned);
        Assert.DoesNotContain("PM手点", cleaned);
        Assert.DoesNotContain("请选 Allow 或 Deny", cleaned);
        Assert.DoesNotContain(command, cleaned);
        Assert.DoesNotContain("\"prompt\"", cleaned);
        var tasks = Bags.Object(JsonFile.Read(url)["tasks"]);
        var old = Bags.Object(tasks["old"]);
        var started = Bags.Object(tasks["new-chat"]);
        Assert.Equal("wait", old["status"]);
        Assert.Equal("Permission", old["title"]);
        Assert.False(old.ContainsKey("prompt"));
        Assert.False(old.ContainsKey("detail"));
        Assert.Equal("running", started["status"]);
        Assert.Equal("Cursor", started["title"]);
        Assert.Equal("Chat", started["detail"]);
        Assert.False(started.ContainsKey("prompt"));
        var replyURL = TempFile("reply.json");
        ReplyWriter.AnswerPermission("old", "req-old", "Allow", "permission", replyURL, url, 30);
        var reply = JsonFile.Read(replyURL);
        Assert.Equal("allow", reply["action"]);
        Assert.Equal("permission", reply["kind"]);
        Assert.Equal("req-old", reply["request_id"]);
        var settledOld = TaskReader.Load(url, 40).Single(task => task.Id == "old");
        Assert.Equal("running", settledOld.Status);
        Assert.Empty(settledOld.Options);
        Assert.Equal("", settledOld.Kind);
        Assert.Equal("", settledOld.Prompt);
        var again = HookEvents.Interpret(null, Bag(
            ("hook_event_name", "beforeShellExecution"),
            ("conversation_id", "old"),
            ("generation_id", "req-old"),
            ("command", command)))!;
        Assert.Equal("req-old", again.RequestID);
        HookEvents.Commit(again, url, 41);
        var hiddenOld = TaskReader.Load(url, 50).Single(task => task.Id == "old");
        Assert.Equal("running", hiddenOld.Status);
        Assert.Empty(hiddenOld.Options);
        Assert.Equal("", hiddenOld.Kind);
        var stored = File.ReadAllText(url);
        Assert.DoesNotContain(secret, stored);
        Assert.DoesNotContain(command, stored);
        Assert.DoesNotContain("PM手点", stored);
        Assert.False(PermissionCopy.AnswersPermission("mode", "deny"));
        Assert.True(ReplyWriter.ClosesBubble("permission", "deny"));
    }
}
