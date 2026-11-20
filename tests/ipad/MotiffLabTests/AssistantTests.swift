import XCTest

@testable import MotiffLab

/// The model-backed assistant on the iPad (#582 item 17): the turn format the device keeps and sends, the relay's answers,
/// one question's turns with the model's tool calls run on the device, and the conversations kept by piece.
final class AssistantTests: XCTestCase {
    private let context = JSONValue.parse(#"""
    {"digest": {"scope": "selection", "bars": {"from": 3, "to": 4}, "ids": ["key", "bars.3"],
     "items": [{"id": "key", "kind": "key", "text": "A minor", "certainty": "inferred"},
               {"id": "bars.3", "kind": "check", "text": "Bar 3 is to check", "bars": {"from": 3, "to": 3}}]}}
    """#)!

    private func chat(role: String = "mentor") -> AssistantChat {
        AssistantChat(id: "c1", started: Date(timeIntervalSince1970: 0), updated: Date(timeIntervalSince1970: 0), role: role)
    }

    func testJSONKeepsIntegersAndFindsFactsAnywhere() {
        let value = JSONValue.parse(#"{"onset": 1920, "ratio": 0.5, "on": true, "list": [1, "a", null]}"#)!
        XCTAssertEqual(value["onset"], .int(1920))
        XCTAssertEqual(value["ratio"], .double(0.5))
        XCTAssertEqual(value["on"], .bool(true))
        XCTAssertEqual(JSONValue.parse(value.text), value)
        XCTAssertTrue(value.text.contains("\"onset\":1920"), "an integer stays an integer for music-core")
        XCTAssertEqual(context.fact("bars.3")?.text, "Bar 3 is to check")
        XCTAssertEqual(context.fact("bars.3")?.bars, 3...3)
        XCTAssertNil(context.fact("nothing"))
    }

    func testAnAnswerShowsEachCitedFactInTheEnginesWords() {
        let pieces = AssistantChat.pieces(of: "It is in [[key]], and [[gone]] see [[bars.3]].", context: context, results: [])
        XCTAssertEqual(pieces.map(\.text), ["It is in ", "A minor", ", and ", " see ", "Bar 3 is to check", "."])
        XCTAssertEqual(pieces.compactMap(\.cites), ["key", "bars.3"], "an id the turn did not give is left out")
        let fromTool = AssistantChat.pieces(of: "[[n1]]", context: context, results: [
            AssistantToolOutcome(id: "t1", name: "notes.read", content: .object(["items": .array([.object(["id": .string("n1"), "text": .string("Four notes")])])])),
        ])
        XCTAssertEqual(fromTool, [AnswerPiece(text: "Four notes", cites: "n1")])
    }

    func testTheTurnIsFormatOneAndLeavesOutTheOldestExchangesBeyondForty() {
        var long = chat()
        for index in 0..<25 { long = long.asking("Q\(index)").answered("A\(index) [[key]]") }
        long = long.asking("Last")
        let turn = long.request(language: "ru", context: context)
        XCTAssertEqual(turn["version"], .int(1))
        XCTAssertEqual(turn["role"], .string("mentor"))
        XCTAssertEqual(turn["language"], .string("ru"))
        XCTAssertEqual(turn["context"], context)
        let history = turn["history"]?.items ?? []
        XCTAssertLessThanOrEqual(history.count, AssistantChat.maxEntries)
        XCTAssertEqual(history.first?["kind"], .string("question"), "it starts on a question")
        XCTAssertEqual(history.last?["text"], .string("Last"))
        XCTAssertNil(history.first?["calls"], "no empty fields are sent")
    }

    func testTheRepliesAndProblemsAreTheWebAppsOnes() {
        XCTAssertEqual(AssistantReply(.object(["kind": .string("notConfigured")])), .notConfigured)
        XCTAssertEqual(AssistantReply(JSONValue.parse(#"{"kind": "answer", "text": "x [[key]]", "fallback": true}"#)!), .answer(text: "x [[key]]", fallback: true))
        let called = AssistantReply(JSONValue.parse(#"{"kind": "toolCalls", "entry": {"kind": "toolCalls", "calls": [{"id": "1", "name": "notes.read", "arguments": {"from": 3, "to": 4}}]}}"#)!)
        XCTAssertEqual(called, .toolCalls([AssistantToolCall(id: "1", name: "notes.read", arguments: .object(["from": .int(3), "to": .int(4)]))]))
        XCTAssertNil(AssistantReply(.object(["kind": .string("other")])))
        XCTAssertEqual(RelayProblem.of(status: 403, body: Data(#"{"code": "pro"}"#.utf8)), .pro)
        XCTAssertEqual(RelayProblem.of(status: 429, body: Data(#"{"code": "dailyCap"}"#.utf8)), .dailyCap)
        XCTAssertEqual(RelayProblem.of(status: 401, body: Data()), .notSignedIn)
        XCTAssertEqual(RelayProblem.of(status: 500, body: Data()), .server, "the server's own error is tried again")
        XCTAssertEqual(RelayProblem.of(status: 503, body: Data()), .server, "a cold start's answer too")
        XCTAssertEqual(RelayProblem.of(status: 400, body: Data(#"{"code": "badRequest"}"#.utf8)), .refused)
        XCTAssertEqual(RelayProblem.of(status: 413, body: Data()), .tooLarge)
        XCTAssertEqual(AssistantStatus(configured: true, allowed: true, pro: true, left: 3).note, nil)
        XCTAssertEqual(AssistantStatus(configured: false, allowed: true, pro: true, left: 3).note, "mentor.notConfigured")
        XCTAssertEqual(AssistantStatus(configured: true, allowed: true, pro: false, left: 3).note, "mentor.pro")
        XCTAssertEqual(AssistantStatus(configured: true, allowed: true, pro: true, left: 0).note, "mentor.dailyCap")
    }

    /// Assistant option A item 4 (v1.2): a template answer says in words why the model's was not shown, from the API's
    /// additive "reason"; a reason this iPad does not know says it in general, and an answer with none (one kept from
    /// before, or an API without the field) says nothing more than before.
    func testAFallbackSaysWhyInWordsInBothLanguages() async {
        let reply = AssistantReply(JSONValue.parse(#"{"kind": "answer", "text": "x [[key]]", "fallback": true, "reason": "noCitation"}"#)!)
        XCTAssertEqual(reply, .answer(text: "x [[key]]", fallback: true, reason: "noCitation"))
        let shown = AssistantReply(JSONValue.parse(#"{"kind": "answer", "text": "x [[key]]", "fallback": false, "reason": "noCitation"}"#)!)
        XCTAssertEqual(shown, .answer(text: "x [[key]]", fallback: false), "a reason counts only with the template")

        let turns = AssistantTurns(send: { _ in (.answer(text: "A minor [[key]]", fallback: true, reason: "timeout"), nil) }, run: { _ in .null })
        let outcome = await turns.ask("Why?", in: chat(), context: context, language: "en")
        guard case let .answered(_, _, fallback, reason) = outcome else { return XCTFail("no answer") }
        XCTAssertTrue(fallback)
        XCTAssertEqual(reason, "timeout")

        var exchange = AssistantExchange(id: "e1", question: "Why?", scope: "Bar 3")
        exchange.fallback = true
        XCTAssertNil(exchange.reasonKey, "no reason named: nothing more is said")
        let reasons = ["empty", "tooLong", "noCitation", "unknownFact", "timeout", "modelError", "toolNotAllowed", "badArguments", "tooManyCalls"]
        var keys: [String] = []
        for named in reasons + ["somethingNew"] {
            exchange.reason = named
            keys.append(try XCTUnwrap(exchange.reasonKey))
        }
        XCTAssertEqual(Set(keys).count, reasons.count + 1, "each reason its own words")
        XCTAssertEqual(keys.last, "mentor.reason.other", "an unknown reason in general words")
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in keys { XCTAssertNotEqual(text[key], key, "\(key) in \(language)") }
        }
        XCTAssertEqual(UIText(language: "ru")["mentor.reason.noCitation"], "Почему: ответ модели не ссылался ни на один факт приложения.")
        exchange.fallback = false
        XCTAssertNil(exchange.reasonKey, "the model's answer shown needs no reason")
    }

    /// Assistant option A item 6 (v1.2): a `practice.suggest` result is a button in the engine's words with its bars, hand,
    /// tempo and loop; a refused call, or another tool's result, is none.
    func testAPracticeSuggestionIsAButtonFromItsResult() {
        let suggested = JSONValue.parse(#"""
        {"tool": "practice.suggest", "kind": "suggestAction", "ok": true,
         "result": {"action": {"bars": {"from": 3, "to": 4}, "hand": "left", "tempo": 60, "loop": true},
                    "label": "Зациклить такты 3–4 в темпе 60 % · левая рука"}}
        """#)!
        let refused = JSONValue.parse(#"{"tool": "practice.suggest", "kind": "suggestAction", "ok": false, "refusal": "В пьесе такты с 0 по 105."}"#)!
        let read = JSONValue.parse(#"{"tool": "notes.list", "kind": "read", "ok": true, "result": {"notes": []}}"#)!
        let calls = [
            AssistantToolCall(id: "c1", name: "practice.suggest", arguments: .object([:])),
            AssistantToolCall(id: "c2", name: "practice.suggest", arguments: .object([:])),
            AssistantToolCall(id: "c3", name: "notes.list", arguments: .object([:])),
        ]
        let answered = chat(role: "proposer").asking("How do I practise bars 3–4?").called(calls).resulted([
            AssistantToolOutcome(id: "c1", name: "practice.suggest", content: suggested),
            AssistantToolOutcome(id: "c2", name: "practice.suggest", content: refused),
            AssistantToolOutcome(id: "c3", name: "notes.list", content: read),
        ]).answered("Slowly [[key]]")
        let offers = AssistantPracticeOffer.offers(in: answered)
        XCTAssertEqual(offers, [AssistantPracticeOffer(first: 3, last: 4, hand: "left", tempo: 60, loop: true, label: "Зациклить такты 3–4 в темпе 60 % · левая рука")])
        XCTAssertTrue(AssistantDraftOffer.offers(in: answered).isEmpty, "a suggestion is no draft")
    }

    func testAQuestionRunsTheModelsCallsOnTheDeviceUntilTheAnswer() async {
        var sent: [JSONValue] = []
        var ran: [String] = []
        let turns = AssistantTurns(
            send: { body in
                let turn = JSONValue.parse(body)!
                sent.append(turn)
                if turn["history"]?.items.last?["kind"] == .string("question") {
                    return (.toolCalls([AssistantToolCall(id: "c1", name: "notes.read", arguments: .object(["from": .int(3), "to": .int(3)]))]), nil)
                }
                return (.answer(text: "Four notes in [[key]].", fallback: false), nil)
            },
            run: { call in
                ran.append(call.name)
                return .object(["tool": .string(call.name), "ok": .bool(true)])
            }
        )
        let outcome = await turns.ask("What is in bar 3?", in: chat(), context: context, language: "en")
        XCTAssertEqual(ran, ["notes.read"])
        XCTAssertEqual(sent.count, 2)
        XCTAssertEqual(sent[1]["history"]?.items.map { $0["kind"]?.string }, ["question", "toolCalls", "toolResults"])
        guard case let .answered(answered, text, fallback, _) = outcome else { return XCTFail("no answer") }
        XCTAssertEqual(text, "Four notes in [[key]].")
        XCTAssertFalse(fallback)
        XCTAssertEqual(answered.history.map(\.kind), [.question, .toolCalls, .toolResults, .answer])
        XCTAssertEqual(answered.resultsInQuestion.map(\.id), ["c1"])
    }

    func testAFailedTurnSendsNothingMoreAndChangesNothing() async {
        let refused = AssistantTurns(send: { _ in (nil, .dailyCap) }, run: { _ in .null })
        let outcome = await refused.ask("Why?", in: chat(), context: context, language: "en")
        XCTAssertEqual(outcome, .unanswered("mentor.dailyCap"))
        let off = AssistantTurns(send: { _ in (.notConfigured, nil) }, run: { _ in .null })
        let none = await off.ask("Why?", in: chat(), context: context, language: "en")
        XCTAssertEqual(none, .unanswered("mentor.notConfigured"))
    }

    func testCallsPastTheCapAreRefusedHereAsMusicCoreRefusesThem() async {
        var ran = 0
        let turns = AssistantTurns(
            send: { body in
                let turn = JSONValue.parse(body)!
                if (turn["history"]?.items.count ?? 0) > 1 { return (.answer(text: "Done [[key]]", fallback: false), nil) }
                let calls = (1...10).map { AssistantToolCall(id: "c\($0)", name: "notes.read", arguments: .object([:])) }
                return (.toolCalls(calls), nil)
            },
            run: { _ in
                ran += 1
                return .object(["ok": .bool(true)])
            }
        )
        let outcome = await turns.ask("All of it", in: chat(), context: context, language: "ru")
        XCTAssertEqual(ran, AssistantChat.mostToolCalls)
        guard case let .answered(answered, _, _, _) = outcome else { return XCTFail("no answer") }
        let refusals = answered.resultsInQuestion.filter { $0.content["ok"] == .bool(false) }
        XCTAssertEqual(refusals.count, 2)
        XCTAssertTrue(refusals[0].content["refusal"]?.string?.hasPrefix("На один вопрос") ?? false)
    }

    func testAFacadeErrorIsARefusalTheModelReads() {
        let refused = AssistantTurns.content(ofFacade: #"{"error": "badRequest", "message": "No bar 99."}"#, tool: "notes.read")
        XCTAssertEqual(refused, .object(["tool": .string("notes.read"), "ok": .bool(false), "refusal": .string("No bar 99.")]))
        let read = AssistantTurns.content(ofFacade: #"{"tool": "notes.read", "ok": true}"#, tool: "notes.read")
        XCTAssertEqual(read["ok"], .bool(true))
    }

    func testThePiecesConversationsStayOnTheIPadNewestFirst() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("assistant-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: folder) }
        let store = AssistantStore(folder: folder)
        XCTAssertEqual(store.chats(of: "p1"), [])
        var older = chat()
        older.exchanges = [AssistantExchange(id: "e1", question: "First?", scope: "The whole piece")]
        var newer = older
        newer.id = "c2"
        newer.updated = Date(timeIntervalSince1970: 10)
        let empty = AssistantChat(id: "c3", started: Date(), updated: Date(), role: "mentor")
        store.keep([older, newer, empty], of: "piece/1 é")
        XCTAssertEqual(store.chats(of: "piece/1 é").map(\.id), ["c2", "c1"], "newest first, an empty one not kept")
        XCTAssertEqual(store.chats(of: "p1"), [], "another piece has its own")
        XCTAssertEqual(store.file(of: "a/b").lastPathComponent, "a_b.json")
    }
}
