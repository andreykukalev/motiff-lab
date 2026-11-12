import XCTest
@testable import MotiffLab

/// A piece that is only a PDF whose PDF is not in its project file (version 0.5): kept by link in a folder the player
/// imported, or synced from another device without it. It is listed, opens with the PDF read where this iPad links it
/// (the folder's bookmark and the path inside it), never takes the PDF's bytes into its file, and says plainly why when
/// the file is gone, changed, or out of reach.
final class AwayPiecesTests: XCTestCase {
    private var base: URL!
    private var music: URL!
    private var records: URL!
    private var links: URL!
    private let pdf = Data("%PDF-1.4\n% Motiff Lab test\n".utf8)

    override func setUpWithError() throws {
        base = FileManager.default.temporaryDirectory.appendingPathComponent("AwayPiecesTests-\(UUID().uuidString)", isDirectory: true)
        music = base.appendingPathComponent("Ноты", isDirectory: true)
        records = base.appendingPathComponent("Records", isDirectory: true)
        links = base.appendingPathComponent("Links", isDirectory: true)
        try FileManager.default.createDirectory(at: music.appendingPathComponent("Шопен/Баллады"), withIntermediateDirectories: true)
        try pdf.write(to: music.appendingPathComponent("Шопен/Баллады/Ballade No. 1.pdf"))
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: base)
    }

    /// A piece made by `library.link` for the PDF in the folder, its project file written, the folder and the link kept.
    private func linkedPiece() async throws -> (url: URL, summary: PieceSummary) {
        let folder = ImportFolder(id: "f1", name: "Ноты", provider: nil, bookmark: try FolderAccess.bookmark(of: music), addedAt: Date())
        ImportFolders.keep(folder, in: records)
        let made = await LibraryLink.create(fileName: "Ballade No. 1.pdf", folder: "Шопен/Баллады", sha256: CloudSync.hash(pdf), bytes: Int64(pdf.count), pages: 1)
        let file = try XCTUnwrap(made)
        let url = base.appendingPathComponent("Ballade.motiff.json")
        try Data(file.utf8).write(to: url)
        let read = await PieceSummary.read(url)
        let summary = try XCTUnwrap(read)
        let link = OriginalLink(bookmark: Data(), fileName: "Ballade No. 1.pdf", sha256: CloudSync.hash(pdf), linkedAt: Date(), folderId: "f1", path: "Шопен/Баллады/Ballade No. 1.pdf")
        OriginalLinks.save(link, project: try XCTUnwrap(summary.id), original: OriginalEntry.documentId, in: links)
        return (url, summary)
    }

    func testAPieceWhosePdfStaysInTheFolderIsListedAsAway() async throws {
        let piece = try await linkedPiece()

        XCTAssertTrue(AwayPdf.isAway(piece.summary))
        XCTAssertFalse(piece.summary.hasNotes)
        let original = try XCTUnwrap(piece.summary.originals.first)
        XCTAssertEqual(original.kept, "link")
        XCTAssertEqual(original.folder, "Шопен/Баллады")
        XCTAssertEqual(original.held, ["ipad"])
        XCTAssertFalse(original.inFile)
    }

    func testItOpensWithThePdfReadInTheFolderAndItsFileNeverTakesTheBytes() async throws {
        let piece = try await linkedPiece()

        let found = try await AwayPdf.read(piece.url, piece.summary, links: links, folders: records).get()
        XCTAssertEqual(found.data, pdf)
        let opened = await OpenPiece.open(fileName: piece.url.lastPathComponent, data: Data(found.project.utf8), language: "en", pdf: found.data)
        let open = try opened.get()
        XCTAssertTrue(open.isDocument)
        try await ProjectFiles.save(piece: open.id, to: piece.url)
        _ = await MusicCore.run { MusicCore.close(piece: open.id) }

        let reread = await PieceSummary.read(piece.url)
        let saved = try XCTUnwrap(reread)
        XCTAssertFalse(saved.document, "a linked PDF is never written into the project file")
        XCTAssertTrue(AwayPdf.isAway(saved))
    }

    func testAFileMovedAwayChangedOrInAFolderGoneIsSaidSo() async throws {
        let piece = try await linkedPiece()
        let file = music.appendingPathComponent("Шопен/Баллады/Ballade No. 1.pdf")

        try Data("%PDF-1.4\n% another edition\n".utf8).write(to: file)
        let changed = await AwayPdf.read(piece.url, piece.summary, links: links, folders: records)
        XCTAssertEqual(changed.failure, .changed)

        try FileManager.default.removeItem(at: file)
        let missing = await AwayPdf.read(piece.url, piece.summary, links: links, folders: records)
        XCTAssertEqual(missing.failure, .missing)

        let elsewhere = await AwayPdf.read(piece.url, piece.summary, links: base.appendingPathComponent("NoLinks"), folders: records)
        XCTAssertEqual(elsewhere.failure, .elsewhere, "no link on this iPad: the PDF is on another device")
    }

    func testAFolderWhoseBookmarkNoLongerOpensIsLostAndSaidSo() throws {
        let folder = ImportFolder(id: "f2", name: "Ноты", provider: "Google Drive", bookmark: Data([1, 2, 3]), addedAt: Date())

        let opened = FolderAccess.open(folder)

        XCTAssertNil(opened.url)
        XCTAssertEqual(opened.folder.problem, .lost)
        XCTAssertEqual(folder.place, "Google Drive › Ноты")
    }

    func testFoldersAreKeptOnThisIpadAndTheirProviderIsToldByWhereFilesKeepsThem() throws {
        let folder = ImportFolder(id: "f3", name: "Ноты", provider: "Google Drive", bookmark: Data([1]), addedAt: Date(timeIntervalSince1970: 1_000_000))
        ImportFolders.keep(folder, in: records)
        var again = folder
        again.checkedAt = Date(timeIntervalSince1970: 2_000_000)
        ImportFolders.keep(again, in: records)

        XCTAssertEqual(ImportFolders.load(in: records), [again])
        XCTAssertEqual(ImportFolders.provider(of: URL(fileURLWithPath: "/private/var/mobile/Containers/Shared/AppGroup/X/File Provider Storage/com.google.Drive/Ноты")), "Google Drive")
        XCTAssertEqual(ImportFolders.provider(of: URL(fileURLWithPath: "/private/var/mobile/Library/Mobile Documents/com~apple~CloudDocs/Ноты")), "iCloud Drive")
        XCTAssertNil(ImportFolders.provider(of: URL(fileURLWithPath: "/var/mobile/Ноты")))
    }

    func testACopyKeptOnThisIpadIsUsedOnlyWithTheSameFingerprint() throws {
        let kept = base.appendingPathComponent("Kept", isDirectory: true)
        KeptCopies.keep(pdf, project: "p_1", in: kept)

        XCTAssertEqual(KeptCopies.data(project: "p_1", sha256: CloudSync.hash(pdf), in: kept), pdf)
        XCTAssertNil(KeptCopies.data(project: "p_1", sha256: String(repeating: "0", count: 64), in: kept))
        KeptCopies.remove(project: "p_1", in: kept)
        XCTAssertFalse(KeptCopies.has(project: "p_1", in: kept))
    }

    func testTheWordsOfThePromptAreInBothLanguages() {
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in ["folderImport.missingTitle", "folderImport.missingLine", "folderImport.offlineTitle", "folderImport.recheck", "folderImport.setting"] {
                XCTAssertNotEqual(text[key], key, "\(language): \(key)")
            }
        }
        XCTAssertEqual(UIText(language: "ru")["folderImport.missingTitle"], "Файл не найден")
    }
}

private extension Result {
    var failure: Failure? {
        if case .failure(let failure) = self { return failure }
        return nil
    }
}
