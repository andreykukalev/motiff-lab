import XCTest

@testable import MotiffLab

/// The facade's answers as data (audit C3, step B5): tests/contracts/facade-answers/*.json, which music-core's
/// FacadeAnswersContractTests writes and keeps equal to what the facade answers on the example piece, decode into the app's
/// typed answers (PieceAnswers.swift) through `FacadeAnswer.decode`, as `PieceCore` decodes them, and from those into what
/// the features read. A field renamed or retyped in C# changes the file there, and fails the decode here on the field the
/// app needs. The files are read where they lie in the repository (`#filePath`).
final class FacadeAnswerFixturesTests: XCTestCase {
    private var reported: [String] = []

    override func setUp() {
        FacadeAnswer.broken = { [weak self] in self?.reported.append($0) }
    }

    override func tearDown() {
        FacadeAnswer.broken = FacadeAnswer.loud
    }

    func testTheSelectionsDecode() throws {
        let answers = try Self.answers("select")
        XCTAssertEqual(answers.count, 2)
        for (request, text) in answers {
            let selection = try XCTUnwrap(FacadeAnswer.decode(SelectionAnswer.self, text, of: "select")?.selection.map(ScoreSelection.init), "\(reported)")
            let bars = try XCTUnwrap(request?["bars"] as? [String: Int])
            XCTAssertEqual(selection.firstBar, bars["first"])
            XCTAssertEqual(selection.lastBar, bars["last"])
            XCTAssertLessThan(selection.onset, selection.end)
            XCTAssertNotNil(selection.firstBeat)
            XCTAssertNotNil(selection.lastBeat)
            XCTAssertFalse(selection.noteIds.isEmpty)
            XCTAssertEqual(selection.hand, (request?["filter"] as? [String: String])?["hand"], "the filter names the hand only when narrowed")
        }
        XCTAssertEqual(reported, [])
    }

    func testThePlaybackScoreDecodes() throws {
        let (_, text) = try XCTUnwrap(Self.answers("playback").first)
        let answer = try XCTUnwrap(FacadeAnswer.decode(PlaybackAnswer.self, text, of: "playback"), "\(reported)")
        let kept = try XCTUnwrap(facadeObject(text))
        let score = PlaybackScore(answer)
        XCTAssertEqual(score.notes.count, (kept["notes"] as? [Any])?.count)
        XCTAssertEqual(score.timeline.tempos.count, (kept["tempos"] as? [Any])?.count)
        XCTAssertEqual(score.timeline.bars.count, (kept["bars"] as? [Any])?.count)
        XCTAssertEqual(score.barNumbers.count, score.timeline.bars.count)
        XCTAssertEqual(score.barBeats.marks.count, (kept["beats"] as? [Any])?.count, "every beat tells its bar and beat")
        XCTAssertTrue(score.hands.canSeparate, "the example's notes carry both hands")
        let tempo = try XCTUnwrap(score.tempo, "the tempo shown is read")
        XCTAssertEqual(tempo.source, "notated")
        XCTAssertEqual(reported, [])
    }

    /// The roll's payload (step B7): typed, it reaches the page (`RollAnswer.argument`, RollPage.load) with every field and
    /// value music-core wrote, as the answer was passed on before it was typed.
    func testTheRollsPayloadDecodesAndReachesThePageAsItWas() throws {
        let (_, text) = try XCTUnwrap(Self.answers("roll").first)
        let roll = try XCTUnwrap(FacadeAnswer.decode(RollAnswer.self, text, of: "roll"), "\(reported)")
        XCTAssertEqual(reported, [])
        XCTAssertEqual(roll.notes.count, roll.ids.count * 7, "seven numbers a note")
        XCTAssertEqual(roll.bars.count % 4, 0, "four numbers a bar")
        XCTAssertEqual(roll.names.notes.count, roll.ids.count)
        XCTAssertEqual(roll.names.short.count, roll.ids.count)
        XCTAssertEqual(roll.names.rows.count, roll.names.keyTicks.count, "a row of names for each key signature")
        XCTAssertTrue((roll.names.rows + roll.names.shortRows).allSatisfy { $0.count == 128 }, "one name for each MIDI pitch")
        XCTAssertEqual(roll.names.octaves.count, 11)
        XCTAssertEqual(facadeRequest(roll.argument), facadeRequest(try XCTUnwrap(facadeObject(text))), "what the page gets is the answer")
    }

    func testTheSourceLayersDecode() throws {
        let answers = try Self.answers("sources.layers")
        XCTAssertEqual(answers.count, 9, "each call's answer but the version's, which only builds the state")
        let read = try answers.map { try XCTUnwrap(SourceLayers(answer: $0.text), "\(reported)") }
        XCTAssertEqual(reported, [])
        XCTAssertEqual(read[0].views.map(\.id), ["n_1"], "the example alone: its file and its working notes")

        let layers = read[7]
        XCTAssertEqual(answers[7].request?["here"] as? [String], ["src_rec"])
        XCTAssertEqual(layers.working, "n_1")
        XCTAssertEqual(layers.hiddenCount, 1)
        XCTAssertEqual(layers.liveCount, 3, "the removed PDF is listed, never counted")
        let removed = try XCTUnwrap(layers.original("src_pdf_2"))
        XCTAssertTrue(removed.isRemoved)
        XCTAssertNotNil(removed.removedAt, "a tombstone keeps its time")
        XCTAssertNil(try XCTUnwrap(layers.original("src_pdf")).removedAt)
        XCTAssertEqual(layers.original("src_pdf")?.pages, 1)
        XCTAssertEqual(layers.original("src_pdf")?.notes, ["n_2"])
        XCTAssertEqual(layers.original("src_rec")?.durationMs, 161_000)
        XCTAssertNil(layers.original("src_rec")?.pages)
        XCTAssertTrue(layers.original("src_rec")?.isHere == true, "its sound is here")
        XCTAssertEqual(read[8].original("src_rec")?.state, "nowhere", "without its sound")

        let readAgain = try XCTUnwrap(layers.notes("n_2"))
        XCTAssertEqual(readAgain.role, "other")
        XCTAssertEqual(readAgain.engine, "homr 0.8.0")
        XCTAssertNotNil(readAgain.at)
        XCTAssertEqual(readAgain.original, "src_pdf")
        XCTAssertTrue(readAgain.hidden)
        let version = try XCTUnwrap(layers.notes("perf_1"))
        XCTAssertEqual(version.role, "version")
        XCTAssertNil(version.original)
        XCTAssertNil(version.state)
        XCTAssertNil(version.at)
        XCTAssertEqual(layers.title(of: version, UIText.current), "Official", "Notes of no original take their name")
        XCTAssertEqual(layers.listedNotes.first?.id, "n_1", "the working notes first")
        // Andrei's v0.8 finding 19: each original with the Notes made from it, the working notes' group first.
        XCTAssertEqual(layers.groups.map(\.id), ["src_file", "src_pdf", "src_rec", "src_pdf_2"])
        XCTAssertEqual(layers.groups.map { $0.notes.map(\.id) }, [["n_1"], ["n_2"], [], []], "the PDF and the notes read from it together")
        XCTAssertEqual(layers.looseNotes.map(\.id), ["perf_1"], "Notes of no original after the groups")
        let english = UIText(language: "en")
        XCTAssertEqual(layers.title(of: readAgain, english), "Nocturne.pdf", "in a list, Notes take their original's name")
        XCTAssertEqual(layers.title(of: readAgain, english, underOriginal: true), "Notes · Recognised from a PDF", "under it, what tells them from it")
        XCTAssertEqual(layers.title(of: version, english, underOriginal: true), "Official", "a name of their own stays")

        XCTAssertNil(try XCTUnwrap(layers.views.first { $0.id == "perf_1" }).group, "Notes of no original are in no group")
        XCTAssertEqual(layers.views.first { $0.id == "n_2" }?.group, "src_pdf")
        XCTAssertEqual(layers.views.first { $0.id == "src_rec" }?.kind, "performance")
        XCTAssertFalse(layers.shownViews.contains { $0.id == "n_2" }, "hidden from the switch")
        XCTAssertFalse(read[8].views.contains { $0.kind == "performance" }, "no performance view without its sound")
    }

    func testTheCorrectionsDecode() throws {
        let answers = try Self.answers("corrections")
        XCTAssertEqual(answers.count, 18, "each call's answer but the version's two, which only build the state")
        let read = try answers.map { try XCTUnwrap(FacadeAnswer.decode(CorrectionsAnswer.self, $0.text, of: "corrections"), "\(reported)") }
        XCTAssertEqual(reported, [])
        let bars = read.map(NoteCorrections.init)
        XCTAssertTrue(read.allSatisfy { EditsLayerState($0.edits) == EditsLayerState(count: $0.edits.count) }, "opened strictly: no layer, on")

        let none = bars[0]
        XCTAssertEqual(none.bar, 2)
        XCTAssertEqual(none.count, 0)
        XCTAssertEqual(none.notes.count, 7)
        XCTAssertTrue(none.removed.isEmpty)
        XCTAssertNil(none.notes[0].place.offset, "on the beat")
        XCTAssertEqual(none.notes[2].place.offset, "1 sixteenth")
        XCTAssertEqual(none.notes[2].place.text(UIText(language: "en")), UIText(language: "en").format("notes.atOffset", "2", "1", "1 sixteenth"))
        XCTAssertTrue(none.notes[1].canShorter)
        XCTAssertFalse(read[0].canUndo)

        XCTAssertEqual(bars[1].notes.first { $0.id == "n_0010" }?.edited, true)
        XCTAssertEqual(bars[1].count, 1)
        XCTAssertEqual(bars[2].removed.map(\.id), ["n_0013"])
        XCTAssertNotNil(bars[2].removed.first?.place.offset)
        XCTAssertEqual(bars[3].count, 1, "the removal undone")
        XCTAssertEqual(bars[4].count, 0, "put back as imported")

        // The note editor's draft: begun, a note added (the only answer with "added") and set, Cancel, then Save.
        XCTAssertEqual(answers[5].request?["bar"] as? Int, 2)
        XCTAssertFalse(read[5].canUndo || read[5].changed)
        XCTAssertEqual(read.compactMap(\.added), ["a1"])
        XCTAssertEqual(read[6].added, "a1")
        XCTAssertTrue(read[6].canUndo && read[6].changed)
        XCTAssertEqual(read[11].edits.count, 0, "Cancel leaves no edit")
        XCTAssertFalse(read[11].changed)
        XCTAssertEqual(read[14].edits.count, 1, "Save keeps the note taken out")

        // The assistant's draft accepted, then undone whole.
        XCTAssertEqual(bars[15].count, 2)
        XCTAssertEqual(bars[16].count, 1)

        // A bar taken from a version: its notes taken are not corrected here.
        let taken = try XCTUnwrap(bars.last).notes.filter(\.taken)
        XCTAssertEqual(taken.count, 3)
        XCTAssertTrue(taken.allSatisfy { !$0.canEarlier && !$0.canShorter && $0.id.hasPrefix("perf-") })
    }

    func testTheCorrectionsLayerDecodes() throws {
        let answers = try Self.answers("corrections.layer")
        XCTAssertEqual(answers.count, 6)
        let read = try answers.map { try XCTUnwrap(FacadeAnswer.decode(CorrectionsAnswer.self, $0.text, of: "corrections"), "\(reported)") }
        XCTAssertEqual(reported, [])
        let layers = read.map { EditsLayerState($0.edits) }

        let laid = layers[0]
        XCTAssertTrue(laid.on)
        XCTAssertEqual(laid.count, 6)
        XCTAssertEqual(laid.moved, [EditsLayerState.Moved(noteId: "n_9998", to: "n_0014", bar: 2)])
        XCTAssertEqual(laid.unfit.map(\.reason), ["missing", "changed", "idTaken"])
        XCTAssertEqual(laid.unfitNotes.map(\.id), ["n_9999", "n_0012", "n_0015"])
        XCTAssertEqual(NoteCorrections(read[0]).notes.filter(\.edited).map(\.id), ["n_0010", "n_0014"])
        XCTAssertEqual(NoteCorrections(read[0]).removed.map(\.id), ["n_0013"])

        XCTAssertEqual(layers[1].unfitNotes.map(\.id), ["n_0012", "n_0015"], "the correction dropped leaves the log")
        XCTAssertEqual(layers[1].count, 5)
        XCTAssertFalse(layers[2].on)
        XCTAssertEqual(layers[2].count, 5, "the corrections are kept while off")
        XCTAssertTrue(NoteCorrections(read[3]).notes.allSatisfy { !$0.edited }, "off: the notes as recognised")
        XCTAssertTrue(layers[4].on)
        XCTAssertTrue(layers[5].moved.isEmpty, "the correction laid on a near note put back")
    }

    func testTheSourceLayersOfAPdfDecode() throws {
        let (_, text) = try XCTUnwrap(Self.answers("sources.layers.pdf", of: "Nocturne.pdf").first)
        let layers = try XCTUnwrap(SourceLayers(answer: text), "\(reported)")
        XCTAssertEqual(reported, [])
        XCTAssertNil(layers.working, "a piece that is only its PDF has no working notes")
        XCTAssertEqual(layers.original("src_pdf")?.pageMarks, 1, "its bookmark")
    }

    func testThePhraseLevelsDecode() throws {
        let answers = try Self.answers("phrases.level")
        XCTAssertEqual(answers.count, 7)
        let levels = try [0, 2, 3].map { try XCTUnwrap(FacadeAnswer.decode(PhraseLevelAnswer.self, answers[$0].text, of: "phrases.level"), "\(reported)") }
        let phrases = try [4, 5, 6].map { try XCTUnwrap(PhraseLevelsShown(answer: answers[$0].text), "\(reported)") }
        XCTAssertNotNil(ScoreSelection(answer: answers[1].text))
        XCTAssertEqual(reported, [])

        let sentences = PhraseLevelsShown(levels[0].phrases)
        XCTAssertEqual(sentences.level, "sentence")
        XCTAssertEqual(sentences.counts, ["short": 39, "phrase": 21, "sentence": 4])
        XCTAssertEqual(sentences.units.count, 4)
        let first = try XCTUnwrap(sentences.unit(1))
        XCTAssertEqual(first.provenance, "inferred")
        XCTAssertLessThan(first.selectableOnset, first.selectableEnd)
        XCTAssertNil(levels[0].selection.selection, "nothing selected: null")

        let moved = try XCTUnwrap(levels[1].selection.selection.map(ScoreSelection.init), "the selection moved to the short units there")
        XCTAssertEqual(PhraseLevelsShown(levels[1].phrases).units.count, 39)
        XCTAssertEqual([moved.firstBar, moved.lastBar], [4, 5])
        XCTAssertEqual(levels[2].selection.selection.map { [$0.firstBar, $0.lastBar] }, [4, 8])

        XCTAssertEqual(phrases[0].edited, [])
        XCTAssertEqual(phrases[1].edited, ["phrase"], "a phrase joined with the next is the player's own")
        XCTAssertEqual(phrases[1].counts["phrase"], 20)
        XCTAssertEqual(phrases[2].edited, [], "reset")
    }

    /// The drafts' calls (v1.1, ADR 0034): the «В черновик» sheet and what it offers first, the Sources group and the draft's
    /// view in the switch, and a draft opened with its places, read only from its "draft" block.
    func testTheDraftsDecode() throws {
        let answers = try Self.answers("drafts")
        XCTAssertEqual(answers.count, 7, "each call's answer")
        let empty = try XCTUnwrap(FacadeAnswer.decode(DraftSheetAnswer.self, answers[0].text, of: "drafts.sheet"), "\(reported)")
        XCTAssertEqual(empty.parts, ["all", "right", "left", "melody"])
        XCTAssertEqual(DraftChoice.first(empty, part: empty.part).into, .new, "no draft yet")
        let second = try XCTUnwrap(FacadeAnswer.decode(DraftSheetAnswer.self, answers[2].text, of: "drafts.sheet"), "\(reported)")
        let choice = DraftChoice.first(second, part: second.part)
        XCTAssertEqual(choice.into, .draft("d_0001"))
        XCTAssertEqual(choice.way, DraftChoice.Way(drop: .variant, place: "pl_0001", label: "① Notes 1–4", percent: 100))

        let layers = try [1, 3, 4, 6].map { try XCTUnwrap(SourceLayers(answer: answers[$0].text), "\(reported)") }
        XCTAssertEqual(layers[1].drafts.first?.places, 2)
        XCTAssertEqual(layers[2].drafts.first?.shelf, 1)
        XCTAssertEqual(layers[3].drafts.first?.name, "Coda", "renamed")
        let tab = try XCTUnwrap(layers[3].views.last)
        XCTAssertEqual(tab.kind, "draft")
        XCTAssertNil(tab.group)
        XCTAssertEqual(SourceLayers.word(tab, UIText.current), "Coda")
        XCTAssertEqual(layers[0].draft("d_0001")?.caption, "from the file's notes")

        let view = try XCTUnwrap(FacadeAnswer.decode(DraftViewAnswer.self, answers[5].text, of: "drafts.open"), "\(reported)")
        XCTAssertEqual(view.draft.places.map(\.label), ["① Notes 1–4", "② Notes 5–8"])
        XCTAssertEqual(view.draft.places.map(\.firstBar), [1, 5])
        XCTAssertEqual(view.draft.places[1].onset, view.draft.places[0].end, "the places follow one another")
        XCTAssertEqual(reported, [])
    }

    /// A version with bars taken into the notes, as a file saved before v1.1 holds it (the bar-by-bar merge left the
    /// iPad, ADR 0034 section 8): the answers of a bar taken and restored, read as any version is.
    func testAVersionWithTakenBarsDecodes() throws {
        let answers = try Self.answers("performance.taken")
        XCTAssertEqual(answers.count, 2, "each call's answer but the version added")
        let applied = try [(0, "performance.takeAll"), (1, "performance.restore")].map {
            try XCTUnwrap(FacadeAnswer.decode(PerformanceAnswer.self, answers[$0.0].text, of: $0.1), "\(reported)")
        }
        XCTAssertEqual(reported, [])

        var shelf = VersionShelf()
        XCTAssertTrue(shelf.read(performance: applied[0]))
        XCTAssertEqual(shelf.shownId, "perf_1")
        XCTAssertEqual(shelf.taken.map(\.bar), [5], "the bar taken, drawn as a taken-bar line")
        XCTAssertEqual(shelf.matched, shelf.printed)
        let bar5 = try XCTUnwrap(shelf.bars.first { $0.bar == 5 })
        XCTAssertTrue(bar5.taken && bar5.differs)
        XCTAssertEqual(bar5.played, 3)
        XCTAssertNotNil(bar5.span)
        XCTAssertTrue(shelf.read(performance: applied[1]))
        XCTAssertEqual(shelf.taken.count, 0, "restored, no bar is taken")
        XCTAssertEqual(shelf.entries.count, 1)
    }

    /// Each answer a file keeps, with its request, as the facade's text; a call that only builds the state keeps none.
    /// `fixture`: the file the piece is made from, as the file names it.
    private static func answers(_ name: String, of fixture: String = "FurElise.musicxml") throws -> [(request: [String: Any]?, text: String)] {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/facade-answers/\(name).json")
        let file = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any], url.path)
        XCTAssertEqual(file["fixture"] as? String, fixture)
        let entries = try XCTUnwrap(file["answers"] as? [[String: Any]])
        return try entries.compactMap { entry -> (request: [String: Any]?, text: String)? in
            guard let answer = entry["answer"] as? [String: Any] else { return nil }
            let text = String(decoding: try JSONSerialization.data(withJSONObject: answer), as: UTF8.self)
            return (entry["request"] as? [String: Any], text)
        }
    }
}
