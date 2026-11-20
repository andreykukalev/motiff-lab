import UIKit
import UniformTypeIdentifiers
import XCTest

@testable import MotiffLab

/// Adding a piece and a piece that is only its recording, as Andrei approved them for 1.1 (docs/ux-baseline.md, ADD-01,
/// ADD-02, VIEW-08, VIEW-09 and VIEW-10; his finding 29 and answers 2–4 to the plan of 1.1, 2026-11-19 21:51 UTC): pages
/// and recordings added on Add piece, a recording's waveform view, «Распознать · Pro» on it and its notes under it, and
/// Sources on a PDF with no notes yet. Taken through what Add piece, the library and the Sources sheet call, over the real
/// music-core, in Russian; the screens' look, the sound heard and the scan server are the device's.
@MainActor
final class AddPieceBaselineTests: XCTestCase {
    /// The pieces opened in music-core for a test, and what it left on the device.
    private var opened: [Int32] = []
    private var made: [URL] = []
    private let russian = UIText(language: "ru")

    /// Four quarter notes, C D E F, at 120 a minute: what the scan server answers for two seconds of a recording.
    private static let heardMidi = "TVRoZAAAAAYAAAABAeBNVHJrAAAALwD/UQMHoSAAkDxQg2CAPAAAkD5Qg2CAPgAAkEBQg2CAQAAAkEFQg2CAQQAA/y8A"

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
        for file in made { try? FileManager.default.removeItem(at: file) }
        made = []
    }

    /// A file the player picks in Files, under its own name.
    private func onDisk(_ name: String, _ data: Data) throws -> URL {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("add-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        made.append(folder)
        let file = folder.appendingPathComponent(name)
        try data.write(to: file)
        return file
    }

    /// A PDF of empty A4 pages.
    private func pages(_ count: Int) -> Data {
        UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842)).pdfData { context in
            for _ in 0..<count { context.beginPage() }
        }
    }

    /// A white picture, as a photo of a page.
    private func picture() -> UIImage {
        UIGraphicsImageRenderer(size: CGSize(width: 60, height: 80)).image { context in
            UIColor.white.setFill()
            context.fill(CGRect(x: 0, y: 0, width: 60, height: 80))
        }
    }

    /// Two seconds of a tone picked on Add piece → «Из записи», made a piece as the card makes it, and what the library
    /// reads of it; its project file and its sound kept on this iPad go after the test.
    private func recordingPiece(_ name: String) async throws -> (project: URL, summary: PieceSummary) {
        let tone = (0..<44_100).map { Int16(6_000 * sin(Double($0) * 2 * .pi * 330 / RecordingSound.sampleRate)) }
        let picked = try onDisk(name, RecordingSound.wav(tone[...]))
        let project = try await RecordingPiece.make(picked, text: russian).get()
        made.append(project)
        let read = await PieceSummary.read(project)
        let summary = try XCTUnwrap(read, "the library reads the new piece")
        if let id = summary.id { made.append(OriginalSounds.folder(project: id)) }
        return (project, summary)
    }

    /// The piece of a project file opened as the library opens it, closed when the test ends.
    private func reopened(_ project: URL) async throws -> OpenPiece {
        let piece = try await OpenPiece.open(fileName: project.lastPathComponent, data: Data(contentsOf: project), language: "ru").get()
        opened.append(piece.id)
        return piece
    }

    /// What the Sources sheet's rows take from it, with no scan server of Motiff Lab's chosen.
    private func sheetRow(showing current: String?) -> SourcesRow {
        SourcesRow(text: russian, current: current, address: nil, hosted: false, pick: { _ in }, delete: { _ in }, deleteNotes: { _ in })
    }

    /// ADD-01: «Открыть файл» takes a PDF, a JPEG or a PNG beside the files it reads, and «Фото страниц» photos of pages;
    /// each is a piece of its pages with no notes, made on this iPad, which opens on its pages and whose card says «PDF».
    /// A picture is one page; the photos are one PDF of them. A PDF and a picture are titled by the file's name, photos
    /// «Фото страниц, <date>» (PhotoReviewScreen, the device's).
    func testADD01PagesAddedArePiecesOfThosePagesWithNoNotes() async throws {
        // What AddPieceSheet's «Открыть файл» passes to its file importer, rebuilt here: the sheet names it nowhere else.
        let takes = LibraryView.openable + PhotoPiece.images
        for type in [UTType.pdf, .jpeg, .png] {
            XCTAssertTrue(takes.contains(type), "«Открыть файл» takes \(type.identifier)")
        }
        XCTAssertTrue(PhotoPiece.isImage(URL(fileURLWithPath: "/tmp/Страница.jpg")))
        XCTAssertFalse(PhotoPiece.isImage(URL(fileURLWithPath: "/tmp/Прелюдия.pdf")), "a PDF opens as it is")

        let pdf = try await OpenPiece.open(fileName: "Прелюдия.pdf", data: pages(2), language: "ru").get()
        opened.append(pdf.id)
        XCTAssertTrue(pdf.isDocument, "a PDF picked opens on its pages")
        XCTAssertNil(pdf.recording)
        XCTAssertEqual(pdf.title, "Прелюдия", "titled by its file's name")
        let own = try XCTUnwrap(pdf.own, "the library's summary of it")
        XCTAssertFalse(own.hasNotes)
        XCTAssertEqual(PieceCard.kind(own, russian), "PDF")

        let photo = try XCTUnwrap(PhotoImages.jpeg(picture()))
        let taken = PhotoImages.pdf([PhotoPage(photo: photo), PhotoPage(photo: photo)])
        let photos = try await PhotoPiece.make(pdf: taken, pages: 2, title: "Этюд", language: "ru").get()
        made.append(photos)
        let png = try onDisk("Страница.png", XCTUnwrap(picture().pngData()))
        let image = try await PhotoPiece.make(image: png, text: russian).get()
        made.append(image)
        XCTAssertTrue(ProjectFiles.title(of: image.lastPathComponent).hasPrefix("Страница"), "a picture is titled by its file's name")
        for (project, count, title) in [(photos, 2, "photos"), (image, 1, "Страница")] {
            let read = await PieceSummary.read(project)
            let summary = try XCTUnwrap(read, title)
            XCTAssertTrue(summary.document, title)
            XCTAssertFalse(summary.hasNotes, "\(title): nothing is recognised")
            XCTAssertEqual(summary.originals.map(\.kind), ["images"], title)
            XCTAssertEqual(summary.originals.first?.pages, count, title)
            XCTAssertEqual(PieceCard.kind(summary, russian), "PDF", title)
            let piece = try await reopened(project)
            XCTAssertTrue(piece.isDocument, "\(title) opens on its pages")
        }
    }

    /// ADD-02: «Из записи» on Add piece makes a new piece that is only that recording, as it is: no notes, its sound kept
    /// on this iPad (free, nothing sent), titled by the recording's name, its card «Запись», opened as the recording.
    func testADD02ARecordingAddedIsAPieceOfThatRecordingAlone() async throws {
        XCTAssertEqual(russian["library.fromRecording"], "Из записи")
        XCTAssertTrue(russian["library.fromRecordingHint"].contains("сохранится на этом iPad как есть"))
        let (project, summary) = try await recordingPiece("Этюд запись.wav")

        XCTAssertTrue(summary.isRecordingOnly)
        XCTAssertFalse(summary.hasNotes, "nothing is recognised")
        XCTAssertEqual(summary.originals.map(\.kind), ["audio"])
        XCTAssertEqual(summary.originals.first?.kept, "device", "its sound stays on this iPad")
        XCTAssertTrue(ProjectFiles.title(of: project.lastPathComponent).hasPrefix("Этюд запись"))
        XCTAssertEqual(PieceCard.kind(summary, russian), "Запись")
        let piece = try await reopened(project)
        XCTAssertEqual(piece.recording, summary.originals.first?.id, "it opens as that recording, on its own screen")
        XCTAssertFalse(piece.isDocument)
    }

    /// VIEW-08: a piece that is only its recording shows the recording's waveform, read on this iPad, with the play cursor
    /// where the sound is; a tap across it puts the sound at the moment under it, within the recording, and starts
    /// nothing. Its Contents say they come with the notes.
    func testVIEW08ARecordingPieceIsItsWaveformAndATapPutsTheSoundThere() async throws {
        let (project, _) = try await recordingPiece("Ноктюрн.wav")
        let piece = try await reopened(project)
        let projectId = try XCTUnwrap(piece.own?.id)
        let recording = try XCTUnwrap(piece.recording)
        let sound = try XCTUnwrap(OriginalSounds.file(project: projectId, original: recording), "the sound the screen plays")
        let read = await RecordingWave.read(sound)
        let wave = try XCTUnwrap(read, "the waveform is read on this iPad")
        XCTAssertEqual(wave.levels.count, 20, "how loud it is every 100 ms of its two seconds")
        XCTAssertEqual(wave.columns(40).count, 40, "drawn in columns across the screen")

        let listening = RecordingListening()
        listening.load(sound)
        XCTAssertEqual(listening.length, 2000)
        XCTAssertEqual(RecordingWave.share(of: listening.elapsed, length: listening.length), 0, "the cursor at the start")
        listening.seek(to: RecordingWave.moment(at: 0.25, length: listening.length))
        XCTAssertEqual(listening.elapsed, 500, "a tap a quarter across puts the sound a quarter in")
        XCTAssertEqual(RecordingWave.share(of: listening.elapsed, length: listening.length), 0.25, "and the cursor there")
        XCTAssertFalse(listening.playing, "the tap only moves it")
        listening.seek(to: RecordingWave.moment(at: 1.3, length: listening.length))
        XCTAssertEqual(listening.elapsed, 2000, "kept within the recording")
        XCTAssertEqual(russian["recording.noContents"], "Содержание появится вместе с нотами: распознайте их в «Источниках».")
        XCTAssertEqual(russian["recording.waveHint"], "Коснитесь места, чтобы слушать запись оттуда")
    }

    /// VIEW-09: in Sources the recording's row offers «Распознать · Pro», its sound going to Motiff Lab's scan server,
    /// whichever the player chose, with no metre or tempo, the piece having none; the panel says the notes heard become the
    /// piece's notes. Heard, they are its working notes, in the recording's card under it, the recording still a view; the
    /// piece opens again as notes.
    func testVIEW09ARecordingPieceIsRecognisedInSourcesAndItsNotesStandUnderIt() async throws {
        let (project, _) = try await recordingPiece("Вальс.wav")
        let piece = try await reopened(project)
        let sources = SourcesController()
        await sources.load(piece: piece.id, file: project)
        let recording = try XCTUnwrap(sources.layers.original(piece.recording))
        let row = sheetRow(showing: piece.recording)
        XCTAssertTrue(sources.canRecognise(recording), "«Распознать» on its row")
        XCTAssertTrue(row.isPro(recording), "· Pro, with no scan server of Motiff Lab's chosen too")
        XCTAssertEqual(russian["sources.sheet.recognise"], "Распознать")
        XCTAssertEqual(SourcesRecognisePanel.hearLine(sources.layers, text: russian), russian["sources.sheet.hearHostedFirst"])
        XCTAssertTrue(russian["sources.sheet.hearHostedFirst"].hasSuffix("Распознанные ноты станут нотами этой пьесы, и она откроется на них."))
        let hint = await sources.gridHint()
        XCTAssertNil(hint, "no metre or tempo is sent")

        let midi = try XCTUnwrap(Data(base64Encoded: Self.heardMidi))
        let heard = HeardRecording(engine: "basic-pitch", milliseconds: 2000, parts: [(index: 0, midi: midi)])
        let fields = SourcesController.hearFields(heard, original: recording.id, hint: nil, now: "2026-11-20T10:00:00Z")
        let answer = await sources.change("sources.notes.hear", fields)
        XCTAssertNotNil(answer, sources.status ?? "music-core keeps the notes heard")
        let working = try XCTUnwrap(sources.layers.working, "the notes heard are the piece's working notes")
        let group = try XCTUnwrap(sources.layers.groups.first)
        XCTAssertEqual(group.original.id, recording.id, "in the recording's card")
        XCTAssertEqual(group.notes.map(\.id), [working], "under it")
        XCTAssertEqual(sources.layers.notes(working)?.op, "audioRecognition")
        let views = sources.layers.shownViews
        XCTAssertTrue(views.contains { $0.id == recording.id && $0.kind == "performance" }, "the recording is still a view")
        let notesView = try XCTUnwrap(views.first { $0.id == working }, "and the notes are one")
        XCTAssertEqual(SourceLayers.word(notesView, russian), "Ноты", "the switch gets «Ноты»")
        XCTAssertEqual(SourcesRecognisePanel.hearLine(sources.layers, text: russian), russian["sources.sheet.hearHosted"], "heard again, beside them")
        let again = try await reopened(project)
        XCTAssertNil(again.recording, "the piece opens again as notes")
        XCTAssertFalse(again.isDocument)
    }

    /// VIEW-10: a PDF piece with no notes yet has Sources: its switch lists only the PDF and the recordings, its pages are
    /// read from the bar's «Распознать ноты», not here, and a recording added is kept on this iPad, not recognised, offered
    /// «Распознать · Pro» and listed in the switch. The first notes open the piece as notes; anything else stays on the PDF.
    func testVIEW10APdfWithNoNotesHasSourcesForItsRecordings() async throws {
        let piece = try await OpenPiece.open(fileName: "Сонатина.pdf", data: pages(1), language: "ru").get()
        opened.append(piece.id)
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("add-\(UUID().uuidString).motiff.json")
        made.append(file)
        let sources = SourcesController()
        await sources.load(piece: piece.id, file: file)
        let project = try XCTUnwrap(sources.projectId, "a PDF piece with no notes has Sources")
        made.append(OriginalSounds.folder(project: project))
        XCTAssertNil(sources.layers.working)
        let document = try XCTUnwrap(sources.layers.original(PdfScreen.documentView))
        XCTAssertFalse(sources.canRecognise(document), "its pages are read from the bar")
        XCTAssertEqual(sources.layers.views(showing: PdfScreen.documentView).shownViews.map(\.id), [PdfScreen.documentView])

        let tone = (0..<22_050).map { Int16(4_000 * sin(Double($0) * 2 * .pi * 262 / RecordingSound.sampleRate)) }
        let take = try onDisk("Урок.wav", RecordingSound.wav(tone[...]))
        await sources.addRecording(take)
        XCTAssertNil(sources.status, "the recording is added")
        let recording = try XCTUnwrap(sources.layers.originals.first { $0.isRecording && !$0.isRemoved })
        XCTAssertEqual(recording.kept, "device", "kept on this iPad")
        XCTAssertNil(sources.layers.working, "adding it recognised nothing")
        XCTAssertTrue(sources.canRecognise(recording))
        let row = sheetRow(showing: PdfScreen.documentView)
        XCTAssertTrue(row.isPro(recording), "«Распознать · Pro»")
        XCTAssertFalse(row.isPro(document), "the PDF's own scan server is the player's")
        let shown = sources.layers.views(showing: PdfScreen.documentView).shownViews.map(\.id)
        XCTAssertEqual(shown, [PdfScreen.documentView, recording.id], "the PDF and the recording, heard over it")
        XCTAssertEqual(PdfScreen.reopening(hadNotes: false, hasNotes: true, page: 1), .tick(0), "the first notes open as notes")
        XCTAssertEqual(PdfScreen.reopening(hadNotes: false, hasNotes: false, page: 1), .page(1), "anything else stays on the PDF")
    }
}
