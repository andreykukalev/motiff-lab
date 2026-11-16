import XCTest

@testable import MotiffLab

/// The metronome's rules (#521 section 15): it follows the piece tempo until the player gives it one of its own, a slide
/// after the hold sets that tempo, and what is kept for a piece on the device says which of the two it is.
final class MetronomeTests: XCTestCase {
    func testASlideAfterTheHoldSetsTheTempoUpOrRightFasterWithinThirtyToTwoHundredForty() {
        XCTAssertEqual(MetronomeTempo.dragged(from: 80, by: CGSize(width: 0, height: -40), step: 4), 90, "up is faster")
        XCTAssertEqual(MetronomeTempo.dragged(from: 80, by: CGSize(width: 20, height: 0), step: 4), 85, "right is faster")
        XCTAssertEqual(MetronomeTempo.dragged(from: 80, by: CGSize(width: -8, height: 12), step: 4), 75, "down and left are slower")
        XCTAssertEqual(MetronomeTempo.dragged(from: 80, by: CGSize(width: 0, height: 400), step: 4), 30)
        XCTAssertEqual(MetronomeTempo.dragged(from: 200, by: CGSize(width: 400, height: 0), step: 4), 240)
        XCTAssertEqual(MetronomeTempo.dragged(from: 80, by: .zero, step: 4), 80, "a hold without a slide keeps the tempo")
        XCTAssertEqual(MetronomeTempo.holdSeconds, 0.55, "the web app's long press")
    }

    func testWhatIsKeptForAPieceSaysWhetherTheMetronomeFollowsThePieceTempo() {
        XCTAssertNil(MetronomeTempo.kept("-,3")?.own)
        XCTAssertEqual(MetronomeTempo.kept("-,3")?.beats, 3)
        XCTAssertEqual(MetronomeTempo.kept("100,5")?.own, 100)
        XCTAssertEqual(MetronomeTempo.kept("100,5")?.beats, 4, "beats not offered are four")
        XCTAssertEqual(MetronomeTempo.kept("500,4")?.own, 240)
        XCTAssertNil(MetronomeTempo.kept("x,4"))
        XCTAssertNil(MetronomeTempo.kept(nil))
        XCTAssertEqual(MetronomeTempo.keptText(own: nil, beats: 2), "-,2")
        XCTAssertEqual(MetronomeTempo.keptText(own: 96, beats: 4), "96,4")
    }

    @MainActor
    func testTheMetronomeFollowsThePieceTempoNotThePaceUntilThePlayerGivesItOne() throws {
        let name = "motiff-metronome-follow-test"
        let store = try XCTUnwrap(UserDefaults(suiteName: name))
        store.removePersistentDomain(forName: name)
        defer { store.removePersistentDomain(forName: name) }

        let metronome = Metronome(store: store)
        metronome.load(piece: "p", tempo: 96, note: "♩.", beats: 2)
        XCTAssertEqual(metronome.bpm, 96, "the piece tempo, as the score's mark shows it")
        XCTAssertNil(metronome.own)
        XCTAssertEqual(metronome.beats, 2, "the first bar's beats")
        XCTAssertEqual(metronome.note, "♩.")

        metronome.change(bpm: 100, beats: 2)
        XCTAssertEqual(metronome.own, 100)
        metronome.load(piece: "p", tempo: 120)
        XCTAssertEqual(metronome.bpm, 100, "the player's own tempo stays when the piece tempo changes")

        metronome.change(bpm: 120, beats: 2)
        XCTAssertNil(metronome.own, "set back to the piece tempo, it follows the piece again")
        XCTAssertEqual(store.string(forKey: "motiff.pdf.metronome.p"), "-,2")
        metronome.load(piece: "p", tempo: 90)
        XCTAssertEqual(metronome.bpm, 90)

        metronome.load(piece: "p")
        XCTAssertEqual(metronome.bpm, 90, "the PDF view of the same piece keeps its piece tempo")
        metronome.load(piece: "q")
        XCTAssertEqual(metronome.bpm, 80, "another piece with no tempo is at the default")
        XCTAssertEqual(metronome.beats, 4)
        XCTAssertEqual(metronome.note, "♩")
    }
}
