import XCTest

@testable import MotiffLab

/// Where the playhead is heard between the engine's reports (v1.4 r-1b): on from the last one while it plays, but never
/// far past it once the audio has stopped (the app in the background, the screen locked, a call).
final class PlayerHeardTests: XCTestCase {
    private func playing(at tick: Double, loop: Range<Double>? = nil) -> PositionReport {
        PositionReport(tick: tick, state: .playing, frame: 0, ticksPerSecond: 960, loopStart: loop?.lowerBound ?? 0, loopEnd: loop?.upperBound ?? 0, pass: -1)
    }

    func testBetweenReportsThePlayheadMovesOnAtItsSpeed() {
        XCTAssertEqual(Player.heard(playing(at: 1000), after: 0.1), 1096, accuracy: 0.001)
        XCTAssertEqual(Player.heard(playing(at: 1000), after: -0.05), 1000, "a report not heard yet holds its tick")
    }

    func testAReportLongAgoMeansTheAudioStoppedAndThePlayheadWaitsNearIt() {
        // Ten seconds in the background at 960 ticks a second ran the playhead 9600 ticks on, past the piece's end.
        XCTAssertEqual(Player.heard(playing(at: 1000), after: 10), 1000 + Player.heardAhead * 960, accuracy: 0.001)
    }

    func testInsideALoopThePlayheadWrapsToItsStart() {
        XCTAssertEqual(Player.heard(playing(at: 1900, loop: 0..<1920), after: 0.1), 76, accuracy: 0.001)
    }

    func testStoppedThePlayheadIsTheReportsTick() {
        var stopped = playing(at: 1000)
        stopped.state = .stopped
        XCTAssertEqual(Player.heard(stopped, after: 5), 1000)
    }
}
