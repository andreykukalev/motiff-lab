import SwiftUI

/// A piece's score (0018 section 2, design/kit/screens/s2_score_page.png): the score is the whole screen, A4 pages in one
/// column in portrait and two to a row in landscape, with the pencil marks on them (0019 section 2.2). Verovio's SVG is in
/// one web view that runs the web app's score modules (ScoreController, Web/score.html). Nothing is docked: the bar floats
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
    /// Opens another project file in this screen's place (v1.2, collage: «Открыть» on the toast of a fragment added to
    /// another piece's draft); on a view shown over the piece, the piece under it does. Nil where there is no way.
    var openFile: ((URL, Landing) async -> Void)? = nil
    /// The screen's controllers and its panels (ScoreWorkspace): made once, as the screen first appears, and gone with it.
    @StateObject private var workspace: ScoreWorkspace

    /// The Insights sheet's width beside the score in landscape (ds.css `.sheet`).
    static let sheetWidth: CGFloat = 380
    /// The Contents drawer's width over the left of the page in landscape (ds.css `.sheet.left`).
    static let contentsWidth: CGFloat = 320
    /// A card that opens over the score at a tap (the settings, a draft's приём).
    static let popoverWidth: CGFloat = 340

    /// The piece's screen, its originals known from the piece's opening (#495 BUG-02).
    /// `assistant` is the one of a screen that outlasts this one (a draft's, `ShownDraftScreen`); nil, it is the screen's own.
    init(
        piece: OpenPiece, switchView: ((Landing) async -> Void)? = nil, viewing: String? = nil, reopen: ((Landing) async -> Void)? = nil,
        openFile: ((URL, Landing) async -> Void)? = nil, assistant: AssistantController? = nil
    ) {
        self.piece = piece
        self.switchView = switchView
        self.viewing = viewing
        self.reopen = reopen
        self.openFile = openFile
        _workspace = StateObject(wrappedValue: ScoreWorkspace(piece: piece, viewing: viewing, assistant: assistant))
    }

    var body: some View {
        ScoreScreenContent(piece: piece, switchView: switchView, viewing: viewing, reopen: reopen, openFile: openFile, workspace: workspace)
    }
}

/// What the score's screen shows: the score and everything over it, and the reactions between the workspace's controllers.
/// It observes each controller itself, as the screen did while it owned them, so it is drawn again exactly when it was.
/// Its parts by concern are extensions beside it, each in its own file: the bars at the top and the transport at the
/// bottom (`ScoreScreenBars`), the sheets and drawers (`ScoreScreenPanels`), the roll's drawer (`ScoreScreenRoll`), the
/// views of the switch and Sources (`ScoreScreenSources`), and the covers and reactions (`ScoreScreenReactions`). This file
/// lays out the layers and holds what the view shows.
struct ScoreScreenContent: View {
    let piece: OpenPiece
    let switchView: ((Landing) async -> Void)?
    let viewing: String?
    let reopen: ((Landing) async -> Void)?
    let openFile: ((URL, Landing) async -> Void)?
    let text = UIText.current
    @Environment(\.colorScheme) var colorScheme
    @Environment(\.scenePhase) var scenePhase
    @Environment(\.dismiss) var dismiss
    /// The piece under this screen when it is shown over it: its recording is heard under this bar.
    @Environment(\.heardOver) var pieceUnder
    @EnvironmentObject var account: Account
    @EnvironmentObject var settings: DeviceSettings
    @ObservedObject var workspace: ScoreWorkspace
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    @ObservedObject var roll: RollController
    @ObservedObject var insights: InsightsController
    @ObservedObject var assistant: AssistantController
    /// The roll drawer's share of the screen's height in landscape (true) and portrait, as the player left it with its handle
    /// (RollDrawerHeight, #511 section 7.1), and the share while the handle is dragged.
    @State var drawerShares: [Bool: CGFloat] = [true: RollDrawerHeight.saved(landscape: true), false: RollDrawerHeight.saved(landscape: false)]
    @State var drawerDrag: CGFloat?
    /// How tall the transport floating at the bottom is now: the selection's toast stands above it (v0.6.4, spec 7).
    @State var transportHeight: CGFloat = 0
    @ObservedObject var contents: ContentsController
    /// Piece DNA (0018 section 11; testing item 22), from More: the map of the whole piece over the score; nil when closed.
    @State var dna: DnaMap?
    /// Whether Piece DNA was opened from More, so it shows "‹ More".
    @State var dnaFromMore = false
    @ObservedObject var originals: OriginalsController
    @ObservedObject var sources: SourcesController
    /// The other versions, which the score's marker and Sources show.
    @ObservedObject var sourcesModel: SourcesModel
    /// The selection and Expand, which the score's menu, pill, bars, reactions and assistant show.
    @ObservedObject var selectionModel: SelectionModel
    @ObservedObject var focusModel: FocusModel
    @ObservedObject var penModel: PenModel
    @ObservedObject var marksModel: MarksModel
    @ObservedObject var checkModel: CheckModel
    /// A view of the switch shown over the score: another PDF or photos as they are, other Notes, or a draft; nil while
    /// none is.
    @State var shownView: ShownView?
    /// The «В черновик» sheet for the selection (v1.1); nil while it is not open.
    @State var draftSheet: DraftSheetRequest?
    /// The draft just made there with «С ассистентом · Pro» (v1.5 d-11), whose view opens once the sheet has gone.
    @State var assisted: String?
    /// A draft's map, open or not, kept by the draft's screen (`ShownDraftScreen`); nil on any other screen.
    @Environment(\.draftMap) var draftMap
    /// Where the pill of a draft's view ends, in the layers' space: in landscape the map starts below it.
    @State var draftPillBottom: CGFloat = 0

    init(piece: OpenPiece, switchView: ((Landing) async -> Void)?, viewing: String?, reopen: ((Landing) async -> Void)?, openFile: ((URL, Landing) async -> Void)?, workspace: ScoreWorkspace) {
        self.piece = piece
        self.switchView = switchView
        self.viewing = viewing
        self.reopen = reopen
        self.openFile = openFile
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
        _selectionModel = ObservedObject(wrappedValue: workspace.score.selectionModel)
        _focusModel = ObservedObject(wrappedValue: workspace.score.focusModel)
        _penModel = ObservedObject(wrappedValue: workspace.score.penModel)
        _marksModel = ObservedObject(wrappedValue: workspace.score.marksModel)
        _checkModel = ObservedObject(wrappedValue: workspace.score.checkModel)
    }

    /// Which panels are open (ScoreWorkspace.panels), as this body reads them.
    var panels: PanelState { workspace.panels }
    /// Whether the piece has other versions: the layers lists offer their marks (MarksModel.pieceExtra).
    var hasVersions: Bool { !sourcesModel.versions.entries.isEmpty }

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
        .onChange(of: panels.sheetOpen) { _, open in
            workspace.sheetChanged(open)
            if open { draftMap?.wrappedValue = nil }
        }
        .onChange(of: draftMap?.wrappedValue) { _, open in
            if open != nil { workspace.draftMapOpened() }
        }
    }

    /// What loops while a variation is heard: what play takes for the selection, on to the variation's end.
    var variationRange: Range<Double>? {
        guard let range = focusModel.playRange?.range else { return nil }
        return range.lowerBound..<max(range.upperBound, insights.trial?.end ?? 0)
    }

    /// ‹ More (ScoreWorkspace.backToMore): the place opened from More goes, and More opens again once it has.
    func backToMore(_ panel: PanelState.Panel?) {
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

    /// Whether the selection's menu is open: a selection, outside Expand, the roll's drawer closed, and the selection
    /// controls shown (#582 item 10: `ScoreController.selectionControlsShown`, whatever the playback controls do).
    private var selectionMenuOpen: Bool {
        selectionModel.selection != nil && !focusModel.focused && !panels[.roll] && score.selectionControlsShown
    }

    /// How high the pill floats: over whatever covers the bottom of the score, else at the screen's edge.
    private static func pillBottom(covered: CGFloat) -> CGFloat {
        covered > 0 ? covered + CGFloat.ds("s-3") : CGFloat.ds("s-4")
    }

    /// How far the pill and the toasts keep from the right: beside a sheet at the side, else beside the assistant's panel
    /// (`side`, its width and a gap, or 0).
    private static func pillTrailing(sheet: SheetPlace, side: CGFloat) -> CGFloat {
        sheet == .side ? ScoreScreen.sheetWidth + CGFloat.ds("s-3") : side
    }

    /// The layers' coordinate space, in which a sheet at the side is placed under the pills over the score.
    static let layersSpace = "layers"

    /// Where what floats over the score keeps below the bar: under it, with a gap.
    static var barBottom: CGFloat {
        CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-2")
    }

    /// Where the bar, and on a draft's view the draft's pill under it, end: a card at a mark keeps below them (v1.4 a-1).
    var barsBottom: CGFloat {
        piece.draft == nil ? Self.barBottom : max(Self.barBottom, draftPillBottom + CGFloat.ds("s-2"))
    }

    /// Where a toast stands: over the pill, a touch's height and a gap above where the pill floats.
    private static func toastBottom(covered: CGFloat) -> CGFloat {
        pillBottom(covered: covered) + CGFloat.ds("h-touch") + CGFloat.ds("s-3")
    }

    /// The score and everything that floats over it.
    private func layers(drawer: CGFloat, screen: CGSize, sheet: SheetPlace, contentsPlace: SheetPlace, covered: CGFloat) -> some View {
        let gap = CGFloat.ds("s-3")
        let pillBottom = Self.pillBottom(covered: covered)
        // The full assistant's panel sits beside the score, which keeps the rest of the width (#582 item 17.1).
        let side = assistant.panelOpen ? ScoreScreen.assistantWidth(landscape: screen.width > screen.height) + gap : 0
        let pillTrailing = Self.pillTrailing(sheet: sheet, side: side)
        let pillLeading: CGFloat = contentsPlace == .side ? ScoreScreen.contentsWidth + gap : 0
        // What the bars at the top and the pill, a drawer or a sheet at the bottom cover: a card at a mark opens where it
        // shows whole between them (v1.4 a-1), the pill's top as the selection's toast keeps above it.
        let controls = EdgeInsets(top: barsBottom, leading: 0, bottom: ToastPlace.bottom(pillBottom: pillBottom, transport: transportHeight, gap: gap), trailing: 0)
        return ScoreWebView(score: score, piece: piece, dark: colorScheme == .dark)
            // While a field has the keyboard, a press on the score only puts it away (TypingFocus, v0.6.4).
            .padding(.trailing, side).overlay { TypingShield() }
            .overlay(alignment: .topLeading) { selectionMenu }
            // Put away, the menu forgets where the player dragged it (#582 item 7).
            .onChange(of: selectionMenuOpen) { _, open in workspace.selectionMenuChanged(open) }
            .overlay(alignment: .topLeading) { cardAtMark(covered: controls) }
            .overlay(alignment: .bottom) {
                let insets = ToastPlace.insets(pillBottom: pillBottom, transport: transportHeight, gap: gap, leading: pillLeading, trailing: pillTrailing)
                toasts(covered: covered, insets: insets, trailing: pillTrailing)
            }
            .overlay {
                // The phrase as a roll over the card (testing item 12); the drawer's roll moves here, so it shows once the drawer is gone.
                if workspace.rollPlace == .focus {
                    FocusRollCard(roll: roll, score: score, text: text)
                }
            }
            .ignoresSafeArea(edges: .bottom)
            .background(Color.ds("desk").ignoresSafeArea())
            .overlay(alignment: .top) { bars(width: screen.width - 2 * CGFloat.ds("s-4")) }
            .overlay(alignment: .bottom) { rollDrawer(height: drawer, screen: screen) }
            .overlay(alignment: .bottomTrailing) { sideSheet(sheet) }
            .overlay(alignment: .topLeading) { contentsDrawer(contentsPlace) }
            .overlay(alignment: .bottom) { transportLayer(bottom: pillBottom, leading: pillLeading, trailing: pillTrailing) }
            .animation(.easeOut(duration: 0.2), value: panels[.roll])
            .animation(.easeOut(duration: 0.2), value: panels[.insights])
            .animation(.easeOut(duration: 0.2), value: panels[.contents])
            .ignoresSafeArea(.keyboard)
    }

    /// The menu over the selection, where the page places it (the web app's menuPlace). While the roll's drawer is open it
    /// gives way to the drawer's head and the pill, so Loop is not shown twice (#338). Hidden with the selection controls
    /// (#582 item 10), whatever the playback controls do.
    @ViewBuilder private var selectionMenu: some View {
        if selectionMenuOpen, let place = selectionModel.menuPlace {
            SelectionMenu(score: score, selectionModel: selectionModel, focusModel: focusModel, penModel: penModel, marksModel: marksModel, player: player, contents: contents, text: text, insightsOpen: panel(.insights), toDraft: workspace.draftOwner(under: pieceUnder) == nil ? nil : { draftSheet = workspace.draftSheet() }, viewOnly: viewing != nil)
                .modifier(MenuDrag(score: score))
                .offset(x: place.x, y: place.y)
        }
    }

    /// The card at a mark tapped, under it, between the bars and what covers the bottom (`covered`).
    @ViewBuilder private func cardAtMark(covered controls: EdgeInsets) -> some View {
        // A version's marker tapped (testing item 33): the popover at it, as every card at a mark (v1.4 a-1); a tap
        // elsewhere puts it away.
        if let pick = sourcesModel.markerPick, !focusModel.focused {
            DraftCardAtMark(at: CGPoint(x: pick.left, y: pick.bottom), mark: score.markHeight, covered: controls, close: { score.closeMarker() }) {
                MarkerPopover(score: score, sourcesModel: sourcesModel, text: text, pick: pick)
            }
        } else if let pick = score.phraseMenu, let unit = selectionModel.phrases?.unit(pick.number), !focusModel.focused {
            // A phrase's number held (v1.2): its mark's menu under it (ScorePhraseEdits.swift).
            DraftCardAtMark(at: pick.at, mark: score.markHeight, covered: controls, close: { score.closePhraseMenu() }) {
                PhraseMarkMenu(score: score, unit: unit, first: unit.number == selectionModel.phrases?.units.first?.number, text: text)
            }
        } else if let draft = piece.draft, let owner = pieceUnder, !focusModel.focused {
            // A draft's приём number or seam's diamond tapped (v1.1): its card under it (DraftSeamCard.swift).
            DraftViewCards(score: score, seams: score.seamsModel, anchors: score.anchorsModel, player: player, owner: owner, draft: draft, text: text, covered: controls, reopen: reopen)
        }
    }

    /// What was just done in the Sources sheet, else the selection's toasts, over the pill (`insets`) and beside a sheet.
    @ViewBuilder private func toasts(covered: CGFloat, insets: EdgeInsets, trailing pillTrailing: CGFloat) -> some View {
        // What was just done in the Sources sheet, with Undo where it can be undone (version 0.6); beside the sheet.
        if let toast = sources.toast, sourcesOn {
            SourcesToastView(sources: sources, toast: toast, text: text)
                .padding(.bottom, Self.toastBottom(covered: covered))
                .padding(.trailing, pillTrailing)
                .transition(.opacity)
        } else if score.practiceChrome.hints {
            // «Выделение снято · Вернуть» and the hint to hold (#511 11), with ⌘Z and ⇧⌘Z; not in practice view (#533 11).
            // Above the pill and whatever floats with it, as tall as that is now (v0.6.4, spec 7: never under Loop or the metronome).
            // Over it, the toast of an edit of the phrases with its Undo or Redo (v1.2, ScorePhraseEdits.swift).
            VStack(spacing: CGFloat.ds("s-2")) {
                toastsOverSelection
                SelectionToastView(score: score, selectionModel: selectionModel, focused: focusModel.focused, text: text)
            }
            .padding(insets)
        }
    }

    /// `drawer` is the roll drawer's height as shown, following its handle while dragged; `inset`, the height it was left at,
    /// is the room the page keeps under its last page, so the score is not laid out again on every move of the handle.
    private func page(drawer: CGFloat, inset: CGFloat, screen: CGSize, sheet: SheetPlace, contentsPlace: SheetPlace) -> some View {
        // The chain is cut into parts so the compiler type-checks each on its own.
        let covered = max(drawer, sheet.bottomHeight, contentsPlace.bottomHeight)
        let laid = layers(drawer: drawer, screen: screen, sheet: sheet, contentsPlace: contentsPlace, covered: covered)
            .coordinateSpace(name: Self.layersSpace)
            .overlay(alignment: .bottomTrailing) {
                // The full assistant beside the score, which layers narrowed for it (#582 item 17.1).
                if assistant.panelOpen {
                    AssistantSide(assistant: assistant, text: text, selection: selectionModel.selection, label: score.askLabel, landscape: screen.width > screen.height, drawer: drawer, ask: askAssistant) { offer in
                        Task { await score.practiseSuggestion(offer, on: player) }
                    }
                }
            }
            .overlay(alignment: .bottom) {
                // A step just taken on a draft's view, with its Undo (v1.1), where the Sources toast stands, beside the map.
                if piece.draft != nil, let owner = pieceUnder {
                    DraftStepToast(owner: owner, score: score, text: text, reopen: reopen)
                        .padding(.bottom, Self.toastBottom(covered: covered))
                        .padding(.trailing, Self.pillTrailing(sheet: sheet, side: 0))
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
}
