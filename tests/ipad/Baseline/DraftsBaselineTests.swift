import XCTest

@testable import MotiffLab

/// Drafts as Andrei approved them for 1.1 (docs/ux-baseline.md, DRAFT-01 to DRAFT-08; ADR 0034 and the spec «Черновики»,
/// accepted 2026-11-19 20:55 UTC): a fragment into a draft only where the «В черновик» sheet says, the group «Черновики»
/// in Sources and the draft's tab, the draft's view of its places, a приём's numbers and card, «Скрыть номера», a seam's
/// diamond and card, the draft's map and its shelf. Taken through the score's workspace, the Sources sheet's controller and
/// the draft view's models as the screen takes them, over the real music-core and the kit's piece opened in Russian, so
/// music-core's words are the player's. What the page draws of a draft's marks, and what a tap on them posts, is
/// `tests/contracts/page-scenarios/score-draft-marks.json`; the sheets' look and the sound are the device's.
@MainActor
final class DraftsBaselineTests: XCTestCase {
    /// The pieces opened in music-core for a test, and what it left on the device.
    private var opened: [Int32] = []
    private var files: [URL] = []
    private let russian = UIText(language: "ru")

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
        for file in files { try? FileManager.default.removeItem(at: file) }
        files = []
    }

    /// A fragment for `drafts.add`: its bars, the part taken, and how it goes in after the first, which makes the draft.
    private struct Fragment {
        let first: Int
        let last: Int
        var part = "all"
        var drop = "place"
    }

    /// The kit's piece on its screen's workspace, its sources read with its project file, as the screen opens it.
    private func pieceOnScreen() async throws -> (workspace: ScoreWorkspace, file: URL) {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "ru", title: example.title).get()
        opened.append(piece.id)
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("draft-\(UUID().uuidString).motiff.json")
        files.append(file)
        let workspace = ScoreWorkspace(piece: piece)
        await workspace.sources.load(piece: piece.id, file: file)
        workspace.score.show(piece, dark: false)
        return (workspace, file)
    }

    /// A draft made of `fragments` of the piece, in order: the first makes it, the others go in as each says (a variant goes
    /// to the first place).
    private func draft(_ sources: SourcesController, _ fragments: [Fragment]) async throws -> SourceLayers.Draft {
        var draft: String?
        var place: String?
        for (index, fragment) in fragments.enumerated() {
            var fields: [String: Any] = ["bars": ["first": fragment.first, "last": fragment.last], "part": fragment.part, "now": "2026-11-20T10:0\(index):00Z"]
            if let draft {
                fields["draft"] = draft
                fields["drop"] = fragment.drop
            }
            if fragment.drop == "variant", let place { fields["place"] = place }
            let answer = await sources.change("drafts.add", fields)
            let added = try XCTUnwrap(answer, sources.status ?? "music-core took the fragment")
            draft = draft ?? added["draft"] as? String
            place = place ?? added["place"] as? String
        }
        return try XCTUnwrap(sources.layers.drafts.first { $0.id == draft })
    }

    /// The draft opened to be shown, as its tab opens it, closed when the test ends.
    private func shown(_ draft: SourceLayers.Draft, _ sources: SourcesController, at tick: Double? = nil) async throws -> OpenPiece {
        let opening = await sources.openDraft(draft, at: tick)
        let view = try XCTUnwrap(opening, sources.status ?? "the draft opens")
        opened.append(view.id)
        return view
    }

    /// The draft's view on a workspace of its own, its приёмы and seams read as the view reads them when it is drawn.
    private func viewed(_ view: OpenPiece) async -> ScoreController {
        let score = ScoreWorkspace(piece: view, viewing: "view only").score
        score.show(view, dark: false)
        await score.draftModel.load(view.draft, on: view.id)
        await score.seamsModel.load(view.draft, on: view.id)
        return score
    }

    /// The «В черновик» sheet as music-core answers it for the passage asked about.
    private func sheet(_ request: DraftSheetRequest) async throws -> DraftSheetAnswer {
        let span = ["onset": Int(request.span.lowerBound), "end": Int(request.span.upperBound)]
        let answer = await PieceCore(piece: request.piece).draftSheet(facadeRequest(["span": span]))
        return try XCTUnwrap(answer.value, answer.message ?? "the sheet is answered")
    }

    /// The piece's notes as it plays them, onset and pitch.
    private func notes(_ piece: Int32) async -> [String] {
        await PieceCore(piece: piece).playback().value?.notes.map { "\(Int($0.onset))/\($0.pitch)" } ?? []
    }

    private static func bars(_ first: Int, _ last: Int) -> String {
        #"{"bars": {"first": \#(first), "last": \#(last)}}"#
    }

    // MARK: - DRAFT-01: the selection menu's «Черновик» and the «В черновик» sheet

    /// DRAFT-01: «Черновик» in the selection menu opens the sheet for the passage; with no draft yet it starts a new one as
    /// its first place; the same passage again is offered first as a variant of the place where it is the same music, with
    /// how much, then as a new place at the end, then on the shelf; it goes only where the player says, and the notes it
    /// came from stay as they are. «Новый черновик» with nothing selected asks for notes and makes no empty draft.
    func testDRAFT01AFragmentGoesIntoADraftOnlyWhereTheSheetSays() async throws {
        let (workspace, _) = try await pieceOnScreen()
        let sources = workspace.sources
        let piece = workspace.piece.id
        let before = await notes(piece)
        XCTAssertNil(workspace.newDraft(), "«Новый черновик» with nothing selected: no empty draft")
        XCTAssertEqual(workspace.score.notice?.key, "drafts.selectNotes")
        XCTAssertEqual(russian["drafts.selectNotes"], "Выделите ноты, затем «Черновик» в меню выделения")
        XCTAssertTrue(sources.layers.drafts.isEmpty)

        await workspace.score.select(request: Self.bars(1, 4), step: false)
        XCTAssertTrue(workspace.draftOwner(under: nil) === sources, "«Черновик» in the selection menu, into this piece's drafts")
        XCTAssertEqual(russian["menu.draft"], "Черновик")
        let request = try XCTUnwrap(workspace.draftSheet(), "the sheet for the passage selected")
        let empty = try await sheet(request)
        XCTAssertEqual(Array(empty.parts.prefix(3)), ["all", "right", "left"], "«Что взять»: «Всё», «Правая рука», «Левая рука»")
        let first = DraftChoice.first(empty, part: empty.part)
        XCTAssertEqual(first.into, .new, "«Куда»: no draft yet, a new one")
        XCTAssertEqual(DraftChoice.ways(nil), [DraftChoice.Way(drop: .place)], "«Как»: a new draft takes it as its first place")
        let made = await sources.addToDraft(first, span: request.span, from: request.piece)
        XCTAssertTrue(made, sources.status ?? "")
        let draft = try XCTUnwrap(sources.layers.drafts.first)

        let again = try await sheet(request)
        let offered = DraftChoice.first(again, part: again.part)
        XCTAssertEqual(offered.into, .draft(draft.id), "the draft where it is the same music")
        XCTAssertEqual(offered.way.drop, .variant, "offered first as a variant of that place")
        XCTAssertGreaterThanOrEqual(offered.way.percent ?? 0, 50, "with how much is the same music")
        let ways = DraftChoice.ways(DraftChoice.draft(offered.into, in: again))
        XCTAssertEqual(ways.map(\.drop), [.variant, .place, .shelf], "then «Новым местом в конце», then «На полку»")
        let shelved = DraftChoice(part: "right", into: offered.into, way: DraftChoice.Way(drop: .shelf))
        let kept = await sources.addToDraft(shelved, span: request.span, from: request.piece)
        XCTAssertTrue(kept, sources.status ?? "")
        XCTAssertEqual(sources.layers.drafts.first?.places, 1, "nothing merged by itself: no place took it")
        XCTAssertEqual(sources.layers.drafts.first?.shelf, 1, "the shelf did, as the player said")
        let after = await notes(piece)
        XCTAssertEqual(after, before, "the notes it came from stay as they are")
        XCTAssertEqual(workspace.score.selectionModel.selection?.onset, request.span.lowerBound, "and so does the selection")
    }

    // MARK: - DRAFT-02: the group «Черновики» in Sources and the draft's tab

    /// DRAFT-02: a draft is listed in Sources under «Черновики · N» with its name, where its fragments came from and its
    /// places, saved to the project file at once; its tab comes last in the switch under its name, and the eye takes it out;
    /// ⋯ «Переименовать…» renames it, «Удалить…» deletes the draft alone, and the toast's Undo writes the file back.
    func testDRAFT02ADraftIsListedInSourcesAndItsTabComesLastInTheSwitch() async throws {
        let (workspace, file) = try await pieceOnScreen()
        let sources = workspace.sources
        var reopened = 0
        sources.reopen = { reopened += 1 }
        let draft = try await draft(sources, [Fragment(first: 1, last: 4)])
        XCTAssertEqual(russian.format("drafts.group", String(sources.layers.drafts.count)), "Черновики · 1")
        XCTAssertEqual(draft.name, "Черновик 1", "music-core names a new draft")
        XCTAssertEqual(SourcesDraftRow.caption(draft, russian), "из нот из файла · 1 место", "where it came from, its places")
        XCTAssertTrue(try String(contentsOf: file, encoding: .utf8).contains(draft.id), "saved at once")
        let tab = try XCTUnwrap(sources.layers.shownViews.last, "its tab comes last in the switch")
        XCTAssertEqual(tab.kind, "draft")
        XCTAssertEqual(tab.id, draft.id)
        XCTAssertEqual(SourceLayers.word(tab, russian), "Черновик 1", "under its name")

        await sources.setHidden(draft.id, true)
        XCTAssertFalse(sources.layers.shownViews.contains { $0.id == draft.id }, "the eye takes the tab out of the switch")
        await sources.setHidden(draft.id, false)
        await sources.renameDraft(draft, to: "  Моя версия  ")
        let renamed = try XCTUnwrap(sources.layers.drafts.first)
        XCTAssertEqual(renamed.name, "Моя версия", "«Переименовать…»")
        XCTAssertEqual(sources.layers.shownViews.last.map { SourceLayers.word($0, russian) }, "Моя версия")

        let working = sources.layers.working
        await sources.removeDraft(renamed)
        XCTAssertTrue(sources.layers.drafts.isEmpty, "«Удалить…»: the draft goes")
        XCTAssertEqual(sources.layers.working, working, "and nothing else: the sources and their notes stay")
        XCTAssertEqual(russian["drafts.deleteLine"], "Удаляется только черновик: источники и их ноты остаются как есть.")
        guard case .restore? = sources.toast?.undo else { return XCTFail("the toast undoes it") }
        await sources.undo()
        XCTAssertTrue(try String(contentsOf: file, encoding: .utf8).contains(renamed.id), "Undo writes the file back")
        XCTAssertEqual(reopened, 1, "and opens the piece again from it")
    }

    // MARK: - DRAFT-03: a draft's view, its places' labels, and play

    /// DRAFT-03: the draft's tab opens the draft over the piece as one score, its places one after another in the draft's
    /// own bars, each labelled over its start with where it came from; a tap on a label selects the place, which plays and
    /// loops as any passage; «↻ Место» loops the place at the playhead with Цикл on, and «▶ Всё» plays the whole draft from
    /// its start, Цикл off.
    func testDRAFT03ADraftsViewIsItsPlacesInOrderEachLabelledAndPlayed() async throws {
        let (workspace, _) = try await pieceOnScreen()
        let draft = try await draft(workspace.sources, [Fragment(first: 1, last: 4), Fragment(first: 9, last: 12)])
        let tab = try XCTUnwrap(workspace.sources.layers.views.first { $0.id == draft.id })
        let picked = expectation(description: "the draft is shown")
        var shownView: ScoreScreenContent.ShownView?
        workspace.pickView(tab, switchView: nil, text: russian) { view in
            shownView = view
            picked.fulfill()
        }
        await fulfillment(of: [picked], timeout: 20)
        let view = try XCTUnwrap(shownView?.piece, "the tab opens the draft")
        opened.append(view.id)
        XCTAssertEqual(shownView?.pdf, false, "as notes over the piece")
        XCTAssertNotEqual(view.id, workspace.piece.id, "never in the piece's place")
        let places = try XCTUnwrap(view.draft?.places)
        XCTAssertEqual(places.map(\.label), ["① Ноты 1–4", "② Ноты 9–12"], "each says where it came from")
        XCTAssertEqual(places.map(\.firstBar), [1, 5], "in the draft's own bars, one place after another")
        XCTAssertEqual(places[1].onset, places[0].end)

        let session = await WorkspaceSession(piece: view, viewing: "view only")
        let score = session.workspace.score
        let player = session.workspace.player
        XCTAssertEqual(score.placeMarkers.filter { $0["place"] as? Bool == true }.compactMap { $0["bar"] as? Int }, [1, 5], "a label over each place's start")
        await session.step("the draft's view opens, its sound not loaded") {
            session.workspace.wire()
            session.appear()
            session.workspace.applyModel()
        }
        let second = Double(places[1].onset)..<Double(places[1].end)
        await session.step("the label of ② is tapped") { score.pickMarker(.performanceBar(places[1].firstBar, left: 0, bottom: 0)) }
        XCTAssertEqual(player.selection, second, "the place is selected")
        XCTAssertEqual(player.loop, second, "and loops as any passage")
        await session.step("Цикл is turned off") { player.apply(.cycle) }
        // The session's player has no sound loaded and is never moved here, so its playhead stays at tick 0, in ①: the
        // place at a playhead elsewhere is the device's line of DRAFT-03.
        await session.step("«↻ Место»") { await score.loopPlace(on: player) }
        XCTAssertTrue(player.cycle, "«↻ Место» turns Цикл on")
        XCTAssertEqual(player.loop, Double(places[0].onset)..<Double(places[0].end), "and loops the place at the playhead, ①")
        await session.step("«▶ Всё»") { score.playDraft(on: player) }
        XCTAssertFalse(player.cycle, "«▶ Всё»: Цикл off, the whole draft plays on")
        XCTAssertNil(player.loop)
        XCTAssertNotNil(player.selection, "the selection stays")
    }

    // MARK: - DRAFT-04 and DRAFT-05: a приём's numbers, its card, «Скрыть номера»

    /// A draft whose one place has the right hand of the same bars as a variant: one приём, «упрощение», in bars 2–4.
    private func priyom() async throws -> (sources: SourcesController, draft: SourceLayers.Draft, view: OpenPiece, score: ScoreController, file: URL) {
        let (workspace, file) = try await pieceOnScreen()
        let fragments = [Fragment(first: 1, last: 4), Fragment(first: 1, last: 4, part: "right", drop: "variant")]
        let draft = try await draft(workspace.sources, fragments)
        let view = try await shown(draft, workspace.sources)
        return (workspace.sources, draft, view, await viewed(view), file)
    }

    /// The states of the приёмы' occurrences on the draft's view opened again.
    private func states(_ score: ScoreController, _ draft: SourceLayers.Draft, _ sources: SourcesController) async throws -> [String] {
        let again = try await shown(draft, sources)
        await score.draftModel.load(again.draft, on: again.id)
        return score.draftModel.spots.map(\.occurrence.state)
    }

    /// DRAFT-04: a приём's number stands in plum over each bar where it is; a tap opens its card there: its kind as the
    /// engine read it, how sure, how many of its notes are in place, and only the steps music-core takes; «Только здесь»
    /// takes it in that bar alone, saved at once, its toast's Undo undoes it; the pill's ‹ › step from one to the next.
    func testDRAFT04APriyomIsNumberedOverItsBarsAndTakenOnlyAsThePlayerSays() async throws {
        let (sources, draft, _, score, file) = try await priyom()
        let numbers = score.placeMarkers.filter { $0["technique"] != nil }
        XCTAssertEqual(numbers.compactMap { $0["bar"] as? Int }, [2, 3, 4], "its number over each bar where it is")
        XCTAssertEqual(Set(numbers.compactMap { $0["text"] as? String }), ["1"])
        XCTAssertEqual(Set(numbers.compactMap { $0["state"] as? String }), ["open"])
        let key = try XCTUnwrap(numbers[1]["technique"] as? String)
        score.pickMarker(.techniqueNumber(key, left: 40, bottom: 60))
        let spot = try XCTUnwrap(score.draftModel.picked?.spot, "a tap on the number picks it")
        XCTAssertEqual(score.draftModel.picked?.at, CGPoint(x: 40, y: 60), "its card at the number")
        XCTAssertEqual(spot.occurrence.bar, 3)
        XCTAssertTrue(spot.technique.inferred, "its kind is the engine's reading: «найден движком»")
        XCTAssertEqual(DraftTechniquesModel.title(spot, russian), "Приём 1 · упрощение")
        XCTAssertEqual(DraftTechniquesModel.status(spot, russian), "уверенно · 4 из 4 нот на месте")
        XCTAssertEqual(spot.offer, DraftTechniquesModel.Offer(steps: true, hands: true, everywhere: 3), "every step while open")
        XCTAssertEqual(russian.format("drafts.takeEverywhere", String(spot.offer.everywhere)), "Взять везде · 3")
        XCTAssertTrue(score.canHear(spot, variant: false), "«Как в нотах»")
        XCTAssertNil(score.recordingHeard(spot), "no recording here: «Как в варианте», its own notes")
        XCTAssertTrue(score.canHear(spot, variant: true))

        let saved = try String(contentsOf: file, encoding: .utf8)
        let here = await sources.take(spot, here: true)
        XCTAssertTrue(here, sources.status ?? "")
        XCTAssertNotEqual(try String(contentsOf: file, encoding: .utf8), saved, "saved at once")
        guard case .draftStep(let undone)? = sources.toast?.undo else { return XCTFail("its toast undoes the step") }
        XCTAssertEqual(undone, draft.id)
        let taken = try await states(score, draft, sources)
        XCTAssertEqual(taken, ["open", "taken", "open"], "«Только здесь»: that bar alone")
        XCTAssertFalse(score.draftModel.spots[1].offer.steps, "taken there: no step is offered again")
        await sources.undo()
        let undoneStates = try await states(score, draft, sources)
        XCTAssertEqual(undoneStates, ["open", "open", "open"], "Undo")

        XCTAssertTrue(score.draftModel.stepping, "the pill's ‹ › step through the приёмы")
        score.stepTechnique(1)
        XCTAssertEqual(score.draftModel.picked?.spot.occurrence.bar, 4, "› goes to the next")
        XCTAssertNil(score.draftModel.picked?.at, "a step shows no card")
        score.stepTechnique(1)
        XCTAssertEqual(score.draftModel.picked?.spot.occurrence.bar, 4, "and stays at the last")
    }

    /// DRAFT-05: «Скрыть номера» leaves the draft to practise on: the numbers leave the page, nothing is picked and no card
    /// opens, the pill's ‹ › give way to the phrase's, the places' labels stay; «Показать номера» brings them back.
    func testDRAFT05HidingTheNumbersLeavesTheDraftToPractiseOn() async throws {
        let (_, _, _, score, _) = try await priyom()
        let key = try XCTUnwrap(score.draftModel.spots.first?.key)
        score.pickMarker(.techniqueNumber(key, left: 0, bottom: 0))
        XCTAssertNotNil(score.draftModel.picked)
        XCTAssertEqual(russian["drafts.hideNumbers"], "Скрыть номера")
        score.hideTechniqueNumbers(true)
        XCTAssertTrue(score.placeMarkers.filter { $0["technique"] != nil }.isEmpty, "the numbers leave the page")
        XCTAssertEqual(score.placeMarkers.filter { $0["place"] as? Bool == true }.count, 1, "the place's label stays")
        XCTAssertNil(score.draftModel.picked, "nothing is picked")
        XCTAssertFalse(score.draftModel.stepping, "the phrase's ‹ › come back")
        score.pickMarker(.techniqueNumber(key, left: 0, bottom: 0))
        XCTAssertNil(score.draftModel.picked, "no card opens")
        XCTAssertEqual(russian["drafts.showNumbers"], "Показать номера")
        score.hideTechniqueNumbers(false)
        XCTAssertEqual(score.placeMarkers.filter { $0["technique"] != nil }.count, 3, "«Показать номера»: back")
        XCTAssertTrue(score.draftModel.stepping)
    }

    // MARK: - DRAFT-06: a seam

    /// The seam of a draft's view opened again, as its diamond shows it; nil where none shows.
    private func seam(_ draft: SourceLayers.Draft, _ sources: SourcesController) async throws -> (DraftSeamsAnswer.Seam?, ScoreController) {
        let view = try await shown(draft, sources)
        let score = await viewed(view)
        return (score.seamsModel.shown.first, score)
    }

    /// DRAFT-06: where two places meet with the same key, meter and tempo no diamond shows; where they differ, a diamond
    /// stands before the place after the seam, and its card tells both sides and offers the place after it in the key and
    /// the tempo before it, and since 1.2, the keys differing, a bridge (DRAFT-10; the register: Drafts12BaselineTests);
    /// nothing changes by itself: each place plays in its own key and tempo until the player takes an offer, a step its toast
    /// undoes; «Оставить» keeps the seam as it is, remembered.
    func testDRAFT06ASeamShowsWhatDiffersAndChangesNothingByItself() async throws {
        let (workspace, _) = try await pieceOnScreen()
        let sources = workspace.sources
        let draft = try await draft(sources, [Fragment(first: 1, last: 4), Fragment(first: 9, last: 12)])
        let (same, plain) = try await seam(draft, sources)
        XCTAssertNil(same, "the same key, meter and tempo: no diamond")
        XCTAssertEqual(plain.seamsModel.seams.count, 1, "one seam where ① and ② meet")
        // ② in another key and tempo, as a fragment of another piece brings them.
        let second = try XCTUnwrap(plain.piece?.draft?.places.last)
        let steps: [(String, [String: Any])] = [("drafts.transpose", ["semitones": 2]), ("drafts.tempo", ["quarterNotesPerMinute": 140])]
        let place: [String: Any] = ["draft": draft.id, "place": second.id, "now": "2026-11-20T11:00:00Z"]
        for (name, value) in steps {
            let fields = value.merging(place) { kept, _ in kept }
            let changed = await sources.change(name, fields)
            XCTAssertNotNil(changed, sources.status ?? "")
        }
        let (differs, score) = try await seam(draft, sources)
        let diamond = try XCTUnwrap(differs, "the key and the tempo differ: a diamond")
        XCTAssertEqual(diamond.changes, ["key", "tempo"])
        XCTAssertEqual(DraftSeamsModel.changes(diamond, russian), "тональность, темп")
        let marker = try XCTUnwrap(score.placeMarkers.first { $0["seam"] != nil })
        XCTAssertEqual(marker["bar"] as? Int, second.firstBar, "before the first bar of the place after it")
        XCTAssertEqual(marker["state"] as? String, "open")
        XCTAssertEqual(diamond.sides.map { $0.key?.name }, ["ля минор", "си минор"], "both sides, each as it is")
        XCTAssertTrue(diamond.listen.onset < diamond.at && diamond.at < diamond.listen.end, "«Послушать стык»: the bars either side")
        XCTAssertEqual(diamond.offers.map(\.label), ["② в ля минор", "Темп как в ①"], "the key and the tempo before it")
        XCTAssertNotNil(diamond.bridge, "and, the keys differing, «Связка · N» beside them (1.2)")
        XCTAssertNil(diamond.register, "the same register: nothing said of it")
        let viewId = try XCTUnwrap(score.piece?.id)
        let tempos = await PieceCore(piece: viewId).playback().value?.tempos ?? []
        XCTAssertTrue(tempos.contains { $0.at == Double(second.onset) && abs(60_000_000 / $0.microsecondsPerQuarter - 140) < 1 }, "② in its own tempo")

        let tempo = try XCTUnwrap(diamond.offers.last)
        let taken = await sources.take(tempo, of: draft.id)
        XCTAssertTrue(taken, sources.status ?? "")
        guard case .draftStep? = sources.toast?.undo else { return XCTFail("its toast undoes the step") }
        let (after, _) = try await seam(draft, sources)
        XCTAssertEqual(after?.changes, ["key"], "«Темп как в ①»: the tempo goes, the key stays")
        let open = try XCTUnwrap(after)
        let left = await sources.leave(open, of: draft.id)
        XCTAssertTrue(left, sources.status ?? "")
        let (kept, keptScore) = try await seam(draft, sources)
        XCTAssertEqual(kept?.left, true, "«Оставить»: kept as it is, remembered")
        XCTAssertEqual(keptScore.placeMarkers.first { $0["seam"] != nil }?["state"] as? String, "left")
    }

    // MARK: - DRAFT-07 and DRAFT-08: the draft's map and its shelf

    /// DRAFT-07: the draft's map shows each place with its variants, the base marked; «Взять всё уверенное · N» takes every
    /// sure приём of the place, counted in Sources; «Сделать основой» makes another variant the base and its place's steps
    /// go; «Перенести…» moves a variant to a new place; each a step its toast undoes.
    func testDRAFT07TheMapTakesWhatIsSureMakesABaseAndMovesAVariant() async throws {
        let (sources, draft, view, score, _) = try await priyom()
        let place = try XCTUnwrap(view.draft?.places.first)
        XCTAssertEqual(place.variants.map(\.base), [true, false], "the base marked")
        XCTAssertEqual(place.variants.map(\.label), ["Ноты 1–4", "Ноты 1–4 · правая рука"])
        XCTAssertEqual(russian["drafts.baseTag"], "основа")
        let sure = try XCTUnwrap(score.draftModel.places.first?.sure)
        XCTAssertEqual(russian.format("drafts.takeSureCount", String(sure)), "Взять всё уверенное · 1")
        let all = await sources.takeSure(place, of: draft.id)
        XCTAssertTrue(all, sources.status ?? "")
        let tallied = try XCTUnwrap(sources.layers.drafts.first)
        XCTAssertEqual(SourcesDraftRow.caption(tallied, russian), "из нот из файла · 1 место · взят 1 приём из 1", "counted in Sources")
        await sources.undo()
        XCTAssertEqual(sources.layers.drafts.first?.taken, 0, "Undo")

        let other = place.variants[1]
        let based = await sources.makeBase(other, of: place, in: draft.id)
        XCTAssertTrue(based, sources.status ?? "")
        let rebased = try await shown(draft, sources)
        XCTAssertEqual(rebased.draft?.places.first?.variants.first { $0.base }?.id, other.id, "«Сделать основой»")
        XCTAssertEqual(rebased.draft?.places.first?.steps, 0, "the place starts again from its new base")
        await sources.undo()
        let moved = await sources.put(other.id, label: other.label, into: nil, of: draft.id)
        XCTAssertTrue(moved, sources.status ?? "")
        let apart = try await shown(draft, sources)
        XCTAssertEqual(apart.draft?.places.map { $0.variants.map(\.id) }, [[place.variants[0].id], [other.id]], "«Новым местом в конце»")
        await sources.undo()
        let back = try await shown(draft, sources)
        XCTAssertEqual(back.draft?.places.map { $0.variants.map(\.id) }, [place.variants.map(\.id)], "Undo")
    }

    /// DRAFT-08: a fragment put on the shelf waits there, counted in the pill's «Полка · N», in no place; «Поставить…» puts
    /// it into a place as a variant or as a new place, and a variant goes back onto the shelf from the map; each a step its
    /// toast undoes.
    func testDRAFT08TheShelfKeepsAFragmentUntilItIsPut() async throws {
        let (workspace, _) = try await pieceOnScreen()
        let sources = workspace.sources
        let draft = try await draft(sources, [Fragment(first: 1, last: 4), Fragment(first: 5, last: 8, drop: "shelf")])
        let view = try await shown(draft, sources)
        let shelved = try XCTUnwrap(view.draft?.shelf.first, "on the shelf")
        let place = try XCTUnwrap(view.draft?.places.first)
        XCTAssertEqual(view.draft?.places.count, 1, "in no place")
        XCTAssertEqual(russian.format("drafts.shelfCount", String(view.draft?.shelf.count ?? 0)), "Полка · 1")
        XCTAssertEqual(russian["drafts.put"], "Поставить…")
        let put = await sources.put(shelved.id, label: shelved.label, into: place.id, of: draft.id)
        XCTAssertTrue(put, sources.status ?? "")
        let placed = try await shown(draft, sources)
        XCTAssertEqual(placed.draft?.places.first?.variants.map(\.id), [place.variants[0].id, shelved.id], "a variant of the place")
        XCTAssertEqual(placed.draft?.shelf, [])
        let shelvedAgain = await sources.put(shelved.id, label: shelved.label, into: nil, shelf: true, of: draft.id)
        XCTAssertTrue(shelvedAgain, sources.status ?? "")
        let back = try await shown(draft, sources)
        XCTAssertEqual(back.draft?.shelf.map(\.id), [shelved.id], "back onto the shelf from the map")
        await sources.undo()
        let undone = try await shown(draft, sources)
        XCTAssertEqual(undone.draft?.shelf, [], "Undo")
        XCTAssertEqual(undone.draft?.places.first?.variants.count, 2)
    }
}
