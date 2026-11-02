import Foundation

/// The click timbres from `tone()` in the old page, rendered as PCM so the app can play them natively.
public enum ToneSynth {
    public enum Oscillator: String {
        case sine
        case triangle
    }

    public struct Partial {
        public var oscillator: Oscillator
        public var frequency: Double
        public var start: Double
        public var duration: Double
        public var gain: Double
        public var slideTo: Double?

        public init(
            _ oscillator: Oscillator,
            frequency: Double,
            start: Double,
            duration: Double,
            gain: Double,
            slideTo: Double? = nil
        ) {
            self.oscillator = oscillator
            self.frequency = frequency
            self.start = start
            self.duration = duration
            self.gain = gain
            self.slideTo = slideTo
        }
    }

    public static let sampleRate = 44_100.0

    public static func recipe(_ name: String) -> [Partial] {
        switch name {
        case "chirp":
            return [
                Partial(.triangle, frequency: 988, start: 0, duration: 0.07, gain: 0.16),
                Partial(.triangle, frequency: 1319, start: 0.07, duration: 0.09, gain: 0.14)
            ]
        case "mew":
            return [
                Partial(.sine, frequency: 620, start: 0, duration: 0.18, gain: 0.15, slideTo: 980),
                Partial(.sine, frequency: 980, start: 0.12, duration: 0.16, gain: 0.1, slideTo: 540)
            ]
        case "pop":
            return [Partial(.sine, frequency: 420, start: 0, duration: 0.09, gain: 0.14, slideTo: 180)]
        case "twinkle":
            return [
                Partial(.sine, frequency: 1319, start: 0, duration: 0.16, gain: 0.12),
                Partial(.sine, frequency: 1976, start: 0.04, duration: 0.18, gain: 0.08)
            ]
        case "glass":
            // Light rising acknowledgment. Quiet glass, well under a second.
            return [
                Partial(.sine, frequency: 784, start: 0, duration: 0.14, gain: 0.09, slideTo: 1175),
                Partial(.sine, frequency: 1568, start: 0.012, duration: 0.12, gain: 0.035, slideTo: 2349)
            ]
        case "ripple":
            return [
                Partial(.sine, frequency: 988, start: 0, duration: 0.06, gain: 0.1),
                Partial(.sine, frequency: 1319, start: 0.05, duration: 0.08, gain: 0.09),
                Partial(.sine, frequency: 1760, start: 0.1, duration: 0.12, gain: 0.06)
            ]
        case "ping":
            return [Partial(.sine, frequency: 1480, start: 0, duration: 0.1, gain: 0.13, slideTo: 1760)]
        case "chime":
            // Warmer two-note completion, with a soft low partial under the second note.
            return [
                Partial(.sine, frequency: 523, start: 0, duration: 0.15, gain: 0.10),
                Partial(.sine, frequency: 659, start: 0.10, duration: 0.18, gain: 0.09),
                Partial(.sine, frequency: 262, start: 0.10, duration: 0.16, gain: 0.035)
            ]
        case "bloom":
            return [
                Partial(.sine, frequency: 784, start: 0, duration: 0.18, gain: 0.1),
                Partial(.sine, frequency: 1175, start: 0.04, duration: 0.2, gain: 0.07)
            ]
        case "spark":
            return [
                Partial(.triangle, frequency: 1976, start: 0, duration: 0.05, gain: 0.09),
                Partial(.sine, frequency: 1568, start: 0.04, duration: 0.12, gain: 0.1)
            ]
        case "drop":
            // Softer falling tone. Quieter than the click sounds, and not an alarm.
            return [
                Partial(.sine, frequency: 698, start: 0, duration: 0.24, gain: 0.08, slideTo: 440),
                Partial(.sine, frequency: 349, start: 0.02, duration: 0.22, gain: 0.03, slideTo: 220)
            ]
        case "thud":
            return [Partial(.triangle, frequency: 240, start: 0, duration: 0.1, gain: 0.15, slideTo: 140)]
        case "hush":
            return [Partial(.sine, frequency: 880, start: 0, duration: 0.2, gain: 0.09, slideTo: 440)]
        default:
            return [Partial(.sine, frequency: 880, start: 0, duration: 0.12, gain: 0.16, slideTo: 420)]
        }
    }

    public static func render(_ name: String, sampleRate: Double = ToneSynth.sampleRate) -> [Float] {
        let notes = recipe(name)
        let end = notes.map { $0.start + $0.duration + 0.02 }.max() ?? 0.12
        let count = max(1, Int(end * sampleRate))
        var buffer = [Float](repeating: 0, count: count)
        for note in notes {
            mix(note, into: &buffer, sampleRate: sampleRate)
        }
        return buffer
    }

    public static func peak(_ name: String) -> Float {
        render(name).map { abs($0) }.max() ?? 0
    }

    private static func mix(_ note: Partial, into buffer: inout [Float], sampleRate: Double) {
        var phase = 0.0
        let attack = 0.012
        for index in buffer.indices {
            let time = Double(index) / sampleRate
            let local = time - note.start
            if local < 0 || local > note.duration + 0.02 { continue }
            let frequency: Double
            if let slide = note.slideTo, note.duration > 0, note.frequency > 0, slide > 0 {
                let progress = min(1, max(0, local / note.duration))
                frequency = note.frequency * pow(max(40, slide) / note.frequency, progress)
            } else {
                frequency = note.frequency
            }
            let envelope: Double
            if local < attack {
                envelope = 0.0001 * pow(note.gain / 0.0001, local / attack)
            } else if local < note.duration {
                let span = max(0.001, note.duration - attack)
                let progress = min(1, (local - attack) / span)
                envelope = note.gain * pow(0.0001 / max(note.gain, 0.0001), progress)
            } else {
                envelope = 0.0001
            }
            phase += 2 * Double.pi * frequency / sampleRate
            let wave: Double
            switch note.oscillator {
            case .sine:
                wave = sin(phase)
            case .triangle:
                let wrapped = phase / (2 * Double.pi)
                let fraction = wrapped - floor(wrapped)
                wave = fraction < 0.5 ? (4 * fraction - 1) : (3 - 4 * fraction)
            }
            let mixed = buffer[index] + Float(wave * envelope)
            buffer[index] = min(1, max(-1, mixed))
        }
    }
}
