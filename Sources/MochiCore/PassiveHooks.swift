import Foundation

/// Passive hook files for Copilot CLI, OpenCode, and the xAI grok CLI.
/// Each file is marked `mochi-island`. Prompt and command text are not written.
public enum PassiveHooks {
    public static let copilotEvents = [
        "sessionStart",
        "userPromptSubmitted",
        "preToolUse",
        "postToolUse",
        "subagentStart",
        "permissionRequest",
        "notification",
        "postToolUseFailure",
        "errorOccurred",
        "agentStop",
        "sessionEnd"
    ]

    public static let grokEvents = [
        "SessionStart",
        "UserPromptSubmit",
        "PreToolUse",
        "PostToolUse",
        "SubagentStart",
        "Notification",
        "PermissionDenied",
        "PostToolUseFailure",
        "Stop",
        "StopFailure",
        "StopCancelled",
        "SessionEnd"
    ]

    public static func copilotDirectory(home: URL, environment: [String: String]) -> URL {
        let override = environment["COPILOT_HOME"]?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if override.isEmpty {
            return home.appendingPathComponent(".copilot/hooks", isDirectory: true)
        }
        return URL(fileURLWithPath: override, isDirectory: true).appendingPathComponent("hooks", isDirectory: true)
    }

    public static func grokDirectory(home: URL) -> URL {
        home.appendingPathComponent(".grok/hooks", isDirectory: true)
    }

    public static func openCodePluginURL(home: URL) -> URL {
        home.appendingPathComponent(".config/opencode/plugins/mochi-island.js")
    }

    public static func installCopilot(directory: URL, binaryURL: URL, privacyAccepted: Bool) -> HookInstallResult {
        guard let ready = ready(binaryURL: binaryURL, privacyAccepted: privacyAccepted) else {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: privacyAccepted ? "hook program missing" : nil, needsPrivacy: !privacyAccepted)
        }
        let fileURL = directory.appendingPathComponent("mochi-island.json")
        backup(fileURL)
        var hooks: [String: Any] = [:]
        for name in copilotEvents {
            let command = shell(ready, source: "copilot", event: name)
            hooks[name] = [[
                "type": "command",
                "bash": command,
                "command": command,
                "timeoutSec": 10,
                AppConfig.hookMarker: AppConfig.hookVersion
            ]]
        }
        return writeHooks(fileURL: fileURL, hooks: hooks, version: 1, binaryURL: ready, source: "copilot")
    }

    public static func installGrok(directory: URL, binaryURL: URL, privacyAccepted: Bool) -> HookInstallResult {
        guard let ready = ready(binaryURL: binaryURL, privacyAccepted: privacyAccepted) else {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: privacyAccepted ? "hook program missing" : nil, needsPrivacy: !privacyAccepted)
        }
        let fileURL = directory.appendingPathComponent("mochi-island.json")
        backup(fileURL)
        let command = shell(ready, source: "grok", event: nil)
        let entry: [String: Any] = [
            "type": "command",
            "command": command,
            "timeout": 10,
            AppConfig.hookMarker: AppConfig.hookVersion
        ]
        var hooks: [String: Any] = [:]
        for name in grokEvents {
            hooks[name] = [["hooks": [entry]]]
        }
        return writeHooks(fileURL: fileURL, hooks: hooks, version: nil, binaryURL: ready, source: "grok")
    }

    public static func installOpenCode(pluginURL: URL, binaryURL: URL, privacyAccepted: Bool) -> HookInstallResult {
        guard privacyAccepted else {
            return HookInstallResult(connected: false, hook: binaryURL.path, needsPrivacy: true)
        }
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "hook program missing")
        }
        backup(pluginURL)
        let script = openCodePlugin(cli: binaryURL.path)
        do {
            try AtomicFile.write(Data(script.utf8), to: pluginURL)
        } catch {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "could not write OpenCode plugin")
        }
        let connected = openCodeInstalled(pluginURL: pluginURL, binaryURL: binaryURL)
        return HookInstallResult(connected: connected, hook: binaryURL.path)
    }

    public static func copilotStatus(directory: URL, binaryURL: URL) -> HookInstallResult {
        jsonStatus(fileURL: directory.appendingPathComponent("mochi-island.json"), binaryURL: binaryURL, source: "copilot")
    }

    public static func grokStatus(directory: URL, binaryURL: URL) -> HookInstallResult {
        jsonStatus(fileURL: directory.appendingPathComponent("mochi-island.json"), binaryURL: binaryURL, source: "grok")
    }

    public static func openCodeStatus(pluginURL: URL, binaryURL: URL) -> HookInstallResult {
        HookInstallResult(
            connected: openCodeInstalled(pluginURL: pluginURL, binaryURL: binaryURL),
            hook: binaryURL.path
        )
    }

    public static func remove(fileURL: URL, binaryURL: URL) -> HookInstallResult {
        backup(fileURL)
        if FileManager.default.fileExists(atPath: fileURL.path) {
            try? FileManager.default.removeItem(at: fileURL)
        }
        return HookInstallResult(connected: false, hook: binaryURL.path)
    }

    private static func ready(binaryURL: URL, privacyAccepted: Bool) -> URL? {
        guard privacyAccepted, FileManager.default.isExecutableFile(atPath: binaryURL.path) else { return nil }
        return binaryURL
    }

    private static func writeHooks(
        fileURL: URL,
        hooks: [String: Any],
        version: Int?,
        binaryURL: URL,
        source: String
    ) -> HookInstallResult {
        var root: [String: Any] = ["hooks": hooks]
        if let version { root["version"] = version }
        do {
            try AtomicFile.writeJSON(root, to: fileURL)
        } catch {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "could not write hooks")
        }
        return jsonStatus(fileURL: fileURL, binaryURL: binaryURL, source: source)
    }

    private static func jsonStatus(fileURL: URL, binaryURL: URL, source: String) -> HookInstallResult {
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path),
              let data = try? Data(contentsOf: fileURL),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let hooks = root["hooks"] as? [String: Any] else {
            return HookInstallResult(connected: false, hook: binaryURL.path)
        }
        let needle = "--source \(source)"
        for raw in hooks.values {
            for entry in commandEntries(raw) {
                let command = entry["command"] as? String ?? ""
                let marked = entry[AppConfig.hookMarker] != nil
                if marked, command.contains(binaryURL.path), command.contains(needle) {
                    return HookInstallResult(connected: true, hook: binaryURL.path)
                }
            }
        }
        return HookInstallResult(connected: false, hook: binaryURL.path)
    }

    private static func commandEntries(_ raw: Any) -> [[String: Any]] {
        guard let list = raw as? [[String: Any]] else { return [] }
        var found: [[String: Any]] = []
        for item in list {
            if let inner = item["hooks"] as? [[String: Any]] {
                found.append(contentsOf: inner)
            } else {
                found.append(item)
            }
        }
        return found
    }

    private static func openCodeInstalled(pluginURL: URL, binaryURL: URL) -> Bool {
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path),
              let text = try? String(contentsOf: pluginURL, encoding: .utf8) else { return false }
        return text.contains("mochi-island")
            && text.contains(binaryURL.path)
            && text.contains("\"--source\", \"opencode\"")
            && text.contains("session.status")
            && text.contains("tool.execute.before")
            && text.contains("permission.asked")
            && text.contains("question.asked")
            && text.contains("session.error")
            && text.contains("spawnSync")
            && !text.contains("message.updated")
            && !text.contains("message.part")
            && !text.contains("http://")
            && !text.contains("https://")
    }

    static func openCodePlugin(cli: String) -> String {
        let quoted = cli.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
        return """
        // mochi-island
        // Passive status plugin. It does not read message text or the local event stream.
        import { spawnSync } from "node:child_process"

        const cli = "\(quoted)"

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
        """
    }

    private static func shell(_ binary: URL, source: String, event: String?) -> String {
        let path = binary.path.contains(" ") ? "\"\(binary.path)\"" : binary.path
        var command = "\(path) --source \(source)"
        if let event, !event.isEmpty {
            command += " --event \(event)"
        }
        return command
    }

    private static func backup(_ url: URL) {
        guard FileManager.default.fileExists(atPath: url.path) else { return }
        let backup = url.appendingPathExtension("mochi-island.bak")
        try? FileManager.default.removeItem(at: backup)
        try? FileManager.default.copyItem(at: url, to: backup)
    }
}
