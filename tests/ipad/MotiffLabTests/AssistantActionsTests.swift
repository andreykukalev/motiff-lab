import XCTest

@testable import MotiffLab

/// The assistant at the selection and its drafts on the iPad (#582 items 17.2, 17.3 and 17.4): the drafts a question's
/// tool calls made, kept with the call that makes them again on Accept; the player's decision on each; «Продолжить в
/// чате»; an Ask opening the full assistant empty; and the small chat lifted above the keyboard.
@MainActor
final class AssistantActionsTests: XCTestCase {
    private let draftsResult = JSONValue.parse(#"""
    {"tool": "notes.remove", "kind": "draft", "ok": true, "drafts": [
      {"tool": "notes.remove", "applied": false, "explanation": "Nothing", "edits": [], "keep": {}, "flags": []},
      {"tool": "notes.remove", "applied": false, "title": "Remove the repeated E5", "explanation": "One note fewer in bar 3.",
       "edits": [{"kind": "remove", "address": "3.R.2", "before": {"pitch": "E5", "bar": 3, "at": "1/4", "length": "1/8", "velocity": 64}},
                 {"kind": "change", "address": "4.R.1", "before": {"pitch": "E5", "bar": 4, "at": "0", "length": "1/8", "velocity": 64},
                  "after": {"pitch": "F5", "bar": 4, "at": "0", "length": "1/8", "velocity": 64}}],
       "keep": {"contour": "kept"}, "flags": ["A hand spans more than a tenth."]}]}
    """#)!

    private func answered() -> AssistantChat {
        let call = AssistantToolCall(id: "t1", name: "notes.remove", arguments: .object(["address": .string("3.R.2")]))
        return AssistantChat(id: "c1", started: Date(timeIntervalSince1970: 0), updated: Date(timeIntervalSince1970: 0), role: "proposer")
            .asking("Take out the repeated note")
            .called([call])
            .resulted([AssistantToolOutcome(id: "t1", name: "notes.remove", content: draftsResult)])
            .answered("Here is a draft [[key]]")
    }

    private func controller() -> (AssistantController, URL) {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("assistant-\(UUID().uuidString)")
        let defaults = UserDefaults(suiteName: "assistant-\(UUID().uuidString)")!
        let assistant = AssistantController(store: AssistantStore(folder: folder), defaults: defaults)
        assistant.load(piece: "p1")
        return (assistant, folder)
    }

    func testTheQuestionsDraftsKeepTheCallThatMakesThemAgain() throws {
        let offers = AssistantDraftOffer.offers(in: answered())
        XCTAssertEqual(offers.count, 1, "a draft that changes nothing is left out")
        let draft = try XCTUnwrap(offers.first)
        XCTAssertEqual(draft.tool, "notes.remove")
        XCTAssertEqual(draft.parameters, .object(["address": .string("3.R.2")]))
        XCTAssertEqual(draft.index, 1, "its place among the call's drafts, as music-core makes them again")
        XCTAssertEqual(draft.title, "Remove the repeated E5")
        XCTAssertEqual(draft.flags, ["A hand spans more than a tenth."])
        XCTAssertEqual(draft.firstBar, 3)
        XCTAssertEqual(draft.lastBar, 4)
        XCTAssertEqual(draft.state, .proposed)
        XCTAssertEqual(draft.acceptRequest, .object([
            "bar": .int(3), "tool": .string("notes.remove"), "parameters": .object(["address": .string("3.R.2")]), "index": .int(1),
        ]))
        let mentor = AssistantChat(id: "c2", started: Date(), updated: Date(), role: "mentor").asking("Why?").answered("Because")
        XCTAssertEqual(AssistantDraftOffer.offers(in: mentor), [], "no tool call, no draft")
    }

    func testEachEditIsSaidInWordsInBothLanguages() throws {
        let draft = try XCTUnwrap(AssistantDraftOffer.offers(in: answered()).first)
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            let removed = AssistantDraftOffer.words(draft.edits[0], text)
            XCTAssertTrue(removed.contains("3.R.2") && removed.contains("E5"), removed)
            let changed = AssistantDraftOffer.words(draft.edits[1], text)
            XCTAssertTrue(changed.contains("E5 · 0 · 1/8") && changed.contains("F5 · 0 · 1/8"), changed)
        }
        let added = AssistantDraftOffer.Edit(kind: "add", address: nil, before: nil, after: .init(pitch: "G4", bar: 5, at: "1/2", length: "1/4"))
        XCTAssertTrue(AssistantDraftOffer.words(added, UIText(language: "en")).contains("G4"))
    }

    /// «Продолжить в чате» carries the question and answer as asked and answered in the new conversation (the one way on
    /// since v0.9: «Добавить в контекст», which joined them to the open one, is gone, Andrei's v0.8 finding 10).
    func testContinueInTheChatStartsAConversationAsAskedThere() {
        let (assistant, folder) = controller()
        defer { try? FileManager.default.removeItem(at: folder) }
        var exchange = AssistantExchange(id: "e1", question: "What is in bar 3?", scope: "This selection · bar 3")
        exchange.answer = [AnswerPiece(text: "Four notes")]
        assistant.start(exchange, answer: "Four notes [[key]]", role: "mentor")
        let chat = assistant.current
        XCTAssertEqual(chat?.history.map(\.kind), [.question, .answer], "the next question there has it")
        XCTAssertEqual(chat?.exchanges.first?.added, true)
        assistant.start(exchange, answer: "Again", role: "mentor")
        XCTAssertEqual(assistant.current?.history.count, 2)
        XCTAssertEqual(assistant.chats.count, 2, "each a new conversation, never into the one open")
        var drafted = exchange
        drafted.drafts = AssistantDraftOffer.offers(in: answered())
        assistant.start(drafted, answer: "A draft", role: "proposer")
        XCTAssertNil(assistant.current?.exchanges.last?.drafts, "as on the web, the question and answer move, not the drafts")
    }

    /// Andrei's v0.8 finding 4: an Ask opens the assistant empty, ready for a question, not on the conversation open before;
    /// nothing is made until the first question goes, which starts a conversation; the earlier one stays in the list.
    func testAnAskOpensTheAssistantEmptyAndTheFirstQuestionStartsAConversation() async {
        let (assistant, folder) = controller()
        defer { try? FileManager.default.removeItem(at: folder) }
        let bar3 = ScoreSelection(onset: 3840, end: 5760, firstBar: 3, lastBar: 3)
        assistant.start(AssistantExchange(id: "e0", question: "Earlier", scope: "Bar 1"), answer: "Before", role: "mentor")
        let earlier = assistant.currentId
        assistant.close()
        XCTAssertEqual(assistant.currentId, earlier, "closing the panel keeps its conversation")
        assistant.listShown = true
        assistant.open(wholePiece: false, selection: bar3)
        XCTAssertTrue(assistant.panelOpen)
        XCTAssertFalse(assistant.listShown)
        XCTAssertNil(assistant.current, "empty, ready for a question")
        assistant.close()
        assistant.open(wholePiece: true, selection: nil)
        XCTAssertNil(assistant.current)
        XCTAssertEqual(assistant.chats.count, 1, "no conversation without a question")
        XCTAssertEqual(AssistantStore(folder: folder).chats(of: "p1").count, 1)
        let signedOut = AssistantRelay(session: .shared, request: { _ in nil })
        await assistant.ask("Why?", about: AssistantAsk(
            piece: 0, selection: nil, wholePiece: true, scope: "The whole piece", language: "en", role: "mentor", relay: signedOut
        ))
        XCTAssertNotEqual(assistant.currentId, earlier, "the first question starts a new conversation")
        XCTAssertEqual(assistant.current?.exchanges.map(\.question), ["Why?"])
        XCTAssertEqual(assistant.chats.count, 2, "the earlier one stays in the list")
    }

    func testTheConversationsAreKeptOnTheIPadWithNoSwitch() {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("assistant-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: folder) }
        let defaults = UserDefaults(suiteName: "assistant-\(UUID().uuidString)")!
        let assistant = AssistantController(store: AssistantStore(folder: folder), defaults: defaults)
        assistant.load(piece: "p1")
        assistant.start(AssistantExchange(id: "e1", question: "Why?", scope: "Bar 3"), answer: "Because", role: "mentor")
        XCTAssertEqual(AssistantStore(folder: folder).chats(of: "p1").count, 1, "kept on the iPad by piece (version 0.6.3: no switch)")
        let again = AssistantController(store: AssistantStore(folder: folder), defaults: defaults)
        again.load(piece: "p1")
        XCTAssertEqual(again.current?.exchanges.map(\.question), ["Why?"], "the piece opens again on its newest conversation")
    }

    func testTheAssistantAtTheSelectionOpensFollowsAndCloses() {
        let (assistant, folder) = controller()
        defer { try? FileManager.default.removeItem(at: folder) }
        let bar3 = ScoreSelection(onset: 3840, end: 5760, firstBar: 3, lastBar: 3)
        let bar4 = ScoreSelection(onset: 5760, end: 7680, firstBar: 4, lastBar: 4)
        assistant.openHere(on: bar3, role: "proposer")
        XCTAssertEqual(assistant.here?.selection, bar3)
        XCTAssertEqual(assistant.here?.role, "proposer")
        XCTAssertNil(assistant.here?.exchange, "it opens empty, with no conversation before it")
        assistant.follow(bar4)
        XCTAssertEqual(assistant.here?.selection, bar4)
        assistant.continueHere()
        XCTAssertNotNil(assistant.here, "nothing to carry before an answer")
        XCTAssertTrue(assistant.chats.isEmpty)
        assistant.closeHere()
        XCTAssertNil(assistant.here)
    }

    /// «Продолжить в чате» (Andrei, 2026-11-18 14:10 UTC): the question and answer at the selection start a new
    /// conversation in the full assistant, whose panel opens on the selected bars; without the press nothing is kept.
    func testContinueInTheAssistantStartsAConversationWithTheQuestionAndAnswer() async {
        let (assistant, folder) = controller()
        defer { try? FileManager.default.removeItem(at: folder) }
        let bar3 = ScoreSelection(onset: 3840, end: 5760, firstBar: 3, lastBar: 3)
        assistant.start(AssistantExchange(id: "e0", question: "Earlier", scope: "Bar 1"), answer: "Before", role: "proposer")
        let earlier = assistant.currentId
        assistant.openHere(on: bar3, role: "proposer")
        assistant.continueHere()
        XCTAssertNotNil(assistant.here, "nothing to carry before an answer")
        XCTAssertFalse(assistant.panelOpen)
        let signedOut = AssistantRelay(session: .shared, request: { _ in nil })
        await assistant.askHere("Why?", about: AssistantAsk(
            piece: 0, selection: bar3, wholePiece: false, scope: "Bar 3", language: "en", role: "proposer", relay: signedOut
        ))
        XCTAssertEqual(assistant.chats.count, 1, "the chat at the selection keeps nothing by itself")
        assistant.continueHere()
        XCTAssertNil(assistant.here, "the small chat closes")
        XCTAssertTrue(assistant.panelOpen, "the full assistant opens")
        XCTAssertFalse(assistant.scope.isWholePiece(bar3), "on the selected bars")
        XCTAssertNotEqual(assistant.currentId, earlier, "a new conversation, not the one open before")
        XCTAssertEqual(assistant.chats.count, 2)
        XCTAssertEqual(assistant.current?.exchanges.map(\.question), ["Why?"], "it starts with the question asked at the selection")
        XCTAssertEqual(AssistantStore(folder: folder).chats(of: "p1").count, 2, "kept with the piece's conversations")
    }

    func testACancelledDraftIsSaidSoAndKeptSo() throws {
        let (assistant, folder) = controller()
        defer { try? FileManager.default.removeItem(at: folder) }
        let draft = try XCTUnwrap(AssistantDraftOffer.offers(in: answered()).first)
        var exchange = AssistantExchange(id: "e1", question: "Take out the repeated note", scope: "Bars 3–4")
        exchange.drafts = [draft]
        // A question asked in the panel keeps its drafts there (Продолжить в чате does not carry them).
        var chat = answered()
        chat.exchanges = [exchange]
        AssistantStore(folder: folder).keep([chat], of: "p1")
        assistant.load(piece: "p2")
        assistant.load(piece: "p1")
        assistant.cancel(draft)
        XCTAssertEqual(assistant.current?.exchanges.first?.drafts?.first?.state, .cancelled)
        let kept = AssistantStore(folder: folder).chats(of: "p1")
        XCTAssertEqual(kept.first?.exchanges.first?.drafts?.first?.state, .cancelled, "the decision is kept with the conversation")
        var marked = exchange
        marked.mark(draft.id, .proposed, problem: "No bar 99.")
        XCTAssertEqual(marked.drafts?.first?.problem, "No bar 99.")
        marked.mark("other", .accepted)
        XCTAssertEqual(marked.drafts?.first?.state, .proposed, "another draft's decision changes nothing here")
    }

    func testTheSmallChatGoesAboveTheKeyboardOnlyWhereItWouldBeCovered() {
        let card = CGRect(x: 100, y: 600, width: 320, height: 200)
        let keyboard = CGRect(x: 0, y: 700, width: 1024, height: 300)
        XCTAssertEqual(KeyboardLift.lift(of: card, keyboard: keyboard, gap: 12), 112, "the covered part and a gap")
        XCTAssertEqual(KeyboardLift.lift(of: card, keyboard: nil, gap: 12), 0)
        XCTAssertEqual(KeyboardLift.lift(of: card, keyboard: CGRect(x: 0, y: 900, width: 1024, height: 100), gap: 12), 0, "below it")
        XCTAssertEqual(KeyboardLift.lift(of: card, keyboard: CGRect(x: 600, y: 500, width: 300, height: 400), gap: 12), 0, "a floating one beside it")
        let high = CGRect(x: 100, y: 40, width: 320, height: 500)
        XCTAssertEqual(KeyboardLift.lift(of: high, keyboard: CGRect(x: 0, y: 300, width: 1024, height: 468), gap: 12), 28, "never above the window's top")
    }
}
