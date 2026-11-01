import Foundation
import MochiCore

func check(_ condition: @autoclosure () -> Bool, _ name: String = #function, line: Int = #line) {
    if !condition() {
        fputs("FAIL \(name):\(line)\n", stderr)
        exit(1)
    }
}

func PlayReducesEnergy() {
        var state = PetState(hunger: 10, energy: 50, happiness: 40, lastDecayTs: 1_000_000)
        PetEngine.play(&state, now: 1_000_000)
        check(state.energy == 50 + PetRules.playEnergy)
        check(state.lastAction == "play")
    }

func DecayPutsThePetToSleepWithoutDeath() {
        var state = PetState(hunger: 94, energy: 50, happiness: 40, lastDecayTs: 0)
        PetEngine.tick(&state, now: PetRules.decayInterval)
        check(state.asleep)
        check(state.hunger >= PetRules.asleepHunger)
        let frozen = state
        PetEngine.tick(&state, now: PetRules.decayInterval * 4)
        check(state.hunger == frozen.hunger)
        check(state.energy == frozen.energy)
    }

func PlayDoesNothingWhileAsleep() {
        var state = PetState(energy: 40, asleep: true, lastDecayTs: 10)
        PetEngine.play(&state, now: 10)
        check(state.energy == 40)
        check(state.asleep)
        check(state.lastAction != "play")
    }

func FeedFromAIWakesAndDedupes() {
        var state = PetState(hunger: 90, energy: 0, happiness: 20, asleep: true, lastDecayTs: 50)
        check(PetEngine.feedFromAI(&state, taskID: "task-a", now: 50))
        check(!state.asleep)
        check(state.hunger < 90)
        let after = state
        check(!PetEngine.feedFromAI(&state, taskID: "task-a", now: 50))
        check(state == after)
    }

func FedTaskIDsStayBounded() {
        var state = PetState(lastDecayTs: 10)
        for index in 0..<(AppConfig.fedTaskLimit + 5) {
            _ = PetEngine.feedFromAI(&state, taskID: "id-\(index)", now: 10)
        }
        check(state.fedTaskIDs.count == AppConfig.fedTaskLimit)
        check(!state.fedTaskIDs.contains("id-0"))
        check(state.fedTaskIDs.contains("id-\(AppConfig.fedTaskLimit + 4)"))
    }

func LowHappinessDoesNotSleep() {
        var state = PetState(hunger: 10, energy: 80, happiness: 0, lastDecayTs: 5)
        PetEngine.tick(&state, now: 5)
        check(!state.asleep)
        check(PetEngine.mood(state, now: 5) == "angry")
    }

func MoodsMatchTheOldThresholds() {
        let now = 1_000_000.0
        var happy = PetState(lastDecayTs: now)
        check(PetEngine.mood(happy, now: now) == "happy")

        var playful = PetState(hunger: 30, energy: 50, lastDecayTs: now, lastAction: "play", lastActionTs: now)
        check(PetEngine.mood(playful, now: now) == "playful")

        var sleepy = PetState(hunger: 30, energy: 20, lastDecayTs: now)
        check(PetEngine.mood(sleepy, now: now) == "sleepy")

        var hungry = PetState(hunger: 60, energy: 70, lastDecayTs: now)
        check(PetEngine.mood(hungry, now: now) == "hungry")

        var angry = PetState(hunger: 60, energy: 50, lastDecayTs: now, lastAction: "play", lastActionTs: now)
        check(PetEngine.mood(angry, now: now) == "angry")
        _ = happy
    }

func LegacyDeadSaveBecomesAsleep() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let legacy = directory.appendingPathComponent("old.json")
        let raw = #"{"name":"Mochi","hunger":99,"energy":10,"happiness":10,"is_alive":false,"last_decay_ts":1,"last_action":"","last_action_ts":0}"#
        try raw.data(using: .utf8)?.write(to: legacy)
        let store = PetStore(directory: directory.appendingPathComponent("live"))
        let loaded = store.load(migrating: [legacy])
        check(loaded.asleep)
        check(loaded.hunger == 99)
        check(FileManager.default.fileExists(atPath: store.fileURL.path))
    }

func SaveClampsHunger() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let store = PetStore(directory: directory)
        store.save(PetState(name: "TestCat", hunger: 150))
        let loaded = store.load()
        check(loaded.name == "TestCat")
        check(loaded.hunger == 100)
    }

func OnlyRunningToDoneFeeds() {
        var tracker = TaskTransitionTracker()
        let first = tracker.newlyCompleted([
            CursorTask(id: "a", title: "A", status: "running"),
            CursorTask(id: "b", title: "B", status: "done")
        ])
        check(first.isEmpty)
        let second = tracker.newlyCompleted([
            CursorTask(id: "a", title: "A", status: "done"),
            CursorTask(id: "b", title: "B", status: "done"),
            CursorTask(id: "c", title: "C", status: "fail")
        ])
        check(second == ["a"])
    }

func HookInstallBacksUpAndMarksEntries() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let binary = directory.appendingPathComponent("MochiHookCLI")
        try Data("#!/bin/sh\nexit 0\n".utf8).write(to: binary)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: binary.path)
        let hooks = directory.appendingPathComponent("hooks.json")
        let raw = #"{"version":1,"hooks":{"sessionStart":[{"command":"/bin/echo keep"}],"stop":[{"command":"/tmp/vibe-island-bridge --source cursor"}]}}"#
        try raw.data(using: .utf8)?.write(to: hooks)
        let tasks = directory.appendingPathComponent("tasks.json")
        let blocked = HookInstaller.install(hooksURL: hooks, binaryURL: binary, tasksURL: tasks, privacyAccepted: false)
        check(blocked.needsPrivacy)
        let installed = HookInstaller.install(hooksURL: hooks, binaryURL: binary, tasksURL: tasks, privacyAccepted: true)
        check(installed.connected)
        check(FileManager.default.fileExists(atPath: hooks.appendingPathExtension("mochi-island.bak").path))
        let data = try Data(contentsOf: hooks)
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        let table = json?["hooks"] as? [String: Any]
        let entries = table?["sessionStart"] as? [[String: Any]]
        check(entries?.count == 2)
        check(entries?.first?[AppConfig.hookMarker] as? Int == AppConfig.hookVersion)
        check(entries?.last?["command"] as? String == "/bin/echo keep")
        let stop = table?["stop"] as? [[String: Any]]
        check(stop?.count == 2)
        check(stop?.first?[AppConfig.hookMarker] as? Int == AppConfig.hookVersion)
        check((stop?.last?["command"] as? String)?.contains("vibe-island-bridge") == true)
        for name in ["afterAgentResponse", "afterFileEdit"] {
            let list = table?[name] as? [[String: Any]]
            check(list?.first?[AppConfig.hookMarker] as? Int == AppConfig.hookVersion)
        }
        let shell = table?["beforeShellExecution"] as? [[String: Any]]
        check(shell?.first?["timeout"] as? Int == 300)
        let removed = HookInstaller.remove(hooksURL: hooks, binaryURL: binary, tasksURL: tasks)
        check(!removed.connected)
        let after = try JSONSerialization.jsonObject(with: Data(contentsOf: hooks)) as? [String: Any]
        let left = (after?["hooks"] as? [String: Any])?["sessionStart"] as? [[String: Any]]
        check(left?.count == 1)
        check(left?.first?["command"] as? String == "/bin/echo keep")
        let leftStop = (after?["hooks"] as? [String: Any])?["stop"] as? [[String: Any]]
        check(leftStop?.count == 1)
        check((leftStop?.first?["command"] as? String)?.contains("vibe-island-bridge") == true)
    }

func TaskWatchFiresWhenTheFileIsReplaced() throws {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-watch-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let file = directory.appendingPathComponent("tasks.json")
    let watch = TaskFileWatch(fileURL: file)
    let lock = NSLock()
    var hits = 0
    watch.start {
        lock.lock()
        hits += 1
        lock.unlock()
    }
    func count() -> Int {
        lock.lock()
        defer { lock.unlock() }
        return hits
    }
    func replace(_ id: String) throws {
        let payload: [String: Any] = [
            "tasks": [id: ["id": id, "status": "running", "updated": Date().timeIntervalSince1970]]
        ]
        let data = try JSONSerialization.data(withJSONObject: payload)
        let temp = directory.appendingPathComponent(".\(UUID().uuidString).tmp")
        try data.write(to: temp)
        if FileManager.default.fileExists(atPath: file.path) {
            _ = try FileManager.default.replaceItemAt(file, withItemAt: temp)
        } else {
            try FileManager.default.moveItem(at: temp, to: file)
        }
    }
    func waitForIncrease(from seen: Int) -> Bool {
        var attempts = 0
        let deadline = Date().addingTimeInterval(1.5)
        while count() == seen && Date() < deadline {
            if attempts < 2 {
                try? replace("n-\(seen)-\(attempts)")
                attempts += 1
                Thread.sleep(forTimeInterval: 0.08)
            } else {
                Thread.sleep(forTimeInterval: 0.02)
            }
        }
        return count() > seen
    }
    Thread.sleep(forTimeInterval: 0.15)
    check(waitForIncrease(from: 0))
    check(waitForIncrease(from: count()))
    watch.stop()
}

func ReplyWriterOnlyAllowsPermissionActions() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".json")
        try ReplyWriter.writePermission(id: "t", requestID: "r", action: "allow", to: url)
        let saved = try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any]
        check(saved?["action"] as? String == "allow")
        check(saved?["kind"] as? String == "permission")
        var threw = false
        do {
            try ReplyWriter.writePermission(id: "t", requestID: "r", action: "approve", to: url)
        } catch {
            threw = true
        }
        check(threw)
        let choiceURL = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".json")
        try ReplyWriter.writeChoice(id: "t2", requestID: "r2", action: "approve", kind: "plan", to: choiceURL)
        let choice = try JSONSerialization.jsonObject(with: Data(contentsOf: choiceURL)) as? [String: Any]
        check(choice?["action"] as? String == "approve")
        check(choice?["kind"] as? String == "plan")
        check(choice?["option_id"] as? String == "approve")
        check(ReplyWriter.closesBubble(kind: "permission", optionID: "allow"))
        check(ReplyWriter.closesBubble(kind: "permission", optionID: "deny"))
        check(ReplyWriter.closesBubble(kind: "", optionID: "Allow"))
        check(ReplyWriter.closesBubble(kind: "mode", optionID: "deny"))
        check(ReplyWriter.closesBubble(kind: "plan", optionID: "approve") == false)
    }

func TaskReaderKeepsSource() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-src-\(UUID().uuidString).json")
        let now = Date().timeIntervalSince1970
        let payload: [String: Any] = [
            "tasks": [
                "c": ["id": "c", "title": "Cursor", "status": "running", "updated": now, "source": "cursor"],
                "x": ["id": "x", "title": "Codex", "status": "running", "updated": now, "source": "codex"],
                "g": ["id": "g", "title": "Grok", "status": "running", "updated": now, "source": "grok-bot"],
                "m": ["id": "m", "title": "Cat", "status": "running", "updated": now, "source": "cat"],
                "z": ["id": "z", "title": "Other", "status": "running", "updated": now, "source": "nope"],
                "p": ["id": "p", "title": "GPT", "status": "running", "updated": now, "source": "chatgpt"]
            ]
        ]
        try JSONSerialization.data(withJSONObject: payload).write(to: url)
        let tasks = TaskReader.load(tasksURL: url, now: now)
        let byID = Dictionary(uniqueKeysWithValues: tasks.map { ($0.id, $0.source) })
        check(byID["c"] == "cursor")
        check(byID["x"] == "codex")
        check(byID["g"] == "grok-bot")
        check(byID["m"] == "mochi")
        check(byID["z"] == "cursor")
        check(byID["p"] == "gpt")
        check(tasks.first { $0.id == "x" }?.dictionary["source"] as? String == "codex")
    }

func AgentLaunchFindsInstalledAppsAndCodexFallback() {
        let cursor = AgentLaunch.plan(source: "Cursor") { path in
            path.hasSuffix("/Cursor.app")
        }
        check(cursor.appPath?.hasSuffix("/Cursor.app") == true)
        check(cursor.url == nil)
        let missing = AgentLaunch.plan(source: "codex") { _ in false }
        check(missing.appPath == nil)
        check(missing.url == "https://chatgpt.com/codex")
        check(missing.note?.contains("Codex.app") == true)
        let grok = AgentLaunch.plan(source: "grok bot") { path in
            path.hasSuffix("/Grok Bot.app")
        }
        check(grok.appPath?.hasSuffix("/Grok Bot.app") == true)
        check(AgentLaunch.normalize("cat") == "mochi")
        check(AgentLaunch.normalize("小猫") == "mochi")
        let cat = AgentLaunch.plan(source: "mochi") { _ in true }
        check(cat.appPath == nil)
        check(cat.url == nil)
        check(cat.note?.contains("小猫") == true)
        let home = AppConfig.homeDirectory
        check(home.isFileURL)
        check(!home.path.isEmpty)
        check(AppConfig.cursorDirectory.path.hasPrefix(home.path))
        check(AppConfig.supportDirectory.path.contains(AppConfig.bundleID))
        let fromHome = AgentLaunch.plan(source: "cursor", home: home) { _ in false }
        check(fromHome.url == "https://cursor.com")
        check(AgentLaunch.normalize("gpt") == "gpt")
        check(AgentLaunch.normalize("ChatGPT") == "gpt")
        check(AgentLaunch.normalize("openai") == "gpt")
        let gpt = AgentLaunch.plan(source: "gpt") { path in
            path.hasSuffix("/ChatGPT.app")
        }
        check(gpt.appPath?.hasSuffix("/ChatGPT.app") == true)
        check(gpt.url == nil)
        let gptMissing = AgentLaunch.plan(source: "chatgpt") { _ in false }
        check(gptMissing.appPath == nil)
        check(gptMissing.url == "https://chatgpt.com")
        check(gptMissing.note?.contains("ChatGPT.app") == true)
        var rng = SplitMix64(seed: 1)
        let created = DialogueSlotStore().created(source: "openai", now: 3, rng: &rng).1
        check(created.source == "gpt")
        check(created.skin == "gpt")
        check(created.read == false)
        check(SlotFace.displayName(for: "gpt") == "GPT")
        check(SlotFace.agentMark(for: "gpt") == "agent-gpt")
        check(SlotFace.agentMark(for: "cursor") == "agent-cursor")
        check(SlotFace.agentMark(for: "Codex") == "agent-codex")
        check(SlotFace.agentMark(for: "grok") == "agent-grok")
        check(SlotFace.agentMark(for: "claude") == "agent-claude")
        check(SlotFace.displayName(for: "claude") == "Claude")
    }

struct SplitMix64: RandomNumberGenerator {
    var state: UInt64
    init(seed: UInt64) { self.state = seed }
    mutating func next() -> UInt64 {
        state &+= 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }
}

func KittenPositionParsesAndClamps() throws {
    let high = IslandPrefs.parse(["kitten_x": 1.4, "kitten_y": -0.2, "kitten_placed": true])
    check(high.kittenX == 1)
    check(high.kittenY == 0)
    check(high.kittenPlaced)
    check(high.dock == "right")
    check(high.dictionary["kitten_x"] as? Double == 1)
    let left = IslandPrefs.parse(["kitten_x": 0.2, "kitten_y": 0.4, "kitten_placed": true], base: IslandPrefs(dock: "right"))
    check(left.kittenX == 0.2)
    check(left.kittenY == 0.4)
    check(left.dock == "left")
    let fresh = IslandPrefs.parse([:])
    check(fresh.kittenPlaced == false)
    check(fresh.dock == "right")
    let data = try JSONEncoder().encode(high)
    let text = String(data: data, encoding: .utf8) ?? ""
    check(text.contains("kitten_x"))
    check(text.contains("kitten_y"))
    check(text.contains("kitten_placed"))
    let decoded = try JSONDecoder().decode(IslandPrefs.self, from: data)
    check(decoded.kittenX == 1)
    check(decoded.kittenY == 0)
    check(decoded.kittenPlaced)
    check(decoded.dock == "right")
    let legacy = try JSONDecoder().decode(IslandPrefs.self, from: Data("{\"dx\":2,\"dock\":\"left\"}".utf8))
    check(legacy.dx == 2)
    check(legacy.dock == "left")
    check(legacy.kittenPlaced == false)
}

func SpecConstantsAndShellGeometry() {
    check(AppConfig.materialAPI == "glassEffect.regular")
    check(AppConfig.bubbleRadius == 12)
    check(AppConfig.beakWidth == 10)
    check(AppConfig.beakHeight == 16)
    check(AppConfig.beakGap == 6)
    check(abs(AppConfig.beakTipRadius - 2.8) < 0.001)
    check(AppConfig.bubbleMinWidth == 160)
    check(AppConfig.bubbleMaxWidth == 220)
    check(AppConfig.bubblePadX == 12)
    check(AppConfig.bubblePadY == 8)
    check(AppConfig.bubbleMinHeight == 44)
    check(AppConfig.doneKeep >= 24 * 60 * 60)
    check(AppConfig.kittenSize == 56)
    check(AppConfig.capsuleWidth == 56)
    check(AppConfig.capsuleRadius == 28)
    check(AppConfig.capsuleWidth == AppConfig.kittenSize)
    check(AppConfig.agentIcon == 28)
    check(AppConfig.capsuleWidth - AppConfig.capsulePadX * 2 >= AppConfig.agentIcon)
    check(AppConfig.rowGap == 4)
    check(AppConfig.dragThreshold == 3)
    check(AppConfig.screenInset == 8)
    check(AppConfig.settingsBarHeight == 32)
    check(AppConfig.settingsSelectRadius == 14)
    check(AppConfig.settingsTabMinWidth == 64)
    check(AppConfig.settingsWidth == 420)
    check(AppConfig.glassTintRed == 28)
    check(AppConfig.glassTintGreen == 28)
    check(AppConfig.glassTintBlue == 30)
    check(ShellGeometry.contentHeight(agentCount: 0) == 169)
    check(ShellGeometry.contentHeight(agentCount: 1) == 217)
    check(ShellGeometry.contentHeight(agentCount: 4) == 361)
    check(ShellGeometry.contentHeight(agentCount: 8) == 553)
    let screen = CGRect(x: 0, y: 0, width: 1440, height: 900)
    let anchor = ShellGeometry.defaultAnchor(screen: screen, visible: screen)
    check(abs(anchor.x - (1440 - 8 - 28)) < 0.1)
    check(abs(anchor.y - 450) < 0.1)
    let ratios = ShellGeometry.ratios(anchor: anchor, screen: screen)
    check(ratios.x > 0.9 && ratios.x <= 1)
    check(abs(ratios.y - 0.5) < 0.02)
    let clamped = ShellGeometry.ratios(anchor: CGPoint(x: -40, y: 2000), screen: screen)
    check(clamped.x == 0)
    check(clamped.y == 1)
    let visible = CGRect(x: 0, y: 0, width: 800, height: 500)
    let collapsed = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 40),
        expanded: false,
        agentCount: 4,
        visible: visible
    )
    check(collapsed.frame.height == 56)
    check(collapsed.frame.minY == 12)
    let opened = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 40),
        expanded: true,
        agentCount: 4,
        visible: visible
    )
    check(opened.upward)
    check(opened.scrolls == false)
    check(abs(opened.frame.minY - collapsed.frame.minY) < 0.1)
    check(abs(opened.frame.height - ShellGeometry.contentHeight(agentCount: 4)) < 0.1)
    check(opened.frame.maxY <= visible.maxY + 0.1)
    check(opened.frame.minY >= visible.minY - 0.1)
    // Empty capsule is 169pt. Kitten top at 100 leaves only 100pt below → must open upward.
    let shallow = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 72),
        expanded: true,
        agentCount: 0,
        visible: visible
    )
    let shallowClosed = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 72),
        expanded: false,
        agentCount: 0,
        visible: visible
    )
    check(shallow.upward)
    check(abs(shallow.frame.minY - shallowClosed.frame.minY) < 0.1)
    check(shallow.frame.maxY <= visible.maxY + 0.1)
    let roomy = CGRect(x: 0, y: 0, width: 800, height: 900)
    let grew = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 600),
        expanded: true,
        agentCount: 4,
        visible: roomy
    )
    let grewClosed = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 600),
        expanded: false,
        agentCount: 4,
        visible: roomy
    )
    let fine = IslandPrefs.parse(["pet_scale": 1.37])
    check(abs(fine.petScale - 1.37) < 0.0001)
    check(grew.upward == false)
    check(abs(grew.frame.maxY - grewClosed.frame.maxY) < 0.1)
    check(abs(grew.frame.midX - grewClosed.frame.midX) < 0.1)
    check(grew.frame.width == AppConfig.capsuleWidth)
    check(grewClosed.frame.width == AppConfig.kittenSize)
    check(grew.frame.width == grewClosed.frame.width)
    check(grew.frame.height == ShellGeometry.contentHeight(agentCount: 4))
    check(grew.scrolls == false)
    let edge = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 1440 - 8 - 28, y: 450),
        expanded: true,
        agentCount: 1,
        visible: screen
    )
    let edgeClosed = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 1440 - 8 - 28, y: 450),
        expanded: false,
        agentCount: 1,
        visible: screen
    )
    check(abs(edge.frame.midX - edgeClosed.frame.midX) < 0.1)
    check(abs(edge.frame.maxY - edgeClosed.frame.maxY) < 0.1)
    let crowded = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 250),
        expanded: true,
        agentCount: 20,
        visible: visible
    )
    check(crowded.scrolls)
    check(crowded.upward == false)
    check(crowded.frame.height <= visible.height)
    check(crowded.frame.minY >= visible.minY - 0.1)
    let lowScreen = CGRect(x: 0, y: 0, width: 800, height: 140)
    let lowClosed = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 40),
        expanded: false,
        agentCount: 4,
        visible: lowScreen
    )
    let lowOpen = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 40),
        expanded: true,
        agentCount: 4,
        visible: lowScreen
    )
    check(lowOpen.upward)
    check(lowOpen.scrolls)
    check(abs(lowOpen.frame.minY - lowClosed.frame.minY) < 0.1)
    check(lowOpen.frame.maxY <= lowScreen.maxY + 0.1)
    let downChrome = ShellGeometry.agentChrome(agentCount: 4, scale: 1, upward: false)
    let upChrome = ShellGeometry.agentChrome(agentCount: 4, scale: 1, upward: true)
    let block = 4 * AppConfig.agentRow + 3 * AppConfig.rowGap
    check(abs(downChrome.agentsTop + block + downChrome.tail - ShellGeometry.contentHeight(agentCount: 4)) < 0.1)
    check(abs(upChrome.agentsTop + block + upChrome.tail - ShellGeometry.contentHeight(agentCount: 4)) < 0.1)
    let rightCapsule = CGRect(x: 1000, y: 200, width: AppConfig.capsuleWidth, height: 361)
    let rightAnchor = CGRect(x: rightCapsule.midX - 14, y: 280, width: 28, height: 28)
    let rightBubble = BubblePlacer.place(anchor: rightAnchor, capsule: rightCapsule, size: CGSize(width: 220, height: 120), screen: screen)
    check(rightBubble.pointsRight)
    check(abs(rightBubble.frame.maxX - (rightCapsule.minX - AppConfig.beakGap)) < 0.1)
    check(rightBubble.frame.intersects(rightCapsule) == false)
    check(rightBubble.frame.maxX <= rightAnchor.minX - AppConfig.beakGap)
    check(abs((rightBubble.frame.maxY - rightBubble.beakY) - rightAnchor.midY) < 1)
    let leftCapsule = CGRect(x: 16, y: 200, width: AppConfig.capsuleWidth, height: 361)
    let leftAnchor = CGRect(x: leftCapsule.midX - 14, y: 280, width: 28, height: 28)
    let leftBubble = BubblePlacer.place(anchor: leftAnchor, capsule: leftCapsule, size: CGSize(width: 220, height: 120), screen: screen)
    check(leftBubble.pointsRight == false)
    check(abs(leftBubble.frame.minX - (leftCapsule.maxX + AppConfig.beakGap)) < 0.1)
    check(leftBubble.frame.intersects(leftCapsule) == false)
    check(leftBubble.frame.minX >= leftAnchor.maxX + AppConfig.beakGap)
    let tight = CGRect(x: 0, y: 0, width: 360, height: 400)
    let tightCapsule = CGRect(x: 200, y: 80, width: AppConfig.capsuleWidth, height: 220)
    let tightAnchor = CGRect(x: tightCapsule.midX - 14, y: 140, width: 28, height: 28)
    let tightBubble = BubblePlacer.place(anchor: tightAnchor, capsule: tightCapsule, size: CGSize(width: 220, height: 80), screen: tight)
    check(tightBubble.pointsRight)
    check(tightBubble.frame.intersects(tightCapsule) == false)
    check(tightBubble.frame.maxX <= tightCapsule.minX - AppConfig.beakGap + 0.1)
    let topCapsule = CGRect(x: 1000, y: 700, width: AppConfig.capsuleWidth, height: 180)
    let topAnchor = CGRect(x: topCapsule.midX - 14, y: 860, width: 28, height: 28)
    let topBubble = BubblePlacer.place(anchor: topAnchor, capsule: topCapsule, size: CGSize(width: 220, height: 180), screen: screen)
    check(topBubble.frame.maxY <= screen.maxY + 0.1)
    check(topBubble.frame.intersects(topCapsule) == false)
    check(topBubble.beakY >= AppConfig.bubbleRadius + 8 - 0.1)
    check(topBubble.beakY <= 180 - AppConfig.bubbleRadius - 8 + 0.1)
    let bottomCapsule = CGRect(x: 1000, y: 4, width: AppConfig.capsuleWidth, height: 180)
    let bottomAnchor = CGRect(x: bottomCapsule.midX - 14, y: 4, width: 28, height: 28)
    let bottomBubble = BubblePlacer.place(anchor: bottomAnchor, capsule: bottomCapsule, size: CGSize(width: 220, height: 180), screen: screen)
    check(bottomBubble.frame.minY >= screen.minY - 0.1)
    check(bottomBubble.frame.intersects(bottomCapsule) == false)
    check(bottomBubble.beakY >= AppConfig.bubbleRadius + 8 - 0.1)
    check(AppConfig.agentRow == 44)
    check(AppConfig.kittenMark == 40)
    let openH = ShellGeometry.contentHeight(agentCount: 1)
    let iconTop = AppConfig.capsulePadY + (AppConfig.agentRow - AppConfig.agentIcon) / 2
    let iconLeft = (AppConfig.capsuleWidth - AppConfig.agentIcon) / 2
    let iconBottom = openH - (iconTop + AppConfig.agentIcon)
    check(iconLeft >= AppConfig.capsulePadX)
    check(CapsuleOutline.contains(CGPoint(x: iconLeft + 1, y: openH - iconTop - 1), width: AppConfig.capsuleWidth, height: openH))
    check(CapsuleOutline.contains(CGPoint(x: iconLeft + AppConfig.agentIcon - 1, y: iconBottom + 1), width: AppConfig.capsuleWidth, height: openH))
    check(CapsuleOutline.contains(CGPoint(x: AppConfig.capsuleWidth / 2, y: openH / 2), width: AppConfig.capsuleWidth, height: openH))
    check(CapsuleOutline.contains(CGPoint(x: 1, y: 1), width: AppConfig.capsuleWidth, height: openH) == false)
    check(CapsuleOutline.contains(CGPoint(x: 1, y: openH - 1), width: AppConfig.capsuleWidth, height: openH) == false)
    let tallScreen = CGRect(x: 0, y: 0, width: 800, height: 2000)
    let doubled = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 1400),
        expanded: true,
        agentCount: 4,
        visible: tallScreen,
        scale: 2
    )
    let doubledClosed = ShellGeometry.placeCapsule(
        anchorCenter: CGPoint(x: 400, y: 1400),
        expanded: false,
        agentCount: 4,
        visible: tallScreen,
        scale: 2
    )
    check(doubledClosed.frame.width == AppConfig.kittenSize * 2)
    check(doubledClosed.frame.height == AppConfig.kittenSize * 2)
    check(doubled.frame.width == AppConfig.capsuleWidth * 2)
    check(doubled.frame.width == doubledClosed.frame.width)
    check(abs(doubled.frame.height - ShellGeometry.contentHeight(agentCount: 4) * 2) < 0.1)
    check(abs(doubled.frame.maxY - doubledClosed.frame.maxY) < 0.1)
    check(abs(doubled.frame.midX - doubledClosed.frame.midX) < 0.1)
    check(ShellGeometry.contentHeight(agentCount: 0, scale: 2) == 338)
    let wide = AppConfig.capsuleWidth * 2
    let tall = ShellGeometry.contentHeight(agentCount: 1, scale: 2)
    let icon = AppConfig.agentIcon * 2
    let insetX = (wide - icon) / 2
    let insetTop = (AppConfig.capsulePadY + (AppConfig.agentRow - AppConfig.agentIcon) / 2) * 2
    check(CapsuleOutline.contains(CGPoint(x: insetX + 1, y: tall - insetTop - 1), width: wide, height: tall))
    check(CapsuleOutline.contains(CGPoint(x: 2, y: 2), width: wide, height: tall) == false)
    check(BubbleAge.relative(updated: 0, now: 100) == "")
    check(BubbleAge.relative(updated: 100, now: 130) == "now")
    check(BubbleAge.relative(updated: 100, now: 160) == "1m")
    check(BubbleAge.relative(updated: 100, now: 100 + 3600) == "1h")
    check(BubbleAge.relative(updated: 100, now: 100 + 47 * 3600) == "47h")
    check(BubbleAge.relative(updated: 100, now: 100 + 50 * 3600) == "2d")
    check(abs(ShellScale.factor(1.37) - 1.37) < 0.0001)
    check(IslandPrefs.parse(["pet_scale": 1.5]).petScale == 1.5)
    check(IslandPrefs.parse(["pet_scale": 4]).petScale == 2)
    check(IslandPrefs.parse(["pet_scale": 0]).petScale == 1)
    check(IslandPrefs.petLooks == ["a", "b", "c", "d", "e", "f"])
    check(IslandPrefs.parse(["look": "b"]).look == "b")
    check(IslandPrefs.parse(["look": "mochi"]).look == "a")
    check(IslandPrefs.parse(["look": "!!!"], base: IslandPrefs(look: "c")).look == "c")
    check(IslandPrefs.normalizeLook("F") == "f")
    check(IslandPrefs.petLetter(for: "d") == "D")
    check(IslandPrefs().look == "a")
    let keptLook = IslandPrefs.parse(["pet_scale": 1.2], base: IslandPrefs(look: "e"))
    check(keptLook.look == "e")
    check(keptLook.petScale == 1.2)
    var gate = AlertSoundGate()
    check(gate.shouldPlay(now: 10))
    check(gate.shouldPlay(now: 10.4) == false)
    check(gate.shouldPlay(now: 11))
    for name in IslandPrefs.soundIDs {
        check(ToneSynth.peak(name) > 0.05)
    }
}

func AgentIconsStayOnePerConversation() {
    let rows = AgentIconList.rows([
        AgentIconCandidate(id: "c-run", source: "cursor", title: "Run", status: "running", read: false, updated: 1),
        AgentIconCandidate(id: "c-done", source: "cursor", title: "Done", status: "done", read: false, updated: 5),
        AgentIconCandidate(id: "c-old", source: "cursor", title: "Old", status: "done", read: true, updated: 9),
        AgentIconCandidate(id: "w-old", source: "cursor", title: "Old wait", status: "wait", read: true, updated: 1),
        AgentIconCandidate(id: "w-new", source: "cursor", title: "New wait", status: "wait", read: false, updated: 4),
        AgentIconCandidate(id: "g-wait", source: "chatgpt", title: "Wait", status: "wait", read: false, updated: 2),
        AgentIconCandidate(id: "g-run", source: "gpt", title: "Run", status: "running", read: false, updated: 8),
        AgentIconCandidate(id: "x-done", source: "codex", title: "Done", status: "done", read: true, updated: 3),
        AgentIconCandidate(id: "fail", source: "grok", title: "Fail", status: "fail", read: false, updated: 4),
        AgentIconCandidate(id: "cat", source: "mochi", title: "Cat", status: "running", read: false, updated: 9)
    ])
    check(rows.map(\.id) == ["c-done", "w-new", "w-old", "c-run", "fail", "g-run", "g-wait"])
    check(rows.map(\.source) == ["cursor", "cursor", "cursor", "cursor", "grok-bot", "gpt", "gpt"])
    check(rows[0].showsDot)
    check(rows[0].memberIDs == ["c-done"])
    check(rows.contains { $0.id == "c-old" } == false)
    check(rows.first { $0.id == "fail" }?.showsDot == true)
    check(rows.first { $0.id == "g-run" }?.status == "running")
    check(rows.first { $0.id == "g-wait" }?.showsDot == true)
    let running = AgentIconList.rows([
        AgentIconCandidate(id: "r", source: "cursor", title: "R", status: "running", read: true, updated: 1)
    ])
    check(running.count == 1)
    check(running[0].showsDot == false)
    let waiting = AgentIconList.rows([
        AgentIconCandidate(id: "w", source: "codex", title: "W", status: "wait", read: true, updated: 2)
    ])
    check(waiting.count == 1)
    check(waiting[0].showsDot == false)
    let done = AgentIconList.rows([
        AgentIconCandidate(id: "d", source: "gpt", title: "D", status: "done", read: false, updated: 3)
    ])
    check(done.count == 1)
    check(done[0].showsDot)
    let opened = AgentIconList.rows([
        AgentIconCandidate(id: "d2", source: "gpt", title: "D", status: "fail", read: true, updated: 3)
    ])
    check(opened.isEmpty)
    let held = AgentIconList.rows([
        AgentIconCandidate(id: "d2", source: "gpt", title: "D", status: "fail", read: true, updated: 3)
    ], holding: ["d2"])
    check(held.count == 1)
    check(held[0].id == "d2")
    check(held[0].showsDot == false)
    let freshPermission = AgentIconList.rows([
        AgentIconCandidate(id: "chat", source: "cursor", title: "Cursor", status: "running", read: false, updated: 5),
        AgentIconCandidate(id: "ask", source: "cursor", title: "Shell", status: "wait", read: false, updated: 9)
    ])
    check(freshPermission.map(\.id) == ["ask", "chat"])
    check(freshPermission[0].source == "cursor")
    check(freshPermission[1].source == "cursor")
    check(AgentIconList.permissionStaysOpen(taskStatus: "wait", isFace: true))
    let olderWait = AgentIconList.rows([
        AgentIconCandidate(id: "old-wait", source: "cursor", title: "合并到一个文件", status: "wait", read: false, updated: 1),
        AgentIconCandidate(id: "new-chat", source: "cursor", title: "Cursor", status: "running", read: false, updated: 20)
    ])
    check(olderWait.map(\.id) == ["new-chat", "old-wait"])
    check(olderWait[0].status == "running")
    check(olderWait[1].status == "wait")
    check(olderWait[0].memberIDs == ["new-chat"])
    check(AgentIconList.permissionStaysOpen(taskStatus: "wait", isFace: false) == false)
    check(AgentIconList.permissionStaysOpen(taskStatus: "running", isFace: true) == false)
    // Live work always stays, even if somehow marked read.
    check(AgentIconList.shouldShow(
        AgentIconCandidate(id: "live", source: "cursor", title: "R", status: "running", read: true, updated: 1)
    ))
    check(AgentIconList.shouldShow(
        AgentIconCandidate(id: "ask", source: "cursor", title: "W", status: "wait", read: true, updated: 2)
    ))
    // Read finished with no choices leaves; pending Allow/Deny keeps it.
    check(AgentIconList.shouldShow(
        AgentIconCandidate(id: "gone", source: "cursor", title: "D", status: "done", read: true, updated: 3)
    ) == false)
    check(AgentIconList.shouldShow(
        AgentIconCandidate(
            id: "choice",
            source: "cursor",
            title: "D",
            status: "done",
            read: true,
            updated: 4,
            hasPendingOptions: true
        )
    ))
    let keptChoice = AgentIconList.rows([
        AgentIconCandidate(
            id: "choice",
            source: "cursor",
            title: "D",
            status: "done",
            read: true,
            updated: 4,
            hasPendingOptions: true
        )
    ])
    check(keptChoice.map(\.id) == ["choice"])
}

func DockPrefDefaultsToRight() {
    let missing = IslandPrefs.parse([:])
    check(missing.dock == "right")
    let left = IslandPrefs.parse(["dock": "left"])
    check(left.dock == "left")
    check(left.dictionary["dock"] as? String == "left")
    let right = IslandPrefs.parse(["dock": "right"], base: IslandPrefs(dock: "left"))
    check(right.dock == "right")
    let junk = IslandPrefs.parse(["dock": "middle"], base: IslandPrefs(dock: "left"))
    check(junk.dock == "left")
}

func PickerFlagsPersistAcrossPrefs() throws {
    let fresh = IslandPrefs()
    check(fresh.pickerSources == IslandPrefs.pickerOrder)
    let hidden = IslandPrefs.parse([
        "picker_cursor": false,
        "picker_gpt": false
    ])
    check(hidden.pickerCursor == false)
    check(hidden.pickerCodex)
    check(hidden.pickerGrok)
    check(hidden.pickerGPT == false)
    check(hidden.pickerSources == ["codex", "claude", "gemini", "grok-bot"])
    check(hidden.dictionary["picker_cursor"] as? Bool == false)
    check(hidden.dictionary["picker_codex"] as? Bool == true)
    check(hidden.dictionary["picker_grok"] as? Bool == true)
    check(hidden.dictionary["picker_gpt"] as? Bool == false)
    let again = IslandPrefs.parse(hidden.dictionary, base: IslandPrefs())
    check(again.pickerSources == ["codex", "claude", "gemini", "grok-bot"])
    let data = try JSONEncoder().encode(hidden)
    let text = String(data: data, encoding: .utf8) ?? ""
    check(text.contains("picker_cursor"))
    check(text.contains("picker_gpt"))
    let decoded = try JSONDecoder().decode(IslandPrefs.self, from: data)
    check(decoded.pickerSources == ["codex", "claude", "gemini", "grok-bot"])
    let legacy = try JSONDecoder().decode(IslandPrefs.self, from: Data("{\"sound\":\"pop\"}".utf8))
    check(legacy.sound == "pop")
    check(legacy.pickerSources == IslandPrefs.pickerOrder)
    let kept = IslandPrefs.parse(["pet_scale": 1.5], base: hidden)
    check(kept.petScale == 1.5)
    check(kept.pickerSources == ["codex", "claude", "gemini", "grok-bot"])
    let numbers = IslandPrefs.parse([
        "picker_cursor": 0,
        "picker_codex": 1,
        "picker_grok": 0,
        "picker_gpt": 1
    ])
    check(numbers.pickerSources == ["codex", "claude", "gemini", "gpt"])
}

func EmptyFileHasNoSeeds() throws {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-slots-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let disk = DialogueSlotDisk(directory: directory)
    let store = disk.load(now: 10, activeTaskIDs: ["live-1"])
    check(store.slots.isEmpty)
    check(store.selected.isEmpty)
    check(!FileManager.default.fileExists(atPath: disk.fileURL.path))
    check(SlotFace.pack(for: "mochi") == "kitten")
    check(SlotFace.resourceName(source: "小猫", status: "") == "kitten-wake")
    check(SlotFace.resourceName(source: "mochi", status: "wait") == "kitten-wait")
    check(SlotFace.displayName(for: "mochi") == "小猫")
    check(SlotFace.label(title: "New chat", source: "mochi") == "小猫")
    let again = disk.load(now: 99)
    check(again.slots.isEmpty)
}

func MergeKeepsSkinAndLeavesSlotsWhenTasksLeave() {
    let kept = DialogueSlotStore(
        selected: "keep",
        slots: [DialogueSlot(id: "keep", source: "cursor", title: "Stay", skin: "cursor", created: 1)]
    )
    let stillThere = kept.merged(with: [])
    check(stillThere.slots.contains { $0.id == "keep" })

    let store = DialogueSlotStore(
        selected: "abc",
        slots: [DialogueSlot(id: "abc", source: "cursor", title: "Keep", skin: "blob-3", created: 1)]
    )
    let task = CursorTask(id: "abc", title: "From bus", status: "running", source: "codex", updated: 5)
    let merged = store.merged(with: [task])
    check(merged.slots.count == 1)
    check(merged.slots[0].skin == "codex")
    check(merged.slots[0].source == "codex")
    check(merged.slots[0].title == "From bus")
    check(merged.merged(with: [task]).slots[0].skin == "codex")

    let added = DialogueSlotStore().merged(with: [task])
    check(added.slots[0].skin == "codex")
    check(added.slots[0].skin == DialogueSlotStore.face(for: "codex"))
}

func CreateSlotAppendsAFaceWithoutATaskFile() {
    let store = DialogueSlotStore(
        selected: "one",
        slots: [DialogueSlot(id: "one", source: "cursor", title: "One", skin: "cursor", created: 1)]
    )
    var createRNG = SplitMix64(seed: 11)
    let (next, slot) = store.created(source: "Grok Bot", now: 20, rng: &createRNG)
    check(next.slots.count == store.slots.count + 1)
    check(next.selected == slot.id)
    check(slot.source == "grok-bot")
    check(slot.skin == "grok-bot")
    check(slot.title == "New chat")
    check(DialogueSlotStore.skins.contains(slot.skin))
    check(!slot.skin.contains("happy"))
    var catRNG = SplitMix64(seed: 12)
    let cat = store.created(source: "小猫", now: 21, rng: &catRNG).1
    check(cat.source == "mochi")
    check(cat.skin == "mochi")
}

func KittenV1FaceFilesFollowSource() {
    check(SlotFace.pack(for: "cursor") == "cursor")
    check(SlotFace.pack(for: "grok-bot") == "grok")
    check(SlotFace.pack(for: "小猫") == "kitten")
    check(SlotFace.resourceName(source: "mochi", status: "running") == "kitten-work")
    check(SlotFace.resourceName(source: "mochi", status: "done") == "kitten-done")
    check(SlotFace.label(title: "Fix tabs", source: "cursor") == "Fix tabs")
    check(SlotFace.displayName(for: "grok-bot") == "Grok Bot")
}

func OldSeedMigrationKeepsActiveTasks() throws {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-migrate-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let raw = """
    {"selected":"seed-cursor","slots":[
      {"id":"seed-cursor","source":"cursor","title":"New chat","skin":"cursor","created":1},
      {"id":"seed-mochi","source":"mochi","title":"小猫","skin":"mochi","created":1},
      {"id":"seed-codex","source":"codex","title":"New chat","skin":"codex","created":1},
      {"id":"live-1","source":"cursor","title":"Keep me","skin":"cursor","created":2},
      {"id":"cat-1","source":"mochi","title":"小猫","skin":"mochi","created":3}
    ]}
    """
    let file = directory.appendingPathComponent("dialogue-slots.json")
    try raw.data(using: .utf8)?.write(to: file)
    let disk = DialogueSlotDisk(directory: directory)
    let store = disk.load(activeTaskIDs: ["live-1", "cat-1", "seed-codex"])
    let ids = Set(store.slots.map(\.id))
    check(ids == Set(["live-1", "cat-1", "seed-codex"]))
    check(!ids.contains("seed-cursor"))
    check(!ids.contains("seed-mochi"))
    check(store.slots.first { $0.id == "live-1" }?.read == false)
    check(store.slots.first { $0.id == "cat-1" }?.source == "mochi")
    let idle = disk.load(activeTaskIDs: [])
    check(idle.slots.map(\.id) == ["live-1"])
}

func RemoveWhenReadAndDone() {
    let store = DialogueSlotStore(
        selected: "a",
        slots: [
            DialogueSlot(id: "a", source: "cursor", title: "Done", skin: "cursor", created: 1, read: true, status: "done"),
            DialogueSlot(id: "b", source: "cursor", title: "Wait", skin: "cursor", created: 1, read: true, status: "wait"),
            DialogueSlot(id: "c", source: "cursor", title: "Unread", skin: "cursor", created: 1, read: false, status: "done"),
            DialogueSlot(id: "d", source: "gpt", title: "Run", skin: "gpt", created: 1, read: true, status: "running"),
            DialogueSlot(id: "e", source: "codex", title: "Fail", skin: "codex", created: 1, read: true, status: "fail")
        ]
    )
    let liveTasks = [
        CursorTask(id: "a", title: "Done", status: "done"),
        CursorTask(id: "b", title: "Wait", status: "wait"),
        CursorTask(id: "c", title: "Unread", status: "done"),
        CursorTask(id: "d", title: "Run", status: "running", source: "gpt"),
        CursorTask(id: "e", title: "Fail", status: "fail", source: "codex")
    ]
    let live = store.removingReadDone(tasks: liveTasks)
    check(Set(live.slots.map(\.id)) == Set(["a", "b", "c", "d", "e"]))
    let kept = live.merged(with: [CursorTask(id: "a", title: "Done", status: "done", updated: 5)])
    check(kept.slots.first { $0.id == "a" }?.read == true)
    check(kept.slots.first { $0.id == "a" }?.status == "done")
    let aged = store.removingReadDone(tasks: [
        CursorTask(id: "b", title: "Wait", status: "wait"),
        CursorTask(id: "c", title: "Unread", status: "done"),
        CursorTask(id: "d", title: "Run", status: "running", source: "gpt")
    ])
    check(Set(aged.slots.map(\.id)) == Set(["b", "c", "d"]))
    check(aged.markingRead("c").slots.first { $0.id == "c" }?.read == true)
}

func SelectingKeepsOnlyKnownIds() {
    let store = DialogueSlotStore(
        selected: "a",
        slots: [
            DialogueSlot(id: "a", source: "cursor", title: "A", skin: "cursor", created: 1),
            DialogueSlot(id: "b", source: "codex", title: "B", skin: "codex", created: 1)
        ]
    )
    let picked = store.slots[1].id
    let next = store.selecting(picked)
    check(next.selected == picked)
    check(next.slots == store.slots)
    let junk = store.selecting("missing")
    check(junk.selected == store.selected)
}

func NewWaitSelectsOnlyAfterTheFirstSnapshot() {
    let store = DialogueSlotStore(
        selected: "seeded",
        slots: [DialogueSlot(id: "seeded", source: "cursor", title: "Old", skin: "cursor", created: 1)]
    )
    var known: Set<String> = []
    var primed = false
    let first = store.selectingNewWaits(
        in: [CursorTask(id: "w", title: "Waiting", status: "wait", updated: 9)],
        known: &known,
        primed: &primed
    )
    check(primed)
    check(first.selected == "seeded")
    check(first.slots.contains { $0.id == "w" })
    let second = first.selectingNewWaits(
        in: [
            CursorTask(id: "w", title: "Waiting", status: "wait", updated: 9),
            CursorTask(id: "w2", title: "Newer", status: "wait", source: "codex", updated: 12)
        ],
        known: &known,
        primed: &primed
    )
    check(second.selected == "w2")
    check(second.slots.contains { $0.id == "w" })
    check(second.slots.first { $0.id == "w2" }?.source == "codex")
}

func iconSources(_ outcome: HookOutcome?) -> [String] {
    guard let outcome else { return [] }
    return AgentIconList.rows([
        AgentIconCandidate(
            id: outcome.id,
            source: outcome.source,
            title: outcome.title,
            status: outcome.status,
            read: false,
            updated: 20
        )
    ]).map(\.source)
}

func SubmittedChatKeepsTheAgentAndDropsThePrompt() throws {
    let secret = "合并到一个文件"
    let longPrompt = String(repeating: "长提示词内容不要整段留下", count: 4)
    let outcome = HookEvents.interpret(sourceFlag: nil, payload: [
        "hook_event_name": "beforeSubmitPrompt",
        "conversation_id": "11111111-2222-3333-4444-555555555555",
        "prompt": secret,
        "command": "rm -rf /tmp/nope",
        "message": "secret message",
        "tool_input": ["command": "ls"]
    ])
    check(outcome?.source == "cursor")
    check(outcome?.title == "Cursor")
    check(outcome?.status == "running")
    check(outcome?.waitsForPermission == false)
    check(outcome?.summary == secret)
    let longOutcome = HookEvents.interpret(sourceFlag: nil, payload: [
        "hook_event_name": "beforeSubmitPrompt",
        "conversation_id": "22222222-3333-4444-5555-666666666666",
        "prompt": longPrompt
    ])
    check(longOutcome?.summary == PermissionCopy.abbreviate(longPrompt))
    check((longOutcome?.summary.count ?? 0) <= 40)
    check(longOutcome?.summary.contains(longPrompt) == false)
    let url = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-hook-\(UUID().uuidString).json")
    HookEvents.commit(outcome!, tasksURL: url, now: 20)
    let text = String(data: try Data(contentsOf: url), encoding: .utf8) ?? ""
    check(text.contains("rm -rf") == false)
    check(text.contains("secret message") == false)
    check(text.contains("\"prompt\"") == false)
    check(text.contains("\"command\"") == false)
    check(text.contains("\"source\" : \"cursor\"") || text.contains("\"source\":\"cursor\""))
    check(text.contains("transcript") == false)
    let saved = try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any]
    let savedTasks = saved?["tasks"] as? [String: Any]
    let savedChat = savedTasks?["11111111-2222-3333-4444-555555555555"] as? [String: Any]
    check(savedChat?["detail"] as? String == "Chat")
    check(savedChat?["source"] as? String == "cursor")
    check(savedChat?["summary"] as? String == secret)
    check(savedChat?["prompt"] == nil)
    check(iconSources(outcome) == ["cursor"])
    let codex = HookEvents.interpret(sourceFlag: "codex", payload: [
        "hook_event_name": "UserPromptSubmit",
        "session_id": "codex-session",
        "prompt": secret
    ])
    check(codex?.source == "codex")
    check(codex?.title == "Codex")
    check(codex?.status == "running")
    check(codex?.detail == "Chat")
    check(iconSources(codex) == ["codex"])
    let claudeChat = HookEvents.interpret(sourceFlag: "claude", payload: [
        "hook_event_name": "UserPromptSubmit",
        "session_id": "claude-chat",
        "prompt": secret,
        "command": "echo hi"
    ])
    check(claudeChat?.source == "claude")
    check(claudeChat?.status == "running")
    check(claudeChat?.detail == "Chat")
    check(iconSources(claudeChat) == ["claude"])
    let permission = HookEvents.interpret(sourceFlag: nil, payload: [
        "hook_event_name": "beforeShellExecution",
        "conversation_id": "11111111-2222-3333-4444-555555555555",
        "command": "echo hi"
    ])
    check(permission?.status == "wait")
    check(permission?.waitsForPermission == true)
    check(permission?.permissionStyle == "cursor")
    check(permission?.options.map(\.self["id"]) == ["allow", "deny"])
    check(permission?.detail == "Shell")
    check(permission?.title == "Shell")
    check(permission?.summary == "")
    check(permission?.title.contains(secret) == false)
    // Chat then shell on the same conversation must keep the abbreviated submit blurb.
    let mergeURL = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-hook-merge-\(UUID().uuidString).json")
    HookEvents.commit(outcome!, tasksURL: mergeURL, now: 20)
    HookEvents.commit(permission!, tasksURL: mergeURL, now: 21)
    let mergedRoot = try JSONSerialization.jsonObject(with: Data(contentsOf: mergeURL)) as? [String: Any]
    let mergedTasks = mergedRoot?["tasks"] as? [String: Any]
    let merged = mergedTasks?["11111111-2222-3333-4444-555555555555"] as? [String: Any]
    check(merged?["status"] as? String == "wait")
    check(merged?["summary"] as? String == secret)
    check(merged?["detail"] as? String == "Shell")
    check(merged?["title"] as? String == "Shell")
    let mergedShown = TaskReader.load(tasksURL: mergeURL, now: 22, includePrompts: true)
    let mergedTask = mergedShown.first { $0.id == "11111111-2222-3333-4444-555555555555" }
    check(mergedTask?.status == "wait")
    check(mergedTask?.kind == "permission")
    check(mergedTask?.summary == secret)
    check(PermissionCopy.cardBlurb(
        summary: mergedTask?.summary ?? "",
        detail: "Shell",
        kind: "permission",
        status: "wait",
        optionIDs: ["allow", "deny"]
    ) == secret)
    check(PermissionCopy.cardBlurb(
        summary: "",
        detail: "Shell",
        kind: "permission",
        status: "wait",
        optionIDs: ["allow", "deny"]
    ) == "Shell needs Allow")
    let shellURL = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-hook-\(UUID().uuidString).json")
    HookEvents.commit(permission!, tasksURL: shellURL, now: 21)
    let shellText = String(data: try Data(contentsOf: shellURL), encoding: .utf8) ?? ""
    check(shellText.contains("echo hi") == false)
    let shellRoot = try JSONSerialization.jsonObject(with: Data(contentsOf: shellURL)) as? [String: Any]
    let shellTasks = shellRoot?["tasks"] as? [String: Any]
    let shellRecord = shellTasks?["11111111-2222-3333-4444-555555555555"] as? [String: Any]
    check(shellRecord?["detail"] as? String == "Shell")
    check(shellRecord?["title"] as? String == "Shell")
    check(shellRecord?["command"] == nil)
    check(shellRecord?["prompt"] == nil)
    let claude = HookEvents.interpret(sourceFlag: "claude", payload: [
        "hook_event_name": "PermissionRequest",
        "session_id": "claude-1",
        "command": "echo hi"
    ])
    check(claude?.source == "claude")
    check(claude?.status == "running")
    check(claude?.waitsForPermission == false)
    check(claude?.detail == "Permission")
    let cursorAgain = HookEvents.interpret(sourceFlag: "claude", payload: [
        "hook_event_name": "UserPromptSubmit",
        "cursor_version": "1.0",
        "session_id": "from-cursor",
        "prompt": secret
    ])
    check(cursorAgain?.source == "cursor")
    check(cursorAgain?.detail == "Chat")
    check(HookInstaller.events.contains("beforeSubmitPrompt"))
    check(HookInstaller.commandEvents.contains("UserPromptSubmit"))
    check(HookInstaller.events.contains { $0.lowercased().contains("gpt") } == false)
    check(HookInstaller.commandEvents.contains { $0.lowercased().contains("gpt") } == false)
    let stdout = HookEvents.permissionStdout(style: "codex", action: "deny")
    let decision = (stdout["hookSpecificOutput"] as? [String: Any])?["decision"] as? [String: Any]
    check(decision?["behavior"] as? String == "deny")
    check(HookEvents.permissionStdout(style: "cursor", action: "allow")["permission"] as? String == "allow")
}

func AnsweredPermissionIsNotShownAgain() throws {
    let secret = "合并到一个文件"
    let debug = "PM手点：请选 Allow 或 Deny"
    let command = "SECRET-COMMAND"
    let url = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-permission-\(UUID().uuidString).json")
    let legacy: [String: Any] = [
        "tasks": [
            "old": [
                "id": "old",
                "title": secret,
                "prompt": secret,
                "command": command,
                "detail": debug,
                "status": "wait",
                "kind": "permission",
                "source": "cursor",
                "request_id": "req-old",
                "updated": 10,
                "options": [
                    ["id": "allow", "label": "Allow"],
                    ["id": "deny", "label": "Deny"]
                ]
            ]
        ]
    ]
    try JSONSerialization.data(withJSONObject: legacy).write(to: url)
    let shown = TaskReader.load(tasksURL: url, now: 20)
    check(shown.count == 1)
    check(shown[0].title == "Permission")
    check(shown[0].detail == "")
    check(shown[0].prompt == "")
    check(shown[0].status == "wait")
    check(shown[0].kind == "permission")
    check(shown[0].options.map { $0["id"] ?? "" } == ["allow", "deny"])
    check(PermissionCopy.visibleDetail(kind: "permission", status: "wait", detail: debug, optionIDs: ["allow", "deny"]) == "")
    check(PermissionCopy.visibleDetail(kind: "permission", status: "wait", detail: "Shell", optionIDs: ["allow", "deny"]) == "")
    check(PermissionCopy.visibleTitle(kind: "permission", status: "wait", title: secret, detail: "Shell", agent: "Cursor", optionIDs: ["allow", "deny"]) == "Shell")
    check(PermissionCopy.visibleTitle(kind: "permission", status: "wait", title: secret, detail: debug, agent: "Cursor", optionIDs: ["allow", "deny"]) == "Permission")
    check(PermissionCopy.containsDebug(debug))
    HookEvents.scrubStored(tasksURL: url)
    let scrubbed = String(data: try Data(contentsOf: url), encoding: .utf8) ?? ""
    check(scrubbed.contains(secret) == false)
    check(scrubbed.contains(debug) == false)
    check(scrubbed.contains(command) == false)
    check(scrubbed.contains("\"prompt\"") == false)
    let chat = HookEvents.interpret(sourceFlag: nil, payload: [
        "hook_event_name": "beforeSubmitPrompt",
        "conversation_id": "new-chat",
        "prompt": "1"
    ])
    check(chat?.status == "running")
    check(chat?.summary == "1")
    check(chat?.title == "Cursor")
    HookEvents.commit(chat!, tasksURL: url, now: 25)
    let cleaned = String(data: try Data(contentsOf: url), encoding: .utf8) ?? ""
    check(cleaned.contains(secret) == false)
    check(cleaned.contains(debug) == false)
    check(cleaned.contains("PM手点") == false)
    check(cleaned.contains("请选 Allow 或 Deny") == false)
    check(cleaned.contains(command) == false)
    check(cleaned.contains("\"prompt\"") == false)
    let root = try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any]
    let tasks = root?["tasks"] as? [String: Any]
    let old = tasks?["old"] as? [String: Any]
    let started = tasks?["new-chat"] as? [String: Any]
    check(old?["status"] as? String == "wait")
    check(old?["title"] as? String == "Permission")
    check(old?["prompt"] == nil)
    check(old?["detail"] == nil)
    check(started?["status"] as? String == "running")
    check(started?["title"] as? String == "Cursor")
    check(started?["detail"] as? String == "Chat")
    check(started?["prompt"] == nil)
    let replyURL = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-reply-\(UUID().uuidString).json")
    try ReplyWriter.answerPermission(
        id: "old",
        requestID: "req-old",
        action: "Allow",
        kind: "permission",
        replyURL: replyURL,
        tasksURL: url,
        now: 30
    )
    let reply = try JSONSerialization.jsonObject(with: Data(contentsOf: replyURL)) as? [String: Any]
    check(reply?["action"] as? String == "allow")
    check(reply?["kind"] as? String == "permission")
    check(reply?["request_id"] as? String == "req-old")
    let settled = TaskReader.load(tasksURL: url, now: 40)
    let settledOld = settled.first { $0.id == "old" }
    check(settledOld?.status == "running")
    check(settledOld?.options.isEmpty == true)
    check(settledOld?.kind == "")
    check(settledOld?.prompt == "")
    let again = HookEvents.interpret(sourceFlag: nil, payload: [
        "hook_event_name": "beforeShellExecution",
        "conversation_id": "old",
        "generation_id": "req-old",
        "command": command
    ])
    check(again?.requestID == "req-old")
    HookEvents.commit(again!, tasksURL: url, now: 41)
    let hidden = TaskReader.load(tasksURL: url, now: 50)
    let hiddenOld = hidden.first { $0.id == "old" }
    check(hiddenOld?.status == "running")
    check(hiddenOld?.options.isEmpty == true)
    check(hiddenOld?.kind == "")
    let stored = String(data: try Data(contentsOf: url), encoding: .utf8) ?? ""
    check(stored.contains(secret) == false)
    check(stored.contains(command) == false)
    check(stored.contains("PM手点") == false)
    check(PermissionCopy.answersPermission(kind: "mode", action: "deny") == false)
    check(ReplyWriter.closesBubble(kind: "permission", optionID: "deny"))
}

func CodexAndClaudeHooksInstallMarkedCommands() throws {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let binary = directory.appendingPathComponent("MochiHookCLI")
    try Data("#!/bin/sh\nexit 0\n".utf8).write(to: binary)
    try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: binary.path)
    let config = directory.appendingPathComponent("config.toml")
    try "[features]\njs_repl = false\n".write(to: config, atomically: true, encoding: .utf8)
    let hooks = directory.appendingPathComponent("hooks.json")
    let existing = #"{"hooks":{"UserPromptSubmit":[{"hooks":[{"type":"command","command":"/bin/echo keep"}]}]}}"#
    try existing.write(to: hooks, atomically: true, encoding: .utf8)
    let blocked = HookInstaller.installCodex(configURL: config, hooksURL: hooks, binaryURL: binary, privacyAccepted: false)
    check(blocked.needsPrivacy)
    check(blocked.connected == false)
    let installed = HookInstaller.installCodex(configURL: config, hooksURL: hooks, binaryURL: binary, privacyAccepted: true)
    check(installed.connected)
    let configText = try String(contentsOf: config, encoding: .utf8)
    check(configText.contains("js_repl = false"))
    check(configText.contains("hooks = true"))
    let data = try Data(contentsOf: hooks)
    let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
    let groups = ((json?["hooks"] as? [String: Any])?["UserPromptSubmit"] as? [[String: Any]]) ?? []
    let commands = groups.flatMap { $0["hooks"] as? [[String: Any]] ?? [] }
    check(commands.contains { ($0["command"] as? String)?.contains("/bin/echo keep") == true })
    check(commands.contains { ($0[AppConfig.hookMarker] as? Int) == AppConfig.hookVersion && ($0["command"] as? String)?.contains("--source codex") == true })
    let settings = directory.appendingPathComponent("settings.json")
    try #"{"theme":"dark","hooks":{}}"#.write(to: settings, atomically: true, encoding: .utf8)
    let claude = HookInstaller.installClaude(settingsURL: settings, binaryURL: binary, privacyAccepted: true)
    check(claude.connected)
    let saved = try JSONSerialization.jsonObject(with: Data(contentsOf: settings)) as? [String: Any]
    check(saved?["theme"] as? String == "dark")
    let removed = HookInstaller.removeNested(fileURL: hooks, binaryURL: binary)
    check(removed.connected == false)
    let ambiguous = "features.hooks = false\n\n[features]\njs_repl = false\n"
    check(CodexFeatureFlag.enablingHooks(in: ambiguous) == nil)
    check(CodexFeatureFlag.isEnabled(in: "features.hooks = true\n") == true)
    check(CodexFeatureFlag.isEnabled(in: ambiguous) == false)
}

func HookRetargetPointsAtTheOpenApp() throws {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-retarget-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let current = directory.appendingPathComponent("MochiHookCLI")
    try Data("#!/bin/sh\nexit 0\n".utf8).write(to: current)
    try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: current.path)
    let hooks = directory.appendingPathComponent("hooks.json")
    let stale = "/Users/albertsmacbookpro/Projects/PythonTemplate/island/MochiIsland.app/Contents/MacOS/MochiHookCLI"
    let raw: [String: Any] = [
        "version": 1,
        "hooks": [
            "beforeSubmitPrompt": [[
                "command": stale,
                AppConfig.hookMarker: AppConfig.hookVersion
            ]],
            "stop": [["command": "/bin/echo keep"]]
        ]
    ]
    try JSONSerialization.data(withJSONObject: raw).write(to: hooks)
    check(HookInstaller.retargetCursor(hooksURL: hooks, binaryURL: current))
    let saved = try JSONSerialization.jsonObject(with: Data(contentsOf: hooks)) as? [String: Any]
    let entries = ((saved?["hooks"] as? [String: Any])?["beforeSubmitPrompt"] as? [[String: Any]]) ?? []
    check(entries.first?["command"] as? String == current.path)
    let stop = ((saved?["hooks"] as? [String: Any])?["stop"] as? [[String: Any]]) ?? []
    check(stop.first?["command"] as? String == "/bin/echo keep")
    let outcome = HookEvents.interpret(sourceFlag: nil, payload: [
        "hook_event_name": "beforeSubmitPrompt",
        "conversation_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        "prompt": "do not keep this"
    ])
    check(outcome?.status == "running")
    check(outcome?.source == "cursor")
    var tasks: [String: Any] = [:]
    HookEvents.merge(outcome!, into: &tasks, now: 5)
    let item = tasks[outcome!.id] as? [String: Any]
    check(item?["prompt"] == nil)
    check(item?["status"] as? String == "running")
    let rows = AgentIconList.rows([
        AgentIconCandidate(id: outcome!.id, source: "cursor", title: "Cursor", status: "running", read: false, updated: 5)
    ])
    check(rows.count == 1)
    check(rows[0].source == "cursor")
}

func PassiveHooksReportTheShortlist() throws {
    func kept(_ outcome: HookOutcome?) -> [String: Any] {
        guard let outcome else { return ["missing": true] }
        var tasks: [String: Any] = [:]
        HookEvents.merge(outcome, into: &tasks, now: 8)
        return tasks[outcome.id] as? [String: Any] ?? [:]
    }
    func clean(_ record: [String: Any]) -> Bool {
        let banned = [
            "prompt", "command", "tool_input", "toolInput", "message", "initialPrompt", "initial_prompt",
            "error", "transcript_path", "last_assistant_message", "notification", "question"
        ]
        if banned.contains(where: { record[$0] != nil }) { return false }
        for (key, value) in record {
            // Summary may keep a short local blurb of the submit text.
            if key == "summary" { continue }
            if let text = value as? String, text.contains("SECRET") { return false }
        }
        return record["missing"] == nil
    }

    let home = URL(fileURLWithPath: "/Users/example", isDirectory: true)
    let copilotHome = PassiveHooks.copilotDirectory(home: home, environment: [:])
    check(copilotHome.lastPathComponent == "hooks")
    check(copilotHome.deletingLastPathComponent().lastPathComponent == ".copilot")
    check(copilotHome.path.contains(".github") == false)
    let custom = PassiveHooks.copilotDirectory(home: home, environment: ["COPILOT_HOME": "/var/copilot-home"])
    check(custom.path == "/var/copilot-home/hooks")
    check(custom.path.contains(".github") == false)
    let blank = PassiveHooks.copilotDirectory(home: home, environment: ["COPILOT_HOME": "  "])
    check(blank.deletingLastPathComponent().lastPathComponent == ".copilot")
    check(PassiveHooks.grokDirectory(home: home).path == home.appendingPathComponent(".grok/hooks").path)
    check(PassiveHooks.openCodePluginURL(home: home).path == home.appendingPathComponent(".config/opencode/plugins/mochi-island.js").path)
    check(IslandPrefs.pickerOrder == ["cursor", "codex", "claude", "gemini", "grok-bot", "gpt"])
    check(IslandPrefs().pickerClaude)
    check(IslandPrefs.pickerOrder.contains("copilot") == false)
    check(IslandPrefs.pickerOrder.contains("opencode") == false)
    check((PassiveHooks.copilotEvents + PassiveHooks.grokEvents).contains { $0.lowercased().contains("gpt") } == false)
    check(PassiveHooks.grokEvents.contains("SubagentStop") == false)

    let copilotRunning = ["sessionStart", "userPromptSubmitted", "preToolUse", "postToolUse", "subagentStart", "postToolUseFailure"]
    for name in copilotRunning {
        let item = HookEvents.interpret(sourceFlag: "copilot", payload: [
            "hook_event_name": name,
            "sessionId": "copilot-1",
            "prompt": "SECRET",
            "toolArgs": "SECRET",
            "initialPrompt": "SECRET"
        ])
        check(item?.status == "running")
        check(item?.source == "copilot")
        check(item?.waitsForPermission == false)
        check(clean(kept(item)))
    }
    let permission = HookEvents.interpret(sourceFlag: "copilot", payload: [
        "sessionId": "copilot-1",
        "prompt": "SECRET"
    ], eventFlag: "permissionRequest")
    check(permission?.status == "wait")
    check(permission?.waitsForPermission == false)
    check(permission?.options.isEmpty == true)
    check(permission?.permissionStyle.isEmpty == true)
    check(clean(kept(permission)))
    for kind in ["permission_prompt", "elicitation_dialog"] {
        let note = HookEvents.interpret(sourceFlag: "copilot", payload: [
            "hook_event_name": "notification",
            "notification_type": kind,
            "sessionId": "copilot-1",
            "message": "SECRET"
        ])
        check(note?.status == "wait")
        check(note?.waitsForPermission == false)
        check(clean(kept(note)))
    }
    check(HookEvents.interpret(sourceFlag: "copilot", payload: [
        "hook_event_name": "notification",
        "notification_type": "shell_completion",
        "sessionId": "copilot-1"
    ]) == nil)
    let toolError = HookEvents.interpret(sourceFlag: "copilot", payload: [
        "hook_event_name": "errorOccurred",
        "errorContext": "tool_execution",
        "sessionId": "copilot-1",
        "error": ["message": "SECRET"]
    ])
    check(toolError?.status == "running")
    check(clean(kept(toolError)))
    let modelError = HookEvents.interpret(sourceFlag: "copilot", payload: [
        "hook_event_name": "ErrorOccurred",
        "error_context": "model_call",
        "sessionId": "copilot-1",
        "error": ["message": "SECRET"]
    ])
    check(modelError?.status == "fail")
    check(clean(kept(modelError)))
    check(HookEvents.interpret(sourceFlag: "copilot", payload: [
        "hook_event_name": "errorOccurred",
        "errorContext": "system",
        "sessionId": "copilot-1"
    ]) == nil)
    check(HookEvents.interpret(sourceFlag: "copilot", payload: [
        "hook_event_name": "agentStop",
        "sessionId": "copilot-1"
    ])?.status == "done")
    var copilotTasks: [String: Any] = ["copilot-1": ["id": "copilot-1", "status": "running", "source": "copilot"]]
    let copilotEnd = HookEvents.interpret(sourceFlag: "copilot", payload: [
        "hook_event_name": "sessionEnd",
        "sessionId": "copilot-1",
        "reason": "error",
        "error": "SECRET"
    ])
    check(copilotEnd?.status == "closed")
    HookEvents.merge(copilotEnd!, into: &copilotTasks, now: 9)
    check(copilotTasks["copilot-1"] == nil)

    let busy = HookEvents.interpret(sourceFlag: "opencode", payload: [
        "hook_event_name": "session.status",
        "sessionID": "oc-1",
        "status": ["type": "retry", "message": "SECRET retry"]
    ])
    check(busy?.status == "running")
    check(busy?.id == "oc-1")
    check(clean(kept(busy)))
    check(HookEvents.interpret(sourceFlag: "opencode", payload: [
        "hook_event_name": "session.status",
        "session_id": "oc-1",
        "status_type": "busy"
    ])?.status == "running")
    check(HookEvents.interpret(sourceFlag: "opencode", payload: [
        "hook_event_name": "session.status",
        "session_id": "oc-1",
        "status_type": "idle"
    ])?.status == "done")
    check(HookEvents.interpret(sourceFlag: "opencode", payload: [
        "hook_event_name": "tool.execute.before",
        "session_id": "oc-1",
        "args": ["command": "SECRET"]
    ])?.status == "running")
    let asked = HookEvents.interpret(sourceFlag: "opencode", payload: [
        "hook_event_name": "question.asked",
        "session_id": "oc-1",
        "question": "SECRET question"
    ])
    check(asked?.status == "wait")
    check(asked?.waitsForPermission == false)
    check(clean(kept(asked)))
    check(HookEvents.interpret(sourceFlag: "opencode", payload: [
        "hook_event_name": "permission.asked",
        "session_id": "oc-1"
    ])?.status == "wait")
    for name in ["permission.replied", "question.replied", "question.rejected"] {
        check(HookEvents.interpret(sourceFlag: "opencode", payload: [
            "hook_event_name": name,
            "session_id": "oc-1",
            "question": "SECRET"
        ])?.status == "running")
    }
    check(HookEvents.interpret(sourceFlag: "opencode", payload: [
        "hook_event_name": "session.idle",
        "session_id": "oc-1"
    ])?.status == "done")
    let failedSession = HookEvents.interpret(sourceFlag: "opencode", payload: [
        "hook_event_name": "session.error",
        "session_id": "oc-1",
        "error": ["message": "SECRET"]
    ])
    check(failedSession?.status == "fail")
    check(clean(kept(failedSession)))
    check(HookEvents.interpret(sourceFlag: "opencode", payload: [
        "hook_event_name": "message.updated",
        "session_id": "oc-1",
        "text": "SECRET"
    ]) == nil)

    let grokRunning = ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "SubagentStart", "PermissionDenied", "PostToolUseFailure"]
    for name in grokRunning {
        let item = HookEvents.interpret(sourceFlag: "grok", payload: [
            "hook_event_name": name,
            "sessionId": "grok-1",
            "prompt": "SECRET",
            "toolInput": ["command": "SECRET"],
            "last_assistant_message": "SECRET"
        ])
        check(item?.source == "grok-bot")
        check(item?.status == "running")
        check(item?.waitsForPermission == false)
        check(clean(kept(item)))
    }
    let grokWait = HookEvents.interpret(sourceFlag: "grok", payload: [
        "hook_event_name": "Notification",
        "notificationType": "permission_prompt",
        "sessionId": "grok-1",
        "message": "SECRET"
    ])
    check(grokWait?.status == "wait")
    check(grokWait?.waitsForPermission == false)
    check(clean(kept(grokWait)))
    let turn = HookEvents.interpret(
        sourceFlag: "cursor",
        payload: [
            "hook_event_name": "Stop",
            "cursor_version": "2.4.0",
            "sessionId": "grok-1",
            "toolInput": ["command": "SECRET"]
        ],
        environment: ["GROK_SESSION_ID": "sess"]
    )
    check(turn?.source == "grok-bot")
    check(turn?.status == "done")
    check(clean(kept(turn)))
    check(HookEvents.interpret(sourceFlag: "grok", payload: [
        "hook_event_name": "StopFailure",
        "sessionId": "grok-1",
        "error": "rate_limit"
    ])?.status == "fail")
    check(HookEvents.interpret(sourceFlag: "grok", payload: [
        "hook_event_name": "StopCancelled",
        "sessionId": "grok-1"
    ])?.status == "fail")
    let imported = HookEvents.interpret(
        sourceFlag: nil,
        payload: [
            "hook_event_name": "beforeSubmitPrompt",
            "cursor_version": "2.4.0",
            "conversation_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            "prompt": "SECRET prompt"
        ],
        environment: ["GROK_HOOK_EVENT": "user_prompt_submit"]
    )
    check(imported?.source == "grok-bot")
    check(imported?.status == "running")
    check(clean(kept(imported)))
    let heartbeat = HookEvents.interpret(
        sourceFlag: nil,
        payload: ["hook_event_name": "stop", "cursor_version": "1", "sessionId": "grok-1"],
        environment: ["GROK_SESSION_ID": "sess"]
    )
    check(heartbeat?.source == "grok-bot")
    check(heartbeat?.status == "heartbeat")
    var grokTasks: [String: Any] = [:]
    let failedTurn = HookEvents.interpret(sourceFlag: "grok", payload: [
        "hook_event_name": "StopFailure",
        "sessionId": "grok-1",
        "error": ["message": "SECRET"]
    ])!
    HookEvents.merge(failedTurn, into: &grokTasks, now: 1)
    HookEvents.merge(heartbeat!, into: &grokTasks, now: 2)
    check((grokTasks["grok-1"] as? [String: Any])?["status"] as? String == "fail")
    let idle = HookEvents.interpret(sourceFlag: "grok", payload: [
        "hook_event_name": "Notification",
        "notificationType": "idle_prompt",
        "sessionId": "grok-1",
        "message": "SECRET idle"
    ])
    check(idle == nil)
    check((grokTasks["grok-1"] as? [String: Any])?["status"] as? String == "fail")
    let closed = HookEvents.interpret(sourceFlag: "grok", payload: [
        "hook_event_name": "SessionEnd",
        "sessionId": "grok-1"
    ])
    check(closed?.status == "closed")
    HookEvents.merge(closed!, into: &grokTasks, now: 3)
    check(grokTasks["grok-1"] == nil)
    let plainCursor = HookEvents.interpret(
        sourceFlag: nil,
        payload: ["hook_event_name": "stop", "cursor_version": "1"],
        environment: ["GROK_SESSION_ID": "   ", "GROK_HOOK_EVENT": ""]
    )
    check(plainCursor?.source == "cursor")
    check(plainCursor?.status == "heartbeat")
    let importedStop = HookEvents.interpret(sourceFlag: "grok", payload: [
        "hook_event_name": "SubagentStop",
        "sessionId": "grok-1"
    ])
    check(importedStop?.source == "grok-bot")
    check(importedStop?.status == "done")

    let rows = AgentIconList.rows([
        AgentIconCandidate(id: "c", source: "copilot", title: "Copilot", status: "running", read: false, updated: 1),
        AgentIconCandidate(id: "o", source: "opencode", title: "OpenCode", status: "wait", read: false, updated: 2),
        AgentIconCandidate(id: "k", source: "claude", title: "Claude", status: "running", read: false, updated: 3)
    ])
    check(rows.map(\.source) == ["claude", "copilot", "opencode"])
    let copilotLaunch = AgentLaunch.plan(source: "github copilot") { _ in false }
    check(copilotLaunch.appPath == nil)
    check(copilotLaunch.url == "https://github.com/features/copilot")
    check(AgentLaunch.plan(source: "open-code") { _ in false }.url == "https://opencode.ai")

    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-passive-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let binary = directory.appendingPathComponent("MochiHookCLI")
    try Data("#!/bin/sh\nexit 0\n".utf8).write(to: binary)
    try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: binary.path)
    let blockedDir = directory.appendingPathComponent("blocked", isDirectory: true)
    try FileManager.default.createDirectory(at: blockedDir, withIntermediateDirectories: true)
    let blocked = PassiveHooks.installCopilot(directory: blockedDir, binaryURL: binary, privacyAccepted: false)
    check(blocked.needsPrivacy)
    check(blocked.connected == false)
    check(FileManager.default.fileExists(atPath: blockedDir.appendingPathComponent("mochi-island.json").path) == false)
    let missing = PassiveHooks.installGrok(directory: blockedDir, binaryURL: directory.appendingPathComponent("missing"), privacyAccepted: true)
    check(missing.error == "hook program missing")
    check(FileManager.default.fileExists(atPath: blockedDir.appendingPathComponent("mochi-island.json").path) == false)

    let copilotDir = directory.appendingPathComponent("copilot-hooks", isDirectory: true)
    try FileManager.default.createDirectory(at: copilotDir, withIntermediateDirectories: true)
    let copilotFile = copilotDir.appendingPathComponent("mochi-island.json")
    try "keep-user".write(to: copilotFile, atomically: true, encoding: .utf8)
    let installed = PassiveHooks.installCopilot(directory: copilotDir, binaryURL: binary, privacyAccepted: true)
    check(installed.connected)
    let backup = try String(contentsOf: copilotFile.appendingPathExtension("mochi-island.bak"), encoding: .utf8)
    check(backup == "keep-user")
    let copilotJSON = try JSONSerialization.jsonObject(with: Data(contentsOf: copilotFile)) as? [String: Any]
    check(copilotJSON?["version"] as? Int == 1)
    let copilotHooks = copilotJSON?["hooks"] as? [String: Any]
    check(Set(copilotHooks?.keys.map { String($0) } ?? []) == Set(PassiveHooks.copilotEvents))
    for name in PassiveHooks.copilotEvents {
        let entry = (copilotHooks?[name] as? [[String: Any]])?.first
        let command = entry?["command"] as? String ?? ""
        check(command == entry?["bash"] as? String)
        check(command.contains(binary.path))
        check(command.contains("--source copilot"))
        check(command.contains("--event \(name)"))
        check(entry?["timeoutSec"] as? Int == 10)
        check(entry?[AppConfig.hookMarker] as? Int == AppConfig.hookVersion)
        check(entry?["hooks"] == nil)
    }
    let other = directory.appendingPathComponent("OtherCLI")
    try Data("#!/bin/sh\nexit 0\n".utf8).write(to: other)
    try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: other.path)
    check(PassiveHooks.copilotStatus(directory: copilotDir, binaryURL: other).connected == false)

    let grokDir = directory.appendingPathComponent("grok-hooks", isDirectory: true)
    let grokInstall = PassiveHooks.installGrok(directory: grokDir, binaryURL: binary, privacyAccepted: true)
    check(grokInstall.connected)
    let grokFile = grokDir.appendingPathComponent("mochi-island.json")
    let grokJSON = try JSONSerialization.jsonObject(with: Data(contentsOf: grokFile)) as? [String: Any]
    check(grokJSON?["version"] == nil)
    let grokHooks = grokJSON?["hooks"] as? [String: Any]
    check(Set(grokHooks?.keys.map { String($0) } ?? []) == Set(PassiveHooks.grokEvents))
    check(grokHooks?["SubagentStop"] == nil)
    for name in PassiveHooks.grokEvents {
        let group = (grokHooks?[name] as? [[String: Any]])?.first
        let entry = (group?["hooks"] as? [[String: Any]])?.first
        let command = entry?["command"] as? String ?? ""
        check(command.contains(binary.path))
        check(command.contains("--source grok"))
        check(command.contains("--event") == false)
        check(entry?["timeout"] as? Int == 10)
        check(entry?[AppConfig.hookMarker] as? Int == AppConfig.hookVersion)
    }
    let names = try FileManager.default.contentsOfDirectory(at: grokDir, includingPropertiesForKeys: nil).map(\.lastPathComponent)
    check(names == ["mochi-island.json"])
    check(names.contains { $0.contains(".tmp") } == false)

    let plugin = directory.appendingPathComponent("plugins/mochi-island.js")
    try FileManager.default.createDirectory(at: plugin.deletingLastPathComponent(), withIntermediateDirectories: true)
    try "old-plugin".write(to: plugin, atomically: true, encoding: .utf8)
    let open = PassiveHooks.installOpenCode(pluginURL: plugin, binaryURL: binary, privacyAccepted: true)
    check(open.connected)
    let pluginBackup = try String(contentsOf: plugin.appendingPathExtension("mochi-island.bak"), encoding: .utf8)
    check(pluginBackup == "old-plugin")
    let script = try String(contentsOf: plugin, encoding: .utf8)
    for name in ["session.status", "tool.execute.before", "permission.asked", "permission.replied", "question.asked", "question.replied", "question.rejected", "session.idle", "session.error"] {
        check(script.contains(name))
    }
    check(script.contains("spawnSync"))
    check(script.contains("\"--source\", \"opencode\""))
    check(script.contains("event.properties"))
    check(script.contains("message.updated") == false)
    check(script.contains("message.part") == false)
    check(script.contains("http://") == false)
    check(script.contains("https://") == false)
    check(script.contains("createServer") == false)
    let removed = PassiveHooks.remove(fileURL: plugin, binaryURL: binary)
    check(removed.connected == false)
    check(FileManager.default.fileExists(atPath: plugin.path) == false)

    let tasksURL = directory.appendingPathComponent("tasks.json")
    let raw: [String: Any] = ["tasks": ["gone": ["id": "gone", "status": "closed", "source": "copilot", "updated": 1]]]
    try JSONSerialization.data(withJSONObject: raw).write(to: tasksURL)
    check(TaskReader.load(tasksURL: tasksURL, now: 10).isEmpty)
}

func GeminiHookMapsTheShortlist() throws {
    let running = [
        "SessionStart", "BeforeAgent", "BeforeModel", "AfterModel",
        "BeforeToolSelection", "BeforeTool", "AfterTool"
    ]
    for name in running {
        let item = HookEvents.interpret(sourceFlag: "gemini", payload: [
            "hook_event_name": name,
            "session_id": "gem-1",
            "prompt": "SECRET",
            "tool_input": ["command": "SECRET"],
            "error": "tool failed",
            "transcript_path": "/tmp/SECRET/transcript.json"
        ])
        check(item?.source == "gemini")
        check(item?.status == "running")
        check(item?.waitsForPermission == false)
        check(item?.id == "gem-1")
        var tasks: [String: Any] = [:]
        HookEvents.merge(item!, into: &tasks, now: 3)
        let record = tasks["gem-1"] as? [String: Any] ?? [:]
        check(record["prompt"] == nil)
        check(record["tool_input"] == nil)
        check(record["transcript_path"] == nil)
        check(record["error"] == nil)
        check(record["status"] as? String == "running")
        let blob = record.values.compactMap { $0 as? String }.joined(separator: " ")
        check(blob.contains("SECRET") == false)
    }
    let pathOnly = HookEvents.interpret(sourceFlag: "gemini", payload: [
        "hook_event_name": "SessionStart",
        "transcript_path": "/tmp/SECRET/transcript.json",
        "prompt": "SECRET prompt"
    ])
    check(pathOnly?.id == "gemini-session")
    check(pathOnly?.status == "running")
    let permission = HookEvents.interpret(sourceFlag: "gemini", payload: [
        "hook_event_name": "Notification",
        "notification_type": "ToolPermission",
        "session_id": "gem-1",
        "message": "SECRET"
    ])
    check(permission?.status == "wait")
    check(permission?.waitsForPermission == false)
    check(permission?.options.isEmpty == true)
    check(permission?.permissionStyle.isEmpty == true)
    check(HookEvents.interpret(sourceFlag: "gemini", payload: [
        "hook_event_name": "Notification",
        "type": "Info",
        "session_id": "gem-1",
        "message": "SECRET"
    ]) == nil)
    var tasks: [String: Any] = [:]
    let finished = HookEvents.interpret(sourceFlag: "gemini", payload: [
        "hook_event_name": "AfterAgent",
        "session_id": "gem-1",
        "error": "model failed",
        "prompt": "SECRET"
    ])
    check(finished?.status == "done")
    HookEvents.merge(finished!, into: &tasks, now: 4)
    check((tasks["gem-1"] as? [String: Any])?["status"] as? String == "done")
    check((tasks["gem-1"] as? [String: Any])?["error"] == nil)
    let ended = HookEvents.interpret(sourceFlag: "gemini", payload: [
        "hook_event_name": "SessionEnd",
        "session_id": "gem-1",
        "transcript_path": "/tmp/SECRET/transcript.json"
    ])
    check(ended?.status == "closed")
    HookEvents.merge(ended!, into: &tasks, now: 5)
    check(tasks["gem-1"] == nil)
    check(HookInstaller.geminiEvents.contains { $0.lowercased().contains("fail") } == false)
    check(HookEvents.interpret(sourceFlag: "gemini", payload: [
        "hook_event_name": "StopFailure",
        "session_id": "gem-1"
    ]) == nil)
    let launch = AgentLaunch.plan(source: "gemini cli") { _ in false }
    check(launch.appPath == nil)
    check(launch.url == "https://geminicli.com")
    check(SlotFace.agentMark(for: "gemini") == "agent-gemini")
    check(IslandPrefs().pickerSources.contains("gemini"))
    let rows = AgentIconList.rows([
        AgentIconCandidate(id: "g", source: "gemini", title: "Gemini", status: "running", read: false, updated: 1)
    ])
    check(rows.map(\.source) == ["gemini"])

    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("mochi-gemini-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let binary = directory.appendingPathComponent("MochiHookCLI")
    try Data("#!/bin/sh\nexit 0\n".utf8).write(to: binary)
    try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: binary.path)
    let settings = directory.appendingPathComponent("settings.json")
    let blocked = HookInstaller.installGemini(settingsURL: settings, binaryURL: binary, privacyAccepted: false)
    check(blocked.needsPrivacy)
    check(blocked.connected == false)
    check(FileManager.default.fileExists(atPath: settings.path) == false)
    let missing = HookInstaller.installGemini(settingsURL: settings, binaryURL: directory.appendingPathComponent("missing"), privacyAccepted: true)
    check(missing.error == "hook program missing")
    let previous: [String: Any] = [
        "theme": "dark",
        "hooks": [
            "BeforeTool": [[
                "hooks": [[
                    "type": "command",
                    "command": "/bin/echo keep"
                ]]
            ]]
        ]
    ]
    try JSONSerialization.data(withJSONObject: previous).write(to: settings)
    let installed = HookInstaller.installGemini(settingsURL: settings, binaryURL: binary, privacyAccepted: true)
    check(installed.connected)
    let backup = try String(contentsOf: settings.appendingPathExtension("mochi-island.bak"), encoding: .utf8)
    check(backup.contains("keep"))
    check(backup.contains("dark"))
    let saved = try JSONSerialization.jsonObject(with: Data(contentsOf: settings)) as? [String: Any]
    check(saved?["theme"] as? String == "dark")
    let hooks = saved?["hooks"] as? [String: Any]
    check(Set(hooks?.keys.map { String($0) } ?? []) == Set(HookInstaller.geminiEvents))
    for name in HookInstaller.geminiEvents {
        let groups = hooks?[name] as? [[String: Any]] ?? []
        let commands = groups.flatMap { $0["hooks"] as? [[String: Any]] ?? [] }
        check(commands.contains { entry in
            let command = entry["command"] as? String ?? ""
            return command.contains(binary.path)
                && command.contains("--source gemini")
                && entry[AppConfig.hookMarker] as? Int == AppConfig.hookVersion
                && entry["timeout"] as? Int == 10000
        })
    }
    let before = (hooks?["BeforeTool"] as? [[String: Any]]) ?? []
    let beforeCommands = before.flatMap { $0["hooks"] as? [[String: Any]] ?? [] }
    check(beforeCommands.contains { ($0["command"] as? String) == "/bin/echo keep" })
    let other = directory.appendingPathComponent("OtherCLI")
    try Data("#!/bin/sh\nexit 0\n".utf8).write(to: other)
    try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: other.path)
    check(HookInstaller.geminiStatus(settingsURL: settings, binaryURL: other).connected == false)
    let names = try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil).map(\.lastPathComponent)
    check(names.contains { $0.contains(".tmp") } == false)
    _ = HookInstaller.removeNested(fileURL: settings, binaryURL: binary)
    check(HookInstaller.geminiStatus(settingsURL: settings, binaryURL: binary).connected == false)
    let removed = try JSONSerialization.jsonObject(with: Data(contentsOf: settings)) as? [String: Any]
    check(removed?["theme"] as? String == "dark")
    let keptHooks = (removed?["hooks"] as? [String: Any])?["BeforeTool"] as? [[String: Any]] ?? []
    let keptCommands = keptHooks.flatMap { $0["hooks"] as? [[String: Any]] ?? [] }
    check(keptCommands.contains { ($0["command"] as? String) == "/bin/echo keep" })
    check(keptCommands.contains { $0[AppConfig.hookMarker] != nil } == false)
}

func ConnectorRegistryNamesCursorAndCodex() {
    let ids = AgentConnectorRegistry.primary.map(\.id)
    check(ids == ["cursor", "codex"])
    check(AgentConnectorRegistry.connector(for: "cursor")?.displayName == "Cursor")
    check(AgentConnectorRegistry.connector(for: "codex")?.displayName == "Codex")
    check(AgentConnectorRegistry.connector(for: "cursor")?.needsPrivacyAck == true)
    check(AgentConnectorRegistry.connector(for: "codex")?.needsPrivacyAck == false)
    check(AgentConnectorRegistry.connector(for: "claude") == nil)
}

func LiveHooksFixWhenAsked() {
        guard let binary = ProcessInfo.processInfo.environment["MOCHI_FIX_LIVE_HOOKS"], !binary.isEmpty else { return }
        let cursor = AppConfig.cursorDirectory
        let result = HookInstaller.install(
            hooksURL: cursor.appendingPathComponent("hooks.json"),
            binaryURL: URL(fileURLWithPath: binary),
            tasksURL: cursor.appendingPathComponent("mochi-island-tasks.json"),
            privacyAccepted: true
        )
        if !result.connected {
            fputs("FAIL live hooks \(result.error ?? "")\n", stderr)
            exit(1)
        }
        print("live hooks updated")
    }

PlayReducesEnergy()
DecayPutsThePetToSleepWithoutDeath()
PlayDoesNothingWhileAsleep()
FeedFromAIWakesAndDedupes()
FedTaskIDsStayBounded()
LowHappinessDoesNotSleep()
MoodsMatchTheOldThresholds()
try LegacyDeadSaveBecomesAsleep()
try SaveClampsHunger()
OnlyRunningToDoneFeeds()
try HookInstallBacksUpAndMarksEntries()
try TaskWatchFiresWhenTheFileIsReplaced()
try ReplyWriterOnlyAllowsPermissionActions()
try TaskReaderKeepsSource()
AgentLaunchFindsInstalledAppsAndCodexFallback()
try KittenPositionParsesAndClamps()
SpecConstantsAndShellGeometry()
AgentIconsStayOnePerConversation()
DockPrefDefaultsToRight()
try PickerFlagsPersistAcrossPrefs()
try EmptyFileHasNoSeeds()
try OldSeedMigrationKeepsActiveTasks()
RemoveWhenReadAndDone()
MergeKeepsSkinAndLeavesSlotsWhenTasksLeave()
CreateSlotAppendsAFaceWithoutATaskFile()
KittenV1FaceFilesFollowSource()
SelectingKeepsOnlyKnownIds()
NewWaitSelectsOnlyAfterTheFirstSnapshot()
try SubmittedChatKeepsTheAgentAndDropsThePrompt()
try AnsweredPermissionIsNotShownAgain()
try CodexAndClaudeHooksInstallMarkedCommands()
try HookRetargetPointsAtTheOpenApp()
try PassiveHooksReportTheShortlist()
try GeminiHookMapsTheShortlist()
ConnectorRegistryNamesCursorAndCodex()
LiveHooksFixWhenAsked()
print("MochiCore checks passed")
