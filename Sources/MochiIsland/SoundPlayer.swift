import AVFoundation
import CoreAudio
import Foundation
import MochiCore

/// Native stand-in for the page's `tone()` clicks. Alert and done share a one-second gate.
@MainActor
final class SoundPlayer {
    static let shared = SoundPlayer()

    enum Event {
        case open, close, select, choice, alert, done
    }

    private let engine = AVAudioEngine()
    private let player = AVAudioPlayerNode()
    private let format: AVAudioFormat?
    private var started = false
    private var gate = AlertSoundGate()

    private init() {
        format = AVAudioFormat(standardFormatWithSampleRate: ToneSynth.sampleRate, channels: 1)
        if let format {
            engine.attach(player)
            engine.connect(player, to: engine.mainMixerNode, format: format)
            engine.mainMixerNode.outputVolume = 1
        }
    }

    func play(_ event: Event, timbre: String) {
        if event == .alert || event == .done {
            guard gate.shouldPlay(now: Date().timeIntervalSince1970) else { return }
        }
        playPreview(timbre)
    }

    /// One task cue per second. A muted Mac stays quiet.
    func playCue(_ name: String) {
        guard gate.shouldPlay(now: Date().timeIntervalSince1970) else { return }
        playPreview(name)
    }

    func playPreview(_ timbre: String) {
        guard !Self.outputMuted() else { return }
        let name = Self.resolved(timbre)
        guard format != nil, let buffer = buffer(ToneSynth.render(name)) else { return }
        do {
            if !started {
                try engine.start()
                started = true
            }
            player.scheduleBuffer(buffer, at: nil, options: .interrupts, completionHandler: nil)
            if !player.isPlaying {
                player.play()
            }
        } catch {
            started = false
        }
    }

    private func buffer(_ samples: [Float]) -> AVAudioPCMBuffer? {
        guard let format, let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(samples.count)) else {
            return nil
        }
        buffer.frameLength = AVAudioFrameCount(samples.count)
        guard let channel = buffer.floatChannelData?[0] else { return nil }
        for (index, sample) in samples.enumerated() {
            channel[index] = sample
        }
        return buffer
    }

    private static func resolved(_ name: String) -> String {
        if IslandPrefs.soundIDs.contains(name) { return name }
        if IslandPrefs.submitSounds.contains(name) { return name }
        if IslandPrefs.doneSounds.contains(name) { return name }
        if IslandPrefs.stuckSounds.contains(name) { return name }
        return "boop"
    }

    static func outputMuted() -> Bool {
        var device = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        let system = AudioObjectID(kAudioObjectSystemObject)
        guard AudioObjectGetPropertyData(system, &address, 0, nil, &size, &device) == noErr, device != 0 else {
            return false
        }
        var muted: UInt32 = 0
        size = UInt32(MemoryLayout<UInt32>.size)
        address.mSelector = kAudioDevicePropertyMute
        address.mScope = kAudioDevicePropertyScopeOutput
        address.mElement = kAudioObjectPropertyElementMain
        if AudioObjectHasProperty(device, &address),
           AudioObjectGetPropertyData(device, &address, 0, nil, &size, &muted) == noErr,
           muted != 0 {
            return true
        }
        var volume: Float32 = 1
        size = UInt32(MemoryLayout<Float32>.size)
        address.mSelector = kAudioDevicePropertyVolumeScalar
        if AudioObjectHasProperty(device, &address),
           AudioObjectGetPropertyData(device, &address, 0, nil, &size, &volume) == noErr,
           volume <= 0.001 {
            return true
        }
        return false
    }
}
