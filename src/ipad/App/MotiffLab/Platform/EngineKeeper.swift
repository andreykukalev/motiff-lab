import AVFoundation

/// Keeps one audio engine running through what the system does to the iPad's audio (core and iPad review v0.6.5,
/// findings 1 and 2): a call, Siri, an alarm or another app's sound interrupts the session and stops the engine; AirPods,
/// a cable or dictation's microphone change the engine's configuration, which stops it too; a reset of the media services
/// stops everything. The system never starts the engine again by itself, so without this Play stays silent until the
/// piece opens again. An interruption that begins `paused` the owner (nothing goes on sounding in its head); when it ends,
/// and after a configuration change or a reset, the engine starts again as it was, without playing.
@MainActor
final class EngineKeeper {
    /// Posted by the app when it gives the sound back to playback (dictation's microphone let go): every engine kept
    /// starts again if it stopped.
    static let soundReturned = Notification.Name("motiff.soundReturned")

    private let engine: AVAudioEngine
    private let paused: @MainActor () -> Void
    /// Whether the owner wants its engine running now (the metronome only while it clicks).
    private let wanted: @MainActor () -> Bool
    private var observers: [NSObjectProtocol] = []

    init(_ engine: AVAudioEngine, wanted: @escaping @MainActor () -> Bool = { true }, paused: @escaping @MainActor () -> Void) {
        self.engine = engine
        self.wanted = wanted
        self.paused = paused
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.restart() }
        })
        observers.append(center.addObserver(forName: AVAudioSession.interruptionNotification, object: nil, queue: .main) { [weak self] note in
            let began = EngineKeeper.began(note.userInfo)
            MainActor.assumeIsolated {
                if began { self?.paused() } else { self?.restart() }
            }
        })
        for name in [AVAudioSession.mediaServicesWereResetNotification, Self.soundReturned] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.restart() }
            })
        }
    }

    deinit {
        for observer in observers { NotificationCenter.default.removeObserver(observer) }
    }

    /// Whether an interruption's notification says it began (else it ended).
    nonisolated static func began(_ info: [AnyHashable: Any]?) -> Bool {
        guard let raw = info?[AVAudioSessionInterruptionTypeKey] as? UInt else { return false }
        return AVAudioSession.InterruptionType(rawValue: raw) == .began
    }

    /// Whether the engine runs now: after `restart`, false while a call holds the audio or the owner does not want it.
    var running: Bool { engine.isRunning }

    /// The engine started again, when it stopped and its owner wants it; the session's category stays as it is (dictation
    /// may hold the microphone), only made active again.
    func restart() {
        guard wanted(), !engine.isRunning else { return }
        try? AVAudioSession.sharedInstance().setActive(true)
        engine.prepare()
        try? engine.start()
    }
}
