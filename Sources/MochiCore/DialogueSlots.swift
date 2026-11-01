import Foundation

public struct DialogueSlot: Codable, Equatable {
    public var id: String
    public var source: String
    public var title: String
    public var skin: String
    public var created: Double
    public var read: Bool
    public var status: String

    public init(id: String, source: String, title: String, skin: String, created: Double, read: Bool = false, status: String = "") {
        self.id = id
        self.source = source
        self.title = title
        self.skin = skin
        self.created = created
        self.read = read
        self.status = status
    }

    public var dictionary: [String: Any] {
        [
            "id": id,
            "source": source,
            "title": title,
            "skin": skin,
            "created": created,
            "read": read,
            "status": status
        ]
    }

    enum CodingKeys: String, CodingKey {
        case id, source, title, skin, created, read, status
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(String.self, forKey: .id)
        source = try container.decodeIfPresent(String.self, forKey: .source) ?? "cursor"
        title = try container.decodeIfPresent(String.self, forKey: .title) ?? "New chat"
        skin = try container.decodeIfPresent(String.self, forKey: .skin) ?? "cursor"
        created = try container.decodeIfPresent(Double.self, forKey: .created) ?? 0
        read = try container.decodeIfPresent(Bool.self, forKey: .read) ?? false
        status = try container.decodeIfPresent(String.self, forKey: .status) ?? ""
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(id, forKey: .id)
        try container.encode(source, forKey: .source)
        try container.encode(title, forKey: .title)
        try container.encode(skin, forKey: .skin)
        try container.encode(created, forKey: .created)
        try container.encode(read, forKey: .read)
        try container.encode(status, forKey: .status)
    }
}

public struct DialogueSlotStore: Codable, Equatable {
    public static let skins = ["cursor", "grok-bot", "mochi", "codex", "claude", "gpt"]

    public var selected: String
    public var slots: [DialogueSlot]

    public init(selected: String = "", slots: [DialogueSlot] = []) {
        self.selected = selected
        self.slots = slots
    }

    public static func face(for source: String) -> String {
        switch AgentLaunch.normalize(source) {
        case "grok-bot": return "grok-bot"
        case "mochi": return "mochi"
        case "codex": return "codex"
        case "claude": return "claude"
        case "copilot": return "copilot"
        case "opencode": return "opencode"
        case "gemini": return "gemini"
        case "gpt": return "gpt"
        default: return "cursor"
        }
    }

    /// Kept for older callers; face now follows the source, not a hash of the id.
    public static func stableSkin(for id: String) -> String {
        face(for: id.hasPrefix("seed-") ? String(id.dropFirst(5)) : "cursor")
    }

    public func normalized() -> DialogueSlotStore {
        var next = self
        next.slots = next.slots.map { slot in
            var copy = slot
            copy.source = AgentLaunch.normalize(copy.source)
            copy.skin = Self.face(for: copy.source)
            if copy.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                copy.title = "New chat"
            }
            return copy
        }
        if !next.slots.contains(where: { $0.id == next.selected }) {
            next.selected = next.slots.first?.id ?? ""
        }
        return next
    }

    /// Drops preset `seed-*` rows and the kitten row when they are not an active task.
    public func droppingIdleSeeds(activeTaskIDs: Set<String>) -> DialogueSlotStore {
        var next = normalized()
        next.slots.removeAll { slot in
            let seeded = slot.id.hasPrefix("seed-")
            let kitten = AgentLaunch.normalize(slot.source) == "mochi"
            guard seeded || kitten else { return false }
            return !activeTaskIDs.contains(slot.id)
        }
        if !next.slots.contains(where: { $0.id == next.selected }) {
            next.selected = next.slots.first?.id ?? ""
        }
        return next
    }

    /// A read finished slot stays while its task is still listed, so the next merge cannot
    /// create it again as unread. Once that task ages out, the slot itself leaves.
    public func removingReadDone(tasks: [CursorTask]) -> DialogueSlotStore {
        let live = Dictionary(tasks.map { ($0.id, $0.status) }, uniquingKeysWith: { _, latest in latest })
        var next = normalized()
        next.slots.removeAll { slot in
            guard slot.read else { return false }
            if live[slot.id] != nil { return false }
            let state = slot.status
            return state == "done" || state == "fail"
        }
        if !next.slots.contains(where: { $0.id == next.selected }) {
            next.selected = next.slots.first?.id ?? ""
        }
        return next
    }

    /// Adds a slot for each task id that is not stored yet. Face follows the source.
    public func merged(with tasks: [CursorTask]) -> DialogueSlotStore {
        var next = normalized()
        var indexByID: [String: Int] = [:]
        for (index, slot) in next.slots.enumerated() {
            indexByID[slot.id] = index
        }
        for task in tasks {
            let title = task.title.trimmingCharacters(in: .whitespacesAndNewlines)
            if let index = indexByID[task.id] {
                next.slots[index].source = AgentLaunch.normalize(task.source)
                next.slots[index].skin = Self.face(for: next.slots[index].source)
                next.slots[index].status = task.status
                if !title.isEmpty {
                    next.slots[index].title = title
                }
                continue
            }
            let created = task.updated > 0 ? task.updated : 0
            let source = AgentLaunch.normalize(task.source)
            let slot = DialogueSlot(
                id: task.id,
                source: source,
                title: title.isEmpty ? "New chat" : title,
                skin: Self.face(for: source),
                created: created,
                status: task.status
            )
            indexByID[task.id] = next.slots.count
            next.slots.append(slot)
        }
        if !next.slots.contains(where: { $0.id == next.selected }) {
            next.selected = next.slots.first?.id ?? ""
        }
        return next
    }

    public func created(source: String, now: Double, rng: inout some RandomNumberGenerator) -> (DialogueSlotStore, DialogueSlot) {
        var next = normalized()
        _ = rng.next()
        let source = AgentLaunch.normalize(source)
        let slot = DialogueSlot(
            id: "slot-\(UUID().uuidString.lowercased())",
            source: source,
            title: "New chat",
            skin: Self.face(for: source),
            created: now
        )
        next.slots.append(slot)
        next.selected = slot.id
        return (next, slot)
    }

    public func selecting(_ id: String) -> DialogueSlotStore {
        var next = normalized()
        if next.slots.contains(where: { $0.id == id }) {
            next.selected = id
        }
        return next
    }

    public func markingRead(_ id: String) -> DialogueSlotStore {
        var next = normalized()
        if let index = next.slots.firstIndex(where: { $0.id == id }) {
            next.slots[index].read = true
        }
        return next
    }

    /// Selects the newest wait id that was not already known. The first call only records ids.
    public func selectingNewWaits(in tasks: [CursorTask], known: inout Set<String>, primed: inout Bool) -> DialogueSlotStore {
        var next = merged(with: tasks)
        let waits = tasks.filter { $0.status == "wait" }
        if primed {
            let fresh = waits.filter { !known.contains($0.id) }
            if let newest = fresh.max(by: { $0.updated < $1.updated }) {
                next.selected = newest.id
            }
        }
        known = Set(waits.map(\.id))
        primed = true
        return next
    }
}

public final class DialogueSlotDisk {
    public let fileURL: URL

    public init(directory: URL) {
        self.fileURL = directory.appendingPathComponent("dialogue-slots.json")
    }

    public static func live() -> DialogueSlotDisk {
        DialogueSlotDisk(directory: AppConfig.supportDirectory)
    }

    /// Missing files stay empty. Existing files drop idle seed and kitten rows.
    public func load(now: Double = Date().timeIntervalSince1970, activeTaskIDs: Set<String> = []) -> DialogueSlotStore {
        _ = now
        guard let existing = read() else {
            return DialogueSlotStore()
        }
        let migrated = existing.droppingIdleSeeds(activeTaskIDs: activeTaskIDs)
        if migrated != existing.normalized() {
            save(migrated)
        }
        return migrated
    }

    public func save(_ store: DialogueSlotStore) {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(store.normalized()) else { return }
        try? AtomicFile.write(data, to: fileURL)
    }

    private func read() -> DialogueSlotStore? {
        guard FileManager.default.fileExists(atPath: fileURL.path),
              let data = try? Data(contentsOf: fileURL),
              let decoded = try? JSONDecoder().decode(DialogueSlotStore.self, from: data) else {
            return nil
        }
        return decoded.normalized()
    }
}
