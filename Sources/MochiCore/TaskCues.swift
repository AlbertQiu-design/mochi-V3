import Foundation

/// Which task moment should make a sound. The shell maps these onto the saved cue timbres.
public enum TaskCue: String, Equatable {
    case submit
    case complete
    case stuck
}

public struct TaskCueEvent: Equatable {
    public var id: String
    public var status: String
    public var submitGen: Int
    public var updated: Double

    public init(id: String, status: String, submitGen: Int, updated: Double) {
        self.id = id
        self.status = status
        self.submitGen = max(0, submitGen)
        self.updated = updated
    }
}

/// Plays each cue once per task when that moment actually changes.
/// The first snapshot is silent, so tasks already on screen at launch do not chime.
/// A later poll with the same status and submit count stays quiet.
public struct TaskCueTracker: Equatable {
    public struct Memory: Equatable {
        public var status: String
        public var submitGen: Int
    }

    public var seen: [String: Memory] = [:]
    public var primed = false

    public init() {}

    /// Newest cue first, so the one-second alert gate keeps the latest moment.
    public mutating func observe(_ tasks: [TaskCueEvent]) -> [TaskCue] {
        if !primed {
            primed = true
            remember(tasks)
            return []
        }
        var cues: [TaskCue] = []
        for task in tasks.sorted(by: { $0.updated > $1.updated }) {
            if let cue = cue(for: task, previous: seen[task.id]) {
                cues.append(cue)
            }
            seen[task.id] = Memory(status: task.status, submitGen: task.submitGen)
        }
        return cues
    }

    private mutating func remember(_ tasks: [TaskCueEvent]) {
        for task in tasks {
            seen[task.id] = Memory(status: task.status, submitGen: task.submitGen)
        }
    }

    private func cue(for task: TaskCueEvent, previous: Memory?) -> TaskCue? {
        let genIncreased = task.submitGen > (previous?.submitGen ?? 0)
        let statusChanged = previous.map { $0.status != task.status } ?? true
        guard statusChanged || genIncreased else { return nil }
        if statusChanged {
            switch task.status {
            case "done":
                return .complete
            case "fail", "wait":
                return .stuck
            case "running":
                // A new running row is a submit only when a prompt was actually sent.
                // Allow moves wait → running without bumping the submit count.
                return genIncreased ? .submit : nil
            default:
                return nil
            }
        }
        return genIncreased ? .submit : nil
    }
}
