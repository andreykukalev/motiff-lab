import SwiftUI
import WebKit

/// A piece's score (0018 section 2, design/kit/screens/s2_score_page.png): the score is the whole screen, A4 pages in one
/// column in portrait and two to a row in landscape, with the pencil marks on them (0019 section 2.2). Verovio's SVG is in
/// one web view that runs the web app's score modules (ScoreController, Web/score.js). Nothing is docked: the bar floats
/// over the top with the way back, the title, the pages in view and the actions, and the pill floats at the bottom with
/// play, the tempo and the metronome. Colours come from the design kit's tokens, Daylight or Night with
/// the iPad's appearance. In phrase focus (0018 section 4) the selection lifts into a card across the screen with the
/// page dimmed behind it, previous and next on the card's edges, and the bar becomes the focus bar. The piano roll opens
/// in a drawer from the bottom (section 4), Insights in a sheet over the right third in landscape or the lower half in
/// portrait (section 6), and the Contents, the player's bookmarks and the pages, in a drawer over the left of the page in
/// landscape or the lower half in portrait (section 5); one of the three at a time.
struct ScoreScreen: View {
    let piece: OpenPiece
    /// Opens the piece's other view in its place, for PDF | Notes (0023); nil where the library has none.
    var switchView: ((Landing) async -> Void)? = nil
    /// The tag of a view of the switch shown on its own over the piece (version 0.6): other Notes, from a copy never
    /// saved; nil for the piece itself.
    var viewing: String? = nil
    /// Opens the piece's project file again in this screen's place, at a place in the music (version 0.6: «Сделать
    /// рабочими» and the Sources toast's Undo); nil where the library has none.
    var reopen: ((Landing) async -> Void)? = nil
    /// The screen's controllers and its panels (ScoreWorkspace): made once, as the screen first appears, and gone with it.
    @StateObject private var workspace: ScoreWorkspace

    /// The Insights sheet's width beside the score in landscape (ds.css `.sheet`).
    static let sheetWidth: CGFloat = 380
    /// The Contents drawer's width over the left of the page in landscape (ds.css `.sheet.left`).
    static let contentsWidth: CGFloat = 320

    /// The piece's screen, its originals known from the piece's opening (#495 BUG-02).
    init(piece: OpenPiece, switchView: ((Landing) async -> Void)? = nil, viewing: String? = nil, reopen: ((Landing) async -> Void)? = nil) {
        self.piece = piece
        self.switchView = switchView
        self.viewing = viewing
        self.reopen = reopen
        _workspace = StateObject(wrappedValue: ScoreWorkspace(piece: piece, viewing: viewing))
    }

    var body: some View {
        ScoreScreenContent(piece: piece, switchView: switchView, viewing: viewing, reopen: reopen, workspace: workspace)
    }
}

/// What the score's screen shows: the score and everything over it, and the reactions between the workspace's controllers.
/// It observes each controller itself, as the screen did while it owned them, so it is drawn again exactly when it was.
struct ScoreScreenContent: View {
    let piece: OpenPiece
    let switchView: ((Landing) async -> Void)?
    let viewing: String?
    let reopen: ((Landing) async -> Void)?
    private let text = UIText.current
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var account: Account
    @EnvironmentObject private var settings: DeviceSettings
    @ObservedObject var workspace: ScoreWorkspace
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    @ObservedObject var roll: RollController
    @ObservedObject var insights: InsightsController
    @ObservedObject var assistant: AssistantController
    /// The roll drawer's share of the screen's height in landscape (true) and portrait, as the player left it with its handle
    /// (RollDrawerHeight, #511 section 7.1), and the share while the handle is dragged.
    @State private var drawerShares: [Bool: CGFloat] = [true: RollDrawerHeight.saved(landscape: true), false: RollDrawerHeight.saved(landscape: false)]
    @State private var drawerDrag: CGFloat?
    /// How tall the transport floating at the bottom is now: the selection's toast stands above it (v0.6.4, spec 7).
    @State private var transportHeight: CGFloat = 0
    @ObservedObject private var contents: ContentsController
    /// Piece DNA (0018 section 11; testing item 22), from More: the map of the whole piece over the score; nil when closed.
    @State private var dna: DnaMap?
    /// Whether Piece DNA was opened from More, so it shows "‹ More".
    @State private var dnaFromMore = false
    @ObservedObject private var originals: OriginalsController
    @ObservedObject private var sources: SourcesController
    /// The other versions and the merge draft, which the score's marker, toast, switch, window and Sources show.
    @ObservedObject private var sourcesModel: SourcesModel
    @ObservedObject private var mergeModel: MergeModel
    /// The selection and Expand, which the score's menu, pill, bars, reactions and assistant show.
    @ObservedObject private var selectionModel: SelectionModel
    @ObservedObject private var focusModel: FocusModel
    /// A view of the switch shown over the score: another PDF or photos as they are, or other Notes; nil while none is.
    @State private var shownView: ShownView?

    init(piece: OpenPiece, switchView: ((Landing) async -> Void)?, viewing: String?, reopen: ((Landing) async -> Void)?, workspace: ScoreWorkspace) {
        self.piece = piece
        self.switchView = switchView
        self.viewing = viewing
        self.reopen = reopen
        _workspace = ObservedObject(wrappedValue: workspace)
        _player = ObservedObject(wrappedValue: workspace.player)
        _score = ObservedObject(wrappedValue: workspace.score)
        _roll = ObservedObject(wrappedValue: workspace.roll)
        _insights = ObservedObject(wrappedValue: workspace.insights)
        _assistant = ObservedObject(wrappedValue: workspace.assistant)
        _contents = ObservedObject(wrappedValue: workspace.contents)
        _originals = ObservedObject(wrappedValue: workspace.originals)
        _sources = ObservedObject(wrappedValue: workspace.sources)
        _sourcesModel = ObservedObject(wrappedValue: workspace.score.sourcesModel)
        _mergeModel = ObservedObject(wrappedValue: workspace.score.mergeModel)
        _selectionModel = ObservedObject(wrappedValue: workspace.score.selectionModel)
        _focusModel = ObservedObject(wrappedValue: workspace.score.focusModel)
    }

    /// Which panels are open (ScoreWorkspace.panels), as this body reads them.
    var panels: PanelState { workspace.panels }
    /// Whether the piece has other versions: the layers lists offer their marks (ScoreController.pieceExtra).
    private var hasVersions: Bool { !sourcesModel.versions.entries.isEmpty }

    var body: some View {
        GeometryReader { screen in
            let landscape = screen.size.width > screen.size.height
            let kept = drawerShare(landscape) * screen.size.height
            page(
                drawer: panels[.roll] ? (drawerDrag.map { $0 * screen.size.height } ?? kept) : 0,
                inset: panels[.roll] ? kept : 0,
                screen: screen.size,
                sheet: sheetPlace(landscape: landscape, height: screen.size.height),
                contentsPlace: panels[.contents] ? (landscape ? .side : .bottom(screen.size.height / 2)) : .none
            )
        }
        // The keyboard never changes the screen's size (#582 item 16): before, a chat's keyboard shrank it, portrait read
        // as landscape and the sheets, the drawer, the pill and the page's room moved, and back again as it went. A chat
        // keeps itself clear of the keyboard (AskFlow, KeyboardRoom).
        .ignoresSafeArea(.keyboard)
        .environmentObject(assistant)
        // One thing at a time on the right: the assistant's panel or the Insights, Check and Other versions sheet.
        .onChange(of: assistant.panelOpen) { _, open in workspace.assistantPanelChanged(open) }
        .onChange(of: panels.sheetOpen) { _, open in workspace.sheetChanged(open) }
    }

    /// Where the Insights sheet is: none, beside the score on the right, or over the lower half.
    enum SheetPlace: Equatable {
        case none, side
        case bottom(CGFloat)

        var bottomHeight: CGFloat {
            if case .bottom(let height) = self { return height }
            return 0
        }
    }

    /// What loops while a variation is heard: what play takes for the selection, on to the variation's end.
    private var variationRange: Range<Double>? {
        guard let range = focusModel.playRange?.range else { return nil }
        return range.lowerBound..<max(range.upperBound, insights.trial?.end ?? 0)
    }

    /// Piece DNA from More: the map music-core gives for the piece as it is now, the selection as its first idea.
    private func openDna(fromMore: Bool) {
        let id = piece.id
        // «Идеи по смыслу» (#582 item 15): the ideas by what they are in the music.
        let request = #"{"ideas":"musical"}"#
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: id, "dna", request) }
            guard let map = DnaMap(answer: answer) else { return }
            dnaFromMore = fromMore
            dna = map
        }
    }

    /// ‹ More (ScoreWorkspace.backToMore): the place opened from More goes, and More opens again once it has.
    private func backToMore(_ panel: PanelState.Panel?) {
        workspace.backToMore(panel)
    }

    /// What an action asks of the panels (ScoreWorkspace.change): only what it writes. The rules follow in the reactions.
    func change(_ intent: PanelState.Intent) {
        workspace.change(intent)
    }

    /// A panel for a sheet's or popover's `isPresented` or a toggle, as this body reads it: what is set goes through `change`.
    func panel(_ panel: PanelState.Panel) -> Binding<Bool> {
        panels.binding(panel, change: change)
    }

    /// What the sheet over the right holds: Sources, Check, or Insights.
    @ViewBuilder private func sheetContent() -> some View {
        if panels[.versions] && sourcesOn {
            sourcesSheet
        } else if panels[.check] {
            CheckDrawer(score: score, selectionModel: selectionModel, text: text, close: { change(.close(.check)) }, back: panels.fromMore.contains(.check) ? (label: text["bar.more"], action: { backToMore(.check) }) : nil)
        } else {
            InsightsSheet(insights: insights, score: score, selectionModel: selectionModel, player: player, range: variationRange, text: text) { change(.close(.insights)) }
        }
    }

    /// The Contents drawer where it goes: over the left of the page in landscape, from the bottom in portrait.
    @ViewBuilder private func contentsDrawer(_ place: SheetPlace) -> some View {
        switch place {
        case .none:
            EmptyView()
        case .side:
            ContentsDrawer(contents: contents, score: score, selectionModel: selectionModel, text: text) { change(.close(.contents)) }
                .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
                .dsShadow("shadow-sheet")
                .frame(width: ScoreScreen.contentsWidth)
                .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-2"))
                .padding([.leading, .bottom], CGFloat.ds("s-3"))
                .transition(.move(edge: .leading).combined(with: .opacity))
        case .bottom(let height):
            VStack {
                Spacer(minLength: 0)
                ContentsDrawer(contents: contents, score: score, selectionModel: selectionModel, text: text) { change(.close(.contents)) }
                    .clipShape(UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
                    .dsShadow("shadow-sheet")
                    .frame(height: height)
                    .padding(.horizontal, CGFloat.ds("s-3"))
            }
            .transition(.move(edge: .bottom))
            .ignoresSafeArea(edges: .bottom)
        }
    }

    /// The drawer's Expand: only with a selection (#521 19.1), without one the dimmed button says why. Expand inspects the
    /// passage, so the Pen, which would write nothing there, is put away first.
    private func expandFromDrawer() {
        guard selectionModel.selection != nil else {
            score.showNotice("roll.expandNeedsSelection")
            return
        }
        if score.penOn { score.penOn = false }
        change(.expandFromDrawer)
        score.expandRoll()
    }

    /// Over the bottom of the page: the chip back to the playing, the pill, and in practice view its own controls (#533 11).
    @ViewBuilder private var transport: some View {
        // Scrolled away from the playing, a chip leads back to it (testing item 4).
        if let away = score.playheadAway, player.state != .stopped, !focusModel.focused, score.practiceChrome.transport, let bar = player.barNumber(at: away) {
            Button {
                score.returnToPlayhead()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "play", size: CGFloat.ds("icon-s"))
                    Text(text.format("transport.backToPlay", String(bar))).dsType(.label)
                }
                .foregroundStyle(Color.ds("accent"))
                .padding(.horizontal, CGFloat.ds("s-3"))
                .frame(height: 32)
                .modifier(FloatingSurface())
            }
            .buttonStyle(.plain)
            .transition(.opacity)
        }
        // Practice view (#533 11): the pill gives way to Play/Pause and Leave, shown a few seconds after a tap.
        // The pill always shows (the playback model, v0.5.2, #479 item 22): it is where the music plays.
        if score.practiceChrome.transport {
            Pill(player: player, score: score, selectionModel: selectionModel, focusModel: focusModel, text: text)
                .transition(.opacity)
        }
        if score.practiceChrome.controls {
            PracticeControls(player: player, score: score, text: text)
                .transition(.opacity)
        }
    }

    /// Whether the selection's menu is open: a selection, outside Expand, the roll's drawer closed, and the selection
    /// controls shown (#582 item 10: `ScoreController.selectionControlsShown`, whatever the playback controls do).
    private var selectionMenuOpen: Bool {
        selectionModel.selection != nil && !focusModel.focused && !panels[.roll] && score.selectionControlsShown
    }

    /// The score and everything that floats over it.
    private func layers(drawer: CGFloat, screen: CGSize, sheet: SheetPlace, contentsPlace: SheetPlace, covered: CGFloat) -> some View {
        let gap = CGFloat.ds("s-3")
        let pillBottom = covered > 0 ? covered + gap : CGFloat.ds("s-4")
        // The full assistant's panel sits beside the score, which keeps the rest of the width (#582 item 17.1).
        let side = assistant.panelOpen ? ScoreScreen.assistantWidth(landscape: screen.width > screen.height) + gap : 0
        let pillTrailing: CGFloat = sheet == .side ? ScoreScreen.sheetWidth + gap : side
        let pillLeading: CGFloat = contentsPlace == .side ? ScoreScreen.contentsWidth + gap : 0
        return ScoreWebView(score: score, piece: piece, dark: colorScheme == .dark)
            // While a field has the keyboard, a press on the score only puts it away (TypingFocus, v0.6.4).
            .padding(.trailing, side).overlay { TypingShield() }
            .overlay(alignment: .topLeading) {
                // The menu over the selection, where the page places it (the web app's menuPlace). While the roll's drawer is
                // open it gives way to the drawer's head and the pill, so Loop is not shown twice (#338). Hidden with the selection
                // controls (#582 item 10), whatever the playback controls do.
                if selectionMenuOpen, let place = selectionModel.menuPlace {
                    SelectionMenu(score: score, selectionModel: selectionModel, focusModel: focusModel, player: player, contents: contents, text: text, rollOpen: panel(.roll), insightsOpen: panel(.insights))
                        .modifier(MenuDrag(score: score))
                        .offset(x: place.x, y: place.y)
                }
            }
            // Put away, the menu forgets where the player dragged it (#582 item 7).
            .onChange(of: selectionMenuOpen) { _, open in workspace.selectionMenuChanged(open) }
            .overlay(alignment: .topLeading) {
                // A version's marker tapped (testing item 33): the popover under it; a tap elsewhere puts it away.
                if let pick = mergeModel.markerPick, !focusModel.focused {
                    GeometryReader { area in
                        ZStack(alignment: .topLeading) {
                            Color.clear.contentShape(Rectangle()).onTapGesture { score.closeMarker() }
                            MarkerPopover(score: score, sourcesModel: sourcesModel, text: text, pick: pick)
                                .offset(x: min(max(CGFloat(pick.left), CGFloat.ds("s-3")), area.size.width - 312), y: CGFloat(pick.bottom) + CGFloat.ds("s-1"))
                        }
                    }
                }
            }
            .overlay(alignment: .bottom) {
                // What was just done in the Sources sheet, with Undo where it can be undone (version 0.6); beside the sheet.
                if let toast = sources.toast, sourcesOn {
                    SourcesToastView(sources: sources, toast: toast, text: text)
                        .padding(.bottom, pillBottom + 56)
                        .padding(.trailing, pillTrailing)
                        .transition(.opacity)
                } else if let toast = mergeModel.toast {
                    MergeToastView(score: score, text: text, toast: toast)
                        .padding(.bottom, pillBottom + 56)
                        .transition(.opacity)
                } else if score.practiceChrome.hints {
                    // «Выделение снято · Вернуть» and the hint to hold (#511 11), with ⌘Z and ⇧⌘Z; not in practice view (#533 11).
                    // Above the pill and whatever floats with it, as tall as that is now (v0.6.4, spec 7: never under Loop or the metronome).
                    SelectionToastView(score: score, selectionModel: selectionModel, focused: focusModel.focused, text: text, merging: mergeModel.draft != nil).padding(ToastPlace.insets(pillBottom: pillBottom, transport: transportHeight, gap: gap, leading: pillLeading, trailing: pillTrailing))
                }
            }
            .fullScreenCover(isPresented: Binding(get: { mergeModel.comparing }, set: { mergeModel.compare($0) })) {
                CompareVersionsScreen(score: score, mergeModel: mergeModel, text: text)
            }
            .overlay {
                // The phrase as a roll over the card (testing item 12); the drawer's roll moves here, so it shows once the drawer is gone.
                if workspace.rollPlace == .focus {
                    FocusRollCard(roll: roll, score: score, text: text)
                }
            }
            .ignoresSafeArea(edges: .bottom)
            .background(Color.ds("desk").ignoresSafeArea())
            .overlay(alignment: .top) {
                VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
                    if focusModel.focused {
                        FocusBar(score: score, selectionModel: selectionModel, focusModel: focusModel, player: player, variationRange: variationRange, text: text)
                    } else {
                        ScoreBar(title: piece.title, pages: score.pages, text: text, player: player, score: score, selectionModel: selectionModel, focusModel: focusModel, hasVersions: hasVersions, panels: panels, change: change, pdf: workspace.toPdf(switchView), openDna: { openDna(fromMore: true) }, openReference: { change(.open(.reference)) },
                                 sourceLayers: switchLayers, currentView: currentView, pickView: switchPick, viewing: viewing,
                                 width: screen.width - 2 * CGFloat.ds("s-4")) { dismiss() }
                    }
                    if sourcesOn && !focusModel.focused { recordingCapsule }
                    if !focusModel.focused {
                        // The chosen mode's options under the bar (#511 4.2, option B): the Pen's palette, Select's row.
                        ModeOptions(score: score, selectionModel: selectionModel, text: text, hasVersions: hasVersions)
                    }
                    if mergeModel.draft != nil && !focusModel.focused && !mergeModel.comparing {
                        MergeShowSwitch(score: score, mergeModel: mergeModel, text: text)
                            .transition(.opacity)
                    }
                }
                .padding(.horizontal, CGFloat.ds("s-4"))
                .padding(.top, CGFloat.ds("s-3"))
                .animation(.easeOut(duration: 0.16), value: score.penOn)
                .animation(.easeOut(duration: 0.16), value: focusModel.focused)
            }
            .overlay(alignment: .bottom) {
                if panels[.roll] {
                    RollDrawer(roll: roll, score: score, selectionModel: selectionModel, text: text, span: rollSpan, expand: expandFromDrawer, resize: { dy, ended in
                        resizeDrawer(by: dy, ended: ended, screen: screen)
                    }, step: { direction in
                        stepDrawer(direction, screen: screen)
                    }) { change(.close(.roll)) }
                        .frame(height: drawer)
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .transition(.move(edge: .bottom))
                        .ignoresSafeArea(edges: .bottom)
                }
            }
            .overlay(alignment: .bottomTrailing) {
                switch sheet {
                case .none:
                    EmptyView()
                case .side:
                    sheetContent()
                        .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
                        .dsShadow("shadow-sheet")
                        .frame(width: ScoreScreen.sheetWidth)
                        .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-2"))
                        .padding([.trailing, .bottom], CGFloat.ds("s-3"))
                        .transition(.move(edge: .trailing).combined(with: .opacity))
                case .bottom(let height):
                    sheetContent()
                        .clipShape(UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
                        .dsShadow("shadow-sheet")
                        .frame(height: height)
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .transition(.move(edge: .bottom))
                        .ignoresSafeArea(edges: .bottom)
                }
            }
            .overlay(alignment: .topLeading) { contentsDrawer(contentsPlace) }
            .overlay(alignment: .bottom) {
                // The pill rides above whatever comes up from the bottom, and beside a sheet or a drawer at the side. It
                // shows with a selection or while the music plays, or only while it plays (the setting
                // "guidance.transportPill", 0018 section 10).
                // Measured, so the selection's toast stands above it (v0.6.4, spec 7); above a hidden keyboard's bar (iPadOS's
                // «RU» and Undo), never above the keyboard itself (v0.6.4).
                VStack(spacing: CGFloat.ds("s-2")) { transport }.onGeometryChange(for: CGFloat.self) { $0.size.height } action: { transportHeight = $0 }.keyboardLift(barOnly: true)
                .padding(.bottom, pillBottom)
                .padding(.trailing, pillTrailing).padding(.leading, pillLeading)
            }
            .animation(.easeOut(duration: 0.2), value: panels[.roll])
            .animation(.easeOut(duration: 0.2), value: panels[.insights])
            .animation(.easeOut(duration: 0.2), value: panels[.contents])
            .ignoresSafeArea(.keyboard)
    }

    /// What the screen hands its controllers when it appears, and the project file written when it goes.
    private func wired(_ content: some View) -> some View {
        content
            .toolbar(.hidden, for: .navigationBar)
            .task { await workspace.appeared() }
            // A new interface language saves the piece and opens it again where it is; not a view shown over the piece.
            .handsOver(piece: viewing == nil ? piece.id : nil) { await workspace.handOverForLanguage() }
            .onDisappear { workspace.disappeared() }
            .overlay {
                // The pencil palette, opened by the Pencil's double tap or squeeze (#479 item 8), over everything else.
                if let call = score.pencilPalette {
                    PencilPalette(holder: score, call: call, text: text, modes: score.ringModes)
                }
            }
            .animation(.easeOut(duration: 0.16), value: score.pencilPalette)
            .onChange(of: scenePhase) { _, phase in workspace.scenePhaseChanged(phase) }
    }

    /// The drawers and sheets: what covers the page, and one of them at a time.
    private func watching(_ content: some View, covered: CGFloat, contentsSide: Bool) -> some View {
        content
            .onChange(of: player.hearingAlone) { _, alone in workspace.hearingAloneChanged(alone) }
            .onChange(of: covered) { _, height in workspace.coveredChanged(height) }
            .onChange(of: contentsSide) { _, side in workspace.contentsSideChanged(side) }
            .onChange(of: panels[.contents]) { _, open in workspace.contentsChanged(open) }
            .onChange(of: focusModel.focused) { _, focused in workspace.focusChanged(focused) }
            .onChange(of: score.practicing, initial: true) { _, on in workspace.practicingChanged(on) }
            .onChange(of: score.selectionControlsShown) { _, shown in workspace.selectionControlsChanged(shown) }
            .onChange(of: focusModel.focused && focusModel.asRoll) { _, shown in workspace.focusRollChanged(shown, dark: colorScheme == .dark) }
            .onChange(of: panels[.roll]) { _, open in workspace.rollDrawerChanged(open, dark: colorScheme == .dark) }
            .onChange(of: score.check) { _, _ in workspace.checkChanged() }
            .onChange(of: panels[.insights]) { _, open in workspace.insightsChanged(open) }
    }

    /// Check and Other versions take the place of the other drawers and sheets, and go when one of them opens. The Settings sheet comes
    /// over the score, and the score and the roll follow each change at once.
    private func checking(_ content: some View) -> some View {
        content
            .fullScreenCover(isPresented: Binding(get: { dna != nil }, set: { if !$0 { dna = nil } })) {
                if let map = dna {
                    DnaScreen(
                        map: map, title: piece.title, player: player, text: text,
                        back: dnaFromMore ? (label: text["bar.more"], action: { dna = nil; backToMore(nil) }) : nil,
                        close: { dna = nil },
                        showInScore: { range, openInsights in
                            dna = nil
                            Task {
                                await score.select(range.lowerBound, range.upperBound)
                                if openInsights { change(.open(.insights)) }
                            }
                        },
                        addBookmarks: {
                            let added = await contents.addForm()
                            return text.count("bookmark.structureAdded", added)
                        },
                        ask: {
                            // The map gives way to the score with the assistant beside it, on the whole piece.
                            dna = nil
                            assistant.open(wholePiece: true, selection: selectionModel.selection)
                        }
                    )
                }
            }
            // The PDF linked where it lies in Files (version 0.3): read from there, shown, never written into the piece.
            .fullScreenCover(item: $originals.linkedPdf, onDismiss: { originals.closeLinkedPdf() }) { pdf in
                PdfScreen(piece: pdf, linked: true).environmentObject(account)
            }
            .task(id: piece.id) { await workspace.loadOriginals() }
            .overlay(alignment: .center) {
                // Opening the linked PDF from the bar: iOS may first download it from iCloud Drive or Google Drive.
                if originals.loading != nil && !panels[.versions] {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        ProgressView()
                        Text(text["originals.loading"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                    }
                    .padding(CGFloat.ds("s-4"))
                    .modifier(FloatingSurface())
                }
            }
            .sheet(isPresented: panel(.reference)) {
                ReferenceSheet(text: text, settings: settings, piece: piece.id)
            }
            .sheet(isPresented: panel(.settings)) {
                SettingsSheet(settings: settings, account: account, text: text, pieceOverrides: score.pieceOverrides, clearPiece: { score.clearPieceOverrides() },
                              back: panels.fromMore.contains(.settings) ? (label: text["bar.more"], action: { backToMore(.settings) }) : nil)
            }
            .onChange(of: settings.values) { _, _ in workspace.settingsChanged() }
            .onChange(of: panels.placeFromMoreOpen) { _, open in workspace.placeFromMoreChanged(open) }
            .onChange(of: panels[.check]) { _, open in workspace.checkDrawerChanged(open) }
            .onChange(of: panels[.versions]) { _, open in workspace.sourcesChanged(open) }
            .onChange(of: panels.otherPlaceOpen(focused: focusModel.focused)) { _, open in workspace.otherPlaceChanged(open) }
    }

    /// What follows the selection, the player and the fact chosen.
    private func following(_ content: some View) -> some View {
        content
            .onChange(of: focusModel.version) { _, version in workspace.focusVersionChanged(version) }
            .onChange(of: insights.chosen) { _, chosen in workspace.factChosen(chosen) }
            .onChange(of: colorScheme) { _, scheme in workspace.appearanceChanged(scheme) }
            .onChange(of: workspace.rollPlace) { _, place in workspace.rollPlaceChanged(place) }
            .onChange(of: player.tick) { _, tick in workspace.playerMoved(tick) }
            .onChange(of: player.state) { old, state in workspace.playerStateChanged(from: old, to: state) }
            .onChange(of: selectionModel.selection) { _, selection in workspace.selectionChanged(selection) }
            .onChange(of: player.loop) { _, loop in workspace.loopChanged(loop) }
            .onChange(of: score.noteEditor) { _, editor in workspace.noteEditorChanged(editor) }
            .onChange(of: focusModel.playRange) { old, new in workspace.whatPlaysChanged(from: old, to: new) }
    }
}

/// The switch of all the piece's views and the Sources sheet (version 0.6; decision record
/// 0030). The working notes are this screen; the first PDF is the PDF view of PDF | Notes; another PDF or photos, and other
/// Notes, open on a screen of their own over this one, read-only, from a copy never saved; a version opens its page in
/// the sheet, as Other versions did; a recording's sound is heard from a capsule under the bar.
extension ScoreScreenContent {
    /// Where the sheet over the score goes: Insights, Check and Other versions beside the score in landscape and over the
    /// lower half in portrait; Sources always as a panel over the lower half (#582 item 4), so it is plainly apart from
    /// the document and its view.
    func sheetPlace(landscape: Bool, height: CGFloat) -> SheetPlace {
        if panels[.versions] && sourcesOn { return .bottom(height / 2) }
        return panels.sheetOpen ? (landscape ? .side : .bottom(height / 2)) : .none
    }

    /// A view of the switch on a screen of its own over the score, its piece closed in music-core as it goes.
    struct ShownView: Identifiable {
        let piece: OpenPiece
        /// Shown as a PDF (another original) rather than as notes.
        let pdf: Bool
        var id: Int32 { piece.id }
    }

    /// Whether the switch of all views and Sources are there: never on a view shown over the piece (0030).
    var sourcesOn: Bool { panels.sourcesOn }

    /// The Sources sheet in Other versions' place.
    var sourcesSheet: some View {
        SourcesSheet(
            sources: sources, score: score, originals: originals, text: text, current: currentView,
            close: { change(.close(.versions)) },
            back: panels.fromMore.contains(.versions) ? (label: text["bar.more"], action: { backToMore(.versions) }) : nil,
            open: pickView,
            versionPage: SourcesSheet.versionPage(sourcesModel),
            versionStatus: sourcesModel.status
        )
    }

    /// «Исполнение»: the recording's sound kept on this iPad, heard from under the bar.
    @ViewBuilder var recordingCapsule: some View {
        if let id = sources.performance, let projectId = sources.projectId, let url = OriginalSounds.file(project: projectId, original: id) {
            RecordingViewCapsule(url: url, title: sources.layers.original(id)?.shown ?? text["sources.rolePlaying"], text: text) {
                sources.performance = nil
            }
            .id(id)
            .transition(.opacity)
        }
    }

    /// The views shown over the score, the sources read as the screen opens, and a variation only tried left before the
    /// Sources sheet writes the project file.
    func sourcing(_ content: some View) -> some View {
        content
            .fullScreenCover(item: $shownView) { view in shownViewScreen(view) }
            .task(id: sourcesOn) { await workspace.loadSources(reopen: reopen, pick: pickView) }
            .onChange(of: panels[.versions]) { _, open in workspace.sourcesOpened(open) }
    }

    /// The view shown now: the recording heard, else the working notes.
    var currentView: String? { sources.performance ?? sources.layers.working }

    /// The views for the bar's switch, and what picking one does; nil while the switch is off.
    var switchLayers: SourceLayers? { sourcesOn ? sources.layers : nil }
    var switchPick: ((SourceLayers.ViewEntry) -> Void)? {
        guard sourcesOn else { return nil }
        return { view in pickView(view) }
    }

    /// A view picked in the switch, its menu or the sheet's «Открыть» (ScoreWorkspace.pickView), with what only the screen
    /// has: its way to the piece's other view, its texts and the cover a view is shown on.
    func pickView(_ view: SourceLayers.ViewEntry) {
        workspace.pickView(view, switchView: switchView, text: text) { shownView = $0 }
    }

    /// The screen of a view shown over the piece; its piece is closed in music-core once the screen has gone.
    func shownViewScreen(_ view: ShownView) -> some View {
        Group {
            if view.pdf {
                PdfScreen(piece: view.piece, linked: true, readOnlyNote: text["sources.sheet.viewOnly"])
            } else {
                ScoreScreen(piece: view.piece, viewing: text["sources.sheet.viewOnly"])
            }
        }
        .environmentObject(account)
        .environmentObject(settings)
        .onDisappear {
            let id = view.piece.id
            Task {
                // After the screen's own leaving, which still reads the piece.
                try? await Task.sleep(for: .seconds(2))
                _ = await MusicCore.run { MusicCore.close(piece: id) }
            }
        }
    }
}

/// The roll on the score's screen: its drawer's height (#511 section 7.1) and the span in its heading.
extension ScoreScreenContent {
    /// `drawer` is the roll drawer's height as shown, following its handle while dragged; `inset`, the height it was left at,
    /// is the room the page keeps under its last page, so the score is not laid out again on every move of the handle.
    private func page(drawer: CGFloat, inset: CGFloat, screen: CGSize, sheet: SheetPlace, contentsPlace: SheetPlace) -> some View {
        // The chain is cut into parts so the compiler type-checks each on its own.
        let covered = max(drawer, sheet.bottomHeight, contentsPlace.bottomHeight)
        let laid = layers(drawer: drawer, screen: screen, sheet: sheet, contentsPlace: contentsPlace, covered: covered)
            .overlay(alignment: .bottomTrailing) {
                // The full assistant beside the score, which layers narrowed for it (#582 item 17.1).
                if assistant.panelOpen {
                    AssistantSide(assistant: assistant, text: text, selection: selectionModel.selection, label: score.askLabel, landscape: screen.width > screen.height, drawer: drawer, ask: askAssistant)
                }
            }
        let room = max(inset, sheet.bottomHeight, contentsPlace.bottomHeight)
        return following(sourcing(checking(watching(wired(laid), covered: room, contentsSide: contentsPlace == .side))))
    }

    /// A question to the full assistant about what it looks at now: the selection, or the whole piece.
    private func askAssistant(_ question: String) {
        let whole = assistant.scope.isWholePiece(selectionModel.selection)
        let ask = AssistantAsk(
            piece: piece.id, selection: selectionModel.selection, wholePiece: whole, scope: whole ? text["ask.wholePiece"] : score.askLabel ?? "",
            language: text.language, role: AssistantSide.role(settings), relay: account.assistant
        )
        Task { await assistant.ask(question, about: ask) }
    }

    /// The drawer's share of the screen's height: as the player left it in this orientation, 40% in landscape and 30% in
    /// portrait until then.
    private func drawerShare(_ landscape: Bool) -> CGFloat {
        drawerShares[landscape] ?? RollDrawerHeight.standard(landscape: landscape)
    }

    /// The drawer's handle dragged by `dy` points (#511 section 7.1): the drawer follows it, and when let go keeps that
    /// height on the device, or goes away when dragged nearly off. The roll keeps its zoom, view and selection, and the
    /// music plays on.
    private func resizeDrawer(by dy: CGFloat, ended: Bool, screen: CGSize) {
        let landscape = screen.width > screen.height
        let share = RollDrawerHeight.share(from: drawerShare(landscape), dragged: dy, height: screen.height)
        guard ended else {
            drawerDrag = share ?? RollDrawerHeight.minShare
            return
        }
        withAnimation(.ds("t-fast")) {
            drawerDrag = nil
            if let share {
                drawerShares[landscape] = share
                RollDrawerHeight.keep(share, landscape: landscape)
            } else {
                change(.close(.roll))
            }
        }
    }

    /// A step of the drawer's height from VoiceOver's adjust (the web's arrow keys on the handle).
    private func stepDrawer(_ direction: CGFloat, screen: CGSize) {
        let landscape = screen.width > screen.height
        let share = RollDrawerHeight.clamped(drawerShare(landscape) + direction * RollDrawerHeight.step)
        withAnimation(.ds("t-fast")) { drawerShares[landscape] = share }
        RollDrawerHeight.keep(share, landscape: landscape)
    }

    /// The selection's span as bar.beat for the roll's heading, under the one playback position (v0.5.2); nil for its bars.
    private var rollSpan: String? {
        guard let range = focusModel.playRange?.range, let span = player.barBeats.span(range) else { return nil }
        return text.format("pill.span", span.first, span.last)
    }
}

/// The bar over the score (ds.css `.bar`): back to the library, the title, which opens the Contents the way a reader opens
/// a book at its contents, and the pages in view, then Phrases, Pen, the piano roll, Insights, Play and More, in the web
/// app's order.
struct ScoreBar: View {
    let title: String
    let pages: ScoreController.PagesInView
    let text: UIText
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    @ObservedObject var selectionModel: SelectionModel
    /// Handed on to Practice, which is off in Expand.
    let focusModel: FocusModel
    /// Whether the piece has other versions: the layers list offers their marks (ScoreController.pieceExtra).
    let hasVersions: Bool
    /// Which panels are open (PanelState), and the screen's way to change them.
    let panels: PanelState
    let change: (PanelState.Intent) -> Void
    /// PDF | Notes (0023): goes to the PDF the notes were read from; nil for notes with none.
    let pdf: (() -> Void)?
    /// Piece DNA from More.
    let openDna: () -> Void
    /// The Reference's circle of fifths; nil where there is none.
    var openReference: (() -> Void)?
    /// The piece's views for the switch of all views (version 0.6, 0030), in PDF | Notes' place with
    /// Sources after it.
    var sourceLayers: SourceLayers? = nil
    var currentView: String? = nil
    var pickView: ((SourceLayers.ViewEntry) -> Void)? = nil
    /// The read-only tag of a view shown over the piece; nil for the piece itself.
    var viewing: String? = nil
    /// The bar's width, the screen's less its margins (TopBar).
    var width: CGFloat = 0
    let back: () -> Void
    @State private var phrasesOpen = false
    /// The list of extra information under the ⓘ button's chevron.
    @State private var extraOpen = false
    @EnvironmentObject private var settings: DeviceSettings
    @EnvironmentObject private var assistant: AssistantController

    /// The bar is laid out as the PDF's (TopBar, #479 item 13, #582 item 2): Back, the Contents, the title and the switch
    /// in the same places in both views, then the pages in view, and each view's own actions at the right.
    var body: some View {
        TopBar(width: width, pages: pageText) {
            BarLeading(title: title, contentsOpen: panels.binding(.contents, change: change), text: text, back: back)
            if let sourceLayers, let pickView {
                SourcesViewSwitch(layers: sourceLayers, current: currentView, text: text, pick: pickView) { change(.open(.versions)) }
                SourcesButton(open: panels[.versions], text: text) { change(.toggle(.versions)) }
            } else if let pdf {
                PdfNotesSwitch(onPdf: false, text: text) { pdf() }
            }
            if let viewing { Tag(text: viewing, kind: .assumed) }
        } actions: {
            actions(compact: false)
        } compact: {
            actions(compact: true)
        }
    }

    /// In groups without captions, as the approved mockup v0.5.2 (#479 item 21): what the score shows (Phrases, the extra
    /// information, the piano roll), then how the score is used (the modes), then the Circle and
    /// Insights, then Play and More. Compact where the bar is short (#582 item 2): the chosen mode by its icon alone.
    @ViewBuilder private func actions(compact: Bool) -> some View {
        viewButtons
        PaletteDivider()
        // The modes (version 0.5): one group for the score and the roll, the Pen in it.
        ModeGroup(score: score, text: text, compact: compact)
        PaletteDivider()
        if let openReference {
            // The Reference: the Circle, the circle with a sharp, opens the circle of fifths.
            Button(action: openReference) {
                Icon(name: "keysig", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["reference.open"])
            .accessibilityHint(text["reference.openHint"])
        }
        Button {
            change(.toggle(.insights))
        } label: {
            Icon(name: "insight", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: panels[.insights]))
        .accessibilityLabel(text["bar.insights"])
        .accessibilityAddTraits(panels[.insights] ? .isSelected : [])
        PracticeButton(score: score, focusModel: focusModel, text: text)
        PaletteDivider()
        playAndMore
    }

    /// What the score shows: Phrases and its menu, the extra information and its list, and the piano roll.
    @ViewBuilder private var viewButtons: some View {
        Button {
            phrasesOpen.toggle()
        } label: {
            HStack(spacing: 0) {
                Icon(name: "phrases", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
                Icon(name: "chevd", size: CGFloat.ds("icon-s")).padding(.trailing, CGFloat.ds("s-1"))
            }
        }
        .buttonStyle(BarActionStyle(on: phrasesOpen))
        .accessibilityLabel(text["bar.phrases"])
        .accessibilityValue(selectionModel.phrasesHidden ? text["phrases.hide"] : "")
        .popover(isPresented: $phrasesOpen) { PhrasesPanel(score: score, selectionModel: selectionModel, text: text) }
        // Extra information (testing item 33): one press shows or hides what the app adds over the notes; the chevron
        // lists each kind and the player's own marks.
        HStack(spacing: 0) {
            let on = !score.hiddenLayers.contains("extra")
            Button {
                score.toggleExtra()
            } label: {
                Icon(name: "info", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: on))
            .accessibilityLabel(text["extra.toggle"])
            .accessibilityHint(text["extra.hint"])
            .accessibilityAddTraits(on ? .isSelected : [])
            Button {
                extraOpen.toggle()
            } label: {
                Icon(name: "chevd", size: CGFloat.ds("icon-s")).frame(width: 20, height: 40)
            }
            .buttonStyle(BarActionStyle(on: extraOpen))
            .accessibilityLabel(text["extra.list"])
            .popover(isPresented: $extraOpen) {
                LayersList(score: score, text: text, hasVersions: hasVersions)
                    .padding(CGFloat.ds("s-4"))
                    .frame(width: 300, alignment: .leading)
                    .presentationCompactAdaptation(.popover)
            }
        }
        Button {
            change(.toggle(.roll))
        } label: {
            Icon(name: "roll", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: panels[.roll]))
        .accessibilityLabel(text["bar.roll"])
        .accessibilityAddTraits(panels[.roll] ? .isSelected : [])
    }

    /// More: the pill, always shown, plays (the playback model, v0.5.2, #479 item 22).
    @ViewBuilder private var playAndMore: some View {
        Button {
            change(.toggle(.more))
        } label: {
            Icon(name: "more", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: panels[.more]))
        .accessibilityLabel(text["bar.more"])
        .popover(isPresented: panels.binding(.more, change: change)) {
            MorePanel(score: score, hasVersions: hasVersions, text: text, problem: player.problem ?? score.problem, openCheck: {
                change(.openFromMore(.check))
            }, openVersions: viewing == nil ? openSources : nil, openSettings: {
                change(.openFromMore(.settings))
            }, openDna: {
                change(.close(.more))
                openDna()
            }, askAboutPiece: {
                change(.close(.more))
                // The assistant's panel opens on the whole piece (#582 item 17.1).
                assistant.open(wholePiece: true, selection: selectionModel.selection)
            })
        }
    }

    /// Sources from More.
    private func openSources() {
        change(.openFromMore(.versions))
    }

    /// "p. 2 of 4", or "pp. 1–2 of 4" with two pages to a row; nothing before the score is laid out.
    private var pageText: String? {
        guard pages.count > 0 else { return nil }
        return pages.first == pages.last
            ? text.format("bar.page", String(pages.first), String(pages.count))
            : text.format("bar.pages", String(pages.first), String(pages.last), String(pages.count))
    }
}

/// The pill (ds.css `.pill`): play, the tempo and the metronome, floating at the bottom of the score.
struct Pill: View {
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    @ObservedObject var selectionModel: SelectionModel
    @ObservedObject var focusModel: FocusModel
    @ObservedObject private var metronome = Metronome.shared
    let text: UIText
    @EnvironmentObject private var settings: DeviceSettings

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            // The playback model (v0.5.2, #495 UX-10): one Play, cobalt or ink by the place, and Цикл with the span; in
            // Expand the same Play over its passage, which always loops (v0.6.4, spec 8).
            PositionTransport(player: player, passage: score.expandPassage, text: text)
            // The tempo observes only itself (#511 section 17), so it opens and changes while playing.
            TempoControl(tempo: player.tempo, text: text) { [weak score] perMinute in
                Task { await score?.setPieceTempo(perMinute) }
            }
            .equatable()
            handsControls
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            // The metronome clicks at once, with or without Play; held, a slide sets its tempo (#521 section 15).
            Button {
                metronome.toggle()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "metronome")
                    Text(text["transport.metronome"])
                }
            }
            .buttonStyle(DSItemStyle(on: metronome.running))
            .accessibilityAddTraits(metronome.running ? .isSelected : [])
            .modifier(MetronomeHold(metronome: metronome, text: text) { metronome.toggle() })
        }
        .disabled(!player.ready)
        .padding(.leading, 6)
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: 52)
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["transport.label"])
    }

    /// Hands separately at the instrument (#328): both, right or left, two touches away, and the
    /// phrase before or after without lifting it into focus. The hands are the ones music-core gives the notes, and the
    /// menu says when they were worked out rather than read from the file.
    @ViewBuilder private var handsControls: some View {
        if player.handsHeld.canSeparate {
            let label = text.format("pill.handsLabel", text[player.hands.labelKey])
            Menu {
                Picker(label, selection: $player.hands) {
                    ForEach(PlayerHands.allCases, id: \.self) { Text(text[$0.labelKey]).tag($0) }
                }
                if player.handsHeld.inferred {
                    Text(text["import.handsInferredWhy"])
                }
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "hands")
                    Text(text[player.hands.labelKey])
                }
            }
            .menuStyle(.button)
            .buttonStyle(DSItemStyle(on: player.hands != .both))
            .accessibilityLabel(label)
            .accessibilityHint(player.handsHeld.inferred ? text["import.handsInferredWhy"] : "")
        }
        if selectionModel.selection != nil && !focusModel.focused {
            step("back", text["focus.previous"], enabled: focusModel.canStepBack) { score.step(-1) }
            step("fwd", text["focus.next"], enabled: focusModel.canStepOn) { score.step(1) }
        }
    }

    private func step(_ icon: String, _ label: String, enabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Icon(name: icon).frame(width: 32, height: 32)
        }
        .buttonStyle(DSItemStyle())
        .disabled(!enabled)
        .accessibilityLabel(label)
    }
}

/// The menu over the selection (ds.css `.menu`, 0018 section 3): the actions a musician takes on a passage. It holds only
/// what applies and what the iPad has so far: Loop, Focus, Bookmark and Note, then the piano roll and Insights (none for a
/// passage without notes), and More with Highlight. Bookmark and Note turn the menu into their cards, as on the web. Its
/// actions show their icons alone (v0.5.2, #495 UX-11), each at a fingertip's square, with its word for VoiceOver.
struct SelectionMenu: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var selectionModel: SelectionModel
    @ObservedObject var focusModel: FocusModel
    @ObservedObject var player: Player
    @ObservedObject var contents: ContentsController
    let text: UIText
    @Binding var rollOpen: Bool
    @Binding var insightsOpen: Bool
    @State private var more = false
    @EnvironmentObject private var account: Account
    @EnvironmentObject private var settings: DeviceSettings
    @EnvironmentObject private var assistant: AssistantController

    var body: some View {
        Group {
            if score.noteDraft != nil {
                NoteCard(score: score, text: text)
                    .frame(width: 320)
                    .padding(CGFloat.ds("s-3"))
                    .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            } else if let draft = contents.draft, draft.id == nil {
                BookmarkCard(contents: contents, text: text) {
                    let selection = selectionModel.selection
                    let title = score.bookmarkTitle
                    Task { await contents.save(over: selection, defaultTitle: title) }
                }
                .frame(width: 300)
                .padding(.vertical, CGFloat.ds("s-3"))
                .padding(.horizontal, CGFloat.ds("s-4"))
                .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            } else if assistant.here != nil, selectionModel.selection != nil {
                // The assistant at the selection where the menu was (#582 items 17.2 and 17.4).
                AssistantHereCard(assistant: assistant, score: score, selectionModel: selectionModel, text: text, ask: askHere)
            } else {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    actions
                    if more {
                        Rectangle().fill(Color.ds("hairline")).frame(height: 1)
                        HighlightRow(score: score, selectionModel: selectionModel, text: text)
                        Button {
                            // The assistant opens at the selection, where the menu was (#582 item 17.2).
                            AssistantHereCard.open(assistant, score: score, settings: settings)
                        } label: {
                            HStack(spacing: 6) {
                                Icon(name: "insight")
                                Text(text["ask.menu"])
                            }
                        }
                        .buttonStyle(DSItemStyle())
                    }
                }
                .padding(.horizontal, CGFloat.ds("s-2"))
                .padding(.vertical, CGFloat.ds("s-1"))
                .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: more ? CGFloat.ds("r-l") : 26))
                .overlay(RoundedRectangle(cornerRadius: more ? CGFloat.ds("r-l") : 26).strokeBorder(Color.ds("hairline")))
            }
        }
        .dsShadow("shadow-menu")
        .fixedSize()
        .background {
            GeometryReader { proxy in
                Color.clear
                    .onAppear { score.menuSize(proxy.size) }
                    .onChange(of: proxy.size) { _, size in score.menuSize(size) }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["menu.label"])
    }

    private func askHere(_ question: String) {
        guard let role = assistant.here?.role, let ask = AssistantHereCard.ask(score: score, text: text, role: role, relay: account.assistant) else { return }
        Task { await assistant.askHere(question, about: ask) }
    }

    /// Where the selection starts and ends: as bar.beat, "9.1 – 12.3", under the one playback position (v0.5.2); beat by
    /// beat until the sound has given the bars' beats.
    private var menuSpan: String? {
        if let range = focusModel.playRange?.range, let span = player.barBeats.span(range) {
            return text.format("pill.span", span.first, span.last)
        }
        return selectionModel.selection?.beatSpan(text)
    }

    private var actions: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            // Where the selection starts and ends, by bar and beat.
            if let span = menuSpan {
                Text(span)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("accent"))
                    .padding(.horizontal, CGFloat.ds("s-2"))
                Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            }
            if !score.penOn {
                // Expand into focus: two arrows to the corners, unlike Insights' magnifier (#511 section 9).
                item("expand", text["focus.enter"], on: false) { score.enterFocus() }
                    .accessibilityHint(text["focus.enterHint"])
            }
            // Note before Bookmark, as the kit's menu; a sheet of paper, not the kit's speech bubble, which read as the
            // assistant (Andrei, 2026-11-18).
            item("memo", text["menu.note"], on: false) {
                contents.cancel()
                score.startNote()
            }
            item("bookmark", text["menu.bookmark"], on: false) {
                score.noteDraft = nil
                contents.start(title: score.bookmarkTitle)
            }
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            item("roll", text["bar.roll"], on: rollOpen) { rollOpen.toggle() }
            if !(selectionModel.selection?.noteIds.isEmpty ?? true) {
                item("insight", text["bar.insights"], on: insightsOpen) { insightsOpen.toggle() }
            }
            item("more", text["menu.more"], on: more || score.selectedHighlight != nil) { more.toggle() }
            // Hide (#533 11): the selection controls go, the selection staying and the playback controls as they are (#582
            // item 10); ✕ is the one that clears it.
            item("practice", text["menu.hide"], on: false) { score.practise(.selection(shown: false)) }
            ClearSelectionButton(score: score, focusModel: focusModel, text: text, iconOnly: true)
        }
    }

    /// An action of the menu as its icon alone (#495 UX-11): the word for VoiceOver and the pointer's tip.
    private func item(_ icon: String, _ label: String, on: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Icon(name: icon)
        }
        .buttonStyle(DSIconItemStyle(on: on))
        .accessibilityLabel(label)
        .accessibilityAddTraits(on ? .isSelected : [])
        .help(label)
    }
}

/// Clear selection (#338): the one way to let go of a selection, as a tap elsewhere on the page or the roll keeps it.
struct ClearSelectionButton: View {
    @ObservedObject var score: ScoreController
    /// Practice view is off in Expand (`ScoreController.practicing`).
    @ObservedObject var focusModel: FocusModel
    let text: UIText
    /// At a fingertip's square, as the selection menu's other icons (#495 UX-11); the roll's head keeps its size.
    var iconOnly = false

    var body: some View {
        let button = Button {
            score.clearSelection()
        } label: {
            Icon(name: StylusRules.RingControl.clearSelection.icon).frame(width: iconOnly ? nil : 32, height: iconOnly ? nil : 32)
        }
        Group {
            if iconOnly {
                button.buttonStyle(DSIconItemStyle())
            } else {
                button.buttonStyle(DSItemStyle())
            }
        }
        // Escape removes the selection too (the modes, version 0.5); in practice view it leaves that view (#533 11).
        .keyboardShortcut(!score.practicing ? KeyboardShortcut(.escape, modifiers: []) : nil)
        .accessibilityLabel(text["menu.clearSelection"])
        .help(text["menu.clearSelection"])
    }
}

/// The focus bar (0018 section 4, the web app's focus bar): Back to the page with its word, where the phrase is, and Play.
/// Escape on a keyboard goes back too. While a variation is tried, Original / Variation / Alternate choose what sounds;
/// with another version over the phrase and no variation, Notes / Version / Alternate, and Take into the score or
/// Restore these notes for the phrase's bars (0022, section 5). Reading / As played comes with transcriptions on the iPad.
/// The phrase as a roll in focus (testing item 12): a card over the page's card, as wide and as tall as the stage between the bar
/// and the pill, with the roll inside and the zoom on each axis floating over the notes. Expand inspects the passage (#521
/// 19.3): no mode switch and no note editor here, no arrows to another phrase; a draft of note edits waits in the drawer.
struct FocusRollCard: View {
    @ObservedObject var roll: RollController
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        RollSurface(roll: roll, place: .focus, text: text)
            .padding(CGFloat.ds("s-3"))
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("accent"), lineWidth: 2))
            .dsShadow("shadow-sheet")
            // The stage's insets, as Web/score.css `#focus .stage` has them.
            .padding(.horizontal, CGFloat.ds("s-4"))
            .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-5"))
            .padding(.bottom, 52 + CGFloat.ds("s-4") * 2)
    }
}

/// Notes | Piano roll in the focus bar (testing item 12), drawn as the hearing switch is.
struct FocusShowAs: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var focusModel: FocusModel
    let text: UIText

    var body: some View {
        HStack(spacing: 0) {
            option(false, "focus.asNotes")
            option(true, "focus.asRoll")
        }
        .padding(2)
        .frame(height: CGFloat.ds("h-compact"))
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["focus.showAs"])
    }

    private func option(_ roll: Bool, _ key: String) -> some View {
        let on = focusModel.asRoll == roll
        return Button {
            focusModel.showAsRoll(roll)
        } label: {
            SteadyWord(text: text[key], on: on)
                .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                .modifier(SegmentFill(on: on))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

struct FocusBar: View {
    @ObservedObject var score: ScoreController
    /// The card's position names the selection, from both models.
    @ObservedObject var selectionModel: SelectionModel
    @ObservedObject var focusModel: FocusModel
    @ObservedObject var player: Player
    let variationRange: Range<Double>?
    let text: UIText
    @EnvironmentObject private var assistant: AssistantController

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            // The way back by the way in (#521 17): «К странице» from the score, «Свернуть» from the roll; Escape too.
            let entry = focusModel.entry
            Button {
                score.leaveFocus()
            } label: {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: entry.backIcon, size: CGFloat.ds("icon-l"))
                    Text(text[entry.backKey]).dsType(.label)
                }
                .padding(.leading, CGFloat.ds("s-1"))
                .padding(.trailing, CGFloat.ds("s-3"))
                .frame(height: CGFloat.ds("h-control"))
            }
            .buttonStyle(BarActionStyle())
            .keyboardShortcut(.cancelAction)
            .help(text[entry.backHintKey])
            .accessibilityHint(text[entry.backHintKey])
            Text(focusModel.position(in: text))
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
                .monospacedDigit()
                .lineLimit(1)
                .truncationMode(.tail)
            FocusShowAs(score: score, focusModel: focusModel, text: text)
            Spacer(minLength: CGFloat.ds("s-2"))
            if player.hasVariation, let variationRange {
                HearingSwitch(player: player, range: variationRange, text: text, version: player.secondIsVersion ? focusModel.version?.name : nil)
            }
            if focusModel.version != nil, !player.hasVariation || player.secondIsVersion {
                // The way into the merge draft for this phrase (testing item 32); bars are no longer taken here.
                Button(text["merge.openDraft"]) { score.openDraftFromFocus() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            Button {
                // The assistant's panel opens on the phrase in focus (#582 item 17.1).
                assistant.open(wholePiece: false, selection: selectionModel.selection)
            } label: {
                Text(text["ask.menu"]).dsType(.label).padding(.horizontal, CGFloat.ds("s-2")).frame(height: 40)
            }
            .buttonStyle(BarActionStyle(on: assistant.panelOpen))
            // No Play here (#511 section 8.1): the pill's blue Play is the one, and the pill always shows in focus.
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
    }
}

/// Phrases, from the bar (0018 section 3; the approved mockup v0.5.2, #479 item 21): the level focus steps by, Short,
/// Phrases or Sentences, each with how many units the piece has at it, or «Не показывать», then «Вернуть найденные» for
/// a level the player changed. It never lists the phrases, and since #479 item 14 the score does not draw them.
struct PhrasesPanel: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var selectionModel: SelectionModel
    let text: UIText

    private static let names = ["short": "phrases.levelShort", "phrase": "phrases.levelPhrases", "sentence": "phrases.levelSentences"]

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(text["phrases.levelTitle"]).overline().padding(.bottom, CGFloat.ds("s-1"))
            ForEach(PhraseLevelsShown.ids, id: \.self) { level in
                let count = selectionModel.phrases?.counts[level]
                row(text[Self.names[level] ?? ""], count: count.map(String.init), on: !selectionModel.phrasesHidden && (selectionModel.phrases?.level ?? "phrase") == level) {
                    score.showLevel(level)
                }
            }
            PaletteRule()
            row(text["phrases.hide"], count: nil, on: selectionModel.phrasesHidden) {
                selectionModel.hidePhrases(true)
            }
            .accessibilityHint(text["phrases.hideHint"])
            PaletteRule()
            Button(text["phrases.resetLevel"]) { score.resetLevel() }
                .buttonStyle(DSItemStyle())
                .disabled(selectionModel.phrasesHidden || !(selectionModel.phrases.map { $0.edited.contains($0.level) } ?? false))
                .accessibilityHint(text["phrases.resetHint"])
        }
        .padding(CGFloat.ds("s-3"))
        .frame(width: 300, alignment: .leading)
        .presentationCompactAdaptation(.popover)
    }

    /// A choice of the menu: a round mark, filled while it is chosen, its word and, for a level, its count.
    private func row(_ title: String, count: String?, on: Bool, choose: @escaping () -> Void) -> some View {
        Button(action: choose) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Circle()
                    .strokeBorder(on ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: on ? 5 : 1.5)
                    .frame(width: 16, height: 16)
                Text(title).lineLimit(1)
                Spacer(minLength: CGFloat.ds("s-2"))
                if let count {
                    Text(count).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(DSItemStyle(on: on))
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// A thin rule across a menu, between its groups.
private struct PaletteRule: View {
    var body: some View {
        Rectangle().fill(Color.ds("hairline")).frame(height: 1).padding(.vertical, CGFloat.ds("s-1"))
    }
}

extension PanelState {
    /// A panel as a binding for the body that read this value (a sheet's or popover's `isPresented`, a toggle): what it sets
    /// goes through `change`.
    func binding(_ panel: Panel, change: @escaping (Intent) -> Void) -> Binding<Bool> {
        Binding(get: { self[panel] }, set: { change($0 ? .open(panel) : .close(panel)) })
    }
}

/// More, from the bar: Check for a piece with flags, Other versions, the layers of marks, what the app owes the piano and the engraving (CC BY 3.0 asks for the piano's
/// credit wherever it plays), and what went wrong, if anything did. The layout, the PDF and the other tools come with later
/// steps.
struct MorePanel: View {
    @ObservedObject var score: ScoreController
    /// Whether the piece has other versions: the layers list offers their marks (ScoreController.pieceExtra).
    let hasVersions: Bool
    let text: UIText
    let problem: String?
    let openCheck: () -> Void
    /// Sources; nil on a view shown over the piece, which has none.
    let openVersions: (() -> Void)?
    let openSettings: () -> Void
    let openDna: () -> Void
    let askAboutPiece: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            if let problem {
                Text(problem)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("danger"))
            }
            if score.check.hasFlags {
                Button(action: openCheck) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "check")
                        Text(score.check.open > 0 ? text.count("check.toReview", score.check.openBars.count) : text["check.title"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
                Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            }
            // The piece's sources (0030): its originals and the notes read from them.
            if let openVersions {
                Button(action: openVersions) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "mic")
                        Text(text["sources.title"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
            // Piece DNA, the map of the whole piece, and the assistant on the whole piece (testing items 22, 23).
            Button(action: openDna) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "dial")
                    Text(text["dna.title"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Button(action: askAboutPiece) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "insight")
                    Text(text["ask.aboutPiece"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            // «Мои правки» (version 0.5): the player's corrections laid over the notes or left out, once there are some.
            if score.editsLayer?.shows == true {
                EditsLayerSection(score: score, text: text)
                Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            }
            LayersList(score: score, text: text, hasVersions: hasVersions)
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            Button(action: openSettings) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "dial")
                    Text(text["settings.all"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            Text(credit)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .tint(Color.ds("accent"))
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 340, alignment: .leading)
        .presentationCompactAdaptation(.popover)
    }

    private var credit: AttributedString {
        let markdown = "\(text["credit.piano"]) [Salamander Grand Piano V3](https://archive.org/details/SalamanderGrandPianoV3) \(text["credit.by"]) Alexander Holm, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/), \(text["credit.shortened"]) \(text["credit.engraving"]) [Verovio](https://www.verovio.org) 6.3.0, [LGPL 3.0](https://www.gnu.org/licenses/lgpl-3.0.html), [\(text["credit.source"])](https://github.com/rism-digital/verovio/tree/version-6.3.0)."
        return (try? AttributedString(markdown: markdown)) ?? AttributedString(markdown)
    }
}

struct ScoreWebView: UIViewRepresentable {
    @ObservedObject var score: ScoreController
    let piece: OpenPiece
    let dark: Bool

    /// A web view made at launch, so the first score does not wait for WebKit's process to start (0019).
    @MainActor private static var warm: WKWebView?

    @MainActor static func prewarm() {
        guard warm == nil else { return }
        let view = WKWebView(frame: .zero, configuration: WKWebViewConfiguration())
        view.loadHTMLString("<!doctype html><title></title>", baseURL: nil)
        warm = view
    }

    func makeUIView(context: Context) -> WKWebView {
        score.webView
    }

    func updateUIView(_ view: WKWebView, context: Context) {
        score.show(piece, dark: dark)
        // The stylus menu opens where the Pencil is over any part of the screen, the roll's too (#521 7).
        score.watchStylus(in: view.window)
    }
}
