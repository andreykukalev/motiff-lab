import XCTest

@testable import MotiffLab

/// SelectionModel (S1, S3, ADR 0031 section 3): the selection's history is the piece's, kept beside the screen; a clear or
/// a replace shows «Вернуть», which Undo puts away; the steps Undo and Redo take back are the history's; selecting and
/// another level go through music-core, and hidden phrases are kept on the device.
@MainActor
final class SelectionModelTests: XCTestCase {
    private let one = ScoreSelection(onset: 0, end: 1920, firstBar: 1, lastBar: 2)
    private let far = ScoreSelection(onset: 7680, end: 9600, firstBar: 9, lastBar: 10)

    private func opened(_ id: Int32) -> SelectionModel {
        let model = SelectionModel()
        model.open(OpenPiece(id: id, title: "Nocturne", pages: [], timemap: ""))
        return model
    }

    func testNothingIsRecordedBeforeAPieceIsOpened() {
        let model = SelectionModel()
        model.record(from: nil, to: one, gesture: nil)
        XCTAssertFalse(model.steps.canUndo)
        XCTAssertNil(model.undo())
    }

    func testAClearShowsTheToastThatUndoPutsAwayAndTheStepComesBack() throws {
        let model = opened(9_101)
        model.record(from: nil, to: one, gesture: nil)
        model.record(from: one, to: nil, gesture: nil)
        XCTAssertEqual(model.toast?.replaced, false)
        XCTAssertEqual(model.toastKey, "selection.clearedToast")
        let step = try XCTUnwrap(model.undo())
        XCTAssertEqual(step.before, SelectionSpan(one))
        XCTAssertNil(model.toast, "Undo puts the toast away")
        let again = try XCTUnwrap(model.redo(), "Redo does the clear again")
        XCTAssertNil(again.after)
    }

    func testAReplaceSaysSo() {
        let model = opened(9_102)
        model.record(from: nil, to: one, gesture: nil)
        model.record(from: one, to: far, gesture: "g1", replaces: true)
        XCTAssertEqual(model.toast?.replaced, true)
        XCTAssertEqual(model.toastKey, "selection.replacedToast")
    }

    func testAnyOtherStepPutsTheToastAway() {
        let model = opened(9_106)
        model.record(from: nil, to: one, gesture: nil)
        model.record(from: one, to: nil, gesture: nil)
        model.record(from: nil, to: nil, gesture: nil)
        XCTAssertNotNil(model.toast, "no step of its own leaves the toast")
        model.record(from: nil, to: far, gesture: nil)
        XCTAssertNil(model.toast, "a new selection is a step that no longer takes back the clear")
    }

    func testTheHistoryIsThePiecesWhenItsScreenOpensAgain() {
        opened(9_103).record(from: nil, to: one, gesture: nil)
        XCTAssertTrue(opened(9_103).steps.canUndo, "the same piece keeps its history")
        opened(9_104).record(from: nil, to: far, gesture: nil)
        XCTAssertTrue(opened(9_104).steps.canUndo)
        XCTAssertFalse(opened(9_103).steps.canUndo, "another piece's history replaced the first one's")
    }

    func testHiddenPhrasesAreKeptOnTheDevice() {
        let before = UserDefaults.standard.bool(forKey: SelectionModel.phrasesHiddenKey)
        defer { UserDefaults.standard.set(before, forKey: SelectionModel.phrasesHiddenKey) }
        SelectionModel().hidePhrases(true)
        XCTAssertTrue(SelectionModel().phrasesHidden, "the next screen finds them hidden")
        SelectionModel().hidePhrases(false)
        XCTAssertFalse(SelectionModel().phrasesHidden)
    }

    /// A model on the kit's piece open in music-core, closed when the test ends, with a history of its own.
    private func inMusicCore() throws -> (SelectionModel, Int32) {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let answer = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: nil)))
        let piece = Int32(answer["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0)
        addTeardownBlock { MusicCore.close(piece: piece) }
        var open = OpenPiece(id: piece, title: example.title, pages: [], timemap: "")
        open.file = FileManager.default.temporaryDirectory.appendingPathComponent("selection-\(UUID().uuidString).motiff.json")
        let model = SelectionModel()
        model.open(open)
        return (model, piece)
    }

    private func select(_ model: SelectionModel, in piece: Int32, _ request: String, step: Bool) async -> String? {
        await model.select(in: piece, request: request, gesture: nil, step: step, picked: false, replaces: false)
    }

    func testASelectionThroughMusicCoreIsAStepUnlessSaidOtherwiseAndARefusalChangesNothing() async throws {
        let (model, piece) = try inMusicCore()
        let quiet = await select(model, in: piece, #"{"bars": {"first": 1, "last": 2}}"#, step: false)
        XCTAssertNil(quiet)
        XCTAssertEqual(model.selection?.firstBar, 1)
        XCTAssertFalse(model.steps.canUndo, "a selection the player did not make now is no step")
        let made = await select(model, in: piece, #"{"bars": {"first": 3, "last": 4}}"#, step: true)
        XCTAssertNil(made)
        let selected = try XCTUnwrap(model.selection)
        XCTAssertEqual(selected.firstBar, 3)
        XCTAssertTrue(model.steps.canUndo)

        let backwards = #"{"span": {"onset": 100, "end": 50}}"#
        let refused = await select(model, in: piece, backwards, step: true)
        XCTAssertEqual(refused, FacadeAnswer.problem(MusicCore.call(piece: piece, "select", backwards)), "music-core's refusal as the player reads it")
        XCTAssertNotNil(refused)
        XCTAssertEqual(model.selection, selected, "a refusal leaves the selection as it was")
        XCTAssertEqual(model.undo()?.after, SelectionSpan(selected), "and makes no step")
    }

    func testAnotherLevelMovesTheSelectionToItsUnit() async throws {
        let (model, piece) = try inMusicCore()
        await model.loadPhrases(of: piece)
        XCTAssertEqual(model.phrases?.level, "phrase")
        _ = await select(model, in: piece, #"{"bars": {"first": 3, "last": 4}}"#, step: false)
        let before = try XCTUnwrap(model.selection)

        await model.showLevel("sentence", in: piece, remember: false)
        XCTAssertEqual(model.phrases?.level, "sentence")
        let moved = try XCTUnwrap(model.selection)
        XCTAssertNotEqual(moved, before, "bars 3–4 are not a sentence: the selection moved")
        let holding = try XCTUnwrap(model.phrases?.units.first { $0.onset <= before.onset && before.onset < $0.end })
        XCTAssertEqual(moved.onset, holding.selectableOnset, "the selection is the sentence that holds it now")
        XCTAssertEqual(moved.end, holding.selectableEnd)
    }

    func testResetLevelHasTheUnitsFoundInTheFileAgain() async throws {
        let (model, piece) = try inMusicCore()
        await model.loadPhrases(of: piece)
        let found = try XCTUnwrap(model.phrases)
        XCTAssertTrue(found.edited.isEmpty)
        _ = MusicCore.call(piece: piece, "phrases.edit", #"{"level": "phrase", "joinWithNext": 1}"#)
        await model.loadPhrases(of: piece)
        XCTAssertEqual(model.phrases?.units.count, found.units.count - 1, "two phrases joined by the player")
        XCTAssertEqual(model.phrases?.edited.contains("phrase"), true)

        await model.resetLevel("phrase", in: piece)
        XCTAssertEqual(model.phrases?.units.count, found.units.count)
        XCTAssertEqual(model.phrases?.edited.isEmpty, true, "the player's own boundaries are forgotten")
    }

    func testARevertedGestureLeavesNoStep() throws {
        let model = opened(9_105)
        model.record(from: nil, to: one, gesture: nil)
        model.record(from: one, to: nil, gesture: "g2")
        let step = try XCTUnwrap(model.revert("g2"))
        XCTAssertEqual(step.before, SelectionSpan(one))
        XCTAssertNil(model.toast)
        XCTAssertNil(model.revert("g2"))
    }
}
