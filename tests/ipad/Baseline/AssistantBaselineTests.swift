import XCTest

@testable import MotiffLab

/// The assistant of 1.2, option A, on the iPad (docs/ux-baseline.md, ASST-01 to ASST-03; the plan of 1.2, item 3 and
/// answer 6, approved 2026-11-20 11:26 UTC; ADR 0028): why a template answer stands in the model's place, said in
/// words; the engine's practice action as a button that loops its bars at its tempo; the consent asked once more
/// because its list grew; and since 1.5 a question not sent kept by the closed panel (ASST-04). Taken through the
/// assistant's own types and controller, and the score's workspace for the button, over the real music-core; no
/// network: the answers are written here as the API sends them. The panel's look is the device's.
@MainActor
final class AssistantBaselineTests: XCTestCase {
    private var opened: [Int32] = []
    private var folders: [URL] = []
    private let russian = UIText(language: "ru")

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
        for folder in folders { try? FileManager.default.removeItem(at: folder) }
        folders = []
    }

    /// The assistant's controller as the app makes it, over settings of its own (`defaults`) and a store in a folder of its own.
    private func assistant(over defaults: UserDefaults) -> AssistantController {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("assistant-baseline-\(UUID().uuidString)")
        folders.append(folder)
        return AssistantController(store: AssistantStore(folder: folder), defaults: defaults)
    }

    /// ASST-01: a template answer in the model's place says why under «Факты приложения · ответ не удалось проверить», in
    /// words, from the reason the API adds; a reason this iPad does not know is said in general words; the model's own
    /// answer shown says no reason.
    func testASST01ATemplateAnswerSaysWhyInWords() throws {
        let sent = try XCTUnwrap(JSONValue.parse(#"{"kind": "answer", "text": "Ля минор [[key]]", "fallback": true, "reason": "timeout"}"#))
        XCTAssertEqual(AssistantReply(sent), .answer(text: "Ля минор [[key]]", fallback: true, reason: "timeout"), "the API's reason is read")
        var exchange = AssistantExchange(id: "baseline", question: "Почему ля минор?", scope: "Такт 3")
        exchange.fallback = true
        exchange.reason = "timeout"
        XCTAssertEqual(russian["mentor.engineFacts"], "Факты приложения · ответ не удалось проверить")
        XCTAssertEqual(exchange.reasonKey.map { russian[$0] }, "Почему: модель не ответила вовремя.", "why, in words")
        exchange.reason = "aReasonOfLater"
        XCTAssertEqual(exchange.reasonKey, "mentor.reason.other", "an unknown reason, in general words")
        XCTAssertNotEqual(russian["mentor.reason.other"], "mentor.reason.other")
        exchange.fallback = false
        XCTAssertNil(exchange.reasonKey, "the model's answer shown: no reason")
    }

    /// ASST-02: the engine's practice action («Зациклить такты 3–4 в темпе 60 %») selects its bars, sets its tempo and
    /// loops them with Цикл on, as «↻ Место» plays a place; one that does not loop plays them once, Цикл off.
    func testASST02ThePracticeButtonLoopsItsBarsAtItsTempo() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "ru", title: example.title).get()
        opened.append(piece.id)
        let session = await WorkspaceSession(piece: piece)
        let score = session.workspace.score
        let player = session.workspace.player
        await session.step("the piece's screen opens, its sound not loaded") {
            session.workspace.wire()
            session.workspace.applyModel()
        }
        let loop = AssistantPracticeOffer(first: 3, last: 4, hand: "both", tempo: 60, loop: true, label: "Зациклить такты 3–4 в темпе 60 %")
        await session.step("«Зациклить такты 3–4 в темпе 60 %»") { await score.practiseSuggestion(loop, on: player) }
        XCTAssertEqual(score.selectionModel.selection?.firstBar, 3, "its bars selected")
        XCTAssertEqual(score.selectionModel.selection?.lastBar, 4)
        XCTAssertEqual(player.tempoPercent, 60, "at its tempo")
        XCTAssertTrue(player.cycle, "looped, Цикл on")
        XCTAssertEqual(player.selection, score.focusModel.playRange?.range, "the player holds its bars")
        let once = AssistantPracticeOffer(first: 5, last: 5, hand: "both", tempo: 80, loop: false, label: "Сыграть такт 5 в темпе 80 %")
        await session.step("«Сыграть такт 5 в темпе 80 %»") { await score.practiseSuggestion(once, on: player) }
        XCTAssertEqual(score.selectionModel.selection?.firstBar, 5)
        XCTAssertEqual(player.tempoPercent, 80)
        XCTAssertFalse(player.cycle, "played once: Цикл off")
    }

    /// ASST-03: the consent lists what a question sends, now with the piece's counted facts; a player who agreed to the
    /// earlier list is asked once more, the consent saying why; agreed again, they are not asked a third time; it covers no
    /// draft, which asks its own (DRAFT-11).
    func testASST03AConsentToTheEarlierListIsAskedOnceMore() throws {
        let defaults = try XCTUnwrap(UserDefaults(suiteName: "assistant-baseline-\(UUID().uuidString)"))
        defaults.set("yes", forKey: AssistantController.consentKey)
        let earlier = assistant(over: defaults)
        XCTAssertFalse(earlier.consentGiven, "the consent to the earlier list does not cover the new one")
        XCTAssertTrue(earlier.consentAskedAgain, "so it is asked again, saying why")
        XCTAssertTrue(russian["mentor.consentAgain"].hasPrefix("Список изменился с тех пор, как вы согласились"))
        XCTAssertTrue(russian["mentor.consentPiece"].contains("самые плотные такты"), "the new list: the piece's counted facts")
        earlier.consent(true)
        let later = assistant(over: defaults)
        XCTAssertTrue(later.consentGiven, "asked once")
        XCTAssertFalse(later.consentAskedAgain)
        XCTAssertFalse(later.draftConsentGiven, "a question in a draft asks its own")
    }

    /// ASST-04 (since 1.5, answer 16 of 1.4): a question typed in the assistant's panel and not sent outlives the closed
    /// panel, also when the field's own change, emptied by the close, comes after it; the panel opened again, or by an
    /// Ask, holds it, and so does the same piece on a screen made again (another score). Sent, it goes; another piece's
    /// panel starts empty. The field's look is the device's.
    func testASST04AQuestionNotSentOutlivesTheClosedPanel() throws {
        let defaults = try XCTUnwrap(UserDefaults(suiteName: "assistant-baseline-\(UUID().uuidString)"))
        let panel = assistant(over: defaults)
        let typed = "Где в этой фразе кульминация?"
        panel.load(piece: "elise", score: ScoreController())
        panel.open(wholePiece: true, selection: nil)
        var field = AskFlow(surface: .panel)
        field.apply(.open)
        field.apply(.type(typed))
        panel.keepUnsent(field.draft)
        field.apply(.close)
        panel.close()
        panel.keepUnsent(field.draft)
        XCTAssertEqual(panel.unsentInPanel, typed, "closed, the panel keeps the question")

        panel.show()
        XCTAssertEqual(panel.unsentInPanel, typed, "the panel opened again")
        panel.close()
        panel.open(wholePiece: false, selection: nil)
        XCTAssertEqual(panel.unsentInPanel, typed, "opened again by an Ask")
        panel.load(piece: "elise", score: ScoreController())
        XCTAssertEqual(panel.unsentInPanel, typed, "the same piece's screen made again, with another score")

        var asking = AskFlow(surface: .panel)
        asking.apply(.open)
        asking.apply(.type(panel.unsentInPanel))
        let sent = asking.apply(.send)
        XCTAssertEqual(sent, typed, "sent as typed")
        panel.keepUnsent(asking.draft)
        XCTAssertEqual(panel.unsentInPanel, "", "sent, it goes")
        panel.keepUnsent("Почему ля минор?")
        panel.close()
        panel.load(piece: "promenade")
        XCTAssertEqual(panel.unsentInPanel, "", "another piece's panel starts empty")
    }
}
