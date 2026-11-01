import Darwin
import Foundation
import MochiCore

let stateURL = AppConfig.cursorDirectory.appendingPathComponent("mochi-island-tasks.json")
let replyURL = AppConfig.cursorDirectory.appendingPathComponent("mochi-island-reply.json")
let permissionTimeout: TimeInterval = 280

func emit(_ object: Any) {
    let data = (try? JSONSerialization.data(withJSONObject: object)) ?? Data("{}".utf8)
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write(Data("\n".utf8))
}

func readStdin() -> [String: Any] {
    let data = FileHandle.standardInput.readDataToEndOfFile()
    guard !data.isEmpty,
          let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
        return [:]
    }
    return json
}

func argumentValue(_ name: String) -> String? {
    let args = Array(CommandLine.arguments.dropFirst())
    var index = 0
    while index < args.count {
        if args[index] == name, index + 1 < args.count {
            return args[index + 1]
        }
        index += 1
    }
    return nil
}

func withLock(_ body: () -> Void) {
    try? FileManager.default.createDirectory(at: stateURL.deletingLastPathComponent(), withIntermediateDirectories: true)
    let lockURL = stateURL.appendingPathExtension("lock")
    let fd = open(lockURL.path, O_CREAT | O_RDWR, 0o644)
    if fd >= 0 {
        flock(fd, LOCK_EX)
        body()
        flock(fd, LOCK_UN)
        close(fd)
    } else {
        body()
    }
}

func readReply() -> [String: Any] {
    guard let data = try? Data(contentsOf: replyURL),
          let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
        return [:]
    }
    return json
}

func clearStaleReply(itemID: String, requestID: String) {
    let data = readReply()
    let action = (data["action"] as? String ?? "").lowercased()
    guard action == "allow" || action == "deny" else { return }
    let rid = data["request_id"] as? String ?? ""
    let tid = data["id"] as? String ?? ""
    let matchesCurrent: Bool
    if !requestID.isEmpty {
        matchesCurrent = rid == requestID
    } else {
        matchesCurrent = tid == itemID
    }
    guard !matchesCurrent else { return }
    // Drop a leftover Allow/Deny so the next Shell/MCP wait needs a fresh click.
    try? AtomicFile.writeJSON(
        ["id": "", "request_id": "", "action": "", "kind": "permission"] as [String: Any],
        to: replyURL
    )
}

func waitPermission(itemID: String, requestID: String) -> String {
    clearStaleReply(itemID: itemID, requestID: requestID)
    let deadline = Date().timeIntervalSince1970 + permissionTimeout
    while Date().timeIntervalSince1970 < deadline {
        let data = readReply()
        let action = (data["action"] as? String ?? "").lowercased()
        let rid = data["request_id"] as? String ?? ""
        let tid = data["id"] as? String ?? ""
        if !requestID.isEmpty, rid == requestID, action == "allow" || action == "deny" {
            return action
        }
        if requestID.isEmpty, tid == itemID, action == "allow" || action == "deny" {
            return action
        }
        Thread.sleep(forTimeInterval: 0.2)
    }
    // No user click: refuse the tool. Exit 0 still keeps Cursor unblocked.
    return "deny"
}

func run() {
    let payload = readStdin()
    let outcome = HookEvents.interpret(
        sourceFlag: argumentValue("--source"),
        payload: payload,
        environment: ProcessInfo.processInfo.environment,
        eventFlag: argumentValue("--event")
    )
    if let outcome {
        withLock {
            HookEvents.commit(outcome, tasksURL: stateURL)
        }
        if outcome.waitsForPermission {
            let action = waitPermission(itemID: outcome.id, requestID: outcome.requestID)
            // Mark the wait answered so the card and status update even if the UI
            // already wrote the reply file, or only the hook saw the click.
            withLock {
                HookEvents.acknowledge(
                    id: outcome.id,
                    requestID: outcome.requestID,
                    tasksURL: stateURL
                )
            }
            emit(HookEvents.permissionStdout(style: outcome.permissionStyle, action: action))
            return
        }
    }
    emit([:] as [String: Any])
}

run()
exit(0)
