namespace Mochi.Core;

public sealed class HookInstallResult
{
    public bool Connected { get; set; }
    public string Hook { get; set; } = "";
    public int Tasks { get; set; }
    public string? Error { get; set; }
    public bool NeedsPrivacy { get; set; }

    public HookInstallResult(bool connected, string hook, int tasks = 0, string? error = null, bool needsPrivacy = false)
    {
        Connected = connected;
        Hook = hook;
        Tasks = tasks;
        Error = error;
        NeedsPrivacy = needsPrivacy;
    }
}

public static class HookInstaller
{
    public static readonly string[] Events =
    [
        "beforeSubmitPrompt", "sessionStart", "subagentStart", "preToolUse", "postToolUse", "postToolUseFailure",
        "beforeShellExecution", "afterShellExecution", "beforeMCPExecution", "afterMCPExecution",
        "afterAgentThought", "afterAgentResponse", "afterFileEdit", "stop", "sessionEnd", "subagentStop"
    ];

    public static readonly string[] CommandEvents =
    [
        "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure",
        "PermissionRequest", "Notification", "Stop", "StopFailure", "SubagentStart", "SubagentStop", "SessionEnd"
    ];

    public static readonly string[] GeminiEvents =
    [
        "SessionStart", "BeforeAgent", "BeforeModel", "AfterModel", "BeforeToolSelection",
        "BeforeTool", "AfterTool", "Notification", "AfterAgent", "SessionEnd"
    ];

    private static readonly HashSet<string> PermissionEvents = ["beforeShellExecution", "beforeMCPExecution"];

    public static HookInstallResult Status(string hooksPath, string binaryPath, string tasksPath)
    {
        var connected = IsInstalled(hooksPath, binaryPath);
        var count = connected ? TaskReader.Load(tasksPath, includePrompts: false).Count : 0;
        return new HookInstallResult(connected, binaryPath, count);
    }

    public static HookInstallResult Install(string hooksPath, string binaryPath, string tasksPath, bool privacyAccepted)
    {
        if (!privacyAccepted) return new HookInstallResult(false, binaryPath, needsPrivacy: true);
        if (!FileBits.IsExecutable(binaryPath)) return new HookInstallResult(false, binaryPath, error: "hook program missing");
        FileBits.Backup(hooksPath);
        var root = JsonFile.Read(hooksPath);
        var hooks = Bags.Object(root.GetValueOrDefault("hooks"));
        var command = CursorCommand(binaryPath);
        foreach (var name in Events)
        {
            var entries = Rows(hooks.GetValueOrDefault(name)).Where(entry => !IsOurs(entry)).ToList();
            var entry = new Dictionary<string, object?>
            {
                ["command"] = command,
                [AppConfig.HookMarker] = AppConfig.HookVersion
            };
            if (PermissionEvents.Contains(name)) entry["timeout"] = 300;
            entries.Insert(0, entry);
            hooks[name] = entries.Select(item => (object?)item).ToList();
        }
        root["version"] = 1;
        root["hooks"] = hooks;
        try
        {
            JsonFile.Write(root, hooksPath);
        }
        catch (Exception)
        {
            return new HookInstallResult(false, binaryPath, error: "could not write hooks");
        }
        return Status(hooksPath, binaryPath, tasksPath);
    }

    public static HookInstallResult Remove(string hooksPath, string binaryPath, string tasksPath)
    {
        FileBits.Backup(hooksPath);
        var root = JsonFile.Read(hooksPath);
        var hooks = Bags.Object(root.GetValueOrDefault("hooks"));
        var cleaned = new Dictionary<string, object?>();
        foreach (var pair in hooks)
        {
            var entries = Rows(pair.Value).Where(entry => !IsOurs(entry)).ToList();
            if (entries.Count > 0) cleaned[pair.Key] = entries.Select(item => (object?)item).ToList();
        }
        root["hooks"] = cleaned;
        try { JsonFile.Write(root, hooksPath); } catch (Exception) { }
        return Status(hooksPath, binaryPath, tasksPath);
    }

    public static bool RetargetCursor(string hooksPath, string binaryPath)
    {
        if (!FileBits.IsExecutable(binaryPath)) return false;
        var root = JsonFile.Read(hooksPath);
        if (root.GetValueOrDefault("hooks") is not Dictionary<string, object?> hooks || hooks.Count == 0) return false;
        var changed = false;
        foreach (var pair in hooks.ToList())
        {
            var entries = Rows(pair.Value);
            if (entries.Count == 0 && pair.Value is not List<object?>) continue;
            for (var index = 0; index < entries.Count; index++)
            {
                if (!IsOurs(entries[index])) continue;
                var command = Bags.Text(entries[index].GetValueOrDefault("command"));
                if (CommandPoints(binaryPath, command)) continue;
                entries[index]["command"] = binaryPath;
                entries[index][AppConfig.HookMarker] = AppConfig.HookVersion;
                changed = true;
            }
            hooks[pair.Key] = entries.Select(item => (object?)item).ToList();
        }
        if (!changed) return IsInstalled(hooksPath, binaryPath);
        root["hooks"] = hooks;
        FileBits.Backup(hooksPath);
        try { JsonFile.Write(root, hooksPath); }
        catch (Exception) { return false; }
        return IsInstalled(hooksPath, binaryPath);
    }

    public static HookInstallResult InstallCodex(string configPath, string hooksPath, string binaryPath, bool privacyAccepted)
    {
        if (!privacyAccepted) return new HookInstallResult(false, binaryPath, needsPrivacy: true);
        if (!FileBits.IsExecutable(binaryPath)) return new HookInstallResult(false, binaryPath, error: "hook program missing");
        var original = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
        var updated = CodexFeatureFlag.EnablingHooks(original);
        if (updated == null) return new HookInstallResult(false, binaryPath, error: "could not enable Codex hooks");
        FileBits.Backup(configPath);
        try
        {
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            AtomicFile.WriteText(updated, configPath);
        }
        catch (Exception)
        {
            return new HookInstallResult(false, binaryPath, error: "could not write Codex config");
        }
        var installed = InstallNested(hooksPath, ShellCommand(binaryPath, "codex"), binaryPath, 300);
        var config = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
        if (!installed.Connected || !CodexFeatureFlag.IsEnabled(config))
        {
            return new HookInstallResult(false, binaryPath, error: installed.Error ?? "Codex hook was not installed");
        }
        if (!TrustCodex(configPath, hooksPath, binaryPath))
        {
            return new HookInstallResult(false, binaryPath, error: "could not trust Codex hooks");
        }
        return new HookInstallResult(true, binaryPath);
    }

    public static HookInstallResult InstallGemini(string settingsPath, string binaryPath, bool privacyAccepted)
    {
        if (!privacyAccepted) return new HookInstallResult(false, binaryPath, needsPrivacy: true);
        if (!FileBits.IsExecutable(binaryPath)) return new HookInstallResult(false, binaryPath, error: "hook program missing");
        if (File.Exists(settingsPath))
        {
            var data = File.ReadAllBytes(settingsPath);
            if (data.Length > 0 && !IsJsonObject(data)) return new HookInstallResult(false, binaryPath, error: "could not read hooks");
        }
        FileBits.Backup(settingsPath);
        var root = JsonFile.Read(settingsPath);
        var hooks = Bags.Object(root.GetValueOrDefault("hooks"));
        var command = ShellCommand(binaryPath, "gemini");
        var entry = new Dictionary<string, object?>
        {
            ["type"] = "command",
            ["command"] = command,
            ["timeout"] = 10000,
            [AppConfig.HookMarker] = AppConfig.HookVersion
        };
        foreach (var name in GeminiEvents)
        {
            var groups = CleanGroups(hooks.GetValueOrDefault(name));
            groups.Insert(0, new Dictionary<string, object?>
            {
                ["hooks"] = new List<object?> { entry.ToDictionary(pair => pair.Key, pair => pair.Value) }
            });
            hooks[name] = groups.Select(item => (object?)item).ToList();
        }
        root["hooks"] = hooks;
        try { JsonFile.Write(root, settingsPath); }
        catch (Exception) { return new HookInstallResult(false, binaryPath, error: "could not write hooks"); }
        var connected = GeminiInstalled(settingsPath, binaryPath);
        return new HookInstallResult(connected, binaryPath, error: connected ? null : "hook program missing");
    }

    public static HookInstallResult GeminiStatus(string settingsPath, string binaryPath)
    {
        return new HookInstallResult(GeminiInstalled(settingsPath, binaryPath), binaryPath);
    }

    public static HookInstallResult InstallClaude(string settingsPath, string binaryPath, bool privacyAccepted)
    {
        if (!privacyAccepted) return new HookInstallResult(false, binaryPath, needsPrivacy: true);
        if (!FileBits.IsExecutable(binaryPath)) return new HookInstallResult(false, binaryPath, error: "hook program missing");
        return InstallNested(settingsPath, ShellCommand(binaryPath, "claude"), binaryPath, 10);
    }

    public static HookInstallResult CodexStatus(string configPath, string hooksPath, string binaryPath)
    {
        var config = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
        var installed = CodexFeatureFlag.IsEnabled(config) && NestedInstalled(hooksPath, binaryPath);
        var connected = installed && CodexTrustMatches(config, hooksPath, binaryPath);
        return new HookInstallResult(connected, binaryPath);
    }

    public static bool TrustCodex(string configPath, string hooksPath, string binaryPath)
    {
        if (!FileBits.IsExecutable(binaryPath)) return false;
        var entries = CodexTrustEntries(hooksPath, binaryPath);
        if (entries.Count == 0) return false;
        var original = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
        var updated = CodexHookTrust.ConfigByTrusting(original, entries);
        if (updated == original) return true;
        FileBits.Backup(configPath);
        try { AtomicFile.WriteText(updated, configPath); }
        catch (Exception) { return false; }
        var saved = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
        return CodexTrustMatches(saved, hooksPath, binaryPath);
    }

    public static HookInstallResult ClaudeStatus(string settingsPath, string binaryPath)
    {
        return new HookInstallResult(NestedInstalled(settingsPath, binaryPath), binaryPath);
    }

    public static HookInstallResult RemoveNested(string filePath, string binaryPath)
    {
        FileBits.Backup(filePath);
        var root = JsonFile.Read(filePath);
        var hooks = Bags.Object(root.GetValueOrDefault("hooks"));
        var cleaned = new Dictionary<string, object?>();
        foreach (var pair in hooks)
        {
            var groups = CleanGroups(pair.Value);
            if (groups.Count > 0) cleaned[pair.Key] = groups.Select(item => (object?)item).ToList();
        }
        root["hooks"] = cleaned;
        try { JsonFile.Write(root, filePath); } catch (Exception) { }
        return new HookInstallResult(NestedInstalled(filePath, binaryPath), binaryPath);
    }

    public static bool RetargetNested(string filePath, string binaryPath, string source)
    {
        if (!FileBits.IsExecutable(binaryPath)) return false;
        var root = JsonFile.Read(filePath);
        if (root.GetValueOrDefault("hooks") is not Dictionary<string, object?> hooks || hooks.Count == 0) return false;
        var command = ShellCommand(binaryPath, source);
        var changed = false;
        foreach (var pair in hooks.ToList())
        {
            var groups = GroupRows(pair.Value);
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                if (groups[groupIndex].GetValueOrDefault("hooks") is not List<object?> && groups[groupIndex].GetValueOrDefault("hooks") is not Dictionary<string, object?>)
                {
                    if (groups[groupIndex].GetValueOrDefault("hooks") == null) continue;
                }
                var entries = Rows(groups[groupIndex].GetValueOrDefault("hooks"));
                if (entries.Count == 0 && groups[groupIndex].GetValueOrDefault("hooks") is not List<object?>) continue;
                for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    if (!IsOurs(entries[entryIndex])) continue;
                    var existing = Bags.Text(entries[entryIndex].GetValueOrDefault("command"));
                    if (existing == command || existing.Contains(binaryPath, StringComparison.Ordinal)) continue;
                    entries[entryIndex]["command"] = command;
                    entries[entryIndex][AppConfig.HookMarker] = AppConfig.HookVersion;
                    changed = true;
                }
                groups[groupIndex]["hooks"] = entries.Select(item => (object?)item).ToList();
            }
            hooks[pair.Key] = groups.Select(item => (object?)item).ToList();
        }
        if (!changed) return NestedInstalled(filePath, binaryPath);
        root["hooks"] = hooks;
        FileBits.Backup(filePath);
        try { JsonFile.Write(root, filePath); }
        catch (Exception) { return false; }
        return NestedInstalled(filePath, binaryPath);
    }

    public static string ShellCommand(string binaryPath, string source, string? eventName = null)
    {
        var path = binaryPath.Contains(' ') ? "\"" + binaryPath + "\"" : binaryPath;
        var command = path + " --source " + source;
        if (!string.IsNullOrEmpty(eventName)) command += " --event " + eventName;
        return command;
    }

    private static HookInstallResult InstallNested(string filePath, string command, string binaryPath, int permissionTimeout)
    {
        if (File.Exists(filePath))
        {
            var data = File.ReadAllBytes(filePath);
            if (data.Length > 0 && !IsJsonObject(data))
            {
                return new HookInstallResult(false, binaryPath, error: "could not read hooks");
            }
        }
        FileBits.Backup(filePath);
        var root = JsonFile.Read(filePath);
        var hooks = Bags.Object(root.GetValueOrDefault("hooks"));
        foreach (var name in CommandEvents)
        {
            var groups = CleanGroups(hooks.GetValueOrDefault(name));
            var entry = new Dictionary<string, object?>
            {
                ["type"] = "command",
                ["command"] = command,
                ["timeout"] = name == "PermissionRequest" ? permissionTimeout : 10,
                [AppConfig.HookMarker] = AppConfig.HookVersion
            };
            groups.Insert(0, new Dictionary<string, object?> { ["hooks"] = new List<object?> { entry } });
            hooks[name] = groups.Select(item => (object?)item).ToList();
        }
        root["hooks"] = hooks;
        try { JsonFile.Write(root, filePath); }
        catch (Exception) { return new HookInstallResult(false, binaryPath, error: "could not write hooks"); }
        var connected = NestedInstalled(filePath, binaryPath);
        return new HookInstallResult(connected, binaryPath, error: connected ? null : "hook program missing");
    }

    private static bool IsInstalled(string hooksPath, string binaryPath)
    {
        if (!FileBits.IsExecutable(binaryPath)) return false;
        var hooks = Bags.Object(JsonFile.Read(hooksPath).GetValueOrDefault("hooks"));
        return Rows(hooks.GetValueOrDefault("beforeSubmitPrompt")).Any(entry =>
            IsOurs(entry) && CommandPoints(binaryPath, Bags.Text(entry.GetValueOrDefault("command"))));
    }

    private static bool CommandPoints(string binaryPath, string command)
    {
        if (command == binaryPath || command.StartsWith(binaryPath + " ", StringComparison.Ordinal)) return true;
        var quoted = "\"" + binaryPath + "\"";
        return command == quoted || command.StartsWith(quoted + " ", StringComparison.Ordinal);
    }

    private static string CursorCommand(string binaryPath)
    {
        return binaryPath.Contains(' ') ? "\"" + binaryPath + "\"" : binaryPath;
    }

    public static bool IsOurs(Dictionary<string, object?> entry)
    {
        if (entry.ContainsKey(AppConfig.HookMarker)) return true;
        var command = Bags.Text(entry.GetValueOrDefault("command"));
        return command.Contains("MochiHookCLI", StringComparison.Ordinal) || command.Contains("mochi-island-status.py", StringComparison.Ordinal);
    }

    private static bool GeminiInstalled(string filePath, string binaryPath)
    {
        if (!FileBits.IsExecutable(binaryPath)) return false;
        var hooks = Bags.Object(JsonFile.Read(filePath).GetValueOrDefault("hooks"));
        const string needle = "--source gemini";
        foreach (var name in GeminiEvents)
        {
            foreach (var group in GroupRows(hooks.GetValueOrDefault(name)))
            {
                foreach (var entry in Rows(group.GetValueOrDefault("hooks")))
                {
                    if (!IsOurs(entry)) continue;
                    var command = Bags.Text(entry.GetValueOrDefault("command"));
                    if (command.Contains(binaryPath, StringComparison.Ordinal) && command.Contains(needle, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static bool NestedInstalled(string filePath, string binaryPath)
    {
        if (!FileBits.IsExecutable(binaryPath)) return false;
        var hooks = Bags.Object(JsonFile.Read(filePath).GetValueOrDefault("hooks"));
        foreach (var group in GroupRows(hooks.GetValueOrDefault("UserPromptSubmit")))
        {
            foreach (var entry in Rows(group.GetValueOrDefault("hooks")))
            {
                if (!IsOurs(entry)) continue;
                var command = Bags.Text(entry.GetValueOrDefault("command"));
                if (command.Contains(binaryPath, StringComparison.Ordinal) || command.Contains("MochiHookCLI", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static List<(string Key, string Hash)> CodexTrustEntries(string hooksPath, string binaryPath)
    {
        var hooks = Bags.Object(JsonFile.Read(hooksPath).GetValueOrDefault("hooks"));
        var source = FileBits.ResolveSymlinks(hooksPath);
        var entries = new List<(string Key, string Hash)>();
        foreach (var pair in hooks)
        {
            var eventKey = CodexHookTrust.EventKey(pair.Key);
            if (eventKey == null) continue;
            var groups = GroupRows(pair.Value);
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var handlers = Rows(groups[groupIndex].GetValueOrDefault("hooks"));
                for (var handlerIndex = 0; handlerIndex < handlers.Count; handlerIndex++)
                {
                    if (!IsOurs(handlers[handlerIndex])) continue;
                    var command = Bags.Text(handlers[handlerIndex].GetValueOrDefault("command"));
                    if (!command.Contains(binaryPath, StringComparison.Ordinal)) continue;
                    var timeout = Bags.Whole(handlers[handlerIndex].GetValueOrDefault("timeout"));
                    if (timeout == 0) timeout = 10;
                    var hash = CodexHookTrust.IdentityHash(eventKey, command, timeout);
                    entries.Add(($"{source}:{eventKey}:{groupIndex}:{handlerIndex}", hash));
                }
            }
        }
        return entries;
    }

    private static bool CodexTrustMatches(string config, string hooksPath, string binaryPath)
    {
        var entries = CodexTrustEntries(hooksPath, binaryPath);
        if (entries.Count == 0) return false;
        return CodexHookTrust.ConfigByTrusting(config, entries) == config;
    }

    private static List<Dictionary<string, object?>> Rows(object? value)
    {
        return Bags.Array(value).Select(Bags.Object).Where(row => row.Count > 0 || value is List<object?>).ToList();
    }

    private static List<Dictionary<string, object?>> GroupRows(object? value)
    {
        return Bags.Array(value).Select(Bags.Object).Where(row => row.Count > 0).ToList();
    }

    private static List<Dictionary<string, object?>> CleanGroups(object? raw)
    {
        var groups = new List<Dictionary<string, object?>>();
        foreach (var group in GroupRows(raw))
        {
            if (group.ContainsKey("hooks"))
            {
                var kept = Rows(group["hooks"]).Where(entry => !IsOurs(entry)).ToList();
                if (kept.Count == 0) continue;
                group["hooks"] = kept.Select(item => (object?)item).ToList();
                groups.Add(group);
            }
            else if (!IsOurs(group))
            {
                groups.Add(group);
            }
        }
        return groups;
    }

    private static bool IsJsonObject(byte[] data)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(data);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
