import SwiftUI

/// The player's marks on the score (ADR 0031 section 3, S6): the margin notes, highlights and inked bars as music-core
/// answered the last mark call, the note being written, the layers of marks hidden and the piece's own settings they are
/// kept in, and the bookmarks as the page draws them with the one last gone to. It owns this state and the rules of its
/// own that need only the selection (SelectionModel, which it reads): the highlight over the selection, whether the
/// selection is the bookmark gone to, the layers and the kinds of extra information a piece has, and reading and keeping
/// the piece's settings through music-core. Changing a mark (the answer drawn on the page), going to a bookmark or a note
/// and what the page hides are ScoreController's, the composition root, which hands in what music-core answers (`show`)
/// and the bookmarks (`keepBookmarks`); the views observe this model.
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
    /// The level a piece opens in, a setting of the device that a piece may have its own of.
    static let granularity = "phrases.granularity"

    @Published private(set) var markCount = 0
    /// The margin notes, highlights and inked bars, as music-core answered the last mark call.
    @Published private(set) var marks = MarksShelf()
    /// The note being written over the selection, or opened from its marker or the Notes tab; nil while none is.
    @Published var noteDraft: NoteDraft?
    /// The layers of marks hidden on the page (0018 section 5), kept with the piece as `layers.*` settings.
    @Published private(set) var hiddenLayers: Set<String> = []
    /// The piece's own settings, by id, as the project keeps them: the Settings sheet lists them, to clear in one tap.
    @Published private(set) var pieceOverrides: [String: String] = [:]
    /// The bookmark last gone to and the selection that gave: a second tap on it lifts the passage into focus.
    @Published private(set) var shownBookmark: ShownBookmark?
    /// The bookmarks as the page draws them, and where each is, for a tap on its bracket or its entry in the Contents.
    private(set) var bookmarkItems: [[String: Any]] = []
    private var bookmarkSpans: [String: Range<Double>] = [:]

    private let selection: SelectionModel
    /// The settings in force for a piece with these overrides of its own: the device's, the piece's over them.
    private let resolved: @MainActor ([String: String]) -> [String: String]
    private let text = UIText.current

    /// `resolved`: the device's settings with a piece's own over them (DeviceSettings.resolved).
    init(selection: SelectionModel, resolved: @escaping @MainActor ([String: String]) -> [String: String]) {
        self.selection = selection
        self.resolved = resolved
    }

    /// The marks music-core answered (`marks`, `ink.add`, `ink.remove`, a note or a highlight changed), with how many
    /// strokes of ink the piece has.
    func show(_ shelf: MarksShelf, inkCount: Int) {
        markCount = inkCount
        marks = shelf
    }

    // MARK: - Margin notes and highlights

    /// Starts a note over the selection.
    func startNote() {
        guard let selection = selection.selection else { return }
        noteDraft = NoteDraft(id: nil, onset: selection.onset, end: selection.end, firstBar: selection.firstBar, lastBar: selection.lastBar, text: "")
    }

    /// The highlight laid exactly over the selection, the newest when there are more.
    var selectedHighlight: HighlightMark? {
        guard let selection = selection.selection else { return nil }
        return marks.highlights.last { $0.onset == selection.onset && $0.end == selection.end }
    }

    // MARK: - Bookmarks

    /// The bookmarks to draw as brackets in their categories' inks above the top staff (0018 section 5), and where each is.
    func keepBookmarks(_ shelf: BookmarkShelf?) {
        let bookmarks = shelf?.bookmarks ?? []
        bookmarkSpans = Dictionary(bookmarks.map { ($0.id, $0.onset..<max($0.end, $0.onset + 1)) }, uniquingKeysWith: { first, _ in first })
        bookmarkItems = bookmarks.map { bookmark in
            [
                "id": bookmark.id, "start": bookmark.onset, "end": bookmark.end, "title": bookmark.title,
                "ink": shelf?.category(of: bookmark)?.ink ?? "graphite", "label": text.format("bookmark.label", bookmark.title, bookmark.bars(text)),
            ] as [String: Any]
        }
    }

    /// Where a bookmark is, in ticks; nil for one the page does not draw.
    func span(ofBookmark id: String) -> Range<Double>? {
        bookmarkSpans[id]
    }

    /// A bookmark gone to: the selection it gave is kept with it.
    func wentTo(bookmark id: String) {
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

    /// Layers of marks shown or hidden as the piece's own choice; `keepLayers` keeps it with the piece.
    func showLayers(_ layers: [String], _ on: Bool) {
        for layer in layers { pieceOverrides["layers.\(layer)"] = on ? "on" : "off" }
        hiddenLayers = layersHidden()
    }

    /// Keeps layers shown or hidden with the piece (`settings.override`).
    func keepLayers(_ layers: [String], _ on: Bool, in piece: Int32) async {
        for layer in layers {
            let request = facadeRequest(["id": "layers.\(layer)", "value": on ? "on" : "off"])
            _ = await MusicCore.run { MusicCore.call(piece: piece, "settings.override", request) }
        }
    }

    /// A level shown becomes the piece's own (`phrases.granularity`: "short", "sentences" or "phrases").
    func keepLevel(_ value: String) {
        pieceOverrides[Self.granularity] = value
    }

    /// The piece's own choices (`settings` on the piece): its layers and its level.
    func loadSettings(of piece: Int32) async {
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "settings") }
        pieceOverrides = facadeObject(answer)?["overrides"] as? [String: String] ?? [:]
        hiddenLayers = layersHidden()
    }

    /// The device's settings changed: the layers hidden follow, where the piece has no choice of its own.
    func deviceSettingsChanged() {
        hiddenLayers = layersHidden()
    }

    /// "Use these settings for this piece": the piece's own choices go; the ids they had, for `forgetOverrides`.
    func clearOverrides() -> [String] {
        let ids = Array(pieceOverrides.keys)
        pieceOverrides = [:]
        return ids
    }

    /// The piece's own choices of these ids forgotten in music-core, so the device's hold for it too.
    func forgetOverrides(_ ids: [String], in piece: Int32) async {
        for id in ids {
            let request = facadeRequest(["id": id, "value": NSNull()])
            _ = await MusicCore.run { MusicCore.call(piece: piece, "settings.override", request) }
        }
    }

    /// The layers hidden: the piece's own choice, else the device's (0018 section 10).
    private func layersHidden() -> Set<String> {
        let values = resolved(pieceOverrides)
        return Set(Self.layerIds.filter { values["layers.\($0)"] == "off" })
    }
}
