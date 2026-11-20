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
        XCTAssertEqual(places[0].firstBar, 1)
        XCTAssertTrue(places[0].label.hasPrefix("①"), places[0].label)
        XCTAssertFalse(workspace.panels[.versions], "nothing went wrong, so Sources did not open")

        // On the draft's own screen the labels stand above each place's start; a tap there selects the place.
        let draftView = ScoreWorkspace(piece: screen.piece, viewing: "view only")
        draftView.score.show(screen.piece, dark: false)
        let markers = draftView.score.placeMarkers
        XCTAssertEqual(markers.count, 1)
        XCTAssertEqual(markers[0]["text"] as? String, places[0].label)
        XCTAssertEqual(markers[0]["place"] as? Bool, true)
        XCTAssertEqual(markers[0]["bar"] as? Int, 1)
        XCTAssertFalse(draftView.score.pickPlace(startingAt: 99), "no place starts there")
        XCTAssertTrue(draftView.score.pickPlace(startingAt: 1))
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
}
