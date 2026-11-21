namespace Mochi.Core;

public static class PassiveHooks
{
    public static readonly string[] CopilotEvents =
    [
        "sessionStart", "userPromptSubmitted", "preToolUse", "postToolUse", "subagentStart",
        "permissionRequest", "notification", "postToolUseFailure", "errorOccurred", "agentStop", "sessionEnd"
    ];

    public static readonly string[] GrokEvents =
    [
        "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "SubagentStart",
        "Notification", "PermissionDenied", "PostToolUseFailure", "Stop", "StopFailure", "StopCancelled", "SessionEnd"
    ];

    public static string CopilotDirectory(string home, IReadOnlyDictionary<string, string> environment)
    {
        var overrideHome = environment.TryGetValue("COPILOT_HOME", out var value) ? value.Trim() : "";
        if (overrideHome.Length == 0) return Path.Combine(home, ".copilot", "hooks");
        return Path.Combine(overrideHome, "hooks");
    }

    public static string GrokDirectory(string home) => Path.Combine(home, ".grok", "hooks");

    public static string OpenCodePluginURL(string home) => Path.Combine(home, ".config", "opencode", "plugins", "mochi-island.js");

    public static HookInstallResult InstallCopilot(string directory, string binaryPath, bool privacyAccepted)
    {
        if (!Ready(binaryPath, privacyAccepted))
        {
            return new HookInstallResult(false, binaryPath, error: privacyAccepted ? "hook program missing" : null, needsPrivacy: !privacyAccepted);
        }
        var filePath = Path.Combine(directory, "mochi-island.json");
        FileBits.Backup(filePath);
        var hooks = new Dictionary<string, object?>();
        foreach (var name in CopilotEvents)
        {
            var command = HookInstaller.ShellCommand(binaryPath, "copilot", name);
            hooks[name] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "command",
                    ["bash"] = command,
                    ["command"] = command,
                    ["timeoutSec"] = 10,
                    [AppConfig.HookMarker] = AppConfig.HookVersion
                }
            };
        }
        return WriteHooks(filePath, hooks, 1, binaryPath, "copilot");
    }

    public static HookInstallResult InstallGrok(string directory, string binaryPath, bool privacyAccepted)
    {
        if (!Ready(binaryPath, privacyAccepted))
        {
            return new HookInstallResult(false, binaryPath, error: privacyAccepted ? "hook program missing" : null, needsPrivacy: !privacyAccepted);
        }
        var filePath = Path.Combine(directory, "mochi-island.json");
        FileBits.Backup(filePath);
        var command = HookInstaller.ShellCommand(binaryPath, "grok");
        var entry = new Dictionary<string, object?>
        {
            ["type"] = "command",
            ["command"] = command,
            ["timeout"] = 10,
            [AppConfig.HookMarker] = AppConfig.HookVersion
        };
        var hooks = new Dictionary<string, object?>();
        foreach (var name in GrokEvents)
        {
            hooks[name] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["hooks"] = new List<object?> { entry.ToDictionary(pair => pair.Key, pair => pair.Value) }
                }
            };
        }
        return WriteHooks(filePath, hooks, null, binaryPath, "grok");
    }

    public static HookInstallResult InstallOpenCode(string pluginPath, string binaryPath, bool privacyAccepted)
    {
        if (!privacyAccepted) return new HookInstallResult(false, binaryPath, needsPrivacy: true);
        if (!FileBits.IsExecutable(binaryPath)) return new HookInstallResult(false, binaryPath, error: "hook program missing");
        FileBits.Backup(pluginPath);
        try
        {
            AtomicFile.WriteText(OpenCodePlugin(binaryPath), pluginPath);
        }
        catch (Exception)
        {
            return new HookInstallResult(false, binaryPath, error: "could not write OpenCode plugin");
        }
        return new HookInstallResult(OpenCodeInstalled(pluginPath, binaryPath), binaryPath);
    }

    public static HookInstallResult CopilotStatus(string directory, string binaryPath)
    {
        return JsonStatus(Path.Combine(directory, "mochi-island.json"), binaryPath, "copilot");
    }

    public static HookInstallResult GrokStatus(string directory, string binaryPath)
    {
        return JsonStatus(Path.Combine(directory, "mochi-island.json"), binaryPath, "grok");
    }

    public static HookInstallResult OpenCodeStatus(string pluginPath, string binaryPath)
    {
        return new HookInstallResult(OpenCodeInstalled(pluginPath, binaryPath), binaryPath);
    }

    public static HookInstallResult Remove(string filePath, string binaryPath)
    {
        FileBits.Backup(filePath);
        if (File.Exists(filePath)) File.Delete(filePath);
        return new HookInstallResult(false, binaryPath);
    }

    public static string OpenCodePlugin(string cli)
    {
        var quoted = cli.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return PluginTemplate.Replace("@@CLI@@", quoted, StringComparison.Ordinal);
    }

    private static bool Ready(string binaryPath, bool privacyAccepted)
    {
        return privacyAccepted && FileBits.IsExecutable(binaryPath);
    }

    private static HookInstallResult WriteHooks(string filePath, Dictionary<string, object?> hooks, int? version, string binaryPath, string source)
    {
        var root = new Dictionary<string, object?> { ["hooks"] = hooks };
        if (version is int number) root["version"] = number;
        try { JsonFile.Write(root, filePath); }
        catch (Exception) { return new HookInstallResult(false, binaryPath, error: "could not write hooks"); }
        return JsonStatus(filePath, binaryPath, source);
    }

    private static HookInstallResult JsonStatus(string filePath, string binaryPath, string source)
    {
        if (!FileBits.IsExecutable(binaryPath) || !File.Exists(filePath)) return new HookInstallResult(false, binaryPath);
        var root = JsonFile.Read(filePath);
        var hooks = Bags.Object(root.GetValueOrDefault("hooks"));
        if (hooks.Count == 0) return new HookInstallResult(false, binaryPath);
        var needle = "--source " + source;
        foreach (var raw in hooks.Values)
        {
            foreach (var entry in CommandEntries(raw))
            {
                var command = Bags.Text(entry.GetValueOrDefault("command"));
                var marked = entry.ContainsKey(AppConfig.HookMarker);
                if (marked && command.Contains(binaryPath, StringComparison.Ordinal) && command.Contains(needle, StringComparison.Ordinal))
                {
                    return new HookInstallResult(true, binaryPath);
                }
            }
        }
        return new HookInstallResult(false, binaryPath);
    }

    private static List<Dictionary<string, object?>> CommandEntries(object? raw)
    {
        var found = new List<Dictionary<string, object?>>();
        foreach (var item in Bags.Array(raw))
        {
            var bag = Bags.Object(item);
            if (bag.Count == 0) continue;
            if (bag.ContainsKey("hooks"))
            {
                foreach (var inner in Bags.Array(bag["hooks"]))
                {
                    var entry = Bags.Object(inner);
                    if (entry.Count > 0) found.Add(entry);
                }
            }
            else
            {
                found.Add(bag);
            }
        }
        return found;
    }

    private static bool OpenCodeInstalled(string pluginPath, string binaryPath)
    {
        if (!FileBits.IsExecutable(binaryPath) || !File.Exists(pluginPath)) return false;
        var text = File.ReadAllText(pluginPath);
        return text.Contains("mochi-island", StringComparison.Ordinal)
            && text.Contains(binaryPath, StringComparison.Ordinal)
            && text.Contains("\"--source\", \"opencode\"", StringComparison.Ordinal)
            && text.Contains("session.status", StringComparison.Ordinal)
            && text.Contains("tool.execute.before", StringComparison.Ordinal)
            && text.Contains("permission.asked", StringComparison.Ordinal)
            && text.Contains("question.asked", StringComparison.Ordinal)
            && text.Contains("session.error", StringComparison.Ordinal)
            && text.Contains("spawnSync", StringComparison.Ordinal)
            && !text.Contains("message.updated", StringComparison.Ordinal)
            && !text.Contains("message.part", StringComparison.Ordinal)
            && !text.Contains("http://", StringComparison.Ordinal)
            && !text.Contains("https://", StringComparison.Ordinal);
    }

    private const string PluginTemplate = """
        // mochi-island
        // Passive status plugin. It does not read message text or the local event stream.
        import { spawnSync } from "node:child_process"

        const cli = "@@CLI@@"

        function text(value) {
          return typeof value === "string" ? value : ""
        }

        function sessionID(input) {
          if (!input || typeof input !== "object") return ""
          const direct = text(input.sessionID || input.session_id || input.sessionId)
          if (direct) return direct
          const session = input.session
          if (session && typeof session === "object") return text(session.id)
          return ""
        }

        function statusType(input) {
          if (!input || typeof input !== "object") return ""
          const status = input.status
          if (status && typeof status === "object") return text(status.type)
          const kind = text(input.type)
          return kind === "busy" || kind === "idle" || kind === "retry" ? kind : ""
        }

        function report(event, fields) {
          const payload = JSON.stringify({
            hook_event_name: event,
            session_id: fields.session_id || "",
            status_type: fields.status_type || ""
          })
          try {
            spawnSync(cli, ["--source", "opencode"], { input: payload, encoding: "utf8", timeout: 8000 })
          } catch (_) {}
        }

        const watched = new Set([
          "session.status",
          "permission.asked",
          "permission.replied",
          "question.asked",
          "question.replied",
          "question.rejected",
          "session.idle",
          "session.error"
        ])

        export const MochiIsland = async () => ({
          event: async ({ event }) => {
            const type = text(event && event.type)
            if (!watched.has(type)) return
            const props = (event && event.properties) || {}
            report(type, { session_id: sessionID(props), status_type: statusType(props) })
          },
          "tool.execute.before": async (input) => {
            report("tool.execute.before", { session_id: sessionID(input) })
          }
        })

        export default MochiIsland
        """;
}
