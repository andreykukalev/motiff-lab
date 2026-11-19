import XCTest
@testable import MotiffLab

/// Add piece (v0.5.6, #533 section 8): From a recording is a card that picks the recording on a tap, with no switch to let
/// the sound go; what hearing needs first is said on the card in place of where the sound goes.
@MainActor
final class AddPieceTests: XCTestCase {
    func testABuildWithoutMotiffLabsServerSaysSoOnTheCardAndItWaits() throws {
        let text = UIText(language: "en")
        let blocked = try XCTUnwrap(RecordingChooser.blocker(Account(options: nil), text))

        XCTAssertEqual(blocked.words, text["version.recordingNoServer"])
        XCTAssertFalse(blocked.pro)
    }

    func testTheCardSaysWhereTheSoundGoesInBothLanguages() {
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in ["library.fromRecording", "library.fromRecordingHint", "library.fromRecordingLineDevice", "transcribe.choose", "transcribe.signInPrompt", "transcribe.proPrompt"] {
                XCTAssertNotEqual(text[key], key, "\(language) \(key)")
            }
        }
        XCTAssertTrue(UIText(language: "en")["library.fromRecordingLineDevice"].contains("Motiff Lab's scan server"))
    }

    func testAWayInCardTakesAProTagAndASecondLine() {
        let card = AddRoute(icon: "mic", title: "From a recording", hint: "Audio or video", pro: true, line: "Where the sound goes")
        XCTAssertTrue(card.pro)
        XCTAssertEqual(card.line, "Where the sound goes")
        XCTAssertNil(AddRoute(icon: "file", title: "Open a file", hint: "MIDI, MusicXML or PDF").line)
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
            let summary = try await XCTUnwrap(PieceSummary.read(project), name)

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
