import SwiftUI
import UniformTypeIdentifiers

/// The library (0018 section 9, design/kit/screens/s1_library.png): the screen outside the piece, a shelf of cards, each
/// resumed with one tap. The pieces are project files in the app's Documents (0019 section 2.3); what a card says comes
/// from music-core (`project.summary`) without opening the piece. One Add piece action holds the ways in; the overflow
/// imports a project file. Everything here runs on the device and is free; signed in, the projects saved online that are
/// not on this iPad are listed under them, to download (0014, #335). For a couple of hundred pieces (version 0.2) the shelf
/// is searched, ordered and filtered by composer and tag, shows as cards or a compact list, and each piece's title,
/// composer and tags are edited from its ⋯.
struct LibraryView: View {
    private let text = UIText.current
    @EnvironmentObject private var account: Account
    @State private var settings = false
    @State private var adding = false
    @State private var opening = false
    @State private var choosing = false
    @State private var piece: OpenPiece?
    @State private var problem: String?
    @State private var projects: [ProjectFiles.Entry] = []
    @State private var summaries: [URL: PieceSummary] = [:]
    /// The projects saved online that are not on this iPad (#335); none while signed out or offline.
    @State private var online: [OnlineProject] = []
    /// The cards made from the project files and their summaries, with the shelf's composers and tags (version 0.2).
    @State private var entries: [LibraryEntry] = []
    @State private var composers: [String] = []
    @State private var tags: [String] = []
    @State private var query = LibraryQuery()
    /// Cards or the list as the player picked, kept on the device; empty until they pick.
    @AppStorage("library.view") private var chosenLayout = ""
    @State private var editing: LibraryEntry?

    /// The shelf's columns, for the projects here and those saved online.
    static let columns = [GridItem(.adaptive(minimum: 320), spacing: CGFloat.ds("s-6"), alignment: .top)]

    /// MIDI, MusicXML (plain and compressed), a PDF to read as it is (0023) and Motiff Lab project files.
    static let openable: [UTType] = [
        .midi,
        .pdf,
        UTType(filenameExtension: "musicxml") ?? .xml,
        UTType(filenameExtension: "mxl") ?? .data,
        .xml,
        .json,
    ]

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-6")) {
                    header
                    if let problem {
                        Text(problem)
                            .dsType(.label)
                            .foregroundStyle(Color.ds("danger"))
                            .accessibilityAddTraits(.isStaticText)
                    }
                    if projects.isEmpty {
                        Text(text["home.emptyWhat"])
                            .dsType(.body)
                            .foregroundStyle(Color.ds("ink-2"))
                            .frame(maxWidth: 560, alignment: .leading)
                    }
                    Text(text["home.projects"])
                        .overline()
                        .accessibilityAddTraits(.isHeader)
                    shelf
                    if !online.isEmpty {
                        OnlineShelf(text: text, projects: online) { project in Task { await getHere(project) } }
                    }
                }
                .padding(.horizontal, CGFloat.ds("s-12"))
                .padding(.vertical, CGFloat.ds("s-10"))
            }
            .background(Color.ds("desk").ignoresSafeArea())
            .disabled(opening)
            .overlay { if opening { ProgressView() } }
            .toolbar(.hidden, for: .navigationBar)
            .sheet(isPresented: $settings) { SettingsSheet(settings: DeviceSettings.shared, account: account, text: text) }
            .sheet(item: $editing) { entry in
                PieceEditSheet(entry: entry, summary: summaries[entry.url], libraryTags: tags, text: text) {
                    await reload()
                }
            }
            .sheet(isPresented: $adding) {
                AddPieceSheet(text: text) { result in
                    adding = false
                    Task { await open(result) }
                } heard: { recording in
                    adding = false
                    Task { await openHeard(recording) }
                } demo: {
                    adding = false
                    Task { await openExample() }
                }
                .environmentObject(account)
            }
            .navigationDestination(item: $piece) { piece in
                Group {
                    if piece.isDocument {
                        PdfScreen(piece: piece, switchView: { landing in await switchView(from: piece, to: landing) }, openRead: { file, landing in await openFile(file, at: landing) })
                    } else {
                        ScoreScreen(piece: piece) { landing in await switchView(from: piece, to: landing) }
                    }
                }
                .id(piece.id)
            }
            .fileImporter(isPresented: $choosing, allowedContentTypes: Self.openable) { result in
                Task { await open(result) }
            }
            .task {
                #if DEBUG
                let screensFile = ScreensHook.start()
                #endif
                await reload()
                await refreshOnline()
                #if DEBUG
                if let screensFile { await open(.success(screensFile)) }
                #endif
            }
            .onChange(of: piece == nil) { _, closed in
                // Back from a score: its project was saved as the screen closed.
                if closed { Task { await reload(); await refreshOnline() } }
            }
            // Signed in or out: the projects saved online come or go.
            .onChange(of: account.player) { _, _ in Task { await refreshOnline() } }
        }
    }

    /// The wordmark and tagline, Add piece, the overflow and the account.
    private var header: some View {
        HStack(alignment: .center, spacing: CGFloat.ds("s-4")) {
            HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-4")) {
                Text("Motiff Lab")
                    .dsType(.display)
                    .foregroundStyle(Color.ds("ink"))
                    .accessibilityAddTraits(.isHeader)
                Text(text["home.tagline"])
                    .font(DesignTokens.font(display: true, weight: 400, size: 17).italic())
                    .foregroundStyle(Color.ds("ink-2"))
            }
            Spacer(minLength: CGFloat.ds("s-4"))
            Button {
                adding = true
            } label: {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "plus")
                    Text(text["library.addPiece"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .primary))
            Menu {
                Button {
                    choosing = true
                } label: {
                    Text(text["home.importProject"])
                }
            } label: {
                Icon(name: "more", size: CGFloat.ds("icon-l"))
                    .foregroundStyle(Color.ds("ink-2"))
                    .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
            }
            .accessibilityLabel(text["library.more"])
            if account.isAvailable {
                Button {
                    settings = true
                } label: {
                    // Signed in, the player's photo or initial (their profile's); signed out, the person.
                    if let name = account.shownName {
                        PlayerAvatar(name: name, image: account.profile?.image, size: CGFloat.ds("h-touch"))
                    } else {
                        Icon(name: "user", size: CGFloat.ds("icon-l"))
                            .foregroundStyle(Color.ds("accent"))
                            .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                            .background(Color.ds("accent-tint"), in: Circle())
                    }
                }
                .accessibilityLabel(account.shownName.map { text.format("account.open", $0) } ?? text["account.title"])
            }
        }
    }

    /// Cards or the list: the player's pick, else a list for a long shelf.
    private var layout: LibraryLayout {
        LibraryShelf.layout(chosen: LibraryLayout(rawValue: chosenLayout), count: entries.count)
    }

    /// The search, order, view and filters, then the pieces as cards or as lines of a list, or a way back to all of them
    /// when none matches.
    @ViewBuilder
    private var shelf: some View {
        let shown = LibraryShelf.apply(entries, query, locale: Locale(identifier: text.language))
        if !entries.isEmpty {
            LibraryTools(query: $query, layout: layout, composers: composers, tags: tags, shown: shown.count, total: entries.count, text: text) {
                chosenLayout = $0.rawValue
            }
        }
        if query.narrows && shown.isEmpty {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                Text(text["library.noneMatch"])
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink-2"))
                Button(text["library.clearFilters"]) { query = query.cleared }
                    .buttonStyle(DSButtonStyle(kind: .secondary))
            }
        } else if layout == .list && !entries.isEmpty {
            LazyVStack(alignment: .leading, spacing: 0) {
                ForEach(shown) { entry in
                    LibraryRow(entry: entry, summary: summaries[entry.url], text: text) {
                        Task { await openProject(entry.url) }
                    } edit: {
                        editing = entry
                    } delete: {
                        delete(entry.url)
                    }
                    if entry.id != shown.last?.id {
                        Rectangle().fill(Color.ds("hairline")).frame(height: 1)
                    }
                }
            }
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        } else {
            LazyVGrid(columns: Self.columns, alignment: .leading, spacing: CGFloat.ds("s-6")) {
                ForEach(shown) { entry in
                    PieceCard(entry: entry, summary: summaries[entry.url], text: text) {
                        Task { await openProject(entry.url) }
                    } edit: {
                        editing = entry
                    } delete: {
                        delete(entry.url)
                    }
                }
                if !query.narrows {
                    AddCard(text: text) { adding = true }
                }
            }
        }
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

    /// The projects in Documents, and what music-core reads from each.
    private func reload() async {
        projects = ProjectFiles.list()
        var read: [URL: PieceSummary] = [:]
        for entry in projects {
            if let summary = await PieceSummary.read(entry.url) {
                read[entry.url] = summary
            }
        }
        summaries = read
        shelve()
    }

    /// The cards made again from the project files and their summaries; a composer or tag no piece has any more stops
    /// filtering.
    private func shelve() {
        let locale = Locale(identifier: text.language)
        entries = shelved.map { LibraryEntry.of($0, summaries[$0.url]) }
        composers = LibraryShelf.composers(entries, locale: locale)
        tags = LibraryShelf.tags(entries, locale: locale)
        query = LibraryShelf.kept(query, composers: composers, tags: tags)
    }

    /// The projects saved online whose id is in no project file here (0014); none while signed out, offline or when the
    /// API cannot be reached, and then nothing is said: the library is about the projects on this iPad.
    private func refreshOnline() async {
        var listed: [OnlineProject] = []
        if account.player != nil { listed = await account.online.list() ?? [] }
        online = OnlineProjects.notHere(listed, here: Set(summaries.values.compactMap(\.id)))
    }

    /// Get it here (#335): downloads a project saved online as a new project file and opens it, from then on an ordinary
    /// project on this iPad. A failed download says so in one line and leaves the library as it was.
    private func getHere(_ project: OnlineProject) async {
        opening = true
        problem = nil
        switch await account.online.keep(project) {
        case .success(let url):
            online.removeAll { $0.id == project.id }
            await openProject(url)
        case .failure(let failure):
            opening = false
            problem = text[failure.key]
        }
    }

    private func openExample() async {
        guard let example = OpenPiece.example else { return }
        let language = text.language
        await show(saveAs: example.title) {
            await OpenPiece.open(fileName: example.fileName, data: example.data, language: language, title: example.title)
        }
    }

    /// A recording heard by the scan server, as a new piece of its notes (0014, 0016), named after the recording.
    private func openHeard(_ recording: HeardRecording) async {
        let language = text.language
        let title = recording.name.isEmpty ? text["library.recorded"] : recording.name
        await show(saveAs: title) {
            await OpenPiece.open(fileName: title + ".mid", data: Data(), language: language, title: title, heard: recording)
        }
    }

    /// Opens a project in Documents where it was left, titled as the player titled it in the library, if they did.
    private func openProject(_ url: URL) async {
        do {
            let data = try Data(contentsOf: url)
            let language = text.language
            let title = summaries[url]?.playersTitle
            await show(file: url) { await OpenPiece.open(fileName: url.lastPathComponent, data: data, language: language, title: title) }
        } catch {
            problem = error.localizedDescription
        }
    }

    /// PDF | Notes (0023): the piece's other view replaces it, at the page or place given. The screen has saved the piece.
    private func switchView(from current: OpenPiece, to landing: Landing) async {
        // A PDF kept in the piece since it was opened is in its own file: the PDF view of the same piece.
        guard let file = current.other?.file ?? current.file else { return }
        await openFile(file, at: landing)
    }

    /// Opens a project file in Documents in place of the piece shown, at a page or place: at a page, a piece with notes and
    /// a PDF inside it opens in its PDF view.
    private func openFile(_ file: URL, at landing: Landing) async {
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

    private func open(_ result: Result<URL, Error>) async {
        switch result {
        case .failure(let error):
            problem = error.localizedDescription
        case .success(let url):
            if ProjectFiles.isInFolder(url) {
                await openProject(url)
                return
            }
            // A file from Files is lent to the app only while it reads it; the piece becomes a project file here.
            let lent = url.startAccessingSecurityScopedResource()
            defer { if lent { url.stopAccessingSecurityScopedResource() } }
            do {
                let data = try Data(contentsOf: url)
                let language = text.language
                let name = url.lastPathComponent
                await show(saveAs: ProjectFiles.title(of: name)) { await OpenPiece.open(fileName: name, data: data, language: language) }
            } catch {
                problem = error.localizedDescription
            }
        }
    }

    /// Opens a piece and shows its score: saved to its own project file, or as a new one in Documents.
    private func show(file: URL? = nil, saveAs title: String? = nil, landing: Landing? = nil, _ opener: () async -> Result<OpenPiece, EngraveError>) async {
        opening = true
        defer { opening = false }
        problem = nil
        switch await opener() {
        case .success(var opened):
            if let earlier = piece {
                _ = await MusicCore.run { MusicCore.close(piece: earlier.id) }
            }
            if let file {
                opened.file = file
            } else if let title {
                let url = ProjectFiles.newURL(for: title)
                do {
                    try await ProjectFiles.save(piece: opened.id, to: url)
                    opened.file = url
                } catch {
                    problem = (error as? ProjectError)?.message ?? error.localizedDescription
                }
            }
            await reload()
            if let saved = opened.file { opened.other = OtherView.of(saved, in: summaries) }
            opened.landing = landing
            piece = opened
        case .failure(let error):
            problem = error.message
        }
    }

    private func delete(_ url: URL) {
        try? FileManager.default.removeItem(at: url)
        projects.removeAll { $0.url == url }
        summaries[url] = nil
        shelve()
    }
}

/// A piece on the shelf (ds.css `.card`): its page, title, composer and what kind of piece it is, the ideas kept, bars to
/// check and the player's tags, and where work on it stopped, with Resume, or Open for a piece not opened yet. A tap on the
/// card opens it too. Its ⋯, or a long press, offers its title, composer and tags, and Delete, which asks once more.
struct PieceCard: View {
    let entry: LibraryEntry
    let summary: PieceSummary?
    let text: UIText
    let open: () -> Void
    let edit: () -> Void
    let delete: () -> Void
    @State private var confirming = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-4")) {
                Button(action: open) {
                    RoundedRectangle(cornerRadius: 3)
                        .fill(Color.ds("paper"))
                        .overlay(RoundedRectangle(cornerRadius: 3).strokeBorder(Color.ds("hairline")))
                        .frame(width: 84, height: 118)
                        .shadow(color: Color.ds("hairline"), radius: 1, y: 1)
                }
                .buttonStyle(.plain)
                .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                    Text(entry.title)
                        .font(DesignTokens.font(display: true, weight: 500, size: 19, relativeTo: .title3))
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                    Text(meta)
                        .dsType(.label)
                        .foregroundStyle(Color.ds("ink-2"))
                    if let summary, summary.ideas > 0 || summary.checkBars > 0 {
                        HStack(spacing: CGFloat.ds("s-2")) {
                            if summary.ideas > 0 { Tag(text: text.count("library.ideas", summary.ideas), kind: .saved) }
                            if summary.checkBars > 0 { Tag(text: text.count("library.check", summary.checkBars), kind: .warn) }
                        }
                    }
                    PieceTagsLine(tags: entry.tags, most: 4)
                }
                Spacer(minLength: 0)
                PieceMenu(title: entry.title, text: text, edit: edit, delete: delete)
            }
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            if let resumeName {
                Text(text["library.recentWork"]).overline()
                Text(resumeName)
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink"))
                Text(text.format("home.lastOpened", entry.modified.formatted(.relative(presentation: .named).locale(Locale(identifier: text.language)))))
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
                Button(action: open) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "play")
                        Text(text["library.resume"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary))
                .accessibilityLabel(text.format("library.resumeLabel", entry.title))
            } else {
                // A piece not opened yet has no work to come back to: one Open, as on the web.
                Button(text["home.open"], action: open)
                    .buttonStyle(DSButtonStyle(kind: .secondary))
                    .accessibilityLabel(text.format("library.openLabel", entry.title))
            }
        }
        .padding(CGFloat.ds("s-4"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .contentShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .onTapGesture(perform: open)
        .contextMenu {
            Button(action: edit) {
                Text(text["library.editDetails"])
            }
            Button(role: .destructive) {
                confirming = true
            } label: {
                Text(text["home.delete"])
            }
        }
        .confirmationDialog(text["home.deleteForGood"], isPresented: $confirming, titleVisibility: .hidden) {
            Button(text["home.deleteForGood"], role: .destructive, action: delete)
            Button(text["home.keep"], role: .cancel) {}
        }
    }

    /// The composer, and what kind of piece: scanned, from a recording or a PDF, or a file the player opened when no
    /// composer is named.
    private var meta: String {
        guard let summary else { return entry.composer ?? " " }
        let kind = Self.kind(summary, text)
        return [entry.composer, kind ?? (entry.composer == nil ? text["library.openFile"] : nil)].compactMap { $0 }.joined(separator: " · ")
    }

    /// What kind of piece, when it is not a file the player opened: scanned, from a recording or a PDF.
    static func kind(_ summary: PieceSummary?, _ text: UIText) -> String? {
        guard let summary else { return nil }
        if !summary.hasNotes { return text["library.pdf"] }
        return switch summary.origin {
        case "scanned": text["library.scanned"]
        case "transcribed": text["library.recorded"]
        default: nil
        }
    }

    /// Where work on the piece stopped; nil for a piece not opened yet.
    private var resumeName: String? {
        guard let summary else { return nil }
        if !summary.hasNotes { return text["library.pdfAsIs"] }
        guard let latest = summary.latest else { return nil }
        return latest.name ?? text["library.page1"]
    }
}

extension OpenPiece: Hashable {
    static func == (lhs: OpenPiece, rhs: OpenPiece) -> Bool { lhs.id == rhs.id }
    func hash(into hasher: inout Hasher) { hasher.combine(id) }
}

#if DEBUG
/// The way in for the screenshot UI test (`MotiffLabScreens`, the `Screens` scheme), in Debug builds only:
/// `-ml-screens-reset` removes the projects in Documents before the library first reads them, and
/// `-ml-screens-open <path>` then opens that file as if it was picked in Files. Read once per launch. With `-ml-screens`
/// the score also has a hidden button that chooses the next phrase, as a tap on its bracket would.
@MainActor
enum ScreensHook {
    private static var used = false
    private static var phrase = 1

    static let active = ProcessInfo.processInfo.arguments.contains("-ml-screens")

    /// Phrase 2, then 3, and so on: each press chooses another phrase, so it never lifts the chosen one into focus.
    static func nextPhrase() -> Int {
        phrase += 1
        return phrase
    }

    static func start(arguments: [String] = ProcessInfo.processInfo.arguments) -> URL? {
        guard !used else { return nil }
        used = true
        if arguments.contains("-ml-screens-reset") {
            for entry in ProjectFiles.list() { try? FileManager.default.removeItem(at: entry.url) }
        }
        guard let index = arguments.firstIndex(of: "-ml-screens-open"), arguments.indices.contains(index + 1) else { return nil }
        return URL(fileURLWithPath: arguments[index + 1])
    }
}
#endif
