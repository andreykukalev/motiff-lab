import Combine
import PDFKit
import SwiftUI
import UniformTypeIdentifiers
import WebKit

/// The score and the pencil marks on it (decision record 0019, sections 1.4 and 2.2). The pages are Verovio's SVG in one
/// web view, which runs the web app's own score modules (Web/score.js over Scripts/score) to know where each bar is
/// and to draw the marks. The pen writes through InkInput, which draws the stroke as it is written with the outline and
/// grain the page keeps it with (#479 item 7): when a stroke ends, its points go to the page to be kept against the bar
/// they start in, music-core saves the stroke in the project file (`ink.add`, `marks.ink` in project file version 12),
/// and the marks are drawn again from what music-core answers, so a mark lives only in the project file and shows in
/// the same place in the web app. Who writes, selects and scrolls with a finger or the Pencil is InputRules' (#479
/// item 18): the stylus follows the tool, a finger moves around the music.
@MainActor
final class ScoreController: NSObject, ObservableObject, PenHolder {
    enum Tool: Equatable {
        case pen(Ink)
        case highlighter
        case eraser
    }

    /// The four inks of the design kit (0018 section 5); the names are the project file's.
    enum Ink: String, CaseIterable {
        case graphite, carmine, forest, ochre

        var labelKey: String {
            switch self {
            case .graphite: "pencil.penGraphite"
            case .carmine: "pencil.penCarmine"
            case .forest: "pencil.penForest"
            case .ochre: "pencil.penOchre"
            }
        }

        var color: UIColor { DesignTokens.color("ink-\(rawValue)") }
    }

    /// The pen's one tool and each tool's settings (PenState, #495 UX-06), as last chosen on this device (PenMemory): the
    /// same after PDF | Notes and when the piece opens again.
    @Published var pen = PenMemory.saved() {
        didSet { if pen != oldValue { PenMemory.keep(pen) } }
    }
    /// The bar's Pen (0018 section 5): while it is on, the Pencil writes; on an iPad without a stylus one finger writes
    /// and two fingers scroll, so the score takes marks with or without a Pencil (InputRules).
    @Published var penOn = false {
        didSet {
            applyInput()
            penChanged?(penOn)
            if shown { Task { await scorePage.send(.pen, penOn) } }
            sendMode()
        }
    }
    /// The mode the bar chose (version 0.5; the only model since v0.6.5), other than the Pen.
    @Published var mode: InteractionMode {
        didSet {
            guard mode != oldValue else { return }
            applyInput()
            sendMode()
        }
    }
    /// The Pen turned on or off: the roll's rules follow it (#511 4.1).
    var penChanged: (@MainActor (Bool) -> Void)?
    /// The bar's mode or the Pen changed: the roll reads presses by the same tool (#543 2, RollInput.barTool).
    var toolChanged: (@MainActor (InteractionMode, Bool) -> Void)?
    /// Whether the roll is in Edit notes: then Undo is the note edits' (#521 9).
    var rollEditing: (@MainActor () -> Bool)?
    /// A tap on a beat under the modes, from its beat line to the next: a playhead goes there.
    var placeTapped: (@MainActor (Range<Double>) -> Void)?
    /// A playhead's handle taken, dragged or let go on the page (#495 UX-08): the player runs the model (PlayheadDrag.swift).
    var playheadGestured: (@MainActor (PlayheadGesture) -> Void)?
    /// The one playback position as the page last had it (the selection it turns cobalt in), or nil for the one playhead of before.
    private var playheadsWanted: [String: Any]?
    private var playheadsShown = ""
    /// The selection, its history, toast and hold hint, the menu's place and the phrases shown (Selection/SelectionModel.swift).
    let selectionModel: SelectionModel
    /// Expand, the card's roll and options, the version in focus and the play range (Playback/FocusModel.swift).
    let focusModel: FocusModel
    /// The other versions, the version's words and page, and the score as last engraved (Sources/SourcesModel.swift).
    let sourcesModel: SourcesModel
    /// The merge draft, its change, view and hearing, Compare versions, the toast and the marker tapped (Sources/MergeModel.swift).
    let mergeModel: MergeModel
    /// The passage selected, the card's roll, the play range and the version in focus, for the protected
    /// tests/ipad/Baseline harness alone, which reads these names; the views observe the models, and these go with the
    /// harness change in S7c (ADR 0031).
    var selection: ScoreSelection? { selectionModel.selection }
    var focusRoll: Bool { focusModel.asRoll }
    var playRange: FocusModel.PlayRange? { focusModel.playRange }
    var focusVersion: FocusModel.Version? { focusModel.version }
    /// A word of guidance for a moment over the pill, by the key of its text, such as why Expand waits for a selection (#521 19).
    @Published private(set) var notice: Notice?

    struct Notice: Equatable {
        var key: String
        var id = UUID()
    }
    /// How long a word of guidance stays, in seconds: as long as the hint to hold.
    static let noticeSeconds = 2.0
    /// The piece's beat lines (SelectionTools.swift): the one selection's grid; the bars/beats choice went in v0.6.4.
    var pieceBeatLines: [Double] = []
    /// The tuplet groups the page read, as blocks the selection's grid never splits (v0.6.4, PlaceSelection.swift): the roll takes them.
    var tupletsRead: (@MainActor ([[String: Double]]) -> Void)?
    /// The zoom and the place a double tap inside the selection zoomed from (PlaceSelection.swift), for the next to go back to.
    var zoomedFrom: (scale: CGFloat, offset: CGPoint)?
    /// The tool the Pencil's «switch to previous» left for Select, which its next one goes back to (StylusRules.selectTurn).
    var toolBeforeSelect: InteractionMode?
    /// A tap on the page that selects nothing: away from the bars, or anywhere while a passage is selected. The Insights
    /// sheet goes away; the selection stays (#338).
    var blankTapped: (@MainActor () -> Void)?
    /// Phrase focus (0018 section 4): the selection lifted into a card across the screen, the page dimmed behind it. Since
    /// v0.5.5 Expand (#521 19): bound to the selection it was opened on, which nothing changes until the way back. Kept
    /// here, not in FocusModel, while the protected UX baseline observes `score.$focused` (ADR 0031, the note of S3).
    @Published private(set) var focused = false
    /// Practice view (#533 11, PracticeView.swift): view state, changed by `practise`, its two visibilities kept on the device
    /// (v0.6.4, spec 9); and the time its controls hide.
    @Published var practice = PracticeView.saved()
    var practiceTimer: Task<Void, Never>?
    /// The name of the variation being tried, which the card in focus shows as an ossia above the phrase; nil for none.
    var ossia: String? {
        didSet { if oldValue != ossia, focused { Task { await engraveCard() } } }
    }
    @Published private(set) var markCount = 0
    /// The margin notes, highlights and inked bars, as music-core answered the last mark call.
    @Published private(set) var marks = MarksShelf()
    /// The note being written over the selection, or opened from its marker or the Notes tab; nil while none is.
    @Published var noteDraft: NoteDraft?
    /// The layers of marks hidden on the page (0018 section 5), kept with the piece as `layers.*` settings.
    @Published private(set) var hiddenLayers: Set<String> = []
    /// The piece's own settings, by id, as the project keeps them: the Settings sheet lists them, to clear in one tap.
    @Published private(set) var pieceOverrides: [String: String] = [:]
    /// The layers of the player's marks (0018 section 5), which show as before.
    static let layers = ["ink", "notes", "bookmarks", "analysis"]
    /// Extra information (testing item 33): what the app adds over the notes, off by default behind the ⓘ button, each
    /// kind with its own switch: differences from versions, the bars taken from a version, and the Check flags.
    static let extra = ["performance", "taken", "check"]
    private static let layerIds = layers + ["extra"] + extra
    /// Whether the Check drawer is open: its flags show then even with extra information off.
    var checkDrawerOpen = false {
        didSet {
            guard oldValue != checkDrawerOpen else { return }
            Task { await applyLayers() }
        }
    }
    /// The bars a scan or a transcription is unsure of (0018 section 9), as music-core answered the last Check call.
    @Published private(set) var check = CheckShelf()
    /// Fix notes (S1-I7): the bar whose notes the Check drawer lists to correct, as music-core answers `corrections`.
    @Published private(set) var corrections: NoteCorrections?
    /// «Мои правки» (version 0.5, on the iPad since v0.6.5): whether the corrections are laid over the notes, and those that
    /// no longer fit, as music-core answered last; nil for notes opened only to be shown.
    @Published private(set) var editsLayer: EditsLayerState?
    /// A Check flag tapped on the score: the screen opens the Check drawer on its bar.
    var checkPicked: (@MainActor (Int) -> Void)?
    /// The piece's sources (testing item 16): the base, the line on what the player's version holds, the PDF kept inside.
    /// Kept here, not in SourcesModel, while the protected UX baseline observes `score.$sources` (ADR 0031, the note of S4).
    @Published private(set) var sources = SourcesInfo()

    /// The draft's notes to hear over a span, once: the screen's player plays them on their own.
    var hearNotes: (@MainActor ([PlaybackNote], Range<Double>) -> Void)?
    /// The player's notes, then the version's, in turn over a span.
    var hearInTurn: (@MainActor ([PlaybackNote], Range<Double>) -> Void)?
    /// The way into a version's page from the score (its marker's popover, focus): the screen opens Sources.
    var versionPicked: (@MainActor (Int) -> Void)?
    /// The piece's notes changed (bars of another version taken or restored): the player and the roll load them again.
    var notesChanged: (@MainActor () -> Void)?
    /// The layers of the player's marks.
    var pieceLayers: [String] { Self.layers }

    /// The kinds of extra information this piece has: the differences and taken bars with a version (`hasVersions`, from
    /// the screen that observes SourcesModel), Check with flags.
    func pieceExtra(hasVersions: Bool) -> [String] {
        Self.extra.filter { item in
            switch item {
            case "check": check.hasFlags
            default: hasVersions
            }
        }
    }

    func layerName(_ layer: String) -> String {
        Self.extra.contains(layer) ? text["extra.\(layer)"] : text["layers.\(layer)"]
    }

    /// Whether a kind of extra information shows: extra information is on, and that kind is.
    func extraShown(_ item: String) -> Bool {
        !hiddenLayers.contains("extra") && !hiddenLayers.contains(item)
    }

    /// The ⓘ button: extra information on or off, kept with the piece.
    func toggleExtra() {
        showLayers(["extra"], hiddenLayers.contains("extra"))
    }

    /// A margin note being written: a new one (no id) or one already kept, over its bars.
    struct NoteDraft: Equatable {
        var id: String?
        var onset: Double
        var end: Double
        var firstBar: Int
        var lastBar: Int
        var text: String
    }
    @Published private(set) var canUndo = false
    /// Whether the Pen's Redo has a step (#521 9).
    @Published private(set) var canRedo = false
    @Published private(set) var problem: String?
    /// The note editor on the roll while it is open (testing item 15); nil when it is closed.
    @Published private(set) var noteEditor: NoteEditorState?
    /// The note edits were saved, «Мои правки» switched or a correction that no longer fits taken out: the screen writes
    /// the project file at once.
    var editsKept: (@MainActor () -> Void)?
    /// The pages under the reading line, for the bar: "p. 2 of 4", or "pp. 1–2 of 4" with two to a row.
    @Published private(set) var pages = PagesInView()

    /// While the piece plays and the playhead is out of sight, the tick the bar it is in starts at; nil while it shows (testing item 4).
    @Published private(set) var playheadAway: Double?

    struct PagesInView: Equatable {
        var first = 0
        var last = 0
        var count = 0
    }

    let webView: WKWebView
    /// The page in `webView`, the one way to its functions (`PageCommand`, 0031); a scenario test puts a recording one here.
    var scorePage: ScorePage
    /// The pen, and who writes and who scrolls (#479 items 7 and 18).
    private let ink = InkInput()
    private(set) var piece: OpenPiece?
    private var dark = false
    private var pageReady = false
    private var shown = false
    /// The page loaded again if iPadOS ends the web view's process (WebContentWatch).
    private var contentWatch: WebContentWatch?
    /// The render under way (`scheduleRender`), and whether another is wanted after it.
    private var rendering: Task<Void, Never>?
    private var renderAgain = false
    /// The pencil palette, opened by the Pencil's double tap or squeeze (#479 item 8); nil while it is closed.
    @Published var pencilPalette: PencilPaletteCall?
    /// The selection's handles, in the document's pixels, as the page last reported them: the Pencil does not write there.
    private var handleSpots: [CGPoint] = []
    /// The playheads' handles, in the document's pixels, as the page last reported them: a finger there takes the handle.
    private var headSpots: [CGPoint] = []
    private let text = UIText.current
    /// Pixels between two staff lines on screen, for the width of the live stroke.
    private var staffSpace: CGFloat = 8
    /// What the pen and the eraser did here, newest last, for Undo and Redo: a stroke kept, or a press of the eraser (BUG-05).
    private var steps = InkHistory()
    /// The page's ink changes, one after another: strokes kept, the eraser's moves and saves, Undo.
    private let inkQueue = InkQueue()
    /// The eraser's points not yet sent to the page, in the page's pixels, and whether a send is waiting in the queue.
    private var rubPoints: [[String: Double]] = []
    private var rubQueued = false
    /// The places ringed for the fact chosen in Insights.
    private var ringed: [Fact.Place] = []
    /// The bookmarks as the page draws them, and where each is, for a tap on its bracket or its entry in the Contents.
    private var bookmarkItems: [[String: Any]] = []
    private var bookmarkSpans: [String: Range<Double>] = [:]
    /// The bookmark last gone to and the selection that gave: a second tap on it lifts the passage into focus.
    @Published private(set) var shownBookmark: ShownBookmark?

    struct ShownBookmark: Equatable {
        var id: String
        var selection: ScoreSelection
    }

    /// A page as the Contents drawer's Pages tab shows it: its stretch of the piece in ticks and its picture.
    struct PageOutline: Identifiable {
        var page: Int
        var start: Double
        var end: Double
        var image: UIImage?
        var id: Int { page }
    }

    /// `mode` is the bar's mode the screen opens with, known before the screen's first frame (v0.6.4 spec section 13).
    init(mode: InteractionMode = .navigate) {
        _mode = Published(initialValue: mode)
        ProbeCounts.shared.add("score")
        ProbeCounts.shared.mark("made")
        let configuration = WKWebViewConfiguration()
        configuration.setURLSchemeHandler(BundleSchemeHandler(), forURLScheme: BundleSchemeHandler.scheme)
        webView = WKWebView(frame: .zero, configuration: configuration)
        scorePage = WebScorePage(webView)
        let selecting = SelectionModel()
        let sourcing = SourcesModel(evenBars: DeviceSettings.shared.isOn("score.evenBars"))
        selectionModel = selecting
        focusModel = FocusModel(selection: selecting)
        sourcesModel = sourcing
        mergeModel = MergeModel(selection: selecting, sources: sourcing)
        super.init()
        webView.configuration.userContentController.add(MessageRelay(self), name: "score")
        webView.isOpaque = false
        webView.backgroundColor = .clear

        // The pen's gesture and the stroke being written lie over the web view; the rules say which pointers they take.
        ink.attach(to: webView, pan: webView.scrollView.panGestureRecognizer)
        ink.style = { [weak self] point in self?.liveStyle(at: point) }
        ink.ended = { [weak self] samples in self?.strokeEnded(samples) }
        ink.rubs = { [weak self] in self?.tool == .eraser }
        ink.rubbed = { [weak self] points, lifted in self?.rubbed(points, lifted: lifted) }
        ink.handles = { [weak self] in self.map { $0.inView($0.handleSpots) } ?? [] }
        ink.heads = { [weak self] in self.map { $0.inView($0.headSpots) } ?? [] }
        ink.pencil.perform = { [weak self] action, point in self?.scorePencilGesture(action, at: point) }
        ink.penDown = { [weak self] down in self?.penDown(down) }
        applyInput()

        let watch = WebContentWatch { [weak self] in self?.pageLost() }
        contentWatch = watch
        webView.navigationDelegate = watch
        webView.load(URLRequest(url: BundleSchemeHandler.url("Web/score.html")))
    }

    /// The web view's process ended: nothing on the page is there any more, so it is drawn again in full once it loads.
    private func pageLost() {
        pageReady = false
        shown = false
        rendered = false
        playheadsShown = ""
    }

    deinit {
        ProbeCounts.shared.add("score", -1)
    }

    /// Shows a piece's pages and its marks; again when the appearance changes.
    func show(_ piece: OpenPiece, dark: Bool) {
        let changed = self.piece?.id != piece.id || self.dark != dark
        guard changed || !shown else { return }
        // The same piece keeps the pages engraved here after its notes changed or its comparison layer came on.
        if self.piece?.id != piece.id {
            self.piece = piece
            editsLayer = piece.edits
            #if DEBUG
            ScreensProbe.score = self
            #endif
            ProbeCounts.shared.mark("show")
            // The pages wait out of sight for the place they land on (#533 6), so the top of the first one never shows first.
            webView.alpha = Landing.holdsTheNotes(piece.landing) ? 0 : 1
            selectionModel.open(piece)
            rendered = false
            sourcesModel.open(evenBars: DeviceSettings.shared.isOn("score.evenBars"))
            sources = SourcesInfo()
            mergeModel.open()
            focusModel.forgetVersionNotes()
        }
        self.dark = dark
        // Every update of the web view came here until the first render had shown the pages, and each started another
        // render over it: the pages sent and laid out again and again as the screen opened (v0.6.4 spec section 11).
        if pageReady && (changed || rendering == nil) { scheduleRender() }
    }

    /// One render at a time; a piece or an appearance that changed meanwhile is drawn once it is done.
    private func scheduleRender() {
        guard rendering == nil else {
            renderAgain = true
            return
        }
        rendering = Task { [weak self] in
            await self?.render()
            guard let self else { return }
            self.rendering = nil
            if self.renderAgain {
                self.renderAgain = false
                self.scheduleRender()
            }
        }
    }

    /// Takes back the last step, after any change still being saved: a stroke goes, and a press of the eraser is put back
    /// whole, the pieces it left taken away and the marks it cut kept again (BUG-05).
    func undo() {
        inkQueue.then { [weak self] in
            guard let self, let piece = self.piece, let last = self.steps.undo() else { return }
            self.inkStepsChanged()
            await last.undo(remove: "ink.remove", add: "ink.add") { call, request, draws in
                await self.change(piece.id, call, request, draws: draws)
            }
        }
    }

    /// Does the step undone last again (#521 9): a stroke comes back, a press of the eraser takes again what it took.
    func redo() {
        inkQueue.then { [weak self] in
            guard let self, let piece = self.piece, let step = self.steps.redo() else { return }
            self.inkStepsChanged()
            await step.redo(remove: "ink.remove", add: "ink.add") { call, request, draws in
                await self.change(piece.id, call, request, draws: draws)
            }
        }
    }

    private func inkStepsChanged() {
        canUndo = steps.canUndo
        canRedo = steps.canRedo
    }

    // MARK: - The page

    /// What the page reports (`PageEvent`): the pages in view, where the menu goes, and what the player did on the score.
    fileprivate func received(_ message: [String: Any]) {
        for event in PageEvent.events(in: message) {
            handle(event)
        }
    }

    /// Acts on one thing the page reported; the roll's long press comes here too (its hold hint).
    func handle(_ event: PageEvent) {
        if !handleSelection(event) {
            handleMarks(event)
        }
    }

    /// The selection's events; false for the others.
    private func handleSelection(_ event: PageEvent) -> Bool {
        switch event {
        case .menu(let place):
            selectionModel.placeMenu(place)
        case let .chosen(chosen, start, end):
            choose(chosen, start, end)
        case .cleared(let gesture):
            clearSelection(gesture: gesture)
        case .gestureCancelled(let gesture):
            revertGesture(gesture)
        case .holdHint:
            selectionModel.showHoldHint()
        case let .picked(id, picked):
            selectNotes([id], gesture: picked["gesture"] as? String, replaces: Self.replaces(picked))
        case .zoomSelection(let box):
            zoomToSelection(box)
        case .tuplets(let groups):
            tupletsRead?(Self.tuplets(groups))
        case let .bar(start, end):
            // A tap in a bar selects it only when nothing is selected: the selection stays until Clear selection or
            // another choice (a long press and drag, a handle, the roll's bar numbers), so a stray tap cannot lose it (#338).
            if selectionModel.selection == nil {
                Task { await select(start, end) }
            } else {
                blankTapped?()
            }
        case .handles(let spots):
            handleSpots = spots
        default:
            return false
        }
        return true
    }

    /// A span the selection rule chose. By places on the notes (#601) it is PlaceSelection's; by beats (#349) music-core
    /// takes the edges to the beat lines; otherwise to the bars. The gesture's changes are one step of the selection's
    /// history (#511 11).
    private func choose(_ chosen: [String: Any], _ start: Double, _ end: Double) {
        let gesture = chosen["gesture"] as? String
        if chosen["places"] as? Bool == true {
            chosenByPlace(chosen, start, end)
        } else if chosen["beats"] as? Bool == true {
            selectBeats(start, end, gesture: gesture)
        } else {
            Task { await select(start, end, gesture: gesture) }
        }
    }

    /// The pages, the marks, the playheads and Expand's card. Under the modes (version 0.5) a tap places a playhead on its
    /// beat; notes are edited only in the roll (#479 item 23). A playhead's handle dragged goes to the player, and where the
    /// handles are is kept for the pen (#495 UX-08).
    private func handleMarks(_ event: PageEvent) {
        switch event {
        case let .pages(first, last, count):
            pages = PagesInView(first: first, last: last, count: count)
        case .bookmark(let id):
            goToBookmark(id)
        case .noteMarker(let id):
            openNote(id)
        case .checkFlag(let bar):
            showCheckBar(bar)
            checkPicked?(bar)
        case let .performanceBar(bar, left, bottom):
            mergeModel.pickMarker(MergeModel.MarkerPick(bar: bar, left: left, bottom: bottom))
            showVersionBar(bar)
        case .playheadAway(let away):
            playheadAway = away < 0 ? nil : away
        case let .place(start, end):
            placeTapped?(start..<max(end, start + 1))
        case .heads(let spots):
            headSpots = spots
        case .drag(let gesture):
            playheadGestured?(gesture)
        case .blank:
            // A tap away from the bars puts the sheet away, and leaves the selection as it is (#338).
            blankTapped?()
        case .leave:
            leaveFocus()
        case .cardLayout(let options):
            focusModel.takeCardOptions(facadeRequest(options))
            Task { await engraveCard() }
        default:
            break
        }
    }

    // MARK: - Phrases and the selection

    /// Shows another level: the selection moves to the unit that is, encloses or starts it at the new level. The units are
    /// not drawn on the score (#479 item 14).
    func showLevel(_ level: String) {
        selectionModel.hidePhrases(false)
        guard let piece, level != selectionModel.phrases?.level else { return }
        pieceOverrides[Self.granularity] = level == "short" ? "short" : level == "sentence" ? "sentences" : "phrases"
        Task {
            await selectionModel.showLevel(level, in: piece.id)
            await showSelection()
            focusModel.updatePlayRange()
        }
    }

    /// «Вернуть найденные» (the web app's Reset level): the level shown forgets the player's own boundaries, made in
    /// the Music Studio, and has the units found in the file again.
    func resetLevel() {
        guard let piece, let level = selectionModel.phrases?.level, selectionModel.phrases?.edited.contains(level) == true else { return }
        Task {
            await selectionModel.resetLevel(level, in: piece.id)
            await showSelection()
            focusModel.updatePlayRange()
        }
    }

    /// Selects a passage through music-core, which keeps it within what a selection may hold; with `gesture`, as part of
    /// that gesture's step of the history.
    func select(_ start: Double, _ end: Double, gesture: String? = nil, step: Bool = true) async {
        await select(request: "{\"span\": {\"onset\": \(Int(start)), \"end\": \(Int(end))}}", gesture: gesture, step: step)
    }

    /// Selects a passage by beats (#349): music-core takes its edges out to the beat lines. A drag on the score or on the
    /// roll's bar numbers and handles (#479 item 23), where the score selects by beats.
    func selectBeats(_ start: Double, _ end: Double, gesture: String? = nil) {
        Task { await select(request: Self.beatsRequest(start, end), gesture: gesture) }
    }

    /// A passage by beats, as music-core's `select` takes it: its edges out to the beat lines.
    static func beatsRequest(_ start: Double, _ end: Double) -> String {
        "{\"span\": {\"onset\": \(Int(start)), \"end\": \(Int(end))}, \"snap\": \"beat\"}"
    }

    /// Selects whole bars, as the piano roll picks them; a tap that extends takes the selection to the bar tapped.
    func selectBars(_ first: Int, _ last: Int, extend: Bool, gesture: String? = nil) {
        var from = first
        var to = last
        if extend, let selection = selectionModel.selection {
            if first < selection.firstBar { to = selection.lastBar } else { from = selection.firstBar }
        }
        Task { await select(request: "{\"bars\": {\"first\": \(from), \"last\": \(to)}}", gesture: gesture) }
    }

    /// Selects through music-core; `step` false for a selection the player did not make now (the resume point, Undo);
    /// `picked` for notes picked one by one (v0.6.2, PlaceSelection.swift), which the selection then names; `replaces` for
    /// a new selection in place of the one there was (a replace with «Вернуть»).
    func select(request: String, gesture: String? = nil, step: Bool = true, picked: Bool = false, replaces: Bool = false) async {
        guard let piece else { return }
        let refused = await selectionModel.select(in: piece.id, request: request, gesture: gesture, step: step, picked: picked, replaces: replaces)
        if let message = refused {
            if !softlyRefused(message) { problem = message }
            return
        }
        await showSelection()
        focusModel.updatePlayRange()
        if focused {
            await engraveCard()
            await updateFocusVersion()
        }
    }

    /// Takes the selection away: ✕, Escape, or a hold outside it (`gesture`, #511 2.1); `step` false for Undo.
    func clearSelection(gesture: String? = nil, step: Bool = true) {
        guard selectionModel.selection != nil else { return }
        if step { selectionModel.record(from: selectionModel.selection, to: nil, gesture: gesture) }
        // Nothing to lift: focus goes with the selection.
        leaveFocus()
        selectionModel.answered(nil)
        Task {
            await showSelection()
            focusModel.updatePlayRange()
        }
    }

    // MARK: - Bookmarks and the Contents

    /// Draws the bookmarks as brackets in their categories' inks above the top staff (0018 section 5).
    func showBookmarks(_ shelf: BookmarkShelf?) {
        let bookmarks = shelf?.bookmarks ?? []
        bookmarkSpans = Dictionary(bookmarks.map { ($0.id, $0.onset..<max($0.end, $0.onset + 1)) }, uniquingKeysWith: { first, _ in first })
        bookmarkItems = bookmarks.map { bookmark in
            [
                "id": bookmark.id, "start": bookmark.onset, "end": bookmark.end, "title": bookmark.title,
                "ink": shelf?.category(of: bookmark)?.ink ?? "graphite", "label": text.format("bookmark.label", bookmark.title, bookmark.bars(text)),
            ] as [String: Any]
        }
        guard shown else { return }
        Task { await drawBookmarks() }
    }

    private func drawBookmarks() async {
        await scorePage.send(.bookmarks, ["items": bookmarkItems])
    }

    /// A bookmark gone to from its bracket or its entry: the score selects it and brings it into view; gone to again with
    /// the selection as it left it, the passage lifts into focus. True when it lifted.
    @discardableResult
    func goToBookmark(_ id: String) -> Bool {
        guard let span = bookmarkSpans[id] else { return false }
        if let shownBookmark, shownBookmark.id == id, shownBookmark.selection == selectionModel.selection, !penOn {
            enterFocus()
            return true
        }
        Task {
            await select(span.lowerBound, span.upperBound)
            if let selection = selectionModel.selection { shownBookmark = ShownBookmark(id: id, selection: selection) }
            await scorePage.send(.reveal, ["start": span.lowerBound])
        }
        return false
    }

    /// Whether the selection is the bookmark last gone to: its entry in the Contents shows it.
    func isShown(_ id: String) -> Bool {
        shownBookmark?.id == id && shownBookmark?.selection == selectionModel.selection
    }

    /// The title a new bookmark starts with: "Phrase 3, bars 9–12" for a unit of the level shown, else "Bars 9–12".
    /// The open piece's title, for what the assistant knows.
    var pieceTitle: String { piece?.title ?? "" }
    /// The piece open in music-core, as the assistant at the selection asks about it; nil while none is.
    var pieceId: Int32? { piece?.id }

    /// The assistant's scope chip for the selection (testing item 23): "This phrase · bars 9–16" or "This selection · bar 3".
    var askLabel: String? {
        guard let selection = selectionModel.selection else { return nil }
        let bars = selection.firstBar == selection.lastBar
            ? text.format("bars.one", String(selection.firstBar))
            : text.format("bars.range", String(selection.firstBar), String(selection.lastBar))
        let phrase = !selectionModel.phrasesHidden && selectionModel.phrases?.units.contains { $0.selectableOnset == selection.onset && $0.selectableEnd == selection.end } == true
        return text.format(phrase ? "ask.thisPhrase" : "ask.thisSelection", bars)
    }

    var bookmarkTitle: String {
        guard let selection = selectionModel.selection else { return "" }
        if !selectionModel.phrasesHidden, let unit = selectionModel.phrases?.units.first(where: { $0.selectableOnset == selection.onset && $0.selectableEnd == selection.end }) {
            return label(of: unit)
        }
        let bars = selection.firstBar == selection.lastBar
            ? text.format("bars.one", String(selection.firstBar))
            : text.format("bars.range", String(selection.firstBar), String(selection.lastBar))
        return bars.prefix(1).uppercased() + bars.dropFirst()
    }

    /// A drawer over the left of the page this wide: the selection menu keeps to its right.
    func clearLeft(_ width: CGFloat) {
        guard shown else { return }
        Task { await scorePage.send(.clearLeft, ["width": Double(width)]) }
    }

    /// The pages with their stretches of the piece and their pictures, `width` pixels wide, for the Pages tab.
    func outline(width: CGFloat) async -> [PageOutline] {
        guard shown,
              let answer = try? await scorePage.call(.outline, ["width": Double(width)]) as? [Any]
        else { return [] }
        return answer.compactMap { entry in
            guard let page = entry as? [String: Any], let number = (page["page"] as? NSNumber)?.intValue else { return nil }
            let image = (page["image"] as? String)
                .flatMap { $0.split(separator: ",", maxSplits: 1).last }
                .flatMap { Data(base64Encoded: String($0)) }
                .flatMap { UIImage(data: $0) }
            return PageOutline(
                page: number,
                start: (page["start"] as? NSNumber)?.doubleValue ?? 0,
                end: (page["end"] as? NSNumber)?.doubleValue ?? .infinity,
                image: image
            )
        }
    }

    /// Where the notes on a page (from 1) start, for PDF | Notes (0023); nil before the score is shown.
    func firstTick(onPage page: Int) async -> Double? {
        guard shown, page > 0 else { return nil }
        let tick = try? await scorePage.call(.firstTickOn, ["page": page])
        return (tick as? NSNumber)?.doubleValue
    }

    /// Brings a page to the top of the view, under the bar.
    func showPage(_ page: Int) {
        guard shown else { return }
        Task { await scorePage.send(.showPage, ["page": page]) }
    }

    // MARK: - Margin notes, highlights and layers

    /// Starts a note over the selection.
    func startNote() {
        guard let selection = selectionModel.selection else { return }
        noteDraft = NoteDraft(id: nil, onset: selection.onset, end: selection.end, firstBar: selection.firstBar, lastBar: selection.lastBar, text: "")
    }

    /// Opens a note to read or change: its bars are selected and the card shows its words.
    func openNote(_ id: String) {
        guard let note = marks.notes.first(where: { $0.id == id }) else { return }
        let draft = NoteDraft(id: id, onset: note.onset, end: note.end, firstBar: note.firstBar, lastBar: note.lastBar, text: note.text)
        Task {
            if selectionModel.selection?.onset != note.onset || selectionModel.selection?.end != note.end {
                await select(note.onset, note.end)
                await scorePage.send(.reveal, ["start": note.onset])
            }
            noteDraft = draft
        }
    }

    /// Done on the note card: a new note is kept when it has words; a kept one changes, or goes when emptied.
    func saveNote() {
        guard let draft = noteDraft, let piece else { return }
        noteDraft = nil
        let words = draft.text.trimmingCharacters(in: .whitespacesAndNewlines)
        Task {
            if let id = draft.id {
                if words.isEmpty {
                    await change(piece.id, "note.remove", ["id": id])
                } else {
                    await change(piece.id, "note.change", ["id": id, "text": words])
                }
            } else if !words.isEmpty {
                let span = ["onset": Int(draft.onset), "end": Int(draft.end)]
                await change(piece.id, "note.add", ["span": span, "text": words, "now": ISO8601DateFormatter().string(from: Date())])
            }
        }
    }

    func deleteNote(_ id: String) {
        guard let piece else { return }
        noteDraft = nil
        Task { await change(piece.id, "note.remove", ["id": id]) }
    }

    /// The highlight laid exactly over the selection, the newest when there are more.
    var selectedHighlight: HighlightMark? {
        guard let selection = selectionModel.selection else { return nil }
        return marks.highlights.last { $0.onset == selection.onset && $0.end == selection.end }
    }

    /// Lays the selection in an ink; the highlight already over it gives way to the new one.
    func highlightSelection(_ ink: String) {
        guard let piece, let selection = selectionModel.selection else { return }
        let existing = selectedHighlight
        guard existing?.colour != ink else { return }
        Task {
            if let existing { await change(piece.id, "highlight.remove", ["id": existing.id]) }
            await change(piece.id, "highlight.add", ["span": ["onset": Int(selection.onset), "end": Int(selection.end)], "colour": ink])
        }
    }

    func removeHighlight(_ id: String) {
        guard let piece else { return }
        Task { await change(piece.id, "highlight.remove", ["id": id]) }
    }

    /// Shows or hides layers of marks, kept with the piece.
    func showLayers(_ layers: [String], _ on: Bool) {
        guard let piece else { return }
        for layer in layers { pieceOverrides["layers.\(layer)"] = on ? "on" : "off" }
        hiddenLayers = layersHidden()
        let id = piece.id
        Task {
            for layer in layers {
                let request = facadeRequest(["id": "layers.\(layer)", "value": on ? "on" : "off"])
                _ = await MusicCore.run { MusicCore.call(piece: id, "settings.override", request) }
            }
            await applyLayers()
            await drawVersions()
        }
    }

    /// The piece's own choices (`settings` on the piece): its layers and its level.
    private func loadPieceSettings(_ piece: OpenPiece) async {
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "settings") }
        pieceOverrides = facadeObject(answer)?["overrides"] as? [String: String] ?? [:]
        hiddenLayers = layersHidden()
    }

    /// The layers hidden: the piece's own choice, else the device's (0018 section 10).
    private func layersHidden() -> Set<String> {
        let values = DeviceSettings.shared.resolved(overrides: pieceOverrides)
        return Set(Self.layerIds.filter { values["layers.\($0)"] == "off" })
    }

    /// What the page hides: the layers hidden, and Check while neither extra information nor the Check drawer shows it.
    private func applyLayers() async {
        guard shown else { return }
        var hidden = Self.layers.filter(hiddenLayers.contains)
        if !extraShown("check") && !checkDrawerOpen { hidden.append("check") }
        let slate = DeviceSettings.shared["appearance.theme"] == "slate"
        await scorePage.send(.layers, ["hidden": hidden, "slate": slate])
    }

    /// The level a piece opens in (the setting "phrases.granularity"), shown without becoming the piece's own; a piece that
    /// has its own keeps it.
    private func applyLevel() async {
        guard let piece, pieceOverrides[Self.granularity] == nil, let shownLevel = selectionModel.phrases?.level else { return }
        let level = switch DeviceSettings.shared[Self.granularity] {
        case "short": "short"
        case "sentences": "sentence"
        default: "phrase"
        }
        guard level != shownLevel else { return }
        await selectionModel.showLevel(level, in: piece.id, remember: false)
        await showSelection()
        focusModel.updatePlayRange()
    }

    private static let granularity = "phrases.granularity"

    /// The device's settings changed in the sheet: the page follows at once.
    func settingsChanged() {
        hiddenLayers = layersHidden()
        Task {
            await applyLayers()
            await applyLevel()
            await drawVersions()
        }
    }

    /// "Use these settings for this piece": the piece's own choices go, and the device's hold for it too.
    func clearPieceOverrides() {
        guard let piece else { return }
        let ids = Array(pieceOverrides.keys)
        pieceOverrides = [:]
        Task {
            for id in ids {
                let request = facadeRequest(["id": id, "value": NSNull()])
                _ = await MusicCore.run { MusicCore.call(piece: piece.id, "settings.override", request) }
            }
            settingsChanged()
        }
    }

    // MARK: - Check

    /// Show: the bar is selected, so it can be looped and compared with the page or the recording, and comes into view.
    func showCheckBar(_ bar: Int) {
        // A bar of a flag, or of notes whose length is uncertain (#352).
        guard let span = check.flags.first(where: { $0.bar == bar && $0.span != nil })?.span
            ?? check.lengths.first(where: { $0.bar == bar })?.span else { return }
        Task {
            await select(span.lowerBound, span.upperBound)
            await scorePage.send(.reveal, ["start": span.lowerBound])
        }
    }

    /// Looks right, or Check again: every flag on the bar resolved or open again, kept with the piece.
    func resolveCheck(_ bar: Int, _ resolved: Bool) {
        resolveChecks(["bar": bar, "resolved": resolved])
    }

    /// Looks right for a whole piece of the pieces layer (version 0.5, Check by piece): every flagged bar from `first` to
    /// `last` resolved, in one change music-core makes, kept with the piece.
    func resolveCheckPiece(first: Int, last: Int) {
        resolveChecks(["bars": ["first": first, "last": last], "resolved": true])
    }

    private func resolveChecks(_ fields: [String: Any]) {
        guard let piece else { return }
        let request = facadeRequest(fields)
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "check.resolve", request) }
            if let shelf = CheckShelf(answer: answer) {
                check = shelf
                await drawChecks()
            } else {
                problem = FacadeAnswer.message(answer)
            }
        }
    }

    /// Fix notes on a bar: it is selected and comes into view, and its notes are listed to correct; again, the list closes.
    func fixCheckBar(_ bar: Int) {
        guard corrections?.bar != bar else {
            corrections = nil
            return
        }
        guard editsOn else {
            showNotice("padEdits.offNotice")
            return
        }
        showCheckBar(bar)
        correct(.bar, ["bar": bar])
    }

    /// A note of the bar a step higher or lower (semitones), earlier or later, shorter or longer (sixteenths), softer or
    /// louder: a correction in the piece's edit log, and the score engraved again.
    func correctNote(_ id: String, pitch: Int = 0, onset: Int = 0, length: Int = 0, velocity: Int = 0) {
        correct(.change, ["id": id, "pitch": pitch, "onset": onset, "length": length, "velocity": velocity], notes: true)
    }

    /// Takes the note out of the piece.
    func removeNote(_ id: String) { correct(.remove, ["id": id], notes: true) }

    /// Puts the note back as it was imported.
    func revertNote(_ id: String) { correct(.revert, ["id": id], notes: true) }

    /// Takes back the last correction.
    func undoCorrection() { correct(.undo, [:], notes: true) }

    /// Accepts a draft of the assistant (#582 item 17.3): music-core makes it again from its call over the notes as they
    /// are now and adds its edits as corrections (`corrections.acceptDraft`), each undone like any other, and the score is
    /// engraved again. Nil when it is accepted, else why not, in music-core's words (empty when it gave none).
    func acceptAssistantDraft(_ draft: AssistantDraftOffer) async -> String? {
        guard editsOn else { return text["edits.off"] }
        guard let piece, let body = draft.acceptRequest?.text else { return "" }
        let answer = await PieceCore(piece: piece.id).corrections(.acceptDraft, body)
        guard let read = answer.value else { return FacadeAnswer.problem(answer.text) }
        readEdits(read)
        await notesDidChange()
        showNotice("padAssistant.accepted")
        return nil
    }

    /// Undo on the assistant's accepted draft: the whole draft goes at once (`corrections.undoDraft`), as on the web. Nil
    /// when it is undone, else why not, in music-core's words.
    func undoAssistantDraft(_ draft: AssistantDraftOffer) async -> String? {
        guard editsOn else { return text["edits.off"] }
        guard let piece else { return "" }
        let bar = draft.firstBar ?? 1
        let answer = await PieceCore(piece: piece.id).corrections(.undoDraft, "{\"bar\": \(bar)}")
        guard let read = answer.value else { return FacadeAnswer.problem(answer.text) }
        readEdits(read)
        await notesDidChange()
        showNotice("padAssistant.undone")
        return nil
    }

    private func correct(_ call: PieceCore.Correction, _ fields: [String: Any], notes: Bool = false) {
        guard let piece, let bar = fields["bar"] as? Int ?? corrections?.bar else { return }
        var request = fields
        request["bar"] = bar
        let body = facadeRequest(request)
        Task {
            let answer = await PieceCore(piece: piece.id).corrections(call, body)
            guard let read = answer.value else {
                problem = FacadeAnswer.problem(answer.text)
                return
            }
            corrections = NoteCorrections(read)
            readEdits(read)
            if notes { await notesDidChange() }
        }
    }

    // MARK: - My corrections (version 0.5, «Мои правки: вкл / выкл»)

    /// Whether notes can be corrected now: not while «Мои правки» is off, when music-core refuses every correction.
    var editsOn: Bool { editsLayer?.on ?? true }

    /// «Мои правки» switched: music-core lays the corrections over the notes one by one, or leaves them out, and keeps that
    /// in the piece's file (`corrections.layer`); the score is engraved again from the notes as they are now. Off, the
    /// bar listed to correct in Check closes, as nothing can be corrected then.
    func switchEdits(on: Bool) {
        let bar = on ? corrections?.bar : nil
        if !on { corrections = nil }
        editsCall(.layer, EditsLayerState.layerRequest(on: on, bar: bar))
    }

    /// «Убрать» on a note whose corrections no longer fit: they leave the piece's edit log (`corrections.drop`).
    func dropUnfitEdits(_ noteIds: [String]) {
        editsCall(.drop, EditsLayerState.dropRequest(noteIds, bar: corrections?.bar))
    }

    private func editsCall(_ call: PieceCore.Correction, _ fields: [String: Any]) {
        guard let piece else { return }
        let body = facadeRequest(fields)
        Task {
            let answer = await PieceCore(piece: piece.id).corrections(call, body)
            guard let read = answer.value else {
                problem = FacadeAnswer.problem(answer.text)
                return
            }
            editsLayer = EditsLayerState(read.edits)
            if corrections != nil, read.bar == corrections?.bar { corrections = NoteCorrections(read) }
            await notesDidChange()
            editsKept?()
        }
    }

    /// Every corrections answer says how many corrections there are and, with a layer, which no longer fit.
    private func readEdits(_ answer: CorrectionsAnswer) {
        editsLayer = EditsLayerState(answer.edits)
    }

    // MARK: - Note editor on the roll

    /// Edit notes in the roll's drawer (testing item 15): the editor opens on a draft in music-core, so every edit sounds
    /// and shows at once but the project file keeps the notes as they were until Save; Cancel puts them back.
    func beginNoteEdit() {
        guard editsOn else {
            showNotice("padEdits.offNotice")
            return
        }
        noteEditor = NoteEditorState()
        noteEditCall(.begin, [:])
    }

    /// Move, Add or Remove: what a press on the roll's notes does.
    func chooseNoteEditTool(_ tool: String) {
        noteEditor?.tool = tool
    }

    /// A note moved, added or taken out on the roll.
    func editNote(_ edit: RollNoteEdit) {
        guard noteEditor != nil else { return }
        switch edit {
        case let .change(id, onset, pitch, length):
            noteEditor?.picked = id
            noteEditCall(.set, ["id": id, "onset": onset, "pitch": pitch, "length": length], notes: true)
        case let .add(onset, pitch, length):
            noteEditCall(.add, ["onset": onset, "pitch": pitch, "length": length], notes: true)
        case let .remove(id):
            noteEditor?.picked = nil
            noteEditCall(.remove, ["id": id], notes: true)
        }
    }

    /// Takes back the last edit, the reset too.
    func undoNoteEdit() { noteEditCall(.undo, [:], notes: true) }

    /// Every correction taken out: the notes as imported, until Save.
    func resetNotes() { noteEditCall(.reset, [:], notes: true) }

    /// Closes the editor with the notes as they were when it opened.
    func cancelNoteEdit() {
        guard noteEditor != nil else { return }
        let changed = noteEditor?.changed ?? false
        noteEditor = nil
        noteEditCall(.discard, [:], notes: changed)
    }

    /// Keeps the edits with the piece and writes the project file.
    func saveNoteEdit() {
        guard noteEditor != nil else { return }
        noteEditor = nil
        noteEditCall(.save, [:]) { [weak self] in self?.editsKept?() }
    }

    private func noteEditCall(_ call: PieceCore.Correction, _ fields: [String: Any], notes: Bool = false, then: (@MainActor () -> Void)? = nil) {
        guard let piece else { return }
        var request = fields
        // The corrections answer with a bar's notes; the editor needs only what the answer says about the draft.
        request["bar"] = selectionModel.selection?.firstBar ?? 1
        let body = facadeRequest(request)
        Task {
            let answer = await PieceCore(piece: piece.id).corrections(call, body)
            guard let read = answer.value else {
                problem = FacadeAnswer.problem(answer.text)
                return
            }
            if noteEditor != nil {
                noteEditor?.canUndo = read.canUndo
                noteEditor?.changed = read.changed
                noteEditor?.canReset = read.edits.count > 0
                if let added = read.added { noteEditor?.picked = added }
            }
            if corrections != nil, read.bar == corrections?.bar { corrections = NoteCorrections(read) }
            readEdits(read)
            if notes { await notesDidChange() }
            then?()
        }
    }

    private func loadCheck(_ piece: OpenPiece) async {
        check = CheckShelf(answer: await MusicCore.run { MusicCore.call(piece: piece.id, "check") }) ?? CheckShelf()
        await drawChecks()
    }

    /// The open bars as amber flags on the score, each labelled with its reasons.
    private func drawChecks() async {
        guard shown else { return }
        let flags: [[String: Any]] = check.openBars.compactMap { bar in
            guard let span = bar.span else { return nil }
            return ["bar": bar.bar, "tick": span.lowerBound, "label": text.format("check.flagLabel", String(bar.bar), CheckShelf.reasonNames(bar.flags, text))]
        }
        await scorePage.send(.checks, ["flags": flags])
    }

    // MARK: - Other versions

    /// Shows another version: its bars compared with these notes, and markers above the bars it has otherwise.
    func showVersion(_ id: String) {
        guard let piece else { return }
        sourcesModel.say(nil)
        Task { await readVersion(piece.id, id) }
    }

    /// A MIDI or MusicXML file of the piece added as another version: music-core lines it up with these notes, keeps it
    /// with the piece and raises the Check flags it gives a scanned piece (`performance.add`, 0022).
    func addVersion(fileName: String, data: Data) async {
        guard let piece else { return }
        let name = (fileName as NSString).deletingPathExtension
        let request = facadeRequest(["name": name.isEmpty ? text["performance.defaultName"] : name, "file": data.base64EncodedString(), "fileName": fileName])
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.add", request) }
        guard sourcesModel.read(performance: answer) else {
            sourcesModel.say(FacadeAnswer.message(answer))
            return
        }
        sourcesModel.say(sourcesModel.addedStatus(in: text))
        await versionAdded(piece)
    }

    /// A version just added: Sources shows its page (testing item 32), and the list, the markers and Check follow.
    private func versionAdded(_ piece: OpenPiece) async {
        sourcesModel.showPage(sourcesModel.versions.shownId)
        await sourcesModel.readList(of: piece.id)
        await drawVersions()
        await loadCheck(piece)
    }

    // MARK: - Sources (testing item 16)

    func refreshSources() {
        Task { await readSources() }
    }

    private func readSources() async {
        guard let piece else { return }
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "sources") }
        if let read = SourcesInfo(answer: answer) { sources = read }
    }

    /// Show on a bar of the version: the bar is selected and comes into view.
    func showVersionBar(_ bar: Int) {
        guard let span = sourcesModel.versions.bars.first(where: { $0.bar == bar })?.span else { return }
        Task {
            await select(span.lowerBound, span.upperBound)
            await scorePage.send(.reveal, ["start": span.lowerBound])
        }
    }

    func renameVersion(_ name: String) {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed != sourcesModel.versions.shown?.name else { return }
        changeVersion("performance.rename", ["name": trimmed], status: nil)
    }

    /// Removes the version shown with the bars taken from it and its Check flags; the toast's Undo puts it back while the
    /// piece is open (`performance.putBack`).
    func removeVersion() {
        guard let piece, let shown = sourcesModel.versions.shown else { return }
        let request = facadeRequest(["id": shown.id])
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.remove", request) }
            guard let object = facadeObject(answer), object["error"] == nil else {
                sourcesModel.say(FacadeAnswer.message(answer))
                return
            }
            sourcesModel.read(list: answer)
            sourcesModel.showPage(nil)
            if mergeModel.draft?.id == shown.id { mergeModel.end() }
            sourcesModel.say(nil)
            mergeModel.say(MergeModel.Toast(text: text.format("merge.removed", shown.name), undo: .putBack))
            if let next = sourcesModel.versions.entries.first {
                await readVersion(piece.id, next.id)
            } else {
                await drawVersions()
            }
            if shown.takenBars > 0 { await notesDidChange() }
            await loadCheck(piece)
        }
    }

    private func changeVersion(_ call: String, _ fields: [String: Any], status: String?, notes: Bool = false) {
        guard let piece, let id = sourcesModel.versions.shownId else { return }
        var request = fields
        request["id"] = id
        let body = facadeRequest(request)
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, call, body) }
            guard sourcesModel.read(performance: answer) else {
                sourcesModel.say(FacadeAnswer.message(answer))
                return
            }
            sourcesModel.say(status)
            if notes {
                await notesDidChange()
            } else {
                await drawVersions()
            }
        }
    }

    /// The versions kept with the piece, and the one shown compared bar by bar.
    private func loadVersions(_ piece: OpenPiece) async {
        await sourcesModel.readList(of: piece.id)
        if let id = sourcesModel.versions.shownId ?? sourcesModel.versions.entries.first?.id {
            await readVersion(piece.id, id)
        } else {
            await drawVersions()
        }
    }

    private func readVersion(_ piece: Int32, _ id: String) async {
        await sourcesModel.readVersion(id, in: piece)
        await drawVersions()
    }

    /// The piece's tempo the player read in the printed notes, or nil to play it as it states again (version 0.5.4, #511
    /// section 17, `tempo.set`): kept in the project file, heard from the player's next playback score and marked on the
    /// score engraved again.
    func setPieceTempo(_ perMinute: Int?) async {
        guard let piece else { return }
        let request = perMinute.map { "{\"perMinute\": \($0)}" } ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "tempo.set", request) }
        if let failure = facadeObject(answer), failure["error"] != nil {
            problem = FacadeAnswer.message(answer)
            return
        }
        notesChanged?()
        await engraveScore()
        if let file = piece.file { try? await ProjectFiles.save(piece: piece.id, to: file) }
    }

    /// The notes changed (bars taken or restored): the player and the roll take the new notes, and the score is engraved
    /// again and drawn with its marks.
    private func notesDidChange() async {
        focusModel.forgetVersionNotes()
        notesChanged?()
        await engraveScore()
    }

    /// Engraves the score again, as the draft's view asks while one is open (the version or the result), the player's
    /// notes otherwise, and draws it with its marks.
    private func engraveScore() async {
        guard let piece else { return }
        let id = piece.id
        let title = piece.title
        let view = wantedView
        let even = DeviceSettings.shared.isOn("score.evenBars")
        let engraved = await MusicCore.run {
            view.map { Engraver.engraveMerge(piece: id, title: title, view: $0, evenBars: even) } ?? Engraver.engrave(piece: id, title: title, evenBars: even)
        }
        switch engraved {
        case .success(let engraving):
            // Its other view stays; where it opened does not, so the page stays where the player is.
            self.piece = OpenPiece(id: id, title: title, pages: engraving.pages, timemap: engraving.timemap, file: piece.file, other: piece.other)
            sourcesModel.keepEngraved(SourcesModel.Engraved(view: view, even: even, added: engraving.version, own: engraving.own))
            await render()
            if focused { await engraveCard() }
        case .failure(let error):
            problem = error.message
        }
    }

    /// The draft's view the score should show: the version or the result while a draft is open out of focus.
    private var wantedView: String? {
        mergeModel.draft != nil && !focused && mergeModel.view != "mine" ? mergeModel.view : nil
    }

    /// The page engraved again when the view it should show changed.
    private func applyMergeView() async {
        // Even bars turned on or off engraves the score again too.
        let engraved = sourcesModel.engraved
        guard wantedView != engraved.view || engraved.even != DeviceSettings.shared.isOn("score.evenBars") else { return }
        await engraveScore()
    }

    /// With extra information on, the bars the version has otherwise as plum labels above them and the printed notes it
    /// does not have tinted, and a thin plum line under the bars taken from it; on a draft's view, its notes coloured.
    private func drawVersions() async {
        await readSources()
        guard shown else { return }
        let name = sourcesModel.versions.shown?.name ?? ""
        let differing = extraShown("performance") ? sourcesModel.versions.differing : []
        let markers: [[String: Any]] = differing.compactMap { bar in
            guard let span = bar.span else { return nil }
            return ["bar": bar.bar, "tick": span.lowerBound, "text": bar.marker, "label": text.format("performance.markerLabel", String(bar.bar), name, bar.marker)]
        }
        let engraved = sourcesModel.engraved
        let viewed = engraved.view != nil
        let data: [String: Any] = [
            "markers": markers, "unheard": viewed ? [] : differing.flatMap(\.notHeard),
            "added": viewed ? engraved.added : [], "artefacts": [String](), "own": viewed ? engraved.own : [],
            "taken": extraShown("taken") && !viewed ? sourcesModel.versions.taken.compactMap { $0.span?.lowerBound } : [],
        ]
        await scorePage.send(.performance, data)
        await updateFocusVersion()
        await applyMergeView()
    }

    /// The version for the phrase in focus: its notes to hear, and the bars of the phrase it has otherwise.
    private func updateFocusVersion() async {
        guard focused, let piece, let selection = selectionModel.selection, let shown = sourcesModel.versions.shown else {
            focusModel.showVersion(nil)
            return
        }
        guard let notes = await focusModel.versionNotes(of: shown.id, in: piece.id), focused, selectionModel.selection == selection, sourcesModel.versions.shownId == shown.id else { return }
        let bars = selection.firstBar...selection.lastBar
        focusModel.showVersion(FocusModel.Version(
            id: shown.id, name: shown.name, notes: notes,
            sounds: notes.contains { $0.onset >= selection.onset && $0.onset < selection.end },
            differing: sourcesModel.versions.differing.map(\.bar).filter { bars.contains($0) }
        ))
    }

    // MARK: - Merging a version (testing items 32–34)

    /// A version's page in Sources: its overview, the draft and what was merged.
    func openVersionPage(_ id: String) {
        sourcesModel.showPage(id)
        if sourcesModel.versions.shownId != id { showVersion(id) }
    }

    func closeVersionPage() {
        sourcesModel.showPage(nil)
    }

    /// Keep my notes, Take the sure ones or Take the version wherever it differs: the draft opens with those choices,
    /// or is applied at once when the player asked for that (`merge.direct`); `saved` opens the choices last applied.
    func startMerge(_ start: String, direct: Bool = true) {
        guard let piece, let id = sourcesModel.versions.shownId else { return }
        Task {
            guard await mergeCall(piece.id, .open, ["id": id, "start": start]) else { return }
            if direct && DeviceSettings.shared["merge.direct"] == "on" {
                await applyDraft(piece)
                return
            }
            mergeModel.showView("result")
            mergeModel.go(to: mergeModel.draft?.bars.first?.bar)
            if let at = mergeModel.at { showVersionBar(at) }
            await mergeChanged()
        }
    }

    /// Open in the draft, from a marker's popover or from focus: the version's page with the draft on the bar, the
    /// choices as last applied when no draft of it is open.
    func openDraft(at bar: Int) {
        guard let piece, let id = sourcesModel.versions.shownId else { return }
        mergeModel.pickMarker(nil)
        sourcesModel.showPage(id)
        versionPicked?(bar)
        Task {
            if mergeModel.draft?.id != id {
                guard await mergeCall(piece.id, .open, ["id": id, "start": "saved"]) else { return }
                mergeModel.showView("result")
            }
            goToMergeBar(bar)
            await mergeChanged()
        }
    }

    /// Open in the draft from focus: focus is left, and the draft opens on the first bar of the phrase that differs.
    func openDraftFromFocus() {
        guard focusModel.version != nil, let bar = focusModel.version?.differing.first ?? selectionModel.selection?.firstBar else { return }
        leaveFocus()
        openDraft(at: bar)
    }

    /// Take mine, Take from the version or Not a difference, for the change shown or the bar given; with Phrase, for every
    /// change of its phrase.
    func chooseMerge(_ choice: String, bar: Int? = nil) {
        guard let piece, let at = bar ?? mergeModel.at else { return }
        changeDraft(piece, .choose, ["bars": mergeModel.choiceBars(at), "choice": choice])
    }

    /// Take the group: every change of the kind at once, mine or the version.
    func chooseMergeGroup(_ group: String, _ choice: String) {
        guard let piece, let merge = mergeModel.draft else { return }
        let bars = merge.bars.filter { $0.group == group }.map(\.bar)
        guard !bars.isEmpty else { return }
        changeDraft(piece, .choose, ["bars": bars, "choice": choice])
    }

    /// Reset the bar: undecided again.
    func undecideMerge(_ bar: Int) {
        guard let piece else { return }
        changeDraft(piece, .undecide, ["bars": [bar]])
    }

    func undoDraft() {
        guard let piece else { return }
        changeDraft(piece, .undo)
    }

    func redoDraft() {
        guard let piece else { return }
        changeDraft(piece, .redo)
    }

    /// Reset the draft: back to how it opened, which Undo can itself take back.
    func resetDraft() {
        guard let piece else { return }
        changeDraft(piece, .reset)
    }

    /// Cancel: the draft goes and nothing has changed.
    func cancelDraft() {
        guard let piece else { return }
        Task {
            _ = await mergeCall(piece.id, .cancel)
            mergeModel.end()
            await mergeChanged()
        }
    }

    func applyMerge() {
        guard let piece else { return }
        Task { await applyDraft(piece) }
    }

    /// Undo the merge: the player's notes back in every bar taken; the choices stay to open again.
    func undoAppliedMerge() {
        guard let piece, let id = sourcesModel.versions.shownId else { return }
        Task { await unapply(piece, id, toast: text["merge.undone"], undo: .reapply) }
    }

    /// The toast's Undo: the step it names taken back.
    func undoToast() {
        guard let piece, let toast = mergeModel.toast, let undo = toast.undo else { return }
        mergeModel.say(nil)
        Task {
            switch undo {
            case .unapply:
                guard let id = sourcesModel.versions.shownId else { return }
                await unapply(piece, id, toast: text["merge.applyUndone"], undo: nil)
            case .reapply:
                guard let id = sourcesModel.versions.shownId, await mergeCall(piece.id, .open, ["id": id, "start": "saved"]) else { return }
                await applyDraft(piece, toast: text["merge.redone"])
            case .putBack:
                let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.putBack") }
                guard sourcesModel.read(performance: answer) else {
                    sourcesModel.say(FacadeAnswer.message(answer))
                    return
                }
                await sourcesModel.readList(of: piece.id)
                mergeModel.say(MergeModel.Toast(text: text.format("merge.putBack", sourcesModel.versions.shown?.name ?? ""), undo: nil))
                await notesDidChange()
                await loadCheck(piece)
            }
        }
    }

    func closeToast() {
        mergeModel.say(nil)
    }

    /// ‹ › through the changes.
    func stepMerge(_ by: Int) {
        guard let bar = mergeModel.stepped(by: by) else { return }
        goToMergeBar(bar)
    }

    /// A change chosen in the list, the strip or the window: the draft and the score go to its bar.
    func goToMergeBar(_ bar: Int) {
        mergeModel.go(to: bar)
        showVersionBar(bar)
        if let hearing = mergeModel.hearing { hearMerge(hearing) }
    }

    /// What the score shows while the draft is open: the player's notes, the version or the result.
    func showMergeView(_ view: String) {
        mergeModel.showView(view)
        Task {
            await applyMergeView()
            await drawVersions()
        }
    }

    /// Hear Mine, Version or Result over the change shown (its phrase with Phrase), or the player's notes and the
    /// version in turn.
    func hearMerge(_ view: String) {
        guard let piece, mergeModel.draft != nil, let span = mergeModel.hearSpan else { return }
        mergeModel.hear(view)
        Task {
            let request = facadeRequest(["view": view == "alternate" ? "version" : view])
            let answer = await PieceCore(piece: piece.id).mergePlayback(request)
            guard let notes = answer.value.map(PlaybackScore.init)?.notes else { return }
            if view == "alternate" { hearInTurn?(notes, span) } else { hearNotes?(notes, span) }
        }
    }

    /// ▶ As in the version, from a marker's popover: the bar as the version plays it.
    func hearMarkerVersion() {
        guard let piece, let pick = mergeModel.markerPick, let id = sourcesModel.versions.shownId,
              let span = sourcesModel.versions.bars.first(where: { $0.bar == pick.bar })?.span else { return }
        Task {
            guard let notes = await focusModel.versionNotes(of: id, in: piece.id) else { return }
            hearNotes?(notes, span)
        }
    }

    func closeMarker() {
        mergeModel.pickMarker(nil)
    }

    /// A draft's view engraved for Compare versions over the bars shown (± 1 bar, the phrase or the sentence round the
    /// change), with the ids of the notes to colour.
    func mergeCard(_ view: String, see: String, zoom: Int) async -> MergeCardImage? {
        guard let piece, let at = mergeModel.at else { return nil }
        let id = piece.id
        let (first, last) = await mergeModel.seenBars(in: id, at, see)
        let engraved = await MusicCore.run { Engraver.engraveMergeBars(piece: id, view: view, firstBar: first, lastBar: last, zoom: zoom) }
        guard case .success(let engraving) = engraved, let svg = engraving.pages.first else { return nil }
        return MergeCardImage(svg: svg, version: engraving.version, own: engraving.own)
    }

    private func changeDraft(_ piece: OpenPiece, _ call: PieceCore.Merge, _ fields: [String: Any] = [:]) {
        Task {
            if await mergeCall(piece.id, call, fields) { await mergeChanged() }
        }
    }

    /// One `performance.merge` call (MergeModel's); false, with why in the version's words, for a refusal.
    @discardableResult private func mergeCall(_ piece: Int32, _ call: PieceCore.Merge, _ fields: [String: Any] = [:]) async -> Bool {
        guard let refused = await mergeModel.call(call, in: piece, fields) else { return true }
        sourcesModel.say(refused)
        return false
    }

    /// After the draft changed: the score shows its view again, and what is heard follows.
    private func mergeChanged() async {
        if wantedView != nil {
            await engraveScore()
        } else {
            await applyMergeView()
        }
        await drawVersions()
        if mergeModel.draft == nil { mergeModel.hear(nil) }
    }

    /// Apply: the draft's bars come into the score, its choices are kept with the piece, and the toast can undo it.
    private func applyDraft(_ piece: OpenPiece, toast: String? = nil) async {
        let at = ISO8601DateFormatter().string(from: Date())
        let request = facadeRequest(["at": at])
        let answer = await PieceCore(piece: piece.id).mergeApply(request)
        guard sourcesModel.read(performance: answer.value) else {
            sourcesModel.say(answer.message)
            return
        }
        mergeModel.end()
        sourcesModel.say(nil)
        mergeModel.say(MergeModel.Toast(text: toast ?? text.count("merge.applied", sourcesModel.versions.shown?.takenBars ?? 0), undo: toast == nil ? .unapply : nil))
        await notesDidChange()
    }

    private func unapply(_ piece: OpenPiece, _ id: String, toast: String, undo: MergeModel.Toast.Undo?) async {
        let request = facadeRequest(["id": id])
        let answer = await PieceCore(piece: piece.id).mergeUndoApplied(request)
        guard sourcesModel.read(performance: answer.value) else {
            sourcesModel.say(answer.message)
            return
        }
        mergeModel.say(MergeModel.Toast(text: toast, undo: undo))
        await notesDidChange()
    }

    // MARK: - Phrase focus

    /// Lifts the selection into focus (Expand, #521 19): the page dims, the card shows the selection's bars on their own, and
    /// the page neither scrolls nor takes the Pencil until the way back, which `entry` names (#521 17).
    func enterFocus(from entry: ExpandEntry = .notes) {
        guard shown, ExpandRules.opens(selected: selectionModel.selection != nil, penOn: penOn), !focused else { return }
        focusModel.open(from: entry)
        focused = true
        setPageStill(true)
        let label = text.format("focus.cardLabel", focusModel.position(in: text))
        let request: [String: Any] = ["open": true, "label": label, "back": text[entry.backKey]]
        Task {
            let answer = try? await scorePage.call(.focus, request) as? [String: Any]
            if let options = answer?["options"] as? [String: Any] { focusModel.takeCardOptions(facadeRequest(options)) }
            focusModel.updatePlayRange()
            await engraveCard()
            await updateFocusVersion()
        }
    }

    /// Expand on the roll drawer: the selection in focus as a roll, its way back Collapse (#521 17). Only with a selection
    /// (#521 19.1): without one the button says so instead (showNotice).
    func expandRoll() {
        guard selectionModel.selection != nil else { return }
        focusModel.showAsRoll(true)
        enterFocus(from: .roll)
    }

    /// Shows a word of guidance over the pill for a moment, as the hint to hold.
    func showNotice(_ key: String) {
        let shown = Notice(key: key)
        notice = shown
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(Self.noticeSeconds))
            if self?.notice == shown { self?.notice = nil }
        }
    }

    /// Back to the page, with the selection as it is: Back, a tap on the dimmed page, or Escape.
    func leaveFocus() {
        guard focused else { return }
        focused = false
        focusModel.close()
        setPageStill(false)
        Task {
            await scorePage.send(.focus, ["open": false])
            focusModel.updatePlayRange()
        }
    }

    /// Previous or next at the level shown: the card moves on to that unit. Outside focus (the pill's arrows, #328) the
    /// page brings it into view.
    func step(_ direction: Int) {
        guard let unit = focusModel.unit, let next = selectionModel.phrases?.unit(unit.number + direction.signum()) else { return }
        Task {
            await select(next.selectableOnset, next.selectableEnd)
            if !focused { await reveal(next.selectableOnset) }
        }
    }

    /// Scrolls the page smoothly to where a tick is, without a jump of page (also for the assistant's scope chip).
    func reveal(_ tick: Double) async {
        await scorePage.send(.reveal, ["start": tick])
    }

    // MARK: - Resume and beats

    /// Whether the page is engraved and everything on it drawn, so a resume point can be shown on it (#327).
    private(set) var rendered = false

    /// The beat lines a selection snaps to (#349): the player's while selecting by beats is on, none otherwise.
    var beatLines: [Double] = [] {
        didSet {
            guard beatLines != oldValue else { return }
            Task { await applyBeats() }
        }
    }

    private func applyBeats() async {
        guard shown else { return }
        await scorePage.send(.beats, ["lines": beatLines])
    }

    /// Back where the player stopped (#327): the passage selected and brought into view, and in focus when it was. It
    /// waits for the page to be drawn, so the level the piece opens in does not move the selection afterwards.
    func arrive(at place: ResumePlace) async {
        var waited = 0
        while !rendered && waited < 100 {
            try? await Task.sleep(nanoseconds: 100_000_000)
            waited += 1
        }
        guard rendered, piece != nil else { return }
        guard let passage = place.passage else {
            await reveal(place.at)
            return
        }
        await select(passage.lowerBound, passage.upperBound, step: false)
        guard let selection = selectionModel.selection else { return }
        await reveal(selection.onset)
        if place.focused { enterFocus() }
    }

    /// The tick at the top of the pages in view: where the player was when nothing is selected.
    func tickInView() async -> Double? {
        await firstTick(onPage: pages.first)
    }

    /// Engraves the selection's bars at the card's width and shows them on the card.
    private func engraveCard() async {
        guard focused, let piece, let selection = selectionModel.selection, let options = focusModel.cardOptions else { return }
        let first = selection.firstBar
        let last = selection.lastBar
        let id = piece.id
        let name = ossia
        let withOssia = name != nil
        // Without a variation, another version of the piece stands above as the performer has it (0022, section 5).
        let version = withOssia ? nil : sourcesModel.versions.shown
        let engraved = await MusicCore.run {
            Engraver.engraveCard(piece: id, firstBar: first, lastBar: last, options: options, ossia: withOssia, version: version?.id)
        }
        // Stepped on, left, or the variation or the version changed while it engraved: the newer call shows its own.
        guard focused, selectionModel.selection == selection, ossia == name, withOssia || sourcesModel.versions.shown == version else { return }
        switch engraved {
        case .success(let card):
            let timemap = (try? JSONSerialization.jsonObject(with: Data(card.timemap.utf8))) ?? []
            let label = name.map { text.format("focus.ossia", $0) } ?? version.map { text.format("focus.version", $0.name) } ?? ""
            let ossias = card.ossias.map { ["staves": $0.staves, "prefix": $0.prefix, "label": label, "added": $0.added] as [String: Any] }
            await scorePage.send(.card, ["pages": card.pages, "timemap": timemap, "start": selection.onset, "ossias": ossias])
        case .failure(let error):
            problem = error.message
        }
    }

    /// In focus the page under the veil stays where it is: it does not scroll and the Pencil does not write on it.
    private func setPageStill(_ still: Bool) {
        webView.scrollView.isScrollEnabled = !still
        ink.still = still
    }

    /// Rings the places of the fact chosen in Insights (0018 section 6); none clears them. With `scroll` the first comes
    /// into view.
    func highlight(_ places: [Fact.Place], scroll: Bool) {
        ringed = places
        guard shown else { return }
        Task { await showRings(scroll: scroll) }
    }

    private func showRings(scroll: Bool) async {
        let data: [String: Any] = ["places": ringed.map { ["onset": $0.onset, "end": $0.end, "noteIds": $0.noteIds] as [String: Any] }, "scroll": scroll]
        await scorePage.send(.highlight, data)
    }

    /// Where the player is (0018 section 7): the notes sounding at the tick turn cobalt on the page, or on the card in focus,
    /// and while playing the page keeps the system with them below the bar. `follow` (a new Play) follows again at once
    /// after the player scrolled by hand. Called with each position the player reports, about thirty times a second.
    func playhead(_ tick: Double, playing: Bool, follow: Bool = false) {
        guard shown else { return }
        // The page follows the playing unless the player turned that off ("playback.follow", testing item 4).
        let data: [String: Any] = ["tick": tick, "playing": playing, "follow": follow, "auto": DeviceSettings.shared.isOn("playback.follow")]
        scorePage.post(.playing, data)
    }

    /// Back to playback: the page scrolls to the playhead and follows again.
    func returnToPlayhead() {
        guard shown else { return }
        Task { await scorePage.send(.returnToPlayhead) }
    }

    /// Room under the last page for the drawer over the bottom of the score, so the end of the piece can still be read.
    func insetBottom(_ height: CGFloat) {
        guard shown else { return }
        Task { await scorePage.send(.inset, ["bottom": Double(height)]) }
    }

    /// The size of the selection menu, for the page to place it over the band.
    func menuSize(_ size: CGSize) {
        guard shown, size.width > 0 else { return }
        Task {
            await scorePage.send(.menuSize, ["width": size.width, "height": size.height])
        }
    }

    /// The selection menu dragged aside by the player and let go (#582 item 7): it stays where it was let go at once, and the
    /// page keeps that place inside the window until the menu closes (Scripts/score/menu-drag.ts), reporting it back.
    func moveMenu(by translation: CGSize) {
        guard shown, let place = selectionModel.menuPlace else { return }
        selectionModel.placeMenu(CGPoint(x: place.x + translation.width, y: place.y + translation.height))
        let data: [String: Any] = ["dx": Double(translation.width), "dy": Double(translation.height)]
        Task { await scorePage.send(.menuMoved, data) }
    }

    /// Whether the selection menu is shown; put away (the selection gone, Hide, the roll's drawer, Expand), the next one
    /// opens over its band.
    func menuShown(_ open: Bool) {
        guard shown, !open else { return }
        Task { await scorePage.send(.menuClosed) }
    }

    private func loadPhrases(_ piece: OpenPiece) async {
        await selectionModel.loadPhrases(of: piece.id)
        await showSelection()
    }

    /// Draws the band over the selection. The phrases are not drawn as brackets (Andrei, 2026-11-13, #479 item 14).
    func showSelection() async {
        guard shown else { return }
        var band: Any = NSNull()
        if let selection = selectionModel.selection {
            var drawn: [String: Any] = ["start": selection.onset, "end": selection.end, "startLabel": text["selection.handleStart"], "endLabel": text["selection.handleEnd"]]
            if let hand = selection.hand { drawn["hand"] = hand }
            if let picked = selection.picked { drawn["picked"] = picked }
            band = drawn
        }
        await scorePage.send(.select, band)
    }

    /// What a unit is called, as a screen reader and the assistant say it: "Phrase 3, bars 9–12".
    private func label(of unit: PhraseUnit) -> String {
        let name = unit.name.prefix(1).uppercased() + unit.name.dropFirst()
        let bars = unit.firstBar == unit.lastBar
            ? text.format("bars.one", String(unit.firstBar))
            : text.format("bars.range", String(unit.firstBar), String(unit.lastBar))
        return text.format("phrases.bracketLabel", name, String(unit.number), bars)
    }

    fileprivate func pageDidLoad() {
        pageReady = true
        ProbeCounts.shared.mark("page")
        if piece != nil { scheduleRender() }
    }

    private func render() async {
        guard let piece else { return }
        ProbeCounts.shared.add("renders")
        let timemap = (try? JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))) ?? []
        do {
            let laid = try await scorePage.call(.show, ["pages": piece.pages, "timemap": timemap, "dark": dark]) as? [String: Any]
            if let space = (laid?["space"] as? NSNumber)?.doubleValue, space > 0 {
                staffSpace = space
            }
            shown = true
            ProbeCounts.shared.mark("drawn")
            if let tick = piece.landing?.tick {
                // From the PDF's page in PDF | Notes (0023): the page of the notes where it starts.
                await scorePage.send(.showTick, ["tick": tick, "instant": true])
            }
            if Landing.notesShowOnRender(piece.landing) { land() }
            await scorePage.send(.pen, penOn)
            applyInput()
            await sendModeNow()
            await sendPlayheadsNow()
            await sendVisibilities()
            let marks = await MusicCore.run { MusicCore.call(piece: piece.id, "marks") }
            await draw(marks)
            ProbeCounts.shared.mark("marks")
            await loadPhrases(piece)
            await drawBookmarks()
            await loadCheck(piece)
            await loadPieceSettings(piece)
            await applyLayers()
            await applyLevel()
            await loadVersions(piece)
            if !ringed.isEmpty { await showRings(scroll: false) }
            await applyBeats()
            rendered = true
            ProbeCounts.shared.mark("rendered")
        } catch {
            problem = error.localizedDescription
            land()
        }
    }

    /// Shows the pages held out of sight while they landed (#533 6), now that they stand where they open.
    func land() {
        ProbeCounts.shared.mark("land")
        webView.alpha = 1
    }

    /// Draws the marks music-core answered (`marks`, `ink.add`, `ink.remove`).
    private func draw(_ answer: String) async {
        guard let object = facadeObject(answer) else { return }
        if object["error"] != nil {
            problem = FacadeAnswer.message(answer)
            return
        }
        let ink = object["ink"] as? [Any] ?? []
        markCount = ink.count
        let shelf = MarksShelf(object: object)
        marks = shelf
        await scorePage.send(.draw, ["marks": ink])
        let notes: [[String: Any]] = shelf.notes.map { note in
            let label = text.format("margin.markerLabel", String(shelf.number(of: note)), barsText(note.firstBar, note.lastBar, text), MarksShelf.firstWords(note.text))
            return ["id": note.id, "tick": note.onset, "number": shelf.number(of: note), "label": label]
        }
        let highlights: [[String: Any]] = shelf.highlights.map { ["id": $0.id, "start": $0.onset, "end": $0.end, "ink": $0.colour] }
        await scorePage.send(.notes, ["notes": notes, "highlights": highlights])
    }

    /// A change to the marks through music-core; the answer is drawn unless `draws` is false, for the first of two calls
    /// that make one change, so the page never shows it half done.
    private func change(_ piece: Int32, _ call: String, _ request: [String: Any], draws: Bool = true) async {
        let text = facadeRequest(request)
        let answer = await MusicCore.run { MusicCore.call(piece: piece, call, text) }
        if draws { await draw(answer) }
    }

    // MARK: - Strokes

    /// A stroke the pen finished (InkInput), its points in the web view's coordinates: in the page's pixels, then kept as
    /// a mark. The pen covers the whole page, so the Pencil writes in the margins too: a stroke begun left or right of a
    /// line is kept against the line's first or last bar and drawn from that edge of its line in any layout (`anchorMark`
    /// and `placeMark` in pencil-marks.ts), and music-core keeps its points thinned (`ink.add`). The stroke written stays
    /// over the page until the page draws it as kept.
    private func strokeEnded(_ samples: [InkSample]) {
        guard let piece, shown, !samples.isEmpty else {
            ink.live.dropOldest()
            return
        }
        let points: [[String: Double]] = samples.map { sample in
            var point = pagePoint(sample.point)
            point["pressure"] = sample.pressure
            return point
        }
        let pen = pen
        inkQueue.then { [weak self] in
            guard let self else { return }
            switch pen.stroke {
            case .pen(let colour):
                await self.add(piece.id, points, tool: "pen", colour: colour.rawValue, width: pen.pencil.width)
            case .highlighter:
                await self.add(piece.id, points, tool: "highlighter", colour: "amber", width: pen.marker.width)
            case .eraser:
                // The eraser rubs out as it moves (`rubbed`); a stroke written before it was chosen is not kept.
                break
            }
            self.ink.live.dropOldest()
        }
    }

    /// A point of the web view in the page's pixels, as the page measures its bars and marks.
    private func pagePoint(_ point: CGPoint) -> [String: Double] {
        let zoom = max(webView.scrollView.zoomScale, 0.01)
        let inPage = webView.scrollView.convert(point, from: webView)
        return ["x": inPage.x / zoom, "y": inPage.y / zoom]
    }

    /// The eraser moved (InkInput, BUG-05): the page takes the marks under it at once (`motiff.rub`), cutting them where
    /// it passes (#479 item 7), with nothing drawn for the eraser itself; when it lifts, the press is saved as one step.
    /// Points that come while the page is still busy go together in the next send, so a fast eraser never queues up.
    private func rubbed(_ points: [CGPoint], lifted: Bool) {
        rubPoints += points.map(pagePoint)
        if !rubQueued, !rubPoints.isEmpty {
            rubQueued = true
            inkQueue.then { [weak self] in await self?.sendRub() }
        }
        if lifted {
            let piece = piece?.id
            inkQueue.then { [weak self] in await self?.endRub(piece) }
        }
    }

    private func sendRub() async {
        rubQueued = false
        let points = rubPoints
        rubPoints = []
        guard !points.isEmpty else { return }
        await scorePage.send(.rub, ["points": points])
    }

    /// The eraser lifted: what the press did is saved as one step (`ink.add` for the pieces left, `ink.remove` for the
    /// marks it cut, drawn once both are done), and Undo puts it back whole.
    private func endRub(_ piece: Int32?) async {
        guard let cut = try? await scorePage.call(.rubEnd) as? [String: Any],
              let removed = cut["removed"] as? [[String: Any]], !removed.isEmpty, let piece
        else { return }
        let pieces = cut["added"] as? [[String: Any]] ?? []
        if !pieces.isEmpty {
            await change(piece, "ink.add", ["strokes": pieces], draws: false)
        }
        await change(piece, "ink.remove", ["ids": removed.compactMap { $0["id"] as? String }])
        steps.record(InkStep(added: pieces.compactMap { $0["id"] as? String }, removed: removed, strokes: pieces))
        inkStepsChanged()
    }

    /// The mark `motiff.anchor` makes goes to `ink.add` as it is, its `margin` (v0.4) with it when the page sets one.
    private func add(_ piece: Int32, _ points: [[String: Double]], tool: String, colour: String, width: Int) async {
        let request: [String: Any] = ["points": points, "tool": tool, "colour": colour, "width": width]
        guard let mark = try? await scorePage.call(.anchor, request) as? [String: Any],
              let id = mark["id"] as? String
        else { return }
        steps.record(InkStep(added: [id], strokes: [mark]))
        inkStepsChanged()
        await change(piece, "ink.add", ["strokes": [mark]])
    }

    /// Who writes, selects and scrolls (InputRules, #479 item 18): the pen's gesture and the scroll view take the
    /// pointers the rules give them, and the page is told how a finger and the Pencil select and tap. Again when the app
    /// comes back, as the iPad's setting «Only draw with Apple Pencil» may have changed.
    func applyInput() {
        let rules = InputRules(stylus: InkInput.stylusPreferred, tool: InputRules.Tool(mode: mode, penOn: penOn))
        ink.apply(rules)
        toolChanged?(mode, penOn)
        guard shown else { return }
        Task { await scorePage.send(.input, rules.page) }
    }

    /// Where the Pencil hovers over the window, for the stylus menu's place over the roll and the rest of the screen (#521 7).
    func watchStylus(in window: UIWindow?) {
        ink.pencil.watch(window)
    }

    /// A stroke of the pen began or ended (#521 10): while it is written playback does not move the page under it, and it
    /// follows again as long after as after a scroll by hand.
    private func penDown(_ down: Bool) {
        guard shown else { return }
        Task { await scorePage.send(.writing, ["on": down]) }
    }

    /// Handles, given in the document's pixels, where they are on the screen now, in the web view's coordinates.
    private func inView(_ spots: [CGPoint]) -> [CGPoint] {
        let zoom = webView.scrollView.zoomScale
        return spots.map { webView.scrollView.convert(CGPoint(x: $0.x * zoom, y: $0.y * zoom), to: webView) }
    }

    /// The mode as the page has it: "pen" while the Pen is on.
    private func sendMode() {
        guard shown else { return }
        Task { await sendModeNow() }
    }

    private func sendModeNow() async {
        let data: [String: Any] = ["name": penOn ? InteractionMode.pen.rawValue : mode.rawValue]
        await scorePage.send(.mode, data)
    }

    /// The one playback position the page draws (v0.5.2, #479 item 19): `{ selection: {start, end} or null }`, inside which it
    /// is the cobalt line and outside it the ink track under its bar; nil draws the one playhead as before.
    func showPlayheads(_ data: [String: Any]?) {
        playheadsWanted = data
        guard shown else { return }
        let key = data.map { fields in fields.keys.sorted().map { name in "\(name)=\(String(describing: fields[name] ?? ""))" }.joined(separator: ",") } ?? ""
        guard key != playheadsShown else { return }
        playheadsShown = key
        Task { await sendPlayheadsNow() }
    }

    private func sendPlayheadsNow() async {
        let value: Any = playheadsWanted.map { $0 as Any } ?? NSNull()
        await scorePage.send(.playheads, value)
    }

    /// How the stroke beginning now looks while it is written: as the page will draw it at this zoom (InkOutline), the
    /// pencil in its ink with the page's grain from the page's top left corner, the highlighter opaque on a layer as
    /// translucent as the page's. The eraser draws nothing: what it takes goes from the page as it moves (BUG-05).
    private func liveStyle(at point: CGPoint) -> LiveInkView.Style? {
        let zoom = webView.scrollView.zoomScale
        let theme = UITraitCollection(userInterfaceStyle: dark ? .dark : .light)
        let space = Double(staffSpace * zoom)
        switch tool {
        case .pen(let colour):
            let origin = webView.scrollView.convert(CGPoint.zero, to: webView)
            return LiveInkView.Style(
                colour: DesignTokens.dynamicColor("ink-\(colour.rawValue)").resolvedColor(with: theme),
                radius: PenSizes.pen[pen.pencil.width] * space / 2, followsPressure: true,
                grain: (origin: origin, tile: InkPaint.tile * zoom)
            )
        case .highlighter:
            return LiveInkView.Style(
                colour: DesignTokens.dynamicColor("amber").resolvedColor(with: theme),
                radius: PenSizes.highlighter[pen.marker.width] * space / 2, followsPressure: false, alpha: PenSizes.highlighterAlpha
            )
        case .eraser:
            return nil
        }
    }
}

/// Passes the page's messages on without the web view keeping the controller alive.
private final class MessageRelay: NSObject, WKScriptMessageHandler {
    weak var controller: ScoreController?

    init(_ controller: ScoreController) {
        self.controller = controller
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        if message.body as? String == "ready" {
            MainActor.assumeIsolated { controller?.pageDidLoad() }
        } else if let body = message.body as? [String: Any] {
            MainActor.assumeIsolated { controller?.received(body) }
        }
    }
}

/// Serves the app's own files to the web view as `motiff://app/<path>`, so the score's modules load as modules do on
/// the web: Web/score.html, score/*.js (the web app's compiled Scripts/score) and tokens.css.
final class BundleSchemeHandler: NSObject, WKURLSchemeHandler {
    static let scheme = "motiff"

    static func url(_ path: String) -> URL {
        URL(string: "\(scheme)://app/\(path)")!
    }

    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        guard let url = task.request.url, let root = Bundle.main.resourceURL else {
            task.didFailWithError(URLError(.badURL))
            return
        }
        let file = root.appendingPathComponent(String(url.path.drop { $0 == "/" })).standardizedFileURL
        guard file.path.hasPrefix(root.standardizedFileURL.path), let data = try? Data(contentsOf: file) else {
            task.didFailWithError(URLError(.fileDoesNotExist))
            return
        }
        let type: String = switch file.pathExtension {
        case "html": "text/html"
        case "js", "mjs": "text/javascript"
        case "css": "text/css"
        case "json": "application/json"
        default: UTType(filenameExtension: file.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
        }
        task.didReceive(URLResponse(url: url, mimeType: type, expectedContentLength: data.count, textEncodingName: "utf-8"))
        task.didReceive(data)
        task.didFinish()
    }

    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {}
}

/// The note editor on the roll while it is open (testing item 15): the tool a press uses ("move", "add" or "delete"),
/// what Undo, Reset and Save can do, and the note the arrows and Delete act on.
struct NoteEditorState: Equatable, Sendable {
    var tool = "move"
    var canUndo = false
    var changed = false
    var canReset = false
    var picked: String?
}

// MARK: - The selection's Undo and Redo (#511 section 11): the steps and the selecting are SelectionModel's, what follows the root's

extension ScoreController {
    /// Undo of the selection (the Selection row, the Pencil's ring, ⌘Z, «Вернуть»): the selection before the last step,
    /// selected again exactly; the score neither scrolls nor zooms.
    func undoSelection() {
        guard let step = selectionModel.undo() else { return }
        Task { await restore(step.before) }
    }

    /// Redo of the selection (the Selection row, ⇧⌘Z).
    func redoSelection() {
        guard let step = selectionModel.redo() else { return }
        Task { await restore(step.after) }
    }

    /// A gesture cancelled after its hold took the selection away: the selection comes back and no step stays.
    func revertGesture(_ gesture: String) {
        guard let step = selectionModel.revert(gesture) else { return }
        Task { await restore(step.before) }
    }

    private func restore(_ span: SelectionSpan?) async {
        guard let span else {
            clearSelection(step: false)
            return
        }
        await select(request: span.request, step: false, picked: span.picked != nil)
    }
}
