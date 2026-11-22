namespace Mochi.Core;

public readonly record struct AgentLaunchPlan(string? AppPath, string? Url, string? Note);

public static class AgentLaunch
{
    public static string Normalize(string? raw)
    {
        var value = (raw ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "codex" => "codex",
            "claude" or "claude-code" or "claude code" => "claude",
            "grok" or "grok-bot" or "grokbot" or "grok bot" => "grok-bot",
            "gpt" or "chatgpt" or "openai" => "gpt",
            "copilot" or "github-copilot" or "github copilot" => "copilot",
            "opencode" or "open-code" or "open code" => "opencode",
            "gemini" or "gemini-cli" or "gemini cli" => "gemini",
            "mochi" or "cat" or "小猫" => "mochi",
            _ => "cursor"
        };
    }

    public static AgentLaunchPlan Plan(
        string? source,
        string? home = null,
        string? localAppData = null,
        Func<string, bool>? fileExists = null,
        string? programFiles = null)
    {
        home ??= AppConfig.HomeDirectory;
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        programFiles ??= Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        fileExists ??= File.Exists;
        var key = Normalize(source);
        var (names, fallback, missing) = key switch
        {
            "mochi" => (Array.Empty<string>(), "", "小猫 stays on this computer."),
            "codex" => (new[] { "ChatGPT" }, "https://chatgpt.com/codex", "Codex is not installed; opened the Codex page."),
            "claude" => (new[] { "Claude" }, "https://claude.ai", "Claude is not installed; opened the Claude page."),
            "grok-bot" => (new[] { "Grok", "Grok Bot" }, "https://grok.com", "Grok is not installed; opened the Grok page."),
            "gpt" => (new[] { "ChatGPT" }, "https://chatgpt.com", "ChatGPT is not installed; opened the ChatGPT page."),
            "copilot" => (Array.Empty<string>(), "https://github.com/features/copilot", "GitHub Copilot CLI stays in the terminal."),
            "opencode" => (Array.Empty<string>(), "https://opencode.ai", "OpenCode stays in the terminal."),
            "gemini" => (Array.Empty<string>(), "https://geminicli.com", "Gemini CLI stays in the terminal."),
            _ => (new[] { "Cursor" }, "https://cursor.com", "Cursor is not installed; opened the Cursor page.")
        };
        if (key == "mochi") return new AgentLaunchPlan(null, null, missing);
        foreach (var name in names)
        {
            foreach (var path in Candidates(home, localAppData, programFiles, name))
            {
                if (fileExists(path)) return new AgentLaunchPlan(path, null, null);
            }
        }
        if (names.Length == 0 && key is "copilot" or "opencode" or "gemini")
        {
            return new AgentLaunchPlan(null, fallback, missing);
        }
        return new AgentLaunchPlan(null, string.IsNullOrEmpty(fallback) ? null : fallback, missing);
    }

    private static IEnumerable<string> Candidates(string home, string localAppData, string programFiles, string name)
    {
        if (name == "Cursor")
        {
            yield return Path.Combine(localAppData, "Programs", "cursor", "Cursor.exe");
            yield return Path.Combine(localAppData, "Programs", "Cursor", "Cursor.exe");
            yield return Path.Combine(programFiles, "cursor", "Cursor.exe");
            yield return Path.Combine(programFiles, "Cursor", "Cursor.exe");
            yield break;
        }
        if (name == "Claude")
        {
            yield return Path.Combine(localAppData, "AnthropicClaude", "claude.exe");
            yield return Path.Combine(localAppData, "Programs", "Claude", "Claude.exe");
            yield return Path.Combine(programFiles, "Claude", "Claude.exe");
            // Claude Code installed as a native CLI (~/.local/bin/claude.exe).
            yield return Path.Combine(home, ".local", "bin", "claude.exe");
            yield return Path.Combine(home, ".local", "bin", "claude");
            yield break;
        }
        if (name == "Grok")
        {
            yield return Path.Combine(localAppData, "Programs", "Grok", "Grok.exe");
            yield return Path.Combine(localAppData, "Programs", "Grok Bot", "Grok Bot.exe");
            yield return Path.Combine(programFiles, "Grok", "Grok.exe");
            yield return Path.Combine(programFiles, "Grok Bot", "Grok Bot.exe");
            yield break;
        }
        if (name == "Grok Bot")
        {
            yield return Path.Combine(localAppData, "Programs", "Grok Bot", "Grok.exe");
            yield return Path.Combine(programFiles, "Grok Bot", "Grok Bot.exe");
            yield break;
        }
        if (name == "ChatGPT")
        {
            yield return Path.Combine(localAppData, "Programs", "ChatGPT", "ChatGPT.exe");
            // App execution alias for the packaged ChatGPT / Codex desktop app.
            yield return Path.Combine(localAppData, "Microsoft", "WindowsApps", "chatgpt-desktop.exe");
            yield return Path.Combine(programFiles, "ChatGPT", "ChatGPT.exe");
            yield break;
        }
        yield return Path.Combine(localAppData, "Programs", name, name + ".exe");
    }
}
