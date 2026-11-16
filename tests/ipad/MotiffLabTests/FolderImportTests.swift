import PDFKit
import UIKit
import XCTest
@testable import MotiffLab

/// «Импорт папки» (version 0.5): a folder is listed without reading it, each file read once for its fingerprint, pages and
/// a PDF's own Title and Author; music-core plans the import; the job makes a piece of each file ticked (a PDF kept by
/// link stays in the folder), is kept after each file and goes on after a relaunch; «Проверить папку» finds a moved file
/// again by its content. Nothing in the folder is ever written.
@MainActor
final class FolderImportTests: XCTestCase {
    private var base: URL!
    private var music: URL!
    private var records: URL!
    private var links: URL!
    private var projects: URL!

    override func setUp() async throws {
        base = FileManager.default.temporaryDirectory.appendingPathComponent("FolderImportTests-\(UUID().uuidString)", isDirectory: true)
        music = base.appendingPathComponent("Ноты", isDirectory: true)
        records = base.appendingPathComponent("Records", isDirectory: true)
        links = base.appendingPathComponent("Links", isDirectory: true)
        projects = base.appendingPathComponent("Projects", isDirectory: true)
        for folder in [music.appendingPathComponent("Шопен/Баллады"), music.appendingPathComponent(".hidden"), projects!] {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        }
    }

    override func tearDown() async throws {
        try? FileManager.default.removeItem(at: base)
    }

    /// A real one-page PDF with its own Title and Author.
    private func pdf(title: String, author: String, mark: String) -> Data {
        let format = UIGraphicsPDFRendererFormat()
        format.documentInfo = [kCGPDFContextTitle as String: title, kCGPDFContextAuthor as String: author]
        return UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842), format: format).pdfData { context in
            context.beginPage()
            (mark as NSString).draw(at: CGPoint(x: 72, y: 72), withAttributes: nil)
        }
    }

    /// The folder kept with its bookmark, as «Добавить папку» keeps it.
    private func keptFolder() throws -> ImportFolder {
        let folder = ImportFolder(id: "f1", name: "Ноты", provider: nil, bookmark: try FolderAccess.bookmark(of: music), addedAt: Date())
        ImportFolders.keep(folder, in: records)
        return folder
    }

    private func snapshot() throws -> [String: Data] {
        var files: [String: Data] = [:]
        let walker = try XCTUnwrap(FileManager.default.enumerator(at: music, includingPropertiesForKeys: nil))
        for case let url as URL in walker where !url.hasDirectoryPath {
            files[url.path] = try Data(contentsOf: url)
        }
        return files
    }

    func testTheFolderIsListedWithoutReadingItAndEachFileIsReadOnce() async throws {
        let ballade = pdf(title: "Ballade No. 1", author: "Chopin", mark: "1")
        try ballade.write(to: music.appendingPathComponent("Шопен/Баллады/Ballade No. 1.pdf"))
        try Data("MThd".utf8).write(to: music.appendingPathComponent("Шопен/Prelude.mid"))
        try Data("text".utf8).write(to: music.appendingPathComponent("notes.txt"))
        try Data("x".utf8).write(to: music.appendingPathComponent(".hidden/secret.pdf"))
        try Data("x".utf8).write(to: music.appendingPathComponent(".Nocturne.pdf.icloud"))

        let files = await FolderScan.list(music)

        XCTAssertEqual(files.map(\.path), ["Nocturne.pdf", "Шопен/Prelude.mid", "Шопен/Баллады/Ballade No. 1.pdf"].sorted())
        XCTAssertEqual(files.first { $0.path == "Nocturne.pdf" }?.bytes, 1, "a file only in the cloud: its size is known once it is read")
        let found = try XCTUnwrap(files.first { $0.isPdf && $0.folder == "Шопен/Баллады" })
        XCTAssertEqual(found.name, "Ballade No. 1.pdf")
        let read = try await FolderScan.read(found, in: music).get()
        XCTAssertEqual(read.facts.sha256, CloudSync.hash(ballade))
        XCTAssertEqual(read.facts.pages, 1)
        XCTAssertEqual(read.facts.pdfTitle, "Ballade No. 1")
        XCTAssertEqual(read.facts.pdfAuthor, "Chopin")
        XCTAssertTrue(FolderScan.facts(Data("%PDF-1.4 broken".utf8), name: "x.pdf").unreadable)
    }

    func testMusicCorePlansTheFolderAndTheReviewAddsTheFilesTickedAsTheyStand() async throws {
        let ballade = pdf(title: "", author: "", mark: "1")
        try ballade.write(to: music.appendingPathComponent("Шопен/Баллады/Ballade No. 1.pdf"))
        try ballade.write(to: music.appendingPathComponent("Ballade again.pdf"))
        try pdf(title: "", author: "", mark: "2").write(to: music.appendingPathComponent("Шопен/Berceuse.pdf"))
        let folder = try keptFolder()
        let review = FolderReview(folder: folder, mode: .first, linkable: true, pieces: [:], root: records, links: links)

        await review.start()

        XCTAssertNil(review.problem)
        XCTAssertEqual(review.plan.counts.found, 3)
        XCTAssertEqual(review.plan.counts.duplicates, 1, "the same file twice is one piece")
        XCTAssertEqual(review.plan.counts.new, 2)
        review.filter = .duplicates
        XCTAssertEqual(review.shown.count, 1)
        let berceuse = try XCTUnwrap(review.plan.files.first { $0.path == "Шопен/Berceuse.pdf" })
        XCTAssertEqual(berceuse.title?.value, "Berceuse")
        XCTAssertEqual(berceuse.title?.from, "fileName")
        XCTAssertTrue(berceuse.playlists.contains("Шопен"), "its folder becomes a playlist")

        review.edits[berceuse.path] = SuggestionEdit(title: "Berceuse, op. 57", composer: "  ")
        let ticked = review.toAdd.first { $0.path != berceuse.path }
        review.unchecked.insert(try XCTUnwrap(ticked).path)
        let job = review.job()
        XCTAssertEqual(job.items.map(\.path), [berceuse.path])
        XCTAssertEqual(job.items.first?.title, "Berceuse, op. 57")
        XCTAssertNil(job.items.first?.composer, "an emptied field is no composer")
        XCTAssertTrue(job.linked)
    }

    func testTheJobMakesLinkedPiecesKeepsItselfAndNeverWritesIntoTheFolder() async throws {
        let ballade = pdf(title: "", author: "", mark: "1")
        try ballade.write(to: music.appendingPathComponent("Шопен/Баллады/Ballade No. 1.pdf"))
        try Data("%PDF-1.4 broken".utf8).write(to: music.appendingPathComponent("Broken.pdf"))
        let folder = try keptFolder()
        let before = try snapshot()
        let runner = ImportRunner()
        runner.root = records
        runner.links = links
        runner.projects = { self.projects }
        let items = [
            ImportItem(path: "Шопен/Баллады/Ballade No. 1.pdf", title: "Ballade No. 1", composer: "Шопен", playlists: ["Баллады"]),
            ImportItem(path: "Broken.pdf"),
            ImportItem(path: "Gone.pdf"),
        ]

        runner.start(ImportJob(folderId: folder.id, place: folder.place, linked: true, items: items))
        for _ in 0..<200 where runner.job?.finished != true { try await Task.sleep(for: .milliseconds(50)) }

        let job = try XCTUnwrap(runner.job)
        XCTAssertEqual(job.items.map(\.state), [.added, .failed, .failed])
        XCTAssertEqual(job.items[1].reason, "folderImport.failUnreadable")
        XCTAssertEqual(job.items[2].reason, "folderImport.failMissing")
        XCTAssertEqual(ImportJobs.load(in: records)?.items, job.items, "kept after each file")
        let url = projects.appendingPathComponent(try XCTUnwrap(job.items[0].file))
        let read = await PieceSummary.read(url)
        let summary = try XCTUnwrap(read)
        XCTAssertEqual(summary.title, "Ballade No. 1")
        XCTAssertEqual(summary.composer, "Шопен")
        XCTAssertEqual(summary.playlists, ["Баллады"])
        XCTAssertTrue(AwayPdf.isAway(summary), "a linked PDF stays in the folder, not in the file")
        XCTAssertEqual(summary.originals.first?.folder, "Шопен/Баллады")
        let link = OriginalLinks.load(project: try XCTUnwrap(summary.id), original: OriginalEntry.documentId, in: links)
        XCTAssertEqual(link?.folderId, folder.id)
        XCTAssertEqual(link?.path, "Шопен/Баллады/Ballade No. 1.pdf")
        XCTAssertEqual(try snapshot(), before, "the folder is only read")
    }

    func testAJobLeftUnfinishedWaitsForContinueAfterARelaunch() {
        var job = ImportJob(folderId: "f1", place: "Ноты", linked: true, items: [ImportItem(path: "a.pdf", state: .added), ImportItem(path: "b.pdf")])
        job.startedAt = Date(timeIntervalSince1970: 1_000_000)
        ImportJobs.save(job, in: records)
        let runner = ImportRunner()
        runner.root = records

        runner.restore()

        XCTAssertEqual(runner.job, job)
        XCTAssertTrue(runner.interrupted)
        runner.stop()
        XCTAssertNil(ImportJobs.load(in: records))
    }

    func testACheckFindsAMovedFileAgainByItsContentAndLinksItWithNoQuestion() async throws {
        let ballade = pdf(title: "", author: "", mark: "1")
        try ballade.write(to: music.appendingPathComponent("Шопен/Баллады/Ballade No. 1.pdf"))
        let folder = try keptFolder()
        let made = await LibraryLink.create(fileName: "Ballade No. 1.pdf", folder: "Шопен/Баллады", sha256: CloudSync.hash(ballade), bytes: Int64(ballade.count), pages: 1)
        let url = projects.appendingPathComponent("Ballade.motiff.json")
        try Data(try XCTUnwrap(made).utf8).write(to: url)
        let first = await PieceSummary.read(url)
        let summary = try XCTUnwrap(first)
        let link = OriginalLink(bookmark: Data(), fileName: "Ballade No. 1.pdf", sha256: CloudSync.hash(ballade), linkedAt: Date(), folderId: folder.id, path: "Шопен/Баллады/Ballade No. 1.pdf")
        OriginalLinks.save(link, project: try XCTUnwrap(summary.id), original: OriginalEntry.documentId, in: links)
        try FileManager.default.createDirectory(at: music.appendingPathComponent("Концерт"), withIntermediateDirectories: true)
        try FileManager.default.moveItem(at: music.appendingPathComponent("Шопен/Баллады/Ballade No. 1.pdf"), to: music.appendingPathComponent("Концерт/Баллада 1.pdf"))

        let review = FolderReview(folder: folder, mode: .recheck, linkable: true, pieces: [url: summary], target: url, root: records, links: links)
        await review.start()

        XCTAssertEqual(review.found, 1)
        XCTAssertEqual(review.reopen, url)
        XCTAssertTrue(review.proposals.isEmpty)
        let again = await PieceSummary.read(url)
        XCTAssertEqual(again?.originals.first?.folder, "Концерт")
        XCTAssertEqual(again?.originals.first?.fileName, "Баллада 1.pdf")
        XCTAssertEqual(OriginalLinks.load(project: try XCTUnwrap(summary.id), original: OriginalEntry.documentId, in: links)?.path, "Концерт/Баллада 1.pdf")
        let relinked = try XCTUnwrap(again)
        let opened = await AwayPdf.read(url, relinked, links: links, folders: records)
        XCTAssertEqual(try opened.get().data, ballade)
    }

    func testAFolderPickedWhoseBookmarkDoesNotOpenIsStillReadThisLaunchAsCopies() async throws {
        try pdf(title: "", author: "", mark: "1").write(to: music.appendingPathComponent("Berceuse.pdf"))
        let folder = ImportFolder(id: "f9", name: "Ноты", provider: "Google Drive", bookmark: Data([1, 2, 3]), addedAt: Date())
        ImportFolders.keep(folder, in: records)
        LentFolders.shared.hold(music, for: folder.id)

        let listed = await FolderAccess.within(folder, root: records) { url in await FolderScan.list(url) }

        XCTAssertEqual(try listed.get().map(\.path), ["Berceuse.pdf"])
        XCTAssertEqual(ImportFolders.folder("f9", in: records)?.problem, .lost, "the lost bookmark is still said")
    }

    @MainActor
    func testSelectAllTicksEveryNewFileShownOrNone() {
        let shown = [["path": "a.pdf", "status": "new"], ["path": "b.pdf", "status": "new"], ["path": "c.pdf", "status": "duplicate"]].compactMap(PlannedFile.init)

        XCTAssertEqual(FolderReview.ticking(["b.pdf", "x.pdf"], shown: shown), ["x.pdf"], "one unticked: all ticked")
        XCTAssertEqual(FolderReview.ticking(["x.pdf"], shown: shown), ["a.pdf", "b.pdf", "x.pdf"], "all ticked: none")
    }

    func testEachFileIsListedOnceByPath() {
        let files = [
            FolderFile(path: "Шопен/Ballade.pdf", bytes: 1, modified: nil),
            FolderFile(path: "Bach.pdf", bytes: 10, modified: nil),
            FolderFile(path: "Шопен/Ballade.pdf", bytes: 5000, modified: nil),
        ]

        XCTAssertEqual(FolderScan.unique(files).map { "\($0.path) \($0.bytes)" }, ["Bach.pdf 10", "Шопен/Ballade.pdf 5000"])
    }

    func testEveryWordTheImportNamesIsInBothLanguages() {
        var keys = FoundFilter.allCases.map { FoundFilter.key + $0.rawValue } + ["folderImport.filter.changed"]
        keys += FolderMapping.allCases.map { "folderImport.folders." + $0.rawValue }
        keys += ["fileName", "folder", "scoreHeader", "folderChoice", "player"].map { "folderImport.from." + $0 }
        keys += ["waiting", "added", "failed", "skipped"].map { "folderImport.state." + $0 }
        keys += ["offline", "folderLost"].map { "folderImport.paused." + $0 }
        keys += ["failDamaged", "failMissing", "failOffline", "failUnreadable", "skippedSame", "folderLostShort", "offlineTitle"].map { "folderImport." + $0 }
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in keys { XCTAssertNotEqual(text[key], key, "\(language): \(key)") }
        }
        XCTAssertEqual(UIText(language: "ru").count("folderImport.addPieces", 3), "Добавить 3 пьесы")
        XCTAssertEqual(UIText(language: "en").count("folderImport.addPieces", 1), "Add 1 piece")
    }
}
