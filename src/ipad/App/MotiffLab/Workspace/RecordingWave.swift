import SwiftUI

/// The waveform of a piece that is only its recording (v1.1, finding 29): how loud the sound is every 100 ms, read on this
/// iPad as hearing it reads it (`RecordingSound.levels`), drawn in columns across the screen.
struct RecordingWave: Equatable, Sendable {
    let levels: [Int]

    /// VoiceOver's step through the recording, in milliseconds.
    static let step = 5000

    /// The waveform of the sound at `url`, read off the main thread; nil when the iPad cannot read it.
    static func read(_ url: URL) async -> RecordingWave? {
        await Task.detached(priority: .userInitiated) { () -> RecordingWave? in
            guard let sound = RecordingSound.read(url), sound.milliseconds > 0 else { return nil }
            return RecordingWave(levels: sound.levels(from: 0, to: sound.milliseconds))
        }.value
    }

    /// The loudness in `count` columns from the start to the end: each its loudest level, as a share of the loudest of all,
    /// from 0 to 1.
    func columns(_ count: Int) -> [Double] {
        guard count > 0, let loudest = levels.max() else { return [] }
        return (0..<count).map { column in
            let first = column * levels.count / count
            let last = min(levels.count, max(first + 1, (column + 1) * levels.count / count))
            return Double(levels[first..<last].max() ?? 0) / Double(max(1, loudest))
        }
    }

    /// The moment under a share of the width, from 0 to 1, within a recording `length` milliseconds long.
    static func moment(at share: Double, length: Int) -> Int {
        Int((min(1, max(0, share)) * Double(length)).rounded())
    }

    /// Where a moment stands as a share of the width, from 0 to 1.
    static func share(of moment: Int, length: Int) -> Double {
        length > 0 ? min(1, max(0, Double(moment) / Double(length))) : 0
    }
}

/// The waveform across the screen with the play cursor where the sound is (`RecordingListening.shared`, the player under
/// it): a tap puts the sound there. For VoiceOver it is one adjustable element, which moves the sound by
/// `RecordingWave.step`, so no gesture is the only way.
struct RecordingWaveView: View {
    let wave: RecordingWave
    /// The recording's sound kept on this iPad.
    let url: URL
    let text: UIText
    @ObservedObject private var listening = RecordingListening.shared

    /// One column every this many points, drawn half as wide.
    private var pitch: CGFloat { CGFloat.ds("s-1") }

    var body: some View {
        GeometryReader { area in
            let at = RecordingWave.share(of: listening.elapsed, length: listening.length) * area.size.width
            ZStack(alignment: .topLeading) {
                Canvas { context, size in draw(in: &context, size: size, cursor: at) }
                Rectangle()
                    .fill(Color.ds("accent"))
                    .frame(width: pitch / 2, height: area.size.height)
                    .offset(x: at - pitch / 4)
            }
            .contentShape(Rectangle())
            .onTapGesture { point in
                move(to: RecordingWave.moment(at: point.x / max(1, area.size.width), length: listening.length))
            }
        }
        .accessibilityElement()
        .accessibilityLabel(text["recording.wave"])
        .accessibilityValue(text.format("sources.sheet.seekValue", SourceLayers.duration(listening.elapsed), SourceLayers.duration(listening.length)))
        .accessibilityHint(text["recording.waveHint"])
        .accessibilityAdjustableAction { direction in
            switch direction {
            case .increment: move(to: listening.elapsed + RecordingWave.step)
            case .decrement: move(to: listening.elapsed - RecordingWave.step)
            @unknown default: break
            }
        }
    }

    /// The columns, each as loud as its stretch, centred on the middle line: the part heard in the ink, the rest fainter.
    private func draw(in context: inout GraphicsContext, size: CGSize, cursor: CGFloat) {
        let columns = wave.columns(Int(size.width / pitch))
        for (index, share) in columns.enumerated() {
            let x = CGFloat(index) * pitch
            let height = max(pitch / 2, CGFloat(share) * size.height)
            let column = CGRect(x: x, y: (size.height - height) / 2, width: pitch / 2, height: height)
            context.fill(Path(roundedRect: column, cornerRadius: pitch / 4), with: .color(Color.ds(x < cursor ? "ink-2" : "ink-3")))
        }
    }

    /// The sound moved to a moment, kept within the recording; playing, it goes on from there.
    private func move(to moment: Int) {
        listening.load(url)
        listening.seek(to: moment)
    }
}
