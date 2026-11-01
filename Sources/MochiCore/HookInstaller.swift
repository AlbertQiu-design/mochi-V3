import Foundation

public struct HookInstallResult: Equatable {
    public var connected: Bool
    public var hook: String
    public var tasks: Int
    public var error: String?
    public var needsPrivacy: Bool

    public init(connected: Bool, hook: String, tasks: Int = 0, error: String? = nil, needsPrivacy: Bool = false) {
        self.connected = connected
        self.hook = hook
        self.tasks = tasks
        self.error = error
        self.needsPrivacy = needsPrivacy
    }

    public var dictionary: [String: Any] {
        var payload: [String: Any] = [
            "connected": connected,
            "hook": hook,
            "tasks": tasks,
            "needsPrivacy": needsPrivacy
        ]
        if let error { payload["error"] = error }
        return payload
    }
}

public enum HookInstaller {
    public static let events = [
        "beforeSubmitPrompt",
        "sessionStart",
        "subagentStart",
        "preToolUse",
        "postToolUse",
        "postToolUseFailure",
        "beforeShellExecution",
        "afterShellExecution",
        "beforeMCPExecution",
        "afterMCPExecution",
        "afterAgentThought",
        "afterAgentResponse",
        "afterFileEdit",
        "stop",
        "sessionEnd",
        "subagentStop"
    ]
    public static let permissionEvents: Set<String> = ["beforeShellExecution", "beforeMCPExecution"]

    public static func status(hooksURL: URL, binaryURL: URL, tasksURL: URL) -> HookInstallResult {
        let connected = isInstalled(hooksURL: hooksURL, binaryURL: binaryURL)
        let count = connected ? TaskReader.load(tasksURL: tasksURL, includePrompts: false).count : 0
        return HookInstallResult(connected: connected, hook: binaryURL.path, tasks: count)
    }

    public static func install(hooksURL: URL, binaryURL: URL, tasksURL: URL, privacyAccepted: Bool) -> HookInstallResult {
        guard privacyAccepted else {
            return HookInstallResult(connected: false, hook: binaryURL.path, needsPrivacy: true)
        }
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "hook program missing")
        }
        backup(hooksURL)
        var root = read(hooksURL)
        var hooks = root["hooks"] as? [String: Any] ?? [:]
        let command = binaryURL.path
        for event in events {
            var entries = hooks[event] as? [[String: Any]] ?? []
            entries.removeAll { isOurs($0) }
            var entry: [String: Any] = [
                "command": command,
                AppConfig.hookMarker: AppConfig.hookVersion
            ]
            if permissionEvents.contains(event) {
                entry["timeout"] = 300
            }
            entries.insert(entry, at: 0)
            hooks[event] = entries
        }
        root["version"] = 1
        root["hooks"] = hooks
        do {
            try AtomicFile.writeJSON(root, to: hooksURL)
        } catch {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "could not write hooks")
        }
        return status(hooksURL: hooksURL, binaryURL: binaryURL, tasksURL: tasksURL)
    }

    public static func remove(hooksURL: URL, binaryURL: URL, tasksURL: URL) -> HookInstallResult {
        backup(hooksURL)
        var root = read(hooksURL)
        var hooks = root["hooks"] as? [String: Any] ?? [:]
        var cleaned: [String: Any] = [:]
        for (event, raw) in hooks {
            let entries = (raw as? [[String: Any]] ?? []).filter { !isOurs($0) }
            if !entries.isEmpty {
                cleaned[event] = entries
            }
        }
        hooks = cleaned
        root["hooks"] = hooks
        try? AtomicFile.writeJSON(root, to: hooksURL)
        return status(hooksURL: hooksURL, binaryURL: binaryURL, tasksURL: tasksURL)
    }

    /// Connected only when our marked hook runs this app's MochiHookCLI.
    private static func isInstalled(hooksURL: URL, binaryURL: URL) -> Bool {
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else { return false }
        let hooks = read(hooksURL)["hooks"] as? [String: Any] ?? [:]
        let entries = hooks["beforeSubmitPrompt"] as? [[String: Any]] ?? []
        return entries.contains { entry in
            isOurs(entry) && commandPoints(at: binaryURL, command: entry["command"] as? String ?? "")
        }
    }

    /// Rewrites our Cursor hook commands onto this app's binary. Other hooks stay.
    @discardableResult
    public static func retargetCursor(hooksURL: URL, binaryURL: URL) -> Bool {
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else { return false }
        var root = read(hooksURL)
        guard var hooks = root["hooks"] as? [String: Any], !hooks.isEmpty else { return false }
        var changed = false
        for (event, raw) in hooks {
            guard var entries = raw as? [[String: Any]] else { continue }
            for index in entries.indices where isOurs(entries[index]) {
                let command = entries[index]["command"] as? String ?? ""
                if commandPoints(at: binaryURL, command: command) { continue }
                entries[index]["command"] = binaryURL.path
                entries[index][AppConfig.hookMarker] = AppConfig.hookVersion
                changed = true
            }
            hooks[event] = entries
        }
        guard changed else { return isInstalled(hooksURL: hooksURL, binaryURL: binaryURL) }
        root["hooks"] = hooks
        backup(hooksURL)
        do {
            try AtomicFile.writeJSON(root, to: hooksURL)
        } catch {
            return false
        }
        return isInstalled(hooksURL: hooksURL, binaryURL: binaryURL)
    }

    private static func commandPoints(at binaryURL: URL, command: String) -> Bool {
        command == binaryURL.path || command.hasPrefix(binaryURL.path + " ")
    }

    private static func isOurs(_ entry: [String: Any]) -> Bool {
        if entry[AppConfig.hookMarker] != nil { return true }
        let command = entry["command"] as? String ?? ""
        return command.contains("MochiHookCLI") || command.contains("mochi-island-status.py")
    }

    private static func read(_ url: URL) -> [String: Any] {
        guard let data = try? Data(contentsOf: url),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return [:]
        }
        return json
    }

    private static func backup(_ url: URL) {
        guard FileManager.default.fileExists(atPath: url.path) else { return }
        let backup = url.appendingPathExtension("mochi-island.bak")
        try? FileManager.default.removeItem(at: backup)
        try? FileManager.default.copyItem(at: url, to: backup)
    }

    /// Claude Code and Codex share this command-hook list. A normal event returns immediately.
    /// Codex `PermissionRequest` is the one that waits for allow or deny.
    public static let commandEvents = [
        "SessionStart",
        "UserPromptSubmit",
        "PreToolUse",
        "PostToolUse",
        "PostToolUseFailure",
        "PermissionRequest",
        "Notification",
        "Stop",
        "StopFailure",
        "SubagentStart",
        "SubagentStop",
        "SessionEnd"
    ]

    public static func installCodex(configURL: URL, hooksURL: URL, binaryURL: URL, privacyAccepted: Bool) -> HookInstallResult {
        guard privacyAccepted else {
            return HookInstallResult(connected: false, hook: binaryURL.path, needsPrivacy: true)
        }
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "hook program missing")
        }
        let original = (try? String(contentsOf: configURL, encoding: .utf8)) ?? ""
        guard let updated = CodexFeatureFlag.enablingHooks(in: original) else {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "could not enable Codex hooks")
        }
        backup(configURL)
        do {
            try FileManager.default.createDirectory(at: configURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            try updated.write(to: configURL, atomically: true, encoding: .utf8)
        } catch {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "could not write Codex config")
        }
        let installed = installNested(
            fileURL: hooksURL,
            command: shellCommand(binaryURL, source: "codex"),
            binaryURL: binaryURL,
            permissionTimeout: 300
        )
        guard installed.connected, CodexFeatureFlag.isEnabled(in: (try? String(contentsOf: configURL, encoding: .utf8)) ?? "") else {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: installed.error ?? "Codex hook was not installed")
        }
        return HookInstallResult(connected: true, hook: binaryURL.path)
    }

    public static let geminiEvents = [
        "SessionStart",
        "BeforeAgent",
        "BeforeModel",
        "AfterModel",
        "BeforeToolSelection",
        "BeforeTool",
        "AfterTool",
        "Notification",
        "AfterAgent",
        "SessionEnd"
    ]

    public static func installGemini(settingsURL: URL, binaryURL: URL, privacyAccepted: Bool) -> HookInstallResult {
        guard privacyAccepted else {
            return HookInstallResult(connected: false, hook: binaryURL.path, needsPrivacy: true)
        }
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "hook program missing")
        }
        if FileManager.default.fileExists(atPath: settingsURL.path) {
            let data = (try? Data(contentsOf: settingsURL)) ?? Data()
            if !data.isEmpty, (try? JSONSerialization.jsonObject(with: data) as? [String: Any]) == nil {
                return HookInstallResult(connected: false, hook: binaryURL.path, error: "could not read hooks")
            }
        }
        backup(settingsURL)
        var root = read(settingsURL)
        var hooks = root["hooks"] as? [String: Any] ?? [:]
        let command = shellCommand(binaryURL, source: "gemini")
        let entry: [String: Any] = [
            "type": "command",
            "command": command,
            "timeout": 10000,
            AppConfig.hookMarker: AppConfig.hookVersion
        ]
        for name in geminiEvents {
            var groups = (hooks[name] as? [[String: Any]] ?? []).compactMap { group -> [String: Any]? in
                var copy = group
                if let inner = copy["hooks"] as? [[String: Any]] {
                    let kept = inner.filter { !isOurs($0) }
                    if kept.isEmpty { return nil }
                    copy["hooks"] = kept
                    return copy
                }
                return isOurs(copy) ? nil : copy
            }
            groups.insert(["hooks": [entry]], at: 0)
            hooks[name] = groups
        }
        root["hooks"] = hooks
        do {
            try AtomicFile.writeJSON(root, to: settingsURL)
        } catch {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "could not write hooks")
        }
        let connected = geminiInstalled(fileURL: settingsURL, binaryURL: binaryURL)
        return HookInstallResult(connected: connected, hook: binaryURL.path, error: connected ? nil : "hook program missing")
    }

    public static func geminiStatus(settingsURL: URL, binaryURL: URL) -> HookInstallResult {
        HookInstallResult(connected: geminiInstalled(fileURL: settingsURL, binaryURL: binaryURL), hook: binaryURL.path)
    }

    public static func installClaude(settingsURL: URL, binaryURL: URL, privacyAccepted: Bool) -> HookInstallResult {
        guard privacyAccepted else {
            return HookInstallResult(connected: false, hook: binaryURL.path, needsPrivacy: true)
        }
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "hook program missing")
        }
        return installNested(
            fileURL: settingsURL,
            command: shellCommand(binaryURL, source: "claude"),
            binaryURL: binaryURL,
            permissionTimeout: 10
        )
    }

    public static func codexStatus(configURL: URL, hooksURL: URL, binaryURL: URL) -> HookInstallResult {
        let config = (try? String(contentsOf: configURL, encoding: .utf8)) ?? ""
        let connected = CodexFeatureFlag.isEnabled(in: config) && nestedInstalled(fileURL: hooksURL, binaryURL: binaryURL)
        return HookInstallResult(connected: connected, hook: binaryURL.path)
    }

    public static func claudeStatus(settingsURL: URL, binaryURL: URL) -> HookInstallResult {
        HookInstallResult(connected: nestedInstalled(fileURL: settingsURL, binaryURL: binaryURL), hook: binaryURL.path)
    }

    public static func removeNested(fileURL: URL, binaryURL: URL) -> HookInstallResult {
        backup(fileURL)
        var root = read(fileURL)
        var hooks = root["hooks"] as? [String: Any] ?? [:]
        var cleaned: [String: Any] = [:]
        for (event, raw) in hooks {
            let groups = (raw as? [[String: Any]] ?? []).compactMap { group -> [String: Any]? in
                var copy = group
                if let inner = copy["hooks"] as? [[String: Any]] {
                    let kept = inner.filter { !isOurs($0) }
                    if kept.isEmpty { return nil }
                    copy["hooks"] = kept
                    return copy
                }
                return isOurs(copy) ? nil : copy
            }
            if !groups.isEmpty { cleaned[event] = groups }
        }
        hooks = cleaned
        root["hooks"] = hooks
        try? AtomicFile.writeJSON(root, to: fileURL)
        return HookInstallResult(connected: nestedInstalled(fileURL: fileURL, binaryURL: binaryURL), hook: binaryURL.path)
    }

    /// Rewrites nested command hooks (Codex / Claude style) onto this app's MochiHookCLI.
    @discardableResult
    public static func retargetNested(fileURL: URL, binaryURL: URL, source: String) -> Bool {
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else { return false }
        var root = read(fileURL)
        guard var hooks = root["hooks"] as? [String: Any], !hooks.isEmpty else { return false }
        let command = shellCommand(binaryURL, source: source)
        var changed = false
        for (event, raw) in hooks {
            guard var groups = raw as? [[String: Any]] else { continue }
            for groupIndex in groups.indices {
                guard var entries = groups[groupIndex]["hooks"] as? [[String: Any]] else { continue }
                for entryIndex in entries.indices where isOurs(entries[entryIndex]) {
                    let existing = entries[entryIndex]["command"] as? String ?? ""
                    if existing == command || existing.contains(binaryURL.path) { continue }
                    entries[entryIndex]["command"] = command
                    entries[entryIndex][AppConfig.hookMarker] = AppConfig.hookVersion
                    changed = true
                }
                groups[groupIndex]["hooks"] = entries
            }
            hooks[event] = groups
        }
        guard changed else {
            return nestedInstalled(fileURL: fileURL, binaryURL: binaryURL)
        }
        root["hooks"] = hooks
        backup(fileURL)
        do {
            try AtomicFile.writeJSON(root, to: fileURL)
        } catch {
            return false
        }
        return nestedInstalled(fileURL: fileURL, binaryURL: binaryURL)
    }

    private static func installNested(fileURL: URL, command: String, binaryURL: URL, permissionTimeout: Int) -> HookInstallResult {
        if FileManager.default.fileExists(atPath: fileURL.path) {
            let data = (try? Data(contentsOf: fileURL)) ?? Data()
            if !data.isEmpty, (try? JSONSerialization.jsonObject(with: data) as? [String: Any]) == nil {
                return HookInstallResult(connected: false, hook: binaryURL.path, error: "could not read hooks")
            }
        }
        backup(fileURL)
        var root = read(fileURL)
        var hooks = root["hooks"] as? [String: Any] ?? [:]
        for name in commandEvents {
            var groups = (hooks[name] as? [[String: Any]] ?? []).compactMap { group -> [String: Any]? in
                var copy = group
                if let inner = copy["hooks"] as? [[String: Any]] {
                    let kept = inner.filter { !isOurs($0) }
                    if kept.isEmpty { return nil }
                    copy["hooks"] = kept
                    return copy
                }
                return isOurs(copy) ? nil : copy
            }
            let entry: [String: Any] = [
                "type": "command",
                "command": command,
                "timeout": name == "PermissionRequest" ? permissionTimeout : 10,
                AppConfig.hookMarker: AppConfig.hookVersion
            ]
            groups.insert(["hooks": [entry]], at: 0)
            hooks[name] = groups
        }
        root["hooks"] = hooks
        do {
            try AtomicFile.writeJSON(root, to: fileURL)
        } catch {
            return HookInstallResult(connected: false, hook: binaryURL.path, error: "could not write hooks")
        }
        let connected = nestedInstalled(fileURL: fileURL, binaryURL: binaryURL)
        return HookInstallResult(connected: connected, hook: binaryURL.path, error: connected ? nil : "hook program missing")
    }

    private static func geminiInstalled(fileURL: URL, binaryURL: URL) -> Bool {
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else { return false }
        let hooks = read(fileURL)["hooks"] as? [String: Any] ?? [:]
        let needle = "--source gemini"
        for name in geminiEvents {
            for group in hooks[name] as? [[String: Any]] ?? [] {
                for entry in group["hooks"] as? [[String: Any]] ?? [] where isOurs(entry) {
                    let command = entry["command"] as? String ?? ""
                    if command.contains(binaryURL.path), command.contains(needle) {
                        return true
                    }
                }
            }
        }
        return false
    }

    private static func nestedInstalled(fileURL: URL, binaryURL: URL) -> Bool {
        guard FileManager.default.isExecutableFile(atPath: binaryURL.path) else { return false }
        let groups = (read(fileURL)["hooks"] as? [String: Any])?["UserPromptSubmit"] as? [[String: Any]] ?? []
        for group in groups {
            for entry in group["hooks"] as? [[String: Any]] ?? [] where isOurs(entry) {
                let command = entry["command"] as? String ?? ""
                if command.contains(binaryURL.path) || command.contains("MochiHookCLI") {
                    return true
                }
            }
        }
        return false
    }

    private static func shellCommand(_ binary: URL, source: String) -> String {
        let path = binary.path.contains(" ") ? "\"\(binary.path)\"" : binary.path
        return "\(path) --source \(source)"
    }
}
