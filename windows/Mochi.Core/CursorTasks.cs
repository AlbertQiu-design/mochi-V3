using System.Globalization;

namespace Mochi.Core;

public sealed class CursorTask
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Status { get; set; }
    public string Kind { get; set; }
    public string Prompt { get; set; }
    public string Detail { get; set; }
    public string Summary { get; set; }
    public string Answer { get; set; }
    public string ChoicePrompt { get; set; }
    public List<Dictionary<string, string>> Options { get; set; }
    public bool AllowMultiple { get; set; }
    public string QuestionID { get; set; }
    public string RequestID { get; set; }
    public string Source { get; set; }
    public double Updated { get; set; }
    public int SubmitGen { get; set; }

    public CursorTask(
        string id,
        string title,
        string status,
        string kind = "",
        string prompt = "",
        string detail = "",
        string summary = "",
        string answer = "",
        string choicePrompt = "",
        List<Dictionary<string, string>>? options = null,
        bool allowMultiple = false,
        string questionID = "",
        string requestID = "",
        string source = "cursor",
        double updated = 0,
        int submitGen = 0)
    {
        Id = id;
        Title = title;
        Status = status;
        Kind = kind;
        Prompt = prompt;
        Detail = detail;
        Summary = summary;
        Answer = answer;
        ChoicePrompt = choicePrompt;
        Options = options ?? [];
        AllowMultiple = allowMultiple;
        QuestionID = questionID;
        RequestID = requestID;
        Source = AgentLaunch.Normalize(source);
        Updated = updated;
        SubmitGen = Math.Max(0, submitGen);
    }

    public Dictionary<string, object?> Dictionary()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["title"] = Title,
            ["status"] = Status,
            ["source"] = Source,
            ["kind"] = Kind,
            ["prompt"] = Prompt,
            ["detail"] = Detail,
            ["summary"] = Summary,
            ["answer"] = Answer,
            ["choice_prompt"] = ChoicePrompt,
            ["options"] = Options.Select(option => (object?)option.ToDictionary(pair => pair.Key, pair => (object?)pair.Value)).ToList(),
            ["allow_multiple"] = AllowMultiple,
            ["question_id"] = QuestionID,
            ["request_id"] = RequestID,
            ["updated"] = Updated,
            ["submit_gen"] = SubmitGen
        };
    }
}

public sealed class TaskTransitionTracker
{
    public Dictionary<string, string> StatusByID { get; } = new();

    public List<string> NewlyCompleted(IReadOnlyList<CursorTask> tasks)
    {
        var feed = new List<string>();
        var next = new Dictionary<string, string>();
        foreach (var task in tasks)
        {
            next[task.Id] = task.Status;
            if (StatusByID.TryGetValue(task.Id, out var previous)
                && (previous == "running" || previous == "wait")
                && task.Status == "done")
            {
                feed.Add(task.Id);
            }
        }
        StatusByID.Clear();
        foreach (var pair in next) StatusByID[pair.Key] = pair.Value;
        return feed;
    }
}

public static class TaskReader
{
    public static List<CursorTask> Load(string tasksPath, double? now = null, bool includePrompts = true)
    {
        var clock = now ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        var root = JsonFile.Read(tasksPath);
        if (!root.TryGetValue("tasks", out var rawTasks)) return [];
        var tasks = Bags.Object(rawTasks);
        if (tasks.Count == 0 && rawTasks is not Dictionary<string, object?>) return [];
        var answered = new HashSet<string>(Bags.Array(root.GetValueOrDefault("answered")).Select(Bags.Text));
        var visible = new List<CursorTask>();
        foreach (var pair in tasks)
        {
            var item = Bags.Object(pair.Value);
            if (item.Count == 0 && pair.Value is not Dictionary<string, object?>) continue;
            var status = Bags.Text(item.GetValueOrDefault("status"));
            if (status.Length == 0) status = "running";
            if (status == "closed") continue;
            if (status is not ("running" or "wait" or "done" or "fail")) continue;
            var updated = Timestamp(item.GetValueOrDefault("updated"));
            if ((status == "done" || status == "fail") && updated > 0 && clock - updated > AppConfig.DoneKeep) continue;
            var id = Bags.Text(item.GetValueOrDefault("id"));
            if (id.Length == 0) id = pair.Key;
            var options = new List<Dictionary<string, string>>();
            foreach (var optionRaw in Bags.Array(item.GetValueOrDefault("options")))
            {
                var option = Bags.Object(optionRaw);
                var oid = Bags.Text(option.GetValueOrDefault("id"));
                var label = Bags.Text(option.GetValueOrDefault("label"));
                if (label.Length == 0) continue;
                options.Add(new Dictionary<string, string> { ["id"] = oid, ["label"] = label });
            }
            var optionIDs = options.Select(option => option["id"]).ToList();
            var kindRaw = Bags.Text(item.GetValueOrDefault("kind"));
            var requestID = Bags.Text(item.GetValueOrDefault("request_id"));
            var source = AgentLaunch.Normalize(Bags.Text(item.GetValueOrDefault("source")));
            var permission = PermissionCopy.IsPermission(kindRaw, status, optionIDs);
            var answeredWait = permission && status == "wait" && requestID.Length > 0 && answered.Contains(requestID);
            if (answeredWait) status = "running";
            var rawPrompt = Bags.Text(item.GetValueOrDefault("prompt"));
            var rawCommand = Bags.Text(item.GetValueOrDefault("command"));
            var title = Bags.Text(item.GetValueOrDefault("title"));
            if ((!string.IsNullOrEmpty(rawPrompt) && title == rawPrompt) || (!string.IsNullOrEmpty(rawCommand) && title == rawCommand))
            {
                title = "";
            }
            var rawDetail = Bags.Text(item.GetValueOrDefault("detail"));
            var rawSummary = Bags.Text(item.GetValueOrDefault("summary"));
            var rawAnswer = Bags.Text(item.GetValueOrDefault("answer"));
            var rawChoice = Bags.Text(item.GetValueOrDefault("choice_prompt"));
            var agent = SlotFace.DisplayName(source);
            string shownKind;
            string shownDetail;
            string shownSummary;
            string shownAnswer;
            string shownChoice;
            List<Dictionary<string, string>> shownOptions;
            if (permission && answeredWait)
            {
                title = agent;
                shownKind = "";
                shownDetail = "";
                shownSummary = includePrompts ? PermissionCopy.Abbreviate(rawSummary) : "";
                shownAnswer = includePrompts ? PermissionCopy.CardAnswer(rawAnswer) : "";
                shownChoice = "";
                shownOptions = [];
            }
            else if (permission)
            {
                title = PermissionCopy.ActionKind(rawDetail, title);
                shownKind = "permission";
                shownDetail = "";
                shownSummary = PermissionCopy.Abbreviate(rawSummary);
                shownAnswer = PermissionCopy.CardAnswer(rawAnswer);
                shownChoice = "";
                shownOptions = options;
            }
            else
            {
                title = PermissionCopy.VisibleTitle("", status, title.Length == 0 ? agent : title, rawDetail, agent, []);
                shownKind = includePrompts ? kindRaw : "";
                shownDetail = includePrompts ? PermissionCopy.VisibleDetail("", status, rawDetail, []) : "";
                shownSummary = includePrompts ? PermissionCopy.Abbreviate(rawSummary) : "";
                shownAnswer = includePrompts ? PermissionCopy.CardAnswer(rawAnswer) : "";
                shownChoice = includePrompts && status == "wait" ? PermissionCopy.Abbreviate(rawChoice) : "";
                shownOptions = options;
            }
            visible.Add(new CursorTask(
                id, title, status, shownKind, "", shownDetail, shownSummary, shownAnswer, shownChoice,
                shownOptions,
                Bags.Flag(item.GetValueOrDefault("allow_multiple"), false),
                Bags.Text(item.GetValueOrDefault("question_id")),
                requestID,
                source,
                updated,
                Bags.Whole(item.GetValueOrDefault("submit_gen"))));
        }
        visible.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
        return visible;
    }

    public static double Timestamp(object? value)
    {
        switch (value)
        {
            case int whole: return whole;
            case long wide: return wide;
            case double real: return real;
            case float single: return single;
            case string text:
                var trimmed = text.Trim();
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
                if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                {
                    return date.ToUnixTimeMilliseconds() / 1000.0;
                }
                return 0;
            default:
                return 0;
        }
    }
}

public static class TranscriptHints
{
    public const double RefreshInterval = 5;

    public static bool Due(double lastScan, double now, bool enabled)
    {
        if (!enabled) return false;
        if (lastScan <= 0) return true;
        return now - lastScan >= RefreshInterval;
    }

    public static List<CursorTask> Merge(IReadOnlyList<CursorTask> tasks, IReadOnlyList<CursorTask> hints, bool enabled)
    {
        if (!enabled || hints.Count == 0) return tasks.ToList();
        var known = new HashSet<string>(tasks.Select(task => task.Id));
        var combined = tasks.ToList();
        foreach (var hint in hints)
        {
            if (!known.Contains(hint.Id)) combined.Add(hint);
        }
        combined.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
        return combined;
    }
}

public static class ReplyWriter
{
    public static void WritePermission(string id, string requestID, string action, string path)
    {
        WriteChoice(id, requestID, action, "permission", path);
    }

    public static void AnswerPermission(
        string id,
        string requestID,
        string action,
        string kind,
        string replyPath,
        string tasksPath,
        double? now = null)
    {
        WriteChoice(id, requestID, action, kind, replyPath);
        if (PermissionCopy.AnswersPermission(kind, action))
        {
            HookEvents.Acknowledge(id, requestID, tasksPath, now ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        }
    }

    public static void WriteChoice(string id, string requestID, string action, string kind, string path)
    {
        var trimmed = action.Trim();
        var normalizedKind = kind.Trim();
        var lower = trimmed.ToLowerInvariant();
        var permission = normalizedKind == "permission" || (normalizedKind.Length == 0 && (lower == "allow" || lower == "deny"));
        string stored;
        if (permission)
        {
            stored = lower;
            if (stored is not ("allow" or "deny")) throw new ReplyError();
        }
        else
        {
            if (trimmed.Length == 0) throw new ReplyError();
            stored = trimmed;
        }
        JsonFile.Write(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["request_id"] = requestID,
            ["action"] = stored,
            ["option_id"] = stored,
            ["kind"] = permission ? "permission" : normalizedKind
        }, path);
    }

    public static bool ClosesBubble(string kind, string optionID)
    {
        if (kind.Trim() == "permission") return true;
        return optionID.Trim().ToLowerInvariant() is "allow" or "deny";
    }

    public sealed class ReplyError : Exception
    {
    }
}
