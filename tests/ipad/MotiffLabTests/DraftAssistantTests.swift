import XCTest

@testable import MotiffLab

/// The assistant in a draft (v1.2, ADR 0034 section 6, the plan's answer 7): a question about a place sends music-core's
/// `drafts.context` and runs the model's tools by `drafts.call`, only after the draft's own consent; what a tool proposes
/// is never applied: it is heard after (`drafts.proposal`), refused, or accepted by the player (`drafts.accept`), a step
/// of the draft its toast undoes. Over the music-core of the tests and a pretend API (`AssistantStub`), no network.
@MainActor
final class DraftAssistantTests: XCTestCase {
    /// Holds a check made to wait, to be let go by the test.
    private final class Gate {
        var waiting: CheckedContinuation<Void, Never>?
    }

    private var opened: [Int32] = []
    private var folder: URL?

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
        if let folder { try? FileManager.default.removeItem(at: folder) }
        AssistantStub.answers = [:]
        AssistantStub.turns = []
    }

    private func controller() -> AssistantController {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("assistant-\(UUID().uuidString)")
        self.folder = folder
        let defaults = UserDefaults(suiteName: "assistant-\(UUID().uuidString)")!
        return AssistantController(store: AssistantStore(folder: folder), defaults: defaults)
    }

    private func stubbed() -> AssistantRelay {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [AssistantStub.self]
        return AssistantRelay(session: URLSession(configuration: configuration), request: { path in
            URLRequest(url: URL(string: "https://api.example.test/\(path)")!)
        })
    }

    /// The tool result music-core writes for `place.take` (tests/contracts/facade-answers/drafts.assistant.json).
    private let takeResult = JSONValue.parse(#"""
        {"tool": "place.take", "kind": "proposeDraft", "ok": true, "drafts": [{"tool": "place.take", "applied": false,
         "title": "Take technique 1", "explanation": "Into ① from Notes 5–8: technique 1, simplification.", "draft": "d_0001",
         "place": "pl_0001", "structure": false, "steps": [{"kind": "take", "technique": {"kind": "simplify", "bars": [4]}}]}]}
        """#)!

    /// r-1: a rotation or Split View lays the panel and a draft's card out anew, their fields with them: the question typed
    /// and not sent is still there in the field made again, until it is sent, or a draft's place's chat closes or opens on
    /// another place.
    func testAQuestionTypedAndNotSentOutlivesItsField() {
        let assistant = controller()
        assistant.open(wholePiece: true, selection: nil)
        assistant.keepUnsent("Почему здесь доминанта?")
        var remade = AskFlow(surface: .panel)
        remade.apply(.open)
        remade.apply(.type(assistant.unsentInPanel))
        XCTAssertEqual(remade.draft, "Почему здесь доминанта?", "the panel's field made again")

        let first = AssistantDraftPlace(draft: "d_0001", place: "pl_0001", label: "①", basis: [])
        let second = AssistantDraftPlace(draft: "d_0001", place: "pl_0002", label: "②", basis: [])
        assistant.keepUnsent("Which bridge?", inDraft: true)
        XCTAssertEqual(assistant.unsentInDraft, "", "none kept while no place's assistant is open")
        assistant.openInDraft(first)
        assistant.keepUnsent("Which bridge?", inDraft: true)
        XCTAssertEqual(assistant.unsentInDraft, "Which bridge?")
        assistant.startInDraft(role: AssistantDraftPlace.explainer)
        XCTAssertEqual(assistant.unsentInDraft, "Which bridge?", "another role in the same place keeps it")
        assistant.openInDraft(second)
        XCTAssertEqual(assistant.unsentInDraft, "", "another place's assistant starts empty")
        assistant.keepUnsent("And here?", inDraft: true)
        assistant.closeInDraft()
        XCTAssertEqual(assistant.unsentInDraft, "")
    }

    /// v1.5, answer 16: the panel's question typed and not sent outlives closing the panel, the field emptied as it closes
    /// keeping it, and stands in the field when the panel opens again, by the pill or by an Ask; it goes once sent and when
    /// the screen's piece is another.
    func testThePanelsUnsentQuestionOutlivesClosingThePanel() {
        let assistant = controller()
        assistant.load(piece: "first")
        assistant.open(wholePiece: true, selection: nil)
        var field = AskFlow(surface: .panel)
        field.apply(.open)
        field.apply(.type("Почему здесь доминанта?"))
        assistant.keepUnsent(field.draft)
        field.apply(.close)
        assistant.close()
        // The field's own change, emptied by its close, reaches the controller after the panel has closed.
        assistant.keepUnsent(field.draft)
        XCTAssertEqual(assistant.unsentInPanel, "Почему здесь доминанта?", "closed, the panel keeps the question")

        assistant.show()
        var reopened = AskFlow(surface: .panel)
        reopened.apply(.open)
        reopened.apply(.type(assistant.unsentInPanel))
        XCTAssertEqual(reopened.draft, "Почему здесь доминанта?", "opened again by the pill")
        assistant.close()
        assistant.open(wholePiece: false, selection: nil)
        XCTAssertEqual(assistant.unsentInPanel, "Почему здесь доминанта?", "opened again by an Ask")

        assistant.load(piece: "first")
        XCTAssertEqual(assistant.unsentInPanel, "Почему здесь доминанта?", "the same piece on a screen made again keeps it")
        let sent = reopened.apply(.send)
        XCTAssertEqual(sent, "Почему здесь доминанта?")
        assistant.keepUnsent(reopened.draft)
        XCTAssertEqual(assistant.unsentInPanel, "", "sent, it goes")

        assistant.keepUnsent("Где кульминация?")
        assistant.close()
        assistant.load(piece: "second")
        XCTAssertEqual(assistant.unsentInPanel, "", "another piece's panel starts empty")
    }

    func testAStepProposedInAPlaceKeepsTheCallThatMakesItAgain() throws {
        let arguments = JSONValue.object(["techniques": .array([.int(1)]), "bar": .null])
        let chat = AssistantChat(id: "c1", started: Date(), updated: Date(), role: AssistantDraftPlace.role)
            .asking("Assemble the place")
            .called([AssistantToolCall(id: "t1", name: "place.take", arguments: arguments)])
            .resulted([AssistantToolOutcome(id: "t1", name: "place.take", content: takeResult)])
            .answered("Take it [[technique.1]]")
        let proposal = try XCTUnwrap(AssistantPlaceProposal.offers(in: chat).first)
        XCTAssertEqual(proposal.tool, "place.take")
        XCTAssertEqual(proposal.parameters, arguments)
        XCTAssertEqual(proposal.role, "placeAssembler", "music-core checks the call against the turn's role again")
        XCTAssertEqual(proposal.draft, "d_0001")
        XCTAssertEqual(proposal.place, "pl_0001")
        XCTAssertEqual(proposal.title, "Take technique 1")
        XCTAssertFalse(proposal.structure)
        XCTAssertEqual(proposal.state, .proposed, "never applied by itself")
        XCTAssertEqual(proposal.words, "Take technique 1")
        XCTAssertEqual(proposal.callFields["tool"] as? String, "place.take")
        XCTAssertEqual(proposal.callFields["place"] as? String, "pl_0001")
        XCTAssertEqual((proposal.callFields["parameters"] as? [String: Any])?["techniques"] as? [Int], [1])
        XCTAssertEqual(AssistantDraftOffer.offers(in: chat), [], "a step in a draft is no draft of the notes")
        let mentor = AssistantChat(id: "c2", started: Date(), updated: Date(), role: "mentor").asking("Why?").answered("Because")
        XCTAssertEqual(AssistantPlaceProposal.offers(in: mentor), [], "no tool call, no proposal")
    }

    func testTheDraftsConsentIsItsOwnAndListsWhatAQuestionInADraftSends() {
        let assistant = controller()
        assistant.consent(true)
        XCTAssertTrue(assistant.consentGiven)
        XCTAssertFalse(assistant.draftConsentGiven, "the piece's consent does not cover a draft")
        assistant.consent(true, inDraft: true)
        XCTAssertTrue(assistant.draftConsentGiven)
        assistant.consent(false)
        XCTAssertTrue(assistant.draftConsentGiven, "each is withdrawn on its own")
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in [AssistantConsent.Wording.draft.title, AssistantConsent.Wording.draft.lead, AssistantConsent.Wording.draft.not]
                + AssistantConsent.Wording.draft.sent {
                XCTAssertNotEqual(text[key], key, "\(key) in \(language)")
            }
        }
        XCTAssertTrue(UIText(language: "ru")["mentor.draftConsentNotes"].contains("не больше 32 тактов"))
        XCTAssertTrue(UIText(language: "ru")["mentor.draftConsentNot"].contains("звук записи"))
    }

    /// The kit's piece open with its Sources, and a draft of bars 1–4 (`then`: what else goes into it before it is opened).
    private func drafted(then: (SourcesController, SourceLayers.Draft, DraftViewAnswer.Place) async -> Void) async throws -> (SourcesController, SourceLayers.Draft, OpenPiece) {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        let sources = ScoreWorkspace(piece: piece).sources
        await sources.load(piece: piece.id, file: nil)
        let made = await sources.change("drafts.add", ["bars": ["first": 1, "last": 4], "now": "2026-11-20T10:00:00Z"])
        XCTAssertNotNil(made, sources.status ?? "")
        let draft = try XCTUnwrap(sources.layers.drafts.first)
        let opening = await sources.openDraft(draft)
        let first = try XCTUnwrap(opening, sources.status ?? "")
        opened.append(first.id)
        let place = try XCTUnwrap(first.draft?.places.first)
        await then(sources, draft, place)
        let reopened = await sources.openDraft(draft)
        let view = try XCTUnwrap(reopened, sources.status ?? "")
        opened.append(view.id)
        return (sources, draft, view)
    }

    /// A draft whose place ① has bars 5–8 as a variant besides its base, bars 1–4: its first приём's number.
    private func placeWithVariant() async throws -> (SourcesController, SourceLayers.Draft, DraftViewAnswer.Place, OpenPiece, Int) {
        let (sources, draft, view) = try await drafted { sources, draft, place in
            let fields: [String: Any] = ["bars": ["first": 5, "last": 8], "draft": draft.id, "drop": "variant", "place": place.id, "now": "2026-11-20T10:05:00Z"]
            let added = await sources.change("drafts.add", fields)
            XCTAssertNotNil(added, sources.status ?? "")
        }
        let place = try XCTUnwrap(view.draft?.places.first)
        let techniques = DraftTechniquesModel()
        await techniques.load(view.draft, on: view.id)
        let number = try XCTUnwrap(techniques.spots.first?.technique.number, "Für Elise's bar 8 against bar 4: one приём")
        return (sources, draft, place, view, number)
    }

    private let allowed = (200, Data(#"{"configured": true, "allowed": true, "pro": true, "left": 5}"#.utf8))

    func testAQuestionInAPlaceProposesAStepThePlayerHearsRefusesAndAccepts() async throws {
        let (sources, draft, place, view, number) = try await placeWithVariant()
        let assistant = controller()
        let here = AssistantDraftPlace(draft: draft.id, place: place.id, label: place.label)
        assistant.consent(true)
        assistant.openInDraft(here)
        let ask = AssistantAsk(piece: view.id, selection: nil, wholePiece: false, scope: place.label, language: "en", role: AssistantDraftPlace.role, relay: stubbed(), place: here)
        AssistantStub.answers = ["status": (200, Data(#"{"configured": true, "allowed": true, "pro": true, "left": 5}"#.utf8))]
        await assistant.askInDraft("Assemble the place", about: ask)
        XCTAssertEqual(assistant.inDraft?.exchange?.note, "mentor.noConsent", "the piece's consent sends nothing from a draft")
        XCTAssertEqual(AssistantStub.asked, [], "not even the status")

        assistant.consent(true, inDraft: true)
        AssistantStub.answers = ["status": (200, Data(#"{"configured": true, "allowed": true, "pro": true, "left": 5}"#.utf8))]
        AssistantStub.turns = [
            Data(#"{"kind": "toolCalls", "entry": {"kind": "toolCalls", "calls": [{"id": "t1", "name": "place.take", "arguments": {"techniques": [\#(number)], "bar": null}}]}}"#.utf8),
            Data(#"{"kind": "answer", "text": "Take it [[technique.\#(number)]]", "fallback": false}"#.utf8),
        ]
        await assistant.askInDraft("Assemble the place", about: ask)
        let exchange = try XCTUnwrap(assistant.inDraft?.exchange)
        XCTAssertNil(exchange.note, "answered")
        XCTAssertEqual(exchange.answer.compactMap(\.cites), ["technique.\(number)"], "the place's fact, found in what drafts.context sent")
        let proposal = try XCTUnwrap(exchange.proposals?.first, "music-core's place.take proposed the take")
        XCTAssertEqual(proposal.place, place.id)
        XCTAssertEqual(sources.layers.drafts.first?.taken, 0, "nothing applied by the question")

        let proposed = await PieceCore(piece: view.id).openProposal(proposal.callFields)
        let after = try XCTUnwrap(proposed.value, "«▶ После»: the draft as it would be")
        opened.append(after.piece)
        XCTAssertEqual(ScoreController.proposedSpan(of: place.id, in: after.draft, before: view.draft), Double(place.onset)..<Double(place.end))
        XCTAssertEqual(sources.layers.drafts.first?.taken, 0, "heard after, still nothing applied")

        await acceptIsHeldWhileAStepIsOnItsWay(proposal, assistant, sources, place: place, draft: draft.id)

        assistant.refuse(proposal)
        XCTAssertEqual(assistant.inDraft?.exchange?.proposals?.first?.state, .cancelled, "«Отказаться» changes nothing but the card")
        let kept = await assistant.accept(proposal) {
            await sources.acceptProposal(proposal.callFields, of: proposal.draft, words: proposal.words)
        }
        XCTAssertTrue(kept, sources.status ?? "")
        XCTAssertEqual(assistant.inDraft?.exchange?.proposals?.first?.state, .accepted)
        XCTAssertEqual(sources.layers.drafts.first?.taken, 1, "«Принять», the player's tap, takes it")
        guard case .draftStep(let undone)? = sources.toast?.undo else { return XCTFail("its toast undoes the step") }
        XCTAssertEqual(undone, draft.id)
        await sources.undo()
        XCTAssertEqual(sources.layers.drafts.first?.taken, 0, "undone as any step of the draft")

        // Too dense for one turn (the API's 413): no fewer bars can be selected in a draft, so it says the place is.
        AssistantStub.answers = ["status": (200, Data(#"{"configured": true, "allowed": true, "pro": true, "left": 5}"#.utf8)), "turn": (413, Data())]
        await assistant.askInDraft("Assemble the place", about: ask)
        XCTAssertEqual(assistant.inDraft?.exchange?.note, "mentor.placeTooLarge", "never «select fewer bars» in a draft")
        // A place no longer in the draft: music-core reads no context, and nothing goes.
        let gone = AssistantDraftPlace(draft: draft.id, place: "pl_9999", label: place.label)
        assistant.openInDraft(gone)
        var lost = ask
        lost.place = gone
        AssistantStub.answers = ["status": (200, Data(#"{"configured": true, "allowed": true, "pro": true, "left": 5}"#.utf8))]
        await assistant.askInDraft("Assemble the place", about: lost)
        XCTAssertEqual(assistant.inDraft?.exchange?.note, "mentor.placeUnread", "not the 32 bars: the place could not be read")
        XCTAssertEqual(AssistantStub.asked, ["status"], "no turn sent")
        for language in ["en", "ru"] {
            for key in ["mentor.placeTooLarge", "mentor.placeUnread", "mentor.placeTooLong"] {
                XCTAssertNotEqual(UIText(language: language)[key], key, "\(key) in \(language)")
            }
        }
    }

    /// The plan's answer 8 (v1.4): in a draft's place the assistant holds a conversation, the panel's own model, in one
    /// role: each question goes with the ones before it; the explainer (F5) proposes nothing; Try again asks the question
    /// that got no answer again in its place; a new conversation starts empty in the role chosen.
    func testAPlaceHoldsAConversationInOneRole() async throws {
        let (sources, _, place, view, number) = try await placeWithVariant()
        let assistant = controller()
        let shown = try XCTUnwrap(view.draft)
        let here = DraftAssistantSection.named(place, of: shown)
        XCTAssertEqual(here.basis.count, 2, "the base and the variant built from")
        XCTAssertTrue(DraftAssistantSection.offered(place))
        XCTAssertEqual(AssistantDraftPlace.roles(bridge: false), ["placeAssembler", "explainer"])
        XCTAssertEqual(AssistantDraftPlace.roles(bridge: true), ["seamChooser"])
        assistant.consent(true, inDraft: true)
        assistant.openInDraft(here, role: AssistantDraftPlace.explainer)
        let ask = AssistantAsk(piece: sources.piece, selection: nil, wholePiece: false, scope: place.label, language: "en", role: AssistantDraftPlace.explainer, relay: stubbed(), place: here)
        AssistantStub.answers = ["status": allowed]
        AssistantStub.turns = [Data(#"{"kind": "answer", "text": "The variant fills bar 4 [[technique.\#(number)]]", "fallback": false}"#.utf8)]
        await assistant.askInDraft("How do they differ?", about: ask)
        AssistantStub.turns = [Data(#"{"kind": "answer", "text": "From bars 5–8 of the notes", "fallback": false}"#.utf8)]
        await assistant.askInDraft("Where does it come from?", about: ask)

        let chat = try XCTUnwrap(assistant.inDraft?.chat)
        XCTAssertEqual(chat.role, "explainer", "the conversation keeps its role")
        XCTAssertEqual(chat.exchanges.map(\.question), ["How do they differ?", "Where does it come from?"])
        XCTAssertEqual(chat.exchanges.first?.answer.compactMap(\.cites), ["technique.\(number)"], "the place's fact, cited")
        XCTAssertNil(assistant.inDraft?.exchange?.proposals, "the explainer proposes nothing")
        let turn = chat.asking("And bar 2?").request(language: "en", context: .object([:]))
        let questions = turn["history"]?.items.filter { $0["kind"] == .string("question") }.compactMap { $0["text"]?.string }
        XCTAssertEqual(questions, ["How do they differ?", "Where does it come from?", "And bar 2?"], "the next question goes with the ones before it")
        XCTAssertEqual(turn["role"], .string("explainer"))

        AssistantStub.answers = ["status": allowed]
        await assistant.askInDraft("And bar 3?", about: ask)
        let failed = try XCTUnwrap(assistant.inDraft?.exchange)
        XCTAssertNotNil(failed.note, "no answer: the turn did not go through")
        let again = assistant.forget(failed)
        XCTAssertEqual(again, "And bar 3?", "Try again asks it again")
        let left = assistant.inDraft?.chat.exchanges.count
        XCTAssertEqual(left, 2, "in its place, not after itself")

        assistant.startInDraft(role: AssistantDraftPlace.role)
        let started = try XCTUnwrap(assistant.inDraft?.chat)
        XCTAssertEqual(started.role, "placeAssembler")
        XCTAssertEqual(started.exchanges, [], "a new conversation starts empty")
        XCTAssertEqual(assistant.inDraft?.place, here)
        for language in ["en", "ru"] {
            for key in ["drafts.assistantAssemble", "drafts.assistantExplain", "drafts.assistantChoose", "drafts.assistantExplainSuggest",
                        "drafts.assistantBridgeSuggest", "drafts.assistantRoles", "drafts.withAssistantBridgeHint"] {
                XCTAssertNotEqual(UIText(language: language)[key], key, "\(key) in \(language)")
            }
        }
    }

    /// v1.4: the assistant's conversation goes with a place the draft no longer offers it in as it was (gone, another
    /// base, its variant moved out), and the draft's screen hands one assistant to each screen its view opens again on.
    func testTheConversationGoesWithThePlaceAsItWas() throws {
        let assistant = controller()
        let here = AssistantDraftPlace(draft: "d_0001", place: "pl_0001", label: "① Notes 1–4 · 2 variants", basis: ["v_0001", "v_0002"])
        assistant.openInDraft(here)
        assistant.closeInDraft(unlessOffered: [here])
        XCTAssertEqual(assistant.inDraft?.place, here, "offered there still: kept")
        let rebased = AssistantDraftPlace(draft: "d_0001", place: "pl_0001", label: here.label, basis: ["v_0002", "v_0001"])
        assistant.closeInDraft(unlessOffered: [rebased])
        XCTAssertNil(assistant.inDraft, "another base: the conversation of the place as it was goes")
        let piece = OpenPiece(id: 9_000_401, title: "Draft", pages: [], timemap: "")
        let handed = ScoreWorkspace(piece: piece, assistant: assistant)
        XCTAssertTrue(handed.assistant === assistant, "the draft's screen's assistant, handed on")
        XCTAssertFalse(ScoreWorkspace(piece: piece).assistant === assistant, "else the screen's own")
    }

    /// v1.4, stale proposals: «Принять» keeps a proposal only while its call, made again on the draft as it is now, still
    /// proposes what its card says; else nothing is taken and the card says it no longer fits.
    func testAProposalNoLongerWhatItsCardSaysIsNotTaken() async throws {
        let (sources, draft, place, _, number) = try await placeWithVariant()
        let arguments = JSONValue.object(["techniques": .array([.int(number)]), "bar": .null])
        let call = AssistantPlaceProposal(
            id: "p1", tool: "place.take", parameters: arguments, role: AssistantDraftPlace.role, draft: draft.id, place: place.id, title: nil, explanation: "", structure: false
        )
        let here = AssistantDraftPlace(draft: draft.id, place: place.id, label: place.label)
        let answer = await AssistantController.run(AssistantToolCall(id: "t1", name: "place.take", arguments: arguments), role: call.role, place: here, piece: sources.piece)
        let now = try XCTUnwrap(answer["drafts"]?.items.first, answer.text)
        var proposal = call
        proposal.title = now["title"]?.string
        proposal.explanation = now["explanation"]?.string ?? ""
        let fresh = await proposal.proposedAgain(on: sources.piece)
        XCTAssertTrue(fresh, "the draft as it was: the same proposal")
        var other = proposal
        other.explanation = "Into ① from Notes 5–8: technique \(number), another bass."
        let stale = await other.proposedAgain(on: sources.piece)
        XCTAssertFalse(stale, "what the card says is not what the call proposes now")
        let refused = await sources.acceptProposal(other.callFields, of: draft.id, words: other.words) { await other.proposedAgain(on: sources.piece) }
        XCTAssertEqual(refused, .refused(""), "refused in no words of music-core's: «уже не подходит»")
        XCTAssertEqual(sources.layers.drafts.first?.taken, 0, "nothing taken")
        let kept = await sources.acceptProposal(proposal.callFields, of: draft.id, words: proposal.words) { await proposal.proposedAgain(on: sources.piece) }
        XCTAssertEqual(kept, .kept, sources.status ?? "")
        XCTAssertEqual(sources.layers.drafts.first?.taken, 1)
    }

    /// Review round 1 (v1.4): while «Принять» asks music-core again whether the proposal still fits, no other step of the
    /// draft starts; it is held as any step on its way holds another.
    func testNoStepStartsWhileAProposalIsAskedAgain() async throws {
        let (sources, draft, place, _, _) = try await placeWithVariant()
        let gate = Gate()
        let accepting = Task {
            await sources.acceptProposal(["tool": "place.take"], of: draft.id, words: "x") {
                await withCheckedContinuation { gate.waiting = $0 }
                return false
            }
        }
        for _ in 0..<1000 where gate.waiting == nil { await Task.yield() }
        XCTAssertNotNil(gate.waiting, "asked again")
        XCTAssertTrue(sources.stepping, "a step on its way meanwhile")
        let other = await sources.takeSure(place, of: draft.id)
        XCTAssertFalse(other, "another step is not sent")
        gate.waiting?.resume()
        let answer = await accepting.value
        XCTAssertEqual(answer, .refused(""), "no longer fits")
        XCTAssertFalse(sources.stepping)
        XCTAssertEqual(sources.layers.drafts.first?.taken, 0, "nothing taken")
    }

    /// Review round 1 (v1.4): the controller a draft's screen hands on keeps the conversation in a draft's place across
    /// the screens its view opens again on, and nothing of the screen before: the assistant at its selection, what the next
    /// question looks at.
    func testAScreenMadeAgainKeepsTheDraftsConversationAndNothingOfTheScreenBefore() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        let assistant = controller()
        let first = ScoreWorkspace(piece: piece, viewing: "draft", assistant: assistant)
        await first.appeared()
        let here = AssistantDraftPlace(draft: "d_0001", place: "pl_0001", label: "① Notes 1–4 · 2 variants")
        assistant.openInDraft(here, role: AssistantDraftPlace.explainer)
        let bars = ScoreSelection(onset: 3840, end: 5760, firstBar: 3, lastBar: 3)
        assistant.openHere(on: bars, role: "mentor")
        assistant.choose(wholePiece: true, selection: bars)
        assistant.listShown = true
        first.disappeared()

        let second = ScoreWorkspace(piece: piece, viewing: "draft", assistant: assistant)
        await second.appeared()
        XCTAssertEqual(assistant.inDraft?.place, here, "the conversation in the draft's place stays")
        XCTAssertEqual(assistant.inDraft?.chat.role, "explainer")
        XCTAssertNil(assistant.here, "the assistant at the selection of the screen before goes")
        XCTAssertEqual(assistant.scope, .selection, "and what its next question looked at")
        XCTAssertFalse(assistant.listShown)
        second.disappeared()
    }

    /// Review round 2 (v1.4): a panel question on its way when the screen is made again (another score loads) is dropped:
    /// nothing joins the piece's conversations, and nothing is left busy; a question asked on the new screen is kept.
    func testAPanelQuestionFromTheScreenBeforeIsDropped() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        let assistant = controller()
        assistant.consent(true)
        assistant.load(piece: "p_screens", score: ScoreController())
        let ask = AssistantAsk(piece: piece.id, selection: nil, wholePiece: true, scope: "The whole piece", language: "en", role: "mentor", relay: stubbed())
        AssistantStub.answers = ["status": allowed, "turn": (200, Data(#"{"kind": "answer", "text": "It is in A minor", "fallback": false}"#.utf8))]
        AssistantStub.holdTurn = true
        defer { AssistantStub.holdTurn = false }
        let before = Task { await assistant.ask("Before", about: ask) }
        for _ in 0..<500 where !AssistantStub.inTurn { try await Task.sleep(for: .milliseconds(10)) }
        XCTAssertTrue(AssistantStub.inTurn, "the question is on its way")
        XCTAssertTrue(assistant.busy)

        let next = ScoreController()
        assistant.load(piece: "p_screens", score: next)
        AssistantStub.holdTurn = false
        AssistantStub.release.signal()
        await before.value
        XCTAssertEqual(assistant.chats, [], "asked on the screen before: nothing joins the conversations")
        XCTAssertFalse(assistant.busy)
        XCTAssertNil(assistant.pending)
        XCTAssertEqual(assistant.phase, .idle)

        await assistant.ask("After", about: ask)
        let questions = assistant.current?.exchanges.map(\.question)
        XCTAssertEqual(questions, ["After"], "a question on the new screen is kept")
        let answered = assistant.current?.exchanges.first?.note
        XCTAssertNil(answered, "and answered")
    }

    /// F5 (v1.4): in a bridge place, the bridge chooser's conversation proposes a candidate as the base, music-core's
    /// step; only «Принять» makes it the base.
    func testTheBridgeChooserProposesACandidateInTheBridgesCard() async throws {
        let (sources, draft, view) = try await drafted { sources, draft, _ in
            // ② a new place in another key, and the bridge's candidates put before it.
            let added = await sources.change("drafts.add", ["bars": ["first": 5, "last": 8], "draft": draft.id, "drop": "place", "now": "2026-11-20T10:05:00Z"])
            XCTAssertNotNil(added, "drafts.add: \(sources.status ?? "")")
            // The new place's id is music-core's, one two devices never clash on (v1.6 c-14c): read from the draft as it is now.
            guard let opening = await sources.openDraft(draft), let second = opening.draft?.places.last?.id else {
                return XCTFail("the draft with its new place: \(sources.status ?? "")")
            }
            opened.append(opening.id)
            let steps: [(String, [String: Any])] = [
                ("drafts.transpose", ["draft": draft.id, "place": second, "semitones": 2, "now": "2026-11-20T10:10:00Z"]),
                ("drafts.bridge", ["draft": draft.id, "place": second, "now": "2026-11-20T10:12:00Z"]),
            ]
            for (name, fields) in steps {
                let changed = await sources.change(name, fields)
                XCTAssertNotNil(changed, "\(name): \(sources.status ?? "")")
            }
        }
        let bridge = try XCTUnwrap(view.draft?.places.first { $0.bridge }, "a bridge place between ① and ②")
        let candidate = try XCTUnwrap(bridge.variants.first { !$0.base }?.bridge, "a candidate besides the base")
        XCTAssertTrue(DraftAssistantSection.offered(bridge))
        let shown = try XCTUnwrap(view.draft)
        let here = DraftAssistantSection.named(bridge, of: shown)
        XCTAssertEqual(here.basis, [], "the chooser's own step changes the base")
        let assistant = controller()
        assistant.consent(true, inDraft: true)
        assistant.openInDraft(here, role: AssistantDraftPlace.roles(bridge: true)[0])
        let ask = AssistantAsk(piece: sources.piece, selection: nil, wholePiece: false, scope: bridge.label, language: "en", role: AssistantDraftPlace.seamChooser, relay: stubbed(), place: here)
        AssistantStub.answers = ["status": allowed]
        AssistantStub.turns = [
            Data(#"{"kind": "toolCalls", "entry": {"kind": "toolCalls", "calls": [{"id": "s1", "name": "place.bridge", "arguments": {"kind": "\#(candidate)"}}]}}"#.utf8),
            Data(#"{"kind": "answer", "text": "This one leads best", "fallback": false}"#.utf8),
        ]
        await assistant.askInDraft("Which bridge?", about: ask)
        let proposal = try XCTUnwrap(assistant.inDraft?.exchange?.proposals?.first, "music-core's place.bridge, proposed")
        XCTAssertTrue(proposal.structure)
        XCTAssertEqual(proposal.role, "seamChooser")
        let kept = await assistant.accept(proposal) {
            await sources.acceptProposal(proposal.callFields, of: proposal.draft, words: proposal.words) { await proposal.proposedAgain(on: sources.piece) }
        }
        XCTAssertTrue(kept, sources.status ?? "")
        let reopened = await sources.openDraft(draft)
        let after = try XCTUnwrap(reopened, sources.status ?? "")
        opened.append(after.id)
        let base = after.draft?.places.first { $0.bridge }?.variants.first { $0.base }?.bridge
        XCTAssertEqual(base, candidate, "«Принять»: the candidate is the bridge's base")
    }

    /// «Принять» while another step of the draft is on its way (v1.4, i-3): held, not sent, the proposal as it was and no
    /// problem told; the other step undone after, so the proposal is taken as before.
    private func acceptIsHeldWhileAStepIsOnItsWay(_ proposal: AssistantPlaceProposal, _ assistant: AssistantController, _ sources: SourcesController, place: DraftViewAnswer.Place, draft: String) async {
        let other = Task { await sources.takeSure(place, of: draft) }
        for _ in 0..<1000 where !sources.stepping { await Task.yield() }
        XCTAssertTrue(sources.stepping, "a step on its way")
        let held = await assistant.accept(proposal) {
            await sources.acceptProposal(proposal.callFields, of: proposal.draft, words: proposal.words)
        }
        XCTAssertFalse(held)
        XCTAssertEqual(assistant.inDraft?.exchange?.proposals?.first?.state, .proposed, "not sent: still proposed")
        XCTAssertNil(assistant.inDraft?.exchange?.proposals?.first?.problem, "and no «уже не подходит»")
        if await other.value { await sources.undo() }
    }
}
