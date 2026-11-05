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
    @Published private(set) var ready = false
    @Published private(set) var problem: String?
    @Published var tempoPercent = 100 { didSet { send(.rate(Double(tempoPercent) / 100)) } }
    @Published var metronome = false { didSet { send(.metronome(metronome)) } }
    /// One bar of clicks before the music, as the web app's Count-in.
    @Published var countIn = false { didSet { send(.countIn(countIn ? 1 : 0)) } }

    static let tempoChoices = [25, 50, 75, 90, 100, 110, 125, 150]

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
            renderer.send(.source(id: "piece", notes: score.notes))
            renderer.send(.activeSources(["piece"]))
            ready = true
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

    /// Plays from a place in the piece, as a note tapped on the score asks: moves there, and starts if it was still.
    func play(from tick: Double) {
        send(.seek(tick))
        if state == .stopped { send(.play) }
    }

    /// The passage that loops, in ticks; nil when nothing loops.
    @Published private(set) var loop: Range<Double>?

    /// Loops a passage and plays it from its start, or stops looping (0018 section 7: a selection loops the selection).
    func setLoop(_ range: Range<Double>?) {
        loop = range
        // Alternating is a loop of its own: another loop, or none, ends it and the original sounds on.
        if hearing == .alternate {
            hearing = .original
            send(.activeSources([Self.pieceSource]))
        }
        guard let range else {
            send(.loopOff)
            return
        }
        send(.loop(start: range.lowerBound, end: range.upperBound))
        if !range.contains(tick) { send(.seek(range.lowerBound)) }
        if state == .stopped { send(.play) }
    }

    /// What sounds (0018 section 7, Original / Variation / Alternate): the piece, the piece with the variation being tried
    /// in place, or the two looping one after the other.
    enum Hearing: Equatable {
        case original, variation, alternate
    }

    @Published private(set) var hearing = Hearing.original
    /// Whether a variation is there to hear.
    @Published private(set) var hasVariation = false

    /// The piece with the variation in place (music-core's `playback` of `inPlace`), or nil when the variation is left:
    /// the original sounds again, looping where it looped.
    func setVariation(_ notes: [PlaybackNote]?) {
        guard let notes else {
            guard hasVariation else { return }
            hasVariation = false
            send(.activeSources([Self.pieceSource]))
            send(.removeSource(Self.variationSource))
            if hearing == .alternate, let loop { send(.loop(start: loop.lowerBound, end: loop.upperBound)) }
            hearing = .original
            return
        }
        send(.source(id: Self.variationSource, notes: notes))
        hasVariation = true
    }

    /// Hears the original, the variation, or the two one after the other, looping `range` and playing from its start
    /// when it was still or elsewhere.
    func hear(_ next: Hearing, over range: Range<Double>) {
        guard next == .original || hasVariation else { return }
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

    /// Stops the sound and the audio engine; the screen calls this when it closes.
    func shutdown() {
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
            state = position.state
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
        return PlaybackScore(notes: notes, timeline: PlaybackTimeline(tempos: tempos, beats: beats, bars: bars, end: end))
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
