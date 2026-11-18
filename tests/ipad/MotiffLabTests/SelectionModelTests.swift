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

    func testSelectingAndAnotherLevelGoThroughMusicCore() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let answer = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: nil)))
        let piece = Int32(answer["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: piece) }
        var open = OpenPiece(id: piece, title: example.title, pages: [], timemap: "")
        open.file = FileManager.default.temporaryDirectory.appendingPathComponent("selection-\(UUID().uuidString).motiff.json")
        let model = SelectionModel()
        model.open(open)

        let quiet = await model.select(in: piece, request: #"{"bars": {"first": 1, "last": 2}}"#, gesture: nil, step: false, picked: false, replaces: false)
        XCTAssertNil(quiet)
        XCTAssertEqual(model.selection?.firstBar, 1)
        XCTAssertFalse(model.steps.canUndo, "a selection the player did not make now is no step")
        let made = await model.select(in: piece, request: #"{"bars": {"first": 3, "last": 4}}"#, gesture: nil, step: true, picked: false, replaces: false)
        XCTAssertNil(made)
        XCTAssertEqual(model.selection?.firstBar, 3)
        XCTAssertTrue(model.steps.canUndo)

        await model.loadPhrases(of: piece)
        XCTAssertEqual(model.phrases?.level, "phrase")
        await model.showLevel("sentence", in: piece, remember: false)
        XCTAssertEqual(model.phrases?.level, "sentence")
        XCTAssertNotNil(model.selection, "the selection moves to the new level")
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
