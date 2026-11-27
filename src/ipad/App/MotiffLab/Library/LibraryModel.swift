import Foundation

/// The library's model (W2.12, ADR 0031): the project files in the environment's folder and what music-core reads from
/// each, the shelf made from them (its pieces, composers, tags and playlists, the search and filters, the tab and the
/// composer or playlist picked on it), and the piece on screen. It is the one owner of opening a piece, whatever asks
/// (a line, Add piece, Files, the example, a piece saved online, a screen's PDF | Notes), and of deleting one. The screen
/// (`Workspace/LibraryView`) shows it and composes it with the account, the imports and the Reference.
@MainActor
final class LibraryModel: ObservableObject {
    /// Where the tab shown last is kept on the device.
    static let tabKey = "library.tab"

    @Published private(set) var projects: [ProjectFiles.Entry] = []
    @Published private(set) var summaries: [URL: PieceSummary] = [:]
    /// The pieces made from the project files and their summaries, with the library's composers, tags and playlists
    /// (version 0.2).
    @Published private(set) var entries: [LibraryEntry] = []
    @Published private(set) var composers: [String] = []
    @Published private(set) var tags: [String] = []
    @Published private(set) var playlists: [String] = []
    @Published var query = LibraryQuery()
    /// The tab shown last, kept on the device. Recent lists the last opened first; the other lists start from A.
    @Published var tab: LibraryTab {
        didSet {
            guard tab != oldValue else { return }
            defaults.set(tab.rawValue, forKey: Self.tabKey)
            picked = nil
            query.sort = tab == .recent ? .recent : .title
            query.ascending = true
        }
    }
    /// The composer or playlist picked on its tab.
    @Published var picked: LibraryGroup?
    /// The piece on screen; nil while the library shows (Back sets it so).
    @Published var piece: OpenPiece?
    /// A piece whose PDF could not be read where it lies (version 0.5), and what can be done.
    @Published var away: AwayPrompt?
    @Published private(set) var problem: String?
    /// A piece is being opened: the library waits for it.
    @Published private(set) var opening = false

    private let text: UIText
    private let folder: () -> URL
    private let defaults: UserDefaults
    /// How a project file is removed from the folder.
    private let remove: (URL) throws -> Void
    /// The library's recognition is told the file open on screen, so it never writes the notes under it.
    private let opened: @MainActor (URL?) -> Void
    private let summaryCache = ProjectFileCache<PieceSummary> { await PieceSummary.read($0) }
    private var reloads = 0

    init(
        text: UIText = .current,
        folder: @escaping () -> URL = { ProjectFiles.folder },
        defaults: UserDefaults = .standard,
        remove: @escaping (URL) throws -> Void = { try FileManager.default.removeItem(at: $0) },
        opened: @escaping @MainActor (URL?) -> Void = { _ in }
    ) {
        self.text = text
        self.folder = folder
        self.defaults = defaults
        self.remove = remove
        self.opened = opened
        tab = defaults.string(forKey: Self.tabKey).flatMap(LibraryTab.init(rawValue:)) ?? .all
    }

    /// A line under the library's header: why a step did not work, or nil.
    func showProblem(_ problem: String?) {
        self.problem = problem
    }

    // MARK: - The shelf

    /// The projects in the folder, and what music-core reads from each; only the newest reload writes what it read.
    func reload() async {
        reloads += 1
        let mine = reloads
        projects = ProjectFiles.list(in: folder())
        let read = await summaryCache.values(projects.map(\.url))
        guard mine == reloads else { return }
        summaries = read
        shelve()
    }

    /// The projects on the shelf: one card per piece. A PDF piece whose notes were read into a piece of their own between
    /// 2026-11-05 and 2026-11-07 is reached from those notes by PDF | Notes (testing item 16).
    private var shelved: [ProjectFiles.Entry] {
        let readFrom = Set(summaries.values.compactMap(\.readFrom))
        return projects.filter { entry in
            guard let summary = summaries[entry.url], !summary.hasNotes, let id = summary.id else { return true }
            return !readFrom.contains(id)
        }
    }

    /// The pieces made again from the project files and their summaries; a composer, tag or playlist no piece has any more
    /// stops filtering, and a composer or playlist picked on its tab that has no piece left goes back to the list of them.
    private func shelve() {
        let locale = Locale(identifier: text.language)
        entries = shelved.map { LibraryEntry.of($0, summaries[$0.url]) }
        composers = LibraryShelf.composers(entries, locale: locale)
        tags = LibraryShelf.tags(entries, locale: locale)
        playlists = LibraryShelf.playlists(entries, locale: locale)
        query = LibraryShelf.kept(query, composers: composers, tags: tags, playlists: playlists)
        if let shown = picked {
            let left = tab == .playlists ? LibraryShelf.pieces(on: shown.name ?? "", entries) : LibraryShelf.pieces(by: shown.name, entries)
            if left.isEmpty { picked = nil }
        }
    }

    /// Deletes a piece's project file and takes its line off the shelf: the one way a piece is deleted. A file that could
    /// not be removed keeps its line, and the library says why (one already gone is taken off as deleted).
    func delete(_ url: URL) {
        do {
            try remove(url)
        } catch {
            if FileManager.default.fileExists(atPath: url.path) {
                problem = error.localizedDescription
                return
            }
        }
        projects.removeAll { $0.url == url }
        summaries[url] = nil
        shelve()
    }

    /// A title or composer typed on a piece's line while choosing: written into the project file as the properties do.
    func rename(_ entry: LibraryEntry, _ field: String, to value: String) async {
        do {
            try await PieceDetailsFile.set(entry.url, field, to: LibraryShelf.clean(value))
            await reload()
        } catch {
            problem = (error as? ProjectError)?.message ?? error.localizedDescription
        }
    }

    // MARK: - Opening a piece

    /// One opening at a time, whatever asks (#582 item 2; the UI review's «three guards for one switch»): `opening` from
    /// the ask until the piece is on screen or the step has failed, and an ask meanwhile does nothing. The screens' own
    /// hand-over only keeps their save before they ask.
    private func openOnce(_ step: () async -> Void) async {
        guard !opening else { return }
        opening = true
        defer { opening = false }
        await step()
    }

    /// A file picked in Files (or Add piece's project file): one in the library's folder opens as that project; another is
    /// lent to the app only while it reads it, and the piece becomes a project file here.
    func open(_ result: Result<URL, Error>) async {
        await openOnce {
            switch result {
            case .failure(let error):
                problem = error.localizedDescription
            case .success(let url):
                if ProjectFiles.isInFolder(url, folder: folder()) {
                    await project(url)
                    return
                }
                let lent = url.startAccessingSecurityScopedResource()
                defer { if lent { url.stopAccessingSecurityScopedResource() } }
                do {
                    let data = try Data(contentsOf: url)
                    let language = text.language
                    let name = url.lastPathComponent
                    await show(saveNew: true) { await OpenPiece.open(fileName: name, data: data, language: language) }
                } catch {
                    problem = error.localizedDescription
                }
            }
        }
    }

    func openExample() async {
        guard let example = OpenPiece.example else { return }
        let language = text.language
        await openOnce {
            await show(saveNew: true) {
                await OpenPiece.open(fileName: example.fileName, data: example.data, language: language, title: example.title)
            }
        }
    }

    /// Get it here (#335): a project saved online downloaded as a new project file and opened, from then on an ordinary
    /// project on this iPad. A failed download says so in one line and leaves the library as it was.
    func openDownloaded(_ download: () async -> Result<URL, ProjectError>) async {
        await openOnce {
            problem = nil
            switch await download() {
            case .success(let url):
                await project(url)
            case .failure(let failure):
                problem = failure.message
            }
        }
    }

    /// Opens a project in the folder where it was left (`project`).
    func openProject(_ url: URL) async {
        await openOnce { await project(url) }
    }

    /// A piece that is only a PDF, opened with its PDF found where it lies (`showFound`).
    func openFound(_ url: URL, _ found: AwayFound, title: String?) async {
        await openOnce { await showFound(url, found, title: title) }
    }

    /// PDF | Notes (0023): the piece's other view replaces it, at the page or place given. The screen has saved the piece.
    /// Only the screen shown asks, and only once at a time (#582 item 2): a second press while the first was still opening
    /// opened the piece twice, the second time closing in music-core the piece the first had just put on screen.
    func switchView(from current: OpenPiece, to landing: Landing) async {
        guard piece?.id == current.id else { return }
        // A PDF kept in the piece since it was opened is in its own file: the PDF view of the same piece.
        guard let file = current.other?.file ?? current.file else { return }
        await openFile(file, at: landing)
    }

    /// Opens a project file in the folder in place of the piece shown, at a page or place: at a page, a piece with notes and
    /// a PDF inside it opens in its PDF view.
    func openFile(_ file: URL, at landing: Landing) async {
        await openOnce {
            opened(file)
            defer { opened(piece?.file) }
            do {
                let data = try Data(contentsOf: file)
                let language = text.language
                let asPdf = landing.page != nil
                await show(file: file, landing: landing) {
                    await OpenPiece.open(fileName: file.lastPathComponent, data: data, language: language, asPdf: asPdf)
                }
            } catch {
                problem = error.localizedDescription
            }
        }
    }

    /// Opens a project in the folder where it was left, titled as the player titled it in the library, if they did. A piece
    /// that is only a PDF whose PDF is not in its file (version 0.5) opens with the PDF read where this iPad links it, or
    /// says why it cannot and what can be done.
    private func project(_ url: URL) async {
        // Open for the batch before its file is read, so notes it writes meanwhile are not lost under the screen.
        opened(url)
        defer { opened(piece?.file) }
        if let summary = summaries[url], AwayPdf.isAway(summary) {
            switch await AwayPdf.read(url, summary) {
            case .success(let found): await showFound(url, found, title: summary.playersTitle)
            case .failure(let reason): away = AwayPrompt(url: url, summary: summary, title: entries.first { $0.url == url }?.title ?? ProjectFiles.title(of: url.lastPathComponent), reason: reason)
            }
            return
        }
        do {
            let data = try Data(contentsOf: url)
            let language = text.language
            let title = summaries[url]?.playersTitle
            await show(file: url) { await OpenPiece.open(fileName: url.lastPathComponent, data: data, language: language, title: title) }
        } catch {
            problem = error.localizedDescription
        }
    }

    /// A piece that is only a PDF, opened with its PDF found where it lies; saved to its own file, which never takes a
    /// linked PDF's bytes.
    private func showFound(_ url: URL, _ found: AwayFound, title: String?) async {
        let language = text.language
        await show(file: url) { await OpenPiece.open(fileName: url.lastPathComponent, data: Data(found.project.utf8), language: language, title: title, pdf: found.data) }
    }

    /// Opens a piece and shows its score: saved to its own project file, or as a new one in the folder named by the piece's
    /// title, the one its line shows (the player's, the score's, else the file's name; v1.5 r-2b), not by the file it came from.
    private func show(file: URL? = nil, saveNew: Bool = false, landing: Landing? = nil, _ opener: () async -> Result<OpenPiece, EngraveError>) async {
        problem = nil
        switch await opener() {
        case .success(var opened):
            if let file {
                opened.file = file
            } else if saveNew {
                let url = ProjectFiles.newURL(for: opened.title, in: folder())
                do {
                    try await ProjectFiles.save(piece: opened.id, to: url)
                    opened.file = url
                } catch {
                    problem = (error as? ProjectError)?.message ?? error.localizedDescription
                }
            }
            // In a screen's place (a landing) only that file is read again; every project is read when the library shows again.
            if landing != nil, let saved = opened.file { summaries[saved] = await PieceSummary.read(saved) } else { await reload() }
            if let saved = opened.file {
                opened.other = OtherView.of(saved, own: opened.own, in: summaries)
                opened.family = PieceFamily.of(saved, in: summaries)
            }
            opened.landing = ViewStates.landing(landing, for: opened)
            // Closed only as its screen gives way: before, it was closed while the files were read and its screen stayed.
            if let earlier = piece { await ClosedPieces.close(earlier.id) }
            piece = opened
        case .failure(let error):
            problem = error.message
        }
    }
}
