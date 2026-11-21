using System.Security.Cryptography;
using System.Text;

namespace Mochi.Core;

public static class CodexFeatureFlag
{
    public static string? EnablingHooks(string contents)
    {
        var newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = SplitLines(contents);
        int? featuresHeader = null;
        int? featuresHooks = null;
        int? rootDotted = null;
        var inFeatures = false;
        var insideTable = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith('['))
            {
                inFeatures = IsFeaturesHeader(trimmed);
                insideTable = true;
                if (inFeatures)
                {
                    if (featuresHeader != null) return null;
                    featuresHeader = index;
                }
                continue;
            }
            var assignment = Assignment(lines[index]);
            if (assignment == null) continue;
            if (inFeatures)
            {
                if (assignment.Value.Key != "hooks") continue;
                if (featuresHooks != null) return null;
                if (assignment.Value.Value is not ("true" or "false")) return null;
                featuresHooks = index;
                continue;
            }
            if (!insideTable && assignment.Value.Key is "features.hooks" or "features.codex_hooks")
            {
                if (rootDotted != null) return null;
                if (assignment.Value.Value is not ("true" or "false")) return null;
                rootDotted = index;
            }
        }
        if (featuresHeader != null && rootDotted != null) return null;
        if (featuresHooks is int hookLine)
        {
            lines[hookLine] = ReplacingValue(lines[hookLine], "true");
            return Join(lines, newline);
        }
        if (rootDotted is int rootLine)
        {
            var key = Assignment(lines[rootLine])?.Key;
            lines[rootLine] = key == "features.codex_hooks"
                ? ReplacingKey(lines[rootLine], "features.hooks", "true")
                : ReplacingValue(lines[rootLine], "true");
            return Join(lines, newline);
        }
        if (featuresHeader is int header)
        {
            lines.Insert(header + 1, "hooks = true");
            return Join(lines, newline);
        }
        var text = contents;
        if (text.Length > 0 && !text.EndsWith('\n')) text += newline;
        if (text.Length > 0) text += newline;
        text += "[features]" + newline + "hooks = true" + newline;
        return text;
    }

    public static bool IsEnabled(string contents)
    {
        var inFeatures = false;
        var insideTable = false;
        string? tableValue = null;
        string? rootValue = null;
        var sawTable = false;
        var sawRoot = false;
        foreach (var raw in contents.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith('['))
            {
                inFeatures = IsFeaturesHeader(line);
                insideTable = true;
                if (inFeatures) sawTable = true;
                continue;
            }
            var assignment = Assignment(raw);
            if (assignment == null) continue;
            if (inFeatures && assignment.Value.Key == "hooks") tableValue = assignment.Value.Value;
            else if (!insideTable && assignment.Value.Key is "features.hooks" or "features.codex_hooks")
            {
                rootValue = assignment.Value.Value;
                sawRoot = true;
            }
        }
        if (sawTable && sawRoot) return false;
        return tableValue == "true" || rootValue == "true";
    }

    private static bool IsFeaturesHeader(string line)
    {
        var text = line.Trim();
        if (!text.StartsWith('[') || text.StartsWith("[[", StringComparison.Ordinal)) return false;
        text = text[1..];
        var end = text.IndexOf(']');
        if (end < 0) return false;
        return text[..end].Trim() == "features";
    }

    private static (string Key, string Value)? Assignment(string line)
    {
        var trimmed = line.Trim();
        var eq = trimmed.IndexOf('=');
        if (eq < 0) return null;
        var key = trimmed[..eq].Trim();
        var value = trimmed[(eq + 1)..].Trim();
        var hash = value.IndexOf('#');
        if (hash >= 0) value = value[..hash].Trim();
        if (key.Length == 0 || value.Length == 0) return null;
        return (key, value);
    }

    private static string ReplacingValue(string line, string value)
    {
        var eq = line.IndexOf('=');
        if (eq < 0) return line;
        var head = line[..(eq + 1)];
        var rest = line[(eq + 1)..];
        var spaces = rest.Length - rest.TrimStart(' ', '\t').Length;
        var body = rest[spaces..];
        string? token = body.StartsWith("true", StringComparison.Ordinal) ? "true"
            : body.StartsWith("false", StringComparison.Ordinal) ? "false" : null;
        if (token == null) return line;
        return head + rest[..spaces] + value + body[token.Length..];
    }

    private static string ReplacingKey(string line, string key, string value)
    {
        if (!line.Contains('=')) return line;
        var indentLength = line.Length - line.TrimStart(' ', '\t').Length;
        var indent = line[..indentLength];
        var tail = ReplacingValue(line, value);
        var tailEq = tail.IndexOf('=');
        if (tailEq < 0) return line;
        return indent + key + tail[tailEq..];
    }

    private static List<string> SplitLines(string contents)
    {
        var lines = contents.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        if ((contents.EndsWith('\n') || contents.EndsWith("\r\n", StringComparison.Ordinal)) && lines.Count > 0 && lines[^1] == "")
        {
            lines.RemoveAt(lines.Count - 1);
        }
        return lines;
    }

    private static string Join(List<string> lines, string newline)
    {
        var text = string.Join(newline, lines);
        if (!text.EndsWith('\n')) text += newline;
        return text;
    }
}

public static class CodexHookTrust
{
    public static string? EventKey(string pascalName)
    {
        return pascalName switch
        {
            "PreToolUse" => "pre_tool_use",
            "PermissionRequest" => "permission_request",
            "PostToolUse" => "post_tool_use",
            "PreCompact" => "pre_compact",
            "PostCompact" => "post_compact",
            "PreModelRequest" => "pre_model_request",
            "PostModelRequest" => "post_model_request",
            "SessionStart" => "session_start",
            "UserPromptSubmit" => "user_prompt_submit",
            "SubagentStart" => "subagent_start",
            "SubagentStop" => "subagent_stop",
            "Stop" => "stop",
            "SessionEnd" => "session_end",
            "Interrupt" => "interrupt",
            _ => null
        };
    }

    public static int NormalizedTimeout(string eventKey, int fileTimeout)
    {
        if (eventKey is "session_end" or "interrupt") return Math.Min(Math.Max(fileTimeout, 1), 3);
        return Math.Max(fileTimeout, 1);
    }

    public static string IdentityHash(string eventKey, string command, int fileTimeout)
    {
        var timeout = NormalizedTimeout(eventKey, fileTimeout);
        var json = "{\"event_name\":\"" + eventKey + "\",\"hooks\":[{\"async\":false,\"command\":" + JsonString(command) + ",\"timeout\":" + timeout + ",\"type\":\"command\"}]}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static string ConfigByTrusting(string contents, IReadOnlyList<(string Key, string Hash)> entries)
    {
        if (entries.Count == 0) return contents;
        if (AlreadyTrusts(contents, entries)) return contents;
        var newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var keys = entries.Select(entry => entry.Key).ToHashSet();
        var lines = contents.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        if ((contents.EndsWith('\n') || contents.EndsWith("\r\n", StringComparison.Ordinal)) && lines.Count > 0 && lines[^1] == "")
        {
            lines.RemoveAt(lines.Count - 1);
        }
        var kept = new List<string>();
        var index = 0;
        while (index < lines.Count)
        {
            var state = StateKey(lines[index]);
            if (state != null && keys.Contains(state))
            {
                index += 1;
                while (index < lines.Count && StateKey(lines[index]) == null && !IsTableHeader(lines[index])) index += 1;
                continue;
            }
            kept.Add(lines[index]);
            index += 1;
        }
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1])) kept.RemoveAt(kept.Count - 1);
        var text = string.Join(newline, kept);
        if (text.Length > 0) text += newline + newline;
        foreach (var entry in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            text += "[hooks.state.\"" + Escaped(entry.Key) + "\"]" + newline;
            text += "enabled = true" + newline;
            text += "trusted_hash = \"" + entry.Hash + "\"" + newline;
            text += newline;
        }
        return text;
    }

    private static bool AlreadyTrusts(string contents, IReadOnlyList<(string Key, string Hash)> entries)
    {
        foreach (var entry in entries)
        {
            var body = SectionBody(contents, entry.Key);
            if (body == null) return false;
            if (!body.Contains("trusted_hash = \"" + entry.Hash + "\"", StringComparison.Ordinal)) return false;
            if (body.Contains("enabled = false", StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static string? SectionBody(string contents, string key)
    {
        var lines = contents.Split(["\r\n", "\n"], StringSplitOptions.None);
        var start = Array.FindIndex(lines, line => StateKey(line) == key);
        if (start < 0) return null;
        var body = new List<string>();
        for (var index = start + 1; index < lines.Length && StateKey(lines[index]) == null && !IsTableHeader(lines[index]); index++)
        {
            body.Add(lines[index]);
        }
        return string.Join("\n", body);
    }

    private static string? StateKey(string line)
    {
        var trimmed = line.Trim();
        const string prefix = "[hooks.state.\"";
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal) || !trimmed.EndsWith("\"]", StringComparison.Ordinal)) return null;
        var start = prefix.Length;
        var end = trimmed.Length - 2;
        if (start > end) return null;
        return trimmed[start..end].Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    private static bool IsTableHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('[') && trimmed.EndsWith(']');
    }

    private static string Escaped(string key) => key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    public static string JsonString(string value)
    {
        var encoded = new StringBuilder();
        encoded.Append('"');
        foreach (var scalar in value)
        {
            switch (scalar)
            {
                case '\\': encoded.Append("\\\\"); break;
                case '"': encoded.Append("\\\""); break;
                case '\n': encoded.Append("\\n"); break;
                case '\r': encoded.Append("\\r"); break;
                case '\t': encoded.Append("\\t"); break;
                default:
                    if (scalar < 0x20) encoded.Append("\\u").Append(((int)scalar).ToString("x4"));
                    else encoded.Append(scalar);
                    break;
            }
        }
        encoded.Append('"');
        return encoded.ToString();
    }
}
