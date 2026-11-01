import Darwin
import Foundation

let allowedSources = ["cursor", "codex", "grok-bot", "gpt"]
let allowedStatus = ["running", "wait", "done", "fail"]

var source = ""
var id = ""
var title = ""
var status = "running"
var tasksPath = ""
var kind = ""
var prompt = ""
var remove = false

var args = Array(CommandLine.arguments.dropFirst())
func take() -> String {
    guard !args.isEmpty else {
        fputs("missing value\n", stderr)
        exit(2)
    }
    return args.removeFirst()
}
while !args.isEmpty {
    let arg = args.removeFirst()
    switch arg {
    case "--source":
        source = take()
    case "--id":
        id = take()
    case "--title":
        title = take()
    case "--status":
        status = take()
    case "--tasks":
        tasksPath = take()
    case "--kind":
        kind = take()
    case "--prompt":
        prompt = take()
    case "--remove":
        remove = true
    default:
        fputs("unknown \(arg)\n", stderr)
        exit(2)
    }
}

if !allowedSources.contains(source) || id.isEmpty || (!remove && !allowedStatus.contains(status)) {
    fputs("usage: mochi-upsert-task.sh --source cursor|codex|grok-bot|gpt --id ID [--title TEXT] [--status running|wait|done|fail] [--tasks PATH] [--remove]\n", stderr)
    exit(2)
}

let tasksURL: URL = {
    if tasksPath.isEmpty {
        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".cursor", isDirectory: true)
            .appendingPathComponent("mochi-island-tasks.json")
    }
    return URL(fileURLWithPath: tasksPath)
}()

do {
    try runUpsert(tasksURL: tasksURL, source: source, id: id, title: title, status: status, kind: kind, prompt: prompt, remove: remove)
} catch {
    fputs("\(error)\n", stderr)
    exit(1)
}

func runUpsert(tasksURL: URL, source: String, id: String, title: String, status: String, kind: String, prompt: String, remove: Bool) throws {

func loadTasks() -> [String: Any] {
    guard let data = try? Data(contentsOf: tasksURL),
          let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
          let tasks = root["tasks"] as? [String: Any] else {
        return [:]
    }
    return tasks
}

func saveTasks(_ tasks: [String: Any]) throws {
    let data = try JSONSerialization.data(withJSONObject: ["tasks": tasks], options: [.prettyPrinted, .sortedKeys])
    let directory = tasksURL.deletingLastPathComponent()
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let temp = directory.appendingPathComponent(".\(tasksURL.lastPathComponent).\(UUID().uuidString).tmp")
    try data.write(to: temp)
    if FileManager.default.fileExists(atPath: tasksURL.path) {
        _ = try FileManager.default.replaceItemAt(tasksURL, withItemAt: temp)
    } else {
        try FileManager.default.moveItem(at: temp, to: tasksURL)
    }
}

let lockURL = tasksURL.appendingPathExtension("lock")
try? FileManager.default.createDirectory(at: tasksURL.deletingLastPathComponent(), withIntermediateDirectories: true)
let fd = open(lockURL.path, O_CREAT | O_RDWR, 0o644)
if fd >= 0 { flock(fd, LOCK_EX) }
defer {
    if fd >= 0 {
        flock(fd, LOCK_UN)
        close(fd)
    }
}

var tasks = loadTasks()
if remove {
    tasks.removeValue(forKey: id)
    try saveTasks(tasks)
    print("removed \(id)")
    exit(0)
}

var previous = tasks[id] as? [String: Any] ?? [:]
previous["id"] = id
previous["title"] = title.isEmpty ? (previous["title"] as? String ?? "Task") : title
previous["status"] = status
previous["updated"] = Date().timeIntervalSince1970
previous["source"] = source
previous["kind"] = kind.isEmpty ? (previous["kind"] as? String ?? "") : kind
previous["prompt"] = prompt.isEmpty ? (previous["prompt"] as? String ?? "") : prompt
if previous["detail"] == nil { previous["detail"] = "" }
if previous["options"] == nil { previous["options"] = [] as [[String: String]] }
if previous["allow_multiple"] == nil { previous["allow_multiple"] = false }
if previous["question_id"] == nil { previous["question_id"] = "" }
if previous["request_id"] == nil { previous["request_id"] = "" }
var aliases = previous["aliases"] as? [String] ?? []
if !aliases.contains(id) { aliases.append(id) }
previous["aliases"] = aliases
tasks[id] = previous
try saveTasks(tasks)
let shown = try JSONSerialization.data(withJSONObject: previous, options: [.sortedKeys])
FileHandle.standardOutput.write(shown)
FileHandle.standardOutput.write(Data("\n".utf8))
}
