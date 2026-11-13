import Foundation
import PDFKit

/// «Похоже, это та же пьеса» (Andrei, 2026-11-12 17:47 UTC): a linked piece whose file is not found by its content, and a
/// file in the folder that is possibly it, by name, pages and size. Offered, never linked by itself.
struct SameProposal: Identifiable, Equatable, Sendable {
    let project: String
    let url: URL
    let title: String
    let composer: String?
    let pages: Int?
    let bytes: Int64?
    let candidate: FolderFile
    var id: String { project + "\n" + candidate.path }
}

/// What the player typed over a suggestion on «Что нашлось».
struct SuggestionEdit: Equatable, Sendable {
    var title: String?
    var composer: String?
}

/// The counters on «Что нашлось», each a filter of the list.
enum FoundFilter: String, CaseIterable, Sendable {
    case found, new, inLibrary, duplicates, unsuitable

    /// Its counter's words, "folderImport.filter.new" with {0} for the count.
    static let key = "folderImport.filter."
}

/// «Что нашлось» and «Проверить папку» (version 0.5, the approved screens import-ipad-2-found.png and
/// import-web-8-recheck.png): the folder listed at once, the plan made by music-core, then each file read in turn (its
/// fingerprint, a PDF's pages, Title and Author), the plan made again as they come, so the counters grow as the player
/// looks. A check of a folder imported before first finds the library's linked files again (`library.relink`): moved or
/// renamed by its content, with no question; a file whose content changed or is gone, offered as «Похоже, это та же
/// пьеса». Only reads the folder.
@MainActor
final class FolderReview: ObservableObject, Identifiable {
    enum Mode: Sendable { case first, recheck }

    let id = UUID()
    private(set) var folder: ImportFolder
    let mode: Mode
    /// Whether the folder's bookmark opened again at once: else the pieces can only be copies on the iPad.
    let linkable: Bool
    @Published private(set) var files: [FolderFile] = []
    @Published private(set) var facts: [String: FileFacts] = [:]
    @Published private(set) var plan = FolderPlan()
    @Published private(set) var listing = true
    @Published private(set) var read = 0
    @Published private(set) var problem: String?
    @Published var filter = FoundFilter.found
    @Published var folders = FolderMapping.playlists { didSet { Task { await replan() } } }
    @Published var recognise = false
    @Published var linked: Bool { didSet { Task { await replan() } } }
    @Published var edits: [String: SuggestionEdit] = [:]
    @Published var unchecked: Set<String> = []
    /// On a check: the library's files found again, and those that may be the same piece.
    @Published private(set) var found = 0
    @Published private(set) var proposals: [SameProposal] = []
    /// On a check from a piece that could not be opened: that piece, once it is found again.
    @Published private(set) var reopen: URL?
    private let pieces: [URL: PieceSummary]
    private let target: URL?
    private let root: URL
    private let links: URL

    init(folder: ImportFolder, mode: Mode, linkable: Bool, pieces: [URL: PieceSummary], target: URL? = nil, root: URL = ImportFolders.root, links: URL = OriginalLinks.folder) {
        self.folder = folder
        self.mode = mode
        self.linkable = linkable
        self.pieces = pieces
        self.target = target
        self.root = root
        self.links = links
        linked = linkable && (folder.linked ?? true)
        filter = mode == .recheck ? .new : .found
    }

    /// The files of the list under the filter chosen.
    var shown: [PlannedFile] {
        switch filter {
        case .found: plan.files
        case .new: plan.files.filter(\.isNew)
        case .inLibrary: plan.files.filter { $0.status == "inLibrary" }
        case .duplicates: plan.files.filter { $0.status == "duplicate" }
        case .unsuitable: plan.files.filter(\.isUnsuitable)
        }
    }

    /// Whether every new file the filter shows is ticked, the state of «Выбрать все».
    var allShownTicked: Bool { shown.filter(\.isNew).allSatisfy { !unchecked.contains($0.path) } }

    /// «Выбрать все»: ticks every new file the filter shows, or unticks them all when all are ticked.
    func tickAllShown() { unchecked = Self.ticking(unchecked, shown: shown) }

    /// The files left unticked once «Выбрать все» is pressed over the files shown.
    static func ticking(_ unchecked: Set<String>, shown: [PlannedFile]) -> Set<String> {
        let new = Set(shown.filter(\.isNew).map(\.path))
        return new.isDisjoint(with: unchecked) ? unchecked.union(new) : unchecked.subtracting(new)
    }

    /// The new files the player left ticked: what «Добавить N пьес» adds.
    var toAdd: [PlannedFile] { plan.files.filter { $0.isNew && !unchecked.contains($0.path) } }

    /// The pages of the PDFs to add, as read so far.
    var pagesToAdd: Int { toAdd.reduce(0) { $0 + (facts[$1.path]?.pages ?? 0) } }

    /// Lists the folder, plans, finds the library's files again on a check, and reads the files one by one.
    func start() async {
        let result = await FolderAccess.within(folder, root: root) { url in await self.look(in: url) }
        if case .failure(let problem) = result { self.problem = problem == .offline ? "folderImport.offlineTitle" : "folderImport.folderLostShort" }
        listing = false
        folder.checkedAt = Date()
        if var kept = ImportFolders.folder(folder.id, in: root) {
            kept.checkedAt = folder.checkedAt
            ImportFolders.keep(kept, in: root)
        }
    }

    private func look(in url: URL) async {
        files = await FolderScan.list(url)
        listing = false
        await replan()
        if mode == .recheck { await relink(in: url) }
        await readAll(in: url)
    }

    /// Each file not read yet, the ones the plan needs first, the plan made again every few files and at the end.
    private func readAll(in url: URL) async {
        let first = Set(plan.fingerprint)
        let order = files.filter { first.contains($0.path) } + files.filter { !first.contains($0.path) && facts[$0.path] == nil }
        for (index, file) in order.enumerated() {
            if Task.isCancelled { return }
            await readOne(file, in: url)
            if index % 20 == 19 { await replan() }
        }
        await replan()
    }

    private func readOne(_ file: FolderFile, in url: URL) async {
        guard facts[file.path] == nil else { return }
        switch await FolderScan.read(file, in: url) {
        case .success(let got):
            facts[file.path] = got.facts
            if let index = files.firstIndex(where: { $0.path == file.path }) { files[index].bytes = Int64(got.data.count) }
        case .failure(let problem):
            if problem != .offline { facts[file.path] = FileFacts(unreadable: true) }
        }
        read += 1
    }

    func replan() async {
        let library = FolderPlanning.library(Array(pieces.values), known: ImportJobs.known(in: root))
        if let next = await FolderPlanning.plan(files, facts: facts, library: library, folders: folders, linked: linked && linkable) {
            plan = next
        }
    }

    /// The import job for the files ticked, with the suggestions as the player left them.
    func job() -> ImportJob {
        let items = toAdd.map { file in
            let edit = edits[file.path]
            return ImportItem(
                path: file.path,
                title: Self.chosen(edit?.title, file.title?.value),
                composer: Self.chosen(edit?.composer, file.composer?.value),
                playlists: file.playlists,
                tags: file.tags
            )
        }
        return ImportJob(folderId: folder.id, place: folder.place, linked: linked && linkable, recognise: recognise, items: items)
    }

    /// What the player typed, an empty field for none, else the suggestion.
    private static func chosen(_ typed: String?, _ suggested: String?) -> String? {
        guard let typed else { return suggested }
        let trimmed = typed.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }
}

/// «Проверить папку»: the library's files from this folder found again.
extension FolderReview {
    /// The linked PDFs of the library: those with this iPad's link to this folder, and those described in a folder with no
    /// link here (linked on another device), which this folder may hold too.
    private var linkedHere: [URL: PieceSummary] {
        pieces.filter { _, summary in
            guard let project = summary.id, let original = summary.originals.first(where: \.isDocument), original.kept == "link" else { return false }
            let link = OriginalLinks.load(project: project, original: original.id, in: links)
            return link?.folderId == folder.id || (link == nil && original.folder != nil)
        }
    }

    fileprivate func relink(in url: URL) async {
        let candidates = linkedHere
        guard !candidates.isEmpty else { return }
        let library = FolderPlanning.library(Array(candidates.values)).filter { $0["original"] as? String == OriginalEntry.documentId }
        var answer = await FolderPlanning.relink(library, files: files, facts: facts)
        var rounds = 0
        while let pending = answer?.fingerprint, !pending.isEmpty, rounds < 3 {
            for path in pending { if let file = files.first(where: { $0.path == path }) { await readOne(file, in: url) } }
            answer = await FolderPlanning.relink(library, files: files, facts: facts)
            rounds += 1
        }
        guard let originals = answer?.originals else { return }
        for relinked in originals {
            guard let piece = candidates.first(where: { $0.value.id == relinked.project }) else { continue }
            if relinked.isFound, let path = relinked.path {
                await link(piece.key, piece.value, to: path, in: url)
            } else if let path = relinked.path ?? relinked.candidates.first, let file = files.first(where: { $0.path == path }) {
                proposals.append(SameProposal(
                    project: relinked.project, url: piece.key, title: piece.value.title ?? ProjectFiles.title(of: piece.key.lastPathComponent),
                    composer: piece.value.composer, pages: piece.value.originals.first(where: \.isDocument)?.pages,
                    bytes: piece.value.originals.first(where: \.isDocument)?.bytes, candidate: file
                ))
            }
        }
    }

    /// Links a piece's PDF to the file at `path`: this iPad's link, and, when its place, name or fingerprint changed, the
    /// piece's description (`library.link`).
    private func link(_ url: URL, _ summary: PieceSummary, to path: String, in folderURL: URL) async {
        guard let project = summary.id, let original = summary.originals.first(where: \.isDocument) else { return }
        let file = files.first { $0.path == path } ?? FolderFile(path: path, bytes: original.bytes ?? 0, modified: nil)
        if facts[path]?.sha256 == nil { await readOne(file, in: folderURL) }
        guard let sha256 = facts[path]?.sha256 else { return }
        let place = FolderAccess.file(path, in: folderURL)
        let kept = OriginalLink(
            bookmark: (try? LinkedFile.bookmark(of: place)) ?? Data(), fileName: file.name, sha256: sha256,
            bytes: facts[path] == nil ? nil : file.bytes, modified: file.modified, linkedAt: Date(), folderId: folder.id, path: path
        )
        let changed: (sha256: String, bytes: Int64, pages: Int?)? = original.sha256 == sha256 ? nil : (sha256, file.bytes, facts[path]?.pages)
        if original.fileName != file.name || (original.folder ?? "") != file.folder || changed != nil, let text = try? String(contentsOf: url, encoding: .utf8) {
            guard let moved = await LibraryLink.move(text, folder: file.folder, fileName: file.name, changed: changed) else { return }
            try? Data(moved.utf8).write(to: url, options: .atomic)
        }
        OriginalLinks.save(kept, project: project, original: original.id, in: links)
        found += 1
        unchecked.insert(path)
        if url == target { reopen = url }
    }

    /// «Это она: открывать этот файл»: the piece's PDF is that file from now on; its notes, marks and changes stay.
    func accept(_ proposal: SameProposal) async {
        if let summary = pieces[proposal.url] {
            _ = await FolderAccess.within(folder, root: root) { url in
                await self.link(proposal.url, summary, to: proposal.candidate.path, in: url)
            }
        }
        proposals.removeAll { $0.id == proposal.id }
        await replan()
    }

    /// «Нет, добавить отдельной пьесой»: the file stays among the new ones.
    func decline(_ proposal: SameProposal) {
        proposals.removeAll { $0.id == proposal.id }
    }
}
