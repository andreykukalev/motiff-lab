import XCTest

@testable import MotiffLab

/// The phrases on the score as 1.2 built them (docs/ux-baseline.md, PHR-01, PHR-02 and PHR-04 to PHR-07; the phrases spec,
/// decisions of 2026-11-19 and 2026-11-20, the plan of 1.2, item 1): a tap on a phrase's number or a section's letter, a
/// mark held and dragged, the selection menu's «Фраза 2 · …», «Вся фраза» and «Начать фразу здесь», the ⓘ list's «Фразы»
/// row, and the card «Выделение было фразой 2…». Nothing about the marks changes the selection by itself. Taken through the
/// score's controller as the screen takes them, over the real music-core and the kit's piece opened in Russian, with the
/// score's page recording what it is sent. What the page draws and what a press on it posts is the page scenarios'
/// (`tests/contracts/page-scenarios/score-phrase-*.json`); the menus' and the cards' look is the device's.
@MainActor
final class PhrasesBaselineTests: XCTestCase {
    private var opened: [Int32] = []
    private let russian = UIText(language: "ru")

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    /// The kit's piece on the score's controller, its phrases read as the screen reads them.
    private func score() async throws -> ScoreController {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "ru", title: example.title).get()
        opened.append(piece.id)
        let score = ScoreController()
        score.scorePage = RecordingScorePage()
        score.show(piece, dark: false)
        await score.selectionModel.loadPhrases(of: piece.id)
        return score
    }

    /// Waits, five seconds at most, for what a tap the controller answers in a task of its own leaves.
    private func settle(_ what: String, until done: () -> Bool) async throws {
        for _ in 0..<100 where !done() { try await Task.sleep(for: .milliseconds(50)) }
        XCTAssertTrue(done(), what)
    }

    private func phrase(_ number: Int, _ score: ScoreController) throws -> PhraseUnit {
        try XCTUnwrap(score.selectionModel.phrases?.unit(number), "phrase \(number) of the kit's piece")
    }

    private func isSelected(_ onset: Double, _ end: Double, _ score: ScoreController) -> Bool {
        score.selectionModel.selection?.onset == onset && score.selectionModel.selection?.end == end
    }

    // MARK: - PHR-01: the marks, and a number or a letter tapped

    /// PHR-01: a number at each phrase's start and a letter at each section's, with what VoiceOver says of them; a tap on a
    /// number selects its whole phrase, on a letter its section, each in place of the selection there was, with «Вернуть».
    func testPHR01ANumberOrALetterTappedSelectsItsPhraseOrSection() async throws {
        let score = try await score()
        let phrases = try XCTUnwrap(score.selectionModel.phrases)
        XCTAssertGreaterThan(phrases.units.count, 2)
        let numbers = try XCTUnwrap(score.phraseMarks["phrases"] as? [[String: Any]])
        XCTAssertEqual(numbers.compactMap { $0["number"] as? Int }, phrases.units.map(\.number), "a number at each phrase's start")
        XCTAssertEqual(numbers.compactMap { $0["tick"] as? Double }, phrases.units.map(\.onset), "at its start note")
        let letters = try XCTUnwrap(score.phraseMarks["sections"] as? [[String: Any]])
        XCTAssertEqual(letters.compactMap { $0["letter"] as? String }, phrases.sections.map(\.label), "a letter at each section's start")
        XCTAssertEqual(russian.format("phrases.markLabel", "2", "4", "3", "после каденции"), "Фраза 2, с такта 4, доля 3, начинается после каденции")

        let first = try phrase(1, score)
        let second = try phrase(2, score)
        score.pickPhrase(1)
        try await settle("a tap on 1 selects phrase 1") { isSelected(first.selectableOnset, first.selectableEnd, score) }
        score.pickPhrase(2)
        try await settle("a tap on 2 selects phrase 2") { isSelected(second.selectableOnset, second.selectableEnd, score) }
        XCTAssertEqual(score.selectionModel.toast?.replaced, true, "in place of the selection there was, with «Вернуть»")
        XCTAssertEqual(phrases.unit(selected: score.selectionModel.selection)?.number, 2, "the selection is phrase 2: its number cobalt")
        let section = try XCTUnwrap(phrases.sections.last)
        score.pickSection(section.id)
        try await settle("a tap on a letter selects its section") { isSelected(section.selectableOnset, section.selectableEnd, score) }
        let selected = try XCTUnwrap(score.selectionModel.selection)
        XCTAssertLessThanOrEqual(selected.lastBar - selected.firstBar + 1, 32, "as far as a selection holds it")
    }

    // MARK: - PHR-02: a mark held, its menu, and dragged

    /// PHR-02: a number held still opens its menu under it; «Выделить фразу» selects the phrase; dragged and let go on a
    /// note, the boundary moves there, the player's own (amber) with the found start kept as a ghost; each edit is one step
    /// of the phrases' own Undo and Redo, offered only by its toast, never a step of the selection's history (⌘Z); «Вернуть
    /// найденную» and «Убрать границу» act on that one boundary. The selection stays as it is throughout.
    func testPHR02AMarkHeldOpensItsMenuAndDraggedMovesItsBoundary() async throws {
        let score = try await score()
        let second = try phrase(2, score)
        XCTAssertFalse(second.own)
        XCTAssertFalse(second.why.isEmpty, "«Почему здесь» on a found start")
        score.holdPhrase(2, at: CGPoint(x: 40, y: 60))
        XCTAssertEqual(score.phraseMenu, PhraseMarkPick(number: 2, at: CGPoint(x: 40, y: 60)), "held still: its menu under its number")
        XCTAssertEqual([russian["phrases.menuFound"], russian["phrases.select"], russian["phrases.remove"]], ["найдена приложением", "Выделить фразу", "Убрать границу"])
        score.selectPhrase(second)
        XCTAssertNil(score.phraseMenu)
        try await settle("«Выделить фразу»") { isSelected(second.selectableOnset, second.selectableEnd, score) }
        let selected = score.selectionModel.selection
        let selectionSteps = score.selectionModel.steps.done.count

        score.movePhrase(from: second.onset, to: second.onset - 360)
        try await settle("let go on a note, the boundary moves there") { score.selectionModel.phrases?.unit(2)?.own == true }
        let moved = try phrase(2, score)
        XCTAssertNotEqual(moved.onset, second.onset)
        XCTAssertEqual(moved.found?.at, second.onset, "the found start stays as a faint comma")
        XCTAssertEqual(russian.format("phrases.putBack", "4.3"), "Вернуть найденную · 4.3")
        XCTAssertEqual(score.selectionModel.selection, selected, "an edit never changes the selection")
        XCTAssertEqual(score.selectionModel.steps.done.count, selectionSteps, "nor is it a step of the selection's Undo")
        XCTAssertEqual(score.selectionModel.phraseToast?.words, score.text["phrases.moved"])
        XCTAssertEqual(score.selectionModel.phraseToast?.offer, .undo, "its toast offers «Отменить»")

        score.stepPhraseEdit(redo: false)
        try await settle("«Отменить»: the found start again") { score.selectionModel.phrases?.unit(2)?.own == false }
        XCTAssertEqual(try phrase(2, score).onset, second.onset)
        XCTAssertEqual(score.selectionModel.phraseToast?.offer, .redo, "then «Повторить»")
        XCTAssertEqual(russian["phrases.redo"], "Повторить")
        score.stepPhraseEdit(redo: true)
        try await settle("«Повторить»: moved again") { score.selectionModel.phrases?.unit(2)?.own == true }
        let movedAgain = try phrase(2, score)
        score.putBackFound(movedAgain)
        try await settle("«Вернуть найденную»") { score.selectionModel.phrases?.unit(2)?.onset == second.onset }
        XCTAssertFalse(try phrase(2, score).own)

        let count = score.selectionModel.phrases?.units.count ?? 0
        score.removeBoundary(second)
        try await settle("«Убрать границу»: the phrase joins the one before it") { score.selectionModel.phrases?.units.count == count - 1 }
        XCTAssertEqual(score.selectionModel.selection, selected, "the selection as it was")
        XCTAssertEqual(score.selectionModel.steps.done.count, selectionSteps)
    }

    // MARK: - PHR-04: a motif's comma in focus

    /// PHR-04: in focus each phrase's motifs are music-core's, named for VoiceOver; a tap on a motif's comma selects the
    /// motif in place of the selection there was, which Expand then loops as any selection.
    func testPHR04AMotifsCommaTappedSelectsTheMotif() async throws {
        let score = try await score()
        let unit = try XCTUnwrap(score.selectionModel.phrases?.units.first { $0.motifs.count > 1 }, "a phrase with motifs")
        let numbers = try XCTUnwrap(score.phraseMarks["phrases"] as? [[String: Any]])
        let mark = try XCTUnwrap(numbers.first { $0["number"] as? Int == unit.number })
        let motifs = try XCTUnwrap(mark["motifs"] as? [[String: Any]])
        XCTAssertEqual(motifs.compactMap { $0["tick"] as? Double }, unit.motifs.map(\.onset), "a small comma at each motif's start")
        XCTAssertEqual(russian.format("phrases.motifLabel", "2", "3", "такты 5–6"), "Мотив 2 фразы 3, такты 5–6")
        score.pickPhrase(unit.number)
        try await settle("the phrase selected first") { isSelected(unit.selectableOnset, unit.selectableEnd, score) }
        let motif = unit.motifs[1]
        score.pickMotif(2, of: unit.number)
        try await settle("a tap on the second motif's comma selects it") { isSelected(motif.onset, motif.end, score) }
        XCTAssertEqual(score.selectionModel.toast?.replaced, true, "in place of the selection there was")
    }

    // MARK: - PHR-05: the selection menu

    /// PHR-05: a selection that is a phrase exactly is named by it («Фраза 2 · 4.3 – 8.3»); one inside a phrase is offered
    /// «Вся фраза · …», which takes it out to the phrase; More's «Начать фразу здесь», where no phrase starts, starts one at
    /// the selection's first note, the player's own, and leaves the selection as it is.
    func testPHR05TheSelectionMenuNamesTakesOutToAndStartsAPhrase() async throws {
        let score = try await score()
        let second = try phrase(2, score)
        XCTAssertEqual(russian.format("selection.phrase", "2", russian.format("pill.span", "4.3", "8.3")), "Фраза 2 · 4.3 – 8.3")
        XCTAssertEqual(russian.format("phrases.wholeSpan", "4.3 – 8.3"), "Вся фраза · 4.3 – 8.3")
        await score.select(second.selectableOnset, second.selectableEnd - 240)
        let phrases = try XCTUnwrap(score.selectionModel.phrases)
        XCTAssertNil(phrases.unit(selected: score.selectionModel.selection), "not the phrase exactly: not named by it")
        XCTAssertEqual(phrases.unit(enclosing: score.selectionModel.selection)?.number, 2, "«Вся фраза» is offered")
        score.selectWholePhrase()
        try await settle("«Вся фраза»") { isSelected(second.selectableOnset, second.selectableEnd, score) }
        XCTAssertEqual(phrases.unit(selected: score.selectionModel.selection)?.number, 2, "named «Фраза 2 · …»")
        XCTAssertNil(phrases.unit(enclosing: score.selectionModel.selection), "and «Вся фраза» is offered no more")

        let inside = second.onset + ((second.end - second.onset) / 2).rounded()
        await score.select(inside, inside + 240)
        let selected = try XCTUnwrap(score.selectionModel.selection)
        XCTAssertFalse(phrases.units.contains { $0.onset == selected.onset }, "no phrase starts here: More offers it")
        XCTAssertEqual(russian["phrases.start"], "Начать фразу здесь")
        score.startPhrase()
        try await settle("a phrase starts here") { score.selectionModel.phrases?.units.count == phrases.units.count + 1 }
        let started = try XCTUnwrap(score.selectionModel.phrases?.units.first { $0.own }, "the player's own boundary")
        XCTAssertGreaterThanOrEqual(started.onset, selected.onset, "at the selection's first note")
        XCTAssertLessThan(started.onset, second.end)
        XCTAssertEqual(score.selectionModel.selection, selected, "the selection as it was")
    }

    // MARK: - PHR-06: the «Фразы» row in ⓘ

    /// PHR-06: the «Фразы» row hides the marks on this iPad and shows them again, the phrases still named; «Вернуть найденные
    /// границы» puts back every boundary as the app found it, after a warning that counts the player's own, one step that
    /// the toast's «Отменить» takes back whole.
    func testPHR06ThePhrasesRowHidesTheMarksAndPutsBackTheFoundBoundaries() async throws {
        let score = try await score()
        score.showPhraseMarks(false)
        XCTAssertTrue(score.marksModel.phraseMarksHidden, "the marks hidden")
        XCTAssertNotNil(score.selectionModel.phrases, "the phrases still named")
        score.showPhraseMarks(true)
        XCTAssertFalse(score.marksModel.phraseMarksHidden, "and shown again")

        let second = try phrase(2, score)
        score.removeBoundary(second)
        try await settle("one boundary of the player's own") { score.selectionModel.phrases?.own == 1 }
        XCTAssertEqual(russian["phrases.reset"], "Вернуть найденные границы")
        XCTAssertEqual(russian.format("phrases.resetWarning", "1"), "Ваших границ: 1. Они заменятся найденными")
        XCTAssertEqual(russian["phrases.resetConfirm"], "Вернуть найденные")
        score.resetPhrases()
        try await settle("every boundary as the app found it") { score.selectionModel.phrases?.own == 0 }
        XCTAssertEqual(try phrase(2, score).onset, second.onset)
        XCTAssertEqual(score.selectionModel.phraseToast?.offer, .undo)
        score.stepPhraseEdit(redo: false)
        try await settle("«Отменить» takes the reset back in one step") { score.selectionModel.phrases?.own == 1 }
    }

    // MARK: - PHR-07: the selection never follows the marks by itself

    /// PHR-07: when the selection was a phrase exactly and an edit changed that phrase, the selection stays as it was and
    /// the card «Выделение было фразой 2. Выделить её по новым границам?» offers it: «Оставить» keeps the selection, and
    /// only «Выделить» selects the phrase by its new boundaries, one step of the selection's history. An Undo shows no card.
    func testPHR07TheSelectionStaysAndTheCardOffersThePhrasesNewBoundaries() async throws {
        let score = try await score()
        let second = try phrase(2, score)
        score.pickPhrase(2)
        try await settle("phrase 2 selected") { isSelected(second.selectableOnset, second.selectableEnd, score) }
        let selected = score.selectionModel.selection

        score.movePhrase(from: second.onset, to: second.onset - 360)
        try await settle("the card offers the phrase's new boundaries") { score.selectionModel.phraseOffer != nil }
        let moved = try phrase(2, score)
        XCTAssertEqual(score.selectionModel.phraseOffer?.number, 2)
        XCTAssertEqual(score.selectionModel.selection, selected, "the selection never follows the marks by itself")
        XCTAssertEqual(russian.format("phrases.offer", "2"), "Выделение было фразой 2. Выделить её по новым границам?")
        XCTAssertEqual([russian["phrases.offerSelect"], russian["phrases.offerKeep"]], ["Выделить", "Оставить"])
        score.selectionModel.closePhraseOffer()
        XCTAssertNil(score.selectionModel.phraseOffer, "«Оставить»")
        XCTAssertEqual(score.selectionModel.selection, selected, "keeps the selection")

        score.stepPhraseEdit(redo: false)
        try await settle("Undo") { score.selectionModel.phrases?.unit(2)?.own == false }
        XCTAssertNil(score.selectionModel.phraseOffer, "an Undo shows no card")
        score.movePhrase(from: second.onset, to: second.onset - 360)
        try await settle("moved again, the card again") { score.selectionModel.phraseOffer != nil }
        let steps = score.selectionModel.steps.done.count
        score.takePhraseOffer()
        try await settle("«Выделить»") { isSelected(moved.selectableOnset, moved.selectableEnd, score) }
        XCTAssertNil(score.selectionModel.phraseOffer)
        XCTAssertEqual(score.selectionModel.steps.done.count, steps + 1, "one step of the selection's history")
    }
}
