import AVFoundation
import SwiftUI

/// «Исполнение» over the view shown (v0.10, Andrei's finding 24): the recording picked in the switch, heard from a capsule
/// under the bar of the notes, the PDF or a view shown over the piece. It is no view of its own: the view under it stays
/// as it is, and it goes on as the screen switches between the PDF and the notes of the same file
/// (`SourcesController.handOver`). ✕ puts it away. Placed by `ScoreScreen` and `PdfScreen`, under their bar, for their own
/// piece or the one under them (`heardOver`).
struct PerformanceCapsule: View {
    @ObservedObject var sources: SourcesController
    let text: UIText

    var body: some View {
        if let id = sources.performance, let projectId = sources.projectId, let url = OriginalSounds.file(project: projectId, original: id) {
            RecordingViewCapsule(url: url, title: sources.layers.original(id)?.shown ?? text["sources.rolePlaying"], text: text) {
                sources.putAway()
            }
            .id(id)
            .transition(.opacity)
        }
    }
}

extension PerformanceCapsule {
    /// Whose recording a screen's bar shows: the piece's under a screen shown over it (a view picked in the switch, the PDF
    /// linked in Files: `heardOver`), else the screen's own while its switch shows; nil for none.
    static func sources(over: SourcesController?, own: SourcesController, switchOn: Bool) -> SourcesController? {
        over ?? (switchOn ? own : nil)
    }
}

private struct HeardOverKey: EnvironmentKey {
    static var defaultValue: SourcesController? { nil }
}

extension EnvironmentValues {
    /// The Sources of the piece under a screen shown over it, whose recording that screen's bar shows.
    var heardOver: SourcesController? {
        get { self[HeardOverKey.self] }
        set { self[HeardOverKey.self] = newValue }
    }
}

extension View {
    /// A screen shown over the piece (a view picked in the switch, the PDF linked in Files) hears the piece's recording in
    /// a capsule under its own bar, above its mode's options (v0.10 s-2 review).
    func heardOver(_ sources: SourcesController) -> some View {
        environment(\.heardOver, sources)
    }
}

/// The «Исполнение» capsule: the recording's sound kept on this iPad, heard as it is, from a capsule under the bar while the
/// view under it stays as it is. Its slider moves through the recording, the time beside it (Andrei, v0.8 finding 20). ✕
/// puts it away. Its sound is the app's one `RecordingListening.shared`, so the capsule of the next screen hears the same.
struct RecordingViewCapsule: View {
    let url: URL
    let title: String
    let text: UIText
    let close: () -> Void
    @ObservedObject private var listening = RecordingListening.shared

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button {
                listening.toggle(url)
            } label: {
                Icon(name: listening.playing ? "pause" : "play", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: listening.playing))
            .accessibilityLabel(listening.playing ? text["sources.sheet.pause"] : text.format("sources.sheet.listen", title))
            Icon(name: "mic", size: CGFloat.ds("icon-s")).foregroundStyle(Color.ds("ink-2"))
            Text(title).dsType(.label).foregroundStyle(Color.ds("ink")).lineLimit(1)
            Slider(
                value: Binding(get: { Double(listening.elapsed) }, set: { listening.seek(to: Int($0.rounded())) }),
                in: 0...Double(max(1, listening.length))
            ) { editing in
                if editing { listening.hold() } else { listening.release() }
            }
            .tint(Color.ds("accent"))
            // Five fingertips wide: room to aim at a minute of a long recording.
            .frame(width: CGFloat.ds("h-touch") * 5)
            .disabled(listening.length == 0)
            .accessibilityLabel(text["sources.sheet.seek"])
            .accessibilityValue(text.format("sources.sheet.seekValue", SourceLayers.duration(listening.elapsed), SourceLayers.duration(listening.length)))
            Text(SourceLayers.duration(listening.elapsed) + " / " + SourceLayers.duration(listening.length))
                .dsType(.caption)
                .monospacedDigit()
                .foregroundStyle(Color.ds("ink-2"))
            Button {
                // Put away: silent, whatever was handed over before.
                listening.cancelHandOver()
                listening.stop()
                close()
            } label: {
                Icon(name: "close", size: CGFloat.ds("icon-s")).frame(width: 28, height: 28)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["bar.close"])
        }
        .controlShape(.capsule)
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
        .onAppear { listening.shown(url) }
        .onDisappear { listening.hidden() }
    }
}

/// The sound of a recording kept on this iPad, heard as it is (AVAudioPlayer).
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
    /// How many capsules show it now: one on the piece's screen, another on a view shown over it.
    private var shownIn = 0
    /// The screen handed the piece to its other view: the capsule going with the old screen leaves the sound on.
    private var handedOver = false

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
        load(url)
        guard let player else { return }
        if player.isPlaying {
            player.pause()
            playing = false
            timer?.invalidate()
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

    func stop() {
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
