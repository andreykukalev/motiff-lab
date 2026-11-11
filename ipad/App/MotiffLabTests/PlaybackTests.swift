import AVFoundation
import XCTest

@testable import MotiffLab

/// The playback engine (decision record 0019 section 2.1), held to the web engine's own tests
/// (tests/web/playback-processor.test.mjs): the same notes start on the same frames.
final class PlaybackEngineTests: XCTestCase {
    private let rate = 48_000.0
    private let quarter = 24_000 // frames per quarter note at 120 BPM
    private let spike = 40 // the test sample is silent but for one spike, this many frames after its start

    /// One sample at middle C: silence with a single spike, so every note shows as one spike in the output.
    private func testRegion() -> SampleRegion {
        var data = [Float](repeating: 0, count: Int(rate))
        data[spike] = 1
        return SampleRegion(pitch: 60, tuneCents: 0, data: data, sampleRate: rate)
    }

    private func engine(
        tempos: [PlaybackTimeline.Tempo] = [.init(at: 0, microsecondsPerQuarter: 500_000)],
        beats: [PlaybackTimeline.Beat] = [],
        bars: [PlaybackTimeline.Bar] = [.init(start: 0, beatTicks: 480, beatsPerBar: 4)],
        end: Double = 1920 * 4,
        samples: Bool = true
    ) -> PlaybackEngine {
        let engine = PlaybackEngine(sampleRate: rate)
        if samples {
            engine.handle(.samples([testRegion()]))
        }
        engine.handle(.timeline(PlaybackTimeline(tempos: tempos, beats: beats, bars: bars, end: end)))
        _ = engine.takeReports()
        return engine
    }

    /// Notes as (onset, duration, track) at middle C, full velocity.
    private func source(_ engine: PlaybackEngine, _ id: String, _ notes: [(Double, Double, Int)]) {
        engine.handle(.source(id: id, notes: notes.map { PlaybackNote(onset: $0.0, duration: $0.1, pitch: 60, velocity: 127, track: $0.2) }))
    }

    private func source(_ engine: PlaybackEngine, _ id: String, _ notes: [(Double, Double)]) {
        source(engine, id, notes.map { ($0.0, $0.1, 0) })
    }

    /// Frames where a note's spike lands, given as the note's start frame.
    private func noteStarts(_ out: [Float]) -> [Int] {
        out.indices.filter { abs(out[$0]) > 0.01 }.map { $0 - spike }
    }

    /// Frames where a sound starts after at least `gap` frames of silence (for metronome clicks).
    private func onsets(_ out: [Float], gap: Int = 1000) -> [Int] {
        var found: [Int] = []
        var silent = gap
        for (i, value) in out.enumerated() {
            if abs(value) > 1e-6 {
                if silent >= gap { found.append(i) }
                silent = 0
            } else {
                silent += 1
            }
        }
        return found
    }

    private func frames(_ n: Double) -> Int { Int((n / 128).rounded(.up)) * 128 }

    func testNotesStartOnTheirExactFrame() {
        let e = engine()
        source(e, "a", [(0, 240), (480, 240), (720, 120), (1000, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.play)

        let out = e.render(frames: frames(Double(quarter * 3)))

        // Tick 1000 is 1000/480 quarters: 50 000 frames.
        XCTAssertEqual(noteStarts(out), [0, quarter, quarter * 3 / 2, 50_000])
    }

    func testTempoScalesFrom25To150PercentAndIsClampedToThatRange() {
        let q = Double(quarter)
        let cases: [(Double, Double)] = [(0.5, q * 2), (1.5, q / 1.5), (0.25, q * 4), (3, q / 1.5), (0.1, q * 4)]
        for (value, expected) in cases {
            let e = engine()
            source(e, "a", [(480, 240)])
            e.handle(.activeSources(["a"]))
            e.handle(.rate(value))
            e.handle(.play)

            let out = e.render(frames: frames(Double(quarter * 4 + 1000)))

            XCTAssertEqual(noteStarts(out), [Int(expected.rounded(.up))], "rate \(value)")
        }
    }

    func testTempoChangesApplyAtTheirTick() {
        // 120 BPM, then 240 BPM from beat 2: tick 960 is one quarter at 120 and one at 240.
        let e = engine(tempos: [.init(at: 0, microsecondsPerQuarter: 500_000), .init(at: 480, microsecondsPerQuarter: 250_000)])
        source(e, "a", [(960, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.play)

        XCTAssertEqual(noteStarts(e.render(frames: frames(Double(quarter * 2)))), [quarter + quarter / 2])
    }

    func testALoopWrapsOnTheExactFrameWithNoGap() {
        let e = engine()
        source(e, "a", [(0, 240), (240, 240), (480, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.loop(start: 0, end: 480))
        e.handle(.play)

        let out = e.render(frames: frames(Double(quarter * 3 + 100)))

        let half = quarter / 2
        XCTAssertEqual(noteStarts(out), [0, half, quarter, quarter + half, quarter * 2, quarter * 2 + half, quarter * 3])
    }

    func testALoopPlayedOnceStopsAtItsEndBackAtItsStart() {
        // The circle's scale (testing, 2026-11-06): once through, then still, ready to play again from its start.
        let e = engine()
        source(e, "a", [(480, 240), (720, 240), (960, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.loop(start: 480, end: 960, once: true))
        e.handle(.seek(480))
        e.handle(.play)

        let out = e.render(frames: frames(Double(quarter * 3)))

        XCTAssertEqual(noteStarts(out), [0, quarter / 2])
        XCTAssertEqual(e.transportState, .stopped)
        XCTAssertEqual(e.tick, 480)
    }

    func testALoopNotOnABlockBoundaryKeepsExactTime() {
        // A loop of 100 ticks: 5000 frames, not a multiple of 128.
        let e = engine()
        source(e, "a", [(1000, 50)])
        e.handle(.activeSources(["a"]))
        e.handle(.loop(start: 1000, end: 1100))
        e.handle(.seek(1000))
        e.handle(.play)

        XCTAssertEqual(noteStarts(e.render(frames: frames(5000 * 4 + 100))), [0, 5000, 10_000, 15_000, 20_000])
    }

    func testTheMetronomeClicksEveryBeat() {
        let beats: [PlaybackTimeline.Beat] = [.init(at: 0, downbeat: true), .init(at: 480, downbeat: false), .init(at: 960, downbeat: false), .init(at: 1440, downbeat: false), .init(at: 1920, downbeat: true)]
        let e = engine(beats: beats, samples: false)
        e.handle(.metronome(true))
        e.handle(.play)

        let out = e.render(frames: frames(Double(quarter * 4 + 1000)))

        // A click is a sine starting at phase 0, so it is first heard one frame after its beat.
        XCTAssertEqual(onsets(out), [1, quarter + 1, quarter * 2 + 1, quarter * 3 + 1, quarter * 4 + 1])
    }

    func testCountInPlaysOneBarOfClicksThenTheMusic() {
        let e = engine(bars: [.init(start: 0, beatTicks: 480, beatsPerBar: 3), .init(start: 1440, beatTicks: 480, beatsPerBar: 3)])
        source(e, "a", [(1440, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.countIn(1))
        e.handle(.seek(1440))
        e.handle(.play)
        XCTAssertEqual(e.transportState, .countingIn)

        let out = e.render(frames: frames(Double(quarter * 3 + 1000)))

        XCTAssertEqual(onsets(out).filter { $0 < quarter * 3 }, [1, quarter + 1, quarter * 2 + 1])
        XCTAssertEqual(e.transportState, .playing)
        let note = out.indices.first { $0 > quarter * 3 && abs(out[$0]) > 0.01 }
        XCTAssertEqual(note.map { $0 - spike }, quarter * 3)
    }

    func testMuteAndSoloPerTrack() {
        let cases: [([Int], [Int], [Int])] = [([1], [], [0]), ([], [1], [quarter]), ([], [], [0, quarter]), ([1], [1], [])]
        for (muted, soloed, expected) in cases {
            let e = engine()
            source(e, "a", [(0, 240, 0), (480, 240, 1)])
            e.handle(.activeSources(["a"]))
            e.handle(.tracks(muted: Set(muted), soloed: Set(soloed)))
            e.handle(.play)

            XCTAssertEqual(noteStarts(e.render(frames: frames(Double(quarter * 2)))), expected, "muted \(muted), soloed \(soloed)")
        }
    }

    func testSwitchingSourceKeepsTheMusicalPosition() {
        let e = engine()
        source(e, "original", [(0, 240), (960, 240)])
        source(e, "variation", [(480, 240), (1440, 240)])
        e.handle(.activeSources(["original"]))
        e.handle(.play)

        let first = e.render(frames: frames(Double(quarter) * 1.5))
        let before = e.tick
        e.handle(.activeSources(["variation"]))
        XCTAssertEqual(e.tick, before)
        let second = e.render(frames: frames(Double(quarter) * 2.5), startFrame: first.count)

        XCTAssertEqual(noteStarts(first), [0])
        XCTAssertEqual(noteStarts(second).map { $0 + first.count }, [quarter * 3])
    }

    func testPauseKeepsThePositionAndPlayResumesFromIt() {
        let e = engine()
        source(e, "a", [(960, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.play)
        _ = e.render(frames: frames(Double(quarter)))
        e.handle(.pause)
        let paused = e.tick
        _ = e.render(frames: frames(Double(quarter)))
        XCTAssertEqual(e.tick, paused)

        e.handle(.play)
        let out = e.render(frames: frames(Double(quarter * 2)))

        XCTAssertEqual(noteStarts(out), [Int(((960 - paused) * Double(quarter) / 480).rounded(.up))])
    }

    /// One bar looping, a short note on each beat: what the playhead moved on the roll plays (v0.2).
    private func loopedBar(rate: Double = 1) -> PlaybackEngine {
        let e = engine()
        source(e, "a", [(0, 120), (480, 120), (960, 120), (1440, 120)])
        e.handle(.activeSources(["a"]))
        e.handle(.rate(rate))
        e.handle(.loop(start: 0, end: 1920))
        return e
    }

    func testThePlayheadDraggedWhilePlayingPlaysOnFromThereKeepingTheLoopAndTheTempo() {
        let e = loopedBar(rate: 0.5)
        e.handle(.play)
        let before = e.render(frames: frames(Double(quarter) / 2))
        e.handle(.seek(1200))

        let out = e.render(frames: frames(Double(quarter) * 5.5), startFrame: before.count)

        // At half the tempo a quarter takes two: the beat at 1440, the loop's start again after its end, then its second beat.
        XCTAssertEqual(noteStarts(out), [quarter, quarter * 3, quarter * 5])
        XCTAssertEqual(e.transportState, .playing)
    }

    func testThePlayheadMovedWhilePausedWaitsThereAndPlayGoesOnFromItNotFromTheLoopsStart() {
        let e = loopedBar()
        e.handle(.play)
        let before = e.render(frames: frames(Double(quarter) * 1.5))
        e.handle(.pause)
        e.handle(.seek(1300))
        _ = e.render(frames: frames(Double(quarter)), startFrame: before.count)
        XCTAssertEqual(e.tick, 1300)
        XCTAssertEqual(e.transportState, .stopped)

        e.handle(.play)
        let out = e.render(frames: 60_000)

        // 140 ticks to the beat at 1440, then 480 to the loop's end and its start again, then its second beat.
        XCTAssertEqual(noteStarts(out), [7_000, 31_000, 55_000])
    }

    func testPausedInsideALoopPlayResumesWhereItPausedNotAtTheLoopsStart() {
        let e = loopedBar()
        e.handle(.play)
        _ = e.render(frames: frames(Double(quarter) * 1.25))
        e.handle(.pause)
        let paused = e.tick
        XCTAssertGreaterThan(paused, 480)

        e.handle(.play)
        let starts = noteStarts(e.render(frames: frames(Double(quarter))))

        // The next beat, 960, from where it paused (to the frame), not the loop's start at 0.
        XCTAssertEqual(starts.count, 1)
        XCTAssertEqual(Double(starts.first ?? 0), (960 - paused) * Double(quarter) / 480, accuracy: 1)
    }

    func testPlaybackStopsAtTheEndAndReportsIt() {
        let e = engine(end: 960)
        source(e, "a", [(0, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.play)

        _ = e.render(frames: frames(Double(quarter * 3)))

        XCTAssertEqual(e.transportState, .stopped)
        XCTAssertEqual(e.tick, 0)
        XCTAssertTrue(e.takeReports().contains(.ended(tick: 960)))
    }

    func testPositionsAreReportedAbout30TimesASecondWithTheirFrame() {
        let e = engine()
        e.handle(.source(id: "a", notes: []))
        e.handle(.activeSources(["a"]))
        e.handle(.play)
        _ = e.takeReports()

        _ = e.render(frames: frames(rate))

        let positions = e.takeReports().compactMap { report -> PositionReport? in
            if case .position(let position) = report { return position }
            return nil
        }
        XCTAssertTrue((29...31).contains(positions.count), "\(positions.count) reports")
        for position in positions {
            XCTAssertEqual(position.state, .playing)
            XCTAssertEqual(position.tick, Double(position.frame) * 480 / Double(quarter), accuracy: 1e-6)
            XCTAssertEqual(position.ticksPerSecond, 960, accuracy: 1e-9)
        }
    }

    func testTheVoiceCountStaysBounded() {
        let e = engine()
        source(e, "a", (0..<400).map { (Double($0), 1920.0) })
        e.handle(.activeSources(["a"]))
        e.handle(.play)

        _ = e.render(frames: frames(Double(quarter)))

        XCTAssertLessThanOrEqual(e.voiceCount, PlaybackEngine.maxVoices)
    }

    func testALiveNoteSoundsAtOnceAndStopsWhenReleased() {
        let e = engine()
        e.handle(.noteOn(pitch: 60, velocity: 100))
        XCTAssertEqual(noteStarts(e.render(frames: 128)), [0])
        e.handle(.noteOff(pitch: 60))
        let after = e.render(frames: 128 * 200)
        XCTAssertTrue(after[(128 * 100)...].allSatisfy { abs($0) < 1e-3 }, "a released note fades out")
    }

    func testASampleAtAnotherRateStillPlaysInTune() {
        // A sample decoded at 44.1 kHz read at 48 kHz: its own pitch steps 44.1/48 of a frame each frame.
        let e = PlaybackEngine(sampleRate: rate)
        var data = [Float](repeating: 0, count: 44_100)
        data[441] = 1
        e.handle(.samples([SampleRegion(pitch: 60, tuneCents: 0, data: data, sampleRate: 44_100)]))
        e.handle(.noteOn(pitch: 60, velocity: 127))
        let out = e.render(frames: 1024)
        // 441 sample frames at 44.1 kHz are 10 ms: 480 output frames, spread over the two frames either side.
        let loudest = out.indices.max { abs(out[$0]) < abs(out[$1]) }
        XCTAssertEqual(Double(loudest ?? 0), 480, accuracy: 1)
    }
}

/// The piano, the playback score from music-core, and the project files.
final class PianoAndProjectTests: XCTestCase {
    func testThePianoIsTheWebAppsThirtySamples() throws {
        let regions = try Piano.load()
        XCTAssertEqual(regions.count, 30)
        XCTAssertEqual(regions.first?.pitch, 21)
        XCTAssertEqual(regions.last?.pitch, 108)
        for region in regions {
            XCTAssertGreaterThan(region.data.count, Int(region.sampleRate), "sample \(region.pitch) is over a second long")
            // Trimmed to the attack: the sound starts within its first 5 ms.
            let peak = region.data.map(abs).max() ?? 0
            let start = region.data.firstIndex { abs($0) > peak * 0.003 } ?? .max
            XCTAssertLessThan(start, Int(region.sampleRate * 0.005), "sample \(region.pitch)")
        }
    }

    func testTheExamplesPlaybackScoreComesFromMusicCore() async throws {
        let example = try XCTUnwrap(KitPiece.example)
        let answer = await MusicCore.run { () -> String in
            let opened = facadeObject(MusicCore.open(fileName: example.fileName, data: example.data))
            let piece = (opened?["piece"] as? NSNumber)?.int32Value ?? 0
            defer { MusicCore.close(piece: piece) }
            return MusicCore.call(piece: piece, "playback")
        }
        let score = try XCTUnwrap(PlaybackScore.read(answer), answer)
        XCTAssertGreaterThan(score.notes.count, 600)
        XCTAssertFalse(score.timeline.tempos.isEmpty)
        XCTAssertFalse(score.timeline.bars.isEmpty)
        XCTAssertGreaterThan(score.timeline.end, 0)
        XCTAssertTrue(score.notes.allSatisfy { (21...108).contains($0.pitch) && $0.duration > 0 })
    }

    func testAPieceSavedAsAProjectFileOpensAgainAsItself() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }

        let example = try XCTUnwrap(KitPiece.example)
        let first = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        let url = ProjectFiles.newURL(for: "Für Elise", in: folder)
        XCTAssertEqual(url.lastPathComponent, "Für Elise.motiff.json")
        try await ProjectFiles.save(piece: first.id, to: url)
        _ = await MusicCore.run { MusicCore.close(piece: first.id) }

        // The next piece of the same title gets its own file.
        XCTAssertEqual(ProjectFiles.newURL(for: "Für Elise", in: folder).lastPathComponent, "Für Elise 2.motiff.json")
        XCTAssertEqual(ProjectFiles.newURL(for: "a/b: c", in: folder).lastPathComponent, "a b  c.motiff.json")

        let listed = ProjectFiles.list(in: folder)
        XCTAssertEqual(listed.map(\.title), ["Für Elise"])
        XCTAssertTrue(ProjectFiles.isInFolder(url, folder: folder))

        let again = try await OpenPiece.open(fileName: url.lastPathComponent, data: Data(contentsOf: url), language: "en").get()
        defer { _ = MusicCore.close(piece: again.id) }
        XCTAssertEqual(again.title, "Für Elise")
        XCTAssertEqual(again.pages.count, first.pages.count)
        let file = try XCTUnwrap(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        XCTAssertEqual(file["version"] as? Int, 15)
    }
}

/// Where the playhead may go when the player moves it on the roll (v0.2): inside the loop, before its end.
final class PlayheadPlaceTests: XCTestCase {
    func testThePlayheadStaysInsideTheLoopBeforeItsEnd() {
        let loop = 1920.0..<3840.0
        XCTAssertEqual(Player.place(2400, inside: loop), 2400)
        XCTAssertEqual(Player.place(100, inside: loop), 1920)
        XCTAssertEqual(Player.place(9000, inside: loop), 3839, "never on the loop's end, where playing would run past it")
    }

    func testWithNoLoopThePlayheadGoesAnywhereFromTheStart() {
        XCTAssertEqual(Player.place(-5, inside: nil), 0)
        XCTAssertEqual(Player.place(5000, inside: nil), 5000)
        XCTAssertEqual(Player.place(700, inside: 960.0..<960.0), 700, "an empty loop is no loop")
    }
}
