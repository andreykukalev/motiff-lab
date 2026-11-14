import XCTest

@testable import MotiffLab

/// A tempo changed while playing (version 0.5.4, #511 section 17), held to the web engine's own tests
/// (tests/web/playback-processor.test.mjs): the place heard stays, nothing restarts, the next note comes at the new rate,
/// the loop stays, and the count-in counts each click at the tempo then. The helpers are the engine tests' own.
extension PlaybackEngineTests {
    /// The engine's last position report, after `takeReports` was emptied before the step.
    private func lastPosition(_ engine: PlaybackEngine) -> PositionReport? {
        engine.takeReports().compactMap { report -> PositionReport? in
            if case .position(let position) = report { return position }
            return nil
        }.last
    }

    @objc func testATempoChangedWhilePlayingGoesOnFromThePlaceHeardTheNextNoteAtTheNewRate() {
        // #511 section 17, as the web's engine: no restart, the place kept, the next onset where the new rate puts it.
        let e = engine()
        source(e, "a", [(0, 240), (480, 240), (960, 240), (1440, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.play)
        let first = e.render(frames: frames(Double(quarter) * 1.5))
        let at = e.tick
        _ = e.takeReports()

        e.handle(.rate(0.5))

        XCTAssertEqual(e.tick, at)
        XCTAssertEqual(e.transportState, .playing)
        let report = lastPosition(e)
        XCTAssertEqual(report?.tick, at)
        XCTAssertEqual(report?.ticksPerSecond ?? 0, 480, accuracy: 1e-9)
        let rest = e.render(frames: frames(Double(quarter * 5)), startFrame: first.count)
        // At 120 BPM a tick is 50 frames; at half the rate 100.
        let next = first.count + Int(((960 - at) * 100).rounded())
        XCTAssertEqual(noteStarts(first), [0, quarter])
        XCTAssertEqual(noteStarts(rest).map { $0 + first.count }, [next, next + quarter * 2])
    }

    @objc func testATempoChangedWhileLoopingKeepsTheLoopAndPlaysRoundItAtTheNewRate() {
        let e = engine()
        source(e, "a", [(0, 240), (480, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.loop(start: 0, end: 960))
        e.handle(.play)
        let first = e.render(frames: frames(Double(quarter) * 1.5))
        let at = e.tick
        _ = e.takeReports()

        e.handle(.rate(1.5))

        let report = lastPosition(e)
        XCTAssertEqual(report?.tick, at)
        XCTAssertEqual(report?.loopStart, 0)
        XCTAssertEqual(report?.loopEnd, 960)
        let rest = e.render(frames: frames(Double(quarter * 2)), startFrame: first.count)
        let round = first.count + Int(((960 - at) * 50 / 1.5).rounded())
        XCTAssertEqual(Array(noteStarts(rest).map { $0 + first.count }.prefix(2)), [round, round + Int((Double(quarter) / 1.5).rounded())])
        XCTAssertEqual(e.transportState, .playing)
    }

    @objc func testTheCountInCountsEachClickAtTheTempoThenSoATempoChangedWhileItCountsAppliesToTheNextClick() {
        let e = engine()
        source(e, "a", [(0, 240)])
        e.handle(.activeSources(["a"]))
        e.handle(.countIn(1))
        e.handle(.play)
        let first = e.render(frames: frames(Double(quarter) * 1.5))
        // Half a beat in at full speed, then half the rate: the rest of the beat lasts twice as long.
        e.handle(.rate(0.5))
        let rest = e.render(frames: frames(Double(quarter * 8)), startFrame: first.count)

        let second = first.count + (quarter * 2 - first.count) * 2
        let clicks = (onsets(first) + onsets(rest).map { $0 + first.count }).filter { $0 < second + quarter * 3 }
        XCTAssertEqual(clicks, [1, quarter + 1, second + 1, second + quarter * 2 + 1])
        XCTAssertEqual(e.transportState, .playing)
        let note = rest.indices.first { $0 + first.count > second + quarter * 4 - 10 && abs(rest[$0]) > 0.01 }
        XCTAssertEqual(note.map { $0 + first.count - spike }, second + quarter * 4)
    }
}
