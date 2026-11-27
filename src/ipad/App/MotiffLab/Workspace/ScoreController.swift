import Combine
import SwiftUI
import WebKit

/// The score and the pencil marks on it (decision record 0019, sections 1.4 and 2.2). The pages are Verovio's SVG in one
/// web view, which runs the web app's own score modules (Web/score.html over Scripts/score) to know where each bar is
/// and to draw the marks. The pen writes through InkInput, which draws the stroke as it is written with the outline and
/// grain the page keeps it with (#479 item 7): when a stroke ends, its points go to the page to be kept against the bar
/// they start in, music-core saves the stroke in the project file (`ink.add`, `marks.ink` in project file version 12),
/// and the marks are drawn again from what music-core answers, so a mark lives only in the project file and shows in
/// the same place in the web app. Who writes, selects and scrolls with a finger or the Pencil is InputRules' (#479
/// item 18): the stylus follows the tool, a finger moves around the music. The composition root over the models (ADR
/// 0031): this file holds its state, the page's events and the render; its parts by concern are extensions beside it
/// (ScoreSelecting, ScoreMarks, ScoreFocus, ScoreInput, ScorePageLayout, ScorePlayheads, ScoreVersions and the others the
/// code map `workspace-controller.md` lists).
@MainActor
final class ScoreController: NSObject, ObservableObject, PenScore, CheckScore, SourcesScore, FocusScore, MarksScore {
    enum Tool: Equatable {
        case pen(Ink)
        case highlighter
        /// The colour bars (finding 31): they colour through the page as Select selects, so nothing is written.
        case colour
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
    var playheadsWanted: [String: Any]?
    var playheadsShown = ""
    /// The selection, its history, toast and hold hint, the menu's place and the phrases shown (Selection/SelectionModel.swift).
    let selectionModel: SelectionModel
    /// Expand opened and left, the card engraved with its roll, options and ossia, the version in focus and the play range
    /// (Playback/FocusModel.swift, through `FocusScore`).
    let focusModel: FocusModel
    /// The other versions, the version's words, the marker tapped and the score as last engraved (Sources/SourcesModel.swift).
    let sourcesModel: SourcesModel
    /// The pen, the bar's Pen, the pencil palette, the Pen's Undo and Redo, the eraser and the ink queue (Ink/PenModel.swift).
    let penModel: PenModel
    /// The marks drawn and changed, the note being written, the layers hidden and what the page hides, the piece's own
    /// settings and the bookmarks drawn and gone to (Marks/MarksModel.swift, through `MarksScore`).
    let marksModel: MarksModel
    /// Check of the notes read: the flags, the drawer, the scan's variants, Fix notes, «Мои правки» and the roll's note
    /// editor (Marks/CheckModel.swift).
    let checkModel: CheckModel
    /// A draft's приёмы on its view, their numbers, the one picked and «Скрыть номера» (Sources/DraftTechniques.swift, v1.1).
    let draftModel = DraftTechniquesModel()
    /// A draft's seams on its view, their diamonds and the one picked (Sources/DraftSeams.swift, v1.1 step 7).
    let seamsModel = DraftSeamsModel()
    /// A draft's anchors on its view, their amber marks and the one picked (Sources/DraftAnchors.swift, v1.2).
    let anchorsModel = DraftAnchorsModel()
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
    /// The player's beats with their bar and beat (`setBarBeats`): what a dragged phrase mark's caption writes
    /// (ScorePhraseEdits.swift).
    var barBeats = BarBeats()
    /// A phrase's mark held, whose menu is open under its number (ScorePhraseEdits.swift); nil for none.
    @Published var phraseMenu: PhraseMarkPick?
    /// How high the mark is that a card opens at, as the page last told it with the mark's place (a tap on it, a move):
    /// the card opens above it when there is no room below (`DraftCardAtMark`, v1.4 a-1). Written only in `handle`.
    @Published private(set) var markHeight: CGFloat = 0
    /// The tuplet groups the page read, as blocks the selection's grid never splits (v0.6.4, PlaceSelection.swift): the roll takes them.
    var tupletsRead: (@MainActor ([[String: Double]]) -> Void)?
    /// The zoom and the place a double tap inside the selection zoomed from (PlaceSelection.swift), for the next to go back to.
    var zoomedFrom: (scale: CGFloat, offset: CGPoint)?
    /// The tool the Pencil's «switch to previous» left for Select, which its next one goes back to (StylusRules.selectTurn).
    var toolBeforeSelect: InteractionMode?
    /// A tap on the page that selects nothing: away from the bars, or anywhere while a passage is selected. The Insights
    /// sheet goes away; the selection stays (#338).
    var blankTapped: (@MainActor () -> Void)?
    /// Practice view (#533 11, PracticeView.swift): view state, changed by `practise`, its two visibilities kept on the device
    /// (v0.6.4, spec 9); and the time its controls hide.
    @Published var practice = PracticeView.saved()
    var practiceTimer: Task<Void, Never>?
    /// A Check flag tapped on the score: the screen opens the Check drawer on its bar.
    var checkPicked: (@MainActor (Int) -> Void)?

    /// A version's notes to hear over a span, once (a marker's «Как в версии»): the screen's player plays them on their own.
    var hearNotes: (@MainActor ([PlaybackNote], Range<Double>) -> Void)?
    /// The way to the versions from the score (its marker's popover, by the marker's bar): the screen opens Sources.
    var versionPicked: (@MainActor (Int) -> Void)?
    /// The piece's notes changed (a correction, the piece's tempo set): the player and the roll load them again.
    var notesChanged: (@MainActor () -> Void)?

    @Published private(set) var problem: String?
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
    /// The pen, and who writes and who scrolls (#479 items 7 and 18; ScoreInput.swift).
    let ink = InkInput()
    private(set) var piece: OpenPiece?
    private(set) var dark = false
    private var pageReady = false
    /// Whether the pages are drawn, so marks and calls can go to them.
    private(set) var shown = false
    /// The page loaded again if iPadOS ends the web view's process (WebContentWatch).
    private var contentWatch: WebContentWatch?
    /// The render under way (`scheduleRender`), and whether another is wanted after it.
    private var rendering: Task<Void, Never>?
    private var renderAgain = false
    /// The selection's handles, in the document's pixels, as the page last reported them: the Pencil does not write there.
    private var handleSpots: [CGPoint] = []
    /// The playheads' handles, in the document's pixels, as the page last reported them: a finger there takes the handle.
    private var headSpots: [CGPoint] = []
    let text = UIText.current
    /// Pixels between two staff lines on screen, for the width of the live stroke.
    private(set) var staffSpace: CGFloat = 8
    /// The places ringed for the fact chosen in Insights (ScorePageLayout.swift).
    var ringed: [Fact.Place] = []
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
        penModel = PenModel()
        marksModel = MarksModel(selection: selecting) { DeviceSettings.shared.resolved(overrides: $0) }
        checkModel = CheckModel(selection: selecting) { ScanServer.jobs }
        super.init()
        penModel.score = self
        checkModel.score = self
        sourcesModel.score = self
        focusModel.score = self
        marksModel.score = self
        webView.configuration.userContentController.add(MessageRelay(self), name: "score")
        webView.isOpaque = false
        webView.backgroundColor = .clear

        // The pen's gesture and the stroke being written lie over the web view; the rules say which pointers they take.
        ink.attach(to: webView, pan: webView.scrollView.panGestureRecognizer)
        ink.style = { [weak self] point in self?.liveStyle(at: point) }
        ink.ended = { [weak self] samples in self?.strokeEnded(samples) }
        ink.rubs = { [weak self] in self?.penModel.tool == .eraser }
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
            checkModel.open(edits: piece.edits)
            #if DEBUG
            ScreensProbe.score = self
            #endif
            ProbeCounts.shared.mark("show")
            // The pages wait out of sight for the place they land on (#533 6), so the top of the first one never shows first.
            webView.alpha = Landing.holdsTheNotes(piece.landing) ? 0 : 1
            selectionModel.open(piece)
            rendered = false
            sourcesModel.open(evenBars: DeviceSettings.shared.isOn("score.evenBars"), appWrittenBars: piece.appWrittenBars)
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

    // MARK: - The page

    /// What the page reports (`PageEvent`): the pages in view, where the menu goes, and what the player did on the score.
    fileprivate func received(_ message: [String: Any]) {
        for event in PageEvent.events(in: message) {
            handle(event)
        }
    }

    /// Acts on one thing the page reported; the roll's long press comes here too (its hold hint).
    func handle(_ event: PageEvent) {
        if let height = event.markHeight.map({ CGFloat($0) }), height != markHeight { markHeight = height }
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
        case .bookmark, .noteMarker, .checkFlag, .performanceBar, .techniqueNumber, .seamDiamond, .anchorMark, .phraseMark, .sectionMark, .motifMark:
            markTapped(event)
        case let .phraseHeld(number, left, bottom, _):
            holdPhrase(number, at: CGPoint(x: left, y: bottom))
        case let .phraseMoved(from, to):
            movePhrase(from: from, to: to)
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
            focusModel.leave()
        case .cardLayout(let options):
            focusModel.engraveCard(options: facadeRequest(options))
        case let .coloured(start, end):
            penModel.colour(start, end)
        case .markMoved, .markAway:
            markChanged(event)
        default:
            break
        }
    }

    /// The mark whose card shows moved on the page (r-1), or left the visible part of it (v1.4 a-1, answer 14).
    private func markChanged(_ event: PageEvent) {
        guard case .markMoved(let point, _) = event else {
            // Out of sight (playback turned the page): its card closes, its cover with it, so the next tap reaches the score.
            closeCardAtMark()
            return
        }
        // No card shows at a mark any more (closed by its own cover, which the page never hears): the page stops following
        // it, else it would tell each frame of a scroll (r-1).
        if !moveCardAtMark(to: point) { Task { await scorePage.send(.markForgotten) } }
    }

    /// A mark on the score tapped: a bookmark goes there, a margin note opens, a Check flag shows its bar, a version's or a
    /// draft's marker opens its card, a phrase's number, a section's letter or a motif's comma selects it
    /// (ScorePhraseMarks.swift).
    private func markTapped(_ event: PageEvent) {
        switch event {
        case .bookmark(let id):
            marksModel.goToBookmark(id)
        case .noteMarker(let id):
            marksModel.openNote(id)
        case .checkFlag(let bar):
            checkModel.showCheckBar(bar)
            checkPicked?(bar)
        case .phraseMark(let number):
            pickPhrase(number)
        case .sectionMark(let id):
            pickSection(id)
        case let .motifMark(phrase, motif):
            pickMotif(motif, of: phrase)
        default:
            pickMarker(event)
        }
    }

    /// The open piece's title, for what the assistant knows.
    var pieceTitle: String { piece?.title ?? "" }
    /// The piece open in music-core, as the assistant at the selection asks about it; nil while none is.
    var pieceId: Int32? { piece?.id }

    // MARK: - Check (Marks/CheckModel.swift)

    /// The open bars as amber flags on the score (`CheckModel.flags`), and the PDF's pages no engine read at their place
    /// (`UnreadPage.marked`, v1.5 i-7); again after «Повторить» on such a page.
    func drawChecks() async {
        guard shown else { return }
        await scorePage.send(.checks, ["flags": UnreadPage.marked(checkModel.flags(text), unreadPages, text)])
    }

    /// What went wrong, in the problem line (the root's parts and Check's calls through `CheckScore`).
    func showProblem(_ message: String?) {
        problem = message
    }

    // MARK: - The notes engraved

    /// The piece's tempo the player read in the printed notes, or nil to play it as it states again (version 0.5.4, #511
    /// section 17, `tempo.set`): kept in the project file, heard from the player's next playback score and marked on the
    /// score engraved again.
    func setPieceTempo(_ perMinute: Int?) async {
        guard let piece else { return }
        let request = perMinute.map { "{\"perMinute\": \($0)}" } ?? "{}"
        let answer = await MusicCore.run { PieceCore(piece: piece.id).send(.tempoSet, request) }
        if let failure = facadeObject(answer), failure["error"] != nil {
            problem = FacadeAnswer.message(answer)
            return
        }
        notesChanged?()
        await engraveScore()
        if let file = piece.file { try? await ProjectFiles.save(piece: piece.id, to: file) }
    }

    /// The notes changed: the player and the roll take the new notes, and the score is engraved again and drawn with its
    /// marks.
    func notesDidChange() async {
        focusModel.forgetVersionNotes()
        notesChanged?()
        await engraveScore()
    }

    /// Engraves the score again and draws it with its marks.
    func engraveScore() async {
        guard let piece else { return }
        let id = piece.id
        let title = piece.title
        let even = DeviceSettings.shared.isOn("score.evenBars")
        let engraved = await MusicCore.run { Engraver.engrave(piece: id, title: title, evenBars: even) }
        switch engraved {
        case .success(let engraving):
            // Its other view stays; where it opened does not, so the page stays where the player is.
            self.piece = OpenPiece(id: id, title: title, pages: engraving.pages, timemap: engraving.timemap, file: piece.file, other: piece.other, draft: piece.draft, shownOnly: piece.shownOnly)
            sourcesModel.keepEngraved(SourcesModel.Engraved(even: even, appWrittenBars: engraving.appWrittenBars))
            await render()
            await focusModel.engraveCard()
        case .failure(let error):
            problem = error.message
        }
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

    // MARK: - Resume and beats (ScoreScreenViews.swift, ScoreSelecting.swift)

    /// Whether the page is engraved and everything on it drawn, so a resume point can be shown on it (#327).
    private(set) var rendered = false

    /// The beat lines a selection snaps to (#349): the player's while selecting by beats is on, none otherwise.
    var beatLines: [Double] = [] {
        didSet {
            guard beatLines != oldValue else { return }
            Task { await applyBeats() }
        }
    }

    /// The piece's phrases read, their marks drawn (ScorePhraseMarks.swift; none on a view only shown), and the selection
    /// with the phrase it is.
    private func loadPhrases(_ piece: OpenPiece) async {
        await selectionModel.loadPhrases(of: piece.id)
        await drawPhraseMarks()
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
            await scorePage.send(.pen, penModel.penOn)
            applyInput()
            await sendModeNow()
            await sendPlayheadsNow()
            await sendVisibilities()
            let marks = await MusicCore.run { PieceCore(piece: piece.id).send(.marks) }
            await marksModel.draw(marks)
            ProbeCounts.shared.mark("marks")
            await loadPhrases(piece)
            await marksModel.drawBookmarks()
            await checkModel.load(in: piece.id)
            await marksModel.loadSettings(of: piece.id)
            await marksModel.applyLayers()
            await draftModel.load(piece.draft, on: piece.id)
            await seamsModel.load(piece.draft, on: piece.id)
            anchorsModel.keep(piece.draft)
            await sourcesModel.loadVersions(of: piece.id)
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
