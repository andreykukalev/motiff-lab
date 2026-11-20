import Foundation

/// Expand and what plays for the selection (ADR 0031 section 3, S3): where Expand was opened from (#521 17), whether the
/// card shows the phrase as a roll (testing item 12), the card's options as the page gives them, the version shown for the
/// phrase in focus (0022 section 5), and the span Play and Loop take for the selection (#533 3). It owns this state and
/// the rules of its own that need only the selection (SelectionModel, which it reads): the play range, the phrase the
/// card is, and the card's words for where it is; and since S4b the version's notes to hear, read for the
/// version id the root hands in; and since S7c whether Expand is open (`focused`). Opening and leaving it on the page,
/// engraving the card and which version is in focus are ScoreController's, the composition root, which hands in what they
/// give (`open`, `close`, `takeCardOptions`, `showVersion`); the views observe this model. ADR 0031, the notes of
/// 2026-11-18 (S3, S4b, S7c).
@MainActor
final class FocusModel: ObservableObject {
    /// The span play takes for a selection, and the selection it was taken for.
    struct PlayRange: Equatable {
        var selection: ScoreSelection
        var range: Range<Double>
    }

    /// The version shown, for the phrase in focus with no variation open: what Version plays, its name, and the bars of
    /// the phrase it has otherwise or that were taken from it.
    struct Version: Equatable {
        var id: String
        var name: String
        /// The version's notes over the whole piece in the score's time, for the player's second source.
        var notes: [PlaybackNote]
        /// Whether any of them start in the phrase: only then can it be heard there.
        var sounds: Bool
        /// The bars of the phrase the version has otherwise, the first of which Open in the draft goes to.
        var differing: [Int]
    }

    /// The selection read: what plays, and the unit the card is.
    private let selection: SelectionModel

    /// Whether Expand is open (0018 section 4, #521 19): the selection lifted into a card, the page dimmed behind it.
    @Published private(set) var focused = false
    /// Where Expand was opened from, which names the way back (#521 17): the score's «К странице», the roll's «Свернуть».
    @Published private(set) var entry = ExpandEntry.notes
    /// Whether the card in focus shows the phrase as a roll (testing item 12): Notes | Piano roll in the focus bar, kept while
    /// the piece is open.
    @Published private(set) var asRoll = false
    /// What Play and Loop take for the selection: its span (Lead-in went in v0.5.6, #533 3).
    @Published private(set) var playRange: PlayRange?
    /// The version shown for the phrase in focus; nil out of focus or without a version.
    @Published private(set) var version: Version?
    /// Verovio's options for the card at its width now, as the page gives them.
    private(set) var cardOptions: String?
    /// The version's notes as music-core gives them to play (`playback` of `version`), for the version they are of.
    private var versionPlayback: (id: String, notes: [PlaybackNote])?

    init(selection: SelectionModel) {
        self.selection = selection
    }

    /// Expand opened, from where its way back leads.
    func open(from entry: ExpandEntry) {
        self.entry = entry
        focused = true
    }

    /// Back to the page: Expand closed, no version for a phrase in focus any more.
    func close() {
        focused = false
        version = nil
    }

    /// Notes | Piano roll on the card.
    func showAsRoll(_ roll: Bool) {
        asRoll = roll
    }

    /// The card's options at its width now, as the page gives them.
    func takeCardOptions(_ options: String) {
        cardOptions = options
    }

    /// The version for the phrase in focus, or none.
    func showVersion(_ version: Version?) {
        self.version = version
    }

    /// The notes of the version (`id`) in the piece in the score's time, as music-core gives them to play, kept until the
    /// notes change (`forgetVersionNotes`); nil when another version's were read meanwhile.
    func versionNotes(of id: String, in piece: Int32) async -> [PlaybackNote]? {
        if versionPlayback?.id != id {
            let request = facadeRequest(["of": "version", "id": id])
            let answer = await PieceCore(piece: piece).playback(request)
            versionPlayback = (id, answer.value.map(PlaybackScore.init)?.notes ?? [])
        }
        return versionPlayback?.id == id ? versionPlayback?.notes : nil
    }

    /// The notes changed, or another piece is shown: a version's notes are read again when next heard.
    func forgetVersionNotes() {
        versionPlayback = nil
    }

    /// What play takes for the selection now: its span; none without a selection.
    func updatePlayRange() {
        guard let selected = selection.selection else {
            playRange = nil
            return
        }
        playRange = PlayRange(selection: selected, range: selected.onset..<selected.end)
    }

    /// The phrase the card is, or that holds its start.
    var unit: PhraseUnit? {
        guard let selected = selection.selection, let phrases = selection.phrases else { return nil }
        return phrases.unit(selected: selected) ?? phrases.units.first { $0.onset <= selected.onset && selected.onset < $0.end }
    }

    var canStepBack: Bool { (unit?.number ?? 1) > 1 }

    var canStepOn: Bool { unit.map { $0.number < (selection.phrases?.units.count ?? 0) } ?? false }

    /// "bars 9–16 · phrase 3 of 16", or the bars alone for a passage that is not a unit.
    func position(in text: UIText) -> String {
        guard let selected = selection.selection else { return "" }
        let bars = selected.firstBar == selected.lastBar
            ? text.format("bars.one", String(selected.firstBar))
            : text.format("bars.range", String(selected.firstBar), String(selected.lastBar))
        guard let phrases = selection.phrases, let unit = phrases.unit(selected: selected) else { return bars }
        return text.format("focus.position", bars, unit.name, String(unit.number), String(phrases.units.count))
    }
}
