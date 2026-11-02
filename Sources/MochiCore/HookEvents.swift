import Foundation

/// One agent hook delivery, reduced to the fields Mochi keeps.
/// Full prompt, command, tool input, and message text are never stored.
/// A short local blurb may be kept so the capsule card can show what was submitted.
public struct HookOutcome: Equatable {
    public var id: String
    public var source: String
    public var title: String
    public var status: String
    public var kind: String
    public var requestID: String
    public var questionID: String
    public var options: [[String: String]]
    public var waitsForPermission: Bool
    /// `cursor` or `codex` when the hook must answer allow or deny. Empty prints `{}`.
    public var permissionStyle: String
    /// Short category such as Chat or Shell. Never prompt or command text.
    public var detail: String
    /// Abbreviated submit text for the bubble. Empty on permission-only deliveries.
    public var summary: String
    /// True only for a prompt event that carried submit text. Allow does not set this.
    public var promptSubmitted: Bool

    public init(
        id: String,
        source: String,
        title: String,
        status: String,
        kind: String = "",
        requestID: String = "",
        questionID: String = "",
        options: [[String: String]] = [],
        waitsForPermission: Bool = false,
        permissionStyle: String = "",
        detail: String = "",
        summary: String = "",
        promptSubmitted: Bool = false
    ) {
        self.id = id
        self.source = source
        self.title = title
        self.status = status
        self.kind = kind
        self.requestID = requestID
        self.questionID = questionID
        self.options = options
        self.waitsForPermission = waitsForPermission
        self.permissionStyle = permissionStyle
        self.detail = detail
        self.summary = summary
        self.promptSubmitted = promptSubmitted
    }
}

public enum PermissionCopy {
    public static let actionKinds = ["Shell", "MCP", "Permission"]

    public static func containsDebug(_ text: String) -> Bool {
        text.contains("PM手点") || text.contains("请选 Allow 或 Deny")
    }

    public static func isPermission(kind: String, status: String, optionIDs: [String]) -> Bool {
        if kind.trimmingCharacters(in: .whitespacesAndNewlines) == "permission" { return true }
        guard status == "wait" else { return false }
        let ids = Set(optionIDs.map { $0.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() })
        return ids.contains("allow") && ids.contains("deny")
    }

    /// Allow and Deny on a permission card. A mode switch uses a different kind.
    public static func answersPermission(kind: String, action: String) -> Bool {
        let action = action.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard action == "allow" || action == "deny" else { return false }
        let kind = kind.trimmingCharacters(in: .whitespacesAndNewlines)
        return kind == "permission" || kind.isEmpty
    }

    /// Shell, MCP, or Permission. Prompt text is never a title.
    public static func actionKind(detail: String, title: String = "") -> String {
        let detail = detail.trimmingCharacters(in: .whitespacesAndNewlines)
        if actionKinds.contains(detail) { return detail }
        let title = title.trimmingCharacters(in: .whitespacesAndNewlines)
        if actionKinds.contains(title) { return title }
        return "Permission"
    }

    public static func visibleTitle(
        kind: String,
        status: String,
        title: String,
        detail: String,
        agent: String,
        optionIDs: [String]
    ) -> String {
        if isPermission(kind: kind, status: status, optionIDs: optionIDs) {
            return actionKind(detail: detail, title: title)
        }
        let trimmed = title.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || trimmed == "New chat" || containsDebug(trimmed) {
            return agent
        }
        return trimmed
    }

    /// One-line description under the status. Permission cards can show the submit blurb.
    public static func visibleDetail(kind: String, status: String, detail: String, optionIDs: [String]) -> String {
        let trimmed = detail.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || containsDebug(trimmed) { return "" }
        if actionKinds.contains(trimmed) { return "" }
        if trimmed == "Chat" { return "" }
        _ = kind
        _ = status
        _ = optionIDs
        return trimmed
    }

    /// Question line under the status. Running, waiting, done, and failed cards all use it.
    /// Prefers the stored submit blurb, then a title that is the question itself.
    public static func cardBlurb(
        summary: String,
        detail: String,
        kind: String,
        status: String,
        optionIDs: [String],
        title: String = ""
    ) -> String {
        _ = status
        let stored = abbreviate(summary)
        if !stored.isEmpty && !containsDebug(stored) { return stored }
        let named = abbreviate(title)
        if isQuestionTitle(named) { return named }
        if isPermission(kind: kind, status: "wait", optionIDs: optionIDs) {
            switch actionKind(detail: detail) {
            case "Shell": return "Shell needs Allow"
            case "MCP": return "MCP needs Allow"
            default: return "Needs Allow or Deny"
            }
        }
        return visibleDetail(kind: kind, status: status, detail: detail, optionIDs: optionIDs)
    }

    /// Agent names and permission labels are not the user's question.
    private static func isQuestionTitle(_ title: String) -> Bool {
        let trimmed = title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !containsDebug(trimmed) else { return false }
        if actionKinds.contains(trimmed) { return false }
        switch trimmed {
        case "Cursor", "Codex", "Claude", "Gemini", "Copilot", "OpenCode", "Grok Bot", "GPT", "小猫", "New chat", "Permission", "Chat":
            return false
        default:
            return true
        }
    }

    /// First line of the submit text, clipped for the bubble. Stays on this Mac only.
    public static func abbreviate(_ text: String, limit: Int = 40) -> String {
        let flat = text
            .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        guard !flat.isEmpty else { return "" }
        guard flat.count > limit else { return flat }
        let end = flat.index(flat.startIndex, offsetBy: max(1, limit - 1))
        return String(flat[..<end]) + "…"
    }
}

public enum HookEvents {
    public static func interpret(
        sourceFlag: String?,
        payload: [String: Any],
        environment: [String: String] = [:],
        eventFlag: String? = nil
    ) -> HookOutcome? {
        let event = eventName(in: payload, fallback: eventFlag)
        guard !event.isEmpty else { return nil }
        let source = resolvedSource(flag: sourceFlag, payload: payload, environment: environment)
        let mapped = status(for: event, payload: payload, source: source)
        guard !mapped.isEmpty else { return nil }
        let permission = isPermission(event, source: source)
        let waits = permission && (source == "cursor" || source == "codex")
        let detail = safeDetail(for: event, source: source)
        let choice = choiceOptions(payload: payload, event: event)
        let options = waits ? allowDeny : choice.options
        let kind = waits ? "permission" : choice.kind
        // Permission deliveries keep summary empty here; merge restores the prior chat blurb.
        let summary = waits ? "" : chatSummary(for: event, payload: payload)
        return HookOutcome(
            id: sessionID(in: payload, source: source, event: event),
            source: source,
            title: waits ? PermissionCopy.actionKind(detail: detail) : SlotFace.displayName(for: source),
            status: mapped,
            kind: mapped == "wait" && waits ? kind : (mapped == "wait" ? "" : ""),
            requestID: waits ? requestID(in: payload) : "",
            questionID: waits ? choice.questionID : "",
            options: waits ? options : [],
            waitsForPermission: waits,
            permissionStyle: waits ? (source == "codex" ? "codex" : "cursor") : "",
            detail: detail,
            summary: summary,
            promptSubmitted: Self.isPromptSubmit(event) && !summary.isEmpty
        )
    }

    /// Chat submit events. An empty resume of `beforeSubmitPrompt` is not one of these.
    /// Cursor, Codex, and Claude spell the same event differently.
    private static func isPromptSubmit(_ event: String) -> Bool {
        switch normalizedEvent(event) {
        case "beforesubmitprompt", "userpromptsubmit", "userpromptsubmitted":
            return true
        default:
            return false
        }
    }

    private static func isSubagentEvent(_ event: String) -> Bool {
        switch normalizedEvent(event) {
        case "subagentstart", "subagentstop":
            return true
        default:
            return false
        }
    }

    private static func normalizedEvent(_ event: String) -> String {
        event.lowercased().replacingOccurrences(of: "_", with: "").replacingOccurrences(of: "-", with: "")
    }

    /// Merges one delivery into the task dictionary. The stored record is id, agent, status, and time,
    /// plus the permission fields the bubble needs to answer. Full prompt and command text stay out;
    /// a short summary may remain for the card.
    public static func merge(_ outcome: HookOutcome, into tasks: inout [String: Any], now: Double) {
        let (oldKey, previous) = find(tasks, id: outcome.id)
        var status = outcome.status
        var requestID = outcome.requestID
        var kind = outcome.kind
        var options = outcome.options
        var questionID = outcome.questionID
        var submitGen = whole(previous["submit_gen"])
        if outcome.promptSubmitted {
            submitGen += 1
        }
        if status == "closed" {
            tasks.removeValue(forKey: outcome.id)
            if oldKey != outcome.id { tasks.removeValue(forKey: oldKey) }
            return
        }
        if status == "heartbeat" {
            status = previous["status"] as? String ?? "running"
            if !["running", "wait", "done", "fail"].contains(status) { status = "running" }
            requestID = previous["request_id"] as? String ?? ""
            kind = previous["kind"] as? String ?? ""
            questionID = previous["question_id"] as? String ?? ""
            options = stringOptions(previous["options"])
        }
        var detail = outcome.detail
        if detail.isEmpty {
            detail = string(previous["detail"])
        }
        var summary = outcome.summary
        if summary.isEmpty {
            summary = string(previous["summary"])
        }
        if oldKey != outcome.id { tasks.removeValue(forKey: oldKey) }
        var record: [String: Any] = [
            "id": outcome.id,
            "title": outcome.title,
            "status": status,
            "source": outcome.source,
            "updated": now
        ]
        if !detail.isEmpty {
            record["detail"] = detail
        }
        if !summary.isEmpty {
            record["summary"] = summary
        }
        if submitGen > 0 {
            record["submit_gen"] = submitGen
        }
        if status == "wait" {
            record["kind"] = kind
            record["options"] = options
            record["request_id"] = requestID
            if !questionID.isEmpty { record["question_id"] = questionID }
        }
        tasks[outcome.id] = record
    }

    public static func commit(_ outcome: HookOutcome, tasksURL: URL, now: Double = Date().timeIntervalSince1970) {
        var root = readRoot(tasksURL)
        var tasks = root["tasks"] as? [String: Any] ?? [:]
        var delivery = outcome
        let answered = Set(stringList(root["answered"]))
        if delivery.status == "wait", !delivery.requestID.isEmpty, answered.contains(delivery.requestID) {
            delivery.status = "running"
            delivery.kind = ""
            delivery.options = []
            delivery.waitsForPermission = false
            delivery.requestID = ""
            delivery.questionID = ""
            delivery.title = SlotFace.displayName(for: delivery.source)
            delivery.detail = ""
        }
        merge(delivery, into: &tasks, now: now)
        scrub(&tasks)
        root["tasks"] = tasks
        try? AtomicFile.writeJSON(root, to: tasksURL)
    }

    /// Rewrites the task file without prompt text, command text, or permission debug copy.
    public static func scrubStored(tasksURL: URL) {
        var root = readRoot(tasksURL)
        guard var tasks = root["tasks"] as? [String: Any] else { return }
        let before = try? JSONSerialization.data(withJSONObject: tasks, options: [.sortedKeys])
        scrub(&tasks)
        let after = try? JSONSerialization.data(withJSONObject: tasks, options: [.sortedKeys])
        guard before != after else { return }
        root["tasks"] = tasks
        try? AtomicFile.writeJSON(root, to: tasksURL)
    }

    /// Records Allow or Deny and leaves the wait, so the card is not shown again.
    public static func acknowledge(id: String, requestID: String, tasksURL: URL, now: Double = Date().timeIntervalSince1970) {
        var root = readRoot(tasksURL)
        var tasks = root["tasks"] as? [String: Any] ?? [:]
        var answered = stringList(root["answered"])
        let trimmedRequest = requestID.trimmingCharacters(in: .whitespacesAndNewlines)
        let trimmedID = id.trimmingCharacters(in: .whitespacesAndNewlines)
        if !trimmedRequest.isEmpty, !answered.contains(trimmedRequest) {
            answered.append(trimmedRequest)
        }
        if answered.count > AppConfig.fedTaskLimit {
            answered = Array(answered.suffix(AppConfig.fedTaskLimit))
        }
        for key in Array(tasks.keys) {
            guard var record = tasks[key] as? [String: Any] else { continue }
            let recordID = string(record["id"]).isEmpty ? key : string(record["id"])
            let recordRequest = string(record["request_id"])
            let idMatch = !trimmedID.isEmpty && recordID == trimmedID
            let requestMatch = !trimmedRequest.isEmpty && recordRequest == trimmedRequest
            guard idMatch || requestMatch else { continue }
            guard string(record["status"]) == "wait", isPermissionRecord(record) else { continue }
            record["status"] = "running"
            record["title"] = SlotFace.displayName(for: string(record["source"]))
            record["updated"] = now
            record.removeValue(forKey: "kind")
            record.removeValue(forKey: "options")
            record.removeValue(forKey: "detail")
            record.removeValue(forKey: "question_id")
            record.removeValue(forKey: "prompt")
            record.removeValue(forKey: "command")
            tasks[key] = record
        }
        scrub(&tasks)
        root["answered"] = answered
        root["tasks"] = tasks
        try? AtomicFile.writeJSON(root, to: tasksURL)
    }

    public static func permissionStdout(style: String, action: String) -> [String: Any] {
        let decision = action == "deny" ? "deny" : "allow"
        if style == "codex" {
            return [
                "hookSpecificOutput": [
                    "hookEventName": "PermissionRequest",
                    "decision": ["behavior": decision]
                ]
            ]
        }
        return ["permission": decision]
    }

    private static let allowDeny: [[String: String]] = [
        ["id": "allow", "label": "Allow"],
        ["id": "deny", "label": "Deny"]
    ]

    private static func resolvedSource(flag: String?, payload: [String: Any], environment: [String: String]) -> String {
        if grokRuntime(environment) { return "grok-bot" }
        if let version = payload["cursor_version"] as? String,
           !version.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return "cursor"
        }
        let flagged = AgentLaunch.normalize(flag)
        switch flagged {
        case "codex", "claude", "grok-bot", "copilot", "opencode", "gemini":
            return flagged
        default:
            return "cursor"
        }
    }

    /// Grok Build sets one of these for every hook it runs, including a Cursor hook imported into Grok.
    private static func grokRuntime(_ environment: [String: String]) -> Bool {
        for key in ["GROK_SESSION_ID", "GROK_HOOK_EVENT"] {
            let value = environment[key]?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            if !value.isEmpty { return true }
        }
        return false
    }

    /// Copilot's camelCase payload does not include the event name, so the installed command passes it.
    /// A name already on the payload wins. Grok's PascalCase `hook_event_name` wins over its snake_case alias.
    private static func eventName(in payload: [String: Any], fallback: String?) -> String {
        let names = ["hook_event_name", "event", "hookEventName", "hook_event"]
        for name in names {
            if let value = payload[name] as? String {
                let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
                if !trimmed.isEmpty { return trimmed }
            }
        }
        return fallback?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    }

    private static func status(for event: String, payload: [String: Any], source: String) -> String {
        switch source {
        case "copilot": return copilotStatus(event, payload: payload)
        case "opencode": return opencodeStatus(event, payload: payload)
        case "gemini": return geminiStatus(event, payload: payload)
        case "grok-bot":
            if let native = grokNative(event, payload: payload) {
                return native
            }
            return cursorFamilyStatus(event, payload: payload, source: "cursor")
        default: return cursorFamilyStatus(event, payload: payload, source: source)
        }
    }

    /// GitHub Copilot CLI events. Unknown names are ignored. Session end closes the slot.
    private static func copilotStatus(_ event: String, payload: [String: Any]) -> String {
        switch event {
        case "sessionStart", "SessionStart",
             "userPromptSubmitted", "UserPromptSubmit",
             "preToolUse", "PreToolUse",
             "postToolUse", "PostToolUse",
             "subagentStart", "SubagentStart",
             "postToolUseFailure", "PostToolUseFailure":
            return "running"
        case "permissionRequest", "PermissionRequest":
            return "wait"
        case "notification", "Notification":
            let kind = token(payload, keys: ["notification_type", "notificationType", "type"])
            return kind == "permission_prompt" || kind == "elicitation_dialog" ? "wait" : ""
        case "errorOccurred", "ErrorOccurred":
            let context = token(payload, keys: ["errorContext", "error_context"])
            if context == "model_call" { return "fail" }
            if context == "tool_execution" { return "running" }
            return ""
        case "agentStop", "Stop":
            return "done"
        case "sessionEnd", "SessionEnd":
            return "closed"
        default:
            return ""
        }
    }

    /// Gemini CLI events. A tool error stays running. There is no failure event.
    private static func geminiStatus(_ event: String, payload: [String: Any]) -> String {
        switch event {
        case "SessionStart", "BeforeAgent", "BeforeModel", "AfterModel",
             "BeforeToolSelection", "BeforeTool", "AfterTool":
            return "running"
        case "Notification":
            let kind = token(payload, keys: ["notification_type", "notificationType", "type"])
            return kind == "ToolPermission" ? "wait" : ""
        case "AfterAgent":
            return "done"
        case "SessionEnd":
            return "closed"
        default:
            return ""
        }
    }

    /// OpenCode plugin events. Status type is read; retry text and question text are not.
    private static func opencodeStatus(_ event: String, payload: [String: Any]) -> String {
        switch event {
        case "session.status":
            switch token(payload, keys: ["status_type", "type"]) {
            case "busy", "retry": return "running"
            case "idle": return "done"
            default:
                if let status = payload["status"] as? [String: Any] {
                    switch string(status["type"]) {
                    case "busy", "retry": return "running"
                    case "idle": return "done"
                    default: return ""
                    }
                }
                return ""
            }
        case "tool.execute.before", "permission.replied", "question.replied", "question.rejected":
            return "running"
        case "permission.asked", "question.asked":
            return "wait"
        case "session.idle":
            return "done"
        case "session.error":
            return "fail"
        default:
            return ""
        }
    }

    /// xAI grok events. Nil means this is not one of those names, so a Cursor hook Grok imported can
    /// still update the same session. An empty string ignores the event. `idle_prompt` stays empty
    /// so it cannot cover a finished or failed turn. PascalCase `Stop` is the turn end, not Cursor's heartbeat.
    private static func grokNative(_ event: String, payload: [String: Any]) -> String? {
        switch event {
        case "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "SubagentStart",
             "PermissionDenied", "PostToolUseFailure":
            return "running"
        case "Notification":
            let kind = token(payload, keys: ["notification_type", "notificationType", "type"])
            return kind == "permission_prompt" ? "wait" : ""
        case "Stop":
            return "done"
        case "StopFailure", "StopCancelled":
            return "fail"
        case "SessionEnd":
            return "closed"
        default:
            return nil
        }
    }

    private static func cursorFamilyStatus(_ event: String, payload: [String: Any], source: String) -> String {
        let name = event.lowercased().replacingOccurrences(of: "_", with: "")
        if name == "stopfailure" { return "fail" }
        if isPermission(event, source: source) {
            return source == "claude" ? "running" : "wait"
        }
        if isChoice(payload: payload), !isFinishedTool(event) { return "wait" }
        switch name {
        case "afteragentthought", "afteragentresponse", "afterfileedit", "stop":
            return "heartbeat"
        case "beforesubmitprompt", "sessionstart", "subagentstart",
             "userpromptsubmit", "pretooluse", "posttooluse", "posttoolusefailure",
             "notification", "beforeshellexecution", "aftershellexecution",
             "beforemcpexecution", "aftermcpexecution":
            return "running"
        case "sessionend", "subagentstop":
            return failed(payload) ? "fail" : "done"
        default:
            return ""
        }
    }

    /// Short submit blurb for chat events only. Shell and MCP keep no command text here.
    /// Cursor may re-fire beforeSubmitPrompt with an empty prompt on resume; that yields "".
    /// A subagent card uses its `task` text. The stop hook's output `summary` is agent text and is ignored.
    private static func chatSummary(for event: String, payload: [String: Any]) -> String {
        if isPromptSubmit(event) {
            let prompt = promptText(in: payload)
            if prompt.isEmpty { return "" }
            return PermissionCopy.abbreviate(prompt)
        }
        if isSubagentEvent(event) {
            // Only when this delivery has its own id, so a parent chat keeps its submit blurb.
            guard !string(payload["subagent_id"]).isEmpty else { return "" }
            let task = stringValue(payload["task"]) ?? ""
            if task.isEmpty { return "" }
            return PermissionCopy.abbreviate(task)
        }
        return ""
    }

    /// Reads the user submit text from common Cursor / Codex / Claude shapes without keeping the full payload.
    private static func promptText(in payload: [String: Any]) -> String {
        for key in ["prompt", "user_prompt", "userPrompt", "prompt_text", "promptText", "text", "content", "message", "initialPrompt", "initial_prompt"] {
            if let text = stringValue(payload[key]), !text.isEmpty {
                return text
            }
        }
        let input = dictionary(payload, keys: ["tool_input", "input", "arguments", "args", "request", "payload", "data"])
        for key in ["prompt", "text", "content", "message", "user_prompt", "userPrompt"] {
            if let text = stringValue(input[key]), !text.isEmpty {
                return text
            }
        }
        return ""
    }

    private static func stringValue(_ value: Any?) -> String? {
        if let text = value as? String {
            let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
            return trimmed.isEmpty || trimmed == "nil" ? nil : trimmed
        }
        if let parts = value as? [Any] {
            var chunks: [String] = []
            for part in parts {
                if let text = part as? String {
                    let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
                    if !trimmed.isEmpty { chunks.append(trimmed) }
                    continue
                }
                guard let dict = part as? [String: Any] else { continue }
                let text = string(dict["text"]).isEmpty ? string(dict["content"]) : string(dict["text"])
                if !text.isEmpty { chunks.append(text) }
            }
            let joined = chunks.joined(separator: " ").trimmingCharacters(in: .whitespacesAndNewlines)
            return joined.isEmpty ? nil : joined
        }
        if let dict = value as? [String: Any] {
            for key in ["text", "content", "message", "prompt", "value"] {
                if let text = dict[key] as? String {
                    let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
                    if !trimmed.isEmpty && trimmed != "nil" { return trimmed }
                }
            }
        }
        return nil
    }

    /// A category for the bubble. The payload's prompt and command are never copied into detail.
    private static func safeDetail(for event: String, source: String) -> String {
        if isPromptSubmit(event) { return "Chat" }
        switch event {
        case "beforeShellExecution":
            return "Shell"
        case "beforeMCPExecution":
            return "MCP"
        case "PermissionRequest", "permission_request", "permissionRequest", "permission.asked":
            return source == "cursor" || source == "codex" || source == "claude" ? "Permission" : ""
        default:
            return ""
        }
    }

    private static func isPermission(_ event: String, source: String) -> Bool {
        guard source == "cursor" || source == "codex" || source == "claude" else { return false }
        switch event {
        case "beforeShellExecution", "beforeMCPExecution", "PermissionRequest", "permission_request":
            return true
        default:
            return false
        }
    }

    private static func token(_ payload: [String: Any], keys: [String]) -> String {
        for key in keys {
            let value = string(payload[key])
            if !value.isEmpty { return value }
        }
        return ""
    }

    private static func isFinishedTool(_ event: String) -> Bool {
        let name = event.lowercased().replacingOccurrences(of: "_", with: "")
        return ["posttooluse", "posttoolusefailure", "aftershellexecution", "aftermcpexecution"].contains(name)
    }

    private static func isChoice(payload: [String: Any]) -> Bool {
        let tool = toolName(payload)
        return tool == "AskQuestion" || tool == "CreatePlan" || tool == "SwitchMode"
    }

    private static func failed(_ payload: [String: Any]) -> Bool {
        if payload["error"] != nil { return true }
        let status = (payload["status"] as? String ?? payload["result"] as? String ?? "").lowercased()
        return ["error", "failed", "fail", "aborted", "cancelled", "canceled"].contains(status)
    }

    private static func toolName(_ payload: [String: Any]) -> String {
        for key in ["tool_name", "toolName", "tool"] {
            if let value = payload[key] as? String, !value.isEmpty { return value }
        }
        return ""
    }

    private static func choiceOptions(payload: [String: Any], event: String) -> (kind: String, questionID: String, options: [[String: String]]) {
        guard !isFinishedTool(event) else { return ("", "", []) }
        switch toolName(payload) {
        case "CreatePlan":
            return ("plan", "", [["id": "approve", "label": "Approve"], ["id": "keep", "label": "Keep planning"]])
        case "SwitchMode":
            return ("mode", "", [["id": "allow", "label": "Allow switch"], ["id": "stay", "label": "Stay"]])
        case "AskQuestion":
            let input = dictionary(payload, keys: ["tool_input", "input", "arguments", "args"])
            let questions = input["questions"] as? [Any] ?? []
            let first = questions.first as? [String: Any] ?? [:]
            let questionID = string(first["id"])
            return ("choice", questionID, optionList(first["options"] ?? input["options"]))
        default:
            return ("", "", [])
        }
    }

    private static func optionList(_ raw: Any?) -> [[String: String]] {
        guard let list = raw as? [Any] else { return [] }
        var options: [[String: String]] = []
        for item in list {
            guard let dict = item as? [String: Any] else { continue }
            var id = string(dict["id"]).isEmpty ? string(dict["value"]) : string(dict["id"])
            let label = string(dict["label"]).isEmpty ? string(dict["title"]) : string(dict["label"])
            if id.isEmpty { id = String(label.prefix(40)) }
            if id.isEmpty || label.isEmpty { continue }
            options.append(["id": String(id.prefix(80)), "label": String(label.prefix(160))])
        }
        return options
    }

    private static func requestID(in payload: [String: Any]) -> String {
        for key in ["generation_id", "tool_use_id", "request_id"] {
            let value = string(payload[key])
            if !value.isEmpty && value != "nil" { return value }
        }
        return String(DispatchTime.now().uptimeNanoseconds)
    }

    /// Uses id fields already in the JSON. A transcript path contributes only its file stem.
    /// The file itself is never opened.
    private static func sessionID(in payload: [String: Any], source: String, event: String) -> String {
        // A subagent delivery is its own card. The parent conversation id would hide its task blurb.
        if isSubagentEvent(event) {
            let subagent = string(payload["subagent_id"])
            if !subagent.isEmpty { return bareID(subagent) }
        }
        for key in ["conversation_id", "session_id", "sessionId", "sessionID", "subagent_id", "composer_id", "thread_id"] {
            let value = string(payload[key])
            if !value.isEmpty { return bareID(value) }
        }
        if let nested = payload["session"] as? [String: Any] {
            let value = string(nested["id"])
            if !value.isEmpty { return bareID(value) }
        }
        if source != "gemini", let path = payload["transcript_path"] as? String, !path.isEmpty {
            let stem = URL(fileURLWithPath: path).deletingPathExtension().lastPathComponent
            if !stem.isEmpty { return bareID(stem) }
        }
        return "\(source)-session"
    }

    private static func bareID(_ value: String) -> String {
        let pattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"
        if let range = value.range(of: pattern, options: .regularExpression) {
            return String(value[range]).lowercased()
        }
        return String(value.prefix(80))
    }

    private static func find(_ tasks: [String: Any], id: String) -> (String, [String: Any]) {
        if let item = tasks[id] as? [String: Any] { return (id, item) }
        for (key, raw) in tasks {
            guard let item = raw as? [String: Any] else { continue }
            if item["id"] as? String == id { return (key, item) }
            let aliases = item["aliases"] as? [String] ?? []
            if aliases.contains(id) { return (key, item) }
        }
        return (id, [:])
    }

    private static func stringList(_ value: Any?) -> [String] {
        value as? [String] ?? []
    }

    private static func isPermissionRecord(_ record: [String: Any]) -> Bool {
        let options = stringOptions(record["options"])
        return PermissionCopy.isPermission(
            kind: string(record["kind"]),
            status: string(record["status"]),
            optionIDs: options.map { $0["id"] ?? "" }
        )
    }

    /// Drops full prompt and command text. Keeps a short summary for the bubble.
    private static func scrub(_ tasks: inout [String: Any]) {
        for key in Array(tasks.keys) {
            guard var record = tasks[key] as? [String: Any] else { continue }
            let prompt = string(record["prompt"])
            let command = string(record["command"])
            for banned in ["prompt", "command", "tool_input", "toolInput", "message", "initialPrompt", "initial_prompt"] {
                record.removeValue(forKey: banned)
            }
            let rawDetail = string(record["detail"])
            var title = string(record["title"])
            if !prompt.isEmpty, title == prompt { title = "" }
            if !command.isEmpty, title == command { title = "" }
            if PermissionCopy.containsDebug(title) { title = "" }
            var detail = rawDetail
            if detail == prompt || detail == command || PermissionCopy.containsDebug(detail) {
                detail = ""
            }
            var summary = string(record["summary"])
            if !summary.isEmpty {
                summary = PermissionCopy.abbreviate(summary)
            }
            // Keep short blurbs. Only drop a summary that still holds the raw long
            // prompt/command text scrub is removing (abbreviate already clipped it).
            if PermissionCopy.containsDebug(summary) {
                summary = ""
            } else if !command.isEmpty, summary == command {
                summary = ""
            } else if !prompt.isEmpty, summary == prompt, prompt.count > 40 {
                summary = ""
            }
            if isPermissionRecord(record) {
                title = PermissionCopy.actionKind(detail: rawDetail, title: title)
                if !PermissionCopy.actionKinds.contains(detail) {
                    detail = ""
                }
            } else if title.isEmpty {
                title = SlotFace.displayName(for: string(record["source"]))
            }
            record["title"] = title
            if detail.isEmpty {
                record.removeValue(forKey: "detail")
            } else {
                record["detail"] = detail
            }
            if summary.isEmpty {
                record.removeValue(forKey: "summary")
            } else {
                record["summary"] = summary
            }
            tasks[key] = record
        }
    }

    private static func readRoot(_ url: URL) -> [String: Any] {
        guard let data = try? Data(contentsOf: url),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return [:]
        }
        return root
    }

    private static func stringOptions(_ value: Any?) -> [[String: String]] {
        if let typed = value as? [[String: String]] { return typed }
        guard let rows = value as? [[String: Any]] else { return [] }
        return rows.compactMap { item in
            let label = string(item["label"])
            guard !label.isEmpty else { return nil }
            return ["id": string(item["id"]), "label": label]
        }
    }

    private static func dictionary(_ payload: [String: Any], keys: [String]) -> [String: Any] {
        for key in keys {
            if let value = payload[key] as? [String: Any] { return value }
        }
        return [:]
    }

    private static func whole(_ value: Any?) -> Int {
        if let number = value as? Int { return number }
        if let number = value as? NSNumber { return number.intValue }
        if let number = value as? Double { return Int(number) }
        return 0
    }

    private static func string(_ value: Any?) -> String {
        guard let text = value as? String else { return "" }
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed == "nil" ? "" : trimmed
    }
}
