import XCTest

@testable import MotiffLab

/// What 1.2 added to drafts (docs/ux-baseline.md, DRAFT-01's other pieces, DRAFT-06's register and bridge, DRAFT-09 to
/// DRAFT-11; ADR 0034 and the plan of 1.2, answers 2 to 5 and 7, approved 2026-11-20 11:26 UTC, and Andrei's «Только у
/// места», 20:46 UTC): a fragment into another piece's draft, the register at a seam, the bridge and its candidates, the
/// anchors the player confirms, and the assistant in a draft's place. Taken through the Sources sheet's controller, the
/// draft view's models and the assistant's controller as the screen takes them, over the real music-core and the kit's
/// piece opened in Russian, so music-core's words are the player's; the assistant's API is answered here, no network.
/// What the page draws of an anchor, and what a tap on it posts, is `tests/contracts/page-scenarios/score-draft-anchors.json`;
/// the cards' look and the sound are the device's.
@MainActor
final class Drafts12BaselineTests: XCTestCase {
    private var pieces: [Int32] = []
    private var folders: [URL] = []
    private let russian = UIText(language: "ru")

    override func tearDown() async throws {
        for piece in pieces { MusicCore.close(piece: piece) }
        pieces = []
        for folder in folders { try? FileManager.default.removeItem(at: folder) }
        folders = []
        AssistantStub.answers = [:]
        AssistantStub.turns = []
    }

    /// The kit's piece on its screen's workspace, its sources read, with no project file yet.
    private func piece() async throws -> ScoreWorkspace {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let opened = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "ru", title: example.title).get()
        pieces.append(opened.id)
        let workspace = ScoreWorkspace(piece: opened)
        await workspace.sources.load(piece: opened.id, file: nil)
        return workspace
    }

    /// A draft of bars `first` of the piece, then each of `more` as it says (a variant goes into the first place).
    private func draft(_ sources: SourcesController, _ first: ClosedRange<Int>, then more: [(bars: ClosedRange<Int>, drop: String, part: String)] = []) async throws -> SourceLayers.Draft {
        let made = await sources.change("drafts.add", ["bars": ["first": first.lowerBound, "last": first.upperBound], "now": "2026-11-20T09:00:00Z"])
        let id = try XCTUnwrap(made?["draft"] as? String, sources.status ?? "the first fragment makes the draft")
        let place = made?["place"] as? String
        for (index, fragment) in more.enumerated() {
            var fields: [String: Any] = ["bars": ["first": fragment.bars.lowerBound, "last": fragment.bars.upperBound], "draft": id, "drop": fragment.drop, "part": fragment.part]
            fields["now"] = "2026-11-20T09:1\(index):00Z"
            if fragment.drop == "variant", let place { fields["place"] = place }
            let added = await sources.change("drafts.add", fields)
            XCTAssertNotNil(added, sources.status ?? "music-core takes the fragment")
        }
        return try XCTUnwrap(sources.layers.drafts.first { $0.id == id })
    }

    /// The draft opened as its tab opens it, and its view's score with its приёмы and seams read as it is drawn.
    private func view(_ draft: SourceLayers.Draft, _ sources: SourcesController) async throws -> (OpenPiece, ScoreController) {
        let opening = await sources.openDraft(draft)
        let shown = try XCTUnwrap(opening, sources.status ?? "the draft opens")
        pieces.append(shown.id)
        let score = ScoreWorkspace(piece: shown, viewing: "view only").score
        score.show(shown, dark: false)
        await score.draftModel.load(shown.draft, on: shown.id)
        await score.seamsModel.load(shown.draft, on: shown.id)
        return (shown, score)
    }

    /// ② moved by `semitones`, as a fragment of another piece or a step of the player's brings it.
    private func transpose(_ draft: SourceLayers.Draft, _ sources: SourcesController, by semitones: Int) async throws {
        let (shown, _) = try await view(draft, sources)
        let second = try XCTUnwrap(shown.draft?.places.last)
        let moved = await sources.change("drafts.transpose", ["draft": draft.id, "place": second.id, "semitones": semitones, "now": "2026-11-20T09:30:00Z"])
        XCTAssertNotNil(moved, sources.status ?? "")
    }

    // MARK: - DRAFT-01: another piece's drafts

    /// DRAFT-01 (1.2): under «Куда» the sheet lists the drafts of the other pieces in the folder, under each piece's title as
    /// the library shows it; into one of them only «Новым местом в конце» or «На полку». The fragment goes into that piece's
    /// file, its toast says so with «Открыть», and the piece it came from stays as it was, with no draft of its own.
    func testDRAFT01AFragmentGoesIntoAnotherPiecesDraftAndOnlyItsFileChanges() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("collage-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        folders.append(folder)
        let other = try await piece()
        let made = try await draft(other.sources, 1...4)
        let otherFile = folder.appendingPathComponent("Elise A.motiff.json")
        try await ProjectFiles.save(piece: other.piece.id, to: otherFile)
        MusicCore.close(piece: other.piece.id)
        pieces.removeAll { $0 == other.piece.id }

        let thisFile = folder.appendingPathComponent("Elise B.motiff.json")
        let current = try await piece()
        try await ProjectFiles.save(piece: current.piece.id, to: thisFile)
        await current.sources.load(piece: current.piece.id, file: thisFile)
        let listed = await OtherPieceDrafts.list(in: folder, except: thisFile, project: current.sources.projectId, language: "ru")
        XCTAssertEqual(listed.map(\.file.lastPathComponent), ["Elise A.motiff.json"], "the other piece's drafts, not this one's file")
        let elsewhere = try XCTUnwrap(listed.first)
        let read = await PieceSummary.read(otherFile)
        let summary = try XCTUnwrap(read)
        XCTAssertEqual(elsewhere.title, summary.shownTitle(else: "Elise A"), "under its title as the library shows it")
        XCTAssertEqual(elsewhere.drafts.map(\.id), [made.id])
        let into = DraftChoice.Elsewhere(file: elsewhere.file, draft: made.id, name: made.name, piece: elsewhere.title)
        let asked = await PieceCore(piece: current.piece.id).draftSheet(facadeRequest(["span": ["onset": 0, "end": 1440]]))
        let sheet = try XCTUnwrap(asked.value, asked.message ?? "the sheet is answered")
        XCTAssertEqual(DraftChoice.ways(.elsewhere(into), in: sheet).map(\.drop), [.place, .shelf], "«Новым местом в конце» or «На полку»")

        let saved = await MusicCore.run { MusicCore.call(piece: current.piece.id, "project.save", #"{"now": "2026-11-20T12:00:00Z"}"#) }
        let added = await current.sources.addToDraft(DraftChoice(part: "all", into: .elsewhere(into), way: DraftChoice.Way(drop: .place)), span: 0..<1440, from: current.piece.id)
        XCTAssertTrue(added, current.sources.status ?? "")
        XCTAssertEqual(current.sources.addedElsewhere?.draft, into, "its toast, with «Открыть»")
        XCTAssertEqual(russian.format("drafts.addedElsewhere", made.name), "Добавлено в черновик «Черновик 1»")
        XCTAssertTrue(current.sources.layers.drafts.isEmpty, "this piece has no draft")
        let after = await MusicCore.run { MusicCore.call(piece: current.piece.id, "project.save", #"{"now": "2026-11-20T12:00:00Z"}"#) }
        XCTAssertEqual(after, saved, "and stays as it was")
        let again = await OtherPieceDrafts.list(in: folder, except: thisFile, project: current.sources.projectId, language: "ru")
        XCTAssertEqual(again.first?.drafts.first?.places, 2, "the other piece's draft has it as a new place at the end, in its file")
    }

    // MARK: - DRAFT-06 (1.2): the register at a seam, the bridge offered

    /// DRAFT-06 (1.2): where the places' registers lie an octave or more apart the seam says so («② на октаву выше») and
    /// offers «② на октаву ниже», a step its toast undoes; where the keys differ it offers «Связка · N», and where the register
    /// is the same it says nothing of it.
    func testDRAFT06ASeamTellsTheRegisterAndOffersABridgeWhereTheKeysDiffer() async throws {
        let workspace = try await piece()
        let sources = workspace.sources
        let high = try await draft(sources, 1...4, then: [(bars: 5...8, drop: "place", part: "all")])
        try await transpose(high, sources, by: 24)
        let (_, registered) = try await view(high, sources)
        let seam = try XCTUnwrap(registered.seamsModel.shown.first, "the register alone shows a diamond")
        XCTAssertEqual(seam.changes, ["register"])
        XCTAssertEqual(seam.register, DraftSeamsAnswer.Register(direction: "higher", label: "② на октаву выше"))
        XCTAssertNil(seam.bridge, "the same key: no bridge")
        let lower = try XCTUnwrap(seam.offers.first)
        XCTAssertEqual(lower.label, "② на октаву ниже")
        XCTAssertEqual(lower.semitones, -12, "an octave towards ①, its spelling kept")
        let taken = await sources.take(lower, of: high.id)
        XCTAssertTrue(taken, sources.status ?? "")
        guard case .draftStep? = sources.toast?.undo else { return XCTFail("its toast undoes the step") }

        let other = try await piece()
        let keyed = try await draft(other.sources, 1...4, then: [(bars: 5...8, drop: "place", part: "all")])
        try await transpose(keyed, other.sources, by: 2)
        let (_, bridged) = try await view(keyed, other.sources)
        let differs = try XCTUnwrap(bridged.seamsModel.shown.first, "the keys differ: a diamond")
        XCTAssertTrue(differs.changes.contains("key"))
        XCTAssertNil(differs.register, "the same register: nothing said of it")
        let bridge = try XCTUnwrap(differs.bridge, "where the keys differ, «Связка»")
        XCTAssertTrue((2...3).contains(bridge.candidates), "two or three candidates")
        XCTAssertTrue(bridge.label.hasPrefix("Связка · \(bridge.candidates) кандидат"), bridge.label)
    }

    // MARK: - DRAFT-10: the bridge's place

    /// DRAFT-10: «Связка · N» puts music-core's candidates as the variants of a small place of their own between the two
    /// places, the first the base, each with what it sounds; «Выбрать» makes another the base and «Оставить» takes the
    /// place out, each a step its toast undoes.
    func testDRAFT10ABridgesCandidatesAreAPlaceOfTheirOwn() async throws {
        let workspace = try await piece()
        let sources = workspace.sources
        let draft = try await draft(sources, 1...4, then: [(bars: 5...8, drop: "place", part: "all")])
        try await transpose(draft, sources, by: 2)
        let (_, score) = try await view(draft, sources)
        let bridge = try XCTUnwrap(score.seamsModel.shown.first?.bridge, "the keys differ: «Связка»")
        let put = await sources.putBridge(bridge, of: draft.id)
        XCTAssertTrue(put, sources.status ?? "")
        guard case .draftStep? = sources.toast?.undo else { return XCTFail("its toast undoes the step") }
        let (bridged, _) = try await view(draft, sources)
        let places = try XCTUnwrap(bridged.draft?.places)
        XCTAssertEqual(places.map(\.bridge), [false, true, false], "a place of its own between ① and the place after it")
        guard places.count == 3 else { return XCTFail("three places") }
        let link = places[1]
        XCTAssertEqual(link.variants.count, bridge.candidates, "its candidates, its variants")
        guard link.variants.count > 1 else { return XCTFail("candidates to choose from") }
        XCTAssertEqual(link.variants.map(\.base).first, true, "the first the base")
        XCTAssertTrue(link.variants.allSatisfy { $0.bridge != nil && !($0.explanation ?? "").isEmpty }, "each with its kind and what it sounds")
        XCTAssertEqual(russian["drafts.bridgeChoose"], "Выбрать")
        let other = link.variants[1]
        let chosen = await sources.makeBase(other, of: link, in: draft.id)
        XCTAssertTrue(chosen, sources.status ?? "")
        let (rebased, _) = try await view(draft, sources)
        let candidates = try XCTUnwrap(rebased.draft?.places.first { $0.bridge })
        XCTAssertEqual(candidates.variants.first { $0.base }?.id, other.id, "«Выбрать»: that candidate the base")
        let left = await sources.leaveBridge(candidates, of: draft.id)
        XCTAssertTrue(left, sources.status ?? "")
        let (unbridged, _) = try await view(draft, sources)
        XCTAssertEqual(unbridged.draft?.places.map(\.bridge), [false, false], "«Оставить»: the place goes, the seam as it was")
        await sources.undo()
        let (back, _) = try await view(draft, sources)
        XCTAssertEqual(back.draft?.places.map(\.bridge), [false, true, false], "Undo")
    }

    // MARK: - DRAFT-09: the anchors

    /// DRAFT-09: «Не тот такт? Сопоставить…» on a приём's card pins where the base and the variant sound together; the
    /// anchor stands as a small amber mark over its bar, hidden with the numbers, and its menu's «Открепить» takes it
    /// away; each a step its toast undoes.
    func testDRAFT09AnAnchorThePlayerConfirmsIsMarkedOverItsBar() async throws {
        let workspace = try await piece()
        let sources = workspace.sources
        let draft = try await draft(sources, 1...4, then: [(bars: 1...4, drop: "variant", part: "right")])
        let (_, score) = try await view(draft, sources)
        let spot = try XCTUnwrap(score.draftModel.spots.first { $0.occurrence.bar == 3 }, "the right hand's приём in bar 3")
        XCTAssertEqual(russian["drafts.match"], "Не тот такт? Сопоставить…")
        let pinned = await sources.anchor(spot, variantTick: spot.occurrence.inVariant.onset)
        XCTAssertTrue(pinned, sources.status ?? "")
        guard case .draftStep? = sources.toast?.undo else { return XCTFail("its toast undoes the step") }
        let (anchoredView, anchored) = try await view(draft, sources)
        let mark = try XCTUnwrap(DraftAnchorsModel.marks(anchoredView.draft).first, "the anchor kept on the variant")
        XCTAssertEqual(mark.anchor.bar, 3)
        let marker = try XCTUnwrap(anchored.placeMarkers.first { $0["anchor"] != nil }, "a small mark over its bar")
        XCTAssertEqual(marker["bar"] as? Int, 3)
        XCTAssertEqual(marker["anchor"] as? String, mark.key)
        XCTAssertTrue(DraftAnchorsModel.label(mark, russian).hasPrefix("Опорная точка в такте 3 · "))
        anchored.hideTechniqueNumbers(true)
        XCTAssertFalse(anchored.placeMarkers.contains { $0["anchor"] != nil }, "hidden with the numbers")
        anchored.hideTechniqueNumbers(false)
        XCTAssertNotNil(anchored.anchorsModel.pick(mark.key, in: anchoredView.draft, at: .zero), "its menu")
        let unpinned = await sources.unanchor(mark, of: draft.id)
        XCTAssertTrue(unpinned, sources.status ?? "")
        let (loose, _) = try await view(draft, sources)
        XCTAssertTrue(DraftAnchorsModel.marks(loose.draft).isEmpty, "«Открепить»")
        await sources.undo()
        let (undone, _) = try await view(draft, sources)
        XCTAssertEqual(DraftAnchorsModel.marks(undone.draft).map(\.key), [mark.key], "Undo")
    }

    // MARK: - DRAFT-11: the assistant in a draft's place

    /// DRAFT-11: in a place with a variant besides its base, the assistant asks its own consent before anything goes; what
    /// it proposes is a draft that is never applied by itself: heard «После» as the draft would be, «Отказаться» changes
    /// nothing, and only «Принять» takes it, a step of the draft its toast undoes.
    func testDRAFT11TheAssistantInAPlaceProposesAndOnlyThePlayerAccepts() async throws {
        let workspace = try await piece()
        let sources = workspace.sources
        let draft = try await draft(sources, 1...4, then: [(bars: 5...8, drop: "variant", part: "all")])
        let (opened, score) = try await view(draft, sources)
        let place = try XCTUnwrap(opened.draft?.places.first)
        XCTAssertTrue(place.variants.contains { !$0.base }, "a variant besides the base: «С ассистентом · Pro» on its card")
        XCTAssertEqual(russian["drafts.withAssistant"], "С ассистентом")
        let number = try XCTUnwrap(score.draftModel.spots.first?.technique.number)

        let defaults = try XCTUnwrap(UserDefaults(suiteName: "draft-assistant-\(UUID().uuidString)"))
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("draft-assistant-\(UUID().uuidString)")
        folders.append(folder)
        let assistant = AssistantController(store: AssistantStore(folder: folder), defaults: defaults)
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [AssistantStub.self]
        let relay = AssistantRelay(session: URLSession(configuration: configuration), request: { path in URLRequest(url: URL(string: "https://api.example.test/\(path)")!) })
        let here = AssistantDraftPlace(draft: draft.id, place: place.id, label: place.label)
        let ask = AssistantAsk(piece: opened.id, selection: nil, wholePiece: false, scope: place.label, language: "ru", role: AssistantDraftPlace.role, relay: relay, place: here)
        let status = (200, Data(#"{"configured": true, "allowed": true, "pro": true, "left": 5}"#.utf8))
        assistant.consent(true)
        assistant.openInDraft(here)
        AssistantStub.answers = ["status": status]
        await assistant.askInDraft("Собери место", about: ask)
        XCTAssertEqual(assistant.inDraft?.exchange?.note, "mentor.noConsent", "the piece's consent sends nothing from a draft")
        XCTAssertTrue(russian["mentor.draftConsentNot"].hasPrefix("Никогда не отправляется: звук записи"))

        assistant.consent(true, inDraft: true)
        AssistantStub.answers = ["status": status]
        AssistantStub.turns = [
            Data(#"{"kind": "toolCalls", "entry": {"kind": "toolCalls", "calls": [{"id": "b1", "name": "place.take", "arguments": {"techniques": [\#(number)], "bar": null}}]}}"#.utf8),
            Data(#"{"kind": "answer", "text": "Возьмите приём [[technique.\#(number)]]", "fallback": false}"#.utf8),
        ]
        await assistant.askInDraft("Собери место", about: ask)
        let proposal = try XCTUnwrap(assistant.inDraft?.exchange?.proposals?.first, "music-core's step, proposed")
        XCTAssertEqual(proposal.state, .proposed)
        XCTAssertEqual(sources.layers.drafts.first?.taken, 0, "never applied by itself")
        XCTAssertEqual(russian["drafts.proposalAfter"], "После")
        let heard = await PieceCore(piece: opened.id).openProposal(proposal.callFields)
        let after = try XCTUnwrap(heard.value, "«После»: the draft as it would be")
        pieces.append(after.piece)
        XCTAssertEqual(sources.layers.drafts.first?.taken, 0, "heard, still nothing applied")
        assistant.refuse(proposal)
        XCTAssertEqual(assistant.inDraft?.exchange?.proposals?.first?.state, .cancelled, "«Отказаться»")
        let accepted = await assistant.accept(proposal) {
            await sources.acceptProposal(proposal.callFields, of: proposal.draft, words: proposal.words)
        }
        XCTAssertTrue(accepted, sources.status ?? "")
        XCTAssertEqual(sources.layers.drafts.first?.taken, 1, "«Принять», the player's tap, takes it")
        guard case .draftStep? = sources.toast?.undo else { return XCTFail("its toast undoes the step") }
        await sources.undo()
        XCTAssertEqual(sources.layers.drafts.first?.taken, 0, "Undo")
    }
}
