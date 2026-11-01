import AppKit
import Foundation
import MochiCore

@MainActor
final class AppSession {
    private let petStore: PetStore
    private let prefsStore: PrefsStore
    private let slotDisk: DialogueSlotDisk
    private var pet: PetState
    private var prefs: IslandPrefs
    private var slots: DialogueSlotStore
    private var tracker = TaskTransitionTracker()
    private var seenStatus: [String: String] = [:]

    init() {
        petStore = PetStore.live()
        prefsStore = PrefsStore.live()
        slotDisk = DialogueSlotDisk.live()
        pet = petStore.load(migrating: Self.legacyFiles("src/pet_state.json"))
        prefs = prefsStore.load(migrating: Self.legacyFiles("island_offset.json"))
        let now = Date().timeIntervalSince1970
        HookEvents.scrubStored(tasksURL: AppConfig.cursorDirectory.appendingPathComponent("mochi-island-tasks.json"))
        var active = Set(
            TaskReader.load(
                tasksURL: AppConfig.cursorDirectory.appendingPathComponent("mochi-island-tasks.json"),
                now: now,
                includePrompts: false
            ).map(\.id)
        )
        if prefs.scanTranscripts {
            active.formUnion(TaskReader.transcriptHints(now: now).map(\.id))
        }
        slots = slotDisk.load(activeTaskIDs: active)
        repairInstalledHooks()
    }

    /// Primary connectors (Cursor, Codex, …) must run the MochiHookCLI inside this app.
    func repairInstalledHooks() {
        let binary = hookBinary()
        guard FileManager.default.isExecutableFile(atPath: binary.path) else { return }
        AgentConnectorRegistry.retargetAll(binaryURL: binary)
    }

    var currentPrefs: IslandPrefs { prefs }

    func replacePrefs(_ json: [String: Any]) {
        prefs = IslandPrefs.parse(json, base: prefs)
        prefsStore.save(prefs)
        if let selected = json["selected"] as? String, slots.slots.contains(where: { $0.id == selected }) {
            slots.selected = selected
            slotDisk.save(slots)
        }
    }

    func statePayload() -> [String: Any] {
        let now = Date().timeIntervalSince1970
        let before = pet
        PetEngine.tick(&pet, now: now)
        let tasks = visibleTasks(now: now)
        for id in tracker.newlyCompleted(tasks) {
            _ = PetEngine.feedFromAI(&pet, taskID: id, now: now)
        }
        if pet != before {
            petStore.save(pet)
        }
        absorbTasks(tasks)
        var payload = PetEngine.snapshot(pet, now: now)
        payload["slots"] = slots.slots.map(\.dictionary)
        payload["selected"] = slots.selected
        payload["dock"] = prefs.dock
        return payload
    }

    func play() -> [String: Any] {
        PetEngine.play(&pet, now: Date().timeIntervalSince1970)
        petStore.save(pet)
        return PetEngine.snapshot(pet, now: Date().timeIntervalSince1970)
    }

    func tasksPayload() -> [[String: Any]] {
        let tasks = visibleTasks(now: Date().timeIntervalSince1970)
        absorbTasks(tasks)
        return tasks.map(\.dictionary)
    }

    func createSlot(source: String) -> [String: Any] {
        var rng = SystemRandomNumberGenerator()
        let (next, slot) = slots.created(source: source, now: Date().timeIntervalSince1970, rng: &rng)
        slots = next
        slotDisk.save(slots)
        return [
            "ok": true,
            "slot": slot.dictionary,
            "slots": slots.slots.map(\.dictionary),
            "selected": slots.selected
        ]
    }

    func selectSlot(_ id: String) -> [String: Any] {
        let next = slots.selecting(id)
        if next != slots {
            slots = next
            slotDisk.save(slots)
        }
        return [
            "ok": true,
            "selected": slots.selected,
            "slots": slots.slots.map(\.dictionary)
        ]
    }

    func linkPayload(agent: String, action: String, acknowledged: Bool) -> [String: Any] {
        let binary = hookBinary()
        let name = agent.isEmpty ? "cursor" : agent
        if action == "connect" || action == "disconnect" {
            if action == "connect" && !prefs.cursorPrivacyAccepted && !acknowledged {
                var payload = agentStatus(binary: binary)
                payload["needsPrivacy"] = true
                payload["connected"] = false
                payload["scanTranscripts"] = prefs.scanTranscripts
                return payload
            }
            if action == "connect" {
                prefs.cursorPrivacyAccepted = true
                prefsStore.save(prefs)
            }
            _ = install(agent: name, action: action, binary: binary)
        }
        var payload = agentStatus(binary: binary)
        payload["scanTranscripts"] = prefs.scanTranscripts
        return payload
    }

    private func install(agent: String, action: String, binary: URL) -> HookInstallResult {
        if let connector = AgentConnectorRegistry.connector(for: agent) {
            return action == "disconnect"
                ? connector.disconnect(binaryURL: binary)
                : connector.connect(binaryURL: binary, privacyAccepted: true)
        }
        switch agent {
        case "claude":
            if action == "disconnect" {
                return HookInstaller.removeNested(fileURL: claudeSettingsURL, binaryURL: binary)
            }
            return HookInstaller.installClaude(
                settingsURL: claudeSettingsURL,
                binaryURL: binary,
                privacyAccepted: true
            )
        case "copilot":
            if action == "disconnect" {
                return PassiveHooks.remove(fileURL: copilotHookURL, binaryURL: binary)
            }
            return PassiveHooks.installCopilot(directory: copilotHooksDirectory, binaryURL: binary, privacyAccepted: true)
        case "opencode":
            if action == "disconnect" {
                return PassiveHooks.remove(fileURL: openCodePluginURL, binaryURL: binary)
            }
            return PassiveHooks.installOpenCode(pluginURL: openCodePluginURL, binaryURL: binary, privacyAccepted: true)
        case "grok", "grok-bot":
            if action == "disconnect" {
                return PassiveHooks.remove(fileURL: grokHookURL, binaryURL: binary)
            }
            return PassiveHooks.installGrok(directory: grokHooksDirectory, binaryURL: binary, privacyAccepted: true)
        case "gemini":
            if action == "disconnect" {
                _ = HookInstaller.removeNested(fileURL: geminiSettingsURL, binaryURL: binary)
                return HookInstaller.geminiStatus(settingsURL: geminiSettingsURL, binaryURL: binary)
            }
            return HookInstaller.installGemini(settingsURL: geminiSettingsURL, binaryURL: binary, privacyAccepted: true)
        default:
            if action == "disconnect" {
                return HookInstaller.remove(hooksURL: hooksURL, binaryURL: binary, tasksURL: tasksURL)
            }
            return HookInstaller.install(
                hooksURL: hooksURL,
                binaryURL: binary,
                tasksURL: tasksURL,
                privacyAccepted: true
            )
        }
    }

    private func agentStatus(binary: URL) -> [String: Any] {
        let cursor = AgentConnectorRegistry.connector(for: "cursor")?.status(binaryURL: binary)
            ?? HookInstaller.status(hooksURL: hooksURL, binaryURL: binary, tasksURL: tasksURL)
        let codex = AgentConnectorRegistry.connector(for: "codex")?.status(binaryURL: binary)
            ?? HookInstaller.codexStatus(configURL: codexConfigURL, hooksURL: codexHooksURL, binaryURL: binary)
        let claude = HookInstaller.claudeStatus(settingsURL: claudeSettingsURL, binaryURL: binary)
        let copilot = PassiveHooks.copilotStatus(directory: copilotHooksDirectory, binaryURL: binary)
        let opencode = PassiveHooks.openCodeStatus(pluginURL: openCodePluginURL, binaryURL: binary)
        let grok = PassiveHooks.grokStatus(directory: grokHooksDirectory, binaryURL: binary)
        let gemini = HookInstaller.geminiStatus(settingsURL: geminiSettingsURL, binaryURL: binary)
        var payload = cursor.dictionary
        payload["agents"] = [
            "cursor": ["connected": cursor.connected],
            "codex": ["connected": codex.connected],
            "claude": ["connected": claude.connected],
            "copilot": ["connected": copilot.connected],
            "opencode": ["connected": opencode.connected],
            "grok": ["connected": grok.connected],
            "gemini": ["connected": gemini.connected]
        ]
        return payload
    }

    func choicePayload(_ body: [String: Any]) -> [String: Any] {
        let kind = body["kind"] as? String ?? ""
        let action = (body["option_id"] as? String) ?? ""
        let id = body["id"] as? String ?? ""
        let requestID = body["request_id"] as? String ?? ""
        do {
            try ReplyWriter.answerPermission(
                id: id,
                requestID: requestID,
                action: action,
                kind: kind,
                replyURL: replyURL,
                tasksURL: tasksURL
            )
            return ["ok": true]
        } catch {
            return ["ok": false, "error": "Could not save the reply. Choose it in Cursor."]
        }
    }

    func openAgent(source: String, taskID: String) -> [String: Any] {
        let plan = AgentLaunch.plan(source: source)
        let normalized = AgentLaunch.normalize(source)
        if normalized == "mochi" {
            return ["ok": true, "opened": false, "source": normalized, "id": taskID]
        }
        if let path = plan.appPath {
            NSWorkspace.shared.open(URL(fileURLWithPath: path))
            return ["ok": true, "opened": true, "source": normalized, "app": path, "id": taskID]
        }
        if let raw = plan.url, let url = URL(string: raw) {
            NSWorkspace.shared.open(url)
            var payload: [String: Any] = [
                "ok": true,
                "opened": true,
                "source": normalized,
                "url": raw,
                "id": taskID
            ]
            if let note = plan.note {
                payload["note"] = note
            }
            return payload
        }
        return ["ok": false, "source": normalized, "id": taskID, "error": plan.note ?? "Could not open that agent."]
    }

    func setScanTranscripts(_ enabled: Bool) {
        prefs.scanTranscripts = enabled
        prefsStore.save(prefs)
    }

    func markRead(_ id: String) {
        let next = slots.markingRead(id)
        guard next != slots else { return }
        slots = next
        slotDisk.save(slots)
    }

    func retireReadDone() {
        let next = slots.removingReadDone(tasks: visibleTasks(now: Date().timeIntervalSince1970))
        guard next != slots else { return }
        slots = next
        slotDisk.save(slots)
    }

    func saveKittenPosition(x: Double, y: Double) {
        prefs.kittenX = min(1, max(0, x))
        prefs.kittenY = min(1, max(0, y))
        prefs.kittenPlaced = true
        prefs.dock = prefs.kittenX < 0.5 ? "left" : "right"
        prefsStore.save(prefs)
    }

    func resetKittenPosition() {
        prefs.kittenPlaced = false
        prefs.kittenX = 1
        prefs.kittenY = 0.5
        prefs.dock = "right"
        prefsStore.save(prefs)
    }

    private func absorbTasks(_ tasks: [CursorTask]) {
        var next = slots.merged(with: tasks)
        for task in tasks {
            if let previous = seenStatus[task.id], previous != task.status,
               let index = next.slots.firstIndex(where: { $0.id == task.id }) {
                next.slots[index].read = false
            }
            seenStatus[task.id] = task.status
        }
        next = next.normalized()
        guard next != slots else { return }
        slots = next
        slotDisk.save(slots)
    }

    private func visibleTasks(now: Double) -> [CursorTask] {
        var tasks = TaskReader.load(tasksURL: tasksURL, now: now, includePrompts: true)
        guard prefs.scanTranscripts else { return tasks }
        let known = Set(tasks.map(\.id))
        for hint in TaskReader.transcriptHints(now: now) where !known.contains(hint.id) {
            tasks.append(hint)
        }
        return tasks.sorted { $0.id < $1.id }
    }

    private func hookBinary() -> URL {
        if let bundled = Bundle.main.url(forAuxiliaryExecutable: "MochiHookCLI") {
            return bundled
        }
        return Bundle.main.bundleURL
            .appendingPathComponent("Contents/MacOS/MochiHookCLI")
    }

    private var tasksURL: URL {
        AppConfig.cursorDirectory.appendingPathComponent("mochi-island-tasks.json")
    }

    private var replyURL: URL {
        AppConfig.cursorDirectory.appendingPathComponent("mochi-island-reply.json")
    }

    private var hooksURL: URL {
        AppConfig.cursorDirectory.appendingPathComponent("hooks.json")
    }

    private var codexConfigURL: URL {
        AppConfig.homeDirectory.appendingPathComponent(".codex/config.toml")
    }

    private var codexHooksURL: URL {
        AppConfig.homeDirectory.appendingPathComponent(".codex/hooks.json")
    }

    private var claudeSettingsURL: URL {
        AppConfig.homeDirectory.appendingPathComponent(".claude/settings.json")
    }

    private var geminiSettingsURL: URL {
        AppConfig.homeDirectory.appendingPathComponent(".gemini/settings.json")
    }

    private var copilotHooksDirectory: URL {
        PassiveHooks.copilotDirectory(home: AppConfig.homeDirectory, environment: ProcessInfo.processInfo.environment)
    }

    private var grokHooksDirectory: URL {
        PassiveHooks.grokDirectory(home: AppConfig.homeDirectory)
    }

    private var grokHookURL: URL {
        grokHooksDirectory.appendingPathComponent("mochi-island.json")
    }

    private var copilotHookURL: URL {
        copilotHooksDirectory.appendingPathComponent("mochi-island.json")
    }

    private var openCodePluginURL: URL {
        PassiveHooks.openCodePluginURL(home: AppConfig.homeDirectory)
    }

    private static func legacyFiles(_ relative: String) -> [URL] {
        var urls: [URL] = []
        let cwd = URL(fileURLWithPath: FileManager.default.currentDirectoryPath, isDirectory: true)
        urls.append(cwd.appendingPathComponent(relative))
        let bundleParent = Bundle.main.bundleURL.deletingLastPathComponent().deletingLastPathComponent()
        urls.append(bundleParent.appendingPathComponent(relative))
        return urls
    }
}
