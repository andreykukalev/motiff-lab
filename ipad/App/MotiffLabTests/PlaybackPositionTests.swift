import XCTest

@testable import MotiffLab

/// The one playback position of version 0.5.2 (#479 items 19, 22 and 25; product spec two-playheads): inside the
/// selection or not, what the engine loops, when Цикл turns on, what each Play does, the pill's state and bar.beat.
final class PlaybackPositionTests: XCTestCase {
    private let selection = 960.0..<1920.0

    func testInsideIsFromTheSelectionsStartUpToNotAtItsEnd() {
        XCTAssertFalse(PlaybackPosition.inside(959, selection))
        XCTAssertTrue(PlaybackPosition.inside(960, selection))
        XCTAssertTrue(PlaybackPosition.inside(1919, selection))
        XCTAssertFalse(PlaybackPosition.inside(1920, selection))
        XCTAssertFalse(PlaybackPosition.inside(1000, nil))
    }

    func testTheEngineLoopsTheSelectionOnlyWhileCycleIsOn() {
        XCTAssertEqual(PlaybackPosition.loop(selection: selection, cycle: true), selection)
        XCTAssertNil(PlaybackPosition.loop(selection: selection, cycle: false))
        XCTAssertNil(PlaybackPosition.loop(selection: nil, cycle: true))
    }

    func testCycleTurnsOnWhenASelectionAppearsAndKeepsThePlayersChoiceOtherwise() {
        XCTAssertTrue(PlaybackPosition.cycle(after: nil, selection, was: false), "a new selection loops")
        XCTAssertFalse(PlaybackPosition.cycle(after: selection, 960..<2400, was: false), "a changed selection keeps Цикл off")
        XCTAssertTrue(PlaybackPosition.cycle(after: selection, nil, was: true))
    }

    func testTheCobaltPlayPausesInsidePlaysFromThePositionInsideAndElseFromTheStart() {
        XCTAssertEqual(PlaybackPosition.playSelection(selection, at: 1200, playing: true), [.pause])
        XCTAssertEqual(PlaybackPosition.playSelection(selection, at: 1200, playing: false), [.play])
        XCTAssertEqual(PlaybackPosition.playSelection(selection, at: 480, playing: false), [.seek(960), .play])
        XCTAssertEqual(PlaybackPosition.playSelection(selection, at: 2400, playing: true), [.seek(960)], "playing on, it jumps in")
    }

    func testTheInkPlayPlaysThePieceAndInsideTheSelectionTurnsCycleOffToPlayOnPastIt() {
        XCTAssertEqual(PlaybackPosition.playPiece(selection: nil, at: 0, playing: false, cycle: true).steps, [.play])
        XCTAssertEqual(PlaybackPosition.playPiece(selection: nil, at: 0, playing: true, cycle: true).steps, [.pause])

        let inside = PlaybackPosition.playPiece(selection: selection, at: 1200, playing: true, cycle: true)
        XCTAssertEqual(inside.steps, [.loopOff])
        XCTAssertFalse(inside.cycle)
        XCTAssertEqual(PlaybackPosition.playPiece(selection: selection, at: 1200, playing: false, cycle: false).steps, [.play])

        XCTAssertEqual(PlaybackPosition.playPiece(selection: selection, at: 2400, playing: true, cycle: true).steps, [.pause])
    }

    func testFromBeforeTheSelectionWithCycleOnTheEnginePlaysFirstThenLoops() {
        let before = PlaybackPosition.playPiece(selection: selection, at: 480, playing: false, cycle: true)
        XCTAssertEqual(before.steps, [.loopOff, .play, .loop(selection)], "its Play would jump into a loop it already had")
        XCTAssertTrue(before.cycle)
        XCTAssertEqual(PlaybackPosition.playPiece(selection: selection, at: 480, playing: false, cycle: false).steps, [.play])
    }

    func testThePillWithoutASelectionHasOnlyTheInkPlay() {
        let stopped = TransportPill.of(position: "16.2", span: nil, inside: false, playing: false, cycle: true)
        XCTAssertNil(stopped.selection)
        XCTAssertEqual(stopped.piece, .init(icon: .play, filled: true, label: .from("16.2")))
        let playing = TransportPill.of(position: "16.2", span: nil, inside: false, playing: true, cycle: true)
        XCTAssertEqual(playing.piece, .init(icon: .pause, filled: true, label: .pauseAt("16.2")))
    }

    func testThePillWithASelectionShowsItsSpanCycleAndTheInkPlayInItsRing() {
        let pill = TransportPill.of(position: "8.2", span: (first: "9.1", last: "12.3"), inside: false, playing: false, cycle: true)
        XCTAssertEqual(pill.selection, TransportPill.SelectionButton(playing: false, label: .span(first: "9.1", last: "12.3")))
        XCTAssertTrue(pill.cycle)
        XCTAssertEqual(pill.piece, .init(icon: .playOutline, filled: false, label: .from("8.2")))

        let outside = TransportPill.of(position: "8.2", span: (first: "9.1", last: "12.3"), inside: false, playing: true, cycle: true)
        XCTAssertEqual(outside.selection?.playing, false)
        XCTAssertEqual(outside.piece, .init(icon: .pause, filled: true, label: .pauseAt("8.2")))
    }

    func testInsideWhilePlayingTheCobaltPlayHasThePositionAndTheInkOneStays() {
        let pill = TransportPill.of(position: "10.3", span: (first: "9.1", last: "12.3"), inside: true, playing: true, cycle: true)
        XCTAssertEqual(pill.selection, TransportPill.SelectionButton(playing: true, label: .inside(position: "10.3", first: "9.1", last: "12.3")))
        XCTAssertEqual(pill.piece, .init(icon: .playOutline, filled: false, label: .from("10.3")))
    }

    func testBarBeatIsTheBeatThePositionLiesInWrittenBarDotBeat() {
        let beats = BarBeats.read([
            ["at": 0, "bar": 0, "beat": 3],
            ["at": 240, "bar": 1, "beat": 1],
            ["at": 480, "bar": 1, "beat": 2],
            ["at": 720, "bar": 1, "beat": 3],
            ["at": 960, "bar": 2, "beat": 1],
        ])
        XCTAssertEqual(beats.at(0)?.text, "0.3", "the pickup's beat as a selection counts it")
        XCTAssertEqual(beats.at(500)?.text, "1.2")
        XCTAssertEqual(beats.at(960)?.text, "2.1")
        XCTAssertEqual(beats.at(-5)?.text, "0.3")
        XCTAssertEqual(beats.span(240..<960)?.first, "1.1")
        XCTAssertEqual(beats.span(240..<960)?.last, "1.3", "the last beat that starts inside")
        XCTAssertNil(BarBeats().at(100))
        XCTAssertTrue(BarBeats.read([["at": 0]]).marks.isEmpty, "an older answer without bar and beat")
    }
}
