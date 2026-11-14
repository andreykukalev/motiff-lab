import AVFoundation
import SwiftUI

/// What the render thread and the screen share: the tempo, the beats to the bar, and the beat sounding now.
private final class ClickClock: @unchecked Sendable {
    let lock = NSLock()
    var bpm = 80.0
    var beats = 4
    var running = false
    /// Frames into the beat sounding now, and that beat counted from the start.
    var phase = 0.0
    var count = 0

    var beat: Int {
        lock.lock()
        defer { lock.unlock() }
        return running ? count % max(1, beats) : -1
    }
}

/// The metronome's rules, kept apart from the sound for the tests (#521 section 15, the web app's `metronome.ts`).
enum MetronomeTempo {
    static let range = 30...240
    /// The beats to the bar the pill goes through.
    static let beatChoices = [2, 3, 4, 6]
    /// The piece tempo while nothing states one: music-core's `PlaybackTempo.PieceDefaultPerMinute` (version 0.5.5).
    static let pieceDefault = 80
    /// How long the button is held before a slide sets the tempo: the web app's long press (`Scripts/input/long-press.ts`).
    static let holdSeconds = 0.55

    static func clamped(_ bpm: Int) -> Int {
        min(range.upperBound, max(range.lowerBound, bpm))
    }

    /// The tempo a slide sets after the hold: one beat a minute for each `step` points, up or right faster, down or left
    /// slower, within 30 to 240.
    static func dragged(from bpm: Int, by translation: CGSize, step: CGFloat) -> Int {
        guard step > 0 else { return clamped(bpm) }
        return clamped(bpm + Int(((translation.width - translation.height) / step).rounded()))
    }

    /// What is kept for a piece on the device: "bpm,beats" for a tempo the player gave the metronome, "-,beats" while it
    /// follows the piece tempo; nil for anything else.
    static func kept(_ value: String?) -> (own: Int?, beats: Int)? {
        guard let parts = value?.split(separator: ",", omittingEmptySubsequences: false), parts.count == 2,
              let beats = Int(parts[1]) else { return nil }
        let own = Int(parts[0]).map(clamped)
        guard own != nil || parts[0] == "-" else { return nil }
        return (own, beatChoices.contains(beats) ? beats : 4)
    }

    static func keptText(own: Int?, beats: Int) -> String {
        "\(own.map { String($0) } ?? "-"),\(beats)"
    }
}

/// The metronome (0023, #521 section 15, the web app's `Scripts/metronome/metronome.ts`): one for the score and the PDF
/// view, which goes on clicking as PDF | Notes hands the piece to its other view. Its button starts and stops it at once,
/// with or without Play; it clicks the piece tempo (never times the playback pace) until the player gives it a tempo of
/// its own, by holding the button and sliding, or on the PDF view's pill. While the score plays, its own clicks go silent
/// and the playback engine clicks the beats heard, so the two never drift apart at another pace; once playback stops it
/// clicks its own tempo again. It clicks with the playback engine's click (a sine dying away fast, the first beat of the
/// bar higher), on the audio clock, from 30 to 240 beats a minute with 2, 3, 4 or 6 beats to the bar. It needs no notes
/// and runs on the device, so it is free. Its tempo and beats are kept per piece on the device.
@MainActor
final class Metronome: ObservableObject {
    static let shared = Metronome()
    static let minBpm = MetronomeTempo.range.lowerBound
    static let maxBpm = MetronomeTempo.range.upperBound
    static let step = 4
    /// The beats to the bar the button goes through.
    static let beatChoices = MetronomeTempo.beatChoices

    @Published private(set) var running = false
    @Published private(set) var bpm = MetronomeTempo.pieceDefault
    @Published private(set) var beats = 4
    /// The beat sounding now, from 0, or -1 when stopped.
    @Published private(set) var beat = -1
    /// The beat's note as the piece's tempo mark writes it: "♩", or "♩." for a dotted quarter.
    @Published private(set) var note = "♩"
    /// The tempo the player gave the metronome for this piece, or nil while it follows the piece tempo.
    @Published private(set) var own: Int?
    /// The piece tempo in beats a minute, as the score's mark shows it.
    private(set) var pieceBpm = MetronomeTempo.pieceDefault

    private let clock = ClickClock()
    private var engine: AVAudioEngine?
    private var timer: Timer?
    private var taps: [Double] = []
    private var id: String?
    private let store: UserDefaults
    /// The score's player, whose engine clicks the beats heard while it plays.
    private weak var playback: Player?
    /// Whether the score plays now: the metronome's own clicks are silent then.
    private var locked = false

    init(store: UserDefaults = .standard) {
        self.store = store
    }

    private func key(_ id: String) -> String { "motiff.pdf.metronome.\(id)" }

    /// The piece's metronome as kept on the device under `motiff.pdf.metronome.<id>`, at the piece tempo `tempo` in beats a
    /// minute and the piece's first bar's beats. A view that has neither (the PDF view) keeps those given last for the same
    /// piece; a piece never given one is at the default of 80. A piece with no id yet keeps nothing.
    func load(piece id: String?, tempo: Int? = nil, note: String? = nil, beats barBeats: Int? = nil) {
        if id == nil || id != self.id {
            pieceBpm = MetronomeTempo.pieceDefault
            self.note = "♩"
            beats = 4
        }
        self.id = id
        if let tempo { pieceBpm = MetronomeTempo.clamped(tempo) }
        if let note { self.note = note }
        let kept = id.flatMap { MetronomeTempo.kept(store.string(forKey: key($0))) }
        own = kept?.own
        if let kept {
            beats = kept.beats
        } else if let barBeats, MetronomeTempo.beatChoices.contains(barBeats) {
            beats = barBeats
        }
        apply()
    }

    /// The score's player: its engine clicks while the metronome runs, and the metronome follows its piece tempo and keeps
    /// silent while it plays.
    func attach(_ player: Player) {
        playback = player
        pieceChanged(player)
        player.metronome = running
        playbackChanged(player)
    }

    /// The score closed: the metronome clicks on its own again.
    func detach(_ player: Player) {
        guard playback === player else { return }
        player.metronome = false
        playback = nil
        lock(false)
    }

    /// The player's piece tempo, read again (the player set it, or the notes changed).
    func pieceChanged(_ player: Player) {
        guard playback === player else { return }
        let reading = player.tempo.reading
        // The time signature's numerator counts eighths in 6/8, where the metronome clicks dotted quarters.
        let bar = player.firstBarBeats.map { reading?.dotted == true && $0 % 3 == 0 ? $0 / 3 : $0 }
        load(piece: player.pieceKey, tempo: reading?.startBeats, note: reading?.note, beats: bar)
    }

    /// Playback started or stopped.
    func playbackChanged(_ player: Player) {
        guard playback === player else { return }
        lock(player.state == .playing)
    }

    func toggle() {
        running ? stop() : start()
    }

    func start() {
        guard !running else { return }
        do {
            try AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
            try AVAudioSession.sharedInstance().setActive(true)
            let engine = try self.engine ?? makeEngine()
            self.engine = engine
            running = true
            restartClock()
            if !engine.isRunning { try engine.start() }
            let timer = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.tick() }
            }
            RunLoop.main.add(timer, forMode: .common)
            self.timer = timer
            playback?.metronome = true
        } catch {
            stop()
        }
    }

    func stop() {
        clock.lock.lock()
        clock.running = false
        clock.lock.unlock()
        timer?.invalidate()
        timer = nil
        engine?.pause()
        running = false
        beat = -1
        playback?.metronome = false
    }

    /// A new tempo or beats to the bar, heard from the next beat and kept for the piece; the piece tempo itself is the
    /// metronome following the piece again.
    func change(bpm: Int, beats: Int) {
        let bpm = MetronomeTempo.clamped(bpm)
        own = bpm == pieceBpm ? nil : bpm
        self.beats = Self.beatChoices.contains(beats) ? beats : 4
        apply()
        if let id { store.set(MetronomeTempo.keptText(own: own, beats: self.beats), forKey: key(id)) }
    }

    func slower() { change(bpm: bpm - Self.step, beats: beats) }
    func faster() { change(bpm: bpm + Self.step, beats: beats) }

    func nextBeats() {
        let index = Self.beatChoices.firstIndex(of: beats) ?? 2
        change(bpm: bpm, beats: Self.beatChoices[(index + 1) % Self.beatChoices.count])
    }

    /// A tap on the tempo: the tempo of the last few taps, once there are two.
    func tap(at now: Double = Date().timeIntervalSince1970 * 1000) {
        taps = taps.filter { now - $0 <= 8000 } + [now]
        if let tapped = Self.tappedBpm(taps) { change(bpm: tapped, beats: beats) }
    }

    /// The mean gap of up to the last four taps (milliseconds), in beats a minute; nil with fewer than two taps or when the
    /// last gap is over two seconds (a new start). As the web app's `tappedBpm`.
    nonisolated static func tappedBpm(_ times: [Double]) -> Int? {
        guard times.count >= 2, times[times.count - 1] - times[times.count - 2] <= 2000 else { return nil }
        let recent = Array(times.suffix(5))
        let gaps = zip(recent.dropFirst(), recent).map { $0 - $1 }.filter { $0 > 0 && $0 <= 2000 }
        guard !gaps.isEmpty else { return nil }
        return MetronomeTempo.clamped(Int((60000 / (gaps.reduce(0, +) / Double(gaps.count))).rounded()))
    }

    /// The tempo heard: the player's own for the piece, else the piece tempo; the beats to the bar with it.
    private func apply() {
        bpm = own ?? pieceBpm
        clock.lock.lock()
        clock.bpm = Double(bpm)
        clock.beats = beats
        clock.lock.unlock()
    }

    /// Silent while the score plays; when it stops, the clicks start again from the first beat of a bar.
    private func lock(_ playing: Bool) {
        guard playing != locked else { return }
        locked = playing
        restartClock()
    }

    private func restartClock() {
        clock.lock.lock()
        clock.bpm = Double(bpm)
        clock.beats = beats
        clock.phase = 0
        clock.count = 0
        clock.running = running && !locked
        clock.lock.unlock()
    }

    private func tick() {
        let now = clock.beat
        if now != beat { beat = now }
    }

    private func makeEngine() throws -> AVAudioEngine {
        let engine = AVAudioEngine()
        let rate = engine.outputNode.outputFormat(forBus: 0).sampleRate > 0 ? engine.outputNode.outputFormat(forBus: 0).sampleRate : 48000
        guard let format = AVAudioFormat(standardFormatWithSampleRate: rate, channels: 1) else {
            throw NSError(domain: "Metronome", code: 1)
        }
        let node = Self.clickNode(format: format, clock: clock, rate: rate)
        engine.attach(node)
        engine.connect(node, to: engine.mainMixerNode, format: format)
        return engine
    }
}

extension Metronome {
    /// The node that renders the clicks. It is made outside the main actor: its block runs on the audio thread.
    nonisolated fileprivate static func clickNode(format: AVAudioFormat, clock: ClickClock, rate: Double) -> AVAudioSourceNode {
        let length = 0.05 * rate
        let decay = 1 / (0.012 * rate)
        return AVAudioSourceNode(format: format) { _, _, frameCount, audioBufferList in
            let buffers = UnsafeMutableAudioBufferListPointer(audioBufferList)
            guard let first = buffers.first, let data = first.mData?.assumingMemoryBound(to: Float.self) else { return noErr }
            clock.lock.lock()
            defer { clock.lock.unlock() }
            let framesPerBeat = rate * 60 / max(1, clock.bpm)
            for frame in 0..<Int(frameCount) {
                var sample: Float = 0
                if clock.running {
                    if clock.phase < length {
                        let accent = clock.count % max(1, clock.beats) == 0
                        let omega = 2 * Double.pi * (accent ? 1760 : 1320) / rate
                        sample = Float(sin(omega * clock.phase) * exp(-clock.phase * decay) * (accent ? 0.5 : 0.3))
                    }
                    clock.phase += 1
                    if clock.phase >= framesPerBeat {
                        clock.phase -= framesPerBeat
                        clock.count += 1
                    }
                }
                data[frame] = sample
            }
            for other in buffers.dropFirst() {
                other.mData?.copyMemory(from: data, byteCount: Int(other.mDataByteSize))
            }
            return noErr
        }
    }
}

/// The metronome's pill (the web app's `.metronome-pill`): start and stop, slower, the tempo tapped in time on its number,
/// faster, the beats to the bar and a dot for each beat, the one sounding now filled and the first a little larger.
struct MetronomePill: View {
    @ObservedObject var metronome: Metronome
    let text: UIText
    let close: () -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Button {
                metronome.toggle()
            } label: {
                Icon(name: metronome.running ? "pause" : "playf")
                    .foregroundStyle(Color.ds("on-accent"))
                    .frame(width: 40, height: 40)
                    .background(Color.ds("accent"), in: Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(metronome.running ? text["metronome.stop"] : text["metronome.start"])
            Button("−") { metronome.slower() }
                .buttonStyle(DSItemStyle())
                .disabled(metronome.bpm <= Metronome.minBpm)
                .accessibilityLabel(text["metronome.slower"])
            Button {
                metronome.tap()
            } label: {
                HStack(spacing: 4) {
                    Text(String(metronome.bpm)).monospacedDigit()
                    Text(text["metronome.bpm"]).foregroundStyle(Color.ds("ink-2"))
                }
            }
            .buttonStyle(DSItemStyle())
            .accessibilityLabel(text.format("metronome.tempoLabel", String(metronome.bpm)))
            .accessibilityHint(text["metronome.tapHint"])
            Button("+") { metronome.faster() }
                .buttonStyle(DSItemStyle())
                .disabled(metronome.bpm >= Metronome.maxBpm)
                .accessibilityLabel(text["metronome.faster"])
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            Button {
                metronome.nextBeats()
            } label: {
                HStack(spacing: 4) {
                    Text(String(metronome.beats)).monospacedDigit()
                    Text(text["metronome.beatsShort"])
                }
            }
            .buttonStyle(DSItemStyle())
            .accessibilityLabel(text.format("metronome.beatsLabel", String(metronome.beats)))
            .accessibilityHint(text["metronome.beatsHint"])
            HStack(spacing: 6) {
                ForEach(0..<metronome.beats, id: \.self) { index in
                    let size: CGFloat = index == 0 ? 10 : 8
                    Circle()
                        .fill(index == metronome.beat ? Color.ds("accent") : Color.clear)
                        .overlay(Circle().strokeBorder(index == metronome.beat ? Color.ds("accent") : Color.ds("ink-3"), lineWidth: 1.5))
                        .frame(width: size, height: size)
                }
            }
            .padding(.horizontal, CGFloat.ds("s-2"))
            .accessibilityHidden(true)
            Button(action: close) {
                Icon(name: "close").frame(width: 32, height: 32)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["metronome.close"])
        }
        .padding(.leading, 6)
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: 52)
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["metronome.label"])
    }
}


/// The metronome's button, on the score's pill and on the PDF view's bar (#521 section 15): a tap starts or stops it at
/// once; held for a moment, it shows the tempo over the button, and a slide up or right makes it faster, down or left
/// slower, one beat a minute each `s-1`; letting go keeps that tempo for the piece. A finger or the Pencil alike, and
/// VoiceOver's swipe up and down. The tap is the button's own for VoiceOver, so the button keeps its trait and label.
struct MetronomeHold: ViewModifier {
    @ObservedObject var metronome: Metronome
    let text: UIText
    /// The tempo shows under the button rather than over it: the PDF view's bar is at the top of the screen.
    var below = false
    let toggle: () -> Void
    /// How far the finger slid since the hold, or nil while the button is not held.
    @GestureState private var held: CGSize?

    private var step: CGFloat { CGFloat.ds("s-1") }

    private var hold: some Gesture {
        LongPressGesture(minimumDuration: MetronomeTempo.holdSeconds)
            .sequenced(before: DragGesture(minimumDistance: 0))
            .updating($held) { value, state, _ in
                if case .second(true, let drag) = value { state = drag?.translation ?? .zero }
            }
            .onEnded { value in
                guard case .second(true, let drag?) = value else { return }
                let bpm = MetronomeTempo.dragged(from: metronome.bpm, by: drag.translation, step: step)
                if bpm != metronome.bpm { metronome.change(bpm: bpm, beats: metronome.beats) }
            }
    }

    func body(content: Content) -> some View {
        content
            .highPriorityGesture(hold.exclusively(before: TapGesture().onEnded { toggle() }))
            .overlay(alignment: below ? .bottom : .top) {
                if let held {
                    Text(verbatim: "\(metronome.note) = \(MetronomeTempo.dragged(from: metronome.bpm, by: held, step: step))")
                        .dsType(.label)
                        .monospacedDigit()
                        .foregroundStyle(Color.ds("ink"))
                        .fixedSize()
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .padding(.vertical, CGFloat.ds("s-2"))
                        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
                        .alignmentGuide(.top) { $0[.bottom] + CGFloat.ds("s-2") }
                        .alignmentGuide(.bottom) { $0[.top] - CGFloat.ds("s-2") }
                        .allowsHitTesting(false)
                        .accessibilityHidden(true)
                }
            }
            .animation(.ds("t-fast"), value: held == nil)
            .accessibilityValue(text.format("metronome.tempoValue", String(metronome.bpm)))
            .accessibilityHint(text["metronome.holdHint"])
            .accessibilityAdjustableAction { direction in
                switch direction {
                case .increment: metronome.faster()
                case .decrement: metronome.slower()
                @unknown default: break
                }
            }
    }
}
