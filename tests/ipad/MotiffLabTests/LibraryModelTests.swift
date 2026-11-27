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

    /// What the library's recognition was told is on screen, in order.
    private var onScreen: [URL?] = []

    private func model(remove: @escaping (URL) throws -> Void = { try FileManager.default.removeItem(at: $0) }) -> LibraryModel {
        let folder = folder!
        return LibraryModel(text: UIText(language: "en"), folder: { folder }, defaults: defaults, remove: remove) { [weak self] file in
            self?.onScreen.append(file)
        }
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
        XCTAssertEqual(ProjectFiles.list(in: folder).map(\.url.lastPathComponent), ["Für Elise.motiff.json"], "saved in the library's folder, named by the work's title (ADD-03)")
        XCTAssertEqual(file.lastPathComponent, "Für Elise.motiff.json")
        XCTAssertEqual(library.entries.map(\.title), ["Für Elise"])
        XCTAssertFalse(library.opening)

        // Only the screen shown asks for its other view.
        let other = OpenPiece(id: piece.id + 1000, title: "", pages: [], timemap: "")
        await library.switchView(from: other, to: .kept)
        XCTAssertEqual(library.piece?.id, piece.id, "a screen no longer shown switches nothing")

        library.piece = nil
        library.delete(try XCTUnwrap(library.entries.first?.url))
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path))
        XCTAssertTrue(library.entries.isEmpty)
        XCTAssertTrue(library.projects.isEmpty)
        XCTAssertTrue(library.summaries.isEmpty)
    }

    func testAFailedDownloadSaysWhyAndLeavesTheLibraryAsItWas() async {
        let library = model()
        await library.openDownloaded { .failure(ProjectError(message: "No connection")) }
        XCTAssertEqual(library.problem, "No connection")
        XCTAssertFalse(library.opening)
        XCTAssertNil(library.piece)
    }

    /// Robustness (v1.6 i-11b): a project saved online that came but could not be read left the library waiting for good,
    /// its spinner over a screen that took no tap; now it says why and the library answers again.
    func testADownloadedFileThatCannotBeReadSaysWhyAndLeavesTheLibraryUsable() async {
        let library = model()
        let missing = folder.appendingPathComponent("Gone.motiff.json")
        await library.openDownloaded { .success(missing) }
        XCTAssertNotNil(library.problem, "why it did not open")
        XCTAssertFalse(library.opening, "the library answers taps again")
        XCTAssertNil(library.piece)
    }

    /// Robustness (v1.6 i-11b, the UI review's «three guards for one switch»): one opening at a time whatever asks: a
    /// second ask while a piece is being opened (here a file from Files while a copy online downloads) does nothing,
    /// where it opened a second piece over the first.
    func testASecondOpenWhileOneIsUnderWayDoesNothing() async throws {
        let library = model()
        let picked = try pickedFile()
        await library.openDownloaded {
            XCTAssertTrue(library.opening)
            await library.open(.success(picked))
            return .failure(ProjectError(message: "No connection"))
        }
        if let piece = library.piece { opened.append(piece.id) }
        XCTAssertNil(library.piece, "nothing opened meanwhile")
        XCTAssertTrue(ProjectFiles.list(in: folder).isEmpty, "and nothing saved")
        XCTAssertFalse(library.opening)
    }

    /// Robustness (v1.6 i-11b): a project file that could not be removed went off the list and came back at the next
    /// reading, as if deleted; now its line stays and the library says why. One already gone is taken off as deleted.
    func testAPieceWhoseFileCannotBeRemovedKeepsItsLineAndSaysWhy() async throws {
        let library = model { _ in throw CocoaError(.fileWriteNoPermission) }
        await library.open(.success(try pickedFile()))
        let piece = try XCTUnwrap(library.piece, library.problem ?? "not opened")
        opened.append(piece.id)
        library.piece = nil
        let file = try XCTUnwrap(library.entries.first?.url)

        library.delete(file)
        XCTAssertTrue(FileManager.default.fileExists(atPath: file.path))
        XCTAssertEqual(library.entries.map(\.url), [file], "the piece is still here, and so is its line")
        XCTAssertEqual(library.problem, UIText(language: "en").format("library.deleteFailed", "Für Elise"), "why, in the interface's language")

        try FileManager.default.removeItem(at: file)
        library.delete(file)
        XCTAssertTrue(library.entries.isEmpty, "a file gone already is taken off")
    }

    /// The library's recognition is told the piece on screen by the model alone, so it never writes the notes under it:
    /// the file opened, nothing once Back leaves it, and the piece still shown when another could not be opened.
    func testTheRecognitionIsToldThePieceOnScreen() async throws {
        let library = model()
        await library.open(.success(try pickedFile()))
        let piece = try XCTUnwrap(library.piece, library.problem ?? "not opened")
        opened.append(piece.id)
        XCTAssertEqual(onScreen.last, piece.file, "the piece opened")

        await library.openProject(folder.appendingPathComponent("Gone.motiff.json"))
        XCTAssertEqual(onScreen.last, piece.file, "not opened: the piece still on screen")

        library.piece = nil
        XCTAssertEqual(onScreen.last, .some(nil), "Back: none")
    }
}
