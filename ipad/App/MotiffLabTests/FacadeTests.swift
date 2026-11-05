import XCTest

@testable import MotiffLab

/// The facade's contract (decision record 0020) through the C functions in the simulator or on the iPad: the calls
/// the xUnit tests make in C# (tests/MotiffLab.MusicCore.Tests/Facade) and ipad/Core/check.py makes on Linux.
final class FacadeTests: XCTestCase {
    private var piece: Int32 = 0

    override func setUpWithError() throws {
        let example = try XCTUnwrap(OpenPiece.example, "the example piece is in the app")
        let opened = try object(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "ru", "id": "p_test"}"#))
        XCTAssertNil(opened["error"], "\(opened)")
        piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0)
    }

    override func tearDown() {
        MusicCore.close(piece: piece)
    }

    func testVersionNamesTheContract() throws {
        let version = try object(MusicCore.version())
        XCTAssertGreaterThanOrEqual(version["contract"] as? Int ?? 0, 3)
        XCTAssertGreaterThanOrEqual(version["projectFile"] as? Int ?? 0, 12)
        XCTAssertTrue((version["runtime"] as? String ?? "").contains("NativeAOT"), "\(version)")
    }

    func testOpenGivesTheSummaryInTheLanguageAskedFor() throws {
        let example = try XCTUnwrap(OpenPiece.example)
        let opened = try object(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "ru"}"#))
        defer { MusicCore.close(piece: Int32(opened["piece"] as? Int ?? 0)) }
        let summary = try XCTUnwrap(opened["summary"] as? [String: Any])
        XCTAssertEqual(summary["noteCount"] as? Int, 636)
        XCTAssertEqual(summary["format"] as? String, "musicXml")
        XCTAssertEqual(opened["language"] as? String, "ru")
    }

    func testPhrasesComeInThreeLevels() throws {
        let phrases = try call("phrases")
        let levels = try XCTUnwrap(phrases["levels"] as? [[String: Any]])
        XCTAssertEqual(levels.map { $0["id"] as? String }, ["short", "phrase", "sentence"])
        XCTAssertTrue(levels.allSatisfy { $0["available"] as? Bool == true })
    }

    func testTheBracketsAndTheSelectionAreReadFromMusicCore() throws {
        let shown = try XCTUnwrap(PhraseLevelsShown(answer: MusicCore.call(piece: piece, "phrases")))
        XCTAssertEqual(shown.level, "phrase")
        XCTAssertEqual(shown.counts["phrase"], shown.units.count)
        let first = try XCTUnwrap(shown.units.first)
        XCTAssertEqual(first.kind, "start")
        XCTAssertEqual(first.name, "фраза", "a unit is named in the piece's language")
        XCTAssertLessThan(first.onset, first.end)

        let request = "{\"span\": {\"onset\": \(Int(first.selectableOnset)), \"end\": \(Int(first.selectableEnd))}}"
        let selected = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", request)))
        XCTAssertEqual(selected.onset, first.selectableOnset)
        XCTAssertEqual(selected.firstBar, first.firstBar)

        let sentences = try XCTUnwrap(PhraseLevelsShown(answer: MusicCore.call(piece: piece, "phrases.level", #"{"level": "sentence"}"#)))
        XCTAssertEqual(sentences.level, "sentence")
        XCTAssertLessThanOrEqual(sentences.units.count, shown.units.count)
    }

    func testSelectAndUnderstandInRussian() throws {
        let selected = try call("select", #"{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}"#)
        let selection = try XCTUnwrap(selected["selection"] as? [String: Any])
        XCTAssertEqual(selection["firstBar"] as? Int, 1)
        XCTAssertFalse((selection["noteIds"] as? [Any] ?? []).isEmpty)

        let understood = try call("understand")
        let motif = try XCTUnwrap(understood["motif"] as? [String: Any])
        let facts = try XCTUnwrap(motif["facts"] as? [[String: Any]])
        XCTAssertFalse(facts.isEmpty)
        XCTAssertTrue(facts.contains { ($0["text"] as? String ?? "").unicodeScalars.contains { (0x0410...0x044F).contains($0.value) } }, "facts are in Russian")
    }

    func testInsightsListMusicCoresFactsByLens() throws {
        _ = try call("select", #"{"bars": {"first": 4, "last": 12}}"#)
        let understanding = try XCTUnwrap(Understanding(answer: MusicCore.call(piece: piece, "understand")))
        XCTAssertGreaterThan(understanding.facts.count, 10)
        XCTAssertTrue(understanding.facts.allSatisfy { Understanding.lenses.contains($0.lens) }, "every fact has a lens the stream groups")
        XCTAssertEqual(Set(understanding.facts.map(\.id)).count, understanding.facts.count, "each fact is listed once")
        let key = try XCTUnwrap(understanding.key, "music-core names the one to hear first")
        XCTAssertFalse(key.places.isEmpty)
        XCTAssertFalse(understanding.keyReason?.isEmpty ?? true)
        XCTAssertTrue(understanding.facts.contains { !$0.tries.isEmpty }, "some facts offer a way to try")

        // The stream's words are the web app's, in both languages.
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in Understanding.lenses.map({ "term.lens.\($0)" }) + ["term.certainty.detected", "term.certainty.inferred", "insights.evidence", "insights.all", "insights.hint", "understand.keyObservation"] {
                XCTAssertNotEqual(text[key], key, "\(language): \(key)")
            }
        }
    }

    func testAWayTriedSoundsInPlaceIsEngravedAsAnOssiaAndKept() throws {
        _ = try call("select", #"{"bars": {"first": 4, "last": 12}}"#)
        let understanding = try XCTUnwrap(Understanding(answer: MusicCore.call(piece: piece, "understand")))
        let fact = try XCTUnwrap(understanding.facts.first { $0.lens == "harmony" && !$0.tries.isEmpty })
        let way = try XCTUnwrap(fact.tries.first)

        let tried = try call("try", "{\"chip\": \"\(way.id)\"}")
        let result = try XCTUnwrap(tried["result"] as? [String: Any])
        XCTAssertFalse((result["notes"] as? [Any] ?? []).isEmpty)
        XCTAssertFalse((result["explanation"] as? String ?? "").isEmpty, "music-core says what changed")
        let heard = try XCTUnwrap(PlaybackScore.read(MusicCore.call(piece: piece, "playback", #"{"of": "inPlace"}"#)))
        XCTAssertFalse(heard.notes.isEmpty, "the piece with the variation in place")

        let card = try Engraver.engraveCard(piece: piece, firstBar: 4, lastBar: 12, options: #"{"scale": 42, "pageWidth": 2600, "adjustPageHeight": true, "breaks": "auto"}"#, ossia: true).get()
        let ossia = try XCTUnwrap(card.ossias.first, "the variation above the original")
        XCTAssertFalse(ossia.staves.isEmpty)
        XCTAssertFalse(ossia.prefix.isEmpty)

        let kept = try call("idea.keep", "{\"name\": \"\(way.label)\", \"factId\": \"\(fact.id)\", \"now\": \"2026-11-05T06:00:00Z\"}")
        let ideas = try XCTUnwrap(kept["ideas"] as? [[String: Any]])
        XCTAssertEqual(ideas.first?["name"] as? String, way.label)
        XCTAssertEqual(ideas.first?["factId"] as? String, fact.id)
        XCTAssertEqual(ideas.first?["hasVariation"] as? Bool, true)
        _ = try call("variation.clear")
        _ = try call("variation", expectError: true)
    }

    func testBookmarksAreMadeChangedAndFiledAsStructure() throws {
        let made = try XCTUnwrap(BookmarkShelf(answer: MusicCore.call(piece: piece, "bookmark.add", #"{"bars": {"first": 4, "last": 8}, "title": "Тема", "category": "teacher", "now": "2026-11-05T06:00:00Z"}"#)))
        XCTAssertEqual(made.categories.map(\.id), ["practise", "performance", "structure", "teacher"])
        let bookmark = try XCTUnwrap(made.bookmarks.first)
        XCTAssertEqual(bookmark.title, "Тема")
        XCTAssertEqual([bookmark.firstBar, bookmark.lastBar], [4, 8])
        XCTAssertEqual(made.category(of: bookmark)?.id, "teacher")

        let changed = try XCTUnwrap(BookmarkShelf(answer: MusicCore.call(piece: piece, "bookmark.change", "{\"id\": \"\(bookmark.id)\", \"category\": \"practise\"}")))
        XCTAssertEqual(changed.bookmarks.first?.category, "practise")
        XCTAssertEqual(changed.bookmarks.first?.title, "Тема")

        // The form music-core found, filed as Structure bookmarks once, named in the piece's language.
        let filed = try XCTUnwrap(BookmarkShelf(answer: MusicCore.call(piece: piece, "bookmark.structure")))
        XCTAssertGreaterThan(filed.bookmarks.count, 1)
        XCTAssertTrue(filed.bookmarks.dropFirst().allSatisfy { $0.category == BookmarkShelf.structure })
        XCTAssertTrue(filed.bookmarks.last?.title.hasPrefix("Предложение") ?? false)
        XCTAssertEqual(try XCTUnwrap(BookmarkShelf(answer: MusicCore.call(piece: piece, "bookmark.structure"))).bookmarks.count, filed.bookmarks.count)
        XCTAssertLessThanOrEqual(filed.inScoreOrder.first?.onset ?? .infinity, bookmark.onset)

        let removed = try XCTUnwrap(BookmarkShelf(answer: MusicCore.call(piece: piece, "bookmark.remove", "{\"id\": \"\(bookmark.id)\"}")))
        XCTAssertEqual(removed.bookmarks.count, filed.bookmarks.count - 1)
    }

    func testMarginNotesHighlightsAndLayersAreKeptWithThePiece() throws {
        // A note on bars 4–5, written, changed and numbered in score order beside one on bar 1.
        let first = try call("note.add", #"{"span": {"onset": 0, "end": 480}, "text": "Тихо", "now": "2026-11-05T06:00:00Z"}"#)
        XCTAssertEqual(MarksShelf(object: first).notes.count, 1)
        let selected = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", #"{"bars": {"first": 4, "last": 5}}"#)))
        let onset = Int(selected.onset)
        let end = Int(selected.end)
        let added = MarksShelf(object: try call("note.add", "{\"span\": {\"onset\": \(onset), \"end\": \(end)}, \"text\": \"Легче большим\", \"now\": \"2026-11-05T06:01:00Z\"}"))
        XCTAssertEqual(added.notes.map(\.text), ["Тихо", "Легче большим"])
        let note = try XCTUnwrap(added.notes.last)
        XCTAssertEqual([note.firstBar, note.lastBar], [4, 5])
        XCTAssertEqual(added.number(of: note), 2)
        let changed = MarksShelf(object: try call("note.change", "{\"id\": \"\(note.id)\", \"text\": \"Легче\"}"))
        XCTAssertEqual(changed.notes.last?.text, "Легче")

        // A highlight in carmine over the same bars, then gone.
        let lit = MarksShelf(object: try call("highlight.add", "{\"span\": {\"onset\": \(onset), \"end\": \(end)}, \"colour\": \"carmine\"}"))
        let highlight = try XCTUnwrap(lit.highlights.first)
        XCTAssertEqual(highlight.colour, "carmine")
        XCTAssertEqual([highlight.firstBar, highlight.lastBar], [4, 5])
        XCTAssertTrue(MarksShelf(object: try call("highlight.remove", "{\"id\": \"\(highlight.id)\"}")).highlights.isEmpty)
        XCTAssertEqual(MarksShelf(object: try call("note.remove", "{\"id\": \"\(note.id)\"}")).notes.count, 1)

        // A layer hidden is kept with the piece as its own setting.
        let catalogue = try call("settings")
        let ids = (catalogue["settings"] as? [[String: Any]] ?? []).compactMap { $0["id"] as? String }
        XCTAssertTrue(ScoreController.layers.allSatisfy { ids.contains("layers.\($0)") })
        let overrides = try call("settings.override", #"{"id": "layers.ink", "value": "off"}"#)
        XCTAssertEqual((overrides["overrides"] as? [String: Any])?["layers.ink"] as? String, "off")
    }

    func testVariationNotationPlaybackAndSave() throws {
        _ = try call("select", #"{"bars": {"first": 1, "last": 2}}"#)
        let proposed = try call("variation.propose", #"{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}"#)
        XCTAssertEqual((proposed["pending"] as? [String: Any])?["operation"] as? String, "V1")
        _ = try call("variation.accept")

        let notation = try call("notation", #"{"bars": "selection", "ossia": "variation"}"#)
        XCTAssertTrue((notation["mei"] as? String ?? "").contains("<mei"))
        let playback = try call("playback")
        XCTAssertNil(playback["error"])

        let saved = MusicCore.call(piece: piece, "project.save", #"{"now": "2026-11-04T12:00:00Z"}"#)
        let file = try object(saved)
        XCTAssertEqual(file["format"] as? String, "motiff-lab-project")
        XCTAssertGreaterThanOrEqual(file["version"] as? Int ?? 0, 12)

        // The saved file opens as a second piece with the work restored.
        let reopened = try object(MusicCore.open(fileName: "elise.motiff.json", data: Data(saved.utf8)))
        let second = Int32(reopened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: second) }
        XCTAssertEqual(reopened["hasVariation"] as? Bool, true)
    }

    func testALibraryCardReadsAProjectFileWithoutOpeningIt() throws {
        let file = MusicCore.call(piece: piece, "project.save")
        let body = try JSONSerialization.data(withJSONObject: ["file": file])
        let summary = try XCTUnwrap(PieceSummary(answer: MusicCore.call(piece: 0, "project.summary", String(decoding: body, as: UTF8.self))))
        XCTAssertTrue(summary.hasNotes)
        XCTAssertEqual(summary.origin, "imported")
        XCTAssertEqual(summary.checkBars, 0)
    }

    func testErrorsComeBackAsCodes() throws {
        XCTAssertEqual(try call("dance", expectError: true)["error"] as? String, "unknownCall")
        XCTAssertEqual(try object(MusicCore.call(piece: 99, "piece"))["error"] as? String, "noPiece")
        XCTAssertEqual(try call("select", "not json", expectError: true)["error"] as? String, "badRequest")
        XCTAssertFalse(MusicCore.close(piece: 99))
    }

    func testCallsRunOneAtATimeOffTheMainThread() async throws {
        let piece = piece
        let answers = await withTaskGroup(of: String.self) { group in
            for _ in 0..<4 {
                group.addTask { await MusicCore.run { MusicCore.call(piece: piece, "phrases") } }
            }
            return await group.reduce(into: [String]()) { $0.append($1) }
        }
        XCTAssertEqual(Set(answers).count, 1, "the same calls give the same answer")
    }

    private func call(_ name: String, _ request: String? = nil, expectError: Bool = false) throws -> [String: Any] {
        let answer = try object(MusicCore.call(piece: piece, name, request))
        if !expectError {
            XCTAssertNil(answer["error"], "\(name): \(answer)")
        }
        return answer
    }

    private func object(_ text: String) throws -> [String: Any] {
        try XCTUnwrap(facadeObject(text), text)
    }
}
