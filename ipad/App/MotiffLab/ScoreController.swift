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
final class ScoreController: NSObject, ObservableObject {
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
    static let layers = ["ink", "notes", "bookmarks", "analysis", "check"]
    /// The bars a scan or a transcription is unsure of (0018 section 9), as music-core answered the last Check call.
    @Published private(set) var check = CheckShelf()
    /// A Check flag tapped on the score: the screen opens the Check drawer on its bar.
    var checkPicked: (@MainActor (Int) -> Void)?

    /// The layers this piece has: Check only for a piece with flags.
    var pieceLayers: [String] {
        Self.layers.filter { $0 != "check" || check.hasFlags }
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
    /// The pages under the reading line, for the bar: "p. 2 of 4", or "pp. 1–2 of 4" with two to a row.
    @Published private(set) var pages = PagesInView()

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
    private var previousTool = Tool.eraser
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
        self.piece = piece
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
        if let bar = number(message["checkFlag"]) {
            showCheckBar(Int(bar))
            checkPicked?(Int(bar))
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
        if focused { await engraveCard() }
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
        if on { hiddenLayers.subtract(layers) } else { hiddenLayers.formUnion(layers) }
        let id = piece.id
        Task {
            for layer in layers {
                let request = Self.json(["id": "layers.\(layer)", "value": on ? "on" : "off"]) ?? "{}"
                _ = await MusicCore.run { MusicCore.call(piece: id, "settings.override", request) }
            }
            await applyLayers()
        }
    }

    /// The layers hidden for this piece: its own choice, else the catalogue's default.
    private func loadLayers(_ piece: OpenPiece) async {
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "settings") }
        guard let object = facadeObject(answer) else { return }
        let overrides = object["overrides"] as? [String: String] ?? [:]
        var defaults: [String: String] = [:]
        for entry in object["settings"] as? [[String: Any]] ?? [] {
            if let id = entry["id"] as? String, let value = entry["default"] as? String { defaults[id] = value }
        }
        hiddenLayers = Set(Self.layers.filter { layer in
            let id = "layers.\(layer)"
            return (overrides[id] ?? defaults[id]) == "off"
        })
    }

    private func applyLayers() async {
        guard shown else { return }
        _ = try? await webView.callAsyncJavaScript("return motiff.layers(data)", arguments: ["data": ["hidden": Array(hiddenLayers)]], contentWorld: .page)
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
        }
    }

    /// Back to the page, with the selection as it is: Back, a tap on the dimmed page, or Escape.
    func leaveFocus() {
        guard focused else { return }
        focused = false
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
        let engraved = await MusicCore.run { Engraver.engraveCard(piece: id, firstBar: first, lastBar: last, options: options, ossia: withOssia) }
        // Stepped on, left or the variation changed while it engraved: the newer call shows its own.
        guard focused, self.selection == selection, ossia == name else { return }
        switch engraved {
        case .success(let card):
            let timemap = (try? JSONSerialization.jsonObject(with: Data(card.timemap.utf8))) ?? []
            let label = name.map { text.format("focus.ossia", $0) } ?? ""
            let ossias = card.ossias.map { ["staves": $0.staves, "prefix": $0.prefix, "label": label] as [String: Any] }
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
            _ = try? await webView.callAsyncJavaScript("return motiff.pen(on)", arguments: ["on": penOn], contentWorld: .page)
            let marks = await MusicCore.run { MusicCore.call(piece: piece.id, "marks") }
            await draw(marks)
            await loadPhrases(piece)
            await drawBookmarks()
            await loadCheck(piece)
            await loadLayers(piece)
            await applyLayers()
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
                await add(piece.id, points, tool: "pen", colour: ink.rawValue)
            case .highlighter:
                await add(piece.id, points, tool: "highlighter", colour: "amber")
            }
        }
    }

    private func add(_ piece: Int32, _ points: [[String: Double]], tool: String, colour: String) async {
        let request: [String: Any] = ["points": points, "tool": tool, "colour": colour]
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
            canvas.tool = PKInkingTool(.pen, color: ink.color, width: 0.16 * staffSpace * zoom * 1.6)
        case .highlighter:
            canvas.tool = PKInkingTool(.marker, color: DesignTokens.color("amber").withAlphaComponent(0.38), width: 1.1 * staffSpace * zoom)
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
