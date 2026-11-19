import XCTest

@testable import MotiffLab

/// VIEW-07 (docs/ux-baseline.md, Andrei's «Оставить и беречь» in the test of 0.8): a recording added in Sources is kept
/// as an original of the piece without being recognised, beside its notes, as a view of its own («Исполнение», which
/// plays it from its own player), and it stays until the player deletes it. Taken through the Sources sheet's controller
/// as the sheet takes it («Добавить ещё» → a recording: `addRecording`; «Удалить только оригинал»: `remove`), over the
/// real music-core, with the piece saved to its project file and opened again from it as the library opens it. The
/// sound itself is the device's.
@MainActor
final class SourcesBaselineTests: XCTestCase {
    /// The pieces opened in music-core for a test, and what it left on the device.
    private var opened: [Int32] = []
    private var files: [URL] = []
    private var projects: [String] = []

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
        for file in files { try? FileManager.default.removeItem(at: file) }
        files = []
        for project in projects { try? FileManager.default.removeItem(at: OriginalSounds.folder(project: project)) }
        projects = []
    }

    /// A place in the temporary folder for a test, with nothing there yet.
    private func temporary(_ name: String) -> URL {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString)-\(name)")
        files.append(file)
        return file
    }

    /// The Sources sheet's controller on a piece opened in music-core, its sources read as the screen reads them.
    private func opening(_ piece: OpenPiece, file: URL) async throws -> SourcesController {
        if !opened.contains(piece.id) { opened.append(piece.id) }
        let sources = SourcesController()
        await sources.load(piece: piece.id, file: file)
        let project = try XCTUnwrap(sources.projectId, "music-core says the project's id")
        if !projects.contains(project) { projects.append(project) }
        return sources
    }

    func testVIEW07ARecordingAddedIsKeptAsAnOriginalUntilThePlayerDeletesIt() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let file = temporary("piece.motiff.json")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        let sources = try await opening(piece, file: file)
        let working = try XCTUnwrap(sources.layers.working, "the piece has its working notes")
        let notes = sources.layers.notes.map(\.id)

        // «Добавить ещё» → a recording: its bytes are only read, never heard by a server.
        let recording = temporary("take.wav")
        try Data([0, 1, 2, 3, 4, 5, 6, 7]).write(to: recording)
        await sources.addRecording(recording)
        XCTAssertNil(sources.status, "the recording is added")
        let added = try XCTUnwrap(sources.layers.originals.first { $0.isRecording && !$0.isRemoved }, "kept as an original of the piece")
        XCTAssertEqual(added.kept, "device", "its sound kept on this iPad, beside the project file")
        XCTAssertTrue(added.isHere)
        XCTAssertEqual(added.notes, [], "not recognised: no notes are made from it")
        XCTAssertEqual(sources.layers.notes.map(\.id), notes, "the piece's notes as they were")
        XCTAssertEqual(sources.layers.working, working, "the working notes as they were")
        XCTAssertTrue(sources.layers.shownViews.contains { $0.id == added.id && $0.kind == "performance" },
                      "a view of its own, «Исполнение», heard from its own player")
        let project = try XCTUnwrap(sources.projectId)
        XCTAssertNotNil(OriginalSounds.file(project: project, original: added.id), "its sound is kept")

        // It stays: the piece opened again from its project file, as the library opens it.
        let saved = try await OpenPiece.open(fileName: file.lastPathComponent, data: try Data(contentsOf: file), language: "en", title: example.title).get()
        let again = try await opening(saved, file: file)
        let kept = try XCTUnwrap(again.layers.original(added.id), "the recording stays in the piece")
        XCTAssertTrue(kept.isHere && kept.isRecording)
        XCTAssertEqual(kept.notes, [], "still not recognised")
        XCTAssertTrue(again.layers.shownViews.contains { $0.id == added.id && $0.kind == "performance" }, "and still heard as «Исполнение»")

        // Until the player deletes it.
        await again.remove(kept, withNotes: false)
        XCTAssertEqual(again.layers.original(added.id)?.isRemoved, true, "deleted, it is listed as deleted")
        XCTAssertFalse(again.layers.shownViews.contains { $0.id == added.id }, "and is no view any more")
        _ = try await opening(saved, file: file)
        XCTAssertNil(OriginalSounds.file(project: project, original: added.id), "its sound leaves this iPad as the piece opens again")
    }
}
