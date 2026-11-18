import XCTest

@testable import MotiffLab

/// FocusModel (S3, ADR 0031 section 3) on the kit's piece open in music-core, selected through SelectionModel: the play
/// range is the selection's span and goes with it; the card is the unit selected, or the unit that holds its start, and
/// says where it is; a way back takes the version in focus away; a version's notes are kept until the notes change.
@MainActor
final class FocusModelTests: XCTestCase {
    private var piece: Int32 = 0
    private var selection: SelectionModel!
    private var focus: FocusModel!
    private let text = UIText(language: "en")

    override func setUp() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: nil)))
        piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0)
        selection = SelectionModel()
        focus = FocusModel(selection: selection)
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
        let notes = try XCTUnwrap(await focus.versionNotes(of: id, in: piece))
        XCTAssertFalse(notes.isEmpty, "the version's notes to hear")

        _ = MusicCore.call(piece: piece, "performance.remove", facadeRequest(["id": id]))
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

    func testTheWayBackTakesTheVersionAwayAndTheRollIsKept() {
        focus.open(from: .roll)
        XCTAssertEqual(focus.entry, .roll, "the way back is the roll's")
        focus.showAsRoll(true)
        focus.showVersion(FocusModel.Version(id: "v1", name: "Urtext", notes: [], sounds: false, differing: [3]))
        focus.takeCardOptions("{}")
        focus.close()
        XCTAssertNil(focus.version, "out of focus no version is shown for the phrase")
        XCTAssertTrue(focus.asRoll, "Notes | Piano roll is kept while the piece is open")
        XCTAssertEqual(focus.cardOptions, "{}", "the card's options are the page's until it gives others")
        focus.open(from: .notes)
        XCTAssertEqual(focus.entry, .notes)
    }
}
