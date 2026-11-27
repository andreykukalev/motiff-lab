import XCTest

@testable import MotiffLab

/// FocusModel (S3, ADR 0031 section 3) on the kit's piece open in music-core, selected through SelectionModel: the play
/// range is the selection's span and goes with it; the card is the unit selected, or the unit that holds its start, and
/// says where it is; a version's notes are kept until the notes change. Since v1.6 i-10b against a fake score
/// (`FocusScore`): Expand opens only on a selection, on pages drawn and with the Pen off, holds the page still until the
/// way back, engraves the card at the page's options, and the way back takes the version in focus away. What
/// ScoreController did before the move, rule for rule.
@MainActor
final class FocusModelTests: XCTestCase {
    /// The score as Expand sees it: a page that answers the card's options, and what was asked of it.
    @MainActor
    private final class FakeScore: FocusScore {
        @MainActor
        final class Page: ScorePage {
            private(set) var commands: [PageCommand] = []

            func run(_ command: PageCommand, _ argument: Any?) async throws -> Any? {
                commands.append(command)
                guard command == .focus else { return nil }
                return ["options": ["scale": 42, "pageWidth": 2600, "adjustPageHeight": true, "breaks": "auto"]]
            }

            func post(_ command: PageCommand, _ argument: Any) {
                commands.append(command)
            }
        }

        let page = Page()
        var scorePage: ScorePage { page }
        var pieceId: Int32?
        var shown = true
        var penOn = false
        var versionShown: VersionEntry?
        var versionDiffering: [Int] = []
        /// Each time the page was held still (true) or let go (false).
        private(set) var still: [Bool] = []
        private(set) var problems: [String?] = []

        func holdPage(still: Bool) {
            self.still.append(still)
        }

        func selectPassage(from start: Double, to end: Double) async {}

        func reveal(_ tick: Double) async {}

        func showProblem(_ message: String?) {
            problems.append(message)
        }
    }

    private var piece: Int32 = 0
    private var selection: SelectionModel!
    private var focus: FocusModel!
    private var score: FakeScore!
    private let text = UIText(language: "en")

    override func setUp() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: nil)))
        piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0)
        selection = SelectionModel()
        focus = FocusModel(selection: selection)
        score = FakeScore()
        score.pieceId = piece
        focus.score = score
    }

    override func tearDown() async throws {
        MusicCore.close(piece: piece)
    }

    private func select(_ onset: Double, _ end: Double) async throws -> ScoreSelection {
        let request = "{\"span\": {\"onset\": \(Int(onset)), \"end\": \(Int(end))}}"
        let refused = await selection.select(in: piece, request: request, gesture: nil, step: false, picked: false, replaces: false)
        XCTAssertNil(refused)
        return try XCTUnwrap(selection.selection)
    }

    func testThePlayRangeIsTheSelectionsSpanAndGoesWithIt() async throws {
        focus.updatePlayRange()
        XCTAssertNil(focus.playRange, "nothing plays for no selection")
        let chosen = try await select(0, 1920)
        XCTAssertNil(focus.playRange, "the play range waits for the root's call, after the page shows the selection")
        focus.updatePlayRange()
        XCTAssertEqual(focus.playRange, FocusModel.PlayRange(selection: chosen, range: chosen.onset..<chosen.end))
        selection.answered(nil)
        focus.updatePlayRange()
        XCTAssertNil(focus.playRange)
    }

    func testTheCardIsTheUnitSelectedAndSaysWhereItIs() async throws {
        await selection.loadPhrases(of: piece)
        let units = try XCTUnwrap(selection.phrases?.units)
        XCTAssertGreaterThan(units.count, 1)
        let first = units[0]
        let last = try XCTUnwrap(units.last)

        _ = try await select(first.selectableOnset, first.selectableEnd)
        XCTAssertEqual(focus.unit?.number, 1)
        XCTAssertFalse(focus.canStepBack, "nothing before the first unit")
        XCTAssertTrue(focus.canStepOn)
        let position = focus.position(in: text)
        XCTAssertTrue(position.contains("\(first.name) 1 of \(units.count)"), position)

        _ = try await select(last.selectableOnset, last.selectableEnd)
        XCTAssertEqual(focus.unit?.number, units.count)
        XCTAssertTrue(focus.canStepBack)
        XCTAssertFalse(focus.canStepOn, "nothing after the last unit")

        // Ten ticks at the first unit's start: not a unit, so the card is the unit that holds its start, named by its bars.
        let passage = try await select(first.selectableOnset, first.selectableOnset + 10)
        XCTAssertEqual(focus.unit?.number, 1)
        XCTAssertEqual(passage.firstBar, passage.lastBar)
        XCTAssertEqual(focus.position(in: text), text.format("bars.one", String(passage.firstBar)))
    }

    func testAVersionsNotesAreKeptUntilTheNotesChange() async throws {
        let example = try XCTUnwrap(KitPiece.example)
        let request = facadeRequest(["name": "Official", "file": example.data.base64EncodedString(), "fileName": example.fileName])
        var shelf = VersionShelf()
        XCTAssertTrue(shelf.read(performance: MusicCore.call(piece: piece, "performance.add", request)))
        let id = try XCTUnwrap(shelf.shownId)
        let read = await focus.versionNotes(of: id, in: piece)
        let notes = try XCTUnwrap(read)
        XCTAssertFalse(notes.isEmpty, "the version's notes to hear")

        _ = MusicCore.call(piece: piece, "sources.notes.remove", facadeRequest(["id": id]))
        let kept = await focus.versionNotes(of: id, in: piece)
        XCTAssertEqual(kept, notes, "kept, not read again")
        focus.forgetVersionNotes()
        let again = await focus.versionNotes(of: id, in: piece)
        XCTAssertEqual(again, [], "read again once forgotten: the version is gone")
    }

    func testNoSelectionIsNoUnitAndNoPosition() {
        XCTAssertNil(focus.unit)
        XCTAssertFalse(focus.canStepBack)
        XCTAssertFalse(focus.canStepOn)
        XCTAssertEqual(focus.position(in: text), "")
    }

    func testExpandOpensOnASelectionOnDrawnPagesWithThePenOffUntilTheWayBack() async throws {
        XCTAssertFalse(focus.focused, "a piece opens out of Expand")
        focus.enter()
        XCTAssertFalse(focus.focused, "nothing to lift without a selection")
        _ = try await select(0, 1920)
        score.penOn = true
        focus.enter()
        XCTAssertFalse(focus.focused, "not with the Pen on")
        score.penOn = false
        score.shown = false
        focus.enter()
        XCTAssertFalse(focus.focused, "not before the pages are drawn")
        XCTAssertEqual(score.still, [], "the page moves as before")
        score.shown = true
        focus.enter()
        XCTAssertTrue(focus.focused)
        XCTAssertEqual(focus.entry, .notes)
        XCTAssertEqual(score.still, [true], "the page under the veil held still")
        await until { self.score.page.commands.contains(.card) }
        XCTAssertEqual(Array(score.page.commands.prefix(2)), [.focus, .card], "the card opens, then shows the bars engraved at its options")
        XCTAssertEqual(focus.playRange?.range, 0..<1920)
        focus.leave()
        XCTAssertFalse(focus.focused, "the way back closes it")
        XCTAssertEqual(score.still, [true, false], "and lets the page go")
        focus.leave()
        XCTAssertEqual(score.still, [true, false], "closed once")
    }

    func testTheWayBackTakesTheVersionAwayAndTheRollIsKept() async throws {
        let example = try XCTUnwrap(KitPiece.example)
        let request = facadeRequest(["name": "Urtext", "file": example.data.base64EncodedString(), "fileName": example.fileName])
        var shelf = VersionShelf()
        XCTAssertTrue(shelf.read(performance: MusicCore.call(piece: piece, "performance.add", request)))
        score.versionShown = try XCTUnwrap(shelf.shown)
        score.versionDiffering = [1, 99]
        _ = try await select(0, 1920)
        focus.expandRoll()
        XCTAssertEqual(focus.entry, .roll, "the way back is the roll's")
        XCTAssertTrue(focus.asRoll)
        await until { self.focus.version != nil }
        let version = try XCTUnwrap(focus.version, "the version shown is the one in focus")
        XCTAssertEqual(version.name, "Urtext")
        XCTAssertEqual(version.differing, [1], "only the bars of the phrase in focus")
        let options = try XCTUnwrap(focus.cardOptions, "the card's options as the page gave them")
        focus.leave()
        XCTAssertNil(focus.version, "out of focus no version is shown for the phrase")
        XCTAssertTrue(focus.asRoll, "Notes | Piano roll is kept while the piece is open")
        XCTAssertEqual(focus.cardOptions, options, "the card's options are the page's until it gives others")
        focus.enter()
        XCTAssertEqual(focus.entry, .notes)
        focus.leave()
    }
}
