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
/// puts it away; a capsule with no `close` has none (v1.1: the player of a piece that is only its recording,
/// `RecordingScreen`). Its sound is the app's one `RecordingListening.shared`, so the capsule of the next screen hears the
/// same.
struct RecordingViewCapsule: View {
    let url: URL
    let title: String
    let text: UIText
    var close: (() -> Void)?
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
            if let close {
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
        }
        .controlShape(.capsule)
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
        .onAppear { listening.shown(url) }
        .onDisappear { listening.hidden() }
    }
}
