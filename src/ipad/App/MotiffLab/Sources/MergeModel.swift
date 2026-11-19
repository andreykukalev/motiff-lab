import Foundation

/// The merge draft on the score (ADR 0031 section 3, S4b; testing items 32–34): the draft music-core answers, the change
/// it shows, whether a choice goes for its bar or its phrase, what the score shows and what was last heard of it, the
/// Compare versions window, the toast after a merge applied or undone or a version removed, and a version's marker tapped
/// on the score. It owns this state and the rules of its own that need only the selection (SelectionModel) and the
/// versions (SourcesModel), which it reads: the bars a choice goes for, the span heard, the change ‹ › go to, the bars
/// Compare versions shows, and one `performance.merge` call. Engraving the draft's view, hearing it (its notes are
/// Playback's), drawing it on the page and what follows a merge (the notes, Check, the version's words) are
/// ScoreController's, the composition root, which hands in what they give; its observers see each change of this model
/// as a change of the controller. Until which step: ADR 0031, the note of 2026-11-18 (S4b).
@MainActor
final class MergeModel: ObservableObject {
    /// What was just done with a version, with the way to undo it.
    struct Toast: Equatable {
        enum Undo: Equatable { case unapply, reapply, putBack }
        var text: String
        var undo: Undo?
    }

    /// A version's marker tapped on the score, and where it is on the screen, in the web view's points: the popover goes
    /// under it.
    struct MarkerPick: Equatable {
        var bar: Int
        var left: Double
        var bottom: Double
    }

    /// The phrases read: the bars a choice and what is heard take with Phrase.
    private let selection: SelectionModel
    /// The versions read: the bars' spans.
    private let sources: SourcesModel

    /// The merge draft open now, as music-core answers `performance.merge`; nil with none.
    @Published private(set) var draft: MergeInfo?
    /// The change the draft shows, by its bar.
    @Published private(set) var at: Int?
    /// Whether a choice in the draft goes for the change's bar or every change of its phrase.
    @Published private(set) var phrase = false
    /// What the score shows while a draft is open: "mine", "version" or "result".
    @Published private(set) var view = "result"
    /// What was last asked to be heard of the draft: "mine", "version", "result" or "alternate".
    @Published private(set) var hearing: String?
    /// The full-screen Compare versions window over the draft.
    @Published private(set) var comparing = false
    /// What was just done with a version, with the way to undo it: a merge applied or undone, a version removed.
    @Published private(set) var toast: Toast?
    /// A version's marker tapped on the score: the popover under it says how the bar differs (testing item 33).
    @Published private(set) var markerPick: MarkerPick?

    init(selection: SelectionModel, sources: SourcesModel) {
        self.selection = selection
        self.sources = sources
    }

    /// Another piece shown: no draft, change, toast, marker or Compare versions of the last one's.
    func open() {
        draft = nil
        at = nil
        toast = nil
        markerPick = nil
        comparing = false
    }

    /// Bar or Phrase, for the choices and what is heard.
    func scope(phrase: Bool) {
        self.phrase = phrase
    }

    /// Compare versions opened or closed.
    func compare(_ comparing: Bool) {
        self.comparing = comparing
    }

    /// Mine, Version or Result on the score.
    func showView(_ view: String) {
        self.view = view
    }

    /// What is heard of the draft now, or nothing.
    func hear(_ view: String?) {
        hearing = view
    }

    /// The toast after something done with a version, or none.
    func say(_ toast: Toast?) {
        self.toast = toast
    }

    /// A version's marker tapped, or the popover closed.
    func pickMarker(_ pick: MarkerPick?) {
        markerPick = pick
    }

    /// The change the draft shows, by its bar.
    func go(to bar: Int?) {
        at = bar
    }

    /// The draft gone: no change shown or heard, Compare versions closed, the result shown when the next one opens.
    func end() {
        draft = nil
        at = nil
        hearing = nil
        comparing = false
        view = "result"
    }

    /// One `performance.merge` call in the piece: the draft as it answers, its change shown kept while the draft has it.
    /// Returns music-core's refusal as the player reads it, nil for a draft read.
    func call(_ call: PieceCore.Merge, in piece: Int32, _ fields: [String: Any] = [:]) async -> String? {
        let request = facadeRequest(fields)
        let answer = await PieceCore(piece: piece).merge(call, request)
        // A refusal, or a draft that cannot be read: "merge" is null only with none open.
        guard let read = answer.value else { return FacadeAnswer.problem(answer.text) }
        draft = read.merge.map(MergeInfo.init)
        if let draft, let at, !draft.bars.contains(where: { $0.bar == at }) { self.at = draft.bars.first?.bar }
        return nil
    }

    /// The change ‹ › go to from the one shown, within the draft's first and last; nil with no change.
    func stepped(by: Int) -> Int? {
        guard let draft, !draft.bars.isEmpty else { return nil }
        let bars = draft.bars.map(\.bar)
        let index = at.flatMap { bars.firstIndex(of: $0) } ?? (by > 0 ? -1 : bars.count)
        return bars[min(max(index + by, 0), bars.count - 1)]
    }

    /// The bars a choice at a bar goes for: the bar, or with Phrase every change of its phrase.
    func choiceBars(_ bar: Int) -> [Int] {
        guard phrase, let draft, let unit = selection.phrases?.units.first(where: { $0.firstBar <= bar && bar <= $0.lastBar }) else { return [bar] }
        let bars = draft.bars.map(\.bar).filter { unit.firstBar <= $0 && $0 <= unit.lastBar }
        return bars.isEmpty ? [bar] : bars
    }

    /// The span heard: the change's bar, or its phrase with Phrase.
    var hearSpan: Range<Double>? {
        guard let at else { return nil }
        let bars = choiceBars(at)
        let spans = sources.versions.bars.filter { bars.contains($0.bar) || $0.bar == at }.compactMap(\.span)
        guard let first = spans.map(\.lowerBound).min(), let last = spans.map(\.upperBound).max() else { return nil }
        if phrase, let unit = selection.phrases?.units.first(where: { $0.firstBar <= at && at <= $0.lastBar }) { return unit.onset..<unit.end }
        return first..<last
    }

    /// The bars Compare versions shows round a change in the piece: its phrase or sentence, or a bar either side.
    func seenBars(in piece: Int32, _ bar: Int, _ see: String) async -> (Int, Int) {
        if see == "phrase" || see == "sentence" {
            let request = facadeRequest(["level": see])
            let answer = await MusicCore.run { MusicCore.call(piece: piece, "phrases", request) }
            if let unit = PhraseLevelsShown(answer: answer)?.units.first(where: { $0.firstBar <= bar && bar <= $0.lastBar }) {
                return (unit.firstBar, unit.lastBar)
            }
        }
        let numbers = sources.versions.bars.map(\.bar)
        return (max(numbers.min() ?? bar, bar - 1), min(numbers.max() ?? bar, bar + 1))
    }
}
