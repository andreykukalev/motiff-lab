import XCTest

@testable import MotiffLab

/// A tempo changed while playing (version 0.5.4, #511 section 17), held to the web engine's own tests
/// (tests/web/playback-processor.test.mjs): the place heard stays, nothing restarts, the next note comes at the new rate,
/// and the loop stays. The helpers are the engine tests' own.
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
}
