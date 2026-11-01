import Foundation

/// Capsule face files and Dock-label copy. Kitten art is the full Designer
/// kitten-v1 illustrations (wake/work/wait/done PNGs), not the shrinked SVGs.
public enum SlotFace {
    public static func pack(for source: String) -> String {
        switch AgentLaunch.normalize(source) {
        case "grok-bot": return "grok"
        case "mochi": return "kitten"
        case "codex": return "codex"
        case "claude": return "claude"
        case "copilot": return "copilot"
        case "opencode": return "opencode"
        case "gemini": return "gemini"
        case "gpt": return "gpt"
        default: return "cursor"
        }
    }

    public static func state(for status: String) -> String {
        switch status {
        case "running": return "work"
        case "wait", "fail": return "wait"
        case "done": return "done"
        default: return "wake"
        }
    }

    public static func resourceName(source: String, status: String) -> String {
        "\(pack(for: source))-\(state(for: status))"
    }

    public static func displayName(for source: String) -> String {
        switch AgentLaunch.normalize(source) {
        case "grok-bot": return "Grok Bot"
        case "mochi": return "小猫"
        case "codex": return "Codex"
        case "claude": return "Claude"
        case "copilot": return "Copilot"
        case "opencode": return "OpenCode"
        case "gemini": return "Gemini"
        case "gpt": return "GPT"
        default: return "Cursor"
        }
    }

    /// Simplified mark shipped in `web/pets/` when the official app icon is missing.
    public static func agentMark(for source: String) -> String {
        switch AgentLaunch.normalize(source) {
        case "codex": return "agent-codex"
        case "claude": return "agent-claude"
        case "copilot": return "agent-copilot"
        case "opencode": return "agent-opencode"
        case "gemini": return "agent-gemini"
        case "grok-bot": return "agent-grok"
        case "gpt": return "agent-gpt"
        case "mochi": return "kitten-mark"
        default: return "agent-cursor"
        }
    }

    public static func pickerName(for source: String) -> String {
        switch AgentLaunch.normalize(source) {
        case "grok-bot": return "Grok"
        case "codex": return "Codex"
        case "claude": return "Claude"
        case "copilot": return "Copilot"
        case "opencode": return "OpenCode"
        case "gemini": return "Gemini"
        case "gpt": return "GPT"
        default: return "Cursor"
        }
    }

    public static func label(title: String, source: String) -> String {
        let trimmed = title.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || trimmed == "New chat" {
            return displayName(for: source)
        }
        return trimmed
    }
}
