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
            if shown { Task { _ = try? await webView.callAsyncJavaScript("return motiff.pen(on)", arguments: ["on": penOn], contentWorld: .page) } }
            sendMode()
        }
    }
    /// The mode the bar chose (version 0.5, the experiment Two playheads and modes), other than the Pen; nil while the
    /// experiment is off, which keeps the score as before.
    @Published var mode: InteractionMode? {
        didSet {
            guard mode != oldValue else { return }
            applyInput()
            sendMode()
        }
    }
    /// The Pen turned on or off: the roll's rules follow it (#511 4.1).
    var penChanged: (@MainActor (Bool) -> Void)?
    /// Whether the roll is in Edit notes: then Undo is the note edits' (#521 9).
    var rollEditing: (@MainActor () -> Bool)?
    /// A tap on a beat under the modes, from its beat line to the next: a playhead goes there.
    var placeTapped: (@MainActor (Range<Double>) -> Void)?
    /// A playhead's handle taken, dragged or let go on the page (#495 UX-08): the player runs the model (PlayheadDrag.swift).
    var playheadGestured: (@MainActor (PlayheadGesture) -> Void)?
    /// The one playback position as the page last had it (the selection it turns cobalt in), or nil for the one playhead of before.
    private var playheadsWanted: [String: Any]?
    private var playheadsShown = ""
    /// The phrases at the level shown (0018 section 3), no longer drawn on the score (#479 item 14).
    @Published private(set) var phrases: PhraseLevelsShown?
    /// «Не показывать» in the Phrases menu (#479 item 21): the phrases are not named, in the Contents nor for the
    /// selection, until a level is chosen again. Kept on this device for every piece; the piece's level stays as it was.
    @Published var phrasesHidden = UserDefaults.standard.bool(forKey: ScoreController.phrasesHiddenKey) {
        didSet { UserDefaults.standard.set(phrasesHidden, forKey: Self.phrasesHiddenKey) }
    }
    static let phrasesHiddenKey = "motiff.phrases.hidden"
    /// The passage selected on the score: a bar tapped, or bars chosen by a long press and a drag.
    @Published private(set) var selection: ScoreSelection?
    /// The selection's Undo and Redo (#511 11, SelectionHistory), kept for the piece beside the screen (SelectionHistories).
    @Published private(set) var selectionSteps = SelectionHistory<SelectionSpan>()
    /// «Выделение снято · Вернуть» for a while after a clear or a replace (#511 11); nil while none shows.
    @Published private(set) var selectionToast: SelectionToast?
    /// «Задержите касание…» for a moment after a pointer that cannot scroll moved off the selection (#511 2.1, C).
    @Published private(set) var holdHint: UUID?
    /// A word of guidance for a moment over the pill, by the key of its text, such as why Expand waits for a selection (#521 19).
    @Published private(set) var notice: Notice?

    struct Notice: Equatable {
        var key: String
        var id = UUID()
    }
    /// Select's «По тактам / По долям» (#511 4.2), and the beat lines and the experiment it chooses between (SelectionTools.swift).
    @Published var selectsByBeats = SelectBy.saved()
    var pieceBeatLines: [Double] = []
    var beatsExperiment = false
    /// The piece as its selection's history knows it (SelectionHistories.key).
    private var historyKey: String?
    /// Where the selection menu's top left corner goes in the score's view, as the web app places its menu; nil while
    /// nothing is selected or a drag is on.
    @Published private(set) var menuPlace: CGPoint?
    /// A tap on the page that selects nothing: away from the bars, or anywhere while a passage is selected. The Insights
    /// sheet goes away; the selection stays (#338).
    var blankTapped: (@MainActor () -> Void)?
    /// Phrase focus (0018 section 4): the selection lifted into a card across the screen, the page dimmed behind it. Since
    /// v0.5.5 Expand (#521 19): bound to the selection it was opened on, which nothing changes until the way back.
    @Published private(set) var focused = false
    /// Where Expand was opened from, which names the way back (#521 17): the score's «К странице», the roll's «Свернуть».
    @Published private(set) var expandEntry = ExpandEntry.notes
    /// What Play and Loop take for the selection: its span (Lead-in went in v0.5.6, #533 3).
    @Published private(set) var playRange: PlayRange?
    /// The name of the variation being tried, which the card in focus shows as an ossia above the phrase; nil for none.
    var ossia: String? {
        didSet { if oldValue != ossia, focused { Task { await engraveCard() } } }
    }

    /// The span play takes for a selection, and the selection it was taken for.
    struct PlayRange: Equatable {
        var selection: ScoreSelection
        var range: Range<Double>
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
    /// A Check flag tapped on the score: the screen opens the Check drawer on its bar.
    var checkPicked: (@MainActor (Int) -> Void)?
    /// The other versions of the piece and the one shown, compared bar by bar (0022).
    @Published private(set) var versions = VersionShelf()
    /// What the last thing done with a version came to: "Bar 12 is now as in Cover", or why it could not be done.
    @Published private(set) var versionStatus: String?
    /// The piece's sources (testing item 16): the base, the line on what the player's version holds, the PDF kept inside.
    @Published private(set) var sources = SourcesInfo()
    /// The version whose page Sources shows (testing item 32): its overview, the merge draft and what was merged; nil for
    /// the list of sources.
    @Published var versionPage: String?
    /// The merge draft open now, as music-core answers `performance.merge` (testing items 32 and 34); nil with none.
    @Published private(set) var merge: MergeInfo?
    /// The change the draft shows, by its bar.
    @Published private(set) var mergeAt: Int?
    /// Whether a choice in the draft goes for the change's bar or every change of its phrase.
    @Published var mergePhrase = false
    /// What the score shows while a draft is open: "mine", "version" or "result".
    @Published private(set) var mergeShow = "result"
    /// What was last asked to be heard of the draft: "mine", "version", "result" or "alternate".
    @Published private(set) var mergeHearing: String?
    /// The full-screen Compare versions window over the draft.
    @Published var comparing = false
    /// What was just done with a version, with the way to undo it: a merge applied or undone, a version removed.
    @Published private(set) var mergeToast: MergeToast?
    /// A version's marker tapped on the score: the popover under it says how the bar differs (testing item 33).
    @Published private(set) var markerPick: MarkerPick?

    struct MergeToast: Equatable {
        enum Undo: Equatable { case unapply, reapply, putBack }
        var text: String
        var undo: Undo?
    }

    struct MarkerPick: Equatable {
        var bar: Int
        /// Where the marker is on the screen, in the web view's points: the popover goes under it.
        var left: Double
        var bottom: Double
    }

    /// The draft's notes to hear over a span, once: the screen's player plays them on their own.
    var hearNotes: (@MainActor ([PlaybackNote], Range<Double>) -> Void)?
    /// The player's notes, then the version's, in turn over a span.
    var hearInTurn: (@MainActor ([PlaybackNote], Range<Double>) -> Void)?
    /// The way into a version's page from the score (its marker's popover, focus): the screen opens Sources.
    var versionPicked: (@MainActor (Int) -> Void)?
    /// The piece's notes changed (bars of another version taken or restored): the player and the roll load them again.
    var notesChanged: (@MainActor () -> Void)?
    /// The version shown, for the phrase in focus with no variation open: what Version plays, its name, and the bars of
    /// the phrase it has otherwise or that were taken from it (0022, section 5); nil out of focus or without a version.
    @Published private(set) var focusVersion: FocusVersion?

    struct FocusVersion: Equatable {
        var id: String
        var name: String
        /// The version's notes over the whole piece in the score's time, for the player's second source.
        var notes: [PlaybackNote]
        /// Whether any of them start in the phrase: only then can it be heard there.
        var sounds: Bool
        /// The bars of the phrase the version has otherwise, the first of which Open in the draft goes to.
        var differing: [Int]
    }

    /// The draft's view the page is engraved with ("version" or "result"); nil for the player's notes.
    private var engravedView: String?
    /// On a view engraved, the ids of the notes from the version (plum) and of the player's own (amber).
    private var mergeAdded: [String] = []
    private var mergeOwn: [String] = []
    /// Even bars (Settings · Notes, testing item 35), as the page was engraved: a change engraves it again.
    private var engravedEven = DeviceSettings.shared.isOn("score.evenBars")
    /// The version's notes as music-core gives them to play (`playback` of `version`), for the version they are of.
    private var versionPlayback: (id: String, notes: [PlaybackNote])?

    /// The layers of the player's marks.
    var pieceLayers: [String] { Self.layers }

    /// The kinds of extra information this piece has: the differences and taken bars with a version, Check with flags.
    var pieceExtra: [String] {
        Self.extra.filter { item in
            switch item {
            case "check": check.hasFlags
            default: !versions.entries.isEmpty
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
    /// The note edits were saved: the screen writes the project file.
    var noteEditSaved: (@MainActor () -> Void)?
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
    /// The pen, and who writes and who scrolls (#479 items 7 and 18).
    private let ink = InkInput()
    private var piece: OpenPiece?
    private var dark = false
    private var pageReady = false
    private var shown = false
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
    /// Verovio's options for the card at its width now, as the page gives them.
    private var cardOptions: String?
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

    override init() {
        let configuration = WKWebViewConfiguration()
        configuration.setURLSchemeHandler(BundleSchemeHandler(), forURLScheme: BundleSchemeHandler.scheme)
        webView = WKWebView(frame: .zero, configuration: configuration)
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

        webView.load(URLRequest(url: BundleSchemeHandler.url("Web/score.html")))
    }

    /// Shows a piece's pages and its marks; again when the appearance changes.
    func show(_ piece: OpenPiece, dark: Bool) {
        guard self.piece?.id != piece.id || self.dark != dark || !shown else { return }
        // The same piece keeps the pages engraved here after its notes changed or its comparison layer came on.
        if self.piece?.id != piece.id {
            self.piece = piece
            // The pages wait out of sight for the place they land on (#533 6), so the top of the first one never shows first.
            webView.alpha = Landing.holdsTheNotes(piece.landing) ? 0 : 1
            historyKey = SelectionHistories.key(piece)
            selectionSteps = SelectionHistories.history(of: SelectionHistories.key(piece))
            rendered = false
            versions = VersionShelf()
            versionStatus = nil
            sources = SourcesInfo()
            versionPage = nil
            merge = nil
            mergeAt = nil
            mergeToast = nil
            markerPick = nil
            comparing = false
            engravedView = nil
            engravedEven = DeviceSettings.shared.isOn("score.evenBars")
            versionPlayback = nil
        }
        self.dark = dark
        if pageReady { Task { await render() } }
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

    /// What the page reports: the pages in view, where the menu goes, and what the player did on the score.
    fileprivate func received(_ message: [String: Any]) {
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        if let report = message["pages"] as? [String: Any] {
            pages = PagesInView(first: Int(number(report["first"]) ?? 0), last: Int(number(report["last"]) ?? 0), count: Int(number(report["count"]) ?? 0))
        }
        if message.keys.contains("menu") {
            let place = message["menu"] as? [String: Any]
            menuPlace = place.flatMap { place in number(place["left"]).map { CGPoint(x: $0, y: number(place["top"]) ?? 0) } }
        }
        if let chosen = message["chosen"] as? [String: Any], let start = number(chosen["start"]), let end = number(chosen["end"]) {
            // By beats (#349) music-core takes the edges to the beat lines; otherwise to the bars, as before. The gesture's
            // changes are one step of the selection's history (#511 11).
            let gesture = chosen["gesture"] as? String
            if chosen["beats"] as? Bool == true {
                selectBeats(start, end, gesture: gesture)
            } else {
                Task { await select(start, end, gesture: gesture) }
            }
        }
        receivedGesture(message)
        // A tap in a bar selects it only when nothing is selected: the selection stays until Clear selection or another
        // choice (a long press and drag, a handle, the roll's bar numbers), so a stray tap cannot lose it (#338).
        if let bar = message["bar"] as? [String: Any], let start = number(bar["start"]), let end = number(bar["end"]) {
            if selection == nil {
                Task { await select(start, end) }
            } else {
                blankTapped?()
            }
        }
        if let bookmark = message["bookmark"] as? String {
            goToBookmark(bookmark)
        }
        if let note = message["noteMarker"] as? String {
            openNote(note)
        }
        if let handles = message["handles"] as? [[String: Any]] {
            handleSpots = handles.compactMap { spot in number(spot["x"]).map { CGPoint(x: $0, y: number(spot["y"]) ?? 0) } }
        }
        if let away = number(message["playheadAway"]) {
            playheadAway = away < 0 ? nil : away
        }
        if let bar = number(message["checkFlag"]) {
            showCheckBar(Int(bar))
            checkPicked?(Int(bar))
        }
        if let bar = number(message["performanceBar"]) {
            markerPick = MarkerPick(bar: Int(bar), left: number(message["left"]) ?? 0, bottom: number(message["bottom"]) ?? 0)
            showVersionBar(Int(bar))
        }
        receivedUnderModes(message)
        // A tap away from the bars puts the sheet away, and leaves the selection as it is (#338).
        if message["blank"] != nil {
            blankTapped?()
        }
        if message["leave"] != nil {
            leaveFocus()
        }
        if let options = message["cardLayout"] as? [String: Any] {
            cardOptions = Self.json(options)
            Task { await engraveCard() }
        }
    }

    /// Under the modes (version 0.5) a tap places a playhead on its beat; notes are edited only in the roll (#479 item 23).
    /// A playhead's handle dragged goes to the player, and where the handles are is kept for the pen (#495 UX-08).
    private func receivedUnderModes(_ message: [String: Any]) {
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        if let place = message["place"] as? [String: Any], let start = number(place["start"]), let end = number(place["end"]) {
            placeTapped?(start..<max(end, start + 1))
        }
        if let heads = message["heads"] as? [[String: Any]] {
            headSpots = heads.compactMap { spot in number(spot["x"]).map { CGPoint(x: $0, y: number(spot["y"]) ?? 0) } }
        }
        if let gesture = PlayheadGesture(message: message["drag"]) {
            playheadGestured?(gesture)
        }
    }

    // MARK: - Phrases and the selection

    /// Shows another level: the selection moves to the unit that is, encloses or starts it at the new level. The units are
    /// not drawn on the score (#479 item 14).
    func showLevel(_ level: String) {
        phrasesHidden = false
        guard let piece, level != phrases?.level else { return }
        let request = "{\"level\": \"\(level)\"}"
        pieceOverrides[Self.granularity] = level == "short" ? "short" : level == "sentence" ? "sentences" : "phrases"
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "phrases.level", request) }
            if let shown = PhraseLevelsShown(answer: answer) { phrases = shown }
            if selection != nil, let moved = ScoreSelection(answer: answer) { selection = moved }
            await showSelection()
            await updatePlayRange()
        }
    }

    /// «Вернуть найденные» (the web app's Reset level): the level shown forgets the player's own boundaries, made in
    /// the Music Studio, and has the units found in the file again.
    func resetLevel() {
        guard let piece, let level = phrases?.level, phrases?.edited.contains(level) == true else { return }
        let request = "{\"reset\": true, \"level\": \"\(level)\"}"
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "phrases.edit", request) }
            if let shown = PhraseLevelsShown(answer: answer) { phrases = shown }
            await showSelection()
            await updatePlayRange()
        }
    }

    /// A unit chosen by its number (the screenshot walk, ScreensHook): it is selected; chosen again, it lifts into focus.
    func selectPhrase(_ number: Int) {
        guard let unit = phrases?.unit(number) else { return }
        if let selection, selection.onset == unit.selectableOnset, selection.end == unit.selectableEnd {
            enterFocus()
            return
        }
        Task { await select(unit.selectableOnset, unit.selectableEnd) }
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
        if extend, let selection {
            if first < selection.firstBar { to = selection.lastBar } else { from = selection.firstBar }
        }
        Task { await select(request: "{\"bars\": {\"first\": \(from), \"last\": \(to)}}", gesture: gesture) }
    }

    /// Selects through music-core; `step` false for a selection the player did not make now (the resume point, Undo).
    func select(request: String, gesture: String? = nil, step: Bool = true) async {
        guard let piece else { return }
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "select", request) }
        guard let chosen = ScoreSelection(answer: answer) else {
            problem = facadeObject(answer)?["message"] as? String
            return
        }
        if step { noteSelectionChange(from: selection, to: chosen, gesture: gesture) }
        selection = chosen
        await showSelection()
        await updatePlayRange()
        if focused {
            await engraveCard()
            await updateFocusVersion()
        }
    }

    /// Takes the selection away: ✕, Escape, or a hold outside it (`gesture`, #511 2.1); `step` false for Undo.
    func clearSelection(gesture: String? = nil, step: Bool = true) {
        guard selection != nil else { return }
        if step { noteSelectionChange(from: selection, to: nil, gesture: gesture) }
        // Nothing to lift: focus goes with the selection.
        leaveFocus()
        selection = nil
        Task {
            await showSelection()
            await updatePlayRange()
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
        _ = try? await webView.callAsyncJavaScript("return motiff.bookmarks(data)", arguments: ["data": ["items": bookmarkItems]], contentWorld: .page)
    }

    /// A bookmark gone to from its bracket or its entry: the score selects it and brings it into view; gone to again with
    /// the selection as it left it, the passage lifts into focus. True when it lifted.
    @discardableResult
    func goToBookmark(_ id: String) -> Bool {
        guard let span = bookmarkSpans[id] else { return false }
        if let shownBookmark, shownBookmark.id == id, shownBookmark.selection == selection, !penOn {
            enterFocus()
            return true
        }
        Task {
            await select(span.lowerBound, span.upperBound)
            if let selection { shownBookmark = ShownBookmark(id: id, selection: selection) }
            _ = try? await webView.callAsyncJavaScript("return motiff.reveal(data)", arguments: ["data": ["start": span.lowerBound]], contentWorld: .page)
        }
        return false
    }

    /// Whether the selection is the bookmark last gone to: its entry in the Contents shows it.
    func isShown(_ id: String) -> Bool {
        shownBookmark?.id == id && shownBookmark?.selection == selection
    }

    /// The title a new bookmark starts with: "Phrase 3, bars 9–12" for a unit of the level shown, else "Bars 9–12".
    /// The open piece's title, for what the assistant knows.
    var pieceTitle: String { piece?.title ?? "" }

    /// The assistant's scope chip for the selection (testing item 23): "This phrase · bars 9–16" or "This selection · bar 3".
    var askLabel: String? {
        guard let selection else { return nil }
        let bars = selection.firstBar == selection.lastBar
            ? text.format("bars.one", String(selection.firstBar))
            : text.format("bars.range", String(selection.firstBar), String(selection.lastBar))
        let phrase = !phrasesHidden && phrases?.units.contains { $0.selectableOnset == selection.onset && $0.selectableEnd == selection.end } == true
        return text.format(phrase ? "ask.thisPhrase" : "ask.thisSelection", bars)
    }

    var bookmarkTitle: String {
        guard let selection else { return "" }
        if !phrasesHidden, let unit = phrases?.units.first(where: { $0.selectableOnset == selection.onset && $0.selectableEnd == selection.end }) {
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
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.clearLeft(data)", arguments: ["data": ["width": Double(width)]], contentWorld: .page) }
    }

    /// The pages with their stretches of the piece and their pictures, `width` pixels wide, for the Pages tab.
    func outline(width: CGFloat) async -> [PageOutline] {
        guard shown,
              let answer = try? await webView.callAsyncJavaScript("return motiff.outline(data)", arguments: ["data": ["width": Double(width)]], contentWorld: .page) as? [Any]
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
        let tick = try? await webView.callAsyncJavaScript("return motiff.firstTickOn(data)", arguments: ["data": ["page": page]], contentWorld: .page)
        return (tick as? NSNumber)?.doubleValue
    }

    /// Brings a page to the top of the view, under the bar.
    func showPage(_ page: Int) {
        guard shown else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.showPage(data)", arguments: ["data": ["page": page]], contentWorld: .page) }
    }

    // MARK: - Margin notes, highlights and layers

    /// Starts a note over the selection.
    func startNote() {
        guard let selection else { return }
        noteDraft = NoteDraft(id: nil, onset: selection.onset, end: selection.end, firstBar: selection.firstBar, lastBar: selection.lastBar, text: "")
    }

    /// Opens a note to read or change: its bars are selected and the card shows its words.
    func openNote(_ id: String) {
        guard let note = marks.notes.first(where: { $0.id == id }) else { return }
        let draft = NoteDraft(id: id, onset: note.onset, end: note.end, firstBar: note.firstBar, lastBar: note.lastBar, text: note.text)
        Task {
            if selection?.onset != note.onset || selection?.end != note.end {
                await select(note.onset, note.end)
                _ = try? await webView.callAsyncJavaScript("return motiff.reveal(data)", arguments: ["data": ["start": note.onset]], contentWorld: .page)
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
        guard let selection else { return nil }
        return marks.highlights.last { $0.onset == selection.onset && $0.end == selection.end }
    }

    /// Lays the selection in an ink; the highlight already over it gives way to the new one.
    func highlightSelection(_ ink: String) {
        guard let piece, let selection else { return }
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
                let request = Self.json(["id": "layers.\(layer)", "value": on ? "on" : "off"]) ?? "{}"
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
        _ = try? await webView.callAsyncJavaScript("return motiff.layers(data)", arguments: ["data": ["hidden": hidden, "slate": slate]], contentWorld: .page)
    }

    /// The level a piece opens in (the setting "phrases.granularity"), shown without becoming the piece's own; a piece that
    /// has its own keeps it.
    private func applyLevel() async {
        guard let piece, pieceOverrides[Self.granularity] == nil, let shownLevel = phrases?.level else { return }
        let level = switch DeviceSettings.shared[Self.granularity] {
        case "short": "short"
        case "sentences": "sentence"
        default: "phrase"
        }
        guard level != shownLevel else { return }
        let request = "{\"level\": \"\(level)\", \"remember\": false}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "phrases.level", request) }
        if let levels = PhraseLevelsShown(answer: answer) { phrases = levels }
        if selection != nil, let moved = ScoreSelection(answer: answer) { selection = moved }
        await showSelection()
        await updatePlayRange()
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
                let request = Self.json(["id": id, "value": NSNull()]) ?? "{}"
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
            _ = try? await webView.callAsyncJavaScript("return motiff.reveal(data)", arguments: ["data": ["start": span.lowerBound]], contentWorld: .page)
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
        let request = Self.json(fields) ?? "{}"
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "check.resolve", request) }
            if let shelf = CheckShelf(answer: answer) {
                check = shelf
                await drawChecks()
            } else {
                problem = facadeObject(answer)?["message"] as? String
            }
        }
    }

    /// Fix notes on a bar: it is selected and comes into view, and its notes are listed to correct; again, the list closes.
    func fixCheckBar(_ bar: Int) {
        guard corrections?.bar != bar else {
            corrections = nil
            return
        }
        showCheckBar(bar)
        correct("corrections", ["bar": bar])
    }

    /// A note of the bar a step higher or lower (semitones), earlier or later, shorter or longer (sixteenths), softer or
    /// louder: a correction in the piece's edit log, and the score engraved again.
    func correctNote(_ id: String, pitch: Int = 0, onset: Int = 0, length: Int = 0, velocity: Int = 0) {
        correct("corrections.change", ["id": id, "pitch": pitch, "onset": onset, "length": length, "velocity": velocity], notes: true)
    }

    /// Takes the note out of the piece.
    func removeNote(_ id: String) { correct("corrections.remove", ["id": id], notes: true) }

    /// Puts the note back as it was imported.
    func revertNote(_ id: String) { correct("corrections.revert", ["id": id], notes: true) }

    /// Takes back the last correction.
    func undoCorrection() { correct("corrections.undo", [:], notes: true) }

    private func correct(_ call: String, _ fields: [String: Any], notes: Bool = false) {
        guard let piece, let bar = fields["bar"] as? Int ?? corrections?.bar else { return }
        var request = fields
        request["bar"] = bar
        let body = Self.json(request) ?? "{}"
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, call, body) }
            guard let read = NoteCorrections(answer: answer) else {
                problem = facadeObject(answer)?["message"] as? String
                return
            }
            corrections = read
            if notes { await notesDidChange() }
        }
    }

    // MARK: - Note editor on the roll

    /// Edit notes in the roll's drawer (testing item 15): the editor opens on a draft in music-core, so every edit sounds
    /// and shows at once but the project file keeps the notes as they were until Save; Cancel puts them back.
    func beginNoteEdit() {
        noteEditor = NoteEditorState()
        noteEditCall("corrections.begin", [:])
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
            noteEditCall("corrections.set", ["id": id, "onset": onset, "pitch": pitch, "length": length], notes: true)
        case let .add(onset, pitch, length):
            noteEditCall("corrections.add", ["onset": onset, "pitch": pitch, "length": length], notes: true)
        case let .remove(id):
            noteEditor?.picked = nil
            noteEditCall("corrections.remove", ["id": id], notes: true)
        }
    }

    /// Takes back the last edit, the reset too.
    func undoNoteEdit() { noteEditCall("corrections.undo", [:], notes: true) }

    /// Every correction taken out: the notes as imported, until Save.
    func resetNotes() { noteEditCall("corrections.reset", [:], notes: true) }

    /// Closes the editor with the notes as they were when it opened.
    func cancelNoteEdit() {
        guard noteEditor != nil else { return }
        let changed = noteEditor?.changed ?? false
        noteEditor = nil
        noteEditCall("corrections.discard", [:], notes: changed)
    }

    /// Keeps the edits with the piece and writes the project file.
    func saveNoteEdit() {
        guard noteEditor != nil else { return }
        noteEditor = nil
        noteEditCall("corrections.save", [:]) { [weak self] in self?.noteEditSaved?() }
    }

    private func noteEditCall(_ call: String, _ fields: [String: Any], notes: Bool = false, then: (@MainActor () -> Void)? = nil) {
        guard let piece else { return }
        var request = fields
        // The corrections answer with a bar's notes; the editor needs only what the answer says about the draft.
        request["bar"] = selection?.firstBar ?? 1
        let body = Self.json(request) ?? "{}"
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, call, body) }
            guard let read = facadeObject(answer), read["error"] == nil else {
                problem = facadeObject(answer)?["message"] as? String
                return
            }
            if noteEditor != nil {
                noteEditor?.canUndo = read["canUndo"] as? Bool ?? false
                noteEditor?.changed = read["changed"] as? Bool ?? false
                noteEditor?.canReset = ((read["count"] as? NSNumber)?.intValue ?? 0) > 0
                if let added = read["added"] as? String { noteEditor?.picked = added }
            }
            if corrections != nil, let list = NoteCorrections(answer: answer), list.bar == corrections?.bar { corrections = list }
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
        _ = try? await webView.callAsyncJavaScript("return motiff.checks(data)", arguments: ["data": ["flags": flags]], contentWorld: .page)
    }

    // MARK: - Other versions

    /// Shows another version: its bars compared with these notes, and markers above the bars it has otherwise.
    func showVersion(_ id: String) {
        guard let piece else { return }
        versionStatus = nil
        Task { await readVersion(piece.id, id) }
    }

    /// A MIDI or MusicXML file of the piece added as another version: music-core lines it up with these notes, keeps it
    /// with the piece and raises the Check flags it gives a scanned piece (`performance.add`, 0022).
    func addVersion(fileName: String, data: Data) async {
        guard let piece else { return }
        let name = (fileName as NSString).deletingPathExtension
        let request = Self.json(["name": name.isEmpty ? text["performance.defaultName"] : name, "file": data.base64EncodedString(), "fileName": fileName]) ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.add", request) }
        guard versions.read(performance: answer) else {
            versionStatus = facadeObject(answer)?["message"] as? String
            return
        }
        versionStatus = versions.matched == 0
            ? text["performance.noMatch"]
            : text.format("performance.added", String(versions.matched), String(versions.printed), String(versions.bars.filter(\.differs).count))
        await versionAdded(piece)
    }

    /// A version just added: Sources shows its page (testing item 32), and the list, the markers and Check follow.
    private func versionAdded(_ piece: OpenPiece) async {
        versionPage = versions.shownId
        versions.read(list: await MusicCore.run { MusicCore.call(piece: piece.id, "performances") })
        await drawVersions()
        await loadCheck(piece)
    }

    /// A recording heard by the scan server in parts, as another version (0022): music-core joins the parts' notes and
    /// lines them up with the score. Returns why it could not be added, or nil.
    func addRecordingVersion(_ heard: HeardRecording) async -> String? {
        guard let piece else { return nil }
        var fields = heard.request
        fields["name"] = heard.name.isEmpty ? text["performance.defaultName"] : heard.name
        let request = Self.json(fields) ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.add", request) }
        guard versions.read(performance: answer) else {
            return facadeObject(answer)?["message"] as? String ?? text["transcribe.failed"]
        }
        let status = versions.matched == 0
            ? text["performance.noMatch"]
            : text.format("performance.added", String(versions.matched), String(versions.printed), String(versions.bars.filter(\.differs).count))
        versionStatus = status
        await versionAdded(piece)
        return nil
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

    /// A PDF kept inside the piece (`sources.document`): its pages shared over the bars until its notes are read; it stays
    /// on the iPad. A PDF kept before is replaced.
    func keepDocument(_ data: Data) async {
        guard let piece else { return }
        var fields: [String: Any] = ["pdf": data.base64EncodedString()]
        if let pages = PDFDocument(data: data)?.pageCount, pages > 0 { fields["pages"] = pages }
        let request = Self.json(fields) ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "sources.document", request) }
        guard let read = SourcesInfo(answer: answer) else {
            versionStatus = facadeObject(answer)?["message"] as? String
            return
        }
        sources = read
        versionStatus = text["sources.pdfKept"]
        if let file = piece.file { try? await ProjectFiles.save(piece: piece.id, to: file) }
    }

    /// Read as a version: the PDF kept in the piece goes to the scan server, and the notes it reads are added as another
    /// version from a score, its pages linked to these bars (`sources.documentNotes`). Answers why not, or nil once added.
    func readDocumentVersion(address: URL, account: Account) async -> String? {
        guard let piece else { return nil }
        let kept = await MusicCore.run { MusicCore.call(piece: piece.id, "sources.pdf") }
        guard let object = facadeObject(kept), let base64 = object["pdf"] as? String, let data = Data(base64Encoded: base64) else {
            return facadeObject(kept)?["message"] as? String ?? text["pdf.readNone"]
        }
        let fileName = object["fileName"] as? String ?? "score.pdf"
        let result: ScanResult
        switch await ScanServer.recognise(fileName: fileName, data: data, address: address, account: account) {
        case .failure(let problem): return problem.text(text)
        case .success(let read): result = read
        }
        let pages = max(1, sources.document?.pages ?? result.pages)
        let request = Self.json(["musicXml": result.musicXml.base64EncodedString(), "engine": result.engine, "pages": pages]) ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "sources.documentNotes", request) }
        guard versions.read(performance: answer) else {
            return facadeObject(answer)?["message"] as? String ?? text["pdf.readNone"]
        }
        versionStatus = versions.matched == 0
            ? text["performance.noMatch"]
            : text.format("performance.added", String(versions.matched), String(versions.printed), String(versions.bars.filter(\.differs).count))
        await versionAdded(piece)
        return nil
    }

    /// Show on a bar of the version: the bar is selected and comes into view.
    func showVersionBar(_ bar: Int) {
        guard let span = versions.bars.first(where: { $0.bar == bar })?.span else { return }
        Task {
            await select(span.lowerBound, span.upperBound)
            _ = try? await webView.callAsyncJavaScript("return motiff.reveal(data)", arguments: ["data": ["start": span.lowerBound]], contentWorld: .page)
        }
    }

    func renameVersion(_ name: String) {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed != versions.shown?.name else { return }
        changeVersion("performance.rename", ["name": trimmed], status: nil)
    }

    /// Removes the version shown with the bars taken from it and its Check flags; the toast's Undo puts it back while the
    /// piece is open (`performance.putBack`).
    func removeVersion() {
        guard let piece, let shown = versions.shown else { return }
        let request = Self.json(["id": shown.id]) ?? "{}"
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.remove", request) }
            guard let object = facadeObject(answer), object["error"] == nil else {
                versionStatus = facadeObject(answer)?["message"] as? String
                return
            }
            versions.read(list: answer)
            versionPage = nil
            if merge?.id == shown.id { endDraft() }
            versionStatus = nil
            mergeToast = MergeToast(text: text.format("merge.removed", shown.name), undo: .putBack)
            if let next = versions.entries.first {
                await readVersion(piece.id, next.id)
            } else {
                await drawVersions()
            }
            if shown.takenBars > 0 { await notesDidChange() }
            await loadCheck(piece)
        }
    }

    private func changeVersion(_ call: String, _ fields: [String: Any], status: String?, notes: Bool = false) {
        guard let piece, let id = versions.shownId else { return }
        var request = fields
        request["id"] = id
        let body = Self.json(request) ?? "{}"
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, call, body) }
            guard versions.read(performance: answer) else {
                versionStatus = facadeObject(answer)?["message"] as? String
                return
            }
            versionStatus = status
            if notes {
                await notesDidChange()
            } else {
                await drawVersions()
            }
        }
    }

    /// The versions kept with the piece, and the one shown compared bar by bar.
    private func loadVersions(_ piece: OpenPiece) async {
        versions.read(list: await MusicCore.run { MusicCore.call(piece: piece.id, "performances") })
        if let id = versions.shownId ?? versions.entries.first?.id {
            await readVersion(piece.id, id)
        } else {
            await drawVersions()
        }
    }

    private func readVersion(_ piece: Int32, _ id: String) async {
        let request = Self.json(["id": id]) ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "performance", request) }
        if !versions.read(performance: answer) { versionStatus = facadeObject(answer)?["message"] as? String }
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
            problem = failure["message"] as? String
            return
        }
        notesChanged?()
        await engraveScore()
        if let file = piece.file { try? await ProjectFiles.save(piece: piece.id, to: file) }
    }

    /// The notes changed (bars taken or restored): the player and the roll take the new notes, and the score is engraved
    /// again and drawn with its marks.
    private func notesDidChange() async {
        versionPlayback = nil
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
            engravedView = view
            engravedEven = even
            mergeAdded = engraving.version
            mergeOwn = engraving.own
            await render()
            if focused { await engraveCard() }
        case .failure(let error):
            problem = error.message
        }
    }

    /// The draft's view the score should show: the version or the result while a draft is open out of focus.
    private var wantedView: String? {
        merge != nil && !focused && mergeShow != "mine" ? mergeShow : nil
    }

    /// The page engraved again when the view it should show changed.
    private func applyMergeView() async {
        // Even bars turned on or off engraves the score again too.
        guard wantedView != engravedView || engravedEven != DeviceSettings.shared.isOn("score.evenBars") else { return }
        await engraveScore()
    }

    /// With extra information on, the bars the version has otherwise as plum labels above them and the printed notes it
    /// does not have tinted, and a thin plum line under the bars taken from it; on a draft's view, its notes coloured.
    private func drawVersions() async {
        await readSources()
        guard shown else { return }
        let name = versions.shown?.name ?? ""
        let differing = extraShown("performance") ? versions.differing : []
        let markers: [[String: Any]] = differing.compactMap { bar in
            guard let span = bar.span else { return nil }
            return ["bar": bar.bar, "tick": span.lowerBound, "text": bar.marker, "label": text.format("performance.markerLabel", String(bar.bar), name, bar.marker)]
        }
        let viewed = engravedView != nil
        let data: [String: Any] = [
            "markers": markers, "unheard": viewed ? [] : differing.flatMap(\.notHeard),
            "added": viewed ? mergeAdded : [], "artefacts": [String](), "own": viewed ? mergeOwn : [],
            "taken": extraShown("taken") && !viewed ? versions.taken.compactMap { $0.span?.lowerBound } : [],
        ]
        _ = try? await webView.callAsyncJavaScript("return motiff.performance(data)", arguments: ["data": data], contentWorld: .page)
        await updateFocusVersion()
        await applyMergeView()
    }

    /// The version for the phrase in focus: its notes to hear, and the bars of the phrase it has otherwise.
    private func updateFocusVersion() async {
        guard focused, let piece, let selection, let shown = versions.shown else {
            focusVersion = nil
            return
        }
        guard let notes = await versionNotes(piece.id, shown.id), focused, self.selection == selection, versions.shownId == shown.id else { return }
        let bars = selection.firstBar...selection.lastBar
        focusVersion = FocusVersion(
            id: shown.id, name: shown.name, notes: notes,
            sounds: notes.contains { $0.onset >= selection.onset && $0.onset < selection.end },
            differing: versions.differing.map(\.bar).filter { bars.contains($0) }
        )
    }

    /// The version's notes in the score's time, as music-core gives them to play (`playback` of `version`), kept until
    /// the notes change.
    private func versionNotes(_ piece: Int32, _ id: String) async -> [PlaybackNote]? {
        if versionPlayback?.id != id {
            let request = Self.json(["of": "version", "id": id]) ?? "{}"
            let answer = await MusicCore.run { MusicCore.call(piece: piece, "playback", request) }
            versionPlayback = (id, PlaybackScore.read(answer)?.notes ?? [])
        }
        return versionPlayback?.id == id ? versionPlayback?.notes : nil
    }

    // MARK: - Merging a version (testing items 32–34)

    /// A version's page in Sources: its overview, the draft and what was merged.
    func openVersionPage(_ id: String) {
        versionPage = id
        if versions.shownId != id { showVersion(id) }
    }

    func closeVersionPage() {
        versionPage = nil
    }

    /// Keep my notes, Take the sure ones or Take the version wherever it differs: the draft opens with those choices,
    /// or is applied at once when the player asked for that (`merge.direct`); `saved` opens the choices last applied.
    func startMerge(_ start: String, direct: Bool = true) {
        guard let piece, let id = versions.shownId else { return }
        Task {
            guard await mergeCall(piece.id, "performance.merge.open", ["id": id, "start": start]) else { return }
            if direct && DeviceSettings.shared["merge.direct"] == "on" {
                await applyDraft(piece)
                return
            }
            mergeShow = "result"
            mergeAt = merge?.bars.first?.bar
            if let mergeAt { showVersionBar(mergeAt) }
            await mergeChanged()
        }
    }

    /// Open in the draft, from a marker's popover or from focus: the version's page with the draft on the bar, the
    /// choices as last applied when no draft of it is open.
    func openDraft(at bar: Int) {
        guard let piece, let id = versions.shownId else { return }
        markerPick = nil
        versionPage = id
        versionPicked?(bar)
        Task {
            if merge?.id != id {
                guard await mergeCall(piece.id, "performance.merge.open", ["id": id, "start": "saved"]) else { return }
                mergeShow = "result"
            }
            goToMergeBar(bar)
            await mergeChanged()
        }
    }

    /// Open in the draft from focus: focus is left, and the draft opens on the first bar of the phrase that differs.
    func openDraftFromFocus() {
        guard focusVersion != nil, let bar = focusVersion?.differing.first ?? selection?.firstBar else { return }
        leaveFocus()
        openDraft(at: bar)
    }

    /// Take mine, Take from the version or Not a difference, for the change shown or the bar given; with Phrase, for every
    /// change of its phrase.
    func chooseMerge(_ choice: String, bar: Int? = nil) {
        guard let piece, let at = bar ?? mergeAt else { return }
        changeDraft(piece, "performance.merge.choose", ["bars": draftBars(at), "choice": choice])
    }

    /// Take the group: every change of the kind at once, mine or the version.
    func chooseMergeGroup(_ group: String, _ choice: String) {
        guard let piece, let merge else { return }
        let bars = merge.bars.filter { $0.group == group }.map(\.bar)
        guard !bars.isEmpty else { return }
        changeDraft(piece, "performance.merge.choose", ["bars": bars, "choice": choice])
    }

    /// Reset the bar: undecided again.
    func undecideMerge(_ bar: Int) {
        guard let piece else { return }
        changeDraft(piece, "performance.merge.undecide", ["bars": [bar]])
    }

    func undoDraft() {
        guard let piece else { return }
        changeDraft(piece, "performance.merge.undo")
    }

    func redoDraft() {
        guard let piece else { return }
        changeDraft(piece, "performance.merge.redo")
    }

    /// Reset the draft: back to how it opened, which Undo can itself take back.
    func resetDraft() {
        guard let piece else { return }
        changeDraft(piece, "performance.merge.reset")
    }

    /// Cancel: the draft goes and nothing has changed.
    func cancelDraft() {
        guard let piece else { return }
        Task {
            _ = await mergeCall(piece.id, "performance.merge.cancel")
            endDraft()
            await mergeChanged()
        }
    }

    func applyMerge() {
        guard let piece else { return }
        Task { await applyDraft(piece) }
    }

    /// Undo the merge: the player's notes back in every bar taken; the choices stay to open again.
    func undoAppliedMerge() {
        guard let piece, let id = versions.shownId else { return }
        Task { await unapply(piece, id, toast: text["merge.undone"], undo: .reapply) }
    }

    /// The toast's Undo: the step it names taken back.
    func undoToast() {
        guard let piece, let toast = mergeToast, let undo = toast.undo else { return }
        mergeToast = nil
        Task {
            switch undo {
            case .unapply:
                guard let id = versions.shownId else { return }
                await unapply(piece, id, toast: text["merge.applyUndone"], undo: nil)
            case .reapply:
                guard let id = versions.shownId, await mergeCall(piece.id, "performance.merge.open", ["id": id, "start": "saved"]) else { return }
                await applyDraft(piece, toast: text["merge.redone"])
            case .putBack:
                let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.putBack") }
                guard versions.read(performance: answer) else {
                    versionStatus = facadeObject(answer)?["message"] as? String
                    return
                }
                versions.read(list: await MusicCore.run { MusicCore.call(piece: piece.id, "performances") })
                mergeToast = MergeToast(text: text.format("merge.putBack", versions.shown?.name ?? ""), undo: nil)
                await notesDidChange()
                await loadCheck(piece)
            }
        }
    }

    func closeToast() {
        mergeToast = nil
    }

    /// ‹ › through the changes.
    func stepMerge(_ by: Int) {
        guard let merge, !merge.bars.isEmpty else { return }
        let bars = merge.bars.map(\.bar)
        let index = mergeAt.flatMap { bars.firstIndex(of: $0) } ?? (by > 0 ? -1 : bars.count)
        goToMergeBar(bars[min(max(index + by, 0), bars.count - 1)])
    }

    /// A change chosen in the list, the strip or the window: the draft and the score go to its bar.
    func goToMergeBar(_ bar: Int) {
        mergeAt = bar
        showVersionBar(bar)
        if let mergeHearing { hearMerge(mergeHearing) }
    }

    /// What the score shows while the draft is open: the player's notes, the version or the result.
    func showMergeView(_ view: String) {
        mergeShow = view
        Task {
            await applyMergeView()
            await drawVersions()
        }
    }

    /// Hear Mine, Version or Result over the change shown (its phrase with Phrase), or the player's notes and the
    /// version in turn.
    func hearMerge(_ view: String) {
        guard let piece, merge != nil, let span = hearSpan else { return }
        mergeHearing = view
        Task {
            let request = Self.json(["view": view == "alternate" ? "version" : view]) ?? "{}"
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.merge.playback", request) }
            guard let notes = PlaybackScore.read(answer)?.notes else { return }
            if view == "alternate" { hearInTurn?(notes, span) } else { hearNotes?(notes, span) }
        }
    }

    /// The span heard: the change's bar, or its phrase with Phrase.
    private var hearSpan: Range<Double>? {
        guard let at = mergeAt else { return nil }
        let bars = draftBars(at)
        let spans = versions.bars.filter { bars.contains($0.bar) || $0.bar == at }.compactMap(\.span)
        guard let first = spans.map(\.lowerBound).min(), let last = spans.map(\.upperBound).max() else { return nil }
        if mergePhrase, let unit = phrases?.units.first(where: { $0.firstBar <= at && at <= $0.lastBar }) { return unit.onset..<unit.end }
        return first..<last
    }

    /// ▶ As in the version, from a marker's popover: the bar as the version plays it.
    func hearMarkerVersion() {
        guard let piece, let pick = markerPick, let id = versions.shownId,
              let span = versions.bars.first(where: { $0.bar == pick.bar })?.span else { return }
        Task {
            guard let notes = await versionNotes(piece.id, id) else { return }
            hearNotes?(notes, span)
        }
    }

    func closeMarker() {
        markerPick = nil
    }

    /// A draft's view engraved for Compare versions over the bars shown (± 1 bar, the phrase or the sentence round the
    /// change), with the ids of the notes to colour.
    func mergeCard(_ view: String, see: String, zoom: Int) async -> MergeCardImage? {
        guard let piece, let at = mergeAt else { return nil }
        let id = piece.id
        let (first, last) = await seenBars(id, at, see)
        let engraved = await MusicCore.run { Engraver.engraveMergeBars(piece: id, view: view, firstBar: first, lastBar: last, zoom: zoom) }
        guard case .success(let engraving) = engraved, let svg = engraving.pages.first else { return nil }
        return MergeCardImage(svg: svg, version: engraving.version, own: engraving.own)
    }

    private func seenBars(_ piece: Int32, _ bar: Int, _ see: String) async -> (Int, Int) {
        if see == "phrase" || see == "sentence" {
            let request = Self.json(["level": see]) ?? "{}"
            let answer = await MusicCore.run { MusicCore.call(piece: piece, "phrases", request) }
            if let unit = PhraseLevelsShown(answer: answer)?.units.first(where: { $0.firstBar <= bar && bar <= $0.lastBar }) {
                return (unit.firstBar, unit.lastBar)
            }
        }
        let numbers = versions.bars.map(\.bar)
        return (max(numbers.min() ?? bar, bar - 1), min(numbers.max() ?? bar, bar + 1))
    }


    private func draftBars(_ bar: Int) -> [Int] {
        guard mergePhrase, let merge, let unit = phrases?.units.first(where: { $0.firstBar <= bar && bar <= $0.lastBar }) else { return [bar] }
        let bars = merge.bars.map(\.bar).filter { unit.firstBar <= $0 && $0 <= unit.lastBar }
        return bars.isEmpty ? [bar] : bars
    }

    private func changeDraft(_ piece: OpenPiece, _ call: String, _ fields: [String: Any] = [:]) {
        Task {
            if await mergeCall(piece.id, call, fields) { await mergeChanged() }
        }
    }

    /// One `performance.merge` call; the draft as it answers. False, with why, for an error.
    @discardableResult private func mergeCall(_ piece: Int32, _ call: String, _ fields: [String: Any] = [:]) async -> Bool {
        let request = Self.json(fields) ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece, call, request) }
        guard let object = facadeObject(answer), object["error"] == nil else {
            versionStatus = facadeObject(answer)?["message"] as? String
            return false
        }
        merge = MergeInfo(object: object)
        if let merge, let mergeAt, !merge.bars.contains(where: { $0.bar == mergeAt }) { self.mergeAt = merge.bars.first?.bar }
        return true
    }

    /// After the draft changed: the score shows its view again, and what is heard follows.
    private func mergeChanged() async {
        if wantedView != nil {
            await engraveScore()
        } else {
            await applyMergeView()
        }
        await drawVersions()
        if merge == nil { mergeHearing = nil }
    }

    private func endDraft() {
        merge = nil
        mergeAt = nil
        mergeHearing = nil
        comparing = false
        mergeShow = "result"
    }

    /// Apply: the draft's bars come into the score, its choices are kept with the piece, and the toast can undo it.
    private func applyDraft(_ piece: OpenPiece, toast: String? = nil) async {
        let at = ISO8601DateFormatter().string(from: Date())
        let request = Self.json(["at": at]) ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.merge.apply", request) }
        guard versions.read(performance: answer) else {
            versionStatus = facadeObject(answer)?["message"] as? String
            return
        }
        endDraft()
        versionStatus = nil
        mergeToast = MergeToast(text: toast ?? text.count("merge.applied", versions.shown?.takenBars ?? 0), undo: toast == nil ? .unapply : nil)
        await notesDidChange()
    }

    private func unapply(_ piece: OpenPiece, _ id: String, toast: String, undo: MergeToast.Undo?) async {
        let request = Self.json(["id": id]) ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.merge.undoApplied", request) }
        guard versions.read(performance: answer) else {
            versionStatus = facadeObject(answer)?["message"] as? String
            return
        }
        mergeToast = MergeToast(text: toast, undo: undo)
        await notesDidChange()
    }

    // MARK: - Phrase focus

    /// Lifts the selection into focus (Expand, #521 19): the page dims, the card shows the selection's bars on their own, and
    /// the page neither scrolls nor takes the Pencil until the way back, which `entry` names (#521 17).
    func enterFocus(from entry: ExpandEntry = .notes) {
        guard shown, ExpandRules.opens(selected: selection != nil, penOn: penOn), !focused else { return }
        expandEntry = entry
        focused = true
        setPageStill(true)
        let request: [String: Any] = ["open": true, "label": text.format("focus.cardLabel", focusPosition), "back": text[entry.backKey]]
        Task {
            let answer = try? await webView.callAsyncJavaScript("return motiff.focus(data)", arguments: ["data": request], contentWorld: .page) as? [String: Any]
            if let options = answer?["options"] as? [String: Any] { cardOptions = Self.json(options) }
            await updatePlayRange()
            await engraveCard()
            await updateFocusVersion()
        }
    }

    /// Whether the card in focus shows the phrase as a roll (testing item 12): Notes | Piano roll in the focus bar, kept while
    /// the piece is open.
    @Published var focusRoll = false

    /// Expand on the roll drawer: the selection in focus as a roll, its way back Collapse (#521 17). Only with a selection
    /// (#521 19.1): without one the button says so instead (showNotice).
    func expandRoll() {
        guard selection != nil else { return }
        focusRoll = true
        enterFocus(from: .roll)
    }

    /// Shows a word of guidance over the pill for a moment, as the hint to hold.
    func showNotice(_ key: String) {
        let shown = Notice(key: key)
        notice = shown
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(Self.holdHintSeconds))
            if self?.notice == shown { self?.notice = nil }
        }
    }

    /// Back to the page, with the selection as it is: Back, a tap on the dimmed page, or Escape.
    func leaveFocus() {
        guard focused else { return }
        focused = false
        focusVersion = nil
        setPageStill(false)
        Task {
            _ = try? await webView.callAsyncJavaScript("return motiff.focus(data)", arguments: ["data": ["open": false]], contentWorld: .page)
            await updatePlayRange()
        }
    }

    /// The unit of the level shown that the card is, or that holds its start.
    var focusUnit: PhraseUnit? {
        guard let selection, let units = phrases?.units else { return nil }
        return units.first { $0.selectableOnset == selection.onset && $0.selectableEnd == selection.end }
            ?? units.first { $0.onset <= selection.onset && selection.onset < $0.end }
    }

    var canStepBack: Bool { (focusUnit?.number ?? 1) > 1 }

    var canStepOn: Bool { focusUnit.map { $0.number < (phrases?.units.count ?? 0) } ?? false }

    /// Previous or next at the level shown: the card moves on to that unit. Outside focus (the pill's arrows, #328) the
    /// page brings it into view.
    func step(_ direction: Int) {
        guard let unit = focusUnit, let next = phrases?.unit(unit.number + direction.signum()) else { return }
        Task {
            await select(next.selectableOnset, next.selectableEnd)
            if !focused { await reveal(next.selectableOnset) }
        }
    }

    /// Scrolls the page smoothly to where a tick is, without a jump of page.
    private func reveal(_ tick: Double) async {
        _ = try? await webView.callAsyncJavaScript("return motiff.reveal(data)", arguments: ["data": ["start": tick]], contentWorld: .page)
    }

    // MARK: - Resume and beats (Settings · Experimental)

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
        _ = try? await webView.callAsyncJavaScript("return motiff.beats(data)", arguments: ["data": ["lines": beatLines]], contentWorld: .page)
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
        guard let selection else { return }
        await reveal(selection.onset)
        if place.focused { enterFocus() }
    }

    /// The tick at the top of the pages in view: where the player was when nothing is selected.
    func tickInView() async -> Double? {
        await firstTick(onPage: pages.first)
    }

    /// "bars 9–16 · phrase 3 of 16", or the bars alone for a passage that is not a unit.
    var focusPosition: String {
        guard let selection else { return "" }
        let bars = selection.firstBar == selection.lastBar
            ? text.format("bars.one", String(selection.firstBar))
            : text.format("bars.range", String(selection.firstBar), String(selection.lastBar))
        guard let units = phrases?.units,
              let unit = units.first(where: { $0.selectableOnset == selection.onset && $0.selectableEnd == selection.end })
        else { return bars }
        return text.format("focus.position", bars, unit.name, String(unit.number), String(units.count))
    }

    /// Engraves the selection's bars at the card's width and shows them on the card.
    private func engraveCard() async {
        guard focused, let piece, let selection, let options = cardOptions else { return }
        let first = selection.firstBar
        let last = selection.lastBar
        let id = piece.id
        let name = ossia
        let withOssia = name != nil
        // Without a variation, another version of the piece stands above as the performer has it (0022, section 5).
        let version = withOssia ? nil : versions.shown
        let engraved = await MusicCore.run {
            Engraver.engraveCard(piece: id, firstBar: first, lastBar: last, options: options, ossia: withOssia, version: version?.id)
        }
        // Stepped on, left, or the variation or the version changed while it engraved: the newer call shows its own.
        guard focused, self.selection == selection, ossia == name, withOssia || versions.shown == version else { return }
        switch engraved {
        case .success(let card):
            let timemap = (try? JSONSerialization.jsonObject(with: Data(card.timemap.utf8))) ?? []
            let label = name.map { text.format("focus.ossia", $0) } ?? version.map { text.format("focus.version", $0.name) } ?? ""
            let ossias = card.ossias.map { ["staves": $0.staves, "prefix": $0.prefix, "label": label, "added": $0.added] as [String: Any] }
            _ = try? await webView.callAsyncJavaScript(
                "return motiff.card(data)", arguments: ["data": ["pages": card.pages, "timemap": timemap, "start": selection.onset, "ossias": ossias]], contentWorld: .page
            )
        case .failure(let error):
            problem = error.message
        }
    }

    /// What play takes for the selection now: its span.
    private func updatePlayRange() async {
        guard let selection else {
            playRange = nil
            return
        }
        playRange = PlayRange(selection: selection, range: selection.onset..<selection.end)
    }

    /// In focus the page under the veil stays where it is: it does not scroll and the Pencil does not write on it.
    private func setPageStill(_ still: Bool) {
        webView.scrollView.isScrollEnabled = !still
        ink.still = still
    }

    private static func json(_ object: [String: Any]) -> String? {
        guard let data = try? JSONSerialization.data(withJSONObject: object) else { return nil }
        return String(decoding: data, as: UTF8.self)
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
        _ = try? await webView.callAsyncJavaScript("return motiff.highlight(data)", arguments: ["data": data], contentWorld: .page)
    }

    /// Where the player is (0018 section 7): the notes sounding at the tick turn cobalt on the page, or on the card in focus,
    /// and while playing the page keeps the system with them below the bar. `follow` (a new Play) follows again at once
    /// after the player scrolled by hand. Called with each position the player reports, about thirty times a second.
    func playhead(_ tick: Double, playing: Bool, follow: Bool = false) {
        guard shown else { return }
        // The page follows the playing unless the player turned that off ("playback.follow", testing item 4).
        let data: [String: Any] = ["tick": tick, "playing": playing, "follow": follow, "auto": DeviceSettings.shared.isOn("playback.follow")]
        webView.callAsyncJavaScript("return motiff.playing(data)", arguments: ["data": data], in: nil, in: .page, completionHandler: nil)
    }

    /// Back to playback: the page scrolls to the playhead and follows again.
    func returnToPlayhead() {
        guard shown else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.returnToPlayhead()", arguments: [:], contentWorld: .page) }
    }

    /// Room under the last page for the drawer over the bottom of the score, so the end of the piece can still be read.
    func insetBottom(_ height: CGFloat) {
        guard shown else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.inset(data)", arguments: ["data": ["bottom": Double(height)]], contentWorld: .page) }
    }

    /// The size of the selection menu, for the page to place it over the band.
    func menuSize(_ size: CGSize) {
        guard shown, size.width > 0 else { return }
        Task {
            _ = try? await webView.callAsyncJavaScript(
                "return motiff.menuSize(data)", arguments: ["data": ["width": size.width, "height": size.height]], contentWorld: .page
            )
        }
    }

    private func loadPhrases(_ piece: OpenPiece) async {
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "phrases") }
        phrases = PhraseLevelsShown(answer: answer)
        await showSelection()
    }

    /// Draws the band over the selection. The phrases are not drawn as brackets (Andrei, 2026-11-13, #479 item 14).
    private func showSelection() async {
        guard shown else { return }
        var band: Any = NSNull()
        if let selection {
            band = ["start": selection.onset, "end": selection.end, "startLabel": text["selection.handleStart"], "endLabel": text["selection.handleEnd"]] as [String: Any]
        }
        _ = try? await webView.callAsyncJavaScript("return motiff.select(data)", arguments: ["data": band], contentWorld: .page)
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
        if piece != nil { Task { await render() } }
    }

    private func render() async {
        guard let piece else { return }
        let timemap = (try? JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))) ?? []
        do {
            let laid = try await webView.callAsyncJavaScript(
                "return motiff.show(data)",
                arguments: ["data": ["pages": piece.pages, "timemap": timemap, "dark": dark]],
                contentWorld: .page
            ) as? [String: Any]
            if let space = (laid?["space"] as? NSNumber)?.doubleValue, space > 0 {
                staffSpace = space
            }
            shown = true
            if let tick = piece.landing?.tick {
                // From the PDF's page in PDF | Notes (0023): the page of the notes where it starts.
                _ = try? await webView.callAsyncJavaScript("return motiff.showTick(data)", arguments: ["data": ["tick": tick, "instant": true]], contentWorld: .page)
            }
            if Landing.notesShowOnRender(piece.landing) { land() }
            _ = try? await webView.callAsyncJavaScript("return motiff.pen(on)", arguments: ["on": penOn], contentWorld: .page)
            applyInput()
            await sendModeNow()
            await sendPlayheadsNow()
            let marks = await MusicCore.run { MusicCore.call(piece: piece.id, "marks") }
            await draw(marks)
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
        } catch {
            problem = error.localizedDescription
            land()
        }
    }

    /// Shows the pages held out of sight while they landed (#533 6), now that they stand where they open.
    func land() {
        webView.alpha = 1
    }

    /// Draws the marks music-core answered (`marks`, `ink.add`, `ink.remove`).
    private func draw(_ answer: String) async {
        guard let object = facadeObject(answer) else { return }
        if object["error"] != nil {
            problem = object["message"] as? String
            return
        }
        let ink = object["ink"] as? [Any] ?? []
        markCount = ink.count
        let shelf = MarksShelf(object: object)
        marks = shelf
        _ = try? await webView.callAsyncJavaScript("return motiff.draw(data)", arguments: ["data": ["marks": ink]], contentWorld: .page)
        let notes: [[String: Any]] = shelf.notes.map { note in
            let label = text.format("margin.markerLabel", String(shelf.number(of: note)), barsText(note.firstBar, note.lastBar, text), MarksShelf.firstWords(note.text))
            return ["id": note.id, "tick": note.onset, "number": shelf.number(of: note), "label": label]
        }
        let highlights: [[String: Any]] = shelf.highlights.map { ["id": $0.id, "start": $0.onset, "end": $0.end, "ink": $0.colour] }
        _ = try? await webView.callAsyncJavaScript("return motiff.notes(data)", arguments: ["data": ["notes": notes, "highlights": highlights]], contentWorld: .page)
    }

    /// A change to the marks through music-core; the answer is drawn unless `draws` is false, for the first of two calls
    /// that make one change, so the page never shows it half done.
    private func change(_ piece: Int32, _ call: String, _ request: [String: Any], draws: Bool = true) async {
        guard let data = try? JSONSerialization.data(withJSONObject: request) else { return }
        let text = String(decoding: data, as: UTF8.self)
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
        _ = try? await webView.callAsyncJavaScript("return motiff.rub(data)", arguments: ["data": ["points": points]], contentWorld: .page)
    }

    /// The eraser lifted: what the press did is saved as one step (`ink.add` for the pieces left, `ink.remove` for the
    /// marks it cut, drawn once both are done), and Undo puts it back whole.
    private func endRub(_ piece: Int32?) async {
        guard let cut = try? await webView.callAsyncJavaScript("return motiff.rubEnd()", contentWorld: .page) as? [String: Any],
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
        guard let mark = try? await webView.callAsyncJavaScript("return motiff.anchor(data)", arguments: ["data": request], contentWorld: .page) as? [String: Any],
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
        guard shown else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.input(data)", arguments: ["data": rules.page], contentWorld: .page) }
    }

    /// Where the Pencil hovers over the window, for the stylus menu's place over the roll and the rest of the screen (#521 7).
    func watchStylus(in window: UIWindow?) {
        ink.pencil.watch(window)
    }

    /// A stroke of the pen began or ended (#521 10): while it is written playback does not move the page under it, and it
    /// follows again as long after as after a scroll by hand.
    private func penDown(_ down: Bool) {
        guard shown else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.writing(data)", arguments: ["data": ["on": down]], contentWorld: .page) }
    }

    /// Handles, given in the document's pixels, where they are on the screen now, in the web view's coordinates.
    private func inView(_ spots: [CGPoint]) -> [CGPoint] {
        let zoom = webView.scrollView.zoomScale
        return spots.map { webView.scrollView.convert(CGPoint(x: $0.x * zoom, y: $0.y * zoom), to: webView) }
    }

    /// The mode as the page has it: "pen" while the Pen is on, none before the modes.
    private func sendMode() {
        guard shown else { return }
        Task { await sendModeNow() }
    }

    private func sendModeNow() async {
        var data: [String: Any] = [:]
        if let mode {
            data["name"] = penOn ? InteractionMode.pen.rawValue : mode.rawValue
        }
        _ = try? await webView.callAsyncJavaScript("return motiff.mode(data)", arguments: ["data": data], contentWorld: .page)
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
        _ = try? await webView.callAsyncJavaScript("return motiff.playheads(data)", arguments: ["data": value], contentWorld: .page)
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

// MARK: - The selection's gestures and its Undo and Redo (#511 sections 2 and 11)

extension ScoreController {
    /// What a toast after a clear or a replace says, and which one it is.
    struct SelectionToast: Equatable {
        var replaced: Bool
        var id = UUID()
    }

    /// How long the toast «Вернуть» stays (Andrei, 2026-11-14), and the hint to hold, in seconds.
    static let selectionToastSeconds = 6.0
    static let holdHintSeconds = 2.0

    /// The page's gesture messages: a hold outside took the selection away, a gesture was cancelled, or a pointer that
    /// cannot scroll moved off the selection before the hold.
    func receivedGesture(_ message: [String: Any]) {
        if let cleared = message["cleared"] as? [String: Any] {
            clearSelection(gesture: cleared["gesture"] as? String)
        }
        if let gesture = message["gestureCancelled"] as? String {
            revertGesture(gesture)
        }
        if message["holdHint"] != nil {
            showHoldHint()
        }
    }

    /// A change of the selection the player made: a step of the history, or a part of its gesture's step. A clear or a
    /// replace shows «Вернуть»; any other step puts it away, as it would no longer take back the clear.
    func noteSelectionChange(from before: ScoreSelection?, to after: ScoreSelection?, gesture: String?) {
        guard let historyKey else { return }
        let step = selectionSteps.record(from: SelectionSpan(before), to: SelectionSpan(after), gesture: gesture)
        SelectionHistories.keep(selectionSteps, of: historyKey)
        switch step?.kind {
        case .clear?, .replace?:
            showSelectionToast(replaced: step?.kind == .replace)
        case nil where gesture == nil:
            break
        default:
            selectionToast = nil
        }
    }

    /// Undo of the selection (the Selection row, the Pencil's ring, ⌘Z, «Вернуть»): the selection before the last step,
    /// selected again exactly; the score neither scrolls nor zooms.
    func undoSelection() {
        guard let step = selectionSteps.undo() else { return }
        keepSteps()
        Task { await restore(step.before) }
    }

    /// Redo of the selection (the Selection row, ⇧⌘Z).
    func redoSelection() {
        guard let step = selectionSteps.redo() else { return }
        keepSteps()
        Task { await restore(step.after) }
    }

    /// A gesture cancelled after its hold took the selection away: the selection comes back and no step stays.
    func revertGesture(_ gesture: String) {
        guard let step = selectionSteps.revert(gesture) else { return }
        keepSteps()
        Task { await restore(step.before) }
    }

    func closeSelectionToast() {
        selectionToast = nil
    }

    private func keepSteps() {
        selectionToast = nil
        if let historyKey { SelectionHistories.keep(selectionSteps, of: historyKey) }
    }

    private func restore(_ span: SelectionSpan?) async {
        guard let span else {
            clearSelection(step: false)
            return
        }
        await select(request: Self.beatsRequest(span.onset, span.end), step: false)
    }

    private func showSelectionToast(replaced: Bool) {
        let toast = SelectionToast(replaced: replaced)
        selectionToast = toast
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(Self.selectionToastSeconds))
            if self?.selectionToast == toast { self?.selectionToast = nil }
        }
    }

    private func showHoldHint() {
        let shown = UUID()
        holdHint = shown
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(Self.holdHintSeconds))
            if self?.holdHint == shown { self?.holdHint = nil }
        }
    }
}
