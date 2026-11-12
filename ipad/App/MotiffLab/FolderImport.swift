import SwiftUI
import UniformTypeIdentifiers

/// «Импорт папки» in the library (version 0.5, behind `experimental.libraryImport`): the folders the player imported, the
/// review of one («Что нашлось» or «Проверить папку»), whether iOS still lets the app into each, and «Держать на этом
/// iPad» for a playlist. Only reads the player's folders.
@MainActor
final class FolderImportModel: ObservableObject {
    @Published private(set) var folders: [ImportFolder] = []
    @Published var review: FolderReview?
    /// A folder to pick again in Files because iOS no longer lets the app into it.
    @Published var lost: ImportFolder?
    @Published var problem: String?
    /// The playlists kept on this iPad, and the one whose copies are being read now with how many of how many.
    @Published private(set) var kept: Set<String> = []
    @Published private(set) var keeping: (playlist: String, done: Int, of: Int)?
    var root = ImportFolders.root
    var links = OriginalLinks.folder

    func load() {
        folders = ImportFolders.load(in: root)
        kept = KeptCopies.playlists(in: root)
    }

    /// Whether iOS still opens each folder's bookmark, written on the folder for the menu to say (a lost Google Drive
    /// folder is said, never silently left).
    func checkAccess() async {
        let root = root
        await Task.detached(priority: .utility) {
            for folder in ImportFolders.load(in: root) {
                let opened = FolderAccess.open(folder)
                if opened.folder != folder { ImportFolders.keep(opened.folder, in: root) }
            }
        }.value
        load()
    }

    /// A folder picked in Files: kept with its bookmark, then looked through. A folder picked before (or picked again for
    /// one iOS no longer opened) is checked rather than imported twice. When the bookmark does not open again at once, the
    /// pieces can only be copies on the iPad, and the review says so.
    func picked(_ result: Result<URL, Error>, pieces: [URL: PieceSummary], replacing: ImportFolder? = nil) {
        problem = nil
        guard case .success(let url) = result else {
            if case .failure(let error) = result { problem = error.localizedDescription }
            return
        }
        let lent = url.startAccessingSecurityScopedResource()
        defer { if lent { url.stopAccessingSecurityScopedResource() } }
        let earlier = replacing ?? same(as: url)
        var folder = earlier ?? ImportFolder(id: UUID().uuidString, name: url.lastPathComponent, provider: ImportFolders.provider(of: url), bookmark: Data(), addedAt: Date())
        folder.bookmark = (try? FolderAccess.bookmark(of: url)) ?? Data()
        folder.name = url.lastPathComponent
        let opened = FolderAccess.open(folder)
        folder = opened.folder
        if opened.url == nil && folder.problem == nil { folder.problem = .lost }
        ImportFolders.keep(folder, in: root)
        LentFolders.shared.hold(url, for: folder.id)
        load()
        review = FolderReview(folder: folder, mode: earlier == nil ? .first : .recheck, linkable: opened.url != nil, pieces: pieces, root: root, links: links)
    }

    /// «Проверить папку»: the folder read again, from the library's menu or from a piece whose file was not found.
    func recheck(_ folder: ImportFolder, pieces: [URL: PieceSummary], target: URL? = nil) {
        problem = nil
        let opened = FolderAccess.open(folder)
        if opened.folder != folder { ImportFolders.keep(opened.folder, in: root) }
        load()
        let linkable = opened.url != nil
        guard linkable || LentFolders.shared.url(folder.id) != nil else {
            lost = opened.folder
            return
        }
        review = FolderReview(folder: opened.folder, mode: .recheck, linkable: linkable, pieces: pieces, target: target, root: root, links: links)
    }

    /// The folder a piece's PDF came from on this iPad, for «Проверить папку» from that piece.
    func folder(of summary: PieceSummary) -> ImportFolder? {
        guard let project = summary.id, let original = summary.originals.first(where: \.isDocument),
              let link = OriginalLinks.load(project: project, original: original.id, in: links) else { return folders.count == 1 ? folders.first : nil }
        return folders.first { $0.id == link.folderId }
    }

    /// The import of the files the player left ticked starts in the background of the library.
    func add(_ job: ImportJob) {
        review = nil
        guard !job.items.isEmpty else { return }
        if var folder = ImportFolders.folder(job.folderId, in: root), folder.linked == nil {
            folder.linked = job.linked
            ImportFolders.keep(folder, in: root)
            load()
        }
        ImportRunner.shared.start(job)
    }

    /// «Держать на этом iPad» on or off for a playlist: on, a copy of each of its linked PDFs is read now from its folder
    /// and kept here, for when there is no connection; off, the copies go.
    func keep(_ playlist: String, _ on: Bool, pieces: [URL: PieceSummary]) async {
        var names = kept
        if on { names.insert(playlist) } else { names.remove(playlist) }
        KeptCopies.setPlaylists(names, in: root)
        kept = names
        let linked = pieces.values.filter { $0.playlists.contains(playlist) && AwayPdf.isAway($0) }
        guard on else {
            let stillKept = Set(pieces.values.filter { $0.playlists.contains { names.contains($0) } }.compactMap(\.id))
            for summary in linked { if let id = summary.id, !stillKept.contains(id) { KeptCopies.remove(project: id) } }
            return
        }
        keeping = (playlist, 0, linked.count)
        for (index, summary) in linked.enumerated() {
            if let project = summary.id, let original = summary.originals.first(where: \.isDocument),
               let link = OriginalLinks.load(project: project, original: original.id, in: links), !KeptCopies.has(project: project),
               case .success(let data) = await AwayPdf.bytes(link, folders: root), CloudSync.hash(data) == (original.sha256 ?? link.sha256) {
                KeptCopies.keep(data, project: project)
            }
            keeping = (playlist, index + 1, linked.count)
        }
        keeping = nil
    }

    /// Runs `step` once the sheet closing now has gone: a sheet does not open while another is closing.
    func later(_ step: @escaping @MainActor () -> Void) {
        Task {
            try? await Task.sleep(for: .milliseconds(450))
            step()
        }
    }

    /// A folder kept here that is the one at `url`, by where its bookmark leads.
    private func same(as url: URL) -> ImportFolder? {
        let path = url.standardizedFileURL.resolvingSymlinksInPath().path
        return folders.first { folder in
            var stale = false
            guard let place = try? URL(resolvingBookmarkData: folder.bookmark, options: [], relativeTo: nil, bookmarkDataIsStale: &stale) else { return false }
            return place.standardizedFileURL.resolvingSymlinksInPath().path == path
        }
    }
}

/// The folder import's sheets over the library: «Что нашлось» (or the check of a folder) and a folder to pick again;
/// and, at launch, the job left unfinished and the folders' access.
@MainActor
struct FolderImportLayer: ViewModifier {
    @ObservedObject var model: FolderImportModel
    @ObservedObject var runner = ImportRunner.shared
    let text: UIText
    let summaries: [URL: PieceSummary]
    let on: Bool
    @ObservedObject var account: Account
    /// Reads the library again: pieces were added, or a piece was linked again.
    let reload: () async -> Void
    /// Opens a piece found again by «Проверить папку».
    let open: (URL) async -> Void

    func body(content: Content) -> some View {
        content
            .sheet(item: $model.review) { review in
                FolderFoundSheet(review: review, text: text, offer: RecognitionOffer.of(account)) { job in
                    model.add(job)
                } open: { url in
                    model.review = nil
                    Task { await reload(); await open(url) }
                }
                .onDisappear { Task { await reload() } }
            }
            .sheet(item: $model.lost) { folder in
                FolderLostSheet(folder: folder, text: text) { result in
                    model.lost = nil
                    model.later { model.picked(result, pieces: summaries, replacing: folder) }
                }
            }
            .onChange(of: runner.revision) { _, _ in Task { await reload() } }
            .task(id: on) {
                guard on else { return }
                model.load()
                runner.restore()
                await model.checkAccess()
            }
    }
}

extension View {
    @MainActor
    func folderImport(_ model: FolderImportModel, text: UIText, summaries: [URL: PieceSummary], on: Bool, account: Account, reload: @escaping () async -> Void, open: @escaping (URL) async -> Void) -> some View {
        modifier(FolderImportLayer(model: model, text: text, summaries: summaries, on: on, account: account, reload: reload, open: open))
    }
}

/// iOS no longer lets the app into a folder (its bookmark did not open): said plainly, with the way back, the folder picked
/// again in Files. The pieces stay; their PDFs open again once the folder is picked.
struct FolderLostSheet: View {
    let folder: ImportFolder
    let text: UIText
    let picked: (Result<URL, Error>) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var choosing = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["folderImport.folderLostTitle"])
                .dsType(.title)
                .foregroundStyle(Color.ds("ink"))
                .accessibilityAddTraits(.isHeader)
            Text(text.format("folderImport.folderLostLine", folder.place))
                .dsType(.body)
                .foregroundStyle(Color.ds("ink"))
                .fixedSize(horizontal: false, vertical: true)
            Text(text["folderImport.pickAgainHint"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: CGFloat.ds("s-2")) {
                Spacer(minLength: 0)
                Button(text["folderImport.notNow"]) { dismiss() }
                    .buttonStyle(DSButtonStyle(kind: .quiet))
                Button(text["folderImport.pickAgain"]) { choosing = true }
                    .buttonStyle(DSButtonStyle(kind: .primary))
            }
        }
        .padding(CGFloat.ds("s-6"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.medium])
        .fileImporter(isPresented: $choosing, allowedContentTypes: [.folder]) { result in
            picked(result)
        }
    }
}

/// The folders in the library's menu (import-web-6-recheck-menu, on the iPad): «Проверить папку «Ноты»», with where it
/// is and when it was checked, or that iOS no longer lets the app into it.
struct FolderMenuItems: View {
    let folders: [ImportFolder]
    let text: UIText
    let recheck: (ImportFolder) -> Void

    var body: some View {
        ForEach(folders) { folder in
            Button {
                recheck(folder)
            } label: {
                Label(text.format("folderImport.recheckFolder", folder.name), image: "ic-recheck")
                Text(status(folder))
            }
        }
    }

    /// «Google Drive · проверена 12.11.2026, 9:40», or «Google Drive · нет доступа: выберите папку заново».
    private func status(_ folder: ImportFolder) -> String {
        let state: String
        switch folder.problem {
        case .lost?: state = text["folderImport.accessLost"]
        case .offline?: state = text["folderImport.offlineTitle"]
        case .unreadable?: state = text["folderImport.accessUnreadable"]
        case nil:
            let formatter = DateFormatter()
            formatter.locale = Locale(identifier: text.language)
            formatter.dateStyle = .short
            formatter.timeStyle = .short
            state = folder.checkedAt.map { text.format("folderImport.checkedAt", formatter.string(from: $0)) } ?? text["folderImport.accessFine"]
        }
        return [folder.provider ?? text["folderImport.onIpad"], state].joined(separator: " · ")
    }
}

/// «Держать на этом iPad» beside a playlist's name: its pieces' linked PDFs read ahead and kept here, for playing with no
/// connection; while they are read, how many of how many.
struct KeepPlaylistToggle: View {
    @ObservedObject var model: FolderImportModel
    let playlist: String
    let summaries: [URL: PieceSummary]
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            if let keeping = model.keeping, keeping.playlist == playlist {
                ProgressView()
                Text(text.format("folderImport.keeping", String(keeping.done), String(keeping.of)))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            Toggle(text["folderImport.keepHere"], isOn: Binding {
                model.kept.contains(playlist)
            } set: { on in
                Task { await model.keep(playlist, on, pieces: summaries) }
            })
            .toggleStyle(.switch)
            .tint(Color.ds("accent"))
            .dsType(.label)
            .foregroundStyle(Color.ds("ink"))
            .fixedSize()
            .disabled(model.keeping != nil)
        }
        .accessibilityHint(text["folderImport.keepHereHint"])
    }
}
