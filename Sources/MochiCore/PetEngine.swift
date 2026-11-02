import Foundation

public enum PetRules {
    public static let feedHunger = -32
    public static let feedEnergy = 8
    public static let feedHappiness = 10
    public static let playHunger = 10
    public static let playEnergy = -24
    public static let playHappiness = 16
    public static let decayHunger = 7
    public static let decayEnergy = -4
    public static let decayHappiness = -8
    public static let asleepHunger = 95
    public static let asleepEnergy = 0
    public static let decayInterval: Double = 6
    public static let moodHungry = 48
    public static let moodSleepy = 36
    public static let moodAngry = 30
    public static let angryPlayHunger = 52
    public static let moodActionHold: Double = 8
    public static let defaultName = "Mochi"
    public static let defaultHunger = 28
    public static let defaultEnergy = 72
    public static let defaultHappiness = 68
    public static let statMin = 0
    public static let statMax = 100
    public static let feedMessage = "Yum! That hit the spot."
}

public struct PetState: Codable, Equatable {
    public var name: String
    public var hunger: Int
    public var energy: Int
    public var happiness: Int
    public var asleep: Bool
    public var lastDecayTs: Double
    public var lastAction: String
    public var lastActionTs: Double
    public var fedTaskIDs: [String]

    public init(
        name: String = PetRules.defaultName,
        hunger: Int = PetRules.defaultHunger,
        energy: Int = PetRules.defaultEnergy,
        happiness: Int = PetRules.defaultHappiness,
        asleep: Bool = false,
        lastDecayTs: Double = Date().timeIntervalSince1970,
        lastAction: String = "",
        lastActionTs: Double = 0,
        fedTaskIDs: [String] = []
    ) {
        self.name = name
        self.hunger = hunger
        self.energy = energy
        self.happiness = happiness
        self.asleep = asleep
        self.lastDecayTs = lastDecayTs
        self.lastAction = lastAction
        self.lastActionTs = lastActionTs
        self.fedTaskIDs = fedTaskIDs
    }

    enum CodingKeys: String, CodingKey {
        case name
        case hunger
        case energy
        case happiness
        case asleep
        case lastDecayTs = "last_decay_ts"
        case lastAction = "last_action"
        case lastActionTs = "last_action_ts"
        case fedTaskIDs = "fed_task_ids"
        case isAlive = "is_alive"
    }

    public init(from decoder: Decoder) throws {
        let box = try decoder.container(keyedBy: CodingKeys.self)
        name = try box.decodeIfPresent(String.self, forKey: .name) ?? PetRules.defaultName
        hunger = try box.decodeIfPresent(Int.self, forKey: .hunger) ?? PetRules.defaultHunger
        energy = try box.decodeIfPresent(Int.self, forKey: .energy) ?? PetRules.defaultEnergy
        happiness = try box.decodeIfPresent(Int.self, forKey: .happiness) ?? PetRules.defaultHappiness
        lastDecayTs = try box.decodeIfPresent(Double.self, forKey: .lastDecayTs) ?? Date().timeIntervalSince1970
        lastAction = try box.decodeIfPresent(String.self, forKey: .lastAction) ?? ""
        lastActionTs = try box.decodeIfPresent(Double.self, forKey: .lastActionTs) ?? 0
        fedTaskIDs = try box.decodeIfPresent([String].self, forKey: .fedTaskIDs) ?? []
        if let asleep = try box.decodeIfPresent(Bool.self, forKey: .asleep) {
            self.asleep = asleep
        } else if let alive = try box.decodeIfPresent(Bool.self, forKey: .isAlive) {
            self.asleep = !alive
        } else {
            self.asleep = false
        }
        clamp()
    }

    public func encode(to encoder: Encoder) throws {
        var box = encoder.container(keyedBy: CodingKeys.self)
        try box.encode(name, forKey: .name)
        try box.encode(hunger, forKey: .hunger)
        try box.encode(energy, forKey: .energy)
        try box.encode(happiness, forKey: .happiness)
        try box.encode(asleep, forKey: .asleep)
        try box.encode(true, forKey: .isAlive)
        try box.encode(lastDecayTs, forKey: .lastDecayTs)
        try box.encode(lastAction, forKey: .lastAction)
        try box.encode(lastActionTs, forKey: .lastActionTs)
        try box.encode(fedTaskIDs, forKey: .fedTaskIDs)
    }

    public mutating func clamp() {
        hunger = min(PetRules.statMax, max(PetRules.statMin, hunger))
        energy = min(PetRules.statMax, max(PetRules.statMin, energy))
        happiness = min(PetRules.statMax, max(PetRules.statMin, happiness))
        if name.isEmpty { name = PetRules.defaultName }
        if fedTaskIDs.count > AppConfig.fedTaskLimit {
            fedTaskIDs = Array(fedTaskIDs.suffix(AppConfig.fedTaskLimit))
        }
    }
}

public enum PetEngine {
    public static func tick(_ state: inout PetState, now: Double) {
        guard !state.asleep else { return }
        while !state.asleep && now - state.lastDecayTs >= PetRules.decayInterval {
            decay(&state)
            state.lastDecayTs += PetRules.decayInterval
        }
    }

    public static func play(_ state: inout PetState, now: Double) {
        tick(&state, now: now)
        guard !state.asleep else { return }
        apply(&state, hunger: PetRules.playHunger, energy: PetRules.playEnergy, happiness: PetRules.playHappiness)
        state.lastAction = "play"
        state.lastActionTs = now
    }

    @discardableResult
    public static func feedFromAI(_ state: inout PetState, taskID: String, now: Double) -> Bool {
        let id = taskID.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !id.isEmpty, !state.fedTaskIDs.contains(id) else { return false }
        tick(&state, now: now)
        let wasAsleep = state.asleep
        if wasAsleep {
            state.asleep = false
        }
        apply(&state, hunger: PetRules.feedHunger, energy: PetRules.feedEnergy, happiness: PetRules.feedHappiness)
        if wasAsleep {
            state.asleep = false
        }
        state.lastAction = "feed"
        state.lastActionTs = now
        state.fedTaskIDs.append(id)
        state.clamp()
        return true
    }

    public static func mood(_ state: PetState, now: Double) -> String {
        if state.asleep { return "sleepy" }
        let held = now - state.lastActionTs < PetRules.moodActionHold
        if state.lastAction == "play" && held && state.hunger >= PetRules.angryPlayHunger {
            return "angry"
        }
        if state.energy <= PetRules.moodSleepy { return "sleepy" }
        if state.hunger >= PetRules.moodHungry { return "hungry" }
        if state.lastAction == "play" && held { return "playful" }
        if state.happiness <= PetRules.moodAngry { return "angry" }
        return "happy"
    }

    public static func message(_ state: PetState, now: Double) -> String {
        if state.lastAction == "feed" && now - state.lastActionTs < PetRules.moodActionHold {
            return PetRules.feedMessage
        }
        switch mood(state, now: now) {
        case "playful":
            return "Zoom zoom! Catch me!"
        case "sleepy":
            return "Zzz... five more minutes."
        case "hungry":
            return "Feed me now, human."
        case "angry":
            return "Don't poke me when I'm hungry!"
        default:
            return "Let's take care of Mochi!"
        }
    }

    /// Short line for the desktop kitten. Asleep is its own line; mood() still says sleepy.
    public static func hoverLine(_ state: PetState, now: Double) -> String {
        if state.lastAction == "feed", now - state.lastActionTs < PetRules.moodActionHold {
            return "吃到啦。"
        }
        if state.asleep { return "在睡觉。" }
        switch mood(state, now: now) {
        case "playful":
            return "想玩一会儿。"
        case "sleepy":
            return "有点困了。"
        case "hungry":
            return "肚子有点空。"
        case "angry":
            return "想自己待一会儿。"
        case "asleep":
            return "在睡觉。"
        default:
            return "心情正好。"
        }
    }

    public static func snapshot(_ state: PetState, now: Double) -> [String: Any] {
        [
            "name": state.name,
            "hunger": state.hunger,
            "energy": state.energy,
            "happiness": state.happiness,
            "is_alive": true,
            "asleep": state.asleep,
            "mood": mood(state, now: now),
            "message": message(state, now: now),
            "hover": hoverLine(state, now: now)
        ]
    }

    private static func decay(_ state: inout PetState) {
        guard !state.asleep else { return }
        apply(&state, hunger: PetRules.decayHunger, energy: PetRules.decayEnergy, happiness: PetRules.decayHappiness)
    }

    private static func apply(_ state: inout PetState, hunger: Int, energy: Int, happiness: Int) {
        guard !state.asleep else { return }
        state.hunger += hunger
        state.energy += energy
        state.happiness += happiness
        state.clamp()
        if state.hunger >= PetRules.asleepHunger || state.energy <= PetRules.asleepEnergy {
            state.asleep = true
        }
    }
}
