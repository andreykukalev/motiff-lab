import Foundation

// The playback engine for the audio thread (decision record 0019 section 2.1): the Swift port of the web app's engine
// (src/MotiffLab.Web/Scripts/playback/playback-processor.ts, 0007), so both apps sound and keep time alike. It owns
// musical time: it advances a canonical tick position frame by frame through the tempo map, so loops wrap on the exact
// frame, tempo changes apply at the exact tick, and the main thread cannot delay a note. It renders the sampled piano
// and a synthesised metronome. The pedal is already in the notes' lengths: music-core's playback score holds a note
// for as long as the pedal holds it.
//
// It has no AVFoundation dependency, so the tests run it directly; `Player` runs it inside an AVAudioSourceNode.
// It is not thread-safe: `Player` hands it messages on the audio thread, before each block.

let ticksPerQuarter = 480.0

enum TransportState: String, Sendable {
    case stopped
    case countingIn
    case playing
}

/// A sample of the piano: the note it was recorded at, its tuning correction, and mono PCM from just before its attack.
struct SampleRegion: Sendable {
    var pitch: Int
    var tuneCents: Double
    var data: [Float]
    var sampleRate: Double
}

/// A note of a playback score, in canonical ticks.
struct PlaybackNote: Sendable, Equatable {
    var onset: Double
    var duration: Double
    var pitch: Int
    var velocity: Int
    var track: Int = 0
}

/// The playback score's time: tempos, beats and bars, from music-core (`playback`).
struct PlaybackTimeline: Sendable {
    struct Tempo: Sendable { var at: Double; var microsecondsPerQuarter: Double }
    struct Beat: Sendable { var at: Double; var downbeat: Bool }
    struct Bar: Sendable { var start: Double; var beatTicks: Double; var beatsPerBar: Double }

    /// The first tempo is at tick 0.
    var tempos: [Tempo]
    var beats: [Beat]
    var bars: [Bar]
    var end: Double
}

/// One pass of an alternating loop: the sources that play and the tick range [start, end) they loop over.
struct LoopPass: Sendable {
    var ids: [String]
    var start: Double
    var end: Double
}

enum EngineMessage: Sendable {
    case samples([SampleRegion])
    case timeline(PlaybackTimeline)
    case source(id: String, notes: [PlaybackNote])
    case removeSource(String)
    case activeSources([String])
    case play
    case pause
    case seek(Double)
    case rate(Double)
    /// A loop over a range; `once` plays it through once and stops at its end, back at its start (the circle's scale).
    case loop(start: Double, end: Double, once: Bool = false)
    case loopOff
    case alternate([LoopPass])
    case metronome(Bool)
    case countIn(Int)
    case tracks(muted: Set<Int>, soloed: Set<Int>)
    case noteOn(pitch: Int, velocity: Int)
    case noteOff(pitch: Int)
    /// A playhead dragged while stopped (#495 UX-09): the position goes from one tick to the other, and the notes whose starts
    /// it passed sound, forward or back.
    case scrub(from: Double, to: Double)
    /// The drag let go: the notes it sounded fade.
    case scrubEnd
}

struct PositionReport: Sendable, Equatable {
    /// Canonical tick at the start of `frame`.
    var tick: Double
    var state: TransportState
    /// The frame the tick belongs to, counted from the engine's first block; -1 when reported between blocks.
    var frame: Int
    /// Ticks per second at the current tempo and rate; 0 when not playing.
    var ticksPerSecond: Double
    var loopStart: Double
    var loopEnd: Double
    /// The pass of an alternating loop that is playing, from 0; -1 when the engine is not alternating.
    var pass: Int
}

enum EngineReport: Sendable, Equatable {
    case position(PositionReport)
    case ended(tick: Double)
}

final class PlaybackEngine {
    static let minRate = 0.25
    static let maxRate = 1.5
    static let maxVoices = 96
    /// Notes the player strikes by hand: they sound at once, over playback or without it, until released.
    static let liveSource = "live"
    private static let liveTrack = -1

    private static let attackFrames = 32.0
    private static let releaseSeconds = 0.12
    private static let clickSeconds = 0.05
    private static let silence: Float = 1e-4
    private static let masterGain: Float = 0.55
    /// Rounding slack, in frames, so an event exactly on a frame is not pushed to the next one by float error.
    private static let frameEpsilon = 1e-6

    let sampleRate: Double
    var reportInterval: Int

    private var regionByPitch = [SampleRegion?](repeating: nil, count: 128)
    private var stepByPitch = [Double](repeating: 0, count: 128)
    private var sources: [String: Source] = [:]
    private var active: [String] = []
    private var timeline: Timeline?

    private(set) var transportState = TransportState.stopped
    private var position = 0.0
    private var rate = 1.0
    private var loopStart = 0.0
    private var loopEnd = 0.0
    /// The loop plays its range once and stops at its end, back at its start, rather than going round.
    private var loopOnce = false
    private var passes: [LoopPass]?
    private var pass = -1
    private var metronome = false
    private var countInBars = 0
    private var muted = Set<Int>()
    private var soloed = Set<Int>()

    private var beatCursor = 0
    private var countInTotal = 0
    private var countInDone = 0
    /// How far the count-in has gone, in beats: each block's frames count at the rate then, so a tempo changed while it
    /// counts applies to the next click (#511 section 17, as the web's engine).
    private var countInBeats = 0.0
    private var countInBeatTicks = 0.0
    private var countInBeatsPerBar = 0

    private var voices: [Voice] = []
    private var clicks: [Click] = []
    private var voiceOrder = 0
    private let releaseFactor: Float
    private var framesSinceReport = 0
    private var blockFrame: Int?
    private var blockOffset = 0
    private var reports: [EngineReport] = []

    init(sampleRate: Double) {
        self.sampleRate = sampleRate
        reportInterval = Int((sampleRate / 30).rounded())
        releaseFactor = Float(exp(-1 / (Self.releaseSeconds * sampleRate)))
        reports.reserveCapacity(16)
        voices.reserveCapacity(Self.maxVoices + 1)
    }

    var tick: Double { position }
    var voiceCount: Int { voices.count }

    /// Reports since the last call: positions about 30 times a second, state changes, the end.
    func takeReports() -> [EngineReport] {
        let taken = reports
        reports.removeAll(keepingCapacity: true)
        return taken
    }

    func handle(_ message: EngineMessage) {
        switch message {
        case .samples(let regions):
            setSamples(regions)
        case .timeline(let timeline):
            setTimeline(timeline)
        case .source(let id, let notes):
            setSource(id, notes)
        case .removeSource(let id):
            sources[id] = nil
            active.removeAll { $0 == id }
            release(at: 0) { $0.sourceId == id }
        case .activeSources(let ids):
            stopAlternating()
            setActive(ids)
        case .play:
            play()
        case .pause:
            pause()
        case .seek(let tick):
            seek(tick)
        case .rate(let value):
            rate = min(Self.maxRate, max(Self.minRate, value))
            requestReport()
        case .loop(let start, let end, let once):
            stopAlternating()
            setLoop(start, end)
            loopOnce = once && hasLoop
        case .loopOff:
            stopAlternating()
            loopOnce = false
            loopStart = 0
            loopEnd = 0
            requestReport()
        case .alternate(let passes):
            alternate(passes)
        case .metronome(let on):
            metronome = on
        case .countIn(let bars):
            countInBars = max(0, bars)
        case .tracks(let muted, let soloed):
            self.muted = muted
            self.soloed = soloed
            release(at: 0) { $0.sourceId != Self.liveSource && !self.isAudible($0.track) }
        case .noteOn(let pitch, let velocity):
            if (0..<128).contains(pitch) {
                startVoice(Self.liveSource, pitch, min(127, max(1, velocity)), Self.liveTrack, .infinity, 0)
            }
        case .noteOff(let pitch):
            release(at: 0) { $0.sourceId == Self.liveSource && $0.pitch == pitch }
        case .scrub(let from, let to):
            scrub(from: from, to: to)
        case .scrubEnd:
            release(at: 0) { $0.sourceId == Self.scrubSource }
        }
    }

    /// Renders one block into `left` (and copies it to `right`), overwriting them. Reports are stamped with
    /// `blockFrame` plus the offset in the block.
    func render(_ left: UnsafeMutablePointer<Float>, _ right: UnsafeMutablePointer<Float>?, frames: Int, blockFrame: Int) {
        var done = 0
        self.blockFrame = blockFrame
        while done < frames {
            blockOffset = done
            switch transportState {
            case .playing: done += advancePlaying(done, frames - done)
            case .countingIn: done += advanceCountIn(done, frames - done)
            case .stopped: done = frames
            }
        }

        left.update(repeating: 0, count: frames)
        renderVoices(left, frames)
        renderClicks(left, frames)
        for i in 0..<frames {
            left[i] *= Self.masterGain
        }
        right?.update(from: left, count: frames)
        self.blockFrame = nil

        framesSinceReport += frames
        if framesSinceReport >= reportInterval {
            framesSinceReport -= reportInterval
            report(frame: blockFrame + frames)
        }
    }

    /// Renders `frames` frames in blocks of `block` and returns them: for tests.
    func render(frames: Int, block: Int = 128, startFrame: Int = 0) -> [Float] {
        var out = [Float](repeating: 0, count: frames)
        var left = [Float](repeating: 0, count: block)
        var at = 0
        while at < frames {
            let count = min(block, frames - at)
            left.withUnsafeMutableBufferPointer { render($0.baseAddress!, nil, frames: count, blockFrame: startFrame + at) }
            out.replaceSubrange(at..<(at + count), with: left[0..<count])
            at += count
        }
        return out
    }

    // MARK: - Messages

    private func setSamples(_ regions: [SampleRegion]) {
        for pitch in 0..<128 {
            var best: SampleRegion?
            for region in regions where best == nil || abs(region.pitch - pitch) < abs(best!.pitch - pitch) {
                best = region
            }
            regionByPitch[pitch] = best
            if let best {
                // The sample's own rate against the output's: a sample decoded at 44.1 kHz plays in tune at 48 kHz.
                stepByPitch[pitch] = pow(2, (Double(pitch - best.pitch) * 100 + best.tuneCents) / 1200) * best.sampleRate / sampleRate
            } else {
                stepByPitch[pitch] = 0
            }
        }
    }

    private func setTimeline(_ message: PlaybackTimeline) {
        var timeline = Timeline(
            tempoTicks: message.tempos.map { $0.at },
            tempoUspq: message.tempos.map { $0.microsecondsPerQuarter },
            beatTicks: message.beats.map { $0.at },
            beatDown: message.beats.map { $0.downbeat },
            barStart: message.bars.map { $0.start },
            barBeatTicks: message.bars.map { $0.beatTicks },
            barBeats: message.bars.map { $0.beatsPerBar },
            end: message.end
        )
        if timeline.tempoTicks.isEmpty {
            timeline.tempoTicks = [0]
            timeline.tempoUspq = [500_000]
        }

        self.timeline = timeline
        stopAlternating()
        release(at: 0) { _ in true }
        transportState = .stopped
        position = 0
        loopStart = 0
        loopEnd = 0
        loopOnce = false
        relocate(chase: false)
        requestReport()
    }

    private func setSource(_ id: String, _ notes: [PlaybackNote]) {
        let ordered = notes.sorted { $0.onset < $1.onset }
        let source = Source(notes: ordered, cursor: firstAtOrAfter(ordered.map { $0.onset }, position))
        sources[id] = source
        release(at: 0) { $0.sourceId == id }
        if transportState == .playing && active.contains(id) {
            chase(id)
        }
    }

    /// A/B switching and layering: the position is kept, and notes already sounding in a newly active source are struck.
    private func setActive(_ ids: [String]) {
        let next = ids.filter { sources[$0] != nil }
        let added = next.filter { !active.contains($0) }
        active = next
        release(at: 0) { $0.sourceId != Self.liveSource && !next.contains($0.sourceId) }
        for id in added {
            sources[id]!.cursor = firstAtOrAfter(sources[id]!.onsets, position)
            if transportState == .playing {
                chase(id)
            }
        }
    }

    private func play() {
        guard let timeline, transportState == .stopped else { return }
        // What a drag sounded gives way to the piece: playback starts clean.
        release(at: 0) { $0.sourceId == Self.scrubSource }

        if hasLoop && (position < loopStart || position >= loopEnd) {
            position = loopStart
        } else if position >= timeline.end {
            position = 0
        }
        relocate(chase: false)

        if countInBars > 0 {
            let bar = max(0, lastAtOrBefore(timeline.barStart, position))
            let beatTicks = timeline.barBeatTicks.isEmpty ? ticksPerQuarter : timeline.barBeatTicks[bar]
            let beatsPerBar = timeline.barBeats.isEmpty ? 4 : Int(timeline.barBeats[bar])
            countInBeatsPerBar = max(1, beatsPerBar)
            countInTotal = countInBeatsPerBar * countInBars
            countInDone = 0
            countInBeats = 0
            countInBeatTicks = beatTicks
            transportState = .countingIn
        } else {
            startPlaying()
        }
        requestReport()
    }

    private func startPlaying() {
        transportState = .playing
        for id in active {
            chase(id)
        }
    }

    private func pause() {
        guard transportState != .stopped else { return }
        transportState = .stopped
        release(at: 0) { $0.sourceId != Self.liveSource }
        requestReport()
    }

    private func seek(_ tick: Double) {
        position = min(max(0, tick), timeline?.end ?? 0)
        release(at: 0) { $0.sourceId != Self.liveSource }
        relocate(chase: transportState == .playing)
        requestReport()
    }

    private func setLoop(_ start: Double, _ end: Double) {
        let limit = timeline?.end ?? 0
        let from = min(max(0, start), limit)
        let to = min(max(0, end), limit)
        if to <= from {
            loopStart = 0
            loopEnd = 0
        } else {
            loopStart = from
            loopEnd = to
        }
        requestReport()
    }

    /// Starts an alternating loop at its first pass: that pass's sources play over its range, then the next pass
    /// starts on the same frame. A loop, a loop off, a choice of sources or a new timeline ends it.
    private func alternate(_ given: [LoopPass]) {
        let limit = timeline?.end ?? 0
        let kept = given
            .map { LoopPass(ids: $0.ids, start: min(max(0, $0.start), limit), end: min(max(0, $0.end), limit)) }
            .filter { $0.end > $0.start }
        guard let first = kept.first else {
            stopAlternating()
            loopStart = 0
            loopEnd = 0
            requestReport()
            return
        }

        loopOnce = false
        passes = kept
        pass = 0
        active = first.ids.filter { sources[$0] != nil }
        loopStart = first.start
        loopEnd = first.end
        seek(first.start)
    }

    private func stopAlternating() {
        passes = nil
        pass = -1
    }

    // MARK: - Time

    private var hasLoop: Bool { loopEnd > loopStart }

    private func isAudible(_ track: Int) -> Bool {
        !muted.contains(track) && (soloed.isEmpty || soloed.contains(track))
    }

    private func tempoIndex(_ tick: Double) -> Int {
        max(0, lastAtOrBefore(timeline!.tempoTicks, tick))
    }

    private func ticksPerFrame(_ tempoIndex: Int) -> Double {
        ticksPerQuarter * rate * 1e6 / (timeline!.tempoUspq[tempoIndex] * sampleRate)
    }

    /// Points every cursor at the position; with chase, strikes notes already sounding there.
    private func relocate(chase strike: Bool) {
        for id in sources.keys {
            sources[id]!.cursor = firstAtOrAfter(sources[id]!.onsets, position)
            if strike && active.contains(id) {
                chase(id)
            }
        }
        beatCursor = timeline.map { firstAtOrAfter($0.beatTicks, position) } ?? 0
    }

    /// Strikes the notes of a source that began before the position and still sound, so a switch or seek is not silent.
    private func chase(_ id: String) {
        guard let source = sources[id] else { return }
        for note in source.notes[0..<source.cursor] {
            let end = note.onset + note.duration
            if note.onset < position && end > position && isAudible(note.track) {
                startVoice(id, note.pitch, note.velocity, note.track, end, 0)
            }
        }
    }

    /// The count-in's clicks for up to `available` frames, each beat as long as the tempo and rate say now; then the music.
    private func advanceCountIn(_ offset: Int, _ available: Int) -> Int {
        let framesPerBeat = countInBeatTicks / ticksPerFrame(tempoIndex(position))
        while countInDone < countInTotal {
            let at = max(0, Int(((Double(countInDone) - countInBeats) * framesPerBeat - Self.frameEpsilon).rounded(.up)))
            if at >= available { break }
            startClick(offset + at, accent: countInDone % countInBeatsPerBar == 0)
            countInDone += 1
        }

        let remaining = (Double(countInTotal) - countInBeats) * framesPerBeat
        let used = max(0, Int((remaining - Self.frameEpsilon).rounded(.up)))
        if used < available {
            countInBeats = Double(countInTotal)
            startPlaying()
            blockOffset = offset + used
            requestReport()
            return used
        }

        countInBeats += Double(available) / framesPerBeat
        return available
    }

    /// Plays up to `available` frames, stopping early at a tempo change, the loop end or the end of the piece.
    private func advancePlaying(_ offset: Int, _ available: Int) -> Int {
        let timeline = timeline!
        let tempo = tempoIndex(position)
        let perFrame = ticksPerFrame(tempo)
        let nextTempo = tempo + 1 < timeline.tempoTicks.count ? timeline.tempoTicks[tempo + 1] : .infinity
        let looping = hasLoop && position < loopEnd
        let stop = looping ? loopEnd : timeline.end
        let boundary = min(nextTempo, stop)

        let toBoundary = framesUntil(boundary, perFrame)
        let frames = min(available, toBoundary)
        let until = position + Double(frames) * perFrame
        let limit = min(until, boundary)

        scheduleNotes(offset, perFrame, limit)
        scheduleBeats(offset, perFrame, limit)
        scheduleReleases(offset, perFrame, until)

        if frames < toBoundary {
            position = until
            return frames
        }

        // The block reached the boundary; carry the overshoot (under one frame) across it.
        let overshoot = max(0, until - boundary)
        if boundary == nextTempo && boundary < stop {
            position = boundary + overshoot * (ticksPerFrame(tempo + 1) / perFrame)
        } else if looping && !loopOnce {
            release(at: offset + frames) { $0.sourceId != Self.liveSource }
            let alternating = passes != nil
            if let passes {
                pass = (pass + 1) % passes.count
                let next = passes[pass]
                active = next.ids.filter { sources[$0] != nil }
                loopStart = next.start
                loopEnd = next.end
            }
            position = loopStart
            relocate(chase: false)
            position += overshoot * (ticksPerFrame(tempoIndex(loopStart)) / perFrame)
            if alternating {
                blockOffset = offset + frames
                requestReport()
            }
        } else {
            transportState = .stopped
            reports.append(.ended(tick: timeline.end))
            position = hasLoop ? loopStart : 0
            relocate(chase: false)
            blockOffset = offset + frames
            requestReport()
        }
        return frames
    }

    /// Whole frames from the position to the first frame at or after a tick.
    private func framesUntil(_ tick: Double, _ perFrame: Double) -> Int {
        if tick == .infinity { return Int.max }
        return max(0, Int(((tick - position) / perFrame - Self.frameEpsilon).rounded(.up)))
    }

    private func scheduleNotes(_ offset: Int, _ perFrame: Double, _ limit: Double) {
        for id in active {
            guard var source = sources[id] else { continue }
            while source.cursor < source.notes.count && source.notes[source.cursor].onset < limit {
                let note = source.notes[source.cursor]
                source.cursor += 1
                if !isAudible(note.track) { continue }
                let frame = offset + framesUntil(note.onset, perFrame)
                startVoice(id, note.pitch, note.velocity, note.track, note.onset + note.duration, frame)
            }
            sources[id] = source
        }
    }

    private func scheduleBeats(_ offset: Int, _ perFrame: Double, _ limit: Double) {
        guard let timeline else { return }
        while beatCursor < timeline.beatTicks.count && timeline.beatTicks[beatCursor] < limit {
            let i = beatCursor
            beatCursor += 1
            if metronome {
                startClick(offset + framesUntil(timeline.beatTicks[i], perFrame), accent: timeline.beatDown[i])
            }
        }
    }

    private func scheduleReleases(_ offset: Int, _ perFrame: Double, _ until: Double) {
        for i in voices.indices where !voices[i].released && voices[i].endTick < until {
            let frame = offset + framesUntil(voices[i].endTick, perFrame)
            voices[i].releaseAt = max(frame, voices[i].startAt)
            voices[i].released = true
        }
    }

    // MARK: - Voices

    /// Starts a voice at a frame of the block. It is released at `endTick` while playing, or, with `hold`, that many frames
    /// after it starts, counted down block by block whether or not anything plays (a drag's notes).
    private func startVoice(_ sourceId: String, _ pitch: Int, _ velocity: Int, _ track: Int, _ endTick: Double, _ frame: Int, hold: Int = .max) {
        guard let region = regionByPitch[pitch] else { return }

        // A key struck again is damped first, as on a piano.
        release(at: frame) { $0.sourceId == sourceId && $0.pitch == pitch }

        if voices.count >= Self.maxVoices {
            stealVoice()
        }

        let loudness = Float(velocity) / 127
        voices.append(Voice(
            sourceId: sourceId, pitch: pitch, track: track, data: region.data, read: 0, step: stepByPitch[pitch],
            gain: 0.08 + 0.92 * loudness * loudness, level: 1, startAt: frame, releaseAt: hold == .max ? .max : frame + hold, released: false,
            endTick: endTick, order: voiceOrder
        ))
        voiceOrder += 1
    }

    /// Frees a voice for a new note: the oldest released one, else the oldest.
    private func stealVoice() {
        var victim = 0
        for i in 1..<voices.count {
            let a = voices[i]
            let b = voices[victim]
            if a.released != b.released ? a.released : a.order < b.order {
                victim = i
            }
        }
        voices.remove(at: victim)
    }

    private func release(at frame: Int, where predicate: (Voice) -> Bool) {
        for i in voices.indices where predicate(voices[i]) && voices[i].releaseAt > frame {
            voices[i].releaseAt = max(frame, voices[i].startAt)
            voices[i].released = true
        }
    }

    private func startClick(_ frame: Int, accent: Bool) {
        clicks.append(Click(startAt: frame, age: 0, frequency: accent ? 1760 : 1320, gain: accent ? 0.5 : 0.3))
    }

    private func renderVoices(_ out: UnsafeMutablePointer<Float>, _ frames: Int) {
        let release = releaseFactor
        var kept = 0
        for v in voices.indices {
            var voice = voices[v]
            let last = voice.data.count - 1
            var read = voice.read
            var level = voice.level
            var alive = true
            voice.data.withUnsafeBufferPointer { data in
                var i = voice.startAt
                while i < frames {
                    if i >= voice.releaseAt {
                        level *= release
                        if level < Self.silence {
                            alive = false
                            break
                        }
                    }
                    let index = Int(read)
                    if index >= last {
                        alive = false
                        break
                    }
                    let fraction = Float(read - Double(index))
                    let sample = data[index] + (data[index + 1] - data[index]) * fraction
                    let attack: Float = read < Self.attackFrames ? Float(read / Self.attackFrames) : 1
                    out[i] += sample * voice.gain * level * attack
                    read += voice.step
                    i += 1
                }
            }

            if alive {
                voice.read = read
                voice.level = level
                voice.startAt = 0
                voice.releaseAt = voice.releaseAt == .max ? .max : max(0, voice.releaseAt - frames)
                voices[kept] = voice
                kept += 1
            }
        }
        voices.removeSubrange(kept...)
    }

    private func renderClicks(_ out: UnsafeMutablePointer<Float>, _ frames: Int) {
        let length = Int((Self.clickSeconds * sampleRate).rounded())
        let decay = 1 / (0.012 * sampleRate)
        var kept = 0
        for c in clicks.indices {
            var click = clicks[c]
            let omega = 2 * Double.pi * click.frequency / sampleRate
            var age = click.age
            var i = click.startAt
            while i < frames && age < length {
                out[i] += Float(sin(omega * Double(age)) * exp(-Double(age) * decay) * click.gain)
                i += 1
                age += 1
            }
            if age < length {
                click.age = age
                click.startAt = 0
                clicks[kept] = click
                kept += 1
            }
        }
        clicks.removeSubrange(kept...)
    }

    // MARK: - Reports

    /// Reports the transport now: inside a block, at the frame it has reached; between blocks, with frame -1.
    private func requestReport() {
        report(frame: blockFrame.map { $0 + blockOffset } ?? -1)
    }

    private func report(frame: Int) {
        let playing = transportState == .playing && timeline != nil
        let perSecond = playing ? ticksPerFrame(tempoIndex(position)) * sampleRate : 0
        reports.append(.position(PositionReport(
            tick: position, state: transportState, frame: frame, ticksPerSecond: perSecond,
            loopStart: loopStart, loopEnd: loopEnd, pass: pass
        )))
    }

    // MARK: - Storage

    private struct Timeline {
        var tempoTicks: [Double]
        var tempoUspq: [Double]
        var beatTicks: [Double]
        var beatDown: [Bool]
        var barStart: [Double]
        var barBeatTicks: [Double]
        var barBeats: [Double]
        var end: Double
    }

    private struct Source {
        var notes: [PlaybackNote]
        var cursor: Int
        var onsets: [Double] { notes.map { $0.onset } }
    }

    private struct Voice {
        var sourceId: String
        var pitch: Int
        var track: Int
        var data: [Float]
        var read: Double
        var step: Double
        var gain: Float
        var level: Float
        /// Frame in the current block where the voice starts (0 once it has started).
        var startAt: Int
        /// Frame in the current block where the release starts, or Int.max.
        var releaseAt: Int
        var released: Bool
        var endTick: Double
        var order: Int
    }

    private struct Click {
        var startAt: Int
        var age: Int
        var frequency: Double
        var gain: Double
    }
}

/// Scrubbing (#495 UX-09, the architect's design playback-model-v0.5.2, slice I4): the notes a dragged playhead passes.
extension PlaybackEngine {
    /// The drag's voices, apart from the piece's, so its end, Play or a seek fades them all.
    static let scrubSource = "scrub"
    /// One move strikes at most this many starts, those nearest where it went: a fast drag never floods the voices.
    static let scrubGroups = 6
    /// A note a drag strikes sounds as long as it lasts at the tempo, never longer than this, in seconds, then fades.
    static let scrubSeconds = 1.5

    /// A playhead dragged while stopped (#495 UX-09): the position goes to `to` without striking what sounds there (a seek
    /// would) and without fading what the drag already sounds; the starts of the active sources' audible notes it passed
    /// sound, forward those in (from, to], back those in [to, from), each start's notes together, as a chord. A move that
    /// passes more starts than `scrubGroups` strikes only those nearest `to`. Each note sounds its length at the tempo, at
    /// most `scrubSeconds`, then fades as a released note does. The metronome stays silent. Ignored while playing.
    fileprivate func scrub(from: Double, to: Double) {
        guard let timeline, transportState == .stopped else { return }
        let start = min(max(0, from), timeline.end)
        let target = min(max(0, to), timeline.end)
        position = target
        relocate(chase: false)
        requestReport()
        // Forward the start itself is behind and the place reached is passed; back the other way round.
        let passed = { (onset: Double) in target > start ? onset > start && onset <= target : onset >= target && onset < start }
        var chords: [Double: [PlaybackNote]] = [:]
        for id in active {
            for note in sources[id]?.notes ?? [] where passed(note.onset) && isAudible(note.track) {
                chords[note.onset, default: []].append(note)
            }
        }
        let longest = Int(Self.scrubSeconds * sampleRate)
        for onset in chords.keys.sorted(by: { abs($0 - target) < abs($1 - target) }).prefix(Self.scrubGroups) {
            for note in chords[onset] ?? [] {
                let frames = Int((note.duration / ticksPerFrame(tempoIndex(note.onset))).rounded())
                startVoice(Self.scrubSource, note.pitch, note.velocity, note.track, .infinity, 0, hold: min(longest, max(1, frames)))
            }
        }
    }
}

/// Index of the last value at or before x in an ascending array, or -1.
func lastAtOrBefore(_ values: [Double], _ x: Double) -> Int {
    var lo = 0
    var hi = values.count - 1
    var found = -1
    while lo <= hi {
        let mid = (lo + hi) / 2
        if values[mid] <= x {
            found = mid
            lo = mid + 1
        } else {
            hi = mid - 1
        }
    }
    return found
}

/// Index of the first value at or after x in an ascending array, or the count.
func firstAtOrAfter(_ values: [Double], _ x: Double) -> Int {
    var lo = 0
    var hi = values.count
    while lo < hi {
        let mid = (lo + hi) / 2
        if values[mid] < x {
            lo = mid + 1
        } else {
            hi = mid
        }
    }
    return lo
}
