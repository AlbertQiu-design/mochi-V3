using System.Text.RegularExpressions;

namespace Mochi.Core;

public sealed class HookOutcome
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Kind { get; set; } = "";
    public string RequestID { get; set; } = "";
    public string QuestionID { get; set; } = "";
    public List<Dictionary<string, string>> Options { get; set; } = [];
    public bool WaitsForPermission { get; set; }
    public string PermissionStyle { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Answer { get; set; } = "";
    public string ChoicePrompt { get; set; } = "";
    public bool PromptSubmitted { get; set; }
}

public static class PermissionCopy
{
    public static readonly string[] ActionKinds = ["Shell", "MCP", "Permission"];
    public const int AnswerLimit = 500;

    public static bool ContainsDebug(string text)
    {
        return text.Contains("PM手点", StringComparison.Ordinal)
            || text.Contains("请选 Allow 或 Deny", StringComparison.Ordinal)
            || text.Contains("请点 Allow 或 Deny", StringComparison.Ordinal);
    }

    public static bool IsPermission(string kind, string status, IReadOnlyList<string> optionIDs)
    {
        if (kind.Trim() == "permission") return true;
        if (status != "wait") return false;
        var ids = new HashSet<string>(optionIDs.Select(id => id.Trim().ToLowerInvariant()));
        return ids.Contains("allow") && ids.Contains("deny");
    }

    public static bool AnswersPermission(string kind, string action)
    {
        var normalized = action.Trim().ToLowerInvariant();
        if (normalized is not ("allow" or "deny")) return false;
        var trimmed = kind.Trim();
        return trimmed == "permission" || trimmed.Length == 0;
    }

    public static string ActionKind(string detail, string title = "")
    {
        var trimmedDetail = detail.Trim();
        if (ActionKinds.Contains(trimmedDetail)) return trimmedDetail;
        var trimmedTitle = title.Trim();
        if (ActionKinds.Contains(trimmedTitle)) return trimmedTitle;
        return "Permission";
    }

    public static string VisibleTitle(string kind, string status, string title, string detail, string agent, IReadOnlyList<string> optionIDs)
    {
        if (IsPermission(kind, status, optionIDs)) return ActionKind(detail, title);
        var trimmed = title.Trim();
        if (trimmed.Length == 0 || trimmed == "New chat" || ContainsDebug(trimmed)) return agent;
        return trimmed;
    }

    public static string VisibleDetail(string kind, string status, string detail, IReadOnlyList<string> optionIDs)
    {
        _ = kind;
        _ = status;
        _ = optionIDs;
        var trimmed = detail.Trim();
        if (trimmed.Length == 0 || ContainsDebug(trimmed)) return "";
        if (ActionKinds.Contains(trimmed) || trimmed == "Chat") return "";
        return trimmed;
    }

    public static string CardBlurb(string summary, string detail, string kind, string status, IReadOnlyList<string> optionIDs, string title = "")
    {
        var stored = Abbreviate(summary);
        if (stored.Length > 0 && !ContainsDebug(stored)) return stored;
        var named = Abbreviate(title);
        if (IsQuestionTitle(named)) return named;
        return VisibleDetail(kind, status, detail, optionIDs);
    }

    public static string CardAnswer(string answer)
    {
        var stored = Abbreviate(answer, AnswerLimit);
        if (stored.Length == 0 || ContainsDebug(stored)) return "";
        return stored;
    }

    public static string StatusLine(string status)
    {
        return status switch
        {
            "running" => "Running",
            "wait" => "Waiting",
            "done" => "Done",
            "fail" => "Failed",
            _ => ""
        };
    }

    public static string ChoiceQuestion(string kind, string detail, string stored, string status, IReadOnlyList<string> optionIDs)
    {
        var abbreviated = Abbreviate(stored);
        if (abbreviated.Length > 0 && !ContainsDebug(abbreviated)) return abbreviated;
        if (IsPermission(kind, status, optionIDs))
        {
            return ActionKind(detail) switch
            {
                "Shell" => "Shell needs Allow",
                "MCP" => "MCP needs Allow",
                _ => "Needs Allow or Deny"
            };
        }
        return kind.Trim() switch
        {
            "plan" => "Approve this plan?",
            "mode" => "Switch mode?",
            _ => ""
        };
    }

    public static string Abbreviate(string text, int limit = 40)
    {
        var flat = Regex.Replace(text, "\\s+", " ").Trim();
        if (flat.Length == 0) return "";
        if (flat.Length <= limit) return flat;
        return flat[..Math.Max(1, limit - 1)] + "…";
    }

    private static bool IsQuestionTitle(string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0 || ContainsDebug(trimmed)) return false;
        if (ActionKinds.Contains(trimmed)) return false;
        return trimmed is not (
            "Cursor" or "Codex" or "Claude" or "Gemini" or "Copilot" or "OpenCode"
            or "Grok Bot" or "GPT" or "小猫" or "New chat" or "Permission" or "Chat");
    }
}

public static class HookEvents
{
    private static readonly List<Dictionary<string, string>> AllowDeny =
    [
        new() { ["id"] = "allow", ["label"] = "Allow" },
        new() { ["id"] = "deny", ["label"] = "Deny" }
    ];

    public static HookOutcome? Interpret(
        string? sourceFlag,
        IReadOnlyDictionary<string, object?> payload,
        IReadOnlyDictionary<string, string>? environment = null,
        string? eventFlag = null)
    {
        environment ??= new Dictionary<string, string>();
        var eventName = EventName(payload, eventFlag);
        if (eventName.Length == 0) return null;
        var source = ResolvedSource(sourceFlag, payload, environment);
        var mapped = StatusFor(eventName, payload, source);
        if (mapped.Length == 0) return null;
        var permission = IsPermissionEvent(eventName, source);
        var waits = permission && (source == "cursor" || source == "codex");
        var detail = SafeDetail(eventName, source);
        var choice = ChoiceOptions(payload, eventName);
        var options = waits ? AllowDeny.Select(CopyOption).ToList() : choice.Options;
        var kind = waits ? "permission" : choice.Kind;
        var summary = waits ? "" : ChatSummary(eventName, payload);
        var waiting = mapped == "wait";
        return new HookOutcome
        {
            Id = SessionID(payload, source, eventName),
            Source = source,
            Title = waits ? PermissionCopy.ActionKind(detail) : SlotFace.DisplayName(source),
            Status = mapped,
            Kind = waiting ? kind : "",
            RequestID = waits ? RequestID(payload) : "",
            QuestionID = waiting ? choice.QuestionID : "",
            Options = waiting ? options : [],
            WaitsForPermission = waits,
            PermissionStyle = waits ? (source == "codex" ? "codex" : "cursor") : "",
            Detail = detail,
            Summary = summary,
            Answer = ChatAnswer(eventName, payload),
            ChoicePrompt = waiting && !waits ? choice.Prompt : "",
            PromptSubmitted = IsPromptSubmit(eventName) && summary.Length > 0
        };
    }

    public static void Merge(HookOutcome outcome, Dictionary<string, object?> tasks, double now)
    {
        var (oldKey, previous) = Find(tasks, outcome.Id);
        var status = outcome.Status;
        var requestID = outcome.RequestID;
        var kind = outcome.Kind;
        var options = outcome.Options.Select(CopyOption).ToList();
        var questionID = outcome.QuestionID;
        var submitGen = Bags.Whole(previous.GetValueOrDefault("submit_gen"));
        if (outcome.PromptSubmitted) submitGen += 1;
        if (status == "closed")
        {
            tasks.Remove(outcome.Id);
            if (oldKey != outcome.Id) tasks.Remove(oldKey);
            return;
        }
        if (status == "heartbeat")
        {
            status = Bags.Text(previous.GetValueOrDefault("status"));
            if (status is not ("running" or "wait" or "done" or "fail")) status = "running";
            requestID = Bags.Text(previous.GetValueOrDefault("request_id"));
            kind = Bags.Text(previous.GetValueOrDefault("kind"));
            questionID = Bags.Text(previous.GetValueOrDefault("question_id"));
            options = Bags.StringOptions(previous.GetValueOrDefault("options"));
        }
        var detail = outcome.Detail;
        if (detail.Length == 0) detail = Bags.Text(previous.GetValueOrDefault("detail"));
        var summary = outcome.Summary;
        if (summary.Length == 0) summary = Bags.Text(previous.GetValueOrDefault("summary"));
        var answer = outcome.Answer;
        if (outcome.PromptSubmitted) answer = "";
        else if (answer.Length == 0) answer = Bags.Text(previous.GetValueOrDefault("answer"));
        var choicePrompt = outcome.ChoicePrompt;
        if (outcome.WaitsForPermission || status != "wait") choicePrompt = "";
        else if (choicePrompt.Length == 0) choicePrompt = Bags.Text(previous.GetValueOrDefault("choice_prompt"));
        if (oldKey != outcome.Id) tasks.Remove(oldKey);
        var record = new Dictionary<string, object?>
        {
            ["id"] = outcome.Id,
            ["title"] = outcome.Title,
            ["status"] = status,
            ["source"] = outcome.Source,
            ["updated"] = now
        };
        if (detail.Length > 0) record["detail"] = detail;
        if (summary.Length > 0) record["summary"] = summary;
        if (answer.Length > 0) record["answer"] = answer;
        if (submitGen > 0) record["submit_gen"] = submitGen;
        if (status == "wait")
        {
            record["kind"] = kind;
            record["options"] = options.Select(option => (object?)option.ToDictionary(pair => pair.Key, pair => (object?)pair.Value)).ToList();
            record["request_id"] = requestID;
            if (questionID.Length > 0) record["question_id"] = questionID;
            if (choicePrompt.Length > 0) record["choice_prompt"] = choicePrompt;
        }
        tasks[outcome.Id] = record;
    }

    public static void Commit(HookOutcome outcome, string tasksPath, double? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        var root = JsonFile.Read(tasksPath);
        var tasks = Bags.Object(root.GetValueOrDefault("tasks"));
        var delivery = Clone(outcome);
        var answered = new HashSet<string>(Bags.Array(root.GetValueOrDefault("answered")).Select(Bags.Text));
        if (delivery.Status == "wait" && delivery.RequestID.Length > 0 && answered.Contains(delivery.RequestID))
        {
            delivery.Status = "running";
            delivery.Kind = "";
            delivery.Options = [];
            delivery.WaitsForPermission = false;
            delivery.RequestID = "";
            delivery.QuestionID = "";
            delivery.Title = SlotFace.DisplayName(delivery.Source);
            delivery.Detail = "";
        }
        Merge(delivery, tasks, clock);
        Scrub(tasks);
        root["tasks"] = tasks;
        JsonFile.Write(root, tasksPath);
    }

    public static void ScrubStored(string tasksPath)
    {
        var root = JsonFile.Read(tasksPath);
        if (root.GetValueOrDefault("tasks") is not Dictionary<string, object?> tasks) return;
        var before = JsonFile.Serialize(tasks);
        Scrub(tasks);
        var after = JsonFile.Serialize(tasks);
        if (before == after) return;
        root["tasks"] = tasks;
        JsonFile.Write(root, tasksPath);
    }

    public static void Acknowledge(string id, string requestID, string tasksPath, double? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        var root = JsonFile.Read(tasksPath);
        var tasks = Bags.Object(root.GetValueOrDefault("tasks"));
        var answered = Bags.Array(root.GetValueOrDefault("answered")).Select(Bags.Text).Where(item => item.Length > 0).ToList();
        var trimmedRequest = requestID.Trim();
        var trimmedID = id.Trim();
        if (trimmedRequest.Length > 0 && !answered.Contains(trimmedRequest)) answered.Add(trimmedRequest);
        if (answered.Count > AppConfig.FedTaskLimit)
        {
            answered = answered.Skip(answered.Count - AppConfig.FedTaskLimit).ToList();
        }
        foreach (var key in tasks.Keys.ToList())
        {
            var record = Bags.Object(tasks[key]);
            if (record.Count == 0 && tasks[key] is not Dictionary<string, object?>) continue;
            var recordID = Bags.Text(record.GetValueOrDefault("id"));
            if (recordID.Length == 0) recordID = key;
            var recordRequest = Bags.Text(record.GetValueOrDefault("request_id"));
            var idMatch = trimmedID.Length > 0 && recordID == trimmedID;
            var requestMatch = trimmedRequest.Length > 0 && recordRequest == trimmedRequest;
            if (!idMatch && !requestMatch) continue;
            if (Bags.Text(record.GetValueOrDefault("status")) != "wait" || !IsPermissionRecord(record)) continue;
            record["status"] = "running";
            record["title"] = SlotFace.DisplayName(Bags.Text(record.GetValueOrDefault("source")));
            record["updated"] = clock;
            record.Remove("kind");
            record.Remove("options");
            record.Remove("detail");
            record.Remove("question_id");
            record.Remove("choice_prompt");
            record.Remove("prompt");
            record.Remove("command");
            tasks[key] = record;
        }
        Scrub(tasks);
        root["answered"] = answered.Select(item => (object?)item).ToList();
        root["tasks"] = tasks;
        JsonFile.Write(root, tasksPath);
    }

    public static Dictionary<string, object?> PermissionStdout(string style, string action)
    {
        var decision = action == "deny" ? "deny" : "allow";
        if (style == "codex")
        {
            return new Dictionary<string, object?>
            {
                ["hookSpecificOutput"] = new Dictionary<string, object?>
                {
                    ["hookEventName"] = "PermissionRequest",
                    ["decision"] = new Dictionary<string, object?> { ["behavior"] = decision }
                }
            };
        }
        return new Dictionary<string, object?> { ["permission"] = decision };
    }

    private static string ResolvedSource(string? flag, IReadOnlyDictionary<string, object?> payload, IReadOnlyDictionary<string, string> environment)
    {
        if (GrokRuntime(environment)) return "grok-bot";
        if (payload.TryGetValue("cursor_version", out var version) && Bags.Text(version).Length > 0) return "cursor";
        var flagged = AgentLaunch.Normalize(flag);
        return flagged is "codex" or "claude" or "grok-bot" or "copilot" or "opencode" or "gemini" ? flagged : "cursor";
    }

    private static bool GrokRuntime(IReadOnlyDictionary<string, string> environment)
    {
        foreach (var key in new[] { "GROK_SESSION_ID", "GROK_HOOK_EVENT" })
        {
            if (environment.TryGetValue(key, out var value) && value.Trim().Length > 0) return true;
        }
        return false;
    }

    private static string EventName(IReadOnlyDictionary<string, object?> payload, string? fallback)
    {
        foreach (var name in new[] { "hook_event_name", "event", "hookEventName", "hook_event" })
        {
            if (payload.TryGetValue(name, out var value))
            {
                var trimmed = Bags.Text(value);
                if (trimmed.Length > 0) return trimmed;
            }
        }
        return fallback?.Trim() ?? "";
    }

    private static string StatusFor(string eventName, IReadOnlyDictionary<string, object?> payload, string source)
    {
        return source switch
        {
            "copilot" => CopilotStatus(eventName, payload),
            "opencode" => OpenCodeStatus(eventName, payload),
            "gemini" => GeminiStatus(eventName, payload),
            "grok-bot" => GrokNative(eventName, payload) ?? (eventName.ToLowerInvariant() == "stop" ? "heartbeat" : CursorFamilyStatus(eventName, payload, "cursor")),
            _ => CursorFamilyStatus(eventName, payload, source)
        };
    }

    private static string CopilotStatus(string eventName, IReadOnlyDictionary<string, object?> payload)
    {
        switch (eventName)
        {
            case "sessionStart" or "SessionStart" or "userPromptSubmitted" or "UserPromptSubmit"
                or "preToolUse" or "PreToolUse" or "postToolUse" or "PostToolUse"
                or "subagentStart" or "SubagentStart" or "postToolUseFailure" or "PostToolUseFailure":
                return "running";
            case "permissionRequest" or "PermissionRequest":
                return "wait";
            case "notification" or "Notification":
                var kind = Token(payload, ["notification_type", "notificationType", "type"]);
                return kind is "permission_prompt" or "elicitation_dialog" ? "wait" : "";
            case "errorOccurred" or "ErrorOccurred":
                var context = Token(payload, ["errorContext", "error_context"]);
                if (context == "model_call") return "fail";
                if (context == "tool_execution") return "running";
                return "";
            case "agentStop" or "Stop":
                return "done";
            case "sessionEnd" or "SessionEnd":
                return "closed";
            default:
                return "";
        }
    }

    private static string GeminiStatus(string eventName, IReadOnlyDictionary<string, object?> payload)
    {
        switch (eventName)
        {
            case "SessionStart" or "BeforeAgent" or "BeforeModel" or "AfterModel"
                or "BeforeToolSelection" or "BeforeTool" or "AfterTool":
                return "running";
            case "Notification":
                var kind = Token(payload, ["notification_type", "notificationType", "type"]);
                return kind == "ToolPermission" ? "wait" : "";
            case "AfterAgent":
                return "done";
            case "SessionEnd":
                return "closed";
            default:
                return "";
        }
    }

    private static string OpenCodeStatus(string eventName, IReadOnlyDictionary<string, object?> payload)
    {
        switch (eventName)
        {
            case "session.status":
                switch (Token(payload, ["status_type", "type"]))
                {
                    case "busy" or "retry": return "running";
                    case "idle": return "done";
                    default:
                        if (payload.GetValueOrDefault("status") is Dictionary<string, object?> status)
                        {
                            return Bags.Text(status.GetValueOrDefault("type")) switch
                            {
                                "busy" or "retry" => "running",
                                "idle" => "done",
                                _ => ""
                            };
                        }
                        return "";
                }
            case "tool.execute.before" or "permission.replied" or "question.replied" or "question.rejected":
                return "running";
            case "permission.asked" or "question.asked":
                return "wait";
            case "session.idle":
                return "done";
            case "session.error":
                return "fail";
            default:
                return "";
        }
    }

    private static string? GrokNative(string eventName, IReadOnlyDictionary<string, object?> payload)
    {
        switch (eventName)
        {
            case "SessionStart" or "UserPromptSubmit" or "PreToolUse" or "PostToolUse" or "SubagentStart"
                or "PermissionDenied" or "PostToolUseFailure":
                return "running";
            case "Notification":
                var kind = Token(payload, ["notification_type", "notificationType", "type"]);
                return kind == "permission_prompt" ? "wait" : "";
            case "Stop":
                return "done";
            case "StopFailure" or "StopCancelled":
                return "fail";
            case "SessionEnd":
                return "closed";
            default:
                return null;
        }
    }

    private static string CursorFamilyStatus(string eventName, IReadOnlyDictionary<string, object?> payload, string source)
    {
        var name = eventName.ToLowerInvariant().Replace("_", "");
        if (name == "stopfailure") return "fail";
        if (IsPermissionEvent(eventName, source)) return source == "claude" ? "running" : "wait";
        if (IsChoice(payload) && !IsFinishedTool(eventName)) return "wait";
        return name switch
        {
            "afteragentthought" or "afteragentresponse" or "afterfileedit" => "heartbeat",
            "stop" => Failed(payload) ? "fail" : "done",
            "beforesubmitprompt" or "sessionstart" or "subagentstart" or "userpromptsubmit"
                or "pretooluse" or "posttooluse" or "posttoolusefailure" or "notification"
                or "beforeshellexecution" or "aftershellexecution" or "beforemcpexecution" or "aftermcpexecution" => "running",
            "sessionend" or "subagentstop" => Failed(payload) ? "fail" : "done",
            _ => ""
        };
    }

    private static bool IsPromptSubmit(string eventName)
    {
        return Normalized(eventName) is "beforesubmitprompt" or "userpromptsubmit" or "userpromptsubmitted";
    }

    private static bool IsSubagentEvent(string eventName)
    {
        return Normalized(eventName) is "subagentstart" or "subagentstop";
    }

    private static string Normalized(string eventName)
    {
        return eventName.ToLowerInvariant().Replace("_", "").Replace("-", "");
    }

    private static string ChatSummary(string eventName, IReadOnlyDictionary<string, object?> payload)
    {
        if (IsPromptSubmit(eventName))
        {
            var prompt = PromptText(payload);
            return prompt.Length == 0 ? "" : PermissionCopy.Abbreviate(prompt);
        }
        if (IsSubagentEvent(eventName))
        {
            if (Bags.Text(payload.GetValueOrDefault("subagent_id")).Length == 0) return "";
            var task = StringValue(payload.GetValueOrDefault("task")) ?? "";
            return task.Length == 0 ? "" : PermissionCopy.Abbreviate(task);
        }
        return "";
    }

    private static string ChatAnswer(string eventName, IReadOnlyDictionary<string, object?> payload)
    {
        switch (Normalized(eventName))
        {
            case "afteragentresponse":
                return PermissionCopy.CardAnswer(StringValue(payload.GetValueOrDefault("text")) ?? "");
            case "stop":
                return PermissionCopy.CardAnswer(StringValue(payload.GetValueOrDefault("last_assistant_message")) ?? "");
            case "subagentstop":
                var output = Bags.Text(payload.GetValueOrDefault("summary"));
                if (output.Length > 0) return PermissionCopy.CardAnswer(output);
                var message = StringValue(payload.GetValueOrDefault("last_assistant_message"));
                if (!string.IsNullOrEmpty(message)) return PermissionCopy.CardAnswer(message);
                return PermissionCopy.CardAnswer(StringValue(payload.GetValueOrDefault("text")) ?? "");
            default:
                return "";
        }
    }

    private static string PromptText(IReadOnlyDictionary<string, object?> payload)
    {
        foreach (var key in new[] { "prompt", "user_prompt", "userPrompt", "prompt_text", "promptText", "text", "content", "message", "initialPrompt", "initial_prompt" })
        {
            var text = StringValue(payload.GetValueOrDefault(key));
            if (!string.IsNullOrEmpty(text)) return text;
        }
        var input = Dictionary(payload, ["tool_input", "input", "arguments", "args", "request", "payload", "data"]);
        foreach (var key in new[] { "prompt", "text", "content", "message", "user_prompt", "userPrompt" })
        {
            var text = StringValue(input.GetValueOrDefault(key));
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return "";
    }

    private static string? StringValue(object? value)
    {
        if (value is string text)
        {
            var trimmed = text.Trim();
            return trimmed.Length == 0 || trimmed == "nil" ? null : trimmed;
        }
        if (value is List<object?> parts)
        {
            var chunks = new List<string>();
            foreach (var part in parts)
            {
                if (part is string chunk)
                {
                    var trimmed = chunk.Trim();
                    if (trimmed.Length > 0) chunks.Add(trimmed);
                    continue;
                }
                var dict = Bags.Object(part);
                if (dict.Count == 0 && part is not Dictionary<string, object?>) continue;
                var piece = Bags.Text(dict.GetValueOrDefault("text"));
                if (piece.Length == 0) piece = Bags.Text(dict.GetValueOrDefault("content"));
                if (piece.Length > 0) chunks.Add(piece);
            }
            var joined = string.Join(" ", chunks).Trim();
            return joined.Length == 0 ? null : joined;
        }
        if (value is Dictionary<string, object?> bag)
        {
            foreach (var key in new[] { "text", "content", "message", "prompt", "value" })
            {
                if (bag.GetValueOrDefault(key) is string inner)
                {
                    var trimmed = inner.Trim();
                    if (trimmed.Length > 0 && trimmed != "nil") return trimmed;
                }
            }
        }
        return null;
    }

    private static string SafeDetail(string eventName, string source)
    {
        if (IsPromptSubmit(eventName)) return "Chat";
        return eventName switch
        {
            "beforeShellExecution" => "Shell",
            "beforeMCPExecution" => "MCP",
            "PermissionRequest" or "permission_request" or "permissionRequest" or "permission.asked"
                => source is "cursor" or "codex" or "claude" ? "Permission" : "",
            _ => ""
        };
    }

    private static bool IsPermissionEvent(string eventName, string source)
    {
        if (source is not ("cursor" or "codex" or "claude")) return false;
        return eventName is "beforeShellExecution" or "beforeMCPExecution" or "PermissionRequest" or "permission_request";
    }

    private static string Token(IReadOnlyDictionary<string, object?> payload, string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Bags.Text(payload.GetValueOrDefault(key));
            if (value.Length > 0) return value;
        }
        return "";
    }

    private static bool IsFinishedTool(string eventName)
    {
        var name = eventName.ToLowerInvariant().Replace("_", "");
        return name is "posttooluse" or "posttoolusefailure" or "aftershellexecution" or "aftermcpexecution";
    }

    private static bool IsChoice(IReadOnlyDictionary<string, object?> payload)
    {
        var tool = ToolName(payload);
        return tool is "AskQuestion" or "CreatePlan" or "SwitchMode";
    }

    private static bool Failed(IReadOnlyDictionary<string, object?> payload)
    {
        if (payload.ContainsKey("error") && payload["error"] != null) return true;
        var status = (Bags.Text(payload.GetValueOrDefault("status")).Length > 0
            ? Bags.Text(payload.GetValueOrDefault("status"))
            : Bags.Text(payload.GetValueOrDefault("result"))).ToLowerInvariant();
        return status is "error" or "failed" or "fail" or "aborted" or "cancelled" or "canceled";
    }

    private static string ToolName(IReadOnlyDictionary<string, object?> payload)
    {
        foreach (var key in new[] { "tool_name", "toolName", "tool" })
        {
            if (payload.GetValueOrDefault(key) is string value && value.Length > 0) return value;
        }
        return "";
    }

    private static (string Kind, string QuestionID, List<Dictionary<string, string>> Options, string Prompt) ChoiceOptions(
        IReadOnlyDictionary<string, object?> payload,
        string eventName)
    {
        if (IsFinishedTool(eventName)) return ("", "", [], "");
        switch (ToolName(payload))
        {
            case "CreatePlan":
                return ("plan", "", [new() { ["id"] = "approve", ["label"] = "Approve" }, new() { ["id"] = "keep", ["label"] = "Keep planning" }], "");
            case "SwitchMode":
                return ("mode", "", [new() { ["id"] = "allow", ["label"] = "Allow switch" }, new() { ["id"] = "stay", ["label"] = "Stay" }], "");
            case "AskQuestion":
                var input = Dictionary(payload, ["tool_input", "input", "arguments", "args"]);
                var questions = Bags.Array(input.GetValueOrDefault("questions"));
                var first = questions.Count > 0 ? Bags.Object(questions[0]) : new Dictionary<string, object?>();
                var questionID = Bags.Text(first.GetValueOrDefault("id"));
                var prompt = Bags.Text(first.GetValueOrDefault("prompt"));
                if (prompt.Length == 0) prompt = Bags.Text(first.GetValueOrDefault("text"));
                if (prompt.Length == 0) prompt = Bags.Text(first.GetValueOrDefault("question"));
                return ("choice", questionID, OptionList(first.GetValueOrDefault("options") ?? input.GetValueOrDefault("options")), PermissionCopy.Abbreviate(prompt));
            default:
                return ("", "", [], "");
        }
    }

    private static List<Dictionary<string, string>> OptionList(object? raw)
    {
        var options = new List<Dictionary<string, string>>();
        foreach (var item in Bags.Array(raw))
        {
            var dict = Bags.Object(item);
            if (dict.Count == 0) continue;
            var id = Bags.Text(dict.GetValueOrDefault("id"));
            if (id.Length == 0) id = Bags.Text(dict.GetValueOrDefault("value"));
            var label = Bags.Text(dict.GetValueOrDefault("label"));
            if (label.Length == 0) label = Bags.Text(dict.GetValueOrDefault("title"));
            if (id.Length == 0) id = label.Length <= 40 ? label : label[..40];
            if (id.Length == 0 || label.Length == 0) continue;
            options.Add(new Dictionary<string, string>
            {
                ["id"] = id.Length <= 80 ? id : id[..80],
                ["label"] = label.Length <= 160 ? label : label[..160]
            });
        }
        return options;
    }

    private static string RequestID(IReadOnlyDictionary<string, object?> payload)
    {
        foreach (var key in new[] { "generation_id", "tool_use_id", "request_id" })
        {
            var value = Bags.Text(payload.GetValueOrDefault(key));
            if (value.Length > 0 && value != "nil") return value;
        }
        return Environment.TickCount64.ToString();
    }

    private static string SessionID(IReadOnlyDictionary<string, object?> payload, string source, string eventName)
    {
        if (IsSubagentEvent(eventName))
        {
            var subagent = Bags.Text(payload.GetValueOrDefault("subagent_id"));
            if (subagent.Length > 0) return BareID(subagent);
        }
        foreach (var key in new[] { "conversation_id", "session_id", "sessionId", "sessionID", "subagent_id", "composer_id", "thread_id" })
        {
            var value = Bags.Text(payload.GetValueOrDefault(key));
            if (value.Length > 0) return BareID(value);
        }
        if (payload.GetValueOrDefault("session") is Dictionary<string, object?> nested)
        {
            var value = Bags.Text(nested.GetValueOrDefault("id"));
            if (value.Length > 0) return BareID(value);
        }
        if (source != "gemini" && payload.GetValueOrDefault("transcript_path") is string path && path.Length > 0)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (stem.Length > 0) return BareID(stem);
        }
        return source + "-session";
    }

    private static string BareID(string value)
    {
        var match = Regex.Match(value, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (match.Success) return match.Value.ToLowerInvariant();
        return value.Length <= 80 ? value : value[..80];
    }

    private static (string Key, Dictionary<string, object?> Item) Find(Dictionary<string, object?> tasks, string id)
    {
        if (tasks.GetValueOrDefault(id) is Dictionary<string, object?> direct) return (id, direct);
        foreach (var pair in tasks)
        {
            if (pair.Value is not Dictionary<string, object?> item) continue;
            if (item.GetValueOrDefault("id") as string == id) return (pair.Key, item);
            if (Bags.Array(item.GetValueOrDefault("aliases")).Select(Bags.Text).Contains(id)) return (pair.Key, item);
        }
        return (id, new Dictionary<string, object?>());
    }

    private static bool IsPermissionRecord(Dictionary<string, object?> record)
    {
        var options = Bags.StringOptions(record.GetValueOrDefault("options"));
        return PermissionCopy.IsPermission(
            Bags.Text(record.GetValueOrDefault("kind")),
            Bags.Text(record.GetValueOrDefault("status")),
            options.Select(option => option.GetValueOrDefault("id") ?? "").ToList());
    }

    private static void Scrub(Dictionary<string, object?> tasks)
    {
        foreach (var key in tasks.Keys.ToList())
        {
            if (tasks[key] is not Dictionary<string, object?> record) continue;
            var prompt = Bags.Text(record.GetValueOrDefault("prompt"));
            var command = Bags.Text(record.GetValueOrDefault("command"));
            foreach (var banned in new[]
            {
                "prompt", "command", "tool_input", "toolInput", "message", "initialPrompt", "initial_prompt",
                "last_assistant_message", "text", "output"
            })
            {
                record.Remove(banned);
            }
            var rawDetail = Bags.Text(record.GetValueOrDefault("detail"));
            var title = Bags.Text(record.GetValueOrDefault("title"));
            if (prompt.Length > 0 && title == prompt) title = "";
            if (command.Length > 0 && title == command) title = "";
            if (PermissionCopy.ContainsDebug(title)) title = "";
            var detail = rawDetail;
            if (detail == prompt || detail == command || PermissionCopy.ContainsDebug(detail)) detail = "";
            var summary = Bags.Text(record.GetValueOrDefault("summary"));
            if (summary.Length > 0) summary = PermissionCopy.Abbreviate(summary);
            if (PermissionCopy.ContainsDebug(summary)) summary = "";
            else if (command.Length > 0 && summary == command) summary = "";
            else if (prompt.Length > 0 && summary == prompt && prompt.Length > 40) summary = "";
            var answer = Bags.Text(record.GetValueOrDefault("answer"));
            if (answer.Length > 0) answer = PermissionCopy.CardAnswer(answer);
            if (PermissionCopy.ContainsDebug(answer)) answer = "";
            else if (command.Length > 0 && answer == command) answer = "";
            var choicePrompt = Bags.Text(record.GetValueOrDefault("choice_prompt"));
            if (choicePrompt.Length > 0) choicePrompt = PermissionCopy.Abbreviate(choicePrompt);
            if (PermissionCopy.ContainsDebug(choicePrompt)) choicePrompt = "";
            if (IsPermissionRecord(record))
            {
                title = PermissionCopy.ActionKind(rawDetail, title);
                if (!PermissionCopy.ActionKinds.Contains(detail)) detail = "";
            }
            else if (title.Length == 0)
            {
                title = SlotFace.DisplayName(Bags.Text(record.GetValueOrDefault("source")));
            }
            record["title"] = title;
            if (detail.Length == 0) record.Remove("detail");
            else record["detail"] = detail;
            if (summary.Length == 0) record.Remove("summary");
            else record["summary"] = summary;
            if (answer.Length == 0) record.Remove("answer");
            else record["answer"] = answer;
            if (choicePrompt.Length == 0 || Bags.Text(record.GetValueOrDefault("status")) != "wait") record.Remove("choice_prompt");
            else record["choice_prompt"] = choicePrompt;
            tasks[key] = record;
        }
    }

    private static Dictionary<string, object?> Dictionary(IReadOnlyDictionary<string, object?> payload, string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.GetValueOrDefault(key) is Dictionary<string, object?> value) return value;
        }
        return new Dictionary<string, object?>();
    }

    private static Dictionary<string, string> CopyOption(Dictionary<string, string> option)
    {
        return new Dictionary<string, string>(option);
    }

    private static HookOutcome Clone(HookOutcome outcome)
    {
        return new HookOutcome
        {
            Id = outcome.Id,
            Source = outcome.Source,
            Title = outcome.Title,
            Status = outcome.Status,
            Kind = outcome.Kind,
            RequestID = outcome.RequestID,
            QuestionID = outcome.QuestionID,
            Options = outcome.Options.Select(CopyOption).ToList(),
            WaitsForPermission = outcome.WaitsForPermission,
            PermissionStyle = outcome.PermissionStyle,
            Detail = outcome.Detail,
            Summary = outcome.Summary,
            Answer = outcome.Answer,
            ChoicePrompt = outcome.ChoicePrompt,
            PromptSubmitted = outcome.PromptSubmitted
        };
    }
}
