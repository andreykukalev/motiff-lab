import XCTest

@testable import MotiffLab

/// The assistant in a draft (v1.2, ADR 0034 section 6, the plan's answer 7): a question about a place sends music-core's
/// `drafts.context` and runs the model's tools by `drafts.call`, only after the draft's own consent; what a tool proposes
/// is never applied: it is heard after (`drafts.proposal`), refused, or accepted by the player (`drafts.accept`), a step
/// of the draft its toast undoes. Over the music-core of the tests and a pretend API (`AssistantStub`), no network.
@MainActor
final class DraftAssistantTests: XCTestCase {
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

    func testAQuestionInAPlaceProposesAStepThePlayerHearsRefusesAndAccepts() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        let workspace = ScoreWorkspace(piece: piece)
        let sources = workspace.sources
        await sources.load(piece: piece.id, file: nil)
        let made = await sources.change("drafts.add", ["bars": ["first": 1, "last": 4], "now": "2026-11-20T10:00:00Z"])
        XCTAssertNotNil(made, sources.status ?? "")
        let draft = try XCTUnwrap(sources.layers.drafts.first)
        let opening = await sources.openDraft(draft)
        let first = try XCTUnwrap(opening, sources.status ?? "")
        opened.append(first.id)
        let place = try XCTUnwrap(first.draft?.places.first)
        let fields: [String: Any] = ["bars": ["first": 5, "last": 8], "draft": draft.id, "drop": "variant", "place": place.id, "now": "2026-11-20T10:05:00Z"]
        let added = await sources.change("drafts.add", fields)
        XCTAssertNotNil(added, sources.status ?? "")
        let reopened = await sources.openDraft(draft)
        let view = try XCTUnwrap(reopened, sources.status ?? "")
        opened.append(view.id)
        let techniques = DraftTechniquesModel()
        await techniques.load(view.draft, on: view.id)
        let number = try XCTUnwrap(techniques.spots.first?.technique.number, "Für Elise's bar 8 against bar 4: one приём")

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
