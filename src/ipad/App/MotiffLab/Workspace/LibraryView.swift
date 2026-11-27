import SwiftUI

/// The library (version 0.2, Andrei's approved screens ux-prototype/v2/screens/lib-1-list.png, lib-3-sessions.png and
/// lib-4-props.png): the screen outside the piece and the way into one, as short as it can be: find a piece and start.
/// The pieces are a list with Title, Composer, Playlists and Opened columns and an index at the edge, searched, filtered by
/// composer, tag and playlist, and in tabs: All pieces, Composers, Recent, Playlists, and Sessions, the cards of current
/// work, each resumed with one tap. A piece's properties (title, composer, tags, playlists) are edited from a long press or a
/// swipe on its line, which also deletes it (#479).
/// The pieces are project files in the app's Documents (0019 section 2.3), read by music-core (`project.summary`) without
/// opening them; the library's model (`LibraryModel`) holds them and opens, switches and deletes a piece. One Add piece
/// action holds the ways in; the overflow imports a project file. Everything here runs on the device and is free; signed
/// in, the pieces sync with the player's other devices by themselves (version 0.2, `CloudSync`), and a project saved
/// online that is still not here is listed in a tab of its own, to download (0014, #335, #479; `LibraryOnline.swift`).
/// The search's keyboard goes away when the list scrolls or empty space is tapped. Here, in the folder that knows every
/// feature, because the screen composes the library with the account, the imports, the Reference and the piece's screen.
struct LibraryView: View {
    let text = UIText.current
    @EnvironmentObject var account: Account
    /// The device's settings.
    @EnvironmentObject private var deviceSettings: DeviceSettings
    @StateObject var library = LibraryModel(opened: { RecognitionQueue.shared.opened($0) })
    @State private var settings = false
    /// The Reference's circle of fifths, open over the library.
    @State private var reference = false
    @State private var adding = false
    @State private var choosing = false
    /// The projects saved online that are not on this iPad (#335); none while signed out or offline.
    @State var online: [OnlineProject] = []
    /// Saving online and sync, on until the player turns it off in Settings (version 0.2).
    @AppStorage(CloudSync.onKey) var syncOn = true
    @State var syncing = false
    /// How many pieces stay on this iPad because the free account's room online is full (0025 section 8).
    @State var overQuota = 0
    @Environment(\.scenePhase) private var phase
    @State private var editing: LibraryEntry?
    /// Whether the search is typed in: off when the list scrolls or empty space is tapped (#479).
    @FocusState private var searching: Bool
    /// «Импорт папки» (version 0.5): the folders imported, their review and «Держать на этом iPad».
    @StateObject private var folderImport = FolderImportModel()
    /// «Распознать ноты» for several pieces: those picked while choosing (nil when not), and the batch's consent.
    @State private var chosen: Set<URL>?
    @State private var consenting = false
    @ObservedObject private var recognitionQueue = RecognitionQueue.shared

    var body: some View {
        NavigationStack {
            let page = LibraryPage.of(library.entries, library.query, tab: library.tab, picked: library.picked, locale: Locale(identifier: text.language))
            // The header keeps the kit's space below the status bar (v0.5 put it right under the clock): the scroll view
            // runs up under the bar, and its content starts the bar's height plus the page's padding down, so the space
            // never depends on whether a scroll view with a hidden navigation bar insets its content by itself.
            GeometryReader { window in
            let bar = Self.statusBar(window.safeAreaInsets.top)
            ScrollViewReader { proxy in
                ScrollView {
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-6")) {
                        header
                        MainEnvironmentLine(account: account, text: text)
                        ImportBanner(runner: ImportRunner.shared, text: text)
                        RecognitionTop(queue: recognitionQueue, text: text, chosen: $chosen, entries: library.entries, shown: page.pieces) { consenting = true }
                        if let problem = library.problem {
                            Text(problem)
                                .dsType(.label)
                                .foregroundStyle(Color.ds("danger"))
                                .accessibilityAddTraits(.isStaticText)
                        }
                        if overQuota > 0 {
                            Text(text.format("online.overQuota", String(overQuota), String(CloudSync.freeProjectsOnline)))
                                .dsType(.label)
                                .foregroundStyle(Color.ds("ink-2"))
                        }
                        if library.projects.isEmpty {
                            Text(text["home.emptyWhat"])
                                .dsType(.body)
                                .foregroundStyle(Color.ds("ink-2"))
                                .frame(maxWidth: 560, alignment: .leading)
                            // No tabs without a piece here: the ones saved online are the shelf's cards.
                            if !online.isEmpty {
                                OnlineShelf(text: text, projects: online) { project in Task { await getHere(project) } }
                            }
                        } else {
                            LibraryShelfView(page: page, summaries: library.summaries, composers: library.composers, tags: library.tags, playlists: library.playlists, tab: $library.tab, query: $library.query, picked: $library.picked, text: text, playlistAccessory: playlistToggle, marks: marks, online: onlineTab) { url in
                                searching = false
                                Task { await library.openProject(url) }
                            } edit: { entry in
                                searching = false
                                editing = entry
                            } delete: { url in
                                library.delete(url)
                            }
                        }
                    }
                    .padding(.horizontal, CGFloat.ds("s-12"))
                    .padding(.top, bar + CGFloat.ds("s-10"))
                    .padding(.bottom, CGFloat.ds("s-10"))
                    // Empty space down to the screen's foot puts the search's keyboard away; a line's or a button's own
                    // tap is drawn over it and keeps working.
                    .frame(maxWidth: .infinity, minHeight: window.size.height + window.safeAreaInsets.top, alignment: .topLeading)
                    .background {
                        Color.clear
                            .contentShape(Rectangle())
                            .onTapGesture { searching = false }
                            .accessibilityHidden(true)
                    }
                }
                .scrollDismissesKeyboard(.immediately)
                .ignoresSafeArea(edges: .top)
                .overlay(alignment: .trailing) {
                    if page.indexed && !page.isEmpty {
                        LibraryIndex(letters: LibraryShelf.indexLetters(page.initials), text: text) { letter in
                            guard let at = LibraryShelf.target(letter, in: page.initials, locale: Locale(identifier: text.language)) else { return }
                            // Just below the status bar, which the scroll view runs under.
                            let frame = window.size.height + window.safeAreaInsets.top
                            proxy.scrollTo(page.id(at: at), anchor: UnitPoint(x: 0, y: frame > 0 ? bar / frame : 0))
                        }
                        .padding(.trailing, CGFloat.ds("s-2"))
                    }
                }
            }
            }
            .background(Color.ds("desk").ignoresSafeArea())
            .disabled(library.opening)
            .overlay { if library.opening { ProgressView() } }
            .toolbar(.hidden, for: .navigationBar)
            .sheet(isPresented: $settings) { SettingsSheet(settings: DeviceSettings.shared, account: account, text: text) }
            .sheet(isPresented: $reference) { ReferenceSheet(text: text, settings: deviceSettings) }
            .sheet(item: $editing) { entry in
                PiecePropertiesSheet(entry: entry, summary: library.summaries[entry.url], composers: library.composers, libraryTags: library.tags, libraryPlaylists: library.playlists, text: text) {
                    await library.reload()
                }
            }
            .sheet(isPresented: $adding) {
                AddPieceSheet(text: text) { result in
                    adding = false
                    Task { await library.open(result) }
                } demo: {
                    adding = false
                    Task { await library.openExample() }
                } folder: { result in
                    adding = false
                    folderImport.later { folderImport.picked(result, pieces: library.summaries) }
                }
                .environmentObject(account)
            }
            .sheet(item: $library.away) { prompt in
                AwayPieceSheet(prompt: prompt, text: text, recheck: recheck(prompt)) { found in
                    await library.openFound(prompt.url, found, title: prompt.summary.playersTitle)
                }
            }
            .navigationDestination(item: $library.piece) { piece in
                PieceScreen(piece: piece, switchView: { landing in await library.switchView(from: piece, to: landing) }, openFile: { file, landing in await library.openFile(file, at: landing) })
                    .id(piece.id)
            }
            .fileImporter(isPresented: $choosing, allowedContentTypes: OpenPiece.openable) { result in
                Task { await library.open(result) }
            }
            .folderImport(folderImport, text: text, summaries: library.summaries, on: true, account: account) { await library.reload() } open: { url in await library.openProject(url) }
            .recognition(chosen: $chosen, consenting: $consenting, account: account, entries: library.entries, on: true, text: text) { settings = true } reload: { await library.reload() }
            .task {
                // «Нет на этом iPad» is not kept as the tab to start on: it is there only once the list online has come.
                if library.tab == .online { library.tab = .all }
                #if DEBUG
                let screensFile = ScreensHook.start()
                #endif
                await library.reload()
                // Built again in a new interface language: the piece that was open comes back where it was.
                if let again = InterfaceLanguage.shared.takeReopen() { await library.openFile(again.file, at: again.landing) }
                await refreshOnline()
                #if DEBUG
                if let screensFile { if ScreensHook.bothViews, let both = await ScreensHook.withNotes(screensFile) { await library.reload(); await library.openProject(both) } else { await library.open(.success(screensFile)) } }
                #endif
            }
            // The batch never writes the notes of the piece on screen: the screen saves it as it closes.
            .onChange(of: library.piece?.file) { _, file in recognitionQueue.opened(file) }
            .onChange(of: library.piece == nil) { _, closed in
                // Back from a score: its project was saved as the screen closed.
                if closed { Task { await library.reload(); await refreshOnline() } }
            }
            // Signed in or out: the projects saved online come or go.
            .onChange(of: account.player) { _, _ in Task { await refreshOnline() } }
            // Back on the screen: what changed on another device meanwhile comes here (never inside a piece).
            .onChange(of: phase) { _, now in
                if now == .active && library.piece == nil { Task { await refreshOnline() } }
            }
            .onChange(of: syncOn) { _, on in
                if on { Task { await refreshOnline() } }
            }
            // Another environment (Settings): its own library and its own pieces online, nothing carried over.
            .onChange(of: account.environment) { _, _ in
                online = []
                overQuota = 0
                library.showProblem(nil)
                library.picked = nil
                Task { await library.reload(); await refreshOnline() }
            }
            // The last piece saved online that was not here came: its tab goes, and All pieces is shown.
            .onChange(of: online.isEmpty) { _, none in
                library.tab = library.tab.shown(online: !none)
            }
            // The Test library cleared (Settings · About, `TestLibrary`): read again from what is left, nothing.
            .onChange(of: account.libraryCleared) { _, _ in online = []; overQuota = 0; library.picked = nil; folderImport.load(); Task { await library.reload(); await refreshOnline() } }
            // Another tab (the model starts its order and forgets the group picked): the keyboard goes away.
            .onChange(of: library.tab) { _, _ in searching = false }
        }
    }

    /// The space the status bar takes at the top: the window's top safe area, and never less than the bar's height on an
    /// iPad (s-6), for a window whose safe area does not include it.
    static func statusBar(_ safeTop: CGFloat) -> CGFloat {
        Swift.max(safeTop, CGFloat.ds("s-6"))
    }

    /// The library's name, the search, Add piece, the overflow and the account (lib-1).
    private var header: some View {
        HStack(alignment: .center, spacing: CGFloat.ds("s-4")) {
            Text(text["library.title"])
                .dsType(.display)
                .foregroundStyle(Color.ds("ink"))
                .fixedSize()
                .accessibilityAddTraits(.isHeader)
            EnvironmentMark(account: account, text: text)
            Spacer(minLength: CGFloat.ds("s-4"))
            if !library.projects.isEmpty {
                LibrarySearchField(search: $library.query.search, text: text, focus: $searching)
            }
            // The Reference: the circle of fifths in one tap.
            Button(text["reference.open"]) { reference = true }
                .buttonStyle(DSButtonStyle(kind: .quiet))
                .accessibilityHint(text["reference.openHint"])
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
                Button(text["recognition.choose"]) { chosen = [] }
                FolderMenuItems(folders: folderImport.folders, text: text) { folder in folderImport.recheck(folder, pieces: library.summaries) }
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

    /// «Держать на этом iPad» beside a playlist's name.
    private var playlistToggle: (String) -> AnyView {
        { name in AnyView(KeepPlaylistToggle(model: folderImport, playlist: name, summaries: library.summaries, text: text)) }
    }

    /// The pieces picked to recognise, a tap on a PDF-only piece picking or unpicking it, and each piece's recognition.
    private var marks: LibraryMarks {
        LibraryMarks(chosen: chosen, toggle: { entry in
            guard entry.withoutNotes, chosen != nil else { return }
            if chosen?.contains(entry.url) == true { chosen?.remove(entry.url) } else { chosen?.insert(entry.url) }
        }, status: { url in recognitionQueue.status(of: url) }, rename: { entry, field, value in
            Task { await library.rename(entry, field, to: value) }
        })
    }

    /// «Проверить папку» for a piece whose PDF was not found, when it came from a folder imported here.
    private func recheck(_ prompt: AwayPrompt) -> (() -> Void)? {
        guard let folder = folderImport.folder(of: prompt.summary) else { return nil }
        return { folderImport.later { folderImport.recheck(folder, pieces: library.summaries, target: prompt.url) } }
    }
}

extension OpenPiece: Hashable {
    static func == (lhs: OpenPiece, rhs: OpenPiece) -> Bool { lhs.id == rhs.id }
    func hash(into hasher: inout Hasher) { hasher.combine(id) }
}
