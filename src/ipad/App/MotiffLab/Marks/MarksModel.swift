import SwiftUI

/// What the marks need of the score they are drawn on (ADR 0031 section 3, v1.6 i-10b), as `PenScore`, `CheckScore`,
/// `SourcesScore` and `FocusScore`: the page and the piece, whether the pages are drawn and the Pen is on, whether the
/// Check drawer is open and the page is slate (Check's and the device's, handed in as values), a passage selected, a tick
/// brought into view, Expand opened, the versions drawn again with the layers, and the problem line. ScoreController, the
/// composition root, is it.
@MainActor
protocol MarksScore: AnyObject {
    /// The score's page, the one way to its functions (`PageCommand`).
    var scorePage: ScorePage { get }
    /// The piece open in music-core; nil while none is.
    var pieceId: Int32? { get }
    /// Whether the pages are drawn, so marks can go on them.
    var shown: Bool { get }
    /// Whether the bar's Pen is on: a bookmark gone to again does not lift into focus then.
    var penOn: Bool { get }
    /// Whether the Check drawer is open: its flags show then even with extra information off (`CheckModel.drawerOpen`).
    var checkDrawerOpen: Bool { get }
    /// Whether the page is drawn in the slate theme (the device's appearance).
    var slate: Bool { get }
    /// A passage selected through music-core, as a tap selects it.
    func selectPassage(from start: Double, to end: Double) async
    /// The page scrolled smoothly to where a tick is.
    func reveal(_ tick: Double) async
    /// The selection lifted into focus (Expand).
    func enterFocus()
    /// The versions drawn again: their markers follow the kinds of extra information shown.
    func drawVersions() async
    /// What went wrong, in the screen's problem line; nil for nothing.
    func showProblem(_ message: String?)
}

/// The player's marks on the score (ADR 0031 section 3, S6): the margin notes, highlights and inked bars as music-core
/// answered the last mark call, the note being written, the layers of marks hidden and the piece's own settings they are
/// kept in, whether the phrases' marks show on this device, and the bookmarks as the page draws them with the one last
/// gone to. It owns this state and the rules of its own that need only the selection (SelectionModel, which it reads):
/// whether the selection is the bookmark gone to, the layers and the kinds of extra information a piece has, and reading
/// and keeping the piece's settings through music-core. Since v1.6 i-10b it changes a mark and draws music-core's answer,
/// draws the bookmarks, goes to a bookmark or a note and sends what the page hides, through the score it is given
/// (`MarksScore`), as CheckModel does; the views observe this model.
@MainActor
final class MarksModel: ObservableObject {
    /// A margin note being written: a new one (no id) or one already kept, over its bars.
    struct NoteDraft: Equatable {
        var id: String?
        var onset: Double
        var end: Double
        var firstBar: Int
        var lastBar: Int
        var text: String
    }

    /// The bookmark last gone to and the selection that gave.
    struct ShownBookmark: Equatable {
        var id: String
        var selection: ScoreSelection
    }

    /// The layers of the player's marks (0018 section 5), which show as before.
    static let layers = ["ink", "notes", "bookmarks", "analysis"]
    /// Extra information (testing item 33): what the app adds over the notes, off by default behind the ⓘ button, each
    /// kind with its own switch: differences from versions, the bars taken from a version, and the Check flags.
    static let extra = ["performance", "taken", "check"]
    private static let layerIds = layers + ["extra"] + extra
    /// Where this device keeps the phrases' marks hidden (the phrases spec, v1.2): a choice of the device for every piece,
    /// shown by default. It replaces «Не показывать» of the Phrases menu, whose key is not read.
    static let phraseMarksHiddenKey = "motiff.phraseMarks.hidden"

    @Published private(set) var markCount = 0
    /// The margin notes, highlights and inked bars, as music-core answered the last mark call.
    @Published private(set) var marks = MarksShelf()
    /// The note being written over the selection, or opened from its marker or the Notes tab; nil while none is.
    @Published var noteDraft: NoteDraft?
    /// The layers of marks hidden on the page (0018 section 5), kept with the piece as `layers.*` settings.
    @Published private(set) var hiddenLayers: Set<String> = []
    /// «Фразы» off in the list of extra information: the phrases' marks are not drawn, on this device.
    @Published private(set) var phraseMarksHidden = UserDefaults.standard.bool(forKey: MarksModel.phraseMarksHiddenKey) {
        didSet { UserDefaults.standard.set(phraseMarksHidden, forKey: Self.phraseMarksHiddenKey) }
    }
    /// The piece's own settings, by id, as the project keeps them: the Settings sheet lists them, to clear in one tap.
    @Published private(set) var pieceOverrides: [String: String] = [:]
    /// The bookmark last gone to and the selection that gave: a second tap on it lifts the passage into focus.
    @Published private(set) var shownBookmark: ShownBookmark?
    /// The bookmarks as the page draws them, and where each is, for a tap on its bracket or its entry in the Contents.
    private(set) var bookmarkItems: [[String: Any]] = []
    private var bookmarkSpans: [String: Range<Double>] = [:]

    /// The score the marks are drawn on, set by the root once it is made.
    weak var score: MarksScore?
    private let selection: SelectionModel
    /// The settings in force for a piece with these overrides of its own: the device's, the piece's over them.
    private let resolved: @MainActor ([String: String]) -> [String: String]
    private let text = UIText.current

    /// `resolved`: the device's settings with a piece's own over them (DeviceSettings.resolved).
    init(selection: SelectionModel, resolved: @escaping @MainActor ([String: String]) -> [String: String]) {
        self.selection = selection
        self.resolved = resolved
    }

    /// Draws the marks music-core answered (`marks`, `ink.add`, `ink.remove`, a note or a highlight changed), and keeps
    /// them with how many strokes of ink the piece has; a refusal goes to the problem line.
    func draw(_ answer: String) async {
        guard let object = facadeObject(answer) else { return }
        if object["error"] != nil {
            score?.showProblem(FacadeAnswer.message(answer))
            return
        }
        let ink = object["ink"] as? [Any] ?? []
        let shelf = MarksShelf(object: object)
        markCount = ink.count
        marks = shelf
        await score?.scorePage.send(.draw, ["marks": ink])
        let notes: [[String: Any]] = shelf.notes.map { note in
            let label = text.format("margin.markerLabel", String(shelf.number(of: note)), barsText(note.firstBar, note.lastBar, text), MarksShelf.firstWords(note.text))
            return ["id": note.id, "tick": note.onset, "number": shelf.number(of: note), "label": label]
        }
        let highlights: [[String: Any]] = shelf.highlights.map { ["id": $0.id, "start": $0.onset, "end": $0.end, "ink": $0.colour] }
        await score?.scorePage.send(.notes, ["notes": notes, "highlights": highlights])
    }

    /// A change to the marks through music-core; the answer is drawn unless `draws` is false, for the first of two calls
    /// that make one change, so the page never shows it half done.
    func changeMarks(_ piece: Int32, _ call: PieceCore.Call, _ request: [String: Any], draws: Bool = true) async {
        let text = facadeRequest(request)
        let answer = await MusicCore.run { PieceCore(piece: piece).send(call, text) }
        if draws { await draw(answer) }
    }

    // MARK: - Margin notes

    /// Starts a note over the selection.
    func startNote() {
        guard let selection = selection.selection else { return }
        noteDraft = NoteDraft(id: nil, onset: selection.onset, end: selection.end, firstBar: selection.firstBar, lastBar: selection.lastBar, text: "")
    }

    /// Opens a note to read or change: its bars are selected and the card shows its words.
    func openNote(_ id: String) {
        guard let score, let note = marks.notes.first(where: { $0.id == id }) else { return }
        let draft = NoteDraft(id: id, onset: note.onset, end: note.end, firstBar: note.firstBar, lastBar: note.lastBar, text: note.text)
        Task {
            if selection.selection?.onset != note.onset || selection.selection?.end != note.end {
                await score.selectPassage(from: note.onset, to: note.end)
                await score.reveal(note.onset)
            }
            noteDraft = draft
        }
    }

    /// Done on the note card: a new note is kept when it has words; a kept one changes, or goes when emptied.
    func saveNote() {
        guard let draft = noteDraft, let piece = score?.pieceId else { return }
        noteDraft = nil
        let words = draft.text.trimmingCharacters(in: .whitespacesAndNewlines)
        Task {
            if let id = draft.id {
                if words.isEmpty {
                    await changeMarks(piece, .noteRemove, ["id": id])
                } else {
                    await changeMarks(piece, .noteChange, ["id": id, "text": words])
                }
            } else if !words.isEmpty {
                let span = ["onset": Int(draft.onset), "end": Int(draft.end)]
                await changeMarks(piece, .noteAdd, ["span": span, "text": words, "now": ISO8601DateFormatter().string(from: Date())])
            }
        }
    }

    func deleteNote(_ id: String) {
        guard let piece = score?.pieceId else { return }
        noteDraft = nil
        Task { await changeMarks(piece, .noteRemove, ["id": id]) }
    }

    // MARK: - Bookmarks

    /// Draws the bookmarks as brackets in their categories' inks above the top staff (0018 section 5), and keeps where
    /// each is.
    func showBookmarks(_ shelf: BookmarkShelf?) {
        let bookmarks = shelf?.bookmarks ?? []
        bookmarkSpans = Dictionary(bookmarks.map { ($0.id, $0.onset..<max($0.end, $0.onset + 1)) }, uniquingKeysWith: { first, _ in first })
        bookmarkItems = bookmarks.map { bookmark in
            [
                "id": bookmark.id, "start": bookmark.onset, "end": bookmark.end, "title": bookmark.title,
                "ink": shelf?.category(of: bookmark)?.ink ?? "graphite", "label": text.format("bookmark.label", bookmark.title, bookmark.bars(text)),
            ] as [String: Any]
        }
        guard let score, score.shown else { return }
        Task { await drawBookmarks() }
    }

    func drawBookmarks() async {
        await score?.scorePage.send(.bookmarks, ["items": bookmarkItems])
    }

    /// A bookmark gone to from its bracket or its entry: the score selects it and brings it into view; gone to again with
    /// the selection as it left it, the passage lifts into focus. True when it lifted.
    @discardableResult
    func goToBookmark(_ id: String) -> Bool {
        guard let score, let span = span(ofBookmark: id) else { return false }
        if let shownBookmark, shownBookmark.id == id, shownBookmark.selection == selection.selection, !score.penOn {
            score.enterFocus()
            return true
        }
        Task {
            await score.selectPassage(from: span.lowerBound, to: span.upperBound)
            wentTo(bookmark: id)
            await score.reveal(span.lowerBound)
        }
        return false
    }

    /// Where a bookmark is, in ticks; nil for one the page does not draw.
    func span(ofBookmark id: String) -> Range<Double>? {
        bookmarkSpans[id]
    }

    /// A bookmark gone to: the selection it gave is kept with it.
    private func wentTo(bookmark id: String) {
        if let selection = selection.selection { shownBookmark = ShownBookmark(id: id, selection: selection) }
    }

    /// Whether the selection is the bookmark last gone to: its entry in the Contents shows it.
    func isShown(_ id: String) -> Bool {
        shownBookmark?.id == id && shownBookmark?.selection == selection.selection
    }

    // MARK: - Layers and the piece's settings

    /// The layers of the player's marks.
    var pieceLayers: [String] { Self.layers }

    /// The kinds of extra information this piece has: the differences and taken bars with a version (`hasVersions`, from
    /// the screen that observes SourcesModel), Check with flags (`hasFlags`, Check's).
    func pieceExtra(hasVersions: Bool, hasFlags: Bool) -> [String] {
        Self.extra.filter { item in
            switch item {
            case "check": hasFlags
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

    /// Layers of marks shown or hidden as the piece's own choice, kept with the piece; the page hides them and draws the
    /// versions again.
    func showLayers(_ layers: [String], _ on: Bool) {
        guard let score, let piece = score.pieceId else { return }
        for layer in layers { pieceOverrides["layers.\(layer)"] = on ? "on" : "off" }
        hiddenLayers = layersHidden()
        Task {
            await keepLayers(layers, on, in: piece)
            await applyLayers()
            await score.drawVersions()
        }
    }

    /// Keeps layers shown or hidden with the piece (`settings.override`).
    private func keepLayers(_ layers: [String], _ on: Bool, in piece: Int32) async {
        for layer in layers {
            let request = facadeRequest(["id": "layers.\(layer)", "value": on ? "on" : "off"])
            _ = await MusicCore.run { PieceCore(piece: piece).send(.settingsOverride, request) }
        }
    }

    /// The phrases' marks shown or hidden («Фразы» in the list of extra information), kept on this device; the selection
    /// stays as it is.
    func showPhraseMarks(_ on: Bool) {
        phraseMarksHidden = !on
        Task { await applyLayers() }
    }

    /// What the page hides: the layers hidden, Check while neither extra information nor the Check drawer shows it, and
    /// the phrases' marks while the player hid them.
    func applyLayers() async {
        guard let score, score.shown else { return }
        var hidden = Self.layers.filter(hiddenLayers.contains)
        if !extraShown("check") && !score.checkDrawerOpen { hidden.append("check") }
        if phraseMarksHidden { hidden.append("phrases") }
        await score.scorePage.send(.layers, ["hidden": hidden, "slate": score.slate])
    }

    /// The piece's own choices (`settings` on the piece): its layers. The level an older piece kept
    /// (`phrases.granularity`) is no choice any more: the score shows phrases (the phrases spec, v1.2).
    func loadSettings(of piece: Int32) async {
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.settings) }
        let overrides = facadeObject(answer)?["overrides"] as? [String: String] ?? [:]
        pieceOverrides = overrides.filter { $0.key.hasPrefix("layers.") }
        hiddenLayers = layersHidden()
    }

    /// The device's settings changed in the sheet: the layers hidden follow, where the piece has no choice of its own, and
    /// the page follows at once.
    func deviceSettingsChanged() {
        hiddenLayers = layersHidden()
        Task {
            await applyLayers()
            await score?.drawVersions()
        }
    }

    /// "Use these settings for this piece": the piece's own choices go, in music-core too, and the device's hold for it.
    func clearOverrides() {
        guard let piece = score?.pieceId else { return }
        let ids = Array(pieceOverrides.keys)
        pieceOverrides = [:]
        Task {
            await forgetOverrides(ids, in: piece)
            deviceSettingsChanged()
        }
    }

    /// The piece's own choices of these ids forgotten in music-core, so the device's hold for it too.
    private func forgetOverrides(_ ids: [String], in piece: Int32) async {
        for id in ids {
            let request = facadeRequest(["id": id, "value": NSNull()])
            _ = await MusicCore.run { PieceCore(piece: piece).send(.settingsOverride, request) }
        }
    }

    /// The layers hidden: the piece's own choice, else the device's (0018 section 10).
    private func layersHidden() -> Set<String> {
        let values = resolved(pieceOverrides)
        return Set(Self.layerIds.filter { values["layers.\($0)"] == "off" })
    }
}
