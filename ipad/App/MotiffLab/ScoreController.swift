import PDFKit
import PencilKit
import SwiftUI
import UniformTypeIdentifiers
import WebKit

/// The score and the pencil marks on it (decision record 0019, sections 1.4 and 2.2). The pages are Verovio's SVG in one
/// web view, which runs the web app's own score modules (Web/score.js over Scripts/score) to know where each bar is
/// and to draw the marks. The Apple Pencil writes with PencilKit for its low latency: when a stroke ends, its points
/// go to the page to be kept against the bar they start in, music-core saves the stroke in the project file
/// (`ink.add`, `marks.ink` in project file version 12), and the marks are drawn again from what music-core answers.
/// PencilKit's own drawing is cleared after every stroke, so a mark lives only in the project file and shows the
/// same in the web app. A finger never writes: it scrolls.
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

    @Published var tool = Tool.pen(.graphite) {
        didSet {
            if oldValue != tool { previousTool = oldValue }
            applyTool()
        }
    }
    /// The bar's Pen (0018 section 5): while it is on, a finger writes too and two fingers scroll, so the score takes
    /// marks on any iPad, with or without a Pencil. The Pencil always writes.
    @Published var penOn = false {
        didSet {
            applyInput()
            if shown { Task { _ = try? await webView.callAsyncJavaScript("return motiff.pen(on)", arguments: ["on": penOn], contentWorld: .page) } }
        }
    }
    /// The phrases at the level shown, drawn as brackets on the score (0018 section 3).
    @Published private(set) var phrases: PhraseLevelsShown?
    /// The passage selected on the score: a bracket tapped, a bar tapped, or bars chosen by a long press and a drag.
    @Published private(set) var selection: ScoreSelection?
    /// Where the selection menu's top left corner goes in the score's view, as the web app places its menu; nil while
    /// nothing is selected or a drag is on.
    @Published private(set) var menuPlace: CGPoint?
    /// A note tapped on the score or on the card in focus: the player plays from it.
    var noteTapped: (@MainActor (Double) -> Void)?
    /// A tap on the page away from the notes; true when the screen took it (the Insights sheet goes away and the
    /// selection stays), false to clear the selection.
    var blankTapped: (@MainActor () -> Bool)?
    /// Phrase focus (0018 section 4): the selection lifted into a card across the screen, the page dimmed behind it.
    @Published private(set) var focused = false
    /// Lead-in, on the pill in focus: play takes in the bar before the phrase and the bar after it.
    @Published var leadIn = false {
        didSet { if oldValue != leadIn { Task { await updatePlayRange() } } }
    }
    /// What Play and Loop take for the selection: its span, with a bar either side while leading in.
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
    /// The layers of marks, then another version's comparison layer and its likely artefacts (0022, section 4).
    static let layers = ["ink", "notes", "bookmarks", "analysis", "check", "version", "artefacts"]
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
    /// The version just added: its four next steps show until one is chosen.
    @Published var addedVersion: String?
    /// The bars Take everything sure took last, from which version, to give back with one tap.
    @Published private(set) var takenAll: TakenAll?

    struct TakenAll: Equatable {
        var version: String
        var bars: [Int]
    }

    /// A version's label tapped on the score: the screen opens Other versions on its bar.
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
        var differing: [Int]
        var taken: [Int]
    }

    /// What the page's engraving holds above the systems: the version's comparison layer and whether with its artefacts.
    private struct LayerEngraved: Equatable {
        var version: String?
        var artefacts = false
    }
    private var layerEngraved = LayerEngraved()
    /// The engraved ids of the notes the layer's version adds, and of its likely artefacts, to tint on the page.
    private var layerAdded: [String] = []
    private var layerArtefacts: [String] = []
    /// The version's notes as music-core gives them to play (`playback` of `version`), for the version they are of.
    private var versionPlayback: (id: String, notes: [PlaybackNote])?

    /// The layers this piece has: Check only for a piece with flags, the comparison layer with a version shown, and its
    /// likely artefacts while that layer is on and the version has some.
    var pieceLayers: [String] {
        Self.layers.filter { layer in
            switch layer {
            case "check": check.hasFlags
            case "version": versions.shown != nil
            case "artefacts": versions.shown != nil && !hiddenLayers.contains("version") && versions.artefactCount > 0
            default: true
            }
        }
    }

    /// What a layer is called in the list: the artefacts with their count.
    func layerName(_ layer: String) -> String {
        layer == "artefacts" ? text.format("layers.artefactsCount", String(versions.artefactCount)) : text["layers.\(layer)"]
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
    private let canvas = PKCanvasView()
    private var piece: OpenPiece?
    private var dark = false
    private var pageReady = false
    private var shown = false
    private var clearing = false
    private(set) var previousTool = Tool.eraser
    /// The thickness of the pen and of the highlighter, each kept apart (PenSizes).
    @Published var penWidth = PenSizes.defaultWidth { didSet { applyTool() } }
    @Published var highlighterWidth = PenSizes.defaultWidth { didSet { applyTool() } }
    private let text = UIText.current
    /// Pixels between two staff lines on screen, for the width of the live stroke.
    private var staffSpace: CGFloat = 8
    /// The strokes added here, newest last, for Undo.
    private var added: [String] = []
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

        // The canvas lies over the web view and takes only the Pencil: its drawing gesture is moved to the web view,
        // so a finger still reaches the web view and scrolls the score.
        canvas.backgroundColor = .clear
        canvas.isOpaque = false
        applyInput()
        canvas.isScrollEnabled = false
        canvas.delegate = self
        canvas.frame = webView.bounds
        canvas.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        canvas.isUserInteractionEnabled = false
        webView.addSubview(canvas)
        webView.addGestureRecognizer(canvas.drawingGestureRecognizer)
        let pencil = UIPencilInteraction()
        pencil.delegate = self
        webView.addInteraction(pencil)
        applyTool()

        webView.load(URLRequest(url: BundleSchemeHandler.url("Web/score.html")))
    }

    /// Shows a piece's pages and its marks; again when the appearance changes.
    func show(_ piece: OpenPiece, dark: Bool) {
        guard self.piece?.id != piece.id || self.dark != dark || !shown else { return }
        // The same piece keeps the pages engraved here after its notes changed or its comparison layer came on.
        if self.piece?.id != piece.id {
            self.piece = piece
            versions = VersionShelf()
            versionStatus = nil
            sources = SourcesInfo()
            addedVersion = nil
            takenAll = nil
            layerEngraved = LayerEngraved()
            versionPlayback = nil
        }
        self.dark = dark
        if pageReady { Task { await render() } }
    }

    func undo() {
        guard let last = added.popLast(), let piece else { return }
        canUndo = !added.isEmpty
        Task { await change(piece.id, "ink.remove", ["ids": [last]]) }
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
            Task { await select(start, end) }
        }
        if let bar = message["bar"] as? [String: Any], let start = number(bar["start"]), let end = number(bar["end"]) {
            Task { await select(start, end) }
        }
        if let bracket = number(message["bracket"]) {
            selectPhrase(Int(bracket))
        }
        if let bookmark = message["bookmark"] as? String {
            goToBookmark(bookmark)
        }
        if let note = message["noteMarker"] as? String {
            openNote(note)
        }
        if let away = number(message["playheadAway"]) {
            playheadAway = away < 0 ? nil : away
        }
        if let bar = number(message["checkFlag"]) {
            showCheckBar(Int(bar))
            checkPicked?(Int(bar))
        }
        if let bar = number(message["performanceBar"]) {
            showVersionBar(Int(bar))
            versionPicked?(Int(bar))
        }
        if let note = number(message["note"]) {
            noteTapped?(note)
        }
        if message["blank"] != nil, blankTapped?() != true {
            clearSelection()
        }
        if let note = number(message["cardNote"]) {
            noteTapped?(note)
        }
        if message["leave"] != nil {
            leaveFocus()
        }
        if let options = message["cardLayout"] as? [String: Any] {
            cardOptions = Self.json(options)
            Task { await engraveCard() }
        }
    }

    // MARK: - Phrases and the selection

    /// Shows another level: the brackets are drawn again, and the selection moves to the unit that is, encloses or starts
    /// it at the new level.
    func showLevel(_ level: String) {
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

    /// A bracket tapped: its unit is selected; a second tap on the selected one lifts it into focus.
    func selectPhrase(_ number: Int) {
        guard let unit = phrases?.unit(number) else { return }
        if let selection, selection.onset == unit.selectableOnset, selection.end == unit.selectableEnd {
            enterFocus()
            return
        }
        Task { await select(unit.selectableOnset, unit.selectableEnd) }
    }

    /// Selects a passage through music-core, which keeps it within what a selection may hold.
    func select(_ start: Double, _ end: Double) async {
        await select(request: "{\"span\": {\"onset\": \(Int(start)), \"end\": \(Int(end))}}")
    }

    /// Selects whole bars, as the piano roll picks them; a tap that extends takes the selection to the bar tapped.
    func selectBars(_ first: Int, _ last: Int, extend: Bool) {
        var from = first
        var to = last
        if extend, let selection {
            if first < selection.firstBar { to = selection.lastBar } else { from = selection.firstBar }
        }
        Task { await select(request: "{\"bars\": {\"first\": \(from), \"last\": \(to)}}") }
    }

    private func select(request: String) async {
        guard let piece else { return }
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "select", request) }
        guard let chosen = ScoreSelection(answer: answer) else {
            problem = facadeObject(answer)?["message"] as? String
            return
        }
        selection = chosen
        await showSelection()
        await updatePlayRange()
        if focused {
            await engraveCard()
            await updateFocusVersion()
        }
    }

    func clearSelection() {
        guard selection != nil else { return }
        // Nothing to lift: focus goes with the selection.
        leaveFocus()
        selection = nil
        Task {
            await showSelection()
            await updatePlayRange()
        }
    }

    // MARK: - Bookmarks and the Contents

    /// Draws the bookmarks as brackets in their categories' inks above the phrase brackets (0018 section 5).
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
        let phrase = phrases?.units.contains { $0.selectableOnset == selection.onset && $0.selectableEnd == selection.end } ?? false
        return text.format(phrase ? "ask.thisPhrase" : "ask.thisSelection", bars)
    }

    var bookmarkTitle: String {
        guard let selection else { return "" }
        if let unit = phrases?.units.first(where: { $0.selectableOnset == selection.onset && $0.selectableEnd == selection.end }) {
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
            await applyVersionLayer()
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
        return Set(Self.layers.filter { values["layers.\($0)"] == "off" })
    }

    /// What the page hides: the layers hidden, and the phrase brackets when Settings turns them off.
    private func applyLayers() async {
        guard shown else { return }
        var hidden = Array(hiddenLayers)
        if DeviceSettings.shared["guidance.brackets"] == "off" { hidden.append("brackets") }
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
            await applyVersionLayer()
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
        guard let span = check.flags.first(where: { $0.bar == bar && $0.span != nil })?.span else { return }
        Task {
            await select(span.lowerBound, span.upperBound)
            _ = try? await webView.callAsyncJavaScript("return motiff.reveal(data)", arguments: ["data": ["start": span.lowerBound]], contentWorld: .page)
        }
    }

    /// Looks right, or Check again: every flag on the bar resolved or open again, kept with the piece.
    func resolveCheck(_ bar: Int, _ resolved: Bool) {
        guard let piece else { return }
        let request = Self.json(["bar": bar, "resolved": resolved]) ?? "{}"
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

    /// A version just added: its four next steps show, and the list, the markers and Check follow.
    private func versionAdded(_ piece: OpenPiece) async {
        addedVersion = versions.shownId
        takenAll = nil
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
        var status = versions.matched == 0
            ? text["performance.noMatch"]
            : text.format("performance.added", String(versions.matched), String(versions.printed), String(versions.bars.filter(\.differs).count))
        if heard.cut { status += " " + text.format("version.recordingCut", "7") }
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

    /// Compare: the version's notes on small staves above the systems, its markers above the bars it has otherwise.
    func compareVersion() {
        addedVersion = nil
        showLayers(["version"], true)
    }

    /// Take everything sure: every bar the comparison lines up with at least 80 % sure, not dismissed or taken already, at
    /// once (`performance.takeAll`); Give back returns them.
    func takeAllSure() {
        guard let piece, let id = versions.shownId else { return }
        let before = Set(versions.taken.map(\.bar))
        addedVersion = nil
        let request = Self.json(["id": id]) ?? "{}"
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "performance.takeAll", request) }
            guard versions.read(performance: answer) else {
                versionStatus = facadeObject(answer)?["message"] as? String
                return
            }
            let bars = versions.taken.map(\.bar).filter { !before.contains($0) }
            guard !bars.isEmpty else {
                versionStatus = text["sources.nothingSure"]
                await drawVersions()
                return
            }
            takenAll = TakenAll(version: id, bars: bars)
            versionStatus = text.count("sources.takenAll", bars.count)
            await notesDidChange()
        }
    }

    /// Give back: the bars Take everything sure took come back as they were.
    func giveBackTakenAll() {
        guard let takenAll, takenAll.version == versions.shownId else { return }
        self.takenAll = nil
        changeVersion("performance.restore", ["bars": takenAll.bars], status: text.count("sources.givenBack", takenAll.bars.count), notes: true)
    }

    /// Where the first bar the shown version has otherwise is, or the first bar when it agrees everywhere.
    private var firstDifference: Range<Double>? {
        versions.differing.first?.span ?? versions.bars.first?.span
    }

    /// Hear it alone: the phrase with the first bar the version has otherwise is selected and lifted into focus, where the
    /// version is heard (0022, section 5).
    func focusFirstDifference() async {
        addedVersion = nil
        guard let span = firstDifference else { return }
        if let unit = phrases?.units.first(where: { $0.onset <= span.lowerBound && span.lowerBound < $0.end }) {
            await select(unit.selectableOnset, unit.selectableEnd)
        } else {
            await select(span.lowerBound, span.upperBound)
        }
        enterFocus()
    }

    /// Adjust it myself: the first bar the version has otherwise is selected and the note editor opens on it.
    func editFirstDifference() async {
        addedVersion = nil
        guard let span = firstDifference else { return }
        await select(span.lowerBound, span.upperBound)
        beginNoteEdit()
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

    /// Not a difference, or Show again: the bar's marker goes or comes back, kept with the piece.
    func dismissVersionBar(_ bar: Int, _ dismissed: Bool) {
        changeVersion("performance.dismiss", ["bar": bar, "dismissed": dismissed], status: text.format(dismissed ? "performance.dismissed" : "performance.undismissed", String(bar)))
    }

    /// Take into the score: the bar's notes become the version's, and the score is engraved again.
    func takeVersionBar(_ bar: Int) {
        let name = versions.shown?.name ?? ""
        changeVersion("performance.take", ["bars": [bar]], status: text.format("performance.taken", String(bar), name), notes: true)
    }

    /// Restore these notes: the bar is as it was before it was taken.
    func restoreVersionBar(_ bar: Int) {
        changeVersion("performance.restore", ["bars": [bar]], status: text.format("performance.restored", String(bar)), notes: true)
    }

    func renameVersion(_ name: String) {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed != versions.shown?.name else { return }
        changeVersion("performance.rename", ["name": trimmed], status: nil)
    }

    /// Removes the version shown; bars taken from it go back to these notes.
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
            versionStatus = text.format("performance.removed", shown.name)
            if let next = versions.entries.first {
                await readVersion(piece.id, next.id)
            } else {
                await drawVersions()
            }
            if shown.takenBars > 0 { await notesDidChange() }
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

    /// The notes changed (bars taken or restored): the player and the roll take the new notes, and the score is engraved
    /// again and drawn with its marks.
    private func notesDidChange() async {
        versionPlayback = nil
        notesChanged?()
        await engraveScore()
    }

    /// The comparison layer as the layers ask (0022, section 4): the version shown on small staves above every system
    /// while its layer is on, its likely artefacts too while theirs is; the score is engraved again when that changes.
    private func applyVersionLayer() async {
        let version = hiddenLayers.contains("version") ? nil : versions.shownId
        let wanted = LayerEngraved(version: version, artefacts: version != nil && !hiddenLayers.contains("artefacts"))
        guard wanted != layerEngraved else { return }
        await engraveScore()
    }

    /// Engraves the score again, with the comparison layer when it is on, and draws it with its marks.
    private func engraveScore() async {
        guard let piece else { return }
        let id = piece.id
        let title = piece.title
        let version = hiddenLayers.contains("version") ? nil : versions.shownId
        let layer = LayerEngraved(version: version, artefacts: version != nil && !hiddenLayers.contains("artefacts"))
        let engraved = await MusicCore.run { Engraver.engrave(piece: id, title: title, version: layer.version, artefacts: layer.artefacts) }
        switch engraved {
        case .success(let engraving):
            // Its other view stays; where it opened does not, so the page stays where the player is.
            self.piece = OpenPiece(id: id, title: title, pages: engraving.pages, timemap: engraving.timemap, file: piece.file, other: piece.other)
            layerEngraved = layer
            layerAdded = engraving.ossias.first?.added ?? []
            layerArtefacts = engraving.ossias.first?.artefacts ?? []
            await render()
            if focused { await engraveCard() }
        case .failure(let error):
            problem = error.message
        }
    }

    /// The bars the version has otherwise as plum labels above them, and the printed notes it does not have tinted.
    private func drawVersions() async {
        await readSources()
        guard shown else { return }
        let name = versions.shown?.name ?? ""
        let differing = versions.differing
        let markers: [[String: Any]] = differing.compactMap { bar in
            guard let span = bar.span else { return nil }
            return ["bar": bar.bar, "tick": span.lowerBound, "text": bar.marker, "label": text.format("performance.markerLabel", String(bar.bar), name, bar.marker)]
        }
        let layered = layerEngraved.version != nil && layerEngraved.version == versions.shownId
        let data: [String: Any] = [
            "markers": markers, "unheard": differing.flatMap(\.notHeard),
            "added": layered ? layerAdded : [], "artefacts": layered ? layerArtefacts : [],
        ]
        _ = try? await webView.callAsyncJavaScript("return motiff.performance(data)", arguments: ["data": data], contentWorld: .page)
        await updateFocusVersion()
        await applyVersionLayer()
    }

    /// Take into the score in focus: every bar of the phrase the version has otherwise, at once.
    func takeFocusBars() {
        guard let focusVersion, !focusVersion.differing.isEmpty else { return }
        let bars = focusVersion.differing
        changeVersion("performance.take", ["bars": bars], status: text.format("performance.takenBars", bars.map(String.init).joined(separator: ", "), focusVersion.name), notes: true)
    }

    /// Restore these notes in focus: every bar of the phrase taken from the version comes back as it was.
    func restoreFocusBars() {
        guard let focusVersion, !focusVersion.taken.isEmpty else { return }
        let bars = focusVersion.taken
        changeVersion("performance.restore", ["bars": bars], status: text.format("performance.restoredBars", bars.map(String.init).joined(separator: ", ")), notes: true)
    }

    /// The version for the phrase in focus: its notes to hear, and the bars to take or restore.
    private func updateFocusVersion() async {
        guard focused, let piece, let selection, let shown = versions.shown else {
            focusVersion = nil
            return
        }
        if versionPlayback?.id != shown.id {
            let request = Self.json(["of": "version", "id": shown.id]) ?? "{}"
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "playback", request) }
            versionPlayback = (shown.id, PlaybackScore.read(answer)?.notes ?? [])
        }
        guard focused, self.selection == selection, versions.shownId == shown.id, let notes = versionPlayback?.notes else { return }
        let bars = selection.firstBar...selection.lastBar
        focusVersion = FocusVersion(
            id: shown.id, name: shown.name, notes: notes,
            sounds: notes.contains { $0.onset >= selection.onset && $0.onset < selection.end },
            differing: versions.differing.map(\.bar).filter { bars.contains($0) },
            taken: versions.taken.map(\.bar).filter { bars.contains($0) }
        )
    }

    // MARK: - Phrase focus

    /// Lifts the selection into focus: the page dims, the card shows the selection's bars on their own, and the page
    /// neither scrolls nor takes the Pencil until Back.
    func enterFocus() {
        guard shown, selection != nil, !penOn, !focused else { return }
        focused = true
        setPageStill(true)
        let request: [String: Any] = ["open": true, "label": text.format("focus.cardLabel", focusPosition), "back": text["focus.back"]]
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

    /// Expand on the roll drawer: the selection, or with none the phrase the playhead is in (the first before any playing),
    /// in focus as a roll.
    func expandRoll(at tick: Double) {
        focusRoll = true
        if selection != nil {
            enterFocus()
            return
        }
        guard let units = phrases?.units, let unit = units.first(where: { $0.onset <= tick && tick < $0.end }) ?? units.first else { return }
        Task {
            await select(unit.selectableOnset, unit.selectableEnd)
            enterFocus()
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

    /// Previous or next at the level shown: the card moves on to that unit.
    func step(_ direction: Int) {
        guard let unit = focusUnit, let next = phrases?.unit(unit.number + direction.signum()) else { return }
        Task { await select(next.selectableOnset, next.selectableEnd) }
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

    /// What play takes for the selection now: its span, with the bar before and the bar after while leading in.
    private func updatePlayRange() async {
        guard let selection else {
            playRange = nil
            return
        }
        var range = selection.onset..<selection.end
        if focused && leadIn,
           let wider = try? await webView.callAsyncJavaScript("return motiff.lead(data)", arguments: ["data": ["start": selection.onset, "end": selection.end]], contentWorld: .page) as? [String: Any],
           let start = (wider["start"] as? NSNumber)?.doubleValue, let end = (wider["end"] as? NSNumber)?.doubleValue, start < end {
            range = start..<end
        }
        guard self.selection == selection else { return }
        playRange = PlayRange(selection: selection, range: range)
    }

    /// In focus the page under the veil stays where it is: it does not scroll and the Pencil does not write on it.
    private func setPageStill(_ still: Bool) {
        webView.scrollView.isScrollEnabled = !still
        canvas.drawingGestureRecognizer.isEnabled = !still
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

    /// Draws the brackets, the selected one in the accent, and the band over the selection.
    private func showSelection() async {
        guard shown else { return }
        let units: [[String: Any]] = (phrases?.units ?? []).map { unit in
            let current = selection.map { $0.onset == unit.selectableOnset && $0.end == unit.selectableEnd } ?? false
            return ["start": unit.onset, "end": unit.end, "number": unit.number, "kind": unit.kind, "current": current, "label": label(of: unit)]
        }
        _ = try? await webView.callAsyncJavaScript("return motiff.phrases(data)", arguments: ["data": ["units": units]], contentWorld: .page)
        var band: Any = NSNull()
        if let selection {
            band = ["start": selection.onset, "end": selection.end, "startLabel": text["selection.handleStart"], "endLabel": text["selection.handleEnd"]] as [String: Any]
        }
        _ = try? await webView.callAsyncJavaScript("return motiff.select(data)", arguments: ["data": band], contentWorld: .page)
    }

    /// What a screen reader says for a bracket: "Phrase 3, bars 9–12".
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
                applyTool()
            }
            shown = true
            if let tick = piece.landing?.tick {
                // From the PDF's page in PDF | Notes (0023): the page of the notes where it starts.
                _ = try? await webView.callAsyncJavaScript("return motiff.showTick(data)", arguments: ["data": ["tick": tick]], contentWorld: .page)
            }
            _ = try? await webView.callAsyncJavaScript("return motiff.pen(on)", arguments: ["on": penOn], contentWorld: .page)
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
        } catch {
            problem = error.localizedDescription
        }
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

    private func change(_ piece: Int32, _ call: String, _ request: [String: Any]) async {
        guard let data = try? JSONSerialization.data(withJSONObject: request) else { return }
        let text = String(decoding: data, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: piece, call, text) }
        await draw(answer)
    }

    // MARK: - Strokes

    /// A stroke PencilKit finished: its points in the page's pixels, then kept as a mark or, with the eraser, what it
    /// passed over taken away.
    private func strokeEnded(_ stroke: PKStroke) {
        guard let piece, shown else { return }
        let zoom = max(webView.scrollView.zoomScale, 0.01)
        let points: [[String: Double]] = stroke.path.interpolatedPoints(by: .distance(1.5)).map { point in
            let inCanvas = point.location.applying(stroke.transform)
            let inPage = webView.scrollView.convert(inCanvas, from: canvas)
            // UITouch force is about 1 for an ordinary press: pressure 0.5, as the web takes a pen without pressure.
            return ["x": inPage.x / zoom, "y": inPage.y / zoom, "pressure": min(1, max(0, Double(point.force) / 2))]
        }
        guard !points.isEmpty else { return }
        let tool = tool
        Task {
            switch tool {
            case .eraser:
                let ids = try? await webView.callAsyncJavaScript("return motiff.erase(data)", arguments: ["data": ["points": points]], contentWorld: .page) as? [String]
                if let ids, !ids.isEmpty {
                    added.removeAll { ids.contains($0) }
                    canUndo = !added.isEmpty
                    await change(piece.id, "ink.remove", ["ids": ids])
                }
            case .pen(let ink):
                await add(piece.id, points, tool: "pen", colour: ink.rawValue, width: penWidth)
            case .highlighter:
                await add(piece.id, points, tool: "highlighter", colour: "amber", width: highlighterWidth)
            }
        }
    }

    private func add(_ piece: Int32, _ points: [[String: Double]], tool: String, colour: String, width: Int) async {
        let request: [String: Any] = ["points": points, "tool": tool, "colour": colour, "width": width]
        guard let mark = try? await webView.callAsyncJavaScript("return motiff.anchor(data)", arguments: ["data": request], contentWorld: .page) as? [String: Any],
              let id = mark["id"] as? String
        else { return }
        added.append(id)
        canUndo = true
        await change(piece, "ink.add", ["strokes": [mark]])
    }

    /// Who writes and who scrolls. The Pencil only writes: the score never scrolls under it.
    private func applyInput() {
        let pan = webView.scrollView.panGestureRecognizer
        #if targetEnvironment(simulator)
        // The simulator has no Pencil and its mouse is a finger: there the mouse writes, and two fingers on the
        // trackpad (or the wheel) scroll the score.
        canvas.drawingPolicy = .anyInput
        pan.allowedTouchTypes = [NSNumber(value: UITouch.TouchType.indirectPointer.rawValue)]
        #else
        canvas.drawingPolicy = penOn ? .anyInput : .pencilOnly
        pan.allowedTouchTypes = [UITouch.TouchType.direct, .indirectPointer].map { NSNumber(value: $0.rawValue) }
        pan.minimumNumberOfTouches = penOn ? 2 : 1
        #endif
    }

    /// PencilKit draws the live stroke as the tool will look: the ink's colour, the web's thickness at this zoom.
    private func applyTool() {
        let zoom = webView.scrollView.zoomScale
        switch tool {
        case .pen(let ink):
            canvas.tool = PKInkingTool(.pen, color: ink.color, width: PenSizes.pen[penWidth] * staffSpace * zoom * 1.6)
        case .highlighter:
            canvas.tool = PKInkingTool(.marker, color: DesignTokens.color("amber").withAlphaComponent(0.38), width: PenSizes.highlighter[highlighterWidth] * staffSpace * zoom)
        case .eraser:
            canvas.tool = PKInkingTool(.pen, color: DesignTokens.color("ink-graphite").withAlphaComponent(0.25), width: 20)
        }
    }
}

extension ScoreController: PKCanvasViewDelegate {
    nonisolated func canvasViewDrawingDidChange(_ canvasView: PKCanvasView) {
        MainActor.assumeIsolated {
            guard !clearing, let stroke = canvasView.drawing.strokes.last else { return }
            clearing = true
            canvasView.drawing = PKDrawing()
            clearing = false
            strokeEnded(stroke)
        }
    }
}

extension ScoreController: UIPencilInteractionDelegate {
    /// A double tap on the Pencil does what the player chose in the iPad's settings, as in 0015.
    nonisolated func pencilInteractionDidTap(_ interaction: UIPencilInteraction) {
        MainActor.assumeIsolated {
            switch UIPencilInteraction.preferredTapAction {
            case .switchEraser:
                tool = tool == .eraser ? previousTool : .eraser
            case .switchPrevious:
                tool = previousTool
            default:
                break
            }
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
