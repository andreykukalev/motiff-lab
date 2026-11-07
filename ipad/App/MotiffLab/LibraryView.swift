import SwiftUI
import UniformTypeIdentifiers

/// The library (0018 section 9, design/kit/screens/s1_library.png): the screen outside the piece, a shelf of cards, each
/// resumed with one tap. The pieces are project files in the app's Documents (0019 section 2.3); what a card says comes
/// from music-core (`project.summary`) without opening the piece. One Add piece action holds the ways in; the overflow
/// imports a project file. Everything here runs on the device and is free.
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
                    LazyVGrid(
                        columns: [GridItem(.adaptive(minimum: 320), spacing: CGFloat.ds("s-6"), alignment: .top)],
                        alignment: .leading,
                        spacing: CGFloat.ds("s-6")
                    ) {
                        ForEach(shelved) { entry in
                            PieceCard(entry: entry, summary: summaries[entry.url], text: text) {
                                Task { await openProject(entry.url) }
                            } delete: {
                                delete(entry)
                            }
                        }
                        AddCard(text: text) { adding = true }
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
                #if DEBUG
                if let screensFile { await open(.success(screensFile)) }
                #endif
            }
            .onChange(of: piece == nil) { _, closed in
                // Back from a score: its project was saved as the screen closed.
                if closed { Task { await reload() } }
            }
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

    private func openProject(_ url: URL) async {
        do {
            let data = try Data(contentsOf: url)
            let language = text.language
            await show(file: url) { await OpenPiece.open(fileName: url.lastPathComponent, data: data, language: language) }
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

    private func delete(_ entry: ProjectFiles.Entry) {
        try? FileManager.default.removeItem(at: entry.url)
        projects.removeAll { $0.url == entry.url }
        summaries[entry.url] = nil
    }
}

/// What a library card says of a piece, read by music-core from its project file (`project.summary`, facade contract 4).
struct PieceSummary: Equatable, Sendable {
    /// "imported", "scanned" or "transcribed".
    var origin: String
    var hasNotes: Bool
    var ideas: Int
    var checkBars: Int
    /// The resume point worked from last, by its name when it has one.
    var latest: Latest?
    /// The project's id, and when it was last opened (ISO 8601, as music-core writes it).
    var id: String?
    var lastOpened = ""
    /// For notes read from a PDF piece: that piece's id, and where each of its pages starts in the notes (PDF | Notes, 0023).
    var readFrom: String?
    var readFromPages: [Double] = []
    /// For a piece with notes and a PDF kept inside it: where each of the PDF's pages starts in the notes (contract 6).
    var documentPages: [Double] = []
    /// Whether the piece keeps a PDF.
    var document = false

    struct Latest: Equatable, Sendable {
        var name: String?
    }

    /// The summary in a facade answer: {"project": {…}}.
    init?(answer: String) {
        guard let project = facadeObject(answer)?["project"] as? [String: Any] else { return nil }
        origin = project["origin"] as? String ?? "imported"
        hasNotes = project["hasNotes"] as? Bool ?? true
        ideas = (project["ideas"] as? NSNumber)?.intValue ?? 0
        checkBars = (project["checkBars"] as? NSNumber)?.intValue ?? 0
        latest = (project["latest"] as? [String: Any]).map { Latest(name: $0["name"] as? String) }
        id = project["id"] as? String
        lastOpened = project["lastOpened"] as? String ?? ""
        readFrom = project["readFrom"] as? String
        readFromPages = (project["readFromPages"] as? [NSNumber] ?? []).map(\.doubleValue)
        documentPages = (project["documentPages"] as? [NSNumber] ?? []).map(\.doubleValue)
        document = project["document"] as? Bool ?? false
    }

    /// Reads a project file's summary through music-core; nil when the file cannot be read.
    static func read(_ url: URL) async -> PieceSummary? {
        guard let data = try? Data(contentsOf: url), let file = String(data: data, encoding: .utf8),
              let body = try? JSONSerialization.data(withJSONObject: ["file": file]),
              let request = String(data: body, encoding: .utf8) else { return nil }
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "project.summary", request) }
        return PieceSummary(answer: answer)
    }
}

/// A piece on the shelf (ds.css `.card`): its page, title and what kind of piece it is, the ideas kept and bars to check,
/// and where work on it stopped, with Resume, or Open for a piece not opened yet. A long press offers Delete, which asks
/// once more.
struct PieceCard: View {
    let entry: ProjectFiles.Entry
    let summary: PieceSummary?
    let text: UIText
    let open: () -> Void
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
                }
                Spacer(minLength: 0)
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
        .contextMenu {
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

    /// What kind of piece: scanned, from a recording or a PDF; nothing more for a file the player opened.
    private var meta: String {
        guard let summary else { return " " }
        if !summary.hasNotes { return text["library.pdf"] }
        return switch summary.origin {
        case "scanned": text["library.scanned"]
        case "transcribed": text["library.recorded"]
        default: text["library.openFile"]
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

/// The last card on the shelf (ds.css `.card.add`): the ways in, and the demo piece.
struct AddCard: View {
    let text: UIText
    let add: () -> Void

    var body: some View {
        Button(action: add) {
            VStack(spacing: CGFloat.ds("s-3")) {
                Icon(name: "plus", size: CGFloat.ds("icon-l"))
                    .foregroundStyle(Color.ds("accent"))
                    .frame(width: 56, height: 56)
                    .background(Color.ds("accent-tint"), in: Circle())
                Text(text["library.addAPiece"])
                    .dsType(.heading)
                    .foregroundStyle(Color.ds("ink"))
                Text(text["library.openFileHintHere"])
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
                    .multilineTextAlignment(.center)
                Text(text["library.tryDemo"])
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            .padding(CGFloat.ds("s-6"))
            .frame(maxWidth: .infinity, minHeight: 280)
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(
                RoundedRectangle(cornerRadius: CGFloat.ds("r-m"))
                    .strokeBorder(Color.ds("hairline-strong"), style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
            )
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text["library.addAPiece"])
    }
}

/// Add piece (0018 section 9): opening a file runs on the device and is free. Scanning a page and hearing a recording come
/// to the iPad in a later step; until then they are on the web app.
struct AddPieceSheet: View {
    let text: UIText
    let picked: (Result<URL, Error>) -> Void
    /// A recording heard by Motiff Lab's scan server (Pro), to open as a new piece.
    let heard: (HeardRecording) -> Void
    let demo: () -> Void
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var account: Account
    @State private var choosing = false
    @State private var progress: String?
    @State private var problem: String?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
                HStack {
                    Text(text["library.addAPiece"])
                        .dsType(.title)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                    Spacer()
                    Button {
                        dismiss()
                    } label: {
                        Icon(name: "close")
                            .foregroundStyle(Color.ds("ink-2"))
                            .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                    }
                    .accessibilityLabel(text["library.close"])
                }
                Button {
                    choosing = true
                } label: {
                    HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
                        Icon(name: "file", size: CGFloat.ds("icon-l"))
                            .foregroundStyle(Color.ds("accent"))
                        VStack(alignment: .leading, spacing: 2) {
                            Text(text["library.openFile"])
                                .dsType(.bodyStrong)
                                .foregroundStyle(Color.ds("ink"))
                            Text(text["library.openFileHintHere"])
                                .dsType(.caption)
                                .foregroundStyle(Color.ds("ink-2"))
                        }
                        Spacer(minLength: 0)
                    }
                    .padding(CGFloat.ds("s-4"))
                    .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
                }
                .buttonStyle(.plain)
                // On the button, not the sheet: the recording's chooser below has its own, and a second file importer on a
                // view that holds another is not shown.
                .fileImporter(isPresented: $choosing, allowedContentTypes: LibraryView.openable) { result in
                    picked(result)
                }
                if account.isAvailable {
                    fromRecording
                }
                Button(action: demo) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "play")
                        Text(text["library.demo"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
            .padding(CGFloat.ds("s-6"))
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.medium, .large])
        .interactiveDismissDisabled(progress != nil)
    }

    /// From a recording (0014): the scan server hears the sound of an audio or video file, up to its first 7 minutes, and
    /// its notes become a new piece. Pro, as on the web; what it needs first is offered here.
    private var fromRecording: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
                Icon(name: "mic", size: CGFloat.ds("icon-l"))
                    .foregroundStyle(Color.ds("accent"))
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Text(text["library.fromRecording"])
                            .dsType(.bodyStrong)
                            .foregroundStyle(Color.ds("ink"))
                        ProTag()
                    }
                    Text(text["library.fromRecordingHint"])
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                Spacer(minLength: 0)
            }
            RecordingChooser(
                text: text,
                line: text["library.fromRecordingLineDevice"],
                button: text["library.chooseRecording"],
                hint: text["transcribe.choose"],
                progress: progress
            ) { url in
                Task { await hear(url) }
            }
            if let problem {
                Text(problem)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("danger"))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(CGFloat.ds("s-4"))
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
    }

    private func hear(_ url: URL) async {
        problem = nil
        defer { progress = nil }
        switch await RecordingHearing.hear(url, account: account, text: text, progress: { progress = $0 }) {
        case .success(let recording):
            heard(recording)
        case .failure(let failure):
            problem = failure.message
        }
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
