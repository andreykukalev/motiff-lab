import Foundation

/// What Expand needs of the score it lifts a passage from (ADR 0031 section 3, v1.6 i-10b), as `PenScore`, `CheckScore`
/// and `SourcesScore`: the page and the piece, whether the pages are drawn and the Pen is on, the version shown on the
/// score (Sources', handed in as values), the page held still under the veil, a passage selected, a tick brought into
/// view, and the problem line. ScoreController, the composition root, is it.
@MainActor
protocol FocusScore: AnyObject {
    /// The score's page, the one way to its functions (`PageCommand`).
    var scorePage: ScorePage { get }
    /// The piece open in music-core; nil while none is.
    var pieceId: Int32? { get }
    /// Whether the pages are drawn, so the card can open on them.
    var shown: Bool { get }
    /// Whether the bar's Pen is on: Expand does not open then (`ExpandRules.opens`).
    var penOn: Bool { get }
    /// The other version shown on the score (`VersionShelf.shown`); nil for none.
    var versionShown: VersionEntry? { get }
    /// The bars the version shown has otherwise and that are still to look at (`VersionShelf.differing`).
    var versionDiffering: [Int] { get }
    /// The page under the veil held still, or let go: it neither scrolls nor takes the Pencil while still.
    func holdPage(still: Bool)
    /// A passage selected through music-core, as a tap selects it.
    func selectPassage(from start: Double, to end: Double) async
    /// The page scrolled smoothly to where a tick is.
    func reveal(_ tick: Double) async
    /// What went wrong, in the screen's problem line; nil for nothing.
    func showProblem(_ message: String?)
}

/// Expand and what plays for the selection (ADR 0031 section 3, S3): where Expand was opened from (#521 17), whether the
/// card shows the phrase as a roll (testing item 12), the card's options as the page gives them, the variation tried as
/// the card's ossia, the version shown for the phrase in focus (0022 section 5), and the span Play and Loop take for the
/// selection (#533 3). It owns this state and the rules of its own that need only the selection (SelectionModel, which it
/// reads): the play range, the phrase the card is, and the card's words for where it is; since S4b the version's notes to
/// hear, read for the version id the root hands in; since S7c whether Expand is open (`focused`); and since v1.6 i-10b
/// opening and leaving it on the page, the previous or the next phrase, the card engraved and the version in focus,
/// through the score it is given (`FocusScore`), as CheckModel does. The views observe this model. ADR 0031, the notes of
/// 2026-11-18 (S3, S4b, S7c) and v1.6 i-10b.
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
    /// The name of the variation being tried, which the card in focus shows as an ossia above the phrase; nil for none.
    private var ossia: String?
    /// The version's notes as music-core gives them to play (`playback` of `version`), for the version they are of.
    private var versionPlayback: (id: String, notes: [PlaybackNote])?
    /// The score Expand lifts its passages from, set by the root once it is made.
    weak var score: FocusScore?
    private let text = UIText.current

    init(selection: SelectionModel) {
        self.selection = selection
    }

    /// Notes | Piano roll on the card.
    func showAsRoll(_ roll: Bool) {
        asRoll = roll
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
        return phrases.unit(selected: selected) ?? phrases.unit(at: selected.onset)
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

    // MARK: - On the page (v1.6 i-10b, from ScoreController)

    /// Lifts the selection into focus (Expand, #521 19): the page dims, the card shows the selection's bars on their own, and
    /// the page neither scrolls nor takes the Pencil until the way back, which `entry` names (#521 17).
    func enter(from entry: ExpandEntry = .notes) {
        guard let score, score.shown, ExpandRules.opens(selected: selection.selection != nil, penOn: score.penOn), !focused else { return }
        self.entry = entry
        focused = true
        score.holdPage(still: true)
        let label = text.format("focus.cardLabel", position(in: text))
        let request: [String: Any] = ["open": true, "label": label, "back": text[entry.backKey]]
        Task {
            let answer = try? await score.scorePage.call(.focus, request) as? [String: Any]
            if let options = answer?["options"] as? [String: Any] { cardOptions = facadeRequest(options) }
            updatePlayRange()
            await engraveCard()
            await updateVersion()
        }
    }

    /// Expand on the roll drawer: the selection in focus as a roll, its way back Collapse (#521 17). Only with a selection
    /// (#521 19.1): without one the button says so instead (`showNotice`).
    func expandRoll() {
        guard selection.selection != nil else { return }
        showAsRoll(true)
        enter(from: .roll)
    }

    /// Back to the page, with the selection as it is: Back, a tap on the dimmed page, or Escape. No version for a phrase in
    /// focus any more.
    func leave() {
        guard focused else { return }
        focused = false
        version = nil
        score?.holdPage(still: false)
        Task {
            await score?.scorePage.send(.focus, ["open": false])
            updatePlayRange()
        }
    }

    /// The previous or the next phrase: the card moves on to it. Outside focus (the pill's arrows, #328) the page brings it
    /// into view.
    func step(_ direction: Int) {
        guard let score, let unit, let next = selection.phrases?.unit(unit.number + direction.signum()) else { return }
        Task {
            await score.selectPassage(from: next.selectableOnset, to: next.selectableEnd)
            if !focused { await score.reveal(next.selectableOnset) }
        }
    }

    /// The variation being tried shown as the card's ossia, or none: the card in focus is engraved again when it changed.
    func showOssia(_ name: String?) {
        guard name != ossia else { return }
        ossia = name
        if focused { Task { await engraveCard() } }
    }

    /// The card's options at its width now, as the page gives them, and the card engraved again at that width.
    func engraveCard(options: String) {
        cardOptions = options
        Task { await engraveCard() }
    }

    /// Engraves the selection's bars at the card's width and shows them on the card.
    func engraveCard() async {
        guard focused, let score, let piece = score.pieceId, let selected = selection.selection, let options = cardOptions else { return }
        let first = selected.firstBar
        let last = selected.lastBar
        let name = ossia
        let withOssia = name != nil
        // Without a variation, another version of the piece stands above as the performer has it (0022, section 5).
        let other = withOssia ? nil : score.versionShown
        let engraved = await MusicCore.run {
            Engraver.engraveCard(piece: piece, firstBar: first, lastBar: last, options: options, ossia: withOssia, version: other?.id)
        }
        // Stepped on, left, or the variation or the version changed while it engraved: the newer call shows its own.
        guard focused, selection.selection == selected, ossia == name, withOssia || score.versionShown == other else { return }
        switch engraved {
        case .success(let card):
            let timemap = (try? JSONSerialization.jsonObject(with: Data(card.timemap.utf8))) ?? []
            let label = name.map { text.format("focus.ossia", $0) } ?? other.map { text.format("focus.version", $0.name) } ?? ""
            let ossias = card.ossias.map { ["staves": $0.staves, "prefix": $0.prefix, "label": label, "added": $0.added] as [String: Any] }
            await score.scorePage.send(.card, ["pages": card.pages, "timemap": timemap, "start": selected.onset, "ossias": ossias])
        case .failure(let error):
            score.showProblem(error.message)
        }
    }

    /// The version for the phrase in focus: its notes to hear, and the bars of the phrase it has otherwise.
    func updateVersion() async {
        guard focused, let score, let piece = score.pieceId, let selected = selection.selection, let shown = score.versionShown else {
            version = nil
            return
        }
        guard let notes = await versionNotes(of: shown.id, in: piece), focused, selection.selection == selected, score.versionShown?.id == shown.id else { return }
        let bars = selected.firstBar...selected.lastBar
        version = Version(
            id: shown.id, name: shown.name, notes: notes,
            sounds: notes.contains { $0.onset >= selected.onset && $0.onset < selected.end },
            differing: score.versionDiffering.filter { bars.contains($0) }
        )
    }
}
