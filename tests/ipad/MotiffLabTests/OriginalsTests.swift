import FileProvider
import UIKit
import XCTest
@testable import MotiffLab

/// Local-first originals on the iPad (version 0.3): the badge from the devices' marks and this iPad's link, the link to a
/// file in Files kept beside the projects and checked by size and date before its fingerprint, and «Привязать оригинал»
/// through music-core (`sources.relink`, `sources.held`), which writes only the iPad's mark into the project file.
final class OriginalsTests: XCTestCase {
    private var folder: URL!

    override func setUpWithError() throws {
        folder = FileManager.default.temporaryDirectory.appendingPathComponent("OriginalsTests-\(UUID().uuidString)")
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: folder)
    }

    private func original(inFile: Bool = false, held: [String] = [], kind: String = "pdf") -> OriginalEntry {
        OriginalEntry(
            id: kind == "pdf" ? "src_pdf" : "perf_1", kind: kind, shown: "Nocturne.pdf", fileName: "Nocturne.pdf",
            sha256: String(repeating: "a", count: 64), bytes: 1000, pages: 3, kept: "file", inFile: inFile, held: held
        )
    }

    private func link(changed: Bool = false) -> OriginalLink {
        OriginalLink(
            bookmark: Data([1, 2, 3]), fileName: "Nocturne.pdf", sha256: String(repeating: "a", count: 64),
            bytes: 1000, modified: Date(timeIntervalSince1970: 1_000_000), changed: changed, linkedAt: Date(timeIntervalSince1970: 1_000_000)
        )
    }

    func testTheBadgeSaysWhereTheBytesAreAsThisIpadSeesThem() {
        XCTAssertEqual(OriginalBadge.of(original(inFile: true, held: ["web"]), link: nil), .here)
        XCTAssertEqual(OriginalBadge.of(original(inFile: true), link: link(changed: true)), .here, "a copy in the piece is here whatever the link says")
        XCTAssertEqual(OriginalBadge.of(original(held: ["web"]), link: link()), .linked)
        XCTAssertEqual(OriginalBadge.of(original(held: ["ipad", "web"]), link: link(changed: true)), .changed)
        XCTAssertEqual(OriginalBadge.of(original(held: ["web"]), link: nil), .inBrowser)
        XCTAssertEqual(OriginalBadge.of(original(held: ["ipad", "web"]), link: nil), .inBrowser, "another kind of device comes first")
        XCTAssertEqual(OriginalBadge.of(original(held: ["ipad"]), link: nil), .otherIpad, "an iPad's mark with no link here is another iPad's")
        XCTAssertEqual(OriginalBadge.of(original(), link: nil), .nowhere)
        XCTAssertTrue(OriginalBadge.here.isHere)
        XCTAssertTrue(OriginalBadge.linked.isHere)
        XCTAssertFalse(OriginalBadge.changed.isHere)
    }

    func testTheBadgesAreWordedInBothLanguages() {
        let badges: [OriginalBadge] = [.here, .linked, .changed, .otherIpad, .inBrowser, .nowhere]
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for badge in badges {
                XCTAssertFalse(badge.text(text).hasPrefix("originals."), "\(language): \(badge)")
            }
            for problem in [LinkProblem.missing, .offline, .unreadable] {
                XCTAssertFalse(problem.text(text).hasPrefix("originals."), "\(language): \(problem)")
            }
        }
        let ru = UIText(language: "ru")
        XCTAssertEqual(OriginalBadge.inBrowser.text(ru), "Только в браузере на компьютере")
        XCTAssertEqual(OriginalBadge.linked.text(ru), "Ссылка на файл в «Файлах»")
        XCTAssertEqual(OriginalBadge.changed.text(ru), "Оригинал изменился")
        XCTAssertEqual(OriginalBadge.nowhere.text(ru), ru["originals.nowhere"], "the web's own words")
    }

    /// #582 item 5: Sources adds another source of the same piece with «Добавить», or «Добавить ещё» once it has one.
    func testSourcesAddsMoreRatherThanAnOriginal() {
        let ru = UIText(language: "ru")
        let en = UIText(language: "en")
        XCTAssertEqual(ru[SourcesSheet.addWord(liveOriginals: 0)], "Добавить")
        XCTAssertEqual(ru[SourcesSheet.addWord(liveOriginals: 2)], "Добавить ещё")
        XCTAssertEqual(en[SourcesSheet.addWord(liveOriginals: 0)], "Add")
        XCTAssertEqual(en[SourcesSheet.addWord(liveOriginals: 1)], "Add more")
    }

    /// The Sources sheet offers «Распознать» on a piece with working notes for the pages kept in the piece here (a PDF or
    /// photos) and, since v0.10 (findings 26, 28), for a recording whose sound is on this iPad; never for a linked or a
    /// deleted PDF, a recording heard elsewhere, MusicXML, nor the pages of a piece that is only its PDF; a piece that is
    /// only its recording, with no notes yet, hears it (v1.1, finding 29).
    @MainActor
    func testThePagesKeptInThePieceAndARecordingHereAreRecognised() {
        func source(_ kind: String, state: String = "here", kept: String = "file") -> SourceLayers.Original {
            SourceLayers.Original(
                id: "src_\(kind)", kind: kind, shown: kind, fileName: kind, state: state, removedAt: nil, kept: kept,
                pages: 2, durationMs: nil, hidden: false, notes: [], pageMarks: 0
            )
        }
        var layers = SourceLayers()
        layers.working = "n_1"
        let sources = SourcesController(layers: layers)
        XCTAssertTrue(sources.canRecognise(source("pdf")))
        XCTAssertTrue(sources.canRecognise(source("images")))
        XCTAssertTrue(sources.canRecognise(source("audio", kept: "device")), "a recording whose sound is here")
        XCTAssertFalse(sources.canRecognise(source("audio", state: "onAnotherDevice", kept: "device")), "its sound is on another iPad")
        XCTAssertFalse(sources.canRecognise(source("musicxml")))
        XCTAssertFalse(sources.canRecognise(source("pdf", kept: "link")), "only bytes kept in the piece go")
        XCTAssertFalse(sources.canRecognise(source("pdf", state: "onAnotherDevice")))
        XCTAssertFalse(sources.canRecognise(source("pdf", state: "removed")))
        XCTAssertFalse(SourcesController().canRecognise(source("pdf")), "a piece that is only its PDF has no notes to add to")
        XCTAssertTrue(SourcesController().canRecognise(source("audio", kept: "device")), "a piece that is only its recording takes its notes")
    }

    /// A recording is recognised from its sound on this iPad (v0.10, finding 26): without it nothing is sent, the row says
    /// why, and the original is no longer being read.
    @MainActor
    func testARecordingWhoseSoundIsNotHereIsNotSent() async {
        let recording = SourceLayers.Original(
            id: "src_rec", kind: "audio", shown: "Live.m4a", fileName: "Live.m4a", state: "here", removedAt: nil, kept: "device",
            pages: nil, durationMs: 60_000, hidden: false, notes: [], pageMarks: 0
        )
        let sources = SourcesController()
        let english = UIText(language: "en")
        let why = await sources.recognise(recording, address: nil, account: Account(options: nil))
        XCTAssertEqual(why, UIText.current["sources.sheet.soundNotHere"])
        XCTAssertFalse(english["sources.sheet.soundNotHere"].hasPrefix("sources."), "worded in English")
        XCTAssertNil(sources.reading)
        XCTAssertNil(sources.progress)
    }

    /// E-sources-1 (v1.6 i-13): one original is recognised at a time, as the library recognises one piece at a time. While
    /// one is on its way, another's panel says which is being recognised instead of offering Send (and `recognise` answers
    /// the same words without sending); the one on its way is offered as before.
    @MainActor
    func testAnotherOriginalWaitsWhileOneIsRecognised() {
        func original(_ id: String, _ fileName: String) -> SourceLayers.Original {
            SourceLayers.Original(
                id: id, kind: "pdf", shown: fileName, fileName: fileName, state: "here", removedAt: nil, kept: "file",
                pages: 2, durationMs: nil, hidden: false, notes: [], pageMarks: 0
            )
        }
        let henle = original("src_a", "Henle.pdf")
        let peters = original("src_b", "Peters.pdf")
        var layers = SourceLayers()
        layers.originals = [henle, peters]
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            let words = SourcesController.readingOther("src_a", than: peters, in: layers, text: text)
            XCTAssertEqual(words, text.format("sources.sheet.readingOther", "Henle.pdf"), language)
            XCTAssertFalse(words?.hasPrefix("sources.") ?? true, "worded in \(language)")
            XCTAssertNil(SourcesController.readingOther("src_a", than: henle, in: layers, text: text), "the one on its way")
            XCTAssertNil(SourcesController.readingOther(nil, than: peters, in: layers, text: text), "none on its way")
        }
        XCTAssertNil(SourcesController(layers: layers).readingOther(than: peters), "nothing is being recognised")
    }

    /// E-sources-2 (v1.6 i-13): a file picked in Files for Sources that cannot be read says so on the sheet, as a picked
    /// original does in «Привязать оригинал», instead of nothing; a readable one is read as it is.
    @MainActor
    func testAPickedFileThatCannotBeReadSaysSo() throws {
        let sources = SourcesController()
        XCTAssertNil(sources.read(picked: folder.appendingPathComponent("Gone.pdf")))
        XCTAssertEqual(sources.status, UIText.current["originals.unreadable"])
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let file = folder.appendingPathComponent("Etude.pdf")
        try Data([1, 2, 3]).write(to: file)
        XCTAssertEqual(sources.read(picked: file), Data([1, 2, 3]))
    }

    /// v1.5 r-2b: pages recognised in Sources with no scan server set (scanning off) say how to set one up, as the PDF's own
    /// panel does, and send nothing; before, they said the server found no notes, which no server had been asked.
    @MainActor
    func testPagesWithNoScanServerSaySetItUpNotThatNoNotesWereFound() async {
        let pages = SourceLayers.Original(
            id: "src_pdf", kind: "pdf", shown: "Etude.pdf", fileName: "Etude.pdf", state: "here", removedAt: nil, kept: "file",
            pages: 2, durationMs: nil, hidden: false, notes: [], pageMarks: 0
        )
        let why = await SourcesController().recognise(pages, address: nil, account: Account(options: nil))
        XCTAssertEqual(why, SourcesController.noScanServer(UIText.current))
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            XCTAssertNotEqual(SourcesController.noScanServer(text), text["pdf.readNone"], language)
        }
        XCTAssertEqual(SourcesController.noScanServer(UIText(language: "ru")), "Motiff Lab может прочитать ноты из PDF или фото страницы с помощью сервера распознавания. Настройте его в Настройках.")
    }

    /// v0.10 s-3: the fields `sources.notes.hear` gets for a recording kept in the piece, as the facade takes them: the
    /// transcription's with the live tempo, the original, the time, and the working notes' metre and tempo as "hint".
    func testTheRecordingHeardIsSentWithTheFieldsTheFacadeTakes() throws {
        let heard = HeardRecording(engine: "basic-pitch", milliseconds: 60_000, parts: [(index: 0, midi: Data([1, 2]))], levels: [3, 4])
        let hint: [String: Any] = ["signature": "3/4", "quarterNotesPerMinute": 92]
        let fields = SourcesController.hearFields(heard, original: "src_rec", hint: hint, now: "2026-11-19T10:00:00Z")
        XCTAssertEqual(Set(fields.keys), ["engine", "milliseconds", "levels", "parts", "tempo", "original", "now", "hint"])
        XCTAssertEqual(fields["tempo"] as? String, "live")
        XCTAssertEqual(fields["original"] as? String, "src_rec")
        XCTAssertEqual(fields["now"] as? String, "2026-11-19T10:00:00Z")
        XCTAssertEqual(fields["milliseconds"] as? Int64, 60_000)
        XCTAssertEqual(fields["engine"] as? String, "basic-pitch")
        let parts = try XCTUnwrap(fields["parts"] as? [[String: Any]])
        XCTAssertEqual(parts.first?["midi"] as? String, Data([1, 2]).base64EncodedString())
        XCTAssertEqual((fields["hint"] as? [String: Any])?["signature"] as? String, "3/4")
        XCTAssertNil(SourcesController.hearFields(heard, original: "src_rec", hint: nil, now: "").index(forKey: "hint"), "no hint, no field")
        XCTAssertNil(fields["starts"], "the iPad does not know where the pieces begin")
        XCTAssertNil(heard.request["tempo"], "the request itself finds no bars")
    }

    /// v1.5 c-11: notes recognised again say in their toast what music-core carried of the player's corrections, and
    /// only what was read when it carried nothing or says nothing.
    func testTheToastOfNotesReadAgainSaysWhatWasCarried() {
        let carried: [String: Any] = ["carried": 2, "text": "2 corrections carried over · not carried: 1 (bar 3)"]
        XCTAssertEqual(SourcesController.toastText("Notes recognised · 1 page", adding: carried), "Notes recognised · 1 page · 2 corrections carried over · not carried: 1 (bar 3)")
        XCTAssertEqual(SourcesController.toastText("Notes recognised · 1 page", adding: nil), "Notes recognised · 1 page")
        XCTAssertEqual(SourcesController.toastText("Notes recognised · 1 page", adding: ["carried": 0, "text": NSNull()] as [String: Any]), "Notes recognised · 1 page")
        // v1.5 c-11b: notes made working say how the bookmarks and phrases followed their bars ("followed").
        let followed: [String: Any] = ["moved": 1, "text": "moved with their bars: 1 bookmark"]
        XCTAssertEqual(SourcesController.toastText("Now working: the notes from Henle", adding: followed), "Now working: the notes from Henle · moved with their bars: 1 bookmark")
    }

    /// v0.10 s-3: the hint is the working notes' first bar as music-core writes a signature, with the tempo the player set
    /// or the notes state; never the default tempo, and none without bars or for a beat no signature counts in.
    func testTheHintIsTheWorkingNotesMetreAndTempo() throws {
        func playback(beatTicks: Int, beats: Int, tempo: String) throws -> PlaybackAnswer {
            let json = """
            {"end": 1920, "notes": [], "tempos": [], "beats": [],
             "bars": [{"number": 1, "onset": 0, "beatTicks": \(beatTicks), "beatsPerBar": \(beats)}], \(tempo)}
            """
            return try JSONDecoder().decode(PlaybackAnswer.self, from: Data(json.utf8))
        }
        let notated = #""tempo": {"perMinute": 92, "source": "notated", "piece": {"perMinute": 92, "source": "notated"}, "player": null}"#
        let waltz = SourcesController.hint(try playback(beatTicks: 480, beats: 3, tempo: notated))
        XCTAssertEqual(waltz?["signature"] as? String, "3/4")
        XCTAssertEqual(waltz?["quarterNotesPerMinute"] as? Int, 92)
        let set = #""tempo": {"perMinute": 70, "source": "player", "piece": {"perMinute": 92, "source": "notated"}, "player": 70}"#
        XCTAssertEqual(SourcesController.hint(try playback(beatTicks: 240, beats: 6, tempo: set))?["signature"] as? String, "6/8")
        XCTAssertEqual(SourcesController.hint(try playback(beatTicks: 240, beats: 6, tempo: set))?["quarterNotesPerMinute"] as? Int, 70, "the player's")
        let unstated = #""tempo": {"perMinute": 80, "source": "default", "piece": {"perMinute": 80, "source": "default"}, "player": null}"#
        let plain = SourcesController.hint(try playback(beatTicks: 480, beats: 4, tempo: unstated))
        XCTAssertEqual(plain?["signature"] as? String, "4/4")
        XCTAssertNil(plain?["quarterNotesPerMinute"], "the default tempo is no hint")
        XCTAssertNil(SourcesController.hint(try playback(beatTicks: 700, beats: 2, tempo: notated)), "no signature counts that beat")
        XCTAssertNil(SourcesController.hint(nil))
    }

    func testTheLibraryLineNamesTheFirstOriginalNotHere() {
        let ru = UIText(language: "ru")
        let away = OriginalAway.of([original(held: ["web"])]) { _ in nil }
        XCTAssertEqual(away, OriginalAway(kind: "pdf", badge: .inBrowser))
        XCTAssertEqual(away?.text(ru), "PDF: только в браузере на компьютере")
        let linkedPdf = OriginalAway.of([original(held: ["web"]), original(held: ["web"], kind: "audio")]) { $0.kind == "pdf" ? self.link() : nil }
        XCTAssertEqual(linkedPdf?.kind, "audio", "a linked PDF is here; the recording is not")
        XCTAssertNil(OriginalAway.of([original(inFile: true)]) { _ in nil })
        let midi = OriginalEntry(id: "src_file", kind: "midi", shown: "a.mid", fileName: "a.mid", sha256: nil, bytes: nil, pages: nil, kept: "file", inFile: false, held: [])
        XCTAssertNil(OriginalAway.of([midi]) { _ in nil }, "a MIDI file is the notes, not an original to show")
        XCTAssertEqual(OriginalAway(kind: "pdf", badge: .changed).text(UIText(language: "en")), "PDF: the original has changed")
    }

    func testOriginalsAreReadFromTheFacadesAnswer() throws {
        let answer = #"{"originals": [{"id": "src_pdf", "kind": "pdf", "shown": "Nocturne", "fileName": "Nocturne.pdf", "sha256": null, "bytes": 12, "pages": null, "durationMs": null, "kept": "file", "inFile": false, "held": [{"device": "web", "at": "2026-11-12T01:00:00Z"}]}]}"#
        let list = OriginalEntry.list(facadeObject(answer)?["originals"])
        let pdf = try XCTUnwrap(list.first)
        XCTAssertEqual(list.count, 1)
        XCTAssertEqual(pdf.shown, "Nocturne")
        XCTAssertNil(pdf.sha256)
        XCTAssertNil(pdf.pages)
        XCTAssertEqual(pdf.bytes, 12)
        XCTAssertEqual(pdf.held, ["web"])
        XCTAssertTrue(pdf.isDocument)
        XCTAssertTrue(pdf.isShown)
    }

    func testALinkIsTheSameFileWhileItsSizeAndDateAreAndItsFingerprintDecidesOtherwise() {
        let kept = link()
        let date = Date(timeIntervalSince1970: 1_000_000)
        XCTAssertEqual(kept.check(bytes: 1000, modified: date), .same)
        XCTAssertEqual(kept.check(bytes: 1001, modified: date), .unknown)
        XCTAssertEqual(kept.check(bytes: 1000, modified: date.addingTimeInterval(60)), .unknown)
        XCTAssertEqual(kept.check(bytes: nil, modified: date), .unknown)

        // Touched but the same bytes: the new size and date are the original's from now on.
        let touched = kept.verified(sha256: kept.sha256, bytes: 1000, modified: date.addingTimeInterval(60))
        XCTAssertFalse(touched.changed)
        XCTAssertEqual(touched.check(bytes: 1000, modified: date.addingTimeInterval(60)), .same)

        // Another file: changed, and not read again while it stays as it is.
        let other = kept.verified(sha256: String(repeating: "b", count: 64), bytes: 2000, modified: date.addingTimeInterval(120))
        XCTAssertTrue(other.changed)
        XCTAssertEqual(other.check(bytes: 2000, modified: date.addingTimeInterval(120)), .changed)
        XCTAssertEqual(other.check(bytes: 1000, modified: date), .same, "back to the original's size and date")
        XCTAssertFalse(other.verified(sha256: kept.sha256, bytes: 1000, modified: date).changed, "the original back again")
    }

    func testLinksAreKeptBesideTheProjectsByProjectAndOriginal() throws {
        let kept = link()
        XCTAssertNil(OriginalLinks.load(project: "p_1", original: "src_pdf", in: folder))
        OriginalLinks.save(kept, project: "p_1", original: "src_pdf", in: folder)
        XCTAssertEqual(OriginalLinks.load(project: "p_1", original: "src_pdf", in: folder), kept)
        XCTAssertNil(OriginalLinks.load(project: "p_2", original: "src_pdf", in: folder))
        XCTAssertEqual(OriginalLinks.url(project: "p_1", original: "src_pdf", in: folder).lastPathComponent, "src_pdf.json")
        XCTAssertEqual(OriginalLinks.safe("../a b/c"), "___a_b_c")
        OriginalLinks.remove(project: "p_1", original: "src_pdf", in: folder)
        XCTAssertNil(OriginalLinks.load(project: "p_1", original: "src_pdf", in: folder))
    }

    func testNoConnectionAndAMissingFileAreToldApart() {
        XCTAssertEqual(LinkProblem.of(NSError(domain: NSURLErrorDomain, code: NSURLErrorNotConnectedToInternet)), .offline)
        let unreachable = NSError(domain: NSFileProviderErrorDomain, code: NSFileProviderError.Code.serverUnreachable.rawValue)
        XCTAssertEqual(LinkProblem.of(NSError(domain: NSCocoaErrorDomain, code: NSFileReadUnknownError, userInfo: [NSUnderlyingErrorKey: unreachable])), .offline)
        XCTAssertEqual(LinkProblem.of(NSError(domain: NSCocoaErrorDomain, code: NSFileReadNoSuchFileError)), .missing)
        XCTAssertEqual(LinkProblem.of(NSError(domain: NSCocoaErrorDomain, code: NSFileReadCorruptFileError)), .unreadable)
    }

    func testTheSamePdfIsKeptInThePieceAgainAndTheIpadWritesOnlyItsOwnMark() throws {
        // A piece with a PDF kept in it, as the online copy has it: notes, and the PDF only described, held by the iPad.
        let example = try XCTUnwrap(KitPiece.example)
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "en"}"#)))
        let first = Int32(opened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: first) }
        let pdf = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842), format: UIGraphicsPDFRendererFormat()).pdfData { context in
            context.beginPage()
        }
        XCTAssertNil(facadeObject(MusicCore.call(piece: first, "sources.document", json(["pdf": pdf.base64EncodedString(), "pages": 1])))?["error"])
        let saved = MusicCore.call(piece: first, "project.save")
        let copy = try XCTUnwrap(facadeObject(MusicCore.call(piece: 0, "project.online", json(["file": saved])))?["file"] as? String)

        let online = try XCTUnwrap(facadeObject(MusicCore.open(fileName: "Elise.motiff.json", data: Data(copy.utf8), request: #"{"language": "en"}"#)))
        let piece = Int32(online["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: piece) }
        let sources = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources")))
        XCTAssertNotNil(sources["projectId"] as? String)
        let described = try XCTUnwrap(OriginalEntry.list(sources["originals"]).first(where: \.isDocument))
        XCTAssertFalse(described.inFile)
        XCTAssertEqual(described.held, ["ipad"])
        XCTAssertEqual(described.sha256, CloudSync.hash(pdf))
        XCTAssertEqual(OriginalBadge.of(described, link: nil), .otherIpad)

        // Its own mark taken back and set again, as a link keeps it, with no PDF in the piece.
        let gone = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources.held", json(["id": "src_pdf", "held": false]))))
        XCTAssertEqual(OriginalEntry.list(gone["originals"]).first?.held, [])
        let linked = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources.held", json(["id": "src_pdf", "held": true]))))
        XCTAssertEqual(OriginalEntry.list(linked["originals"]).first?.held, ["ipad"])
        XCTAssertTrue(linked["document"] is NSNull)

        // Another PDF is refused; the same one is kept in the piece, the same original.
        let other = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 842, height: 595), format: UIGraphicsPDFRendererFormat()).pdfData { context in
            context.beginPage()
            context.beginPage()
        }
        let refused = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources.relink", json(["pdf": other.base64EncodedString()]))))
        XCTAssertEqual(refused["linked"] as? Bool, false)
        let kept = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources.relink", json(["pdf": pdf.base64EncodedString(), "pages": 1]))))
        XCTAssertEqual(kept["linked"] as? Bool, true)
        XCTAssertEqual(SourcesInfo(answer: MusicCore.call(piece: piece, "sources"))?.document?.pages, 1)
        let after = OriginalEntry.list(kept["originals"]).filter(\.isDocument)
        XCTAssertEqual(after.count, 1, "no second original")
        XCTAssertEqual(after.first?.inFile, true)
    }

    /// v1.1, finding 29 («в одну пьесу можно загрузить, например, PDF и аудиозапись, распознать оба»): a PDF piece with no
    /// notes yet has Sources too, where a recording added is kept on this iPad and offered «Распознать · Pro»; its notes,
    /// the piece's first, open it again as notes, and anything else at the page under the bar.
    @MainActor
    func testAPdfPieceWithNoNotesTakesARecordingToRecognise() async throws {
        let pages = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842), format: UIGraphicsPDFRendererFormat()).pdfData { context in
            context.beginPage()
        }
        let piece = try await OpenPiece.open(fileName: "Prelude.pdf", data: pages, language: "en").get()
        XCTAssertTrue(piece.isDocument)
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let file = folder.appendingPathComponent("Prelude.motiff.json")
        let sources = SourcesController()
        await sources.load(piece: piece.id, file: file)
        let project = try XCTUnwrap(sources.projectId, "a PDF piece answers sources")
        defer {
            MusicCore.close(piece: piece.id)
            try? FileManager.default.removeItem(at: folder)
            try? FileManager.default.removeItem(at: OriginalSounds.folder(project: project))
        }
        XCTAssertNil(sources.layers.working, "no notes yet")

        let tone = (0..<22_050).map { Int16(8_000 * sin(Double($0) * 2 * .pi * 440 / RecordingSound.sampleRate)) }
        let take = folder.appendingPathComponent("take.wav")
        try RecordingSound.wav(tone[...]).write(to: take)
        await sources.addRecording(take)
        XCTAssertNil(sources.status, "the recording is added to the PDF piece")
        let recording = try XCTUnwrap(sources.layers.originals.first { $0.isRecording && !$0.isRemoved })
        XCTAssertNotNil(OriginalSounds.file(project: project, original: recording.id), "its sound is kept on this iPad")
        XCTAssertTrue(sources.canRecognise(recording), "«Распознать · Pro» on its row")
        XCTAssertNil(sources.layers.working, "adding it made no notes")
        let noHint = await sources.gridHint()
        XCTAssertNil(noHint, "no metre or tempo is sent for a piece with no notes")
        XCTAssertTrue(sources.layers.shownViews.contains { $0.id == PdfScreen.documentView }, "the PDF is a view")
        XCTAssertTrue(sources.layers.shownViews.contains { $0.id == recording.id && $0.kind == "performance" },
                      "and the recording, so the switch draws and its capsule plays it, as on the score")

        XCTAssertEqual(PdfScreen.reopening(hadNotes: false, hasNotes: true, page: 3), .tick(0), "the first notes open as notes")
        XCTAssertEqual(PdfScreen.reopening(hadNotes: false, hasNotes: false, page: 3), .page(3), "an Undo stays on the PDF")
        XCTAssertEqual(PdfScreen.reopening(hadNotes: true, hasNotes: true, page: 0), .page(1), "«Сделать рабочими» too")
    }

    /// A PDF piece with no notes yet lists in its switch only the PDF it shows and the recordings (v1.1, finding 29): photos
    /// or a second PDF added in Sources are no view there, so picking one never leaves a view pending for a later screen.
    @MainActor
    func testAPdfWithNoNotesListsOnlyItselfAndItsRecordings() {
        var layers = SourceLayers()
        layers.views = [
            SourceLayers.ViewEntry(id: "src_pdf", kind: "original", group: "src_pdf", working: false, hidden: false, op: "pdf"),
            SourceLayers.ViewEntry(id: "src_photos", kind: "original", group: "src_photos", working: false, hidden: false, op: "pdf"),
            SourceLayers.ViewEntry(id: "src_pdf_2", kind: "original", group: "src_pdf_2", working: false, hidden: false, op: "pdf"),
            SourceLayers.ViewEntry(id: "src_rec", kind: "performance", group: "src_rec", working: false, hidden: false, op: "recording"),
            SourceLayers.ViewEntry(id: "src_rec_2", kind: "performance", group: "src_rec_2", working: false, hidden: true, op: "recording"),
        ]
        let shown = layers.views(showing: PdfScreen.documentView)
        XCTAssertEqual(shown.views.map(\.id), ["src_pdf", "src_rec", "src_rec_2"])
        XCTAssertEqual(shown.shownViews.map(\.id), ["src_pdf", "src_rec"], "a recording the player hid stays hidden")
        XCTAssertEqual(layers.views.count, 5, "the piece's own layers are as music-core lists them")
    }

    /// Recognising a recording sends the working notes' metre and tempo as the grid's hint, the same `hint` music-core's
    /// playback gives (v0.10 s-3); a piece with no notes sends none (the PDF piece's test above).
    @MainActor
    func testARecordingIsHeardWithTheWorkingNotesMetre() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { MusicCore.close(piece: piece.id) }
        let sources = SourcesController()
        await sources.load(piece: piece.id, file: nil)
        XCTAssertNotNil(sources.layers.working)
        let hint = await sources.gridHint()
        let playback = await PieceCore(piece: piece.id).playback().value
        let direct = SourcesController.hint(playback)
        XCTAssertEqual(hint.map { NSDictionary(dictionary: $0) }, direct.map { NSDictionary(dictionary: $0) })
    }

    /// The panel before Send says what the heard notes become (v1.1, finding 29): beside the working notes, or, on a piece
    /// with none yet, the piece's own notes, which it opens on; never that working notes stay as they are when it has none.
    @MainActor
    func testTheRecognisePanelSaysWhatTheHeardNotesBecome() {
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            var layers = SourceLayers()
            XCTAssertEqual(SourcesRecognisePanel.hearLine(layers, text: text), text["sources.sheet.hearHostedFirst"], language)
            XCTAssertNotEqual(text["sources.sheet.hearHostedFirst"], "sources.sheet.hearHostedFirst", language)
            layers.working = "n_1"
            XCTAssertEqual(SourcesRecognisePanel.hearLine(layers, text: text), text["sources.sheet.hearHosted"], language)
        }
        XCTAssertTrue(UIText(language: "en")["sources.sheet.hearHostedFirst"].contains("become this piece's notes"))
        XCTAssertTrue(UIText(language: "ru")["sources.sheet.hearHostedFirst"].contains("станут нотами этой пьесы"))
    }

    /// The panel before Send says how long only when it can ask (v1.5 i-7, E-scan-5).
    @MainActor
    func testSourcesAsksHowLongOnlyForPagesWithNothingBlockingAndAnAddress() throws {
        // E-scan-5: no estimate for a recording, a blocked original, without «Четыре движка» or without an address.
        let address = try XCTUnwrap(URL(string: "https://scan.test/"))
        XCTAssertEqual(SourcesRecognisePanel.timedBy(jobs: true, recording: false, blocked: false, address: address), address)
        XCTAssertNil(SourcesRecognisePanel.timedBy(jobs: true, recording: true, blocked: false, address: address))
        XCTAssertNil(SourcesRecognisePanel.timedBy(jobs: true, recording: false, blocked: true, address: address))
        XCTAssertNil(SourcesRecognisePanel.timedBy(jobs: false, recording: false, blocked: false, address: address))
        XCTAssertNil(SourcesRecognisePanel.timedBy(jobs: true, recording: false, blocked: false, address: nil))
    }

    private func json(_ object: [String: Any]) -> String {
        String(decoding: (try? JSONSerialization.data(withJSONObject: object)) ?? Data("{}".utf8), as: UTF8.self)
    }
}
