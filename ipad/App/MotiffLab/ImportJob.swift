import Foundation
import PDFKit

/// One file of an import job: where it is in the folder, what its piece is to be (the suggestions as the player left them
/// on «Что нашлось»), and what came of it.
struct ImportItem: Codable, Equatable, Identifiable, Sendable {
    enum State: String, Codable, Sendable {
        case waiting, added, failed, skipped
    }

    var path: String
    var title: String?
    var composer: String?
    var playlists: [String] = []
    var tags: [String] = []
    var state = State.waiting
    /// Why it was not added, as a key of the interface's words or music-core's own message.
    var reason: String?
    /// The project file it became, by name in the library's folder.
    var file: String?

    var id: String { path }
    var name: String { path.split(separator: "/").last.map(String.init) ?? path }
}

/// The import of a folder (version 0.5, «Импорт папки»): the files to add, one at a time so playing is never disturbed,
/// kept in Application Support after each file, so it can be paused, stopped, and carried on after a relaunch from the
/// file it stopped at; what was added stays added.
struct ImportJob: Codable, Equatable, Sendable {
    var id = UUID().uuidString
    var folderId: String
    /// «Google Drive › Ноты».
    var place: String
    /// PDFs stay in the folder, linked; else every file is copied into its piece on the iPad.
    var linked: Bool
    /// The PDFs added go to «Распознать ноты» as they are added (the player agreed to it for the batch).
    var recognise = false
    var items: [ImportItem]
    /// Paused by the player, or by the app with `stopped` saying why.
    var paused = false
    /// "offline" or "folderLost" when the app paused it.
    var stopped: String?
    var startedAt = Date()

    var done: Int { items.filter { $0.state != .waiting }.count }
    var added: Int { items.filter { $0.state == .added }.count }
    var failed: Int { items.filter { $0.state == .failed }.count }
    var finished: Bool { !items.contains { $0.state == .waiting } }
}

/// The job and what this iPad remembers of the files it imported, beside the folders: `job.json`, and `known.json`, the
/// fingerprint of each imported file by project, for the files whose piece does not keep it (MIDI and MusicXML).
enum ImportJobs {
    static func load(in root: URL = ImportFolders.root) -> ImportJob? {
        guard let data = try? Data(contentsOf: root.appendingPathComponent("job.json")) else { return nil }
        return try? ImportFolders.decoder.decode(ImportJob.self, from: data)
    }

    static func save(_ job: ImportJob?, in root: URL = ImportFolders.root) {
        let url = root.appendingPathComponent("job.json")
        guard let job else {
            try? FileManager.default.removeItem(at: url)
            return
        }
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        if let data = try? ImportFolders.encoder.encode(job) { try? data.write(to: url, options: .atomic) }
    }

    static func known(in root: URL = ImportFolders.root) -> [String: String] {
        guard let data = try? Data(contentsOf: root.appendingPathComponent("known.json")) else { return [:] }
        return (try? JSONDecoder().decode([String: String].self, from: data)) ?? [:]
    }

    static func remember(_ sha256: String, project: String, in root: URL = ImportFolders.root) {
        var known = known(in: root)
        known[project] = sha256
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(known) { try? data.write(to: root.appendingPathComponent("known.json"), options: .atomic) }
    }
}

/// Makes one piece of a file read from the folder, the same way «Открыть файл» makes one: a PDF kept by link becomes a
/// project file that describes it (`library.linked`), anything else is opened by music-core and saved with its bytes; the
/// suggestions the player kept are confirmed into its metadata (`library.confirm`), and the link to the file is kept on
/// this iPad. Nothing is written into the folder.
enum PieceMaker {
    struct Made: Sendable {
        let url: URL
        let project: String
        let sha256: String
    }

    static func make(_ data: Data, item: ImportItem, folder: ImportFolder, in root: URL, linked: Bool, projects: URL, links: URL) async -> Result<Made, ProjectError> {
        let file = FolderFile(path: item.path, bytes: Int64(data.count), modified: nil)
        let sha256 = CloudSync.hash(data)
        let link = linked && file.isPdf
        let made: String?
        if link {
            made = await linkedFile(data, file, sha256)
        } else {
            made = await copied(data, name: file.name)
        }
        guard let made else { return .failure(ProjectError(message: "folderImport.failDamaged")) }
        guard let confirmed = await FolderPlanning.confirm(made, title: item.title, composer: item.composer, tags: item.tags, playlists: item.playlists) else {
            return .failure(ProjectError(message: "folderImport.failDamaged"))
        }
        let url = ProjectFiles.newURL(for: item.title ?? ProjectFiles.title(of: file.name), in: projects)
        do {
            try Data(confirmed.file.utf8).write(to: url, options: .atomic)
        } catch {
            return .failure(ProjectError(message: error.localizedDescription))
        }
        if link {
            let place = FolderAccess.file(item.path, in: root)
            let bookmark = (try? LinkedFile.bookmark(of: place)) ?? Data()
            let kept = OriginalLink(bookmark: bookmark, fileName: file.name, sha256: sha256, bytes: Int64(data.count), modified: LinkedFile.attributes(of: place).modified, linkedAt: Date(), folderId: folder.id, path: item.path)
            OriginalLinks.save(kept, project: confirmed.project, original: OriginalEntry.documentId, in: links)
        }
        return .success(Made(url: url, project: confirmed.project, sha256: sha256))
    }

    /// A project file describing a PDF that stays in the folder.
    private static func linkedFile(_ data: Data, _ file: FolderFile, _ sha256: String) async -> String? {
        let pages = PDFDocument(data: data)?.pageCount
        return await LibraryLink.create(fileName: file.name, folder: file.folder, sha256: sha256, bytes: Int64(data.count), pages: pages)
    }

    /// A project file with the file's bytes, as «Открыть файл» saves one.
    private static func copied(_ data: Data, name: String) async -> String? {
        await MusicCore.run {
            guard let opened = facadeObject(MusicCore.open(fileName: name, data: data, request: nil)),
                  let piece = (opened["piece"] as? NSNumber)?.int32Value, piece > 0 else { return nil }
            defer { MusicCore.close(piece: piece) }
            let saved = MusicCore.call(piece: piece, "project.save", nil)
            guard let object = facadeObject(saved), object["error"] == nil else { return nil }
            return saved
        }
    }
}

/// Runs the import job in the background of the library (version 0.5, «Импорт папки»): one file at a time inside the
/// folder's security scope, the job saved after each, the library told now and then to read the new pieces. A file that
/// cannot be added gets its reason and the rest go on; no connection three times in a row, or a folder iOS no longer lets
/// the app into, pauses the job with the reason said.
@MainActor
final class ImportRunner: ObservableObject {
    static let shared = ImportRunner()

    @Published private(set) var job: ImportJob?
    @Published private(set) var running = false
    /// Grows as pieces are added (every few) and when a run ends, so the library reads its pieces again.
    @Published private(set) var revision = 0
    /// The piece added last, by file, for «Распознать ноты» when the batch asked for it.
    var added: (@MainActor (URL) -> Void)?
    var root = ImportFolders.root
    var links = OriginalLinks.folder
    var projects: () -> URL = { ProjectFiles.folder }
    private var restored = false

    /// A job left unfinished when the app was closed, waiting for the player's «Продолжить».
    var interrupted: Bool { job.map { !$0.finished && !$0.paused } == true && !running }

    /// The job kept on this iPad, once per launch: an unfinished one waits for «Продолжить».
    func restore() {
        guard !restored else { return }
        restored = true
        job = ImportJobs.load(in: root)
    }

    func start(_ job: ImportJob) {
        self.job = job
        ImportJobs.save(job, in: root)
        Task { await run() }
    }

    func pause() {
        job?.paused = true
        ImportJobs.save(job, in: root)
    }

    func resume() {
        job?.paused = false
        job?.stopped = nil
        ImportJobs.save(job, in: root)
        Task { await run() }
    }

    /// Stops the job: the files not added yet are not added; the pieces added stay.
    func stop() {
        job = nil
        ImportJobs.save(nil, in: root)
        revision += 1
    }

    private func run() async {
        guard !running, let current = job, let folder = ImportFolders.folder(current.folderId, in: root) else { return }
        running = true
        let result = await FolderAccess.within(folder, root: root) { url in await self.addAll(from: url, folder: folder) }
        running = false
        if case .failure(let problem) = result, job?.id == current.id {
            job?.paused = true
            job?.stopped = problem == .offline ? "offline" : "folderLost"
            ImportJobs.save(job, in: root)
        }
        revision += 1
    }

    private func addAll(from url: URL, folder: ImportFolder) async {
        var offline = 0
        var shas = Set(ImportJobs.known(in: root).values)
        while let current = job, !current.paused, let index = current.items.firstIndex(where: { $0.state == .waiting }) {
            let item = await add(current.items[index], from: url, folder: folder, linked: current.linked, shas: &shas)
            guard job?.id == current.id, job?.items.indices.contains(index) == true else { return }
            job?.items[index] = item
            offline = item.reason == "folderImport.failOffline" ? offline + 1 : 0
            if offline >= 3 {
                job?.paused = true
                job?.stopped = "offline"
            }
            ImportJobs.save(job, in: root)
            if item.state == .added, (job?.added ?? 0) % 10 == 1 { revision += 1 }
            if item.state == .added, current.recognise, let file = item.file { added?(projects().appendingPathComponent(file)) }
        }
    }

    /// One file read and made a piece; a file with a fingerprint added already (in this job or before) is the same piece.
    private func add(_ waiting: ImportItem, from url: URL, folder: ImportFolder, linked: Bool, shas: inout Set<String>) async -> ImportItem {
        var item = waiting
        let file = FolderFile(path: item.path, bytes: 0, modified: nil)
        switch await FolderScan.read(file, in: url) {
        case .failure(let problem):
            item.state = .failed
            item.reason = Self.reason(problem)
        case .success(let read):
            if let sha256 = read.facts.sha256, shas.contains(sha256) {
                item.state = .skipped
                item.reason = "folderImport.skippedSame"
            } else if read.facts.unreadable {
                item.state = .failed
                item.reason = "folderImport.failUnreadable"
            } else {
                switch await PieceMaker.make(read.data, item: item, folder: folder, in: url, linked: linked, projects: projects(), links: links) {
                case .failure(let problem):
                    item.state = .failed
                    item.reason = problem.message
                case .success(let made):
                    item.state = .added
                    item.file = made.url.lastPathComponent
                    shas.insert(made.sha256)
                    ImportJobs.remember(made.sha256, project: made.project, in: root)
                }
            }
        }
        return item
    }

    static func reason(_ problem: LinkProblem) -> String {
        switch problem {
        case .offline: "folderImport.failOffline"
        case .missing: "folderImport.failMissing"
        case .unreadable: "folderImport.failUnreadable"
        }
    }
}
