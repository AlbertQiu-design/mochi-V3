namespace Mochi.Core;

public static class SlotFace
{
    public static string Pack(string source)
    {
        return AgentLaunch.Normalize(source) switch
        {
            "grok-bot" => "grok",
            "mochi" => "kitten",
            "codex" => "codex",
            "claude" => "claude",
            "copilot" => "copilot",
            "opencode" => "opencode",
            "gemini" => "gemini",
            "gpt" => "gpt",
            _ => "cursor"
        };
    }

    public static string State(string status)
    {
        return status switch
        {
            "running" => "work",
            "wait" or "fail" => "wait",
            "done" => "done",
            _ => "wake"
        };
    }

    public static string ResourceName(string source, string status) => Pack(source) + "-" + State(status);

    public static string DisplayName(string source)
    {
        return AgentLaunch.Normalize(source) switch
        {
            "grok-bot" => "Grok Bot",
            "mochi" => "小猫",
            "codex" => "Codex",
            "claude" => "Claude",
            "copilot" => "Copilot",
            "opencode" => "OpenCode",
            "gemini" => "Gemini",
            "gpt" => "GPT",
            _ => "Cursor"
        };
    }

    public static string AgentMark(string source)
    {
        return AgentLaunch.Normalize(source) switch
        {
            "codex" => "agent-codex",
            "claude" => "agent-claude",
            "copilot" => "agent-copilot",
            "opencode" => "agent-opencode",
            "gemini" => "agent-gemini",
            "grok-bot" => "agent-grok",
            "gpt" => "agent-gpt",
            "mochi" => "kitten-mark",
            _ => "agent-cursor"
        };
    }

    public static string PickerName(string source)
    {
        return AgentLaunch.Normalize(source) switch
        {
            "grok-bot" => "Grok",
            "codex" => "Codex",
            "claude" => "Claude",
            "copilot" => "Copilot",
            "opencode" => "OpenCode",
            "gemini" => "Gemini",
            "gpt" => "GPT",
            _ => "Cursor"
        };
    }

    public static string Label(string title, string source)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0 || trimmed == "New chat") return DisplayName(source);
        return trimmed;
    }
}
