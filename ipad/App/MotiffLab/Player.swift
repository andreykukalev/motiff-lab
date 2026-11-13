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
    @Published var tempoPercent = 100 { didSet { send(.rate(Double(tempoPercent) / 100)) } }
    @Published var metronome = false { didSet { send(.metronome(metronome)) } }
    /// One bar of clicks before the music, as the web app's Count-in.
    @Published var countIn = false { didSet { send(.countIn(countIn ? 1 : 0)) } }

    static let tempoChoices = [25, 50, 75, 90, 100, 110, 125, 150]

    /// Which hands sound (#328, an experiment): the piece's notes as music-core gives them for both hands or for one
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

    private let audio = AVAudioEngine()
    private var renderer: Renderer?
    private var last: Renderer.Stamped?
    private var polling: Task<Void, Never>?

    /// Starts the audio, loads the piano and the piece's playback score from music-core.
    func load(piece: Int32) async {
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

            let piano = Task.detached(priority: .userInitiated) { try Piano.load() }
            let answer = await MusicCore.run { MusicCore.call(piece: piece, "playback") }
            guard let score = PlaybackScore.read(answer) else {
                throw PlayerError(message: facadeObject(answer)?["message"] as? String ?? "music-core gave no playback score.")
            }
            renderer.send(.samples(try await piano.value))
            renderer.send(.timeline(score.timeline))
            barNumbers = score.barNumbers
            beatLines = score.timeline.beats.map(\.at)
            barBeats = score.barBeats
            handsHeld = score.hands
            self.piece = piece
            renderer.send(.source(id: "piece", notes: score.notes))
            renderer.send(.activeSources(["piece"]))
            ready = true
            startPolling()
        } catch {
            problem = (error as? PlayerError)?.message ?? error.localizedDescription
        }
    }

    func toggle() {
        if state == .stopped { transportKept = true }
        send(state == .stopped ? .play : .pause)
    }

    /// Whether the pill stays after playback stops (testing, 2026-11-06: it went away on pause, and Count-in and the
    /// tempo with it); a tap on the page with nothing selected puts it away.
    @Published var transportKept = false

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

    /// The playhead moved on the roll (v0.2). Dragged or nudged it only moves: playing, the music goes on from there; paused,
    /// it waits there, and Play goes on from there. A tap (`play`) plays from there, looping `selection` first when nothing
    /// loops yet. It stays inside the loop, which keeps its span, as the tempo does.
    func move(to tick: Double, play: Bool, selection: Range<Double>?) {
        if play, loop == nil, let selection {
            setLoop(selection, from: Self.place(tick, inside: selection))
            return
        }
        send(.seek(Self.place(tick, inside: loop)))
        // Playing already, the engine plays on; Play is only for when it was still.
        if play { send(.play) }
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

    // MARK: - The playback model (version 0.5.2, #495 UX-10; Settings · Experimental, «Два указателя и режимы»)

    /// The selection as the range it plays (the model's S), the player's copy of the score's: the player owns it and Цикл,
    /// and the views only read them (PlaybackModel).
    @Published private(set) var selection: Range<Double>?

    /// Цикл: the selection loops while it is on, and Play keeps the position inside it. It turns on by itself when a
    /// selection appears, and is kept while there is none.
    @Published private(set) var cycle = true

    /// The piece's beats as bar.beat, from music-core's playback score: the pill's and the roll's words for a place.
    private(set) var barBeats = BarBeats()

    /// The position as bar.beat, "16.2"; empty before the score is read.
    var placeLabel: String {
        barBeats.at(tick)?.text ?? ""
    }

    /// The model's state now: the selection and Цикл are the player's, the position and the transport the engine's.
    var model: PlaybackModel.State {
        PlaybackModel.State(selection: selection, loop: cycle, tick: tick, transport: state)
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
        perform(decided.steps)
        return decided.state
    }

    /// The score's selection as it is now, after focus or with the experiment turned on: the model takes it (T9–T11), and
    /// the engine loops what the model says again, in place of the loop another owner, such as focus, gave it.
    func hold(selection range: Range<Double>?) {
        apply(.select(range))
        let wanted = PlaybackModel.engineLoop(model)
        if wanted != loop { applyLoop(wanted) }
    }

    /// A tap on a beat, a bar or the whole-piece strip (T12): the position goes there, playing on from there when it plays,
    /// and Цикл turns off when it leaves the selection; `play` starts it from there.
    func go(to at: Double, play: Bool) {
        apply(.seek(at))
        if play && state == .stopped {
            perform([.play])
        }
    }

    /// The model's steps to the engine; its loop goes through applyLoop, so `loop` says what the engine loops.
    private func perform(_ steps: [TransportStep]) {
        for step in steps {
            switch step {
            case .pause:
                send(.pause)
            case .play:
                transportKept = true
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
        let request = Self.handsRequest(hands)
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "playback", request) }
        guard let score = PlaybackScore.read(answer) else { return }
        barNumbers = score.barNumbers
        beatLines = score.timeline.beats.map(\.at)
        barBeats = score.barBeats
        handsHeld = score.hands
        send(.timeline(score.timeline))
        send(.source(id: Self.pieceSource, notes: score.notes))
    }

    /// The piece's notes for the hands chosen, in place of those that sounded: what plays or loops carries on (#328).
    private func applyHands() async {
        guard let piece else { return }
        let chosen = hands
        let request = Self.handsRequest(chosen)
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "playback", request) }
        // Another choice came while music-core answered: the newer one sends its own notes.
        guard chosen == hands, let score = PlaybackScore.read(answer) else { return }
        send(.source(id: Self.pieceSource, notes: score.notes))
    }

    /// The `playback` request for the hands chosen: nothing for both, as before the experiment.
    nonisolated static func handsRequest(_ hands: PlayerHands) -> String? {
        hands == .both ? nil : "{\"hands\": \"\(hands.rawValue)\"}"
    }

    /// Stops the sound and the audio engine; the screen calls this when it closes.
    func shutdown() {
        // A playhead held as the screen closes is let go where it is: what the drag sounds fades, the pause ends the rest.
        if drag != nil { send(.scrubEnd) }
        drag = nil
        polling?.cancel()
        polling = nil
        send(.pause)
        audio.stop()
        ready = false
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

    static func read(_ text: String) -> PlaybackScore? {
        guard let object = facadeObject(text), let end = number(object["end"]) else { return nil }
        let items = { (key: String) in object[key] as? [[String: Any]] ?? [] }
        let notes = items("notes").compactMap { note -> PlaybackNote? in
            guard let onset = number(note["onset"]), let duration = number(note["duration"]),
                  let pitch = number(note["pitch"]), let velocity = number(note["velocity"]) else { return nil }
            return PlaybackNote(onset: onset, duration: duration, pitch: Int(pitch), velocity: Int(velocity), track: Int(number(note["track"]) ?? 0))
        }
        let tempos = items("tempos").compactMap { tempo -> PlaybackTimeline.Tempo? in
            guard let at = number(tempo["at"]), let uspq = number(tempo["microsecondsPerQuarter"]) else { return nil }
            return .init(at: at, microsecondsPerQuarter: uspq)
        }
        let beats = items("beats").compactMap { beat -> PlaybackTimeline.Beat? in
            guard let at = number(beat["at"]) else { return nil }
            return .init(at: at, downbeat: beat["downbeat"] as? Bool ?? false)
        }
        let bars = items("bars").compactMap { bar -> PlaybackTimeline.Bar? in
            guard let start = number(bar["onset"]), let beatTicks = number(bar["beatTicks"]),
                  let beatsPerBar = number(bar["beatsPerBar"]) else { return nil }
            return .init(start: start, beatTicks: beatTicks, beatsPerBar: beatsPerBar)
        }
        let numbers = items("bars").compactMap { bar -> (start: Double, number: Int)? in
            guard let start = number(bar["onset"]), let printed = number(bar["number"]) else { return nil }
            return (start, Int(printed))
        }
        let timeline = PlaybackTimeline(tempos: tempos, beats: beats, bars: bars, end: end)
        return PlaybackScore(notes: notes, timeline: timeline, barNumbers: numbers, hands: HandsHeld(answer: object), barBeats: BarBeats.read(items("beats")))
    }

    private static func number(_ value: Any?) -> Double? {
        (value as? NSNumber)?.doubleValue
    }
}

/// The piano the web app plays (Salamander Grand Piano V3 by Alexander Holm, CC BY 3.0, tools/piano): its 30 MP3
/// samples and `piano.json`, copied into the app from the web app's files.
enum Piano {
    /// The attack starts where the sample first rises above this share of its peak; encoders add silence before it.
    private static let onsetThreshold: Float = 0.003
    private static let onsetLeadSeconds = 0.002

    static func load(bundle: Bundle = .main) throws -> [SampleRegion] {
        guard let folder = bundle.url(forResource: "piano", withExtension: nil),
              let manifest = try JSONSerialization.jsonObject(with: Data(contentsOf: folder.appendingPathComponent("piano.json"))) as? [String: Any],
              let samples = manifest["samples"] as? [[String: Any]], !samples.isEmpty
        else { throw PlayerError(message: "The piano samples are missing from the app.") }

        return try samples.map { sample in
            guard let file = sample["file"] as? String, let pitch = (sample["pitch"] as? NSNumber)?.intValue else {
                throw PlayerError(message: "piano.json lists a sample without its file or note.")
            }
            let audio = try AVAudioFile(forReading: folder.appendingPathComponent(file))
            let format = audio.processingFormat
            guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(audio.length)) else {
                throw PlayerError(message: "The piano sample \(file) could not be read.")
            }
            try audio.read(into: buffer)
            return SampleRegion(
                pitch: pitch,
                tuneCents: (sample["tuneCents"] as? NSNumber)?.doubleValue ?? 0,
                data: trimmedToOnset(buffer),
                sampleRate: format.sampleRate
            )
        }
    }

    /// A mono copy of the decoded sample from just before its attack, so every note starts on time.
    static func trimmedToOnset(_ buffer: AVAudioPCMBuffer) -> [Float] {
        guard let channels = buffer.floatChannelData else { return [] }
        let count = Int(buffer.frameLength)
        let channelCount = Int(buffer.format.channelCount)
        var peak: Float = 0
        for c in 0..<channelCount {
            for i in 0..<count { peak = max(peak, abs(channels[c][i])) }
        }

        let threshold = peak * onsetThreshold
        var onset = 0
        search: for i in 0..<count {
            for c in 0..<channelCount where abs(channels[c][i]) > threshold {
                onset = i
                break search
            }
        }

        let start = max(0, onset - Int((onsetLeadSeconds * buffer.format.sampleRate).rounded()))
        var mono = [Float](repeating: 0, count: count - start)
        for c in 0..<channelCount {
            for i in 0..<mono.count { mono[i] += channels[c][start + i] / Float(channelCount) }
        }
        return mono
    }
}
