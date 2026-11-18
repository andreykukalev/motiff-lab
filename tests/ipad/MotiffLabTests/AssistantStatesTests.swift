import XCTest

@testable import MotiffLab

/// The real assistant on the iPad (version 0.6.3, the lead's decision 3): every Ask reaches the model through the API, with
/// no switch; what a question looks at follows the selection (the bug "the assistant does not see the selected bars");
/// the states while a question is on its way; and why a question got no answer, with what to do: sign in, or try again.
@MainActor
final class AssistantStatesTests: XCTestCase {
    private let bar3 = ScoreSelection(onset: 3840, end: 5760, firstBar: 3, lastBar: 3)
    private let bar4 = ScoreSelection(onset: 5760, end: 7680, firstBar: 4, lastBar: 4)

    private func controller(consent: Bool = true) -> (AssistantController, URL) {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("assistant-\(UUID().uuidString)")
        let defaults = UserDefaults(suiteName: "assistant-\(UUID().uuidString)")!
        let assistant = AssistantController(store: AssistantStore(folder: folder), defaults: defaults)
        assistant.load(piece: "p1")
        assistant.consent(consent)
        return (assistant, folder)
    }

    /// Nobody signed in: the relay has no token, so nothing is sent.
    private let signedOut = AssistantRelay(session: .shared, request: { _ in nil })

    /// A relay to a pretend API whose answers each test sets (`AssistantStub.answers`, by the path's last part).
    private func stubbed() -> AssistantRelay {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [AssistantStub.self]
        return AssistantRelay(session: URLSession(configuration: configuration), request: { path in
            URLRequest(url: URL(string: "https://api.example.test/\(path)")!)
        })
    }

    private func ask(_ relay: AssistantRelay, selection: ScoreSelection? = nil) -> AssistantAsk {
        AssistantAsk(piece: 0, selection: selection, wholePiece: selection == nil, scope: "Bar 3", language: "en", role: "mentor", relay: relay)
    }

    func testTheBarsSelectedAfterAskAboutThePieceAreWhatTheNextQuestionIsAbout() {
        XCTAssertTrue(AssistantScope.selection.isWholePiece(nil), "with nothing selected, the whole piece")
        XCTAssertFalse(AssistantScope.selection.isWholePiece(bar3), "with bars selected, those bars")
        let whole = AssistantScope.choose(wholePiece: true, selection: bar3)
        XCTAssertTrue(whole.isWholePiece(bar3), "the whole piece, chosen while bar 3 stands")
        XCTAssertFalse(whole.isWholePiece(bar4), "bar 4 selected after it: the question is about bar 4")
        XCTAssertEqual(AssistantScope.choose(wholePiece: true, selection: nil), .selection)
        XCTAssertEqual(AssistantScope.choose(wholePiece: false, selection: bar3), .selection)

        let (assistant, folder) = controller()
        defer { try? FileManager.default.removeItem(at: folder) }
        assistant.open(wholePiece: true, selection: nil)
        XCTAssertTrue(assistant.panelOpen)
        XCTAssertTrue(assistant.scope.isWholePiece(nil))
        XCTAssertFalse(assistant.scope.isWholePiece(bar3), "Ask about the piece, then bars selected: they reach the model")
        assistant.choose(wholePiece: true, selection: bar3)
        XCTAssertTrue(assistant.scope.isWholePiece(bar3))
        assistant.open(wholePiece: false, selection: bar3)
        XCTAssertFalse(assistant.scope.isWholePiece(bar3), "Ask on the selection looks at it")
    }

    /// Andrei, 2026-11-18: picking the selected bars in the panel scrolls the score to them (the page's `reveal`, the one
    /// way the app brings a passage into view); the whole piece moves nothing, and nor does the panel opening on them.
    func testPickingTheSelectedBarsBringsThemIntoView() async {
        let (assistant, folder) = controller()
        defer { try? FileManager.default.removeItem(at: folder) }
        let score = ScoreController()
        let page = RecordingScorePage()
        var revealed: [Double] = []
        page.called = { command, argument in
            if command == .reveal, let start = (argument as? [String: Any])?["start"] as? Double { revealed.append(start) }
        }
        score.scorePage = page
        assistant.load(piece: "p1", score: score)
        assistant.open(wholePiece: false, selection: bar3)
        XCTAssertEqual(revealed, [], "the panel opening on the selection does not scroll")
        await assistant.pickShowingBars(wholePiece: true, selection: bar3)
        XCTAssertTrue(assistant.scope.isWholePiece(bar3))
        XCTAssertEqual(revealed, [], "the whole piece does not scroll")
        await assistant.pickShowingBars(wholePiece: false, selection: bar3)
        XCTAssertFalse(assistant.scope.isWholePiece(bar3))
        XCTAssertEqual(revealed, [bar3.onset], "the selected bars come into view")
    }

    func testEachProblemSaysWhatToDo() {
        XCTAssertEqual(AssistantRecovery.of("mentor.signIn"), .signIn)
        XCTAssertEqual(AssistantRecovery.shown("mentor.signIn"), "padAssistant.signIn")
        for retried in ["mentor.unreachable", "padAssistant.serverError", "mentor.notConfigured"] {
            XCTAssertEqual(AssistantRecovery.of(retried), .retry, retried)
        }
        for said in ["mentor.pro", "mentor.notAllowed", "mentor.dailyCap", "mentor.tooLarge", "mentor.tooManyBars", "mentor.refused"] {
            XCTAssertEqual(AssistantRecovery.of(said), .nothing, said)
            XCTAssertEqual(AssistantRecovery.shown(said), said)
        }
        XCTAssertTrue(AssistantRecovery.isPro("mentor.pro"))
        XCTAssertFalse(AssistantRecovery.isPro("mentor.signIn"))
        XCTAssertTrue(AssistantRecovery.stopsAsking("mentor.signIn"))
        XCTAssertTrue(AssistantRecovery.stopsAsking("mentor.pro"))
        XCTAssertFalse(AssistantRecovery.stopsAsking("mentor.unreachable"), "asking again is how a lost connection is tried again")
        XCTAssertFalse(AssistantRecovery.stopsAsking("padAssistant.serverError"))
        XCTAssertFalse(AssistantRecovery.stopsAsking(nil))
        XCTAssertEqual(RelayProblem.server.note, "padAssistant.serverError")
    }

    func testEveryStateAndProblemIsSaidInBothLanguages() {
        let problems: [RelayProblem] = [.notSignedIn, .unreachable, .notAllowed, .pro, .tooLarge, .dailyCap, .refused, .server]
        let notes = problems.map(\.note) + ["mentor.noConsent", "mentor.notConfigured", "mentor.tooManyBars"]
        let keys = notes.map(AssistantRecovery.shown) + [AssistantPhase.sending, .answering, .lookingUp].compactMap(\.key) + ["padAssistant.retry"]
        XCTAssertNil(AssistantPhase.idle.key)
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in keys {
                XCTAssertNotEqual(text[key], key, "\(key) in \(language)")
            }
        }
        XCTAssertTrue(UIText(language: "ru")["padAssistant.signIn"].hasPrefix("Войдите, чтобы говорить с помощником"))
    }

    func testSignedOutTheAssistantSaysSignInAndSendsNothing() async {
        AssistantStub.answers = [:]
        let (assistant, folder) = controller()
        defer { try? FileManager.default.removeItem(at: folder) }
        await assistant.check(signedOut)
        XCTAssertEqual(assistant.standing, "mentor.signIn")
        XCTAssertFalse(assistant.checking)
        await assistant.ask("Why?", about: ask(signedOut))
        XCTAssertEqual(assistant.current?.exchanges.last?.note, "mentor.signIn")
        XCTAssertNil(assistant.pending, "the question on its way joined the conversation")
        XCTAssertEqual(assistant.phase, .idle)
        XCTAssertFalse(assistant.busy)
        XCTAssertEqual(assistant.current?.history, [], "a question with no answer is not in what the next turn sends")
    }

    func testWithoutConsentNothingIsAskedOfTheAPI() async {
        let (assistant, folder) = controller(consent: false)
        defer { try? FileManager.default.removeItem(at: folder) }
        AssistantStub.answers = ["status": (200, Data(#"{"configured": true, "allowed": true, "pro": true, "left": 5}"#.utf8))]
        await assistant.ask("Why?", about: ask(stubbed()))
        XCTAssertEqual(assistant.current?.exchanges.last?.note, "mentor.noConsent")
        XCTAssertEqual(AssistantStub.asked, [], "not even the status")
    }

    func testTryAgainTakesTheUnansweredQuestionOutToAskItAgain() async {
        let (assistant, folder) = controller()
        defer { try? FileManager.default.removeItem(at: folder) }
        await assistant.ask("Why?", about: ask(signedOut))
        guard let failed = assistant.current?.exchanges.last else { return XCTFail("the question with no answer is shown") }
        XCTAssertEqual(assistant.forget(failed), "Why?")
        XCTAssertEqual(assistant.current?.exchanges ?? [], [], "it goes, to come back with its answer")
        XCTAssertNil(assistant.forget(failed), "only once")
        XCTAssertNil(assistant.forget(AssistantExchange(id: "e9", question: "Answered", scope: "Bar 3")), "an answered one stays")
    }

    func testTheAPIsAnswersBecomeTheStatesThePanelShows() async {
        let relay = stubbed()
        AssistantStub.answers = ["status": (200, Data(#"{"configured": true, "allowed": true, "pro": false, "left": 5}"#.utf8))]
        let (status, problem) = await relay.status()
        XCTAssertEqual(AssistantStanding.note(status: status, problem: problem), "mentor.pro", "the Pro gate as the API says it")
        AssistantStub.answers = ["status": (200, Data(#"{"configured": true, "allowed": true, "pro": true, "left": 5}"#.utf8))]
        let ready = await relay.status()
        XCTAssertNil(AssistantStanding.note(status: ready.0, problem: ready.1))
        AssistantStub.answers = ["status": (401, Data())]
        let expired = await relay.status()
        XCTAssertEqual(AssistantStanding.note(status: expired.0, problem: expired.1), "mentor.signIn")
        AssistantStub.answers = [:]
        let offline = await relay.status()
        XCTAssertEqual(AssistantStanding.note(status: offline.0, problem: offline.1), "mentor.unreachable", "no connection")

        AssistantStub.answers = ["turn": (503, Data("Service Unavailable".utf8))]
        let failed = await relay.turn(Data("{}".utf8))
        XCTAssertNil(failed.0)
        XCTAssertEqual(failed.1, .server)
        AssistantStub.answers = ["turn": (200, Data(#"{"kind": "something else"}"#.utf8))]
        let unread = await relay.turn(Data("{}".utf8))
        XCTAssertEqual(unread.1, .server, "an answer the iPad cannot read is the server's error")
        AssistantStub.answers = ["turn": (200, Data(#"{"kind": "answer", "text": "A minor [[key]]", "fallback": false}"#.utf8))]
        let answered = await relay.turn(Data("{}".utf8))
        XCTAssertEqual(answered.0, .answer(text: "A minor [[key]]", fallback: false))
        XCTAssertNil(answered.1)
    }

    func testTheNextQuestionSendsTheConversationBeforeIt() async {
        let context = JSONValue.parse(#"{"digest": {"ids": ["key"], "items": [{"id": "key", "kind": "key", "text": "A minor"}]}}"#)!
        let before = AssistantChat(id: "c1", started: Date(timeIntervalSince1970: 0), updated: Date(timeIntervalSince1970: 0), role: "mentor")
            .asking("What key is it in?")
            .answered("A minor [[key]]")
        var sent: JSONValue?
        let turns = AssistantTurns(send: { body in
            sent = JSONValue.parse(body)
            return (.answer(text: "It stays in A minor [[key]]", fallback: false), nil)
        }, run: { _ in .null })
        let outcome = await turns.ask("Does it change?", in: before, context: context, language: "en")
        guard case let .answered(answered, _, _) = outcome else { return XCTFail("answered") }
        guard case let .array(history)? = sent?["history"] else { return XCTFail("a history") }
        XCTAssertEqual(history.count, 3, "the first question, its answer and the new question")
        XCTAssertEqual(sent?["context"], context, "the passage's facts digest goes with every turn")
        XCTAssertEqual(answered.history.map(\.kind), [.question, .answer, .question, .answer])
    }
}

/// A pretend API for the relay: answers by the request path's last part ("status", "turn"); no answer is a lost connection.
final class AssistantStub: URLProtocol {
    /// Set at the start of each test, which forgets what was asked before.
    static var answers: [String: (Int, Data)] = [:] {
        didSet { asked = [] }
    }
    static var asked: [String] = []

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        let path = request.url?.lastPathComponent ?? ""
        Self.asked.append(path)
        guard let (status, body) = Self.answers[path], let url = request.url,
              let response = HTTPURLResponse(url: url, statusCode: status, httpVersion: nil, headerFields: nil)
        else {
            client?.urlProtocol(self, didFailWithError: URLError(.notConnectedToInternet))
            return
        }
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: body)
        client?.urlProtocolDidFinishLoading(self)
    }

    override func stopLoading() {}
}
