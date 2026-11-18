import AVFoundation
import QuartzCore
import SwiftUI

/// Playing a piece (decision record 0019 section 2.1): `AVAudioEngine` with an `AVAudioSourceNode` that runs
/// `PlaybackEngine`, the Swift port of the web's engine, over music-core's playback score. The audio session's playback
/// category plays with the silent switch on.
@MainActor
final class Player: ObservableObject {
    @Published private(set) var state = TransportState.stopped
    /// The tick being heard now, about 30 times a second while playing.
    @Published private(set) var tick = 0.0
    /// The bars' starts and printed numbers, from music-core's playback score.
    private(set) var barNumbers: [(start: Double, number: Int)] = []

    /// The printed number of the bar that holds the tick; nil before the score is read.
    func barNumber(at tick: Double) -> Int? {
        barNumbers.last { $0.start <= tick }?.number ?? barNumbers.first?.number
    }
    @Published private(set) var ready = false
    @Published private(set) var problem: String?
    /// The playback tempo and the piece's (#511 section 17): an object of its own, so that the pill's tempo control observes
    /// it alone and is not drawn again with every position report while playing; a change applies to the engine at once.
    let tempo = TempoSetting()
    /// The playback tempo, 25 % to 150 % of the piece's, as resume points keep it.
    var tempoPercent: Int {
        get { tempo.percent }
        set { tempo.percent = newValue }
    }
    /// The engine's clicks on the piece's beats while it plays: on while the metronome (`Metronome.shared`) runs, which
    /// keeps its own clicks silent then (#521 section 15).
    @Published var metronome = false { didSet { send(.metronome(metronome)) } }

    init() {
        ProbeCounts.shared.add("player")
        tempo.changed = { [weak self] percent in self?.send(.rate(Double(percent) / 100)) }
    }

    deinit {
        ProbeCounts.shared.add("player", -1)
    }

    /// Which hands sound (#328): the piece's notes as music-core gives them for both hands or for one
    /// alone. A change applies at once, also while playing, and the loop and the place stay.
    @Published var hands = PlayerHands.both {
        didSet { if oldValue != hands { Task { await applyHands() } } }
    }
    /// Which hands the piece's notes carry, and whether any was inferred, from music-core's playback score.
    @Published private(set) var handsHeld = HandsHeld()
    /// The beat lines of the piece in ticks, from music-core's playback score: what a selection by beats snaps to (#349).
    private(set) var beatLines: [Double] = []
    /// The piece played, for hearing one hand alone.
    private var piece: Int32?
    /// The piece's project id, by which the metronome keeps its tempo on the device, and the first bar's time signature
    /// numerator.
    private(set) var pieceKey: String?
    private(set) var firstBarBeats: Int?

    private let audio = AVAudioEngine()
    private var renderer: Renderer?
    private var last: Renderer.Stamped?
    private var polling: Task<Void, Never>?
    /// Starts the engine again after a call, Siri, AirPods or dictation stopped it (EngineKeeper); none once shut down.
    private var keeper: EngineKeeper?
    /// The screen has gone (`shutdown`): a load still on its way starts nothing (#582 item 2: a view switched while the
    /// notes were loading left a player clicking the metronome for a screen no longer there).
    private var shutDown = false

    /// Starts the audio, loads the piano and the piece's playback score from music-core; the metronome follows its piece
    /// tempo, kept under `key`.
    func load(piece: Int32, key: String? = nil) async {
        do {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.playback, mode: .default)
            try session.setActive(true)

            let sampleRate = audio.outputNode.outputFormat(forBus: 0).sampleRate
            let renderer = Renderer(sampleRate: sampleRate > 0 ? sampleRate : 48_000)
            self.renderer = renderer
            guard let format = AVAudioFormat(standardFormatWithSampleRate: renderer.sampleRate, channels: 2) else {
                throw PlayerError(message: "No audio format at \(renderer.sampleRate) Hz.")
            }
            let node = Self.sourceNode(format: format, renderer: renderer)
            audio.attach(node)
            audio.connect(node, to: audio.mainMixerNode, format: format)
            try audio.start()
            // An interruption pauses the music where it is; the engine starts again after it, and after a route change.
            keeper = EngineKeeper(audio) { [weak self] in self?.send(.pause) }

            let piano = Task.detached(priority: .userInitiated) { try Piano.load() }
            let answer = await PieceCore(piece: piece).playback()
            let samples = try await piano.value
            guard !shutDown else {
                keeper = nil
                audio.stop()
                return
            }
            guard let score = answer.value.map(PlaybackScore.init) else {
                throw PlayerError(message: answer.message ?? "music-core gave no playback score.")
            }
            renderer.send(.samples(samples))
            renderer.send(.timeline(score.timeline))
            renderer.send(.rate(Double(tempo.percent) / 100))
            tempo.reading = score.tempo
            barNumbers = score.barNumbers
            beatLines = score.timeline.beats.map(\.at)
            barBeats = score.barBeats
            handsHeld = score.hands
            self.piece = piece
            renderer.send(.source(id: "piece", notes: score.notes))
            renderer.send(.activeSources(["piece"]))
            ready = true
            pieceKey = key
            firstBarBeats = score.timeline.bars.first.map { Int($0.beatsPerBar) }
            Metronome.shared.attach(self)
            startPolling()
        } catch {
            problem = (error as? PlayerError)?.message ?? error.localizedDescription
        }
    }

    func toggle() {
        send(state == .stopped ? .play : .pause)
    }

    /// Back to the start, playing on if it was playing.
    func rewind() {
        send(.seek(0))
    }

    /// Moves to a place in the piece without starting or stopping, as a place of a fact chosen in Insights asks.
    func seek(to tick: Double) {
        send(.seek(tick))
    }

    /// Plays from a place in the piece, as Piece DNA's Play asks: moves there, and starts if it was still.
    func play(from tick: Double) {
        send(.seek(tick))
        if state == .stopped { send(.play) }
    }

    /// Where the playhead may go: inside the loop when one plays (before its end, so playing does not run past it), else
    /// anywhere from the start.
    nonisolated static func place(_ tick: Double, inside loop: Range<Double>?) -> Double {
        guard let loop, !loop.isEmpty else { return max(0, tick) }
        return min(max(tick, loop.lowerBound), max(loop.lowerBound, loop.upperBound - 1))
    }

    /// The passage that loops, in ticks; nil when nothing loops.
    @Published private(set) var loop: Range<Double>?

    /// Loops a passage and plays it from its start, or from `start` when given, or stops looping (0018 section 7: a selection
    /// loops the selection). With `play` false the loop is set and waits for Play, as a resumed piece's does (#327). With
    /// `once` the passage plays through once and stops at its end, back at its start (the selection Play without Loop).
    func setLoop(_ range: Range<Double>?, from start: Double? = nil, play: Bool = true, once: Bool = false) {
        applyLoop(range, once: once)
        guard let range else { return }
        if let start {
            send(.seek(start))
        } else if !range.contains(tick) {
            send(.seek(range.lowerBound))
        }
        if play && state == .stopped { send(.play) }
    }

    /// The engine loops a passage, or nothing, and the place stays.
    private func applyLoop(_ range: Range<Double>?, once: Bool = false) {
        loop = range
        // Notes heard alone (the circle's scale) give way to the piece, or the variation, as any other loop comes.
        if hearingAlone {
            hearingAlone = false
            send(.activeSources([hearing == .variation ? Self.variationSource : Self.pieceSource]))
            send(.removeSource(Self.aloneSource))
        }
        // Alternating is a loop of its own: another loop, or none, ends it and the original sounds on.
        if hearing == .alternate {
            hearing = .original
            send(.activeSources([Self.pieceSource]))
        }
        guard let range else {
            send(.loopOff)
            return
        }
        send(.loop(start: range.lowerBound, end: range.upperBound, once: once))
    }

    // MARK: - The playback model (version 0.5.2, #495 UX-10; «Два указателя и режимы»)

    /// The selection as the range it plays (the model's S), the player's copy of the score's: the player owns it and Цикл,
    /// and the views only read them (PlaybackModel).
    @Published private(set) var selection: Range<Double>?

    /// Цикл: the selection loops while it is on, and Play keeps the position inside it. It turns on by itself when a
    /// selection appears, and is kept while there is none.
    @Published private(set) var cycle = true

    /// The position put outside the selection by a tap with Цикл on (the model's lead-in, `PlaybackModel.State.leadIn`):
    /// Play plays from it into the selection.
    @Published private(set) var leadIn = false

    /// The piece's beats as bar.beat, from music-core's playback score: the pill's and the roll's words for a place.
    private(set) var barBeats = BarBeats()

    /// The position as bar.beat, "16.2"; empty before the score is read.
    var placeLabel: String {
        barBeats.at(tick)?.text ?? ""
    }

    /// The model's state now: the selection and Цикл are the player's, the position and the transport the engine's.
    var model: PlaybackModel.State {
        PlaybackModel.State(selection: selection, loop: cycle, tick: tick, transport: state, leadIn: leadIn)
    }

    /// A playhead held by the pen or a finger now (T14–T16, PlayheadDrag.swift); nil while none is.
    var drag: PlayheadDrag?

    /// One event of the playback model: the engine gets its steps, and the selection and Цикл become what the model says;
    /// the position and the transport come back from the engine. A drag gives the state as it holds it (`from`). Returns the
    /// state the model expects once the engine has the steps.
    @discardableResult
    func apply(_ event: PlaybackModel.Event, from given: PlaybackModel.State? = nil) -> PlaybackModel.State {
        let decided = PlaybackModel.next(given ?? model, event)
        selection = decided.state.selection
        cycle = decided.state.loop
        leadIn = decided.state.leadIn
        perform(decided.steps)
        return decided.state
    }

    /// The score's selection as it is now, after focus: the model takes it (T9–T11), and
    /// the engine loops what the model says again, in place of the loop another owner, such as focus, gave it.
    func hold(selection range: Range<Double>?) {
        apply(.select(range))
        let wanted = PlaybackModel.engineLoop(model)
        if wanted != loop { applyLoop(wanted) }
    }

    /// A tap on the roll's bars or strips (T12): the position goes there, playing on from there when it plays, and Цикл
    /// turns off when it leaves the selection; `play` starts it from there. A tap on the score's beat is `place`.
    func go(to at: Double, play: Bool) {
        apply(.seek(at))
        if play && state == .stopped {
            perform([.play])
        }
    }

    /// The model's steps to the engine; its loop goes through applyLoop, so `loop` says what the engine loops.
    func perform(_ steps: [TransportStep]) {
        for step in steps {
            switch step {
            case .pause:
                send(.pause)
            case .play:
                send(.play)
            case .seek(let at):
                send(.seek(at))
            case .loop(let range):
                applyLoop(range)
            case .loopOff:
                applyLoop(nil)
            case .scrub(let from, let to):
                send(.scrub(from: from, to: to))
            case .scrubEnd:
                send(.scrubEnd)
            }
        }
    }

    /// What sounds (0018 section 7, Original / Variation / Alternate): the piece, the piece with the variation being tried
    /// in place, or the two looping one after the other.
    enum Hearing: Equatable {
        case original, variation, alternate
    }

    @Published private(set) var hearing = Hearing.original
    /// Whether a variation is there to hear.
    @Published private(set) var hasVariation = false
    /// Whether what stands in the variation's place is another version of the piece, heard in focus (0022, section 5):
    /// then the switch reads Notes / Version / Alternate.
    @Published private(set) var secondIsVersion = false

    /// The piece with the variation in place (music-core's `playback` of `inPlace`), or with `version` another version's
    /// notes (`playback` of `version`); nil when it is left: the original sounds again, looping where it looped.
    func setVariation(_ notes: [PlaybackNote]?, version: Bool = false) {
        guard let notes else {
            guard hasVariation else { return }
            hasVariation = false
            secondIsVersion = false
            send(.activeSources([Self.pieceSource]))
            send(.removeSource(Self.variationSource))
            if hearing == .alternate, let loop { send(.loop(start: loop.lowerBound, end: loop.upperBound)) }
            hearing = .original
            return
        }
        send(.source(id: Self.variationSource, notes: notes))
        hasVariation = true
        secondIsVersion = version
    }

    /// Hears the original, the variation, or the two one after the other, looping `range` and playing from its start
    /// when it was still or elsewhere.
    func hear(_ next: Hearing, over range: Range<Double>) {
        guard next == .original || hasVariation else { return }
        if hearingAlone {
            hearingAlone = false
            send(.removeSource(Self.aloneSource))
        }
        hearing = next
        switch next {
        case .original:
            send(.activeSources([Self.pieceSource]))
            setLoop(range)
        case .variation:
            send(.activeSources([Self.variationSource]))
            setLoop(range)
        case .alternate:
            loop = range
            let passes = [Self.pieceSource, Self.variationSource].map { LoopPass(ids: [$0], start: range.lowerBound, end: range.upperBound) }
            send(.alternate(passes))
            if state == .stopped { send(.play) }
        }
    }

    private static let pieceSource = "piece"
    private static let variationSource = "variation"
    private static let aloneSource = "alone"

    /// Whether notes outside the piece are heard on their own, as the circle of fifths' scale.
    /// Whether notes play alone (the circle's scale), until they end or the player stops them.
    @Published private(set) var hearingAlone = false

    /// Hears notes on their own in place of the piece (the circle's Hear the scale), looping where they sound and playing
    /// from their start; the next loop, or none, brings the piece back.
    func hearAlone(_ notes: [PlaybackNote], over range: Range<Double>) {
        setLoop(nil)
        send(.source(id: Self.aloneSource, notes: notes))
        send(.activeSources([Self.aloneSource]))
        hearingAlone = true
        // Testing (2026-11-06): the scale went round with no way to stop it. It plays once and stops at its end.
        send(.loop(start: range.lowerBound, end: range.upperBound, once: true))
        send(.seek(range.lowerBound))
        if state == .stopped { send(.play) }
    }

    /// Stops notes heard alone: the piece, or the variation, sounds again where it was.
    func stopAlone() {
        guard hearingAlone else { return }
        send(.pause)
        setLoop(nil)
    }

    /// The piece's notes again after they changed, as when bars of another version are taken into the score or restored
    /// (0022): the timeline and the notes are sent anew, and whatever plays or loops carries on.
    func reload(piece: Int32) async {
        let answer = await PieceCore(piece: piece).playback(Self.handsRequest(hands))
        guard let score = answer.value.map(PlaybackScore.init) else { return }
        tempo.reading = score.tempo
        barNumbers = score.barNumbers
        beatLines = score.timeline.beats.map(\.at)
        barBeats = score.barBeats
        handsHeld = score.hands
        send(.timeline(score.timeline))
        send(.source(id: Self.pieceSource, notes: score.notes))
        Metronome.shared.pieceChanged(self)
    }

    /// The piece's notes for the hands chosen, in place of those that sounded: what plays or loops carries on (#328).
    private func applyHands() async {
        guard let piece else { return }
        let chosen = hands
        let answer = await PieceCore(piece: piece).playback(Self.handsRequest(chosen))
        // Another choice came while music-core answered: the newer one sends its own notes.
        guard chosen == hands, let score = answer.value.map(PlaybackScore.init) else { return }
        send(.source(id: Self.pieceSource, notes: score.notes))
    }

    /// The `playback` request for the hands chosen: nothing for both, as before.
    nonisolated static func handsRequest(_ hands: PlayerHands) -> String? {
        hands == .both ? nil : "{\"hands\": \"\(hands.rawValue)\"}"
    }

    /// Stops the sound and the audio engine; the screen calls this when it closes.
    func shutdown() {
        shutDown = true
        // A playhead held as the screen closes is let go where it is: what the drag sounds fades, the pause ends the rest.
        if drag != nil { send(.scrubEnd) }
        drag = nil
        polling?.cancel()
        polling = nil
        send(.pause)
        keeper = nil
        audio.stop()
        ready = false
        Metronome.shared.detach(self)
    }

    /// The node that pulls blocks from the engine. It is made outside the main actor: its block runs on the audio thread.
    nonisolated private static func sourceNode(format: AVAudioFormat, renderer: Renderer) -> AVAudioSourceNode {
        AVAudioSourceNode(format: format) { _, _, frameCount, buffers in
            renderer.render(UnsafeMutableAudioBufferListPointer(buffers), frames: Int(frameCount))
            return noErr
        }
    }

    private func send(_ message: EngineMessage) {
        renderer?.send(message)
    }

    private func startPolling() {
        polling?.cancel()
        polling = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 33_000_000)
                self?.poll()
            }
        }
    }

    private func poll() {
        guard let renderer else { return }
        for report in renderer.takeReports() {
            if case .position = report.report {
                last = report
            }
        }
        guard let last, case .position(let position) = last.report else { return }
        if state != position.state {
            let started = state != .stopped
            state = position.state
            Metronome.shared.playbackChanged(self)
            // Notes heard alone that came to their end give way to the piece.
            if hearingAlone && started && state == .stopped { setLoop(nil) }
        }
        var now = position.tick
        if position.state == .playing && position.ticksPerSecond > 0 {
            let heard = CACurrentMediaTime() - last.at - audio.outputNode.presentationLatency
            now += max(0, heard) * position.ticksPerSecond
            if position.loopEnd > position.loopStart && position.tick >= position.loopStart && position.tick < position.loopEnd {
                let length = position.loopEnd - position.loopStart
                now = position.loopStart + (now - position.loopStart).truncatingRemainder(dividingBy: length)
            }
        }
        if abs(now - tick) >= 1 {
            tick = now
        }
        // The lead-in ends once the position reaches the selection (T4–T6).
        if leadIn { leadIn = PlaybackModel.next(model, .report(tick: tick, transport: state)).state.leadIn }
    }

    /// The engine on the audio thread: messages from the main thread wait in an inbox until the next block, and the
    /// engine's reports wait in an outbox, each stamped with the time its frame was rendered.
    final class Renderer: @unchecked Sendable {
        struct Stamped: Sendable {
            var report: EngineReport
            var at: CFTimeInterval
        }

        let sampleRate: Double
        private let engine: PlaybackEngine
        private let lock = NSLock()
        private var inbox: [EngineMessage] = []
        private var outbox: [Stamped] = []
        private var frame = 0

        init(sampleRate: Double) {
            self.sampleRate = sampleRate
            engine = PlaybackEngine(sampleRate: sampleRate)
        }

        func send(_ message: EngineMessage) {
            lock.lock()
            inbox.append(message)
            lock.unlock()
        }

        func takeReports() -> [Stamped] {
            lock.lock()
            defer { lock.unlock() }
            let taken = outbox
            outbox.removeAll(keepingCapacity: true)
            return taken
        }

        /// On the audio thread: the messages since the last block, then the block itself.
        func render(_ buffers: UnsafeMutableAudioBufferListPointer, frames: Int) {
            lock.lock()
            let messages = inbox
            inbox.removeAll(keepingCapacity: true)
            lock.unlock()

            for message in messages {
                engine.handle(message)
            }
            guard let left = buffers.first?.mData?.assumingMemoryBound(to: Float.self) else { return }
            let right = buffers.count > 1 ? buffers[1].mData?.assumingMemoryBound(to: Float.self) : nil
            let blockFrame = frame
            engine.render(left, right, frames: frames, blockFrame: blockFrame)
            frame += frames

            let reports = engine.takeReports()
            guard !reports.isEmpty else { return }
            let now = CACurrentMediaTime()
            let stamped = reports.map { report -> Stamped in
                var at = now
                if case .position(let position) = report, position.frame >= 0 {
                    // The block is heard from now on; a report inside it is that many frames later.
                    at += Double(position.frame - blockFrame) / sampleRate
                }
                return Stamped(report: report, at: at)
            }
            lock.lock()
            outbox.append(contentsOf: stamped)
            if outbox.count > 64 {
                outbox.removeFirst(outbox.count - 64)
            }
            lock.unlock()
        }
    }
}

struct PlayerError: Error {
    let message: String
}

/// music-core's playback score (`playback`): notes with the pedal already in their lengths, tempos, beats and bars.
struct PlaybackScore: Sendable {
    var notes: [PlaybackNote]
    var timeline: PlaybackTimeline
    /// Where each bar starts and the number it is printed with, in time order: "Back to playback · bar 12".
    var barNumbers: [(start: Double, number: Int)] = []
    /// Which hands the piece's notes carry, and whether any was inferred (#328).
    var hands = HandsHeld()
    /// Each beat's bar and beat, for writing a place as bar.beat (v0.5.2).
    var barBeats = BarBeats()
    /// The tempo at the start and where it comes from (v0.5.4, #511 section 17); nil from a music-core before it.
    var tempo: TempoReading?

    /// The score as music-core answers it (`PlaybackAnswer`); "hands" only `playback` writes.
    init(_ answer: PlaybackAnswer) {
        notes = answer.notes.map { PlaybackNote(onset: $0.onset, duration: $0.duration, pitch: $0.pitch, velocity: $0.velocity, track: $0.track) }
        timeline = PlaybackTimeline(
            tempos: answer.tempos.map { .init(at: $0.at, microsecondsPerQuarter: $0.microsecondsPerQuarter) },
            beats: answer.beats.map { .init(at: $0.at, downbeat: $0.downbeat) },
            bars: answer.bars.map { .init(start: $0.onset, beatTicks: $0.beatTicks, beatsPerBar: $0.beatsPerBar) },
            end: answer.end
        )
        barNumbers = answer.bars.map { (start: $0.onset, number: $0.number) }
        hands = HandsHeld(answer.hands)
        barBeats = BarBeats.read(answer.beats)
        tempo = TempoReading.read(answer.tempo, first: answer.tempos.first)
    }

    /// Every answer `Answer.Playback` writes, in its text (the reference's sounds); nil for a refusal or an answer that
    /// cannot be read.
    static func read(_ text: String) -> PlaybackScore? {
        FacadeAnswer.decode(PlaybackAnswer.self, text, of: "playback").map(PlaybackScore.init)
    }
}
