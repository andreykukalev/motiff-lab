import XCTest
@testable import MotiffLab

/// Add piece: From a recording adds the recording as it is, free and on this iPad, as the one original of a new piece with
/// no notes (v1.1, finding 29); where the sound goes, and what hearing needs first, is said only when it is recognised in
/// Sources (v0.10 s-3).
@MainActor
final class AddPieceTests: XCTestCase {
    func testABuildWithoutMotiffLabsServerSaysSoWhenARecordingIsRecognised() throws {
        let text = UIText(language: "en")
        let blocked = try XCTUnwrap(RecordingChooser.blocker(Account(options: nil), text))

        XCTAssertEqual(blocked.words, text["version.recordingNoServer"])
        XCTAssertFalse(blocked.pro)
    }

    func testTheCardSaysTheRecordingStaysOnThisIPadInBothLanguages() {
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in ["library.fromRecording", "library.fromRecordingHint", "transcribe.signInPrompt", "transcribe.proPrompt"] {
                XCTAssertNotEqual(text[key], key, "\(language) \(key)")
            }
            XCTAssertFalse(text["library.fromRecordingHint"].contains("Motiff Lab"), "\(language): nothing is sent from the card")
        }
        XCTAssertTrue(UIText(language: "en")["library.fromRecordingHint"].contains("on this iPad"))
        XCTAssertTrue(UIText(language: "ru")["library.fromRecordingHint"].contains("на этом iPad"))
    }

    /// A piece made from a second of a tone picked as «Etude take.wav», as Add piece makes it; the picked file and the
    /// project file go after the test.
    private func recordingPiece() async throws -> URL {
        let tone = (0..<22_050).map { Int16(8_000 * sin(Double($0) * 2 * .pi * 440 / RecordingSound.sampleRate)) }
        let picked = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString).appendingPathComponent("Etude take.wav")
        try FileManager.default.createDirectory(at: picked.deletingLastPathComponent(), withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: picked.deletingLastPathComponent()) }
        try RecordingSound.wav(tone[...]).write(to: picked)
        let project = try await RecordingPiece.make(picked, text: UIText(language: "en")).get()
        addTeardownBlock { try? FileManager.default.removeItem(at: project) }
        return project
    }

    /// v1.1, finding 29: a recording picked on Add piece makes a piece whose one original is that recording, its sound
    /// kept on this iPad, with no notes, titled by the recording's name; it opens again as that recording.
    func testARecordingAddedMakesAPieceOfThatOneRecordingWithNoNotes() async throws {
        let project = try await recordingPiece()
        let read = await PieceSummary.read(project)
        let summary = try XCTUnwrap(read)
        let projectId = try XCTUnwrap(summary.id)
        defer { try? FileManager.default.removeItem(at: OriginalSounds.folder(project: projectId)) }

        XCTAssertFalse(summary.hasNotes)
        XCTAssertFalse(summary.document)
        XCTAssertEqual(summary.originals.map(\.kind), ["audio"])
        XCTAssertEqual(summary.originals.first?.kept, "device", "its sound is beside the project file, not in it")
        XCTAssertTrue(summary.isRecordingOnly)
        XCTAssertTrue(ProjectFiles.title(of: project.lastPathComponent).hasPrefix("Etude take"))
        let original = try XCTUnwrap(summary.originals.first?.id)
        XCTAssertNotNil(OriginalSounds.file(project: projectId, original: original), "its sound is kept on this iPad")

        let opened = try await OpenPiece.open(fileName: project.lastPathComponent, data: Data(contentsOf: project), language: "en").get()
        XCTAssertEqual(opened.recording, original, "it opens as that recording")
        XCTAssertFalse(opened.isDocument)
        _ = await MusicCore.run { MusicCore.close(piece: opened.id) }
    }

    /// v1.1, finding 29: a PDF added in Sources to a piece that is only its recording opens the piece again in its place,
    /// as music-core now holds it, a PDF piece, the toast handed to that screen.
    func testAPdfAddedToARecordingPieceOpensItAgainAsAPdfPiece() async throws {
        let project = try await recordingPiece()
        let piece = try await OpenPiece.open(fileName: project.lastPathComponent, data: Data(contentsOf: project), language: "en").get()
        XCTAssertNotNil(piece.recording)
        let sources = SourcesController()
        await sources.load(piece: piece.id, file: project)
        let projectId = try XCTUnwrap(sources.projectId)
        defer {
            MusicCore.close(piece: piece.id)
            try? FileManager.default.removeItem(at: OriginalSounds.folder(project: projectId))
        }
        var reopened = 0
        sources.reopen = { reopened += 1 }

        let pages = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842), format: UIGraphicsPDFRendererFormat()).pdfData { context in
            context.beginPage()
        }
        await sources.addPages(pages, fileName: "Etude.pdf", kind: "pdf")
        XCTAssertNil(sources.status, "the PDF is added")
        XCTAssertEqual(reopened, 1, "the piece opens again in its place")
        XCTAssertNil(sources.toast, "the toast is the next screen's")
        XCTAssertNotNil(SourcesHandover.toast(for: project))
        let again = try await OpenPiece.open(fileName: project.lastPathComponent, data: Data(contentsOf: project), language: "en").get()
        defer { MusicCore.close(piece: again.id) }
        XCTAssertTrue(again.isDocument, "as music-core now holds it: a PDF piece")
        XCTAssertNil(again.recording)
    }

    /// The fields `open` gets for a recording: its length when the iPad reads one, and this iPad as the device keeping it.
    func testARecordingIsOpenedWithItsLengthAndThisIPad() {
        let now = Date(timeIntervalSince1970: 0)
        let fields = RecordingPiece.request(OriginalSounds.Picked(fileName: "Etude.m4a", milliseconds: 61_000, data: nil), language: "ru", now: now)
        XCTAssertEqual(fields["language"] as? String, "ru")
        XCTAssertEqual(fields["device"] as? String, "ipad")
        XCTAssertEqual(fields["now"] as? String, "1970-01-01T00:00:00Z")
        XCTAssertEqual((fields["recording"] as? [String: Any])?["durationMs"] as? Int, 61_000)
        let unread = RecordingPiece.request(OriginalSounds.Picked(fileName: "Etude.m4a", milliseconds: nil, data: nil), language: "en", now: now)
        XCTAssertEqual((unread["recording"] as? [String: Any])?.isEmpty, true, "no length it cannot read")
        XCTAssertNil(unread["meter"])
        XCTAssertNil(unread["tempo"])
    }

    /// v1.1, finding 29: «Открыть файл» takes a JPEG or a PNG and makes the same piece of one page as one photo of a page,
    /// a PDF of images with no notes, titled by the file's name.
    func testAJpegOrPngOpenedMakesAPdfPieceOfOnePage() async throws {
        let text = UIText(language: "en")
        let image = UIGraphicsImageRenderer(size: CGSize(width: 60, height: 80)).image { context in
            UIColor.white.setFill()
            context.fill(CGRect(x: 0, y: 0, width: 60, height: 80))
        }
        for (name, data) in [("Prelude page.png", image.pngData()), ("Prelude page.jpg", image.jpegData(compressionQuality: 0.9))] {
            let picked = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString).appendingPathComponent(name)
            try FileManager.default.createDirectory(at: picked.deletingLastPathComponent(), withIntermediateDirectories: true)
            try XCTUnwrap(data).write(to: picked)
            defer { try? FileManager.default.removeItem(at: picked.deletingLastPathComponent()) }
            XCTAssertTrue(PhotoPiece.isImage(picked), name)

            let project = try await PhotoPiece.make(image: picked, text: text).get()
            defer { try? FileManager.default.removeItem(at: project) }
            let read = await PieceSummary.read(project)
            let summary = try XCTUnwrap(read, name)

            XCTAssertTrue(summary.document, name)
            XCTAssertFalse(summary.hasNotes, name)
            XCTAssertEqual(summary.originals.map(\.kind), ["images"], name)
            XCTAssertEqual(summary.originals.first?.pages, 1, name)
            XCTAssertTrue(ProjectFiles.title(of: project.lastPathComponent).hasPrefix("Prelude page"), name)
        }
    }

    func testOnlyAJpegOrPngIsMadeAPage() {
        XCTAssertFalse(PhotoPiece.isImage(URL(fileURLWithPath: "/tmp/Prelude.pdf")))
        XCTAssertFalse(PhotoPiece.isImage(URL(fileURLWithPath: "/tmp/Prelude.musicxml")))
        XCTAssertFalse(PhotoPiece.isImage(URL(fileURLWithPath: "/tmp/Prelude.heic")))
        XCTAssertTrue(PhotoPiece.isImage(URL(fileURLWithPath: "/tmp/Prelude.JPEG")))
    }
}
