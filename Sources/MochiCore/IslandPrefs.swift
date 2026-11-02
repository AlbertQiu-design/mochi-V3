import Foundation

public struct IslandPrefs: Codable, Equatable {
    public var dx: Double
    public var dy: Double
    public var bubbleDx: Double
    public var bubbleDy: Double
    public var fill: String
    public var text: String
    public var stroke: String
    public var cardFill: String
    public var cardStroke: String
    public var sound: String
    /// Cue played when a task is submitted.
    public var soundSubmit: String
    /// Cue played when a task finishes.
    public var soundDone: String
    /// Cue played when a task is waiting or fails.
    public var soundStuck: String
    public var look: String
    public var petScale: Double
    public var bubbleScale: Double
    public var cursorPrivacyAccepted: Bool
    public var scanTranscripts: Bool
    public var dock: String
    public var kittenX: Double
    public var kittenY: Double
    public var kittenPlaced: Bool
    public var pickerCursor: Bool
    public var pickerCodex: Bool
    public var pickerClaude: Bool
    public var pickerGemini: Bool
    public var pickerGrok: Bool
    public var pickerGPT: Bool

    public static let soundIDs = ["boop", "chirp", "mew", "pop", "twinkle"]
    /// Apple-like cues for a submitted task. Glass is the default.
    public static let submitSounds = ["glass", "ripple", "ping"]
    /// Apple-like cues for a finished task. Chime is the default.
    public static let doneSounds = ["chime", "bloom", "spark"]
    /// Apple-like cues for a waiting or failed task. Drop is the default.
    public static let stuckSounds = ["drop", "thud", "hush"]
    public static let docks = ["left", "right"]
    public static let pickerOrder = ["cursor", "codex", "claude", "gemini", "grok-bot", "gpt"]
    /// Placeholder pet looks shown as A…F in Settings → Pet until real art ships.
    public static let petLooks = ["a", "b", "c", "d", "e", "f"]

    /// Maps stored `look` (including legacy `"mochi"`) onto a selectable pet letter id.
    public static func normalizeLook(_ raw: String) -> String {
        let lower = raw.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if petLooks.contains(lower) { return lower }
        if lower == "mochi" { return "a" }
        return "a"
    }

    public static func petLetter(for look: String) -> String {
        normalizeLook(look).uppercased()
    }

    public var pickerSources: [String] {
        var sources: [String] = []
        if pickerCursor { sources.append("cursor") }
        if pickerCodex { sources.append("codex") }
        if pickerClaude { sources.append("claude") }
        if pickerGemini { sources.append("gemini") }
        if pickerGrok { sources.append("grok-bot") }
        if pickerGPT { sources.append("gpt") }
        return sources
    }

    public init(
        dx: Double = 0,
        dy: Double = 0,
        bubbleDx: Double = 0,
        bubbleDy: Double = 0,
        fill: String = "#ffffff",
        text: String = "#241f1c",
        stroke: String = "#2c2926",
        cardFill: String = "#fffdf8",
        cardStroke: String = "#2c2926",
        sound: String = "boop",
        soundSubmit: String = "glass",
        soundDone: String = "chime",
        soundStuck: String = "drop",
        look: String = "a",
        petScale: Double = 1,
        bubbleScale: Double = 1,
        cursorPrivacyAccepted: Bool = false,
        scanTranscripts: Bool = false,
        dock: String = "right",
        kittenX: Double = 1,
        kittenY: Double = 0.5,
        kittenPlaced: Bool = false,
        pickerCursor: Bool = true,
        pickerCodex: Bool = true,
        pickerClaude: Bool = true,
        pickerGemini: Bool = true,
        pickerGrok: Bool = true,
        pickerGPT: Bool = true
    ) {
        self.dx = dx
        self.dy = dy
        self.bubbleDx = bubbleDx
        self.bubbleDy = bubbleDy
        self.fill = fill
        self.text = text
        self.stroke = stroke
        self.cardFill = cardFill
        self.cardStroke = cardStroke
        self.sound = Self.soundIDs.contains(sound) ? sound : "boop"
        self.soundSubmit = Self.choice(soundSubmit, allowed: Self.submitSounds, fallback: "glass")
        self.soundDone = Self.choice(soundDone, allowed: Self.doneSounds, fallback: "chime")
        self.soundStuck = Self.choice(soundStuck, allowed: Self.stuckSounds, fallback: "drop")
        self.look = Self.normalizeLook(look)
        self.petScale = petScale
        self.bubbleScale = bubbleScale
        self.cursorPrivacyAccepted = cursorPrivacyAccepted
        self.scanTranscripts = scanTranscripts
        self.dock = Self.docks.contains(dock) ? dock : "right"
        self.kittenX = Self.unit(kittenX)
        self.kittenY = Self.unit(kittenY)
        self.kittenPlaced = kittenPlaced
        self.pickerCursor = pickerCursor
        self.pickerCodex = pickerCodex
        self.pickerClaude = pickerClaude
        self.pickerGemini = pickerGemini
        self.pickerGrok = pickerGrok
        self.pickerGPT = pickerGPT
        if kittenPlaced {
            self.dock = self.kittenX < 0.5 ? "left" : "right"
        }
    }

    public var dictionary: [String: Any] {
        [
            "dx": dx,
            "dy": dy,
            "bubble_dx": bubbleDx,
            "bubble_dy": bubbleDy,
            "fill": fill,
            "text": text,
            "stroke": stroke,
            "card_fill": cardFill,
            "card_stroke": cardStroke,
            "sound": sound,
            "sound_submit": soundSubmit,
            "sound_done": soundDone,
            "sound_stuck": soundStuck,
            "look": look,
            "pet_scale": petScale,
            "bubble_scale": bubbleScale,
            "dock": dock,
            "kitten_x": kittenX,
            "kitten_y": kittenY,
            "kitten_placed": kittenPlaced,
            "picker_cursor": pickerCursor,
            "picker_codex": pickerCodex,
            "picker_claude": pickerClaude,
            "picker_gemini": pickerGemini,
            "picker_grok": pickerGrok,
            "picker_gpt": pickerGPT
        ]
    }

    enum CodingKeys: String, CodingKey {
        case dx, dy, bubbleDx, bubbleDy, fill, text, stroke, cardFill, cardStroke
        case sound, look, petScale, bubbleScale, cursorPrivacyAccepted, scanTranscripts, dock
        case soundSubmit = "sound_submit"
        case soundDone = "sound_done"
        case soundStuck = "sound_stuck"
        case kittenX = "kitten_x"
        case kittenY = "kitten_y"
        case kittenPlaced = "kitten_placed"
        case pickerCursor = "picker_cursor"
        case pickerCodex = "picker_codex"
        case pickerClaude = "picker_claude"
        case pickerGemini = "picker_gemini"
        case pickerGrok = "picker_grok"
        case pickerGPT = "picker_gpt"
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let parsed = IslandPrefs(
            dx: try container.decodeIfPresent(Double.self, forKey: .dx) ?? 0,
            dy: try container.decodeIfPresent(Double.self, forKey: .dy) ?? 0,
            bubbleDx: try container.decodeIfPresent(Double.self, forKey: .bubbleDx) ?? 0,
            bubbleDy: try container.decodeIfPresent(Double.self, forKey: .bubbleDy) ?? 0,
            fill: try container.decodeIfPresent(String.self, forKey: .fill) ?? "#ffffff",
            text: try container.decodeIfPresent(String.self, forKey: .text) ?? "#241f1c",
            stroke: try container.decodeIfPresent(String.self, forKey: .stroke) ?? "#2c2926",
            cardFill: try container.decodeIfPresent(String.self, forKey: .cardFill) ?? "#fffdf8",
            cardStroke: try container.decodeIfPresent(String.self, forKey: .cardStroke) ?? "#2c2926",
            sound: try container.decodeIfPresent(String.self, forKey: .sound) ?? "boop",
            soundSubmit: try container.decodeIfPresent(String.self, forKey: .soundSubmit) ?? "glass",
            soundDone: try container.decodeIfPresent(String.self, forKey: .soundDone) ?? "chime",
            soundStuck: try container.decodeIfPresent(String.self, forKey: .soundStuck) ?? "drop",
            look: try container.decodeIfPresent(String.self, forKey: .look) ?? "a",
            petScale: try container.decodeIfPresent(Double.self, forKey: .petScale) ?? 1,
            bubbleScale: try container.decodeIfPresent(Double.self, forKey: .bubbleScale) ?? 1,
            cursorPrivacyAccepted: try container.decodeIfPresent(Bool.self, forKey: .cursorPrivacyAccepted) ?? false,
            scanTranscripts: try container.decodeIfPresent(Bool.self, forKey: .scanTranscripts) ?? false,
            dock: try container.decodeIfPresent(String.self, forKey: .dock) ?? "right",
            kittenX: try container.decodeIfPresent(Double.self, forKey: .kittenX) ?? 1,
            kittenY: try container.decodeIfPresent(Double.self, forKey: .kittenY) ?? 0.5,
            kittenPlaced: try container.decodeIfPresent(Bool.self, forKey: .kittenPlaced) ?? false,
            pickerCursor: try container.decodeIfPresent(Bool.self, forKey: .pickerCursor) ?? true,
            pickerCodex: try container.decodeIfPresent(Bool.self, forKey: .pickerCodex) ?? true,
            pickerClaude: try container.decodeIfPresent(Bool.self, forKey: .pickerClaude) ?? true,
            pickerGemini: try container.decodeIfPresent(Bool.self, forKey: .pickerGemini) ?? true,
            pickerGrok: try container.decodeIfPresent(Bool.self, forKey: .pickerGrok) ?? true,
            pickerGPT: try container.decodeIfPresent(Bool.self, forKey: .pickerGPT) ?? true
        )
        self = parsed
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(dx, forKey: .dx)
        try container.encode(dy, forKey: .dy)
        try container.encode(bubbleDx, forKey: .bubbleDx)
        try container.encode(bubbleDy, forKey: .bubbleDy)
        try container.encode(fill, forKey: .fill)
        try container.encode(text, forKey: .text)
        try container.encode(stroke, forKey: .stroke)
        try container.encode(cardFill, forKey: .cardFill)
        try container.encode(cardStroke, forKey: .cardStroke)
        try container.encode(sound, forKey: .sound)
        try container.encode(soundSubmit, forKey: .soundSubmit)
        try container.encode(soundDone, forKey: .soundDone)
        try container.encode(soundStuck, forKey: .soundStuck)
        try container.encode(look, forKey: .look)
        try container.encode(petScale, forKey: .petScale)
        try container.encode(bubbleScale, forKey: .bubbleScale)
        try container.encode(cursorPrivacyAccepted, forKey: .cursorPrivacyAccepted)
        try container.encode(scanTranscripts, forKey: .scanTranscripts)
        try container.encode(dock, forKey: .dock)
        try container.encode(kittenX, forKey: .kittenX)
        try container.encode(kittenY, forKey: .kittenY)
        try container.encode(kittenPlaced, forKey: .kittenPlaced)
        try container.encode(pickerCursor, forKey: .pickerCursor)
        try container.encode(pickerCodex, forKey: .pickerCodex)
        try container.encode(pickerClaude, forKey: .pickerClaude)
        try container.encode(pickerGemini, forKey: .pickerGemini)
        try container.encode(pickerGrok, forKey: .pickerGrok)
        try container.encode(pickerGPT, forKey: .pickerGPT)
    }

    public static func parse(_ json: [String: Any], base: IslandPrefs = IslandPrefs()) -> IslandPrefs {
        var next = base
        next.dx = number(json["dx"], fallback: base.dx)
        next.dy = number(json["dy"], fallback: base.dy)
        next.bubbleDx = number(json["bubble_dx"], fallback: base.bubbleDx)
        next.bubbleDy = number(json["bubble_dy"], fallback: base.bubbleDy)
        next.fill = hex(json["fill"]) ?? base.fill
        next.text = hex(json["text"]) ?? base.text
        next.stroke = hex(json["stroke"]) ?? base.stroke
        next.cardFill = hex(json["card_fill"]) ?? base.cardFill
        next.cardStroke = hex(json["card_stroke"]) ?? base.cardStroke
        if let sound = json["sound"] as? String, soundIDs.contains(sound) {
            next.sound = sound
        }
        if let sound = json["sound_submit"] as? String, submitSounds.contains(sound) {
            next.soundSubmit = sound
        }
        if let sound = json["sound_done"] as? String, doneSounds.contains(sound) {
            next.soundDone = sound
        }
        if let sound = json["sound_stuck"] as? String, stuckSounds.contains(sound) {
            next.soundStuck = sound
        }
        if let look = json["look"] as? String,
           look.range(of: "^[a-z0-9_-]{1,32}$", options: .regularExpression) != nil {
            next.look = normalizeLook(look)
        }
        next.petScale = scale(json["pet_scale"], fallback: base.petScale)
        next.bubbleScale = scale(json["bubble_scale"], fallback: base.bubbleScale)
        if let accepted = json["cursor_privacy_accepted"] as? Bool {
            next.cursorPrivacyAccepted = accepted
        }
        if let scan = json["scan_transcripts"] as? Bool {
            next.scanTranscripts = scan
        }
        if let dock = json["dock"] as? String, docks.contains(dock) {
            next.dock = dock
        }
        if json["kitten_x"] != nil {
            next.kittenX = unit(number(json["kitten_x"], fallback: base.kittenX))
        }
        if json["kitten_y"] != nil {
            next.kittenY = unit(number(json["kitten_y"], fallback: base.kittenY))
        }
        if let placed = json["kitten_placed"] as? Bool {
            next.kittenPlaced = placed
        } else if let placed = json["kitten_placed"] as? NSNumber {
            next.kittenPlaced = placed.boolValue
        }
        if next.kittenPlaced {
            next.dock = next.kittenX < 0.5 ? "left" : "right"
        }
        if let value = json["picker_cursor"] { next.pickerCursor = flag(value, fallback: base.pickerCursor) }
        if let value = json["picker_codex"] { next.pickerCodex = flag(value, fallback: base.pickerCodex) }
        if let value = json["picker_claude"] { next.pickerClaude = flag(value, fallback: base.pickerClaude) }
        if let value = json["picker_gemini"] { next.pickerGemini = flag(value, fallback: base.pickerGemini) }
        if let value = json["picker_grok"] { next.pickerGrok = flag(value, fallback: base.pickerGrok) }
        if let value = json["picker_gpt"] { next.pickerGPT = flag(value, fallback: base.pickerGPT) }
        return next
    }

    private static func choice(_ raw: String, allowed: [String], fallback: String) -> String {
        allowed.contains(raw) ? raw : fallback
    }

    private static func unit(_ value: Double) -> Double {
        min(1, max(0, value))
    }

    private static func number(_ value: Any?, fallback: Double) -> Double {
        if let number = value as? NSNumber { return number.doubleValue }
        if let number = value as? Double { return number }
        if let number = value as? Int { return Double(number) }
        return fallback
    }

    private static func scale(_ value: Any?, fallback: Double) -> Double {
        let number = number(value, fallback: fallback)
        if number == 0 { return 1 }
        return min(2, max(0.5, number))
    }

    private static func flag(_ value: Any, fallback: Bool) -> Bool {
        if let value = value as? Bool { return value }
        if let value = value as? NSNumber { return value.boolValue }
        return fallback
    }

    private static func hex(_ value: Any?) -> String? {
        guard let raw = value as? String else { return nil }
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard trimmed.range(of: "^#[0-9a-f]{6}$", options: .regularExpression) != nil else { return nil }
        return trimmed
    }
}

public final class PrefsStore {
    public let fileURL: URL

    public init(directory: URL) {
        self.fileURL = directory.appendingPathComponent("prefs.json")
    }

    public static func live() -> PrefsStore {
        PrefsStore(directory: AppConfig.supportDirectory)
    }

    public func load(migrating legacyURLs: [URL] = []) -> IslandPrefs {
        if let prefs = read(fileURL) { return prefs }
        for legacy in legacyURLs {
            if let prefs = read(legacy) {
                save(prefs)
                return prefs
            }
        }
        let fresh = IslandPrefs()
        save(fresh)
        return fresh
    }

    public func save(_ prefs: IslandPrefs) {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(prefs) else { return }
        try? AtomicFile.write(data, to: fileURL)
    }

    private func read(_ url: URL) -> IslandPrefs? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        if let decoded = try? JSONDecoder().decode(IslandPrefs.self, from: data) {
            return decoded
        }
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        return IslandPrefs.parse(json)
    }
}
