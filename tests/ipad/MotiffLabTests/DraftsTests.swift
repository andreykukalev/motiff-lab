import XCTest

@testable import MotiffLab

/// Drafts on the iPad (v1.1, ADR 0034 section 9 step 5): the «В черновик» sheet's choices (`DraftChoice`, from music-core's
/// `drafts.sheet`), and a draft's tab in the switch, which opens the draft by `drafts.open` over the music-core of the
/// tests, never as Notes, with its places' labels for the score.
@MainActor
final class DraftsTests: XCTestCase {
    private var opened: [Int32] = []

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    private func sheet(_ json: String) throws -> DraftSheetAnswer {
        try XCTUnwrap(FacadeAnswer.decode(DraftSheetAnswer.self, json, of: "drafts.sheet"))
    }

    /// A piece with no draft yet: the fragment starts a new one, as its first place, and nothing else is offered.
    func testWithNoDraftTheFragmentStartsANewOne() throws {
        let first = try sheet(#"{"parts": ["all", "right", "left"], "part": "all", "notes": 27, "newName": "Черновик 1", "drafts": [], "suggested": null}"#)
        let choice = DraftChoice.first(first, part: first.part)
        XCTAssertEqual(choice.into, .new)
        XCTAssertEqual(choice.way.drop, .place)
        XCTAssertEqual(DraftChoice.ways(nil), [DraftChoice.Way(drop: .place)], "a new draft takes it as its first place only")
        let fields = choice.fields(span: 1440..<2880, now: "2026-11-20T10:00:00Z")
        XCTAssertNil(fields["draft"], "no draft named: music-core makes a new one")
        XCTAssertEqual(fields["drop"] as? String, "place")
        XCTAssertEqual(fields["part"] as? String, "all")
        XCTAssertEqual(fields["span"] as? [String: Int], ["onset": 1440, "end": 2880])
        XCTAssertNil(fields["place"])
    }

    /// The same music in a place of a draft: that draft, as a variant of that place, is offered first, with its percentage;
    /// then a variant of each other place (without one where it is not the same music), a new place at the end and the
    /// shelf.
    func testTheSameMusicIsOfferedFirstAsAVariantOfItsPlace() throws {
        let read = try sheet(#"""
        {"parts": ["all", "right", "left", "melody"], "part": "all", "notes": 27, "newName": "Draft 3",
         "drafts": [
           {"id": "d_1", "name": "Draft 1", "suggested": "pl_2", "places": [
             {"id": "pl_1", "label": "① Notes 1–4", "percent": 60, "sameMusic": true},
             {"id": "pl_2", "label": "② Notes 5–8", "percent": 100, "sameMusic": true},
             {"id": "pl_3", "label": "③ Notes 9–12", "percent": 20, "sameMusic": false}]},
           {"id": "d_2", "name": "Draft 2", "suggested": null, "places": [{"id": "pl_1", "label": "① Notes 1–4", "percent": 0, "sameMusic": false}]}],
         "suggested": {"draft": "d_1", "place": "pl_2", "percent": 100}}
        """#)
        let choice = DraftChoice.first(read, part: "all")
        XCTAssertEqual(choice.into, .draft("d_1"), "the draft music-core suggests, not the latest")
        XCTAssertEqual(choice.way, DraftChoice.Way(drop: .variant, place: "pl_2", label: "② Notes 5–8", percent: 100))
        let ways = DraftChoice.ways(DraftChoice.draft(.draft("d_1"), in: read))
        XCTAssertEqual(ways.map(\.drop), [.variant, .variant, .variant, .place, .shelf])
        XCTAssertEqual(ways.compactMap(\.place), ["pl_2", "pl_1", "pl_3"], "the suggested place first, the others that are the same music, then the rest")
        XCTAssertEqual(ways.map(\.percent), [100, 60, nil, nil, nil], "how much is the same music only where it is")
        let fields = choice.fields(span: 0..<2880, now: "now")
        XCTAssertEqual(fields["draft"] as? String, "d_1")
        XCTAssertEqual(fields["drop"] as? String, "variant")
        XCTAssertEqual(fields["place"] as? String, "pl_2")

        let other = DraftChoice.first(read, part: "right", into: .draft("d_2"))
        XCTAssertEqual(other.into, .draft("d_2"), "the draft the player chose stays as the part changes")
        XCTAssertEqual(other.way.drop, .place, "no same music there: a new place at the end")
        XCTAssertEqual(DraftChoice.ways(DraftChoice.draft(.draft("d_2"), in: read)), [
            DraftChoice.Way(drop: .variant, place: "pl_1", label: "① Notes 1–4"), DraftChoice.Way(drop: .place), DraftChoice.Way(drop: .shelf),
        ], "a variant of any place can be chosen")
        XCTAssertEqual(other.part, "right")
        XCTAssertEqual(DraftChoice.first(read, part: "all", into: .new).into, .new)
        XCTAssertEqual(DraftChoice.first(read, part: "all", into: .draft("d_gone")).into, .draft("d_1"), "a draft no longer there gives way")
    }

    /// No same music anywhere: the piece's latest draft, as a new place at the end; the shelf too.
    func testWithoutTheSameMusicTheLatestDraftTakesANewPlace() throws {
        let read = try sheet(#"""
        {"parts": ["all"], "part": "all", "notes": 4, "newName": "Draft 3",
         "drafts": [{"id": "d_1", "name": "Draft 1", "suggested": null, "places": []}, {"id": "d_2", "name": "Draft 2", "suggested": null, "places": []}],
         "suggested": null}
        """#)
        let choice = DraftChoice.first(read, part: "all")
        XCTAssertEqual(choice.into, .draft("d_2"))
        XCTAssertEqual(choice.way.drop, .place)
        XCTAssertEqual(DraftChoice.ways(read.drafts.last).map(\.drop), [.place, .shelf])
        XCTAssertEqual(choice.fields(span: 0..<480, now: "now")["drop"] as? String, "place")
    }

    func testThePartsAreWordedInBothLanguages() {
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for part in ["all", "right", "left", "melody"] {
                XCTAssertFalse(DraftChoice.word(part: part, text).hasPrefix("drafts."), "\(language): \(part)")
            }
        }
        XCTAssertEqual(DraftChoice.word(part: "right", UIText(language: "ru")), "Правая рука")
    }

    /// A fragment added from the sheet is a draft in Sources, its view last in the switch with its name; picked there, the
    /// draft opens by `drafts.open` on a screen of its own (never as Notes), its places with it for the labels above them.
    func testADraftsTabOpensTheDraftWithItsPlaces() async throws {
        let (workspace, piece) = try await sourced()
        let new = DraftChoice(part: "all", into: .new, way: DraftChoice.Way(drop: .place))
        let added = await workspace.sources.addToDraft(new, span: 0..<1440, from: piece.id)
        XCTAssertTrue(added, workspace.sources.status ?? "")

        let layers = workspace.sources.layers
        let draft = try XCTUnwrap(layers.drafts.first)
        XCTAssertEqual(layers.drafts.count, 1)
        XCTAssertEqual(draft.places, 1)
        let tab = try XCTUnwrap(layers.views.last, "a draft's view comes last")
        XCTAssertEqual(tab.kind, "draft")
        XCTAssertEqual(tab.id, draft.id)
        XCTAssertEqual(SourceLayers.word(tab, UIText.current), draft.name, "its name is its word in the switch")
        XCTAssertEqual(SourceLayers.pictogram(tab), "assemble")

        let shown = expectation(description: "the draft is shown")
        var view: ScoreScreenContent.ShownView?
        workspace.pickView(tab, switchView: nil, text: UIText.current) { picked in
            view = picked
            shown.fulfill()
        }
        await fulfillment(of: [shown], timeout: 20)
        let screen = try XCTUnwrap(view)
        opened.append(screen.piece.id)
        XCTAssertFalse(screen.pdf)
        XCTAssertNotEqual(screen.piece.id, piece.id, "a number of its own, only to be shown")
        XCTAssertEqual(screen.piece.title, draft.name)
        let places = try XCTUnwrap(screen.piece.draft?.places)
        XCTAssertEqual(places.count, 1)
        XCTAssertEqual(places[0].firstBar, 0, "Für Elise starts with its pickup, bar 0, and so does the place taken from 0")
        XCTAssertTrue(places[0].label.hasPrefix("①"), places[0].label)
        XCTAssertFalse(workspace.panels[.versions], "nothing went wrong, so Sources did not open")

        // On the draft's own screen the labels stand above each place's start; a tap there selects the place.
        let draftView = ScoreWorkspace(piece: screen.piece, viewing: "view only")
        draftView.score.show(screen.piece, dark: false)
        let markers = draftView.score.placeMarkers
        XCTAssertEqual(markers.count, 1)
        XCTAssertEqual(markers[0]["text"] as? String, places[0].label)
        XCTAssertEqual(markers[0]["place"] as? Bool, true)
        XCTAssertEqual(markers[0]["bar"] as? Int, 0, "the bar the label's tap posts")
        XCTAssertFalse(draftView.score.pickPlace(startingAt: 99), "no place starts there")
        XCTAssertFalse(draftView.score.pickPlace(startingAt: 1), "bar 1 is inside the place, not its start")
        XCTAssertTrue(draftView.score.pickPlace(startingAt: 0))
        for _ in 0..<100 where draftView.score.selectionModel.selection == nil { try await Task.sleep(for: .milliseconds(50)) }
        let selected = try XCTUnwrap(draftView.score.selectionModel.selection, "music-core selects on a draft's view")
        XCTAssertEqual(selected.onset, Double(places[0].onset))
        XCTAssertNil(draftView.draftOwner(under: workspace.sources), "no «Черновик» on a draft's own view")
        XCTAssertTrue(workspace.score.placeMarkers.isEmpty, "the piece itself is no draft")
        XCTAssertTrue(workspace.draftOwner(under: nil) === workspace.sources, "on the piece, its own drafts")
    }

    /// Notes shown over the piece (`sources.notes.open`): a passage is selected there, music-core answering on a view only
    /// to be shown, and goes into a draft of the piece they were opened from, sent to the shown notes' number.
    func testAFragmentSelectedOnNotesShownOverThePieceGoesIntoItsDraft() async throws {
        let (workspace, piece) = try await sourced()
        let working = try XCTUnwrap(workspace.sources.layers.notes(workspace.sources.layers.working))
        let openedNotes = await workspace.sources.openNotes(working, title: "Notes", at: nil)
        let shown = try XCTUnwrap(openedNotes, workspace.sources.status ?? "")
        opened.append(shown.id)
        XCTAssertNotEqual(shown.id, piece.id)
        let view = ScoreWorkspace(piece: shown, viewing: "view only")
        view.score.show(shown, dark: false)
        await view.score.select(request: #"{"bars": {"first": 1, "last": 2}}"#, step: false)
        XCTAssertNotNil(view.score.selectionModel.selection, workspace.sources.status ?? "")
        XCTAssertTrue(view.draftOwner(under: workspace.sources) === workspace.sources, "the piece under the view owns its drafts")

        let request = try XCTUnwrap(view.draftSheet())
        XCTAssertEqual(request.piece, shown.id)
        XCTAssertNil(request.into)
        let added = await workspace.sources.addToDraft(DraftChoice(part: "all", into: .new, way: DraftChoice.Way(drop: .place)), span: request.span, from: request.piece)
        XCTAssertTrue(added, workspace.sources.status ?? "")
        XCTAssertEqual(workspace.sources.layers.drafts.count, 1)
    }

    /// «Новый черновик» with nothing selected: Sources gives way to the notes and the hint to select them shows; no empty
    /// draft is made. With a passage selected, the sheet opens for a new draft.
    func testANewDraftAsksForNotesUntilSomeAreSelected() async throws {
        let (workspace, piece) = try await sourced()
        workspace.change(.open(.versions))
        XCTAssertNil(workspace.newDraft(), "no empty draft")
        XCTAssertFalse(workspace.panels[.versions], "Sources gives way to the notes")
        XCTAssertEqual(workspace.score.notice?.key, "drafts.selectNotes")
        XCTAssertTrue(workspace.sources.layers.drafts.isEmpty)

        workspace.score.show(piece, dark: false)
        await workspace.score.select(request: #"{"bars": {"first": 1, "last": 2}}"#, step: false)
        let request = try XCTUnwrap(workspace.newDraft())
        XCTAssertEqual(request.into, .new)
        XCTAssertEqual(request.piece, piece.id)
    }

    /// «Удалить…» on a draft's row: only the draft goes, and the toast's Undo writes the project file back as it was and
    /// opens the piece again from it.
    func testADraftIsRemovedAndTheToastUndoesIt() async throws {
        let (workspace, piece) = try await sourced()
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).motiff.json")
        addTeardownBlock { try? FileManager.default.removeItem(at: file) }
        let sources = workspace.sources
        await sources.load(piece: piece.id, file: file)
        var reopened = 0
        sources.reopen = { reopened += 1 }
        let added = await sources.addToDraft(DraftChoice(part: "all", into: .new, way: DraftChoice.Way(drop: .place)), span: 0..<1440, from: piece.id)
        XCTAssertTrue(added, sources.status ?? "")
        let draft = try XCTUnwrap(sources.layers.drafts.first)

        await sources.removeDraft(draft)
        XCTAssertTrue(sources.layers.drafts.isEmpty)
        XCTAssertNotNil(sources.layers.working, "its notes stay")
        XCTAssertFalse(try String(contentsOf: file, encoding: .utf8).contains(draft.id), "saved at once")
        guard case .restore(let before)? = sources.toast?.undo else { return XCTFail("the toast undoes it") }
        XCTAssertTrue(before.contains(draft.id))
        await sources.undo()
        XCTAssertEqual(try String(contentsOf: file, encoding: .utf8), before, "the file as it was")
        XCTAssertEqual(reopened, 1, "the piece opens again from it")
    }

    /// A draft whose place has a variant (Für Elise bars 1–4, and 5–8 added to it): its view numbers the приём above its
    /// bar, a tap picks it, «Взять везде» takes it (saved, its toast undoes it with `drafts.undo`), the view opened again
    /// at it shows it taken, the hand and «Оставить» are steps too, and Sources counts what is taken.
    func testAPriyomIsNumberedTakenAndUndone() async throws {
        let (workspace, _) = try await sourced()
        let sources = workspace.sources
        let first = await sources.change("drafts.add", ["bars": ["first": 1, "last": 4], "now": "2026-11-20T10:00:00Z"])
        XCTAssertNotNil(first, sources.status ?? "")
        let draft = try XCTUnwrap(sources.layers.drafts.first)
        var view = try await draftView(draft, sources)
        let place = try XCTUnwrap(view.draft?.places.first)
        let variant = await sources.change("drafts.add", ["bars": ["first": 5, "last": 8], "draft": draft.id, "drop": "variant", "place": place.id, "now": "2026-11-20T10:05:00Z"])
        XCTAssertNotNil(variant, sources.status ?? "")
        view = try await draftView(draft, sources)

        let screen = ScoreWorkspace(piece: view, viewing: "view only")
        let model = screen.score.draftModel
        await model.load(view.draft, on: view.id)
        let spot = try XCTUnwrap(model.spots.first, "Für Elise's bar 8 against bar 4: one приём")
        XCTAssertEqual(model.total, 1)
        XCTAssertEqual(model.taken, 0)
        XCTAssertTrue(spot.technique.inferred)
        let numbers = screen.score.placeMarkers.filter { $0["technique"] != nil }
        XCTAssertEqual(numbers.count, 1, "one number above its bar, beside the place's label")
        XCTAssertEqual(numbers[0]["text"] as? String, String(spot.technique.number))
        XCTAssertEqual(numbers[0]["state"] as? String, "open")
        XCTAssertEqual(model.pick(spot.key, at: CGPoint(x: 10, y: 20)), spot)
        XCTAssertEqual(model.picked?.at, CGPoint(x: 10, y: 20), "its card at the number")
        XCTAssertEqual(model.step(1), spot, "the last stays the current one")
        XCTAssertNil(model.picked?.at, "stepping shows no card")
        XCTAssertTrue(screen.score.canHear(spot, variant: false), "open: the draft sounds as the base there")
        XCTAssertNil(spot.occurrence.recording, "not heard from a recording")
        XCTAssertTrue(screen.score.canHear(spot, variant: true), "the variant's own notes, opened alone")
        let variantNotes = await DraftTechniquesModel.variantPlayback(spot, on: view.id)
        XCTAssertFalse(variantNotes?.notes.isEmpty ?? true, "the variant's music, opened alone and closed again")
        XCTAssertEqual(spot.offer, DraftTechniquesModel.Offer(steps: true, hands: true, everywhere: 1), "open: every step")
        XCTAssertTrue(model.stepping, "the pill's ‹ ›, in place of the phrase's")
        XCTAssertEqual(screen.score.currentPlace(at: 0)?.id, spot.place, "«↻ Место» loops the current occurrence's place")

        let taken = await sources.take(spot, here: false)
        XCTAssertTrue(taken, sources.status ?? "")
        guard case .draftStep(let undone)? = sources.toast?.undo else { return XCTFail("its toast undoes the step") }
        XCTAssertEqual(undone, draft.id)
        let reopened = await sources.openDraft(draft, at: spot.span.lowerBound)
        let again = try XCTUnwrap(reopened, sources.status ?? "")
        opened.append(again.id)
        XCTAssertEqual(again.landing, .tick(spot.span.lowerBound), "opened again where the приём is")
        await model.load(again.draft, on: again.id)
        let takenSpot = try XCTUnwrap(model.spots.first)
        XCTAssertTrue(takenSpot.isTaken)
        XCTAssertFalse(screen.score.canHear(takenSpot, variant: false), "taken: the draft's notes there are the variant's")
        XCTAssertTrue(screen.score.canHear(takenSpot, variant: true))
        XCTAssertEqual(sources.layers.drafts.first?.taken, 1, "the Sources tally, from music-core")
        XCTAssertEqual(takenSpot.offer, DraftTechniquesModel.Offer(steps: false, hands: false, everywhere: 0), "taken: music-core refuses another step")

        await sources.undo()
        await model.load(again.draft, on: again.id)
        XCTAssertEqual(model.spots.first?.isTaken, false, "undone by drafts.undo")

        let hand = await sources.takeHand("right", at: spot)
        XCTAssertTrue(hand, sources.status ?? "")
        await sources.undo()
        let left = await sources.leave(spot)
        XCTAssertTrue(left, sources.status ?? "")
        await model.load(again.draft, on: again.id)
        XCTAssertEqual(model.spots.first?.occurrence.state, "left")

        model.hideNumbers(true)
        XCTAssertTrue(model.markers(UIText.current).isEmpty, "«Скрыть номера»: practice on the draft")
        XCTAssertNil(model.pick(spot.key, at: nil))
        XCTAssertNil(model.step(1))
        XCTAssertFalse(model.stepping, "hidden: the phrase's ‹ › come back")
    }

    /// The card offers only what music-core takes: an open occurrence every step, a hand only over bars with notes (an
    /// inserted bar not yet put in has none), one taken or left nothing; «Взять везде · N» counts the open ones.
    func testTheCardOffersOnlyTheStepsMusicCoreTakes() throws {
        let json = #"""
        {"draft": "dr_0001", "place": "pl_0001", "total": 1, "taken": 0, "sure": 1, "techniques": [{"number": 1, "variant": "va_0001",
        "kind": "octave", "label": "октава", "inferred": true, "confidence": 90, "sure": true, "state": "open", "occurrences": [
        {"bar": 4, "firstBar": 4, "lastBar": 4, "onset": 5760, "end": 7200, "confidence": 90, "matched": 3, "total": 4, "state": "open",
         "inVariant": {"onset": 0, "end": 1440}, "recording": null},
        {"bar": 6, "firstBar": 6, "lastBar": 6, "onset": 8640, "end": 8640, "confidence": 80, "matched": 0, "total": 2, "state": "open",
         "inVariant": {"onset": 2880, "end": 4320}, "recording": {"project": "p_1", "original": "src_rec", "from": 3000, "to": 4500}},
        {"bar": 8, "firstBar": 8, "lastBar": 8, "onset": 11520, "end": 12960, "confidence": 90, "matched": 4, "total": 4, "state": "taken",
         "inVariant": {"onset": 5760, "end": 7200}, "recording": null},
        {"bar": 9, "firstBar": 9, "lastBar": 9, "onset": 12960, "end": 14400, "confidence": 90, "matched": 1, "total": 1, "state": "left",
         "inVariant": {"onset": 7200, "end": 8640}, "recording": null}]}]}
        """#
        let read = try XCTUnwrap(FacadeAnswer.decode(DraftTechniquesAnswer.self, json, of: "drafts.techniques"))
        let model = DraftTechniquesModel()
        model.keep([read])
        let offers = model.spots.map(\.offer)
        XCTAssertEqual(offers.map(\.steps), [true, true, false, false])
        XCTAssertEqual(offers.map(\.hands), [true, false, false, false], "no hand over a bar not yet put in")
        XCTAssertEqual(offers.map(\.everywhere), [2, 2, 2, 2], "«Взять везде · 2»: the open ones")
        let russian = UIText(language: "ru")
        XCTAssertEqual(model.spots.map { DraftTechniquesModel.status($0, russian) }, [
            "уверенно · 3 из 4 нот на месте", "уверенно · 0 из 2 нот на месте", "уверенно · 4 из 4 нот на месте · взят",
            "уверенно · 1 из 1 ноты на месте · оставлен как в основе",
        ])
        XCTAssertEqual(model.spots[1].occurrence.recording?.from, 3000, "heard from a recording's stretch")
    }

    /// On a view only to be shown (Notes or a draft over the piece) the selection menu offers no Note, Bookmark, Insights or
    /// More, which music-core refuses there; elsewhere all of them, Insights only over notes.
    func testAViewOnlyMenuOffersOnlyWhatWorksThere() {
        XCTAssertEqual(SelectionMenu.offered(viewOnly: true, notes: true), [])
        XCTAssertEqual(SelectionMenu.offered(viewOnly: false, notes: true), [.note, .bookmark, .insights, .more])
        XCTAssertEqual(SelectionMenu.offered(viewOnly: false, notes: false), [.note, .bookmark, .more])
    }

    /// The draft opened to be shown, closed when the test ends.
    private func draftView(_ draft: SourceLayers.Draft, _ sources: SourcesController) async throws -> OpenPiece {
        let opening = await sources.openDraft(draft)
        let shown = try XCTUnwrap(opening, sources.status ?? "")
        opened.append(shown.id)
        return shown
    }

    /// The kit's piece open in music-core, with its sources read.
    private func sourced() async throws -> (ScoreWorkspace, OpenPiece) {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        let workspace = ScoreWorkspace(piece: piece)
        await workspace.sources.load(piece: piece.id, file: nil)
        return (workspace, piece)
    }
}
