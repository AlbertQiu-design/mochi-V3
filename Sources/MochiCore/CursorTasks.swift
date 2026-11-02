import Foundation

public struct CursorTask: Equatable {
    public var id: String
    public var title: String
    public var status: String
    public var kind: String
    public var prompt: String
    public var detail: String
    public var summary: String
    public var options: [[String: String]]
    public var allowMultiple: Bool
    public var questionID: String
    public var requestID: String
    public var source: String
    public var updated: Double
    /// How many prompt submits this conversation has recorded. Allow does not increment it.
    public var submitGen: Int

    public init(
        id: String,
        title: String,
        status: String,
        kind: String = "",
        prompt: String = "",
        detail: String = "",
        summary: String = "",
        options: [[String: String]] = [],
        allowMultiple: Bool = false,
        questionID: String = "",
        requestID: String = "",
        source: String = "cursor",
        updated: Double = 0,
        submitGen: Int = 0
    ) {
        self.id = id
        self.title = title
        self.status = status
        self.kind = kind
        self.prompt = prompt
        self.detail = detail
        self.summary = summary
        self.options = options
        self.allowMultiple = allowMultiple
        self.questionID = questionID
        self.requestID = requestID
        self.source = AgentLaunch.normalize(source)
        self.updated = updated
        self.submitGen = max(0, submitGen)
    }

    public var dictionary: [String: Any] {
        [
            "id": id,
            "title": title,
            "status": status,
            "source": source,
            "kind": kind,
            "prompt": prompt,
            "detail": detail,
            "summary": summary,
            "options": options,
            "allow_multiple": allowMultiple,
            "question_id": questionID,
            "request_id": requestID,
            "updated": updated,
            "submit_gen": submitGen
        ]
    }
}

public struct TaskTransitionTracker: Equatable {
    public var statusByID: [String: String] = [:]

    public init() {}

    public mutating func newlyCompleted(_ tasks: [CursorTask]) -> [String] {
        var feed: [String] = []
        var next: [String: String] = [:]
        for task in tasks {
            next[task.id] = task.status
            if let previous = statusByID[task.id],
               previous == "running" || previous == "wait",
               task.status == "done" {
                feed.append(task.id)
            }
        }
        statusByID = next
        return feed
    }
}

public enum TaskReader {
    public static func load(tasksURL: URL, now: Double = Date().timeIntervalSince1970, includePrompts: Bool = true) -> [CursorTask] {
        guard let data = try? Data(contentsOf: tasksURL),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tasks = root["tasks"] as? [String: Any] else {
            return []
        }
        let answered = Set((root["answered"] as? [String]) ?? [])
        var visible: [CursorTask] = []
        for (key, raw) in tasks {
            guard let item = raw as? [String: Any] else { continue }
            var status = item["status"] as? String ?? "running"
            if status == "closed" { continue }
            if !["running", "wait", "done", "fail"].contains(status) {
                status = "running"
            }
            let updated = (item["updated"] as? NSNumber)?.doubleValue ?? (item["updated"] as? Double) ?? 0
            // Running and waiting chats stay visible until they settle. Only finished
            // rows age out after doneKeep so the capsule can keep live icons.
            if status == "done" || status == "fail" {
                if updated > 0, now - updated > AppConfig.doneKeep { continue }
            }
            let id = item["id"] as? String ?? key
            let options = (item["options"] as? [[String: Any]] ?? []).compactMap { option -> [String: String]? in
                let oid = (option["id"] as? String) ?? ""
                let label = (option["label"] as? String) ?? ""
                guard !label.isEmpty else { return nil }
                return ["id": oid, "label": label]
            }
            let optionIDs = options.map { $0["id"] ?? "" }
            let kindRaw = item["kind"] as? String ?? ""
            let requestID = item["request_id"] as? String ?? ""
            let source = AgentLaunch.normalize(item["source"] as? String)
            let permission = PermissionCopy.isPermission(kind: kindRaw, status: status, optionIDs: optionIDs)
            let answeredWait = permission && status == "wait" && !requestID.isEmpty && answered.contains(requestID)
            if answeredWait { status = "running" }
            let rawPrompt = item["prompt"] as? String ?? ""
            let rawCommand = item["command"] as? String ?? ""
            var title = item["title"] as? String ?? ""
            if (!rawPrompt.isEmpty && title == rawPrompt) || (!rawCommand.isEmpty && title == rawCommand) {
                title = ""
            }
            let rawDetail = item["detail"] as? String ?? ""
            let rawSummary = item["summary"] as? String ?? ""
            let agent = SlotFace.displayName(for: source)
            let shownKind: String
            let shownDetail: String
            let shownSummary: String
            let shownOptions: [[String: String]]
            if permission && answeredWait {
                title = agent
                shownKind = ""
                shownDetail = ""
                shownSummary = includePrompts ? PermissionCopy.abbreviate(rawSummary) : ""
                shownOptions = []
            } else if permission {
                title = PermissionCopy.actionKind(detail: rawDetail, title: title)
                shownKind = "permission"
                shownDetail = ""
                // Permission cards need the chat blurb even when other prompt fields stay hidden.
                shownSummary = PermissionCopy.abbreviate(rawSummary)
                shownOptions = options
            } else {
                title = PermissionCopy.visibleTitle(
                    kind: "",
                    status: status,
                    title: title.isEmpty ? agent : title,
                    detail: rawDetail,
                    agent: agent,
                    optionIDs: []
                )
                shownKind = includePrompts ? kindRaw : ""
                shownDetail = includePrompts ? PermissionCopy.visibleDetail(kind: "", status: status, detail: rawDetail, optionIDs: []) : ""
                shownSummary = includePrompts ? PermissionCopy.abbreviate(rawSummary) : ""
                shownOptions = options
            }
            visible.append(CursorTask(
                id: id,
                title: title,
                status: status,
                kind: shownKind,
                prompt: "",
                detail: shownDetail,
                summary: shownSummary,
                options: shownOptions,
                allowMultiple: item["allow_multiple"] as? Bool ?? false,
                questionID: item["question_id"] as? String ?? "",
                requestID: item["request_id"] as? String ?? "",
                source: AgentLaunch.normalize(item["source"] as? String),
                updated: updated,
                submitGen: (item["submit_gen"] as? NSNumber)?.intValue ?? 0
            ))
        }
        return visible.sorted { $0.id < $1.id }
    }

    public static func transcriptHints(now: Double = Date().timeIntervalSince1970) -> [CursorTask] {
        let root = AppConfig.cursorDirectory.appendingPathComponent("projects", isDirectory: true)
        guard let enumerator = FileManager.default.enumerator(
            at: root,
            includingPropertiesForKeys: [.contentModificationDateKey],
            options: [.skipsHiddenFiles]
        ) else {
            return []
        }
        var hints: [CursorTask] = []
        for case let url as URL in enumerator {
            if hints.count >= 20 { break }
            guard url.pathExtension == "jsonl", url.path.contains("agent-transcripts") else { continue }
            let modified = (try? url.resourceValues(forKeys: [.contentModificationDateKey]))?
                .contentModificationDate?.timeIntervalSince1970 ?? 0
            guard modified > 0, now - modified <= AppConfig.liveKeep else { continue }
            let id = url.deletingPathExtension().lastPathComponent
            hints.append(CursorTask(id: id, title: "Cursor task", status: "running", updated: modified))
        }
        return hints
    }
}

/// Transcript hints are optional and slow. The hook path reads the tasks file only.
public enum TranscriptHints {
    public static let refreshInterval: Double = 5

    public static func due(lastScan: Double, now: Double, enabled: Bool) -> Bool {
        guard enabled else { return false }
        if lastScan <= 0 { return true }
        return now - lastScan >= refreshInterval
    }

    public static func merge(_ tasks: [CursorTask], hints: [CursorTask], enabled: Bool) -> [CursorTask] {
        guard enabled, !hints.isEmpty else { return tasks }
        let known = Set(tasks.map(\.id))
        var combined = tasks
        for hint in hints where !known.contains(hint.id) {
            combined.append(hint)
        }
        return combined.sorted { $0.id < $1.id }
    }
}

public enum ReplyWriter {
    public static func writePermission(id: String, requestID: String, action: String, to url: URL) throws {
        try writeChoice(id: id, requestID: requestID, action: action, kind: "permission", to: url)
    }

    /// Writes the reply file, then marks that permission wait answered so it is not shown again.
    public static func answerPermission(
        id: String,
        requestID: String,
        action: String,
        kind: String,
        replyURL: URL,
        tasksURL: URL,
        now: Double = Date().timeIntervalSince1970
    ) throws {
        try writeChoice(id: id, requestID: requestID, action: action, kind: kind, to: replyURL)
        if PermissionCopy.answersPermission(kind: kind, action: action) {
            HookEvents.acknowledge(id: id, requestID: requestID, tasksURL: tasksURL, now: now)
        }
    }

    /// Writes the bubble reply. Permission actions stay allow or deny. Other choices store the option id.
    /// Opening an agent window is a separate path.
    public static func writeChoice(id: String, requestID: String, action: String, kind: String, to url: URL) throws {
        let trimmed = action.trimmingCharacters(in: .whitespacesAndNewlines)
        let normalizedKind = kind.trimmingCharacters(in: .whitespacesAndNewlines)
        let lower = trimmed.lowercased()
        let permission = normalizedKind == "permission" || (normalizedKind.isEmpty && (lower == "allow" || lower == "deny"))
        let storedAction: String
        if permission {
            storedAction = lower
            guard storedAction == "allow" || storedAction == "deny" else {
                throw ReplyError.unsupported
            }
        } else {
            guard !trimmed.isEmpty else { throw ReplyError.unsupported }
            storedAction = trimmed
        }
        let payload: [String: Any] = [
            "id": id,
            "request_id": requestID,
            "action": storedAction,
            "option_id": storedAction,
            "kind": permission ? "permission" : normalizedKind
        ]
        try AtomicFile.writeJSON(payload, to: url)
    }

    /// Allow and Deny close the dialogue once the reply file has been written.
    public static func closesBubble(kind: String, optionID: String) -> Bool {
        if kind.trimmingCharacters(in: .whitespacesAndNewlines) == "permission" { return true }
        switch optionID.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "allow", "deny": return true
        default: return false
        }
    }

    public enum ReplyError: Error {
        case unsupported
    }
}
