import XCTest

@testable import MotiffLab

/// A draft's structure on the iPad (v1.4 i-3b, ADR 0034 section 8): a place moved on the map (F1), «Найти на записи»
/// (F2), a place's length and a приём carried. What the cards offer is read from music-core's answers
/// (tests/contracts/facade-answers/drafts.order, .recording, .length, .carry); each step goes through
/// `SourcesController`'s one draft step over the music-core of the tests, says what went with it, and its toast undoes it.
@MainActor
final class DraftStructureTests: XCTestCase {
    private var numbers: [Int32] = []

    override func tearDown() async throws {
        for number in numbers { MusicCore.close(piece: number) }
        numbers = []
    }

    // MARK: - What the cards read

    /// `drafts.movePlace` and `drafts.resize` answer as the sources, with what went ("dropped"); the map shows the order and
    /// the label music-core made.
    func testTheMoveAndTheResizeAnswerAsTheSources() throws {
        let order = try FacadeAnswerFixturesTests.answers("drafts.order")
        XCTAssertEqual(try XCTUnwrap(SourceLayers(answer: order[2].text)).drafts.first?.places, 2)
        XCTAssertEqual(facadeObject(order[2].text)?["dropped"] as? [Int], [])
        let moved = try XCTUnwrap(FacadeAnswer.decode(DraftViewAnswer.self, order[3].text, of: "drafts.open"))
        XCTAssertEqual(moved.draft.places.map(\.label), ["① Notes 5–8", "② Notes 1–4"])

        let lengths = try FacadeAnswerFixturesTests.answers("drafts.length")
        let length = try XCTUnwrap(FacadeAnswer.decode(DraftLengthAnswer.self, lengths[2].text, of: "drafts.length"))
        XCTAssertEqual(length.bars, DraftLengthAnswer.Bars(first: 1, last: 4))
        XCTAssertEqual(length.most, DraftLengthAnswer.Bars(first: 0, last: 105))
        XCTAssertTrue(length.resizable)
        XCTAssertNil(length.reason)
        XCTAssertNotNil(SourceLayers(answer: lengths[3].text))
        XCTAssertEqual(facadeObject(lengths[3].text)?["dropped"] as? [Int], [])
        let resized = try XCTUnwrap(FacadeAnswer.decode(DraftViewAnswer.self, lengths[4].text, of: "drafts.open"))
        XCTAssertEqual(resized.draft.places.first?.label, "① Notes 1–6")
    }

    /// The length chosen stays within the bars the Notes have and at least one bar long, and is offered only once it differs
    /// from the place's own; a place whose length cannot change offers no choice, only music-core's reason.
    func testTheLengthChosenStaysWithinTheNotes() throws {
        let lengths = try FacadeAnswerFixturesTests.answers("drafts.length")
        let length = try XCTUnwrap(FacadeAnswer.decode(DraftLengthAnswer.self, lengths[2].text, of: "drafts.length"))
        let start = try XCTUnwrap(DraftLengthChoice(length))
        XCTAssertNil(start.asked, "the place's own length: no resize")
        var longer = start
        longer.step(last: 2)
        XCTAssertEqual(longer.asked, 1...6)
        var earliest = start
        earliest.step(first: -5)
        XCTAssertEqual(earliest.asked, 0...4, "no bar before the Notes' first")
        var one = start
        one.step(first: 9)
        XCTAssertEqual(one.asked, 4...4, "the first stays at most the last")
        one.step(last: -9)
        XCTAssertEqual(one.asked, 4...4, "the last stays at least the first")
        var latest = start
        latest.step(last: 500)
        XCTAssertEqual(latest.asked, 1...105, "no bar after the Notes' last")
        latest.step(last: -101)
        XCTAssertNil(latest.asked, "back to its own length")

        let bridge = #"{"draft": "d_0001", "place": "pl_0002", "bars": null, "most": null, "resizable": false, "reason": "A bridge keeps its bar."}"#
        let fixed = try XCTUnwrap(FacadeAnswer.decode(DraftLengthAnswer.self, bridge, of: "drafts.length"))
        XCTAssertNil(DraftLengthChoice(fixed))
        XCTAssertEqual(fixed.reason, "A bridge keeps its bar.")
        XCTAssertNil(DraftLengthChoice(nil))
    }

    /// «Найти на записи» puts in the stretch found that the place does not have yet; one it has is named, never added again;
    /// none found, or no answer, leaves the entry off, and its line says why, in both languages.
    func testFindOnRecordingOffersOnlyAStretchThePlaceLacks() throws {
        let recording = try FacadeAnswerFixturesTests.answers("drafts.recording")
        let first = try XCTUnwrap(FacadeAnswer.decode(DraftRecordingAnswer.self, recording[0].text, of: "drafts.findOnRecording"))
        let offer = DraftRecordingOffer(first)
        XCTAssertEqual(offer.adds?.notes, "n_2")
        XCTAssertEqual(offer.line(UIText(language: "ru")), "Recording 0–2 · та же музыка на 77 %")
        XCTAssertNotNil(SourceLayers(answer: recording[1].text), "added: answered as the sources")
        let again = try XCTUnwrap(FacadeAnswer.decode(DraftRecordingAnswer.self, recording[2].text, of: "drafts.findOnRecording"))
        XCTAssertEqual(again.found.first?.variant, "v_0002")
        XCTAssertEqual(DraftRecordingOffer(again), .already(again.found[0]))
        XCTAssertNil(DraftRecordingOffer(again).adds, "a stretch the place has is not added twice")
        XCTAssertEqual(DraftRecordingOffer(again).line(UIText(language: "en")), "Already a variant of this place: “Recording 0–2”")

        let two = #"""
        {"draft": "d_1", "place": "pl_1", "found": [
          {"notes": "n_2", "label": "Recording 1–4", "percent": 90, "variant": "v_2"},
          {"notes": "n_3", "label": "Recording 9–12", "percent": 70, "variant": null}]}
        """#
        let both = try XCTUnwrap(FacadeAnswer.decode(DraftRecordingAnswer.self, two, of: "drafts.findOnRecording"))
        XCTAssertEqual(DraftRecordingOffer(both).adds?.notes, "n_3", "the other recording's stretch, which the place lacks")
        let none = try XCTUnwrap(FacadeAnswer.decode(DraftRecordingAnswer.self, #"{"draft": "d_1", "place": "pl_1", "found": []}"#, of: "drafts.findOnRecording"))
        XCTAssertEqual(DraftRecordingOffer(none), .notFound)
        XCTAssertEqual(DraftRecordingOffer(nil), .notFound)
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for offer in [DraftRecordingOffer.noRecording, .searching, .notFound, .already(both.found[0])] {
                XCTAssertFalse(offer.line(text).hasPrefix("drafts."), "\(language): \(offer)")
            }
        }
    }

    /// Where a приём can be carried: each other place with the bars it fits; the menu's line says why it goes nowhere.
    func testTheCarryPlacesDecodeAndSayWhyNot() throws {
        let carry = try FacadeAnswerFixturesTests.answers("drafts.carry")
        let places = try XCTUnwrap(FacadeAnswer.decode(DraftCarryPlacesAnswer.self, carry[3].text, of: "drafts.carryPlaces"))
        XCTAssertEqual(places.places, [DraftCarryPlacesAnswer.Place(place: "pl_0002", label: "② Notes 1–4", bars: [1, 2, 3, 4])])
        let carried = try XCTUnwrap(SourceLayers(answer: carry[4].text))
        XCTAssertEqual(carried.drafts.first?.caption, "from the file's notes and a carried technique")
        let there = try XCTUnwrap(FacadeAnswer.decode(DraftTechniquesAnswer.self, carry[5].text, of: "drafts.techniques"))
        XCTAssertEqual(there.techniques.map(\.variant), ["v_0004"], "the variant music-core built, its приём to take")

        let english = UIText(language: "en")
        XCTAssertNil(DraftCarryMenu.reason(places, refused: nil, english))
        XCTAssertEqual(DraftCarryMenu.reason(nil, refused: "Bars put in are not carried.", english), "Bars put in are not carried.", "music-core's words")
        let alone = DraftCarryPlacesAnswer(draft: "d_1", place: "pl_1", technique: 1, places: [])
        XCTAssertEqual(DraftCarryMenu.reason(alone, refused: nil, english), english["drafts.carryNoPlace"])
        let nowhere = DraftCarryPlacesAnswer(draft: "d_1", place: "pl_1", technique: 1, places: [DraftCarryPlacesAnswer.Place(place: "pl_2", label: "②", bars: [])])
        XCTAssertEqual(DraftCarryMenu.reason(nowhere, refused: nil, english), english["drafts.carryNoFit"])
    }

    /// The toast of a move or a resize says what went with it, in both languages.
    func testTheToastSaysWhatWent() {
        let russian = UIText(language: "ru")
        XCTAssertEqual(SourcesController.told(russian["drafts.toastMoved"], steps: 2, bridges: 1, russian), "Порядок мест изменён · ушла 1 связка · ушли 2 шага")
        XCTAssertEqual(SourcesController.told("Moved", steps: 1, bridges: 0, UIText(language: "en")), "Moved · 1 step went")
        XCTAssertEqual(SourcesController.told("Moved", steps: 0, bridges: 0, russian), "Moved", "nothing went: nothing said")
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            let keys = ["drafts.toastMoved", "drafts.toastResized", "drafts.toastCarried", "drafts.earlier", "drafts.later", "drafts.onRecording",
                        "drafts.lengthChange", "drafts.lengthApply", "drafts.carryTechnique", "drafts.carryNoPlace", "drafts.carryNoFit"]
            for key in keys {
                XCTAssertNotEqual(text[key], key, "\(language): \(key)")
            }
            XCTAssertFalse(text.count("drafts.goneBridges", 5).hasPrefix("drafts."), language)
            XCTAssertFalse(text.count("drafts.carryBars", 2).hasPrefix("drafts."), language)
        }
    }

    // MARK: - The steps over music-core

    /// F1: a place moved to the front; the «Оставить» of the seam before it goes, and the toast says so; one step at a
    /// time (a resize sent meanwhile is not, and not refused); Undo puts the order and the seam back.
    func testAPlaceMovesItsToastSaysWhatWentAndUndoes() async throws {
        let sources = try await piece()
        let draft = try await newDraft(sources, [1...4, 5...8])
        let before = try await shown(draft, sources)
        guard before.places.count == 2 else { return XCTFail("two places") }
        let left = await sources.change("drafts.leaveSeam", ["draft": draft, "place": before.places[1].id, "now": "2026-11-26T10:10:00Z"])
        XCTAssertNotNil(left, sources.status ?? "")

        let move = Task { await sources.movePlace(before.places[1], to: 0, of: draft) }
        for _ in 0..<1000 where !sources.stepping { await Task.yield() }
        let held = await sources.resize(before.places[0].id, to: 1...5, of: draft)
        XCTAssertFalse(held, "one step at a time")
        XCTAssertNil(sources.refusal(held, told: true, otherwise: "refused"), "a step not sent is not refused")
        let moved = await move.value
        XCTAssertTrue(moved, sources.status ?? "")
        XCTAssertEqual(sources.toast?.text, SourcesController.told(sources.text["drafts.toastMoved"], steps: 1, bridges: 0, sources.text), "the seam's «Оставить» went")
        guard case .draftStep(let undone)? = sources.toast?.undo else { return XCTFail("its toast undoes the move") }
        XCTAssertEqual(undone, draft)
        let after = try await shown(draft, sources)
        XCTAssertEqual(after.places.map(\.id), [before.places[1].id, before.places[0].id])
        XCTAssertEqual(after.places.map(\.lastBar), [4, 8], "the draft's own bars, through the places in their new order")

        await sources.undo()
        let back = try await shown(draft, sources)
        XCTAssertEqual(back.places.map(\.id), before.places.map(\.id))
        let seams = await PieceCore(piece: sources.piece).draftSeams(draft).value
        XCTAssertEqual(seams?.seams.map(\.left), [true], "the seam kept as it was again")
    }

    /// A bridge place is not moved: music-core says why, in its words; nor is its length changed. A place moved past it
    /// takes it away, made for a seam that is no longer there, and the toast says so; Undo puts it back.
    func testABridgeIsNotMovedAndGoesWhenItsNeighboursChange() async throws {
        let sources = try await piece()
        let draft = try await newDraft(sources, [1...4, 5...8])
        let two = try await shown(draft, sources)
        let second = try XCTUnwrap(two.places.last)
        let transposed = await sources.change("drafts.transpose", ["draft": draft, "place": second.id, "semitones": 2, "now": "2026-11-26T10:10:00Z"])
        XCTAssertNotNil(transposed, sources.status ?? "")
        let seams = await PieceCore(piece: sources.piece).draftSeams(draft).value
        let bridge = try XCTUnwrap(seams?.seams.first?.bridge, "the keys differ: «Связка»")
        let put = await sources.putBridge(bridge, of: draft)
        XCTAssertTrue(put, sources.status ?? "")
        let three = try await shown(draft, sources)
        XCTAssertEqual(three.places.map(\.bridge), [false, true, false])
        guard three.places.count == 3 else { return XCTFail("three places") }

        let refused = await sources.movePlace(three.places[1], to: 0, of: draft)
        XCTAssertFalse(refused)
        XCTAssertEqual(sources.refusal(refused, told: true, otherwise: "refused"), "A bridge can't be moved: it is built for its seam.", "music-core's words")
        XCTAssertEqual(sources.refusal(refused, told: false, otherwise: "refused"), "refused", "the other cards keep their own words")
        let unmoved = try await shown(draft, sources)
        XCTAssertEqual(unmoved.places.map(\.id), three.places.map(\.id))
        let length = await sources.length(of: three.places[1].id, in: draft)
        XCTAssertNil(DraftLengthChoice(length), "a bridge's length does not change")
        XCTAssertFalse(length?.reason?.isEmpty ?? true, "music-core says why")

        let moved = await sources.movePlace(three.places[0], to: 2, of: draft)
        XCTAssertTrue(moved, sources.status ?? "")
        let toast = try XCTUnwrap(sources.toast?.text)
        XCTAssertTrue(toast.hasPrefix(sources.text["drafts.toastMoved"]), toast)
        XCTAssertTrue(toast.contains(sources.text.count("drafts.goneBridges", 1)), toast)
        let after = try await shown(draft, sources)
        XCTAssertEqual(after.places.map(\.id), [three.places[2].id, three.places[0].id], "the bridge went with its seam")

        await sources.undo()
        let back = try await shown(draft, sources)
        XCTAssertEqual(back.places.map(\.id), three.places.map(\.id))
    }

    /// A place's length: music-core's bounds, the base cut again over two more bars of its Notes, told in the toast, the
    /// draft's bars after it moved on; Undo puts the length back.
    func testAPlacesLengthChangesAndUndoes() async throws {
        let sources = try await piece()
        let draft = try await newDraft(sources, [1...4, 5...8])
        let before = try await shown(draft, sources)
        let place = try XCTUnwrap(before.places.first)
        let read = await sources.length(of: place.id, in: draft)
        var choice = try XCTUnwrap(DraftLengthChoice(read), "a place of the piece's own notes changes its length")
        XCTAssertEqual(choice.now, DraftLengthAnswer.Bars(first: 1, last: 4))
        choice.step(last: 2)
        let bars = try XCTUnwrap(choice.asked)

        let resized = await sources.resize(place.id, to: bars, of: draft)
        XCTAssertTrue(resized, sources.status ?? "")
        XCTAssertEqual(sources.toast?.text, sources.text.format("drafts.toastResized", "1", "6"), "nothing else went")
        let after = try await shown(draft, sources)
        XCTAssertEqual(after.places.map { $0.lastBar - $0.firstBar }, [5, 3], "six bars, and the next place after them")
        let now = await sources.length(of: place.id, in: draft)
        XCTAssertEqual(now?.bars, DraftLengthAnswer.Bars(first: 1, last: 6))

        await sources.undo()
        let back = try await shown(draft, sources)
        XCTAssertEqual(back.places.map(\.lastBar), before.places.map(\.lastBar))
    }

    /// «Перенести приём»: the other place listed with the bars the приём fits, a variant music-core builds put in there and
    /// told; a second carry there refused in music-core's words; Undo takes the variant out.
    func testATechniqueIsCarriedOnceAndUndoes() async throws {
        let sources = try await piece()
        let draft = try await newDraft(sources, [1...4])
        let made = try await shown(draft, sources)
        let place = try XCTUnwrap(made.places.first)
        let variant = await sources.change("drafts.add", ["bars": ["first": 5, "last": 8], "draft": draft, "drop": "variant", "place": place.id, "now": "2026-11-26T10:10:00Z"])
        XCTAssertNotNil(variant, sources.status ?? "")
        let other = await sources.change("drafts.add", ["bars": ["first": 1, "last": 4], "draft": draft, "drop": "place", "now": "2026-11-26T10:11:00Z"])
        XCTAssertNotNil(other, sources.status ?? "")
        let view = try await shown(draft, sources)
        let model = DraftTechniquesModel()
        await model.load(view, on: sources.piece)
        let spot = try XCTUnwrap(model.spots.first { $0.place == place.id }, "bar 8 against bar 4: a приём")

        let targets = await sources.carryPlaces(spot)
        let target = try XCTUnwrap(targets.value?.places.first { !$0.bars.isEmpty }, targets.message ?? "it fits the other place")
        XCTAssertEqual(target.place, view.places.last?.id)
        let carried = await sources.carry(spot, to: target)
        XCTAssertTrue(carried, sources.status ?? "")
        XCTAssertEqual(sources.toast?.text, sources.text.format("drafts.toastCarried", String(spot.technique.number), target.label))
        let after = try await shown(draft, sources)
        XCTAssertEqual(after.places.last?.variants.count, 2, "a variant music-core built for that place")

        let twice = await sources.carry(spot, to: target)
        XCTAssertFalse(twice, "carried there already")
        let words = try XCTUnwrap(sources.refusal(twice, told: true, otherwise: "refused"))
        XCTAssertNotEqual(words, "refused", "music-core says why")

        await sources.undo()
        let back = try await shown(draft, sources)
        XCTAssertEqual(back.places.last?.variants.count, 1)
    }

    /// F2 on a piece with no recording heard as notes: nothing is searched and the entry says so; a stretch of a recording
    /// that is not there is refused in music-core's words, and the draft stays as it was.
    func testFindOnRecordingWithoutARecordingHeard() async throws {
        let sources = try await piece()
        let draft = try await newDraft(sources, [1...4])
        let made = try await shown(draft, sources)
        let place = try XCTUnwrap(made.places.first)
        let offer = await sources.recordingOffer(for: place.id, of: draft)
        XCTAssertEqual(offer, .noRecording)
        let found = await PieceCore(piece: sources.piece).draftRecording(draft: draft, place: place.id).value
        XCTAssertEqual(found?.found, [], "music-core finds nothing either")

        let stray = #"{"draft": "d_1", "place": "pl_1", "found": [{"notes": "n_9", "label": "Recording 1–4", "percent": 80, "variant": null}]}"#
        let gone = try XCTUnwrap(FacadeAnswer.decode(DraftRecordingAnswer.self, stray, of: "drafts.findOnRecording")?.found.first)
        let added = await sources.addFromRecording(gone, to: place.id, of: draft)
        XCTAssertFalse(added)
        XCTAssertEqual(sources.refusal(added, told: true, otherwise: "refused"), "The recording has no stretch of this place's music.")
        let after = try await shown(draft, sources)
        XCTAssertEqual(after.places.first?.variants.count, 1)
    }

    // MARK: - Helpers

    /// The kit's piece open in music-core in English, with its sources read.
    private func piece() async throws -> SourcesController {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let opened = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        numbers.append(opened.id)
        let sources = SourcesController()
        await sources.load(piece: opened.id, file: nil)
        return sources
    }

    /// A new draft whose places are `places`' bars of the piece, in order; its id.
    private func newDraft(_ sources: SourcesController, _ places: [ClosedRange<Int>]) async throws -> String {
        var id: String?
        for (index, bars) in places.enumerated() {
            var fields: [String: Any] = ["bars": ["first": bars.lowerBound, "last": bars.upperBound], "now": "2026-11-26T10:0\(index):00Z"]
            if let id {
                fields["draft"] = id
                fields["drop"] = "place"
            }
            let added = await sources.change("drafts.add", fields)
            id = try XCTUnwrap(added?["draft"] as? String, sources.status ?? "music-core takes the fragment")
        }
        return try XCTUnwrap(id)
    }

    /// The draft as its map reads it now (`drafts.open`, not engraved), its number closed when the test ends.
    private func shown(_ draft: String, _ sources: SourcesController) async throws -> DraftViewAnswer.Draft {
        let answer = await PieceCore(piece: sources.piece).openDraft(draft)
        let view = try XCTUnwrap(answer.value, answer.message ?? "the draft opens")
        numbers.append(view.piece)
        return view.draft
    }
}
