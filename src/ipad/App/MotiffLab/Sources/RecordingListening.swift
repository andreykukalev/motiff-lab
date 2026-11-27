import AVFoundation
import Combine

/// The sound of a recording kept on this iPad, heard as it is (AVAudioPlayer). A call taking the audio, the app gone to
/// the background or the screen locked, and the headphones gone pause it, as they pause the notes (v1.5 a-2, answer 30).
@MainActor
final class RecordingListening: ObservableObject {
    /// The one the «Исполнение» capsules hear (v0.10, finding 24): one recording sounds at a time, and it goes on from one
    /// screen of the piece to the next and under a view shown over the piece.
    static let shared = RecordingListening()

    @Published private(set) var playing = false
    /// Where it is and how long it is, in milliseconds.
    @Published private(set) var elapsed = 0
    @Published private(set) var length = 0
    /// The player's finger is on the slider: the place shown is the finger's, and the sound moves there on release.
    private(set) var holding = false
    /// Where the sound is now, in milliseconds.
    var heardAt: Int { atEnd ? length : Int(((player?.currentTime ?? 0) * 1000).rounded()) }
    private var player: AVAudioPlayer?
    /// The recording loaded.
    private var url: URL?
    /// Moved to the end: AVAudioPlayer keeps no `currentTime` equal to its duration (it reads back 0), so the sound is
    /// stopped there and the next play starts from the beginning.
    private var atEnd = false
    private var timer: Timer?
    /// Where a stretch heard once pauses (`playStretch`).
    private var stretchEnd: Timer?
    /// How many capsules show it now: one on the piece's screen, another on a view shown over it.
    private var shownIn = 0
    /// The screen handed the piece to its other view: the capsule going with the old screen leaves the sound on.
    private var handedOver = false
    /// Hears what the system does to the audio (EngineKeeper, with no engine): its pauses pause the recording.
    private var keeper: EngineKeeper?

    init() {
        keeper = EngineKeeper(nil) { [weak self] in self?.pauseForSystem() }
    }

    /// The recording at `url`, loaded once; another recording stops the one before.
    func load(_ url: URL) {
        guard player == nil || self.url != url else { return }
        if player != nil {
            stop()
            elapsed = 0
            atEnd = false
        }
        self.url = url
        player = try? AVAudioPlayer(contentsOf: url)
        player?.prepareToPlay()
        length = Int(((player?.duration ?? 0) * 1000).rounded())
    }

    /// A capsule shows the recording (its `onAppear`): the same sound goes on.
    func shown(_ url: URL) {
        load(url)
        shownIn += 1
        handedOver = false
    }

    /// A capsule goes (its `onDisappear`): once none shows the recording, the sound stops, unless the screen handed the
    /// piece over to its other view (`handOver`), whose capsule hears it on.
    func hidden() {
        shownIn = max(0, shownIn - 1)
        if !handedOver { end() }
        handedOver = false
    }

    /// The screen hands the piece over to its other view: the sound goes on until the next screen's capsule shows it, or
    /// stops there when that screen does not show the recording (`SourcesController.load`).
    func handOver() {
        handedOver = true
    }

    /// The stretch `from` to `to` milliseconds of the recording at `url`, heard once (a draft's приём «Как на записи», v1.1
    /// step 6): paused at its end, and the file let go there when no capsule shows it; a press of a capsule's play before
    /// then lets it play on.
    func playStretch(_ url: URL, from: Int, to: Int) {
        load(url)
        seek(to: from)
        if !playing { toggle(url) }
        stretchEnd?.invalidate()
        stretchEnd = Timer.scheduledTimer(withTimeInterval: Double(max(0, to - from)) / 1000, repeats: false) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                if self.shownIn == 0 {
                    self.end()
                } else if let url = self.url, self.playing {
                    self.toggle(url)
                }
            }
        }
    }

    /// The hand-over came to nothing: a screen went without handing the piece over, or the switch never ran. Once no
    /// capsule shows the recording, the sound stops (v0.10 s-2 review: never heard on in the library).
    func cancelHandOver() {
        handedOver = false
        end()
    }

    /// Once no capsule shows the recording, the sound stops and the player lets its file go; the next capsule loads it
    /// again.
    func end() {
        guard shownIn == 0 else { return }
        stop()
        player = nil
        url = nil
        elapsed = 0
        length = 0
        atEnd = false
    }

    func toggle(_ url: URL) {
        stretchEnd?.invalidate()
        stretchEnd = nil
        load(url)
        guard let player else { return }
        if player.isPlaying {
            pause()
        } else {
            // Heard with the ring switch on silent too, as the score's own playback is (Player).
            try? AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
            try? AVAudioSession.sharedInstance().setActive(true)
            atEnd = false
            player.play()
            playing = true
            timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
                Task { @MainActor in self?.tick() }
            }
        }
    }

    /// Pauses the sound, the time shown where it was heard.
    private func pause() {
        player?.pause()
        playing = false
        timer?.invalidate()
        timer = nil
        if !holding { elapsed = heardAt }
    }

    /// The system paused the audio (EngineKeeper): the recording waits where it was heard, and plays on only from a
    /// capsule's play. The system already paused an AVAudioPlayer that a call interrupted, so this never toggles; a stretch
    /// heard once still lets its file go at its end (`playStretch`).
    private func pauseForSystem() {
        guard playing else { return }
        pause()
    }

    func stop() {
        stretchEnd?.invalidate()
        stretchEnd = nil
        player?.stop()
        playing = false
        timer?.invalidate()
        timer = nil
    }

    /// Moves the sound to a place, in milliseconds, kept within the recording; playing, it goes on from there, and at the
    /// end it stops. While the finger holds the slider only the place shown moves, so the sound does not stutter; it
    /// follows on `release`.
    func seek(to milliseconds: Int) {
        guard let player else { return }
        elapsed = min(max(0, milliseconds), length)
        guard !holding else { return }
        atEnd = elapsed >= length
        if atEnd { stop() }
        player.currentTime = atEnd ? 0 : Double(elapsed) / 1000
    }

    /// The finger is on the slider.
    func hold() {
        holding = true
    }

    /// The finger leaves the slider: the sound moves to the place shown.
    func release() {
        holding = false
        seek(to: elapsed)
    }

    private func tick() {
        guard let player, !holding else { return }
        elapsed = heardAt
        if !player.isPlaying {
            playing = false
            timer?.invalidate()
        }
    }
}
