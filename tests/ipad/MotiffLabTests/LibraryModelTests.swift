import XCTest
@testable import MotiffLab

/// The library's model (W2.12, v1.6 i-11b): the shelf made from the project files of its folder, the tab kept on the
/// device with the order it starts, and the one owner of opening, switching and deleting a piece.
@MainActor
final class LibraryModelTests: XCTestCase {
    private var folder: URL!
    private var defaults: UserDefaults!
    /// The pieces opened in music-core here, closed again at the end.
    private var opened: [Int32] = []

    override func setUp() async throws {
        folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defaults = try XCTUnwrap(UserDefaults(suiteName: "LibraryModelTests"))
        defaults.removePersistentDomain(forName: "LibraryModelTests")
    }

    override func tearDown() async throws {
        for piece in opened { _ = await MusicCore.run { MusicCore.close(piece: piece) } }
        try? FileManager.default.removeItem(at: folder)
        defaults.removePersistentDomain(forName: "LibraryModelTests")
    }

    private func model() -> LibraryModel {
        let folder = folder!
        return LibraryModel(text: UIText(language: "en"), folder: { folder }, defaults: defaults)
    }

    /// The kit's Für Elise as a file picked in Files, outside the library's folder.
    private func pickedFile() throws -> URL {
        let example = try XCTUnwrap(KitPiece.example)
        let picked = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString)-\(example.fileName)")
        try example.data.write(to: picked)
        addTeardownBlock { try? FileManager.default.removeItem(at: picked) }
        return picked
    }

    func testAnotherTabStartsItsOwnOrderForgetsTheGroupAndIsKeptOnTheDevice() {
        let library = model()
        XCTAssertEqual(library.tab, .all, "nothing kept yet")
        library.picked = LibraryGroup(name: "Bach", count: 2)
        library.query = library.query.sorted(by: .composer).sorted(by: .composer)
        XCTAssertFalse(library.query.ascending)

        library.tab = .recent
        XCTAssertNil(library.picked, "the group picked belongs to the tab before")
        XCTAssertEqual(library.query.sort, .recent, "Recent lists the last opened first")
        XCTAssertTrue(library.query.ascending)
        XCTAssertEqual(defaults.string(forKey: LibraryModel.tabKey), "recent")
        XCTAssertEqual(model().tab, .recent, "the tab shown last comes back")

        library.tab = .composers
        XCTAssertEqual(library.query.sort, .title, "the other lists start from A")
        library.picked = LibraryGroup(name: "Bach", count: 2)
        library.tab = .composers
        XCTAssertEqual(library.picked?.name, "Bach", "the same tab again changes nothing")
    }

    func testAPieceFromFilesBecomesAProjectFileOnTheShelfAndIsDeletedFromIt() async throws {
        let library = model()
        await library.open(.success(try pickedFile()))
        let piece = try XCTUnwrap(library.piece, library.problem ?? "not opened")
        opened.append(piece.id)
        let file = try XCTUnwrap(piece.file)
        XCTAssertEqual(file.deletingLastPathComponent().standardizedFileURL, folder.standardizedFileURL, "saved in the library's folder")
        XCTAssertEqual(file.lastPathComponent, "Für Elise.motiff.json", "named by the work's title (ADD-03)")
        XCTAssertEqual(library.entries.map(\.title), ["Für Elise"])
        XCTAssertFalse(library.opening)

        // Only the screen shown asks for its other view.
        let other = OpenPiece(id: piece.id + 1000, title: "", pages: [], timemap: "")
        await library.switchView(from: other, to: .kept)
        XCTAssertEqual(library.piece?.id, piece.id, "a screen no longer shown switches nothing")

        library.piece = nil
        library.delete(file)
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path))
        XCTAssertTrue(library.entries.isEmpty)
        XCTAssertTrue(library.projects.isEmpty)
        XCTAssertNil(library.summaries[file])
    }

    func testAFailedDownloadSaysWhyAndLeavesTheLibraryAsItWas() async {
        let library = model()
        await library.openDownloaded { .failure(ProjectError(message: "No connection")) }
        XCTAssertEqual(library.problem, "No connection")
        XCTAssertFalse(library.opening)
        XCTAssertNil(library.piece)
    }
}
