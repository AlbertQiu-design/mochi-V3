using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mochi.Core;

/// <summary>
/// The Windows Grok Bot app does not run hook files, just like the macOS app. Its
/// electron store under %APPDATA%\Grok Bot\sand-client-persistence carries the same
/// `value.entries` shape, with `send-message` holding the bot's reply and
/// `message`/role=user holding what the user asked. While Grok is connected we copy
/// recent turns into the shared task file so the capsule can show its state.
/// </summary>
public static class GrokBotSession
{
    public const string Origin = "grok-session";

    public static string Directory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Grok Bot",
        "sand-client-persistence");

    public static bool Available() => System.IO.Directory.Exists(Directory());

    public static void Sync(string tasksPath, double now, string? directory = null, double maxAge = AppConfig.IconMaxAge)
    {
        directory ??= Directory();
        if (!System.IO.Directory.Exists(directory)) return;
        var turns = ReadTurns(directory, now, maxAge);
        FileStream? gate = null;
        try
        {
            gate = new FileStream(tasksPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch
        {
            gate = null;
            return;
        }
        try
        {
            var root = JsonFile.Read(tasksPath);
            var tasks = Bags.Object(root.GetValueOrDefault("tasks"));
            var before = JsonFile.Serialize(tasks);
            var incoming = turns.Select(turn => turn.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var key in tasks.Keys.ToList())
            {
                var record = Bags.Object(tasks[key]);
                if (Bags.Text(record.GetValueOrDefault("origin")) != Origin) continue;
                var id = Bags.Text(record.GetValueOrDefault("id"));
                if (id.Length == 0) id = key;
                if (!incoming.Contains(id)) tasks.Remove(key);
            }
            foreach (var turn in turns)
            {
                var record = new Dictionary<string, object?>
                {
                    ["id"] = turn.Id,
                    ["title"] = "Grok Bot",
                    ["status"] = turn.Status,
                    ["source"] = "grok-bot",
                    ["updated"] = turn.Updated,
                    ["detail"] = "Chat",
                    ["origin"] = Origin
                };
                if (turn.Summary.Length > 0) record["summary"] = turn.Summary;
                if (turn.Answer.Length > 0) record["answer"] = turn.Answer;
                tasks[turn.Id] = record;
            }
            if (JsonFile.Serialize(tasks) == before) return;
            root["tasks"] = tasks;
            AtomicFile.WriteText(JsonFile.Serialize(root), tasksPath);
        }
        finally
        {
            gate?.Dispose();
        }
    }

    private sealed record Turn(string Id, string Summary, string Answer, string Status, double Updated);

    private static List<Turn> ReadTurns(string directory, double now, double maxAge)
    {
        var turns = new List<Turn>();
        string[] files;
        try { files = System.IO.Directory.GetFiles(directory, "*.blob"); }
        catch { return turns; }
        foreach (var file in files)
        {
            string text;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                text = reader.ReadToEnd();
            }
            catch
            {
                continue;
            }
            if (!text.Contains("send-message", StringComparison.Ordinal)) continue;
            try
            {
                using var document = JsonDocument.Parse(text);
                if (!document.RootElement.TryGetProperty("value", out var value)) continue;
                if (!value.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) continue;
                var turn = TurnFrom(entries, StableId(Path.GetFileName(file)), now, maxAge);
                if (turn != null) turns.Add(turn);
            }
            catch
            {
                // A blob can rotate mid-read; skip it.
            }
        }
        return turns;
    }

    private static Turn? TurnFrom(JsonElement entries, string id, double now, double maxAge)
    {
        var summary = "";
        var answer = "";
        var askedAt = 0.0;
        var answeredAt = 0.0;
        var lastSpeaker = "";
        var streaming = false;
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var kind = Text(entry, "kind");
            var at = Seconds(entry, "timestampMs");
            if (kind == "message")
            {
                var role = Text(entry, "role");
                var content = Trimmed(entry, "content");
                if (role == "user")
                {
                    if (content.Length == 0) continue;
                    summary = content;
                    askedAt = at;
                    lastSpeaker = "user";
                    streaming = false;
                }
                else if (role == "assistant")
                {
                    if (content.Length > 0) answer = content;
                    answeredAt = at;
                    lastSpeaker = "assistant";
                    streaming = entry.TryGetProperty("isStreaming", out var flag) && flag.ValueKind == JsonValueKind.True;
                }
                continue;
            }
            if (kind == "send-message")
            {
                var content = "";
                if (entry.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                {
                    content = Trimmed(message, "content");
                }
                if (content.Length > 0)
                {
                    answer = content;
                    answeredAt = at;
                }
                lastSpeaker = "assistant";
                streaming = entry.TryGetProperty("isStreaming", out var flag) && flag.ValueKind == JsonValueKind.True;
            }
        }
        if (summary.Length == 0 || lastSpeaker.Length == 0) return null;
        var waiting = lastSpeaker == "user" || streaming;
        var updated = waiting ? askedAt : Math.Max(askedAt, answeredAt);
        if (updated <= 0) return null;
        if (waiting)
        {
            if (now - updated > AppConfig.LiveKeep) return null;
        }
        else if (now - updated > maxAge)
        {
            return null;
        }
        var storedAnswer = waiting && lastSpeaker == "user" ? "" : PermissionCopy.CardAnswer(answer);
        return new Turn(
            id,
            PermissionCopy.Abbreviate(summary),
            storedAnswer,
            waiting ? "running" : "done",
            waiting ? now : updated);
    }

    private static string Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static string Trimmed(JsonElement element, string name)
    {
        var text = Text(element, name).Trim();
        return text;
    }

    private static double Seconds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) return 0;
        var raw = value.GetDouble();
        return raw > 1_000_000_000_000 ? raw / 1000 : raw > 10_000_000_000 ? raw / 1000 : raw;
    }

    private static string StableId(string name)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        var hex = Convert.ToHexString(digest).ToLowerInvariant();
        return hex[..8] + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-" + hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
    }
}
