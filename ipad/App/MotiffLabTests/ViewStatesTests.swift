import XCTest
@testable import MotiffLab

/// PDF | Notes keep each view as the player left it (#511 section 10): the places are the web app's
/// (tests/web/view-anchor.test.mjs, the same cases), each view is kept by the piece on the device, a view kept wins over the
/// landing on the same stretch of music, and a piece the library closed is not saved again by its screen.
final class ViewStatesTests: XCTestCase {
    /// Three systems of 200 points with 40 between them, starting at bars of 0, 3840 and 7680 ticks.
    private let systems = [
        AnchorSpan(key: 0, top: 100, bottom: 300),
        AnchorSpan(key: 3840, top: 340, bottom: 540),
        AnchorSpan(key: 7680, top: 580, bottom: 780),
    ]

    private var defaults: UserDefaults!

    override func setUpWithError() throws {
        defaults = try XCTUnwrap(UserDefaults(suiteName: "ViewStatesTests"))
        defaults.removePersistentDomain(forName: "ViewStatesTests")
    }

    func testALineIsTheSystemItCrossesAndItsShare() {
        XCTAssertEqual(ViewAnchor.at(390, in: systems), ViewAnchor(key: 3840, fraction: 0.25))
        XCTAssertEqual(ViewAnchor.at(340, in: systems), ViewAnchor(key: 3840, fraction: 0))
        // In the gap above a system: that system, the gap kept below zero.
        XCTAssertEqual(ViewAnchor.at(320, in: systems), ViewAnchor(key: 3840, fraction: -0.1))
        XCTAssertEqual(ViewAnchor.at(0, in: systems), ViewAnchor(key: 0, fraction: -0.5))
        // Below the last: the last, past its end.
        XCTAssertEqual(ViewAnchor.at(880, in: systems), ViewAnchor(key: 7680, fraction: 1.5))
    }

    func testTheSameLayoutComesBackToTheSamePointAndDoesNotDrift() throws {
        for y in [0.0, 100, 250, 320, 340, 539, 700, 900] {
            let place = try XCTUnwrap(ViewAnchor.at(y, in: systems))
            XCTAssertEqual(try XCTUnwrap(place.line(in: systems)), y, accuracy: 1e-9)
        }
        var y = 437.0
        for _ in 0..<50 {
            y = try XCTUnwrap(ViewAnchor.at(y, in: systems)?.line(in: systems))
        }
        XCTAssertEqual(y, 437, accuracy: 1e-9)
    }

    func testANewLayoutComesBackOnTheSystemThatHoldsTheBar() {
        let wider = [AnchorSpan(key: 0, top: 80, bottom: 380), AnchorSpan(key: 5760, top: 420, bottom: 720)]
        XCTAssertEqual(ViewAnchor(key: 3840, fraction: 0.5).line(in: wider), 230)
        XCTAssertEqual(ViewAnchor(key: -1, fraction: 0).line(in: wider), 80)
    }

    func testPagesSideBySideAndNothingToGoTo() {
        let pages = [AnchorSpan(key: 2, top: 0, bottom: 1000), AnchorSpan(key: 1, top: 0, bottom: 1000), AnchorSpan(key: 3, top: 1020, bottom: 2020)]
        XCTAssertEqual(ViewAnchor.at(500, in: pages), ViewAnchor(key: 1, fraction: 0.5))
        XCTAssertEqual(ViewAnchor(key: 3, fraction: 0.25).line(in: pages), 1270)
        XCTAssertNil(ViewAnchor.at(10, in: []))
        XCTAssertNil(ViewAnchor(key: 0, fraction: 0).line(in: []))
        XCTAssertNil(ViewAnchor.at(.nan, in: systems))
        XCTAssertNil(ViewAnchor(page: ["key": "3", "fraction": 0]))
        XCTAssertNil(ViewAnchor(page: NSNull()))
        XCTAssertEqual(ViewAnchor(page: ["key": NSNumber(value: 3), "fraction": NSNumber(value: 0.5)]), ViewAnchor(key: 3, fraction: 0.5))
    }

    func testTheNotesViewIsKeptByThePieceWithABarOfNoNotesAndTheRoll() throws {
        var state = NotesViewState()
        state.selection = SelectionSpanKept(onset: 1920, end: 3840)
        state.place = ViewAnchor(key: 3840, fraction: 0.4)
        state.zoom = 1.5
        state.left = 0.2
        state.position = 2400
        state.loop = true
        state.rollOpen = true
        state.roll = ["zoom": 48, "rowScale": 1.25, "scroll": 1920, "pitchTop": 84]
        ViewStates.keep(state, "p_1", in: defaults)
        XCTAssertEqual(ViewStates.notes("p_1", in: defaults), state)
        XCTAssertNil(ViewStates.notes("p_2", in: defaults), "another piece keeps its own")
        defaults.set(Data("not json".utf8), forKey: ViewStates.notesKey("p_3"))
        XCTAssertNil(ViewStates.notes("p_3", in: defaults), "what cannot be read is not drawn from")
    }

    func testThePdfComesBackToItsPlaceWhileItsPageIsTheOneLastShown() {
        let state = PdfViewState(place: ViewAnchor(key: 3, fraction: 0.4), left: 0.1, zoom: 1.5)
        ViewStates.keep(state, "p_1", in: defaults)
        XCTAssertEqual(defaults.integer(forKey: ViewStates.pdfPageKey("p_1")), 3, "the page under the bar goes with it")
        XCTAssertEqual(ViewStates.pdf("p_1", shownPage: 3, in: defaults), state)
        XCTAssertEqual(ViewStates.pdf("p_1", shownPage: 0, in: defaults), state, "no page kept: the place")
        XCTAssertNil(ViewStates.pdf("p_1", shownPage: 5, in: defaults), "scrolled on to page 5 and left another way: page 5's top")
    }

    func testAViewKeptWinsOverTheLandingOnlyFromTheOtherView() throws {
        let own = try XCTUnwrap(PieceSummary(answer: #"{"project": {"id": "p_1", "hasNotes": true, "document": true}}"#))
        let notes = OpenPiece(id: 7, title: "Nocturne", pages: [], timemap: "", own: own)
        let pdf = OpenPiece(id: 8, title: "Nocturne", pages: [], timemap: "", isDocument: true, own: own)
        // The first switch: nothing kept, the landing on the same stretch of music.
        XCTAssertEqual(ViewStates.landing(.tick(7680), for: notes, in: defaults), .tick(7680))
        XCTAssertEqual(ViewStates.landing(.page(2), for: pdf, in: defaults), .page(2))
        ViewStates.keep(NotesViewState(), "p_1", in: defaults)
        XCTAssertEqual(ViewStates.landing(.tick(7680), for: notes, in: defaults), .kept)
        XCTAssertEqual(ViewStates.landing(.page(2), for: pdf, in: defaults), .page(2), "the PDF's own view is kept apart")
        ViewStates.keep(PdfViewState(place: ViewAnchor(key: 2, fraction: 0)), "p_1", in: defaults)
        XCTAssertEqual(ViewStates.landing(.page(2), for: pdf, in: defaults), .kept)
        // From the library, with no landing, a piece opens as before.
        XCTAssertNil(ViewStates.landing(nil, for: notes, in: defaults))
        XCTAssertNil(Landing.kept.tick)
        XCTAssertNil(Landing.kept.page)
    }

    /// #533 6: the notes stay out of sight until they stand at their place, so the top of the first page never shows first.
    func testTheNotesWaitOutOfSightForAPlaceAndShowAtOnceWithNone() {
        XCTAssertTrue(Landing.holdsTheNotes(.tick(7680)), "from a PDF's page: held until the page of the music is shown")
        XCTAssertTrue(Landing.holdsTheNotes(.kept), "back to the view kept: held until it is back")
        XCTAssertFalse(Landing.holdsTheNotes(nil), "from the library: shown as drawn")
        XCTAssertFalse(Landing.holdsTheNotes(.page(2)))
        XCTAssertTrue(Landing.notesShowOnRender(.tick(7680)), "the render reaches the place itself")
        XCTAssertFalse(Landing.notesShowOnRender(.kept), "the view kept shows once returnToNotesView has put it back")
        XCTAssertTrue(Landing.notesShowOnRender(nil))
    }

    @MainActor
    func testAPieceClosedByTheLibraryIsNotSavedAgainAsItsScreenGoes() async {
        XCTAssertFalse(ClosedPieces.take(9_000_001), "a piece still open is saved as its screen goes")
        await ClosedPieces.close(9_000_001)
        XCTAssertTrue(ClosedPieces.take(9_000_001))
        XCTAssertFalse(ClosedPieces.take(9_000_001), "asked once")
    }

    @MainActor
    func testTheRollFrameWaitsForItsOwnPiecesRoll() throws {
        let frame = ["zoom": 48.0, "rowScale": 1.0, "scroll": 960.0, "pitchTop": 84.0]
        RollFrameHandoff.give(frame, to: 7)
        XCTAssertTrue(RollFrameHandoff.take(for: 8) is NSNull, "another piece's roll does not take it")
        XCTAssertEqual(RollFrameHandoff.waiting(for: 7), frame)
        XCTAssertEqual(try XCTUnwrap(RollFrameHandoff.take(for: 7) as? [String: Double]), frame)
        XCTAssertTrue(RollFrameHandoff.take(for: 7) is NSNull, "taken once")
        XCTAssertNil(RollFrameHandoff.waiting(for: 7))
    }
}
