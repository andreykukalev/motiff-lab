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

    /// The phrases are read with what their marks say (the phrases spec, v1.2): each start's bar, beat and why, whether it
    /// is the player's own, and the sections' letters; a selection is a phrase only when it is its selectable span.
    func testThePhrasesAreReadWithTheirStartsAndSections() async throws {
        let (model, piece) = try inMusicCore()
        await model.loadPhrases(of: piece)
        let phrases = try XCTUnwrap(model.phrases)
        let first = try XCTUnwrap(phrases.unit(1))
        XCTAssertEqual(first.startBar, first.firstBar)
        XCTAssertFalse(first.why.isEmpty, "«Почему здесь»")
        XCTAssertTrue(phrases.units.allSatisfy { !$0.own }, "found in the file, none the player's yet")
        XCTAssertFalse(phrases.sections.isEmpty)
        XCTAssertEqual(phrases.sections.first?.label, "A")
        XCTAssertEqual(phrases.sections.first?.onset, 0)

        _ = await select(model, in: piece, #"{"span": {"onset": \#(Int(first.selectableOnset)), "end": \#(Int(first.selectableEnd))}}"#, step: false)
        XCTAssertEqual(phrases.unit(selected: model.selection)?.number, 1)
        _ = await select(model, in: piece, #"{"span": {"onset": \#(Int(first.selectableOnset)), "end": \#(Int(first.selectableEnd) - 1)}}"#, step: false)
        XCTAssertNil(phrases.unit(selected: model.selection), "a passage that is not the phrase exactly")
    }

    func testAStartThePlayerSetIsTheirOwn() async throws {
        let (model, piece) = try inMusicCore()
        await model.loadPhrases(of: piece)
        let found = try XCTUnwrap(model.phrases)
        let second = try XCTUnwrap(found.unit(2))
        let inside = Int(second.onset + (second.end - second.onset) / 2)
        _ = MusicCore.call(piece: piece, "phrases.edit", #"{"start": \#(inside)}"#)
        await model.loadPhrases(of: piece)
        let edited = try XCTUnwrap(model.phrases)
        XCTAssertEqual(edited.units.count, found.units.count + 1, "a phrase started by the player")
        XCTAssertEqual(edited.units.filter(\.own).map(\.number), [3], "drawn in amber")
    }

    /// A mark dragged is one step of the phrases' own Undo and Redo (v1.2 i-3a, c-2's restore): Undo puts the found start
    /// back, Redo moves it again, each with its toast; the selection stays exactly as it was throughout.
    func testAPhraseEditIsOneStepOfItsUndoAndRedoAndLeavesTheSelection() async throws {
        let (model, piece) = try inMusicCore()
        await model.loadPhrases(of: piece)
        let second = try XCTUnwrap(model.phrases?.unit(2))
        _ = await select(model, in: piece, #"{"span": {"onset": \#(Int(second.selectableOnset)), "end": \#(Int(second.selectableEnd))}}"#, step: true)
        let selected = model.selection

        let refused = await model.editPhrases(in: piece, #"{"move": {"from": \#(Int(second.onset)), "to": \#(Int(second.onset) - 360)}}"#, done: "moved")
        XCTAssertNil(refused)
        let moved = try XCTUnwrap(model.phrases?.unit(2))
        XCTAssertTrue(moved.own, "the player's boundary, amber")
        XCTAssertEqual(moved.found?.at, second.onset, "the found start keeps its ghost")
        XCTAssertEqual(model.selection, selected, "an edit never changes the selection")
        XCTAssertEqual(model.phraseToast?.offer, .undo)
        XCTAssertTrue(model.phraseSteps.canUndo)

        let undone = await model.stepPhrases(in: piece, redo: false, done: "undone")
        XCTAssertNil(undone)
        XCTAssertEqual(model.phrases?.unit(2)?.onset, second.onset, "Undo: the found start again")
        XCTAssertFalse(model.phrases?.unit(2)?.own ?? true)
        XCTAssertEqual(model.phraseToast?.offer, .redo)
        let redone = await model.stepPhrases(in: piece, redo: true, done: "redone")
        XCTAssertNil(redone)
        XCTAssertEqual(model.phrases?.unit(2)?.onset, moved.onset, "Redo: moved again")
        XCTAssertEqual(model.selection, selected, "nor do Undo and Redo")
    }

    /// «Начать фразу здесь» where a phrase already starts changes no edit (c-2): no step, so its toast offers no Undo.
    func testAnEditThatChangesNothingIsNoStep() async throws {
        let (model, piece) = try inMusicCore()
        await model.loadPhrases(of: piece)
        let second = try XCTUnwrap(model.phrases?.unit(2))
        let refused = await model.editPhrases(in: piece, #"{"start": \#(Int(second.onset))}"#, done: "started")
        XCTAssertNil(refused)
        XCTAssertNil(model.phraseToast?.offer, "nothing to take back")
        XCTAssertFalse(model.phraseSteps.canUndo)
    }

    /// An Undo after the phrases changed elsewhere is refused (c-2: "unavailable"): its reason in the toast, and the step goes.
    func testAnUndoThatCanNoLongerBeDoneSaysWhyAndGoes() async throws {
        let (model, piece) = try inMusicCore()
        await model.loadPhrases(of: piece)
        let second = try XCTUnwrap(model.phrases?.unit(2))
        _ = await model.editPhrases(in: piece, #"{"remove": \#(Int(second.onset))}"#, done: "removed")
        _ = MusicCore.call(piece: piece, "phrases.edit", #"{"resetAll": true}"#)
        let refused = await model.stepPhrases(in: piece, redo: false, done: "undone")
        XCTAssertNotNil(refused)
        XCTAssertEqual(model.phraseToast?.words, refused, "the reason, in the toast")
        XCTAssertNil(model.phraseToast?.offer)
        XCTAssertFalse(model.phraseSteps.canUndo)
        XCTAssertFalse(model.phraseSteps.canRedo, "the step is gone")
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

    /// The card «Выделить её по новым границам?» (the phrases spec, «Player's boundaries» 4, v1.2 i-3b): after an edit that
    /// changed the phrase the selection was exactly, it offers that phrase's new span; the selection itself never changes,
    /// and the next selection puts the card away.
    func testAnEditOfTheSelectedPhraseOffersItsNewBoundariesAndChangesNothing() async throws {
        let (model, piece) = try inMusicCore()
        await model.loadPhrases(of: piece)
        let second = try XCTUnwrap(model.phrases?.unit(2))
        _ = await select(model, in: piece, #"{"span": {"onset": \#(Int(second.selectableOnset)), "end": \#(Int(second.selectableEnd))}}"#, step: true)
        let selected = model.selection

        _ = await model.editPhrases(in: piece, #"{"move": {"from": \#(Int(second.onset)), "to": \#(Int(second.onset) - 360)}}"#, done: "moved")
        let offer = try XCTUnwrap(model.phraseOffer, "the selection was phrase 2")
        let moved = try XCTUnwrap(model.phrases?.unit(2))
        XCTAssertEqual(offer.number, 2)
        XCTAssertEqual([offer.onset, offer.end], [moved.selectableOnset, moved.selectableEnd], "its new boundaries")
        XCTAssertEqual(model.selection, selected, "only «Выделить» changes the selection")

        _ = await select(model, in: piece, #"{"span": {"onset": \#(Int(offer.onset)), "end": \#(Int(offer.end))}}"#, step: true)
        XCTAssertNil(model.phraseOffer, "«Выделить» (or any other selection) puts it away")
        XCTAssertEqual(model.phrases?.unit(selected: model.selection)?.number, 2, "the phrase by its new boundaries")
        XCTAssertEqual(model.undo()?.before, SelectionSpan(selected), "one step of the selection's own history")
    }

    /// A selection that was not exactly a phrase gets no card; a reset is one edit, its Undo in the phrases' own history.
    func testASelectionThatWasNoPhraseGetsNoCardAndAResetIsOneStep() async throws {
        let (model, piece) = try inMusicCore()
        await model.loadPhrases(of: piece)
        let second = try XCTUnwrap(model.phrases?.unit(2))
        _ = await select(model, in: piece, #"{"span": {"onset": \#(Int(second.selectableOnset)), "end": \#(Int(second.selectableEnd) - 240)}}"#, step: true)
        XCTAssertEqual(model.phrases?.unit(enclosing: model.selection)?.number, 2, "«Вся фраза» offers phrase 2")

        _ = await model.editPhrases(in: piece, #"{"remove": \#(Int(second.onset))}"#, done: "removed")
        XCTAssertNil(model.phraseOffer, "not exactly a phrase: no card")
        XCTAssertEqual(model.phrases?.own, 1, "«Ваших границ: 1»")

        _ = await model.editPhrases(in: piece, #"{"resetAll": true}"#, done: "reset")
        XCTAssertEqual(model.phrases?.own, 0)
        XCTAssertEqual(model.phrases?.unit(2)?.onset, second.onset, "the found boundary again")
        XCTAssertEqual(model.phraseToast?.offer, .undo)
        _ = await model.stepPhrases(in: piece, redo: false, done: "undone")
        XCTAssertEqual(model.phrases?.own, 1, "Undo takes the reset back in one step")
    }

    /// A motif's comma tapped in focus (the phrases spec, decision 2): the page gets each phrase's motifs from music-core with
    /// what a screen reader says, and a tap selects the motif's span in place of the selection there was, one step of the
    /// selection's history; a motif the phrase has not selects nothing.
    func testAMotifTappedInFocusIsSelectedAsOneStep() async throws {
        let example = try XCTUnwrap(OpenPiece.example, "the example piece is in the app")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let score = ScoreController()
        score.scorePage = RecordingScorePage()
        score.show(piece, dark: false)
        await score.selectionModel.loadPhrases(of: piece.id)
        let phrase = try XCTUnwrap(score.selectionModel.phrases?.unit(1))
        let motif = try XCTUnwrap(phrase.motifs.last, "music-core gives the phrase its motifs")
        let marks = try XCTUnwrap(score.phraseMarks["phrases"] as? [[String: Any]])
        let drawn = try XCTUnwrap(marks.first?["motifs"] as? [[String: Any]])
        XCTAssertEqual(drawn.compactMap { $0["tick"] as? Double }, phrase.motifs.map(\.onset))
        let label = try XCTUnwrap(drawn.last?["label"] as? String)
        XCTAssertTrue(label.contains(barsText(motif.firstBar, motif.lastBar, score.text)), label)
        XCTAssertFalse(label.contains("phrases.motifLabel"), "its words are in the Words")

        score.pickMotif(phrase.motifs.count + 1, of: 1)
        XCTAssertNil(score.selectionModel.selection, "no such motif")
        // The piece's history is kept by its number in music-core, which another test may have left steps under.
        let stepsBefore = score.selectionModel.steps.done.count
        score.pickMotif(phrase.motifs.count, of: 1)
        for _ in 0..<100 where score.selectionModel.selection == nil { try await Task.sleep(for: .milliseconds(50)) }
        let selected = try XCTUnwrap(score.selectionModel.selection, "music-core selects the motif")
        XCTAssertEqual([selected.onset, selected.end], [motif.onset, motif.end])
        XCTAssertEqual(score.selectionModel.steps.done.count, stepsBefore + 1, "one step of the selection's history: this tap's")
    }

    /// «Вся фраза» (the phrases spec, decision 5): only for a selection inside one phrase that is not that phrase exactly.
    func testTheWholePhraseIsOfferedOnlyInsideOnePhrase() throws {
        let answer = #"{"units": [{"number": 1, "onset": 0, "end": 1920}, {"number": 2, "onset": 1920, "end": 3840}]}"#
        let phrases = try XCTUnwrap(PiecePhrases(answer: answer))
        let inside = ScoreSelection(onset: 480, end: 960, firstBar: 1, lastBar: 1)
        XCTAssertEqual(phrases.unit(enclosing: inside)?.number, 1)
        XCTAssertNil(phrases.unit(enclosing: ScoreSelection(onset: 0, end: 1920, firstBar: 1, lastBar: 2)), "the phrase exactly")
        XCTAssertNil(phrases.unit(enclosing: ScoreSelection(onset: 960, end: 2400, firstBar: 2, lastBar: 3)), "across a boundary")
        XCTAssertEqual(phrases.unit(at: 1920)?.number, 2)
    }
}
