import Foundation

public struct AgentLaunchPlan: Equatable {
    public var appPath: String?
    public var url: String?
    public var note: String?

    public init(appPath: String? = nil, url: String? = nil, note: String? = nil) {
        self.appPath = appPath
        self.url = url
        self.note = note
    }
}

public enum AgentLaunch {
    public static func normalize(_ raw: String?) -> String {
        let value = (raw ?? "").trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        switch value {
        case "codex":
            return "codex"
        case "claude", "claude-code", "claude code":
            return "claude"
        case "grok", "grok-bot", "grokbot", "grok bot":
            return "grok-bot"
        case "gpt", "chatgpt", "openai":
            return "gpt"
        case "copilot", "github-copilot", "github copilot":
            return "copilot"
        case "opencode", "open-code", "open code":
            return "opencode"
        case "gemini", "gemini-cli", "gemini cli":
            return "gemini"
        case "mochi", "cat", "小猫":
            return "mochi"
        default:
            return "cursor"
        }
    }

    /// Codex.app is often absent. Callers open `url` and can show `note` in that case.
    public static func plan(
        source: String?,
        home: URL = AppConfig.homeDirectory,
        fileExists: (String) -> Bool = { FileManager.default.fileExists(atPath: $0) }
    ) -> AgentLaunchPlan {
        let key = normalize(source)
        let names: [String]
        let fallback: String
        let missing: String
        switch key {
        case "mochi":
            return AgentLaunchPlan(note: "小猫 stays on this Mac.")
        case "codex":
            names = ["Codex"]
            fallback = "https://chatgpt.com/codex"
            missing = "Codex.app is not installed; opened the Codex page."
        case "claude":
            names = ["Claude"]
            fallback = "https://claude.ai"
            missing = "Claude.app is not installed; opened the Claude page."
        case "grok-bot":
            names = ["Grok Bot", "Grok"]
            fallback = "https://grok.com"
            missing = "Grok Bot.app is not installed; opened the Grok page."
        case "gpt":
            names = ["ChatGPT"]
            fallback = "https://chatgpt.com"
            missing = "ChatGPT.app is not installed; opened the ChatGPT page."
        case "copilot":
            names = []
            fallback = "https://github.com/features/copilot"
            missing = "GitHub Copilot CLI stays in the terminal."
        case "opencode":
            names = []
            fallback = "https://opencode.ai"
            missing = "OpenCode stays in the terminal."
        case "gemini":
            names = []
            fallback = "https://geminicli.com"
            missing = "Gemini CLI stays in the terminal."
        default:
            names = ["Cursor"]
            fallback = "https://cursor.com"
            missing = "Cursor.app is not installed; opened the Cursor page."
        }
        let roots = [
            URL(fileURLWithPath: "/Applications", isDirectory: true),
            home.appendingPathComponent("Applications", isDirectory: true)
        ]
        for name in names {
            for root in roots {
                let path = root.appendingPathComponent("\(name).app").path
                if fileExists(path) {
                    return AgentLaunchPlan(appPath: path)
                }
            }
        }
        return AgentLaunchPlan(url: fallback, note: missing)
    }
}
