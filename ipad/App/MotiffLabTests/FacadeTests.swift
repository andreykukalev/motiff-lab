import XCTest

@testable import MotiffLab

/// The facade's contract (decision record 0020) through the C functions in the simulator or on the iPad: the calls
/// the xUnit tests make in C# (tests/MotiffLab.MusicCore.Tests/Facade) and ipad/Core/check.py makes on Linux.
final class FacadeTests: XCTestCase {
    private var piece: Int32 = 0

    override func setUpWithError() throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
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
        let example = try XCTUnwrap(KitPiece.example)
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

    func testThePhrasesAndTheSelectionAreReadFromMusicCore() throws {
        let shown = try XCTUnwrap(PhraseLevelsShown(answer: MusicCore.call(piece: piece, "phrases")))
        XCTAssertEqual(shown.level, "phrase")
        XCTAssertEqual(shown.counts["phrase"], shown.units.count)
        let first = try XCTUnwrap(shown.units.first)
        XCTAssertEqual(first.number, 1, "the first unit starts the piece")
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
        // Main (testing item 14): the harmonic motion first, then the texture, at most three.
        let main = understanding.mainFacts
        XCTAssertEqual(main.first?.id, "harmony.motion")
        XCTAssertEqual(main.dropFirst().first?.id, "character.texture")
        XCTAssertLessThanOrEqual(main.count, 3)
        XCTAssertTrue(main.allSatisfy { !$0.places.isEmpty })
        XCTAssertTrue(Understanding.lenses.allSatisfy { understanding.facts(of: $0).allSatisfy { !main.contains($0) } }, "a fact under Main is not repeated under Details")
        XCTAssertTrue(understanding.facts.contains { !$0.tries.isEmpty }, "some facts offer a way to try")

        // The stream's words are the web app's, in both languages.
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in Understanding.lenses.map({ "term.lens.\($0)" }) + ["term.certainty.detected", "term.certainty.inferred", "insights.evidence", "insights.all", "insights.main", "insights.details"] {
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

    func testPieceDnaReadsTheFormKeyHarmonyDensityAndTheIdeasFound() throws {
        // Piece DNA filled from the engine (testing item 22): the form lettered by what it shares, the lanes by bar, the ideas found.
        let map = try XCTUnwrap(DnaMap(answer: MusicCore.call(piece: piece, "dna")))
        XCTAssertGreaterThan(map.lastBar, map.firstBar)
        XCTAssertEqual(map.barStarts.count, map.lastBar - map.firstBar + 2)
        XCTAssertNotNil(map.bar(map.firstBar))
        XCTAssertEqual(map.form.first?.label, "A")
        XCTAssertGreaterThan(map.form.count, 1)
        // A section chip in the Contents selects what a selection holds: the section, or its first bars when it is long.
        for section in map.form {
            XCTAssertEqual(section.selectableOnset, section.onset)
            XCTAssertLessThanOrEqual(section.selectableEnd, section.end)
            _ = try call("select", #"{"span": {"onset": \#(Int(section.selectableOnset)), "end": \#(Int(section.selectableEnd))}}"#)
        }
        XCTAssertFalse(map.keys.isEmpty)
        XCTAssertFalse(map.harmony.isEmpty)
        XCTAssertEqual(map.density.count, map.lastBar - map.firstBar + 1)
        XCTAssertFalse(map.register.isEmpty)
        // The map explains itself (testing item 30): its summary, the chords' steps and functions, the closes in words, a circle per section.
        XCTAssertTrue(map.summary.hasPrefix("Форма A"), map.summary)
        XCTAssertTrue(map.harmony.contains { $0.function == "tonic" && !$0.degree.isEmpty })
        XCTAssertTrue(map.cadences.allSatisfy { !$0.explanation.isEmpty })
        XCTAssertEqual(map.circles.count, map.form.count)
        XCTAssertEqual(map.circles.first?.circle.zones.count, 12)
        let idea = try XCTUnwrap(map.ideas.first)
        XCTAssertTrue(idea.title.hasPrefix("Идея 1"), idea.title)
        XCTAssertTrue(idea.summary.hasPrefix("Звучит"), idea.summary)
        XCTAssertEqual(idea.marks.filter { $0.kind == "original" }.count, 1)
        XCTAssertGreaterThan(idea.marks.count, 1)

        // Add Structure bookmarks on the map: a bookmark per section, once.
        let filed = try XCTUnwrap(BookmarkShelf(answer: MusicCore.call(piece: piece, "bookmark.form")))
        XCTAssertEqual(filed.bookmarks.filter { $0.category == BookmarkShelf.structure }.count, map.form.count)
        XCTAssertTrue(filed.bookmarks.first?.title.hasPrefix("Раздел A") ?? false)
        XCTAssertEqual(try XCTUnwrap(BookmarkShelf(answer: MusicCore.call(piece: piece, "bookmark.form"))).bookmarks.count, filed.bookmarks.count)
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

    func testCheckListsTheBarsToReviewWithTheirReasonsAndResolvesThem() throws {
        // A project file from a scan, with flags on bar 3 and one already checked on bar 6.
        var file = try object(MusicCore.call(piece: piece, "project.save"))
        let flags: [[String: Any]] = [
            ["bar": 3, "source": "Scan", "reason": "BarTooLong", "resolved": false],
            ["bar": 3, "source": "Scan", "reason": "GraceNotes", "resolved": false],
            ["bar": 6, "source": "Scan", "reason": "BarTooShort", "resolved": true],
        ]
        let scan: [String: Any] = ["engine": "homr 0.7.0", "confirmedBars": [Int]()]
        file["check"] = flags
        file["scan"] = scan
        let data = try JSONSerialization.data(withJSONObject: file)
        let opened = try object(MusicCore.open(fileName: "elise.motiff.json", data: data, request: #"{"language": "ru"}"#))
        let scanned = Int32(opened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: scanned) }

        let check = try XCTUnwrap(CheckShelf(answer: MusicCore.call(piece: scanned, "check")))
        XCTAssertEqual(check.origin, "scanned")
        XCTAssertEqual(check.engine, "homr 0.7.0")
        XCTAssertEqual(check.openBars.map(\.bar), [3])
        XCTAssertEqual(check.resolvedBars.map(\.bar), [6])
        let bar = try XCTUnwrap(check.openBars.first)
        XCTAssertEqual(CheckShelf.reasonNames(bar.flags, UIText(language: "ru")), "Такт длиннее размера, форшлаги")
        let selected = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: scanned, "select", #"{"bars": {"first": 3, "last": 3}}"#)))
        XCTAssertEqual(bar.span, selected.onset..<selected.end)

        let resolved = try XCTUnwrap(CheckShelf(answer: MusicCore.call(piece: scanned, "check.resolve", #"{"bar": 3}"#)))
        XCTAssertEqual(resolved.open, 0)
        XCTAssertEqual(resolved.resolvedBars.map(\.bar), [3, 6])
        let again = try XCTUnwrap(CheckShelf(answer: MusicCore.call(piece: scanned, "check.resolve", #"{"bar": 6, "resolved": false}"#)))
        XCTAssertEqual(again.openBars.map(\.bar), [6])
    }

    func testAnotherVersionIsComparedBarByBarDismissedRenamedAndRemoved() throws {
        // The piece's own MusicXML as another version, as an official score would be: every note finds its pair.
        let example = try XCTUnwrap(KitPiece.example)
        let request = try XCTUnwrap(String(data: JSONSerialization.data(withJSONObject: [
            "name": "Official", "file": example.data.base64EncodedString(), "fileName": example.fileName,
        ]), encoding: .utf8))
        var shelf = VersionShelf()
        XCTAssertTrue(shelf.read(performance: MusicCore.call(piece: piece, "performance.add", request)))
        XCTAssertEqual(shelf.shown?.name, "Official")
        XCTAssertGreaterThan(shelf.printed, 0)
        XCTAssertEqual(shelf.matched, shelf.printed)
        XCTAssertFalse(shelf.bars.isEmpty)
        XCTAssertTrue(shelf.differing.isEmpty)
        let bar5 = try XCTUnwrap(shelf.bars.first { $0.bar == 5 })
        let selected = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", #"{"bars": {"first": 5, "last": 5}}"#)))
        XCTAssertEqual(bar5.span, selected.onset..<selected.end)

        let id = try XCTUnwrap(shelf.shownId)
        XCTAssertTrue(shelf.read(performance: MusicCore.call(piece: piece, "performance.dismiss", #"{"id": "\#(id)", "bar": 5}"#)))
        XCTAssertEqual(shelf.dismissed.map(\.bar), [5])
        XCTAssertTrue(shelf.read(performance: MusicCore.call(piece: piece, "performance.rename", #"{"id": "\#(id)", "name": "Urtext"}"#)))
        shelf.read(list: MusicCore.call(piece: piece, "performances"))
        XCTAssertEqual(shelf.entries.map(\.name), ["Urtext"])

        // A merge draft of it has nothing to decide, and its result engraves the whole piece (testing item 32); the version
        // above the phrase in focus, and the version to hear.
        let opened = try object(MusicCore.call(piece: piece, "performance.merge.open", #"{"id": "\#(id)", "start": "all"}"#))
        let merge = try XCTUnwrap(MergeInfo(object: opened))
        XCTAssertEqual(merge.total, 0)
        let result = try Engraver.engraveMerge(piece: piece, title: "Für Elise", view: "result").get()
        XCTAssertFalse(result.pages.isEmpty)
        XCTAssertTrue(result.version.isEmpty, "the piece's own score adds nothing")
        XCTAssertNil(MergeInfo(object: try object(MusicCore.call(piece: piece, "performance.merge.cancel"))))
        let card = try Engraver.engraveCard(piece: piece, firstBar: 4, lastBar: 8, options: #"{"scale": 42, "pageWidth": 2600, "adjustPageHeight": true, "breaks": "auto"}"#, version: id).get()
        XCTAssertEqual(card.ossias.count, 1)
        let heard = try XCTUnwrap(PlaybackScore.read(MusicCore.call(piece: piece, "playback", #"{"of": "version", "id": "\#(id)"}"#)))
        XCTAssertFalse(heard.notes.isEmpty)

        shelf.read(list: MusicCore.call(piece: piece, "performance.remove", #"{"id": "\#(id)"}"#))
        XCTAssertTrue(shelf.entries.isEmpty)
        XCTAssertNil(shelf.shown)
        XCTAssertTrue(shelf.bars.isEmpty)
        XCTAssertFalse(shelf.read(performance: MusicCore.call(piece: piece, "performance", #"{"id": "\#(id)"}"#)))
    }

    @MainActor
    func testDeviceSettingsApplyPresetsKeepValuesAndYieldToThePiece() async throws {
        let store = try XCTUnwrap(UserDefaults(suiteName: "motiff-settings-test"))
        store.removePersistentDomain(forName: "motiff-settings-test")
        let settings = DeviceSettings(store: store)
        await settings.load()
        XCTAssertEqual(settings.preset, "practising", "a new device starts from Practising")
        XCTAssertEqual(settings.catalogue["phrases.granularity"]?.choices, ["short", "phrases", "sentences"])

        await settings.update(["preset": "clean"])
        XCTAssertEqual(settings.preset, "clean")
        XCTAssertEqual(settings["layers.notes"], "off")
        await settings.update(["set": ["id": "layers.notes", "value": "on"]])
        XCTAssertEqual(settings.preset, "custom")

        // Kept on the device: a second store reads the same values.
        let again = DeviceSettings(store: store)
        await again.load()
        XCTAssertEqual(again["layers.notes"], "on")
        XCTAssertEqual(again.preset, "custom")
        XCTAssertNil((store.dictionary(forKey: DeviceSettings.storeKey) as? [String: String])?["appearance.language"], "the language is the iPad's")

        // A piece's own choice wins for the settings a piece keeps, and only for those.
        let resolved = again.resolved(overrides: ["layers.notes": "off", "appearance.theme": "night"])
        XCTAssertEqual(resolved["layers.notes"], "off")
        XCTAssertNotEqual(resolved["appearance.theme"], "night")
        store.removePersistentDomain(forName: "motiff-settings-test")
    }

    @MainActor
    func testAPdfOpensAsItIsWithItsBookmarkedPagesKeptInTheProject() async throws {
        // Two A4 pages drawn here, as a printed score would come.
        let format = UIGraphicsPDFRendererFormat()
        let data = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842), format: format).pdfData { context in
            for _ in 0..<2 {
                context.beginPage()
                UIBezierPath(rect: CGRect(x: 60, y: 120, width: 475, height: 2)).fill()
            }
        }
        let opened = try await OpenPiece.open(fileName: "Nocturne.pdf", data: data, language: "en").get()
        XCTAssertTrue(opened.isDocument, "a PDF is read as it is, with nothing engraved")
        XCTAssertEqual(opened.title, "Nocturne")
        XCTAssertTrue(opened.pages.isEmpty)
        defer { MusicCore.close(piece: opened.id) }
        XCTAssertEqual(try object(MusicCore.call(piece: opened.id, "phrases"))["error"] as? String, "noNotes")

        let pdf = PdfController()
        await pdf.load(piece: opened.id, text: UIText(language: "en"))
        XCTAssertNil(pdf.problem)
        XCTAssertEqual(pdf.pageCount, 2)
        XCTAssertEqual(pdf.document?.pageCount, 2)
        XCTAssertEqual(pdf.shareable?.lastPathComponent, "Nocturne.pdf")
        pdf.shown(first: 2, last: 2)
        await pdf.toggleBookmark()
        XCTAssertEqual(pdf.bookmarks.map(\.page), [2])
        XCTAssertEqual(pdf.currentBookmark?.page, 2)
        XCTAssertTrue(pdf.document?.page(at: 1)?.annotations.contains { $0 is BookmarkTab } ?? false, "a bookmarked page carries its tab")
        await pdf.rename(try XCTUnwrap(pdf.currentBookmark), "Coda")

        // A stroke on page 2, kept in the page's own spaces, drawn with the page, and erased by a path over it.
        let box = try XCTUnwrap(pdf.document?.page(at: 1)).bounds(for: .cropBox)
        let line = [(CGPoint(x: 100, y: 700), 0.5), (CGPoint(x: 150, y: 690), 0.5), (CGPoint(x: 200, y: 700), 0.5)]
        let request = try XCTUnwrap(DocumentStroke.request(id: "pk_test", tool: "pen", colour: "carmine", size: PdfController.penSize, page: 2, box: box, points: line))
        await pdf.change("document.ink.add", ["strokes": [request]])
        let kept = try XCTUnwrap(pdf.strokes.first)
        XCTAssertEqual(pdf.strokes.count, 1)
        XCTAssertEqual(kept.page, 2)
        XCTAssertEqual(kept.u, 100 / 595, accuracy: 1e-6)
        XCTAssertEqual(kept.path(in: box).last?.x ?? 0, 200, accuracy: 1e-6, "a stroke comes back where it was written")
        XCTAssertTrue(pdf.document?.page(at: 1)?.annotations.contains { $0 is InkMark } ?? false)
        XCTAssertEqual(pdf.erased(by: [CGPoint(x: 150, y: 691)], on: 2, box: box), ["pk_test"])
        XCTAssertEqual(pdf.erased(by: [CGPoint(x: 150, y: 691)], on: 1, box: box), [], "the eraser takes only the page it is on")
        let cut = pdf.erasure(by: [CGPoint(x: 150, y: 691)], on: 2, box: box)
        XCTAssertEqual(cut.added.count, 2, "the eraser cuts the stroke in two where it passed (#479 item 7)")
        XCTAssertEqual(cut.added.compactMap { $0["colour"] as? String }, ["carmine", "carmine"], "the pieces keep the stroke's ink")
        XCTAssertNil(request["margin"], "a stroke begun on the page is in no margin")

        // A stroke begun beside the page is in its margin (v0.4), kept so and drawn where it was written.
        let beside = [(CGPoint(x: -12, y: 400), 0.5), (CGPoint(x: 20, y: 400), 0.5)]
        let marginal = try XCTUnwrap(DocumentStroke.request(id: "pk_left", tool: "pen", colour: "graphite", size: PdfController.penSize, page: 2, box: box, points: beside))
        XCTAssertEqual(marginal["margin"] as? String, "left")
        XCTAssertEqual(DocumentStroke.margin(u: 1.02), "right")
        await pdf.change("document.ink.add", ["strokes": [marginal]])
        let left = try XCTUnwrap(pdf.strokes.first { $0.id == "pk_left" })
        XCTAssertEqual(left.margin, "left", "music-core answers the margin it kept")
        XCTAssertEqual(left.path(in: box).first?.x ?? 0, -12, accuracy: 1e-6)
        XCTAssertNil(pdf.strokes.first { $0.id == "pk_test" }?.margin)
        await pdf.change("document.ink.remove", ["ids": ["pk_left"]])

        // The project file keeps the PDF and its bookmarks, and opens as a PDF piece again.
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("Nocturne test.motiff.json")
        try await ProjectFiles.save(piece: opened.id, to: url)
        let file = try Data(contentsOf: url)
        let again = try await OpenPiece.open(fileName: url.lastPathComponent, data: file, language: "en").get()
        defer { MusicCore.close(piece: again.id) }
        XCTAssertTrue(again.isDocument)
        let marks = try object(MusicCore.call(piece: again.id, "document.marks"))["marks"] as? [String: Any]
        let bookmarks = PdfController.bookmarks(marks)
        XCTAssertEqual(bookmarks.map(\.name), ["Coda"])
        XCTAssertEqual(DocumentStroke.list(marks).map(\.id), ["pk_test"], "the strokes are kept with the PDF")
        let read = await PieceSummary.read(url)
        let summary = try XCTUnwrap(read)
        XCTAssertFalse(summary.hasNotes, "the library shows it as a PDF")
        try? FileManager.default.removeItem(at: url)
    }

    @MainActor
    func testTheMetronomeTakesATappedTempoAndKeepsItsTempoForThePiece() throws {
        XCTAssertEqual(Metronome.tappedBpm([0, 500, 1000, 1500]), 120)
        XCTAssertNil(Metronome.tappedBpm([0]), "one tap is no tempo")
        XCTAssertNil(Metronome.tappedBpm([0, 2500]), "a long gap is a new start")
        XCTAssertEqual(Metronome.tappedBpm([0, 100]), 240, "the tempo stays within 30 to 240")

        let store = try XCTUnwrap(UserDefaults(suiteName: "motiff-metronome-test"))
        store.removePersistentDomain(forName: "motiff-metronome-test")
        let metronome = Metronome(store: store)
        metronome.load(piece: "p_pdf")
        XCTAssertEqual(metronome.bpm, 80)
        XCTAssertEqual(metronome.beats, 4)
        metronome.faster()
        metronome.nextBeats()
        XCTAssertEqual(metronome.bpm, 84)
        XCTAssertEqual(metronome.beats, 6)
        metronome.nextBeats()
        XCTAssertEqual(metronome.beats, 2)
        let again = Metronome(store: store)
        again.load(piece: "p_pdf")
        XCTAssertEqual(again.bpm, 84)
        XCTAssertEqual(again.beats, 2)
        store.removePersistentDomain(forName: "motiff-metronome-test")
    }

    func testALibraryCardReadsAProjectFileWithoutOpeningIt() throws {
        let file = MusicCore.call(piece: piece, "project.save")
        let body = try JSONSerialization.data(withJSONObject: ["file": file])
        let summary = try XCTUnwrap(PieceSummary(answer: MusicCore.call(piece: 0, "project.summary", String(decoding: body, as: UTF8.self))))
        XCTAssertTrue(summary.hasNotes)
        XCTAssertEqual(summary.origin, "imported")
        XCTAssertEqual(summary.checkBars, 0)
    }

    func testAPdfAndTheNotesReadFromItFindEachOtherOnTheShelf() throws {
        // Notes read from a PDF piece name it and where each of its pages starts in them (PDF | Notes, 0023).
        let saved = MusicCore.call(piece: piece, "project.save")
        let brace = try XCTUnwrap(saved.firstIndex(of: "{"))
        let linked = saved.replacingCharacters(in: brace...brace, with: #"{"readFrom": {"project": "p_pdf", "pages": [0, 5760, 11520]}, "#)
        let body = try JSONSerialization.data(withJSONObject: ["file": linked])
        let notes = try XCTUnwrap(PieceSummary(answer: MusicCore.call(piece: 0, "project.summary", String(decoding: body, as: UTF8.self))))
        XCTAssertEqual(notes.readFrom, "p_pdf")
        XCTAssertEqual(notes.readFromPages, [0, 5760, 11520])
        XCTAssertNotNil(notes.id)

        var pdf = notes
        pdf.id = "p_pdf"
        pdf.hasNotes = false
        pdf.readFrom = nil
        pdf.readFromPages = []
        let notesFile = URL(fileURLWithPath: "/shelf/Nocturne.motiff.json")
        let pdfFile = URL(fileURLWithPath: "/shelf/Nocturne PDF.motiff.json")
        let shelf = [notesFile: notes, pdfFile: pdf]
        XCTAssertEqual(OtherView.of(notesFile, in: shelf)?.file, pdfFile, "the notes lead to the PDF they were read from")
        let back = try XCTUnwrap(OtherView.of(pdfFile, in: shelf))
        XCTAssertEqual(back.file, notesFile, "and the PDF to the notes read from it")
        XCTAssertEqual(back.pages.start(of: 2), 5760)
        XCTAssertEqual(back.pages.start(of: 9), 11520, "a page past the last starts where the last does")
        XCTAssertEqual(back.pages.page(of: 5759), 1)
        XCTAssertEqual(back.pages.page(of: 5760), 2)
        XCTAssertEqual(back.pages.page(of: 99_999), 3)
        XCTAssertNil(OtherView.of(notesFile, in: [notesFile: notes]), "no PDF on the shelf, no switch")
        XCTAssertNil(OtherView.of(URL(fileURLWithPath: "/shelf/Other.motiff.json"), in: shelf))
    }

    func testARecordingIsPlannedInPartsAndItsSoundReadBack() async throws {
        let parts = await RecordingParts.plan(600_000)
        XCTAssertEqual(parts.count, 5, "all 10 minutes, in parts of about two minutes")
        XCTAssertEqual(parts.first?.sendFrom, 0)
        XCTAssertEqual(parts.last?.sendTo, 600_000)
        XCTAssertEqual(parts.first?.sendTo, 125_000, "without levels the seam is the even cut, with five seconds of the next part")

        // The levels of 100 ms lay the seam at the quietest moment within ten seconds of the even cut (0014 item 7).
        var levels = [Int](repeating: 500, count: 6_000)
        for index in 1_150..<1_160 { levels[index] = 0 }
        let quiet = await RecordingParts.plan(600_000, levels: levels)
        XCTAssertEqual(quiet.first?.sendTo, 120_750, "kept to 115.75 s, sent five seconds on")

        let tone = (0..<22_050).map { Int16(8_000 * sin(Double($0) * 2 * .pi * 440 / RecordingSound.sampleRate)) }
        let wav = RecordingSound.wav(tone[...])
        XCTAssertEqual(wav.count, 44 + tone.count * 2)
        XCTAssertEqual(String(decoding: wav.prefix(4), as: UTF8.self), "RIFF")
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("tone-\(UUID().uuidString).wav")
        try wav.write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        let sound = try XCTUnwrap(RecordingSound.read(url, most: 420_000))
        XCTAssertEqual(Double(sound.milliseconds), 1_000, accuracy: 20)
        XCTAssertEqual(RecordingSound.read(url, most: 500)?.milliseconds, 500)
        XCTAssertEqual(sound.wav(from: 250, to: 750).count, 44 + 11_025 * 2)
        let heard = sound.levels(from: 0, to: 1_000)
        XCTAssertEqual(heard.count, 10, "one level every 100 ms")
        XCTAssertEqual(Double(heard[3]), 8_000 * 2 / .pi, accuracy: 60, "the mean size of a sine 8,000 high")
    }

    func testTheNotesAScanReadsFromAPdfFillThatPieceWhichOpensAsNotesOrAsItsPdf() throws {
        // Read the notes (0023; testing item 16, contract 6): what the scan server reads goes into the PDF piece itself.
        let data = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842), format: UIGraphicsPDFRendererFormat()).pdfData { context in
            for _ in 0..<3 { context.beginPage() }
        }
        let opened = try object(MusicCore.open(fileName: "Elise.pdf", data: data, request: #"{"language": "en", "id": "p_pdf"}"#))
        let pdf = Int32(opened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: pdf) }

        let example = try XCTUnwrap(KitPiece.example)
        let request: [String: Any] = ["musicXml": example.data.base64EncodedString(), "engine": "homr 0.7.0", "pages": 3,
                                      "language": "en", "now": "2026-11-05T12:00:00Z"]
        let body = String(decoding: try JSONSerialization.data(withJSONObject: request), as: UTF8.self)
        let read = try object(MusicCore.call(piece: pdf, "document.read", body))
        XCTAssertNil(read["error"], "\(read)")
        let project = try XCTUnwrap(read["project"] as? [String: Any])
        XCTAssertEqual(project["id"] as? String, "p_pdf", "the same piece")
        XCTAssertNil(project["readFrom"])
        XCTAssertEqual((project["documentPages"] as? [NSNumber])?.count, 3)
        let file = try XCTUnwrap(read["file"] as? String)
        XCTAssertEqual(MusicCore.call(piece: pdf, "project.save", #"{"now": "2026-11-05T12:00:00Z"}"#), file)
        let notes = try XCTUnwrap(PieceSummary(answer: MusicCore.call(piece: 0, "project.summary", String(decoding: try JSONSerialization.data(withJSONObject: ["file": file]), as: UTF8.self))))
        XCTAssertTrue(notes.hasNotes)
        XCTAssertTrue(notes.document)
        XCTAssertEqual(notes.documentPages.count, 3)
        XCTAssertEqual(notes.origin, "scanned", "notes a scan read are detected, never imported")
        let url = URL(fileURLWithPath: "/shelf/Elise.motiff.json")
        let other = try XCTUnwrap(OtherView.of(url, in: [url: notes]))
        XCTAssertEqual(other.file, url, "PDF | Notes switches the two views of the one file")
        XCTAssertEqual(other.pages.start(of: 1), 0)

        let asNotes = try object(MusicCore.open(fileName: "Elise.motiff.json", data: Data(file.utf8), request: #"{"language": "en"}"#))
        defer { MusicCore.close(piece: Int32(asNotes["piece"] as? Int ?? 0)) }
        XCTAssertNil(asNotes["document"], "a piece with notes opens as notes")
        let asPdf = try object(MusicCore.open(fileName: "Elise.motiff.json", data: Data(file.utf8), request: #"{"language": "en", "view": "pdf"}"#))
        defer { MusicCore.close(piece: Int32(asPdf["piece"] as? Int ?? 0)) }
        XCTAssertNotNil(asPdf["document"] as? [String: Any], "or in its PDF view")
        XCTAssertEqual(try object(MusicCore.call(piece: pdf, "document.read", #"{"musicXml": "not base64!"}"#))["error"] as? String, "badRequest")
    }

    func testAPieceKeepsItsSourcesAndAPdfInsideIt() throws {
        // Sources (testing item 16, contract 6): the base, a PDF kept inside the piece, and its bytes back for the scan server.
        let first = try XCTUnwrap(SourcesInfo(answer: MusicCore.call(piece: piece, "sources")))
        XCTAssertEqual(first.base, "file")
        XCTAssertFalse(first.baseName.isEmpty)
        XCTAssertTrue(first.summary.contains(first.baseName), "the line names the base, in the piece's language: \(first.summary)")
        XCTAssertNil(first.document)

        let data = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842), format: UIGraphicsPDFRendererFormat()).pdfData { context in
            for _ in 0..<2 { context.beginPage() }
        }
        let body = String(decoding: try JSONSerialization.data(withJSONObject: ["pdf": data.base64EncodedString(), "pages": 2]), as: UTF8.self)
        let kept = try XCTUnwrap(SourcesInfo(answer: MusicCore.call(piece: piece, "sources.document", body)))
        XCTAssertEqual(kept.document?.pages, 2)
        XCTAssertEqual(kept.document?.starts.count, 2)
        XCTAssertEqual(kept.document?.starts.first, 0)
        let back = try object(MusicCore.call(piece: piece, "sources.pdf"))
        XCTAssertEqual((back["pdf"] as? String).flatMap { Data(base64Encoded: $0) }, data)
        XCTAssertEqual(try object(MusicCore.call(piece: piece, "performance.takeAll", #"{"id": "perf_9"}"#))["error"] as? String, "notFound")
    }

    @MainActor
    func testTheScanServerIsTheAddressThePlayerGaveOrTheHostedOne() throws {
        XCTAssertEqual(ScanServer.parse(" http://192.168.1.20:8765 ")?.absoluteString, "http://192.168.1.20:8765/")
        XCTAssertNil(ScanServer.parse("ftp://example.com"))
        XCTAssertNil(ScanServer.parse("http://example.com/recognise"), "an address, not a path on it")
        XCTAssertNil(ScanServer.parse("http://me@example.com"))
        XCTAssertNil(ScanServer.parse("example"))
        XCTAssertTrue(ScanServer.isLocal(try XCTUnwrap(ScanServer.parse(ScanServer.suggested))))
        XCTAssertFalse(ScanServer.isLocal(try XCTUnwrap(ScanServer.parse("http://192.168.1.20:8765"))))

        let store = try XCTUnwrap(UserDefaults(suiteName: "motiff-scan-test"))
        store.removePersistentDomain(forName: "motiff-scan-test")
        defer { store.removePersistentDomain(forName: "motiff-scan-test") }
        let hosted = URL(string: "https://scan.example/")
        let scan = ScanServer(defaults: store)
        XCTAssertEqual(scan.address(hosted: hosted), hosted, "Motiff Lab's server until the player chooses")
        XCTAssertNil(scan.address(hosted: nil), "and off in a build without one")
        let own = try XCTUnwrap(ScanServer.parse("http://192.168.1.20:8765"))
        scan.choose(own)
        XCTAssertEqual(ScanServer(defaults: store).address(hosted: hosted), own, "the player's server is kept on the device")
        scan.choose(nil)
        XCTAssertNil(scan.address(hosted: hosted), "turned off, nothing is sent")
        scan.chooseHosted()
        XCTAssertEqual(scan.address(hosted: hosted), hosted)
    }

    /// A file larger than the scan server reads is not sent, so it is not refused after the whole upload (v0.5, E).
    @MainActor
    func testAFileLargerThanTheScanServerReadsIsNotSent() async throws {
        let address = try XCTUnwrap(ScanServer.parse("http://127.0.0.1:9"))
        let big = Data(count: ScanServer.maxUploadBytes + 1)
        let answer = await ScanServer.recognisePage(fileName: "big.pdf", data: big, address: address, account: Account(options: nil))
        guard case .failure(.tooLarge) = answer.result else { return XCTFail("refused before sending") }
        XCTAssertNil(answer.report)
        XCTAssertTrue(ScanProblem.tooLarge.text(UIText(language: "en")).hasPrefix("This file is larger than 50 MB"))
    }

    func testANoteOfABarIsCorrectedAndPutBackAsImported() throws {
        // Fix notes in Check (S1-I7): a correction is a step in the piece's edit log; Revert puts the imported note back.
        let bar = try XCTUnwrap(NoteCorrections(answer: MusicCore.call(piece: piece, "corrections", #"{"bar": 1}"#)))
        XCTAssertEqual(bar.bar, 1)
        XCTAssertEqual(bar.count, 0)
        let note = try XCTUnwrap(bar.notes.first)
        XCTAssertEqual(note.place.bar, 1)
        XCTAssertEqual(note.place.text(UIText(language: "en")).prefix(6), "bar 1,")
        let higher = try XCTUnwrap(NoteCorrections(answer: MusicCore.call(piece: piece, "corrections.change", #"{"bar": 1, "id": "\#(note.id)", "pitch": 1}"#)))
        let changed = try XCTUnwrap(higher.notes.first { $0.id == note.id })
        XCTAssertEqual(changed.pitch, note.pitch + 1)
        XCTAssertTrue(changed.edited)
        XCTAssertEqual(higher.count, 1)
        let removed = try XCTUnwrap(NoteCorrections(answer: MusicCore.call(piece: piece, "corrections.remove", #"{"bar": 1, "id": "\#(note.id)"}"#)))
        XCTAssertEqual(removed.removed.map(\.id), [note.id])
        XCTAssertFalse(removed.notes.contains { $0.id == note.id })
        let back = try XCTUnwrap(NoteCorrections(answer: MusicCore.call(piece: piece, "corrections.revert", #"{"bar": 1, "id": "\#(note.id)"}"#)))
        XCTAssertEqual(back.count, 0)
        XCTAssertEqual(back.notes.first { $0.id == note.id }?.pitch, note.pitch)
        XCTAssertEqual(try call("corrections.undo", #"{"bar": 1}"#, expectError: true)["error"] as? String, "unavailable")
    }

    func testTheNoteEditorAddsMovesAndRemovesNotesOnADraftKeptOnlyBySave() throws {
        // Edit notes on the roll (testing item 15): the edits sound at once, the project file keeps the notes as they
        // were until Save, Undo steps back through the draft, Cancel puts every edit back.
        let begun = try call("corrections.begin", #"{"bar": 1}"#)
        XCTAssertEqual(begun["editing"] as? Bool, true)
        XCTAssertEqual(begun["canUndo"] as? Bool, false)
        let added = try call("corrections.add", #"{"bar": 1, "onset": 0, "pitch": 40, "length": 240}"#)
        let id = try XCTUnwrap(added["added"] as? String)
        XCTAssertEqual(added["changed"] as? Bool, true)
        let moved = try call("corrections.set", #"{"bar": 1, "id": "\#(id)", "onset": 120, "pitch": 41, "length": 480}"#)
        XCTAssertEqual(moved["canUndo"] as? Bool, true)
        XCTAssertEqual(RollNoteEdit(["kind": "change", "id": id, "onset": 120, "pitch": 41, "length": 480]), .change(id: id, onset: 120, pitch: 41, length: 480))
        XCTAssertEqual(RollNoteEdit(["kind": "add", "onset": 0, "pitch": 40, "length": 240]), .add(onset: 0, pitch: 40, length: 240))
        XCTAssertNil(RollNoteEdit(["kind": "remove"]))
        _ = try call("corrections.undo", #"{"bar": 1}"#)
        let discarded = try call("corrections.discard", #"{"bar": 1}"#)
        XCTAssertEqual(discarded["editing"] as? Bool, false)
        XCTAssertEqual(discarded["count"] as? Int, 0, "Cancel leaves no edit")
        _ = try call("corrections.begin", #"{"bar": 1}"#)
        _ = try call("corrections.add", #"{"bar": 1, "onset": 0, "pitch": 40, "length": 240}"#)
        let saved = try call("corrections.save", #"{"bar": 1}"#)
        XCTAssertEqual(saved["editing"] as? Bool, false)
        XCTAssertEqual(saved["count"] as? Int, 1, "Save keeps the added note")
        _ = try call("corrections.reset", #"{"bar": 1}"#)
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
