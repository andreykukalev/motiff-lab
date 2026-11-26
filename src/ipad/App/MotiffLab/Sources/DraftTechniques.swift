import CoreGraphics
import Foundation

/// The приёмы of a draft's view (v1.1, ADR 0034 section 4 and section 9 step 6; spec «Экран (iPad)»): music-core's
/// `drafts.techniques` for each place of the draft shown, their numbers above the bars (plum: what the app composed), the
/// occurrence picked (its card at its number, or only the pill's current one), stepped through by the pill's ‹ ›, and the
/// numbers hidden for practice («Скрыть номера»). Everything here is music-core's answer: a приём's kind is the engine's
/// reading of the difference, shown as such, never as a fact. Owned by the draft view's `ScoreController`; its state is
/// set only through its methods.
@MainActor
final class DraftTechniquesModel: ObservableObject {
    /// One occurrence of a приём, where it stands in the draft.
    struct Spot: Equatable, Sendable {
        let draft: String
        let place: String
        /// The place's base variant, heard «Как в нотах».
        let base: String
        let technique: DraftTechniquesAnswer.Technique
        let occurrence: DraftTechniquesAnswer.Occurrence

        /// The page's key of its number: the place, the приём's number and the place's bar.
        var key: String { "\(place)/\(technique.number)/\(occurrence.bar)" }
        /// Where it is in the draft's ticks; empty for bars of a recording not yet put in (they would go there).
        var span: Range<Double> { Double(occurrence.onset)..<Double(max(occurrence.onset, occurrence.end)) }
        /// The variant heard on one side, the variant's (`variant`) or the base's, and the occurrence in its own ticks
        /// (c-4b); empty where it has no bars there (an inserted bar in the base).
        func heard(variant: Bool) -> (variant: String, span: Range<Double>) {
            let side = variant ? occurrence.inVariant : occurrence.inBase
            return (variant ? technique.variant : base, Double(side.onset)..<Double(max(side.onset, side.end)))
        }

        /// What the card offers here, as music-core takes it: the steps only while the occurrence is open (one taken or
        /// left is refused), a hand from the variant only over bars that hold notes (an inserted bar not yet put in has
        /// none), and «Взять везде · N» counts the occurrences still open.
        var offer: Offer {
            let open = occurrence.state == "open"
            return Offer(steps: open, hands: open && !span.isEmpty, everywhere: technique.occurrences.filter { $0.state == "open" }.count)
        }
    }

    /// The card's steps at an occurrence (`Spot.offer`).
    struct Offer: Equatable {
        /// «Взять везде», «Только здесь» and «Оставить».
        let steps: Bool
        /// «Правая рука из варианта», «Левая рука из варианта».
        let hands: Bool
        /// The N of «Взять везде · N».
        let everywhere: Int
    }

    /// The occurrence picked: its card shows at its number (`at`, the number's bottom left on the page); picked by the
    /// pill's ‹ ›, it is only the current one, and no card shows.
    struct Pick: Equatable {
        let spot: Spot
        var at: CGPoint?
    }

    /// The приёмы of each place, in the places' order; empty on a view that is no draft's.
    @Published private(set) var places: [DraftTechniquesAnswer] = []
    @Published private(set) var picked: Pick?
    /// «Скрыть номера»: practice on the draft, its numbers, card and stepping put away until shown again.
    @Published private(set) var numbersHidden = false

    /// The приёмы of every place of `draft`, asked on `piece` (the draft's view; music-core answers for the piece it was
    /// opened from); none for a view that is no draft's. The occurrence picked stays picked when it is still there.
    func load(_ draft: DraftViewAnswer.Draft?, on piece: Int32) async {
        var read: [DraftTechniquesAnswer] = []
        for place in draft?.places ?? [] {
            if let id = draft?.id, let answer = await PieceCore(piece: piece).draftTechniques(draft: id, place: place.id).value {
                read.append(answer)
            }
        }
        keep(read)
    }

    /// The приёмы music-core answered for each place, in the places' order.
    func keep(_ read: [DraftTechniquesAnswer]) {
        places = read
        picked = picked.flatMap { pick in spot(pick.spot.key).map { Pick(spot: $0, at: pick.at) } }
    }

    /// Every occurrence of every приём, in the order of the music, the lower number first in one place.
    var spots: [Spot] {
        places.flatMap { answer in
            answer.techniques.flatMap { technique in
                technique.occurrences.map { Spot(draft: answer.draft, place: answer.place, base: answer.base, technique: technique, occurrence: $0) }
            }
        }
        .sorted { ($0.occurrence.onset, $0.technique.number) < ($1.occurrence.onset, $1.technique.number) }
    }

    func spot(_ key: String) -> Spot? {
        spots.first { $0.key == key }
    }

    /// How many приёмы the draft has, and how many are taken.
    var total: Int { places.map(\.total).reduce(0, +) }
    var taken: Int { places.map(\.taken).reduce(0, +) }

    /// Whether the pill steps through the occurrences (‹ ›): there are some, and the numbers show. The playback pill's
    /// phrase steps give way to it meanwhile, so one pair of ‹ › is on the screen.
    var stepping: Bool { !numbersHidden && !spots.isEmpty }

    /// A number tapped (`at` its bottom left), or the pill's step (`at` nil): that occurrence is the one picked. Nil for a
    /// key no occurrence has, or while the numbers are hidden.
    @discardableResult
    func pick(_ key: String, at point: CGPoint?) -> Spot? {
        guard !numbersHidden, let spot = spot(key) else { return nil }
        picked = Pick(spot: spot, at: point)
        return spot
    }

    /// The pill's ‹ (-1) or › (1): the occurrence before or after the one picked, the first or the last with none, kept
    /// at the ends; its card does not show. Nil without приёмы or while the numbers are hidden.
    func step(_ direction: Int) -> Spot? {
        let all = spots
        guard !numbersHidden, !all.isEmpty else { return nil }
        let index = picked.flatMap { pick in all.firstIndex { $0.key == pick.spot.key } }
        let next = index.map { min(max($0 + direction, 0), all.count - 1) } ?? (direction < 0 ? all.count - 1 : 0)
        picked = Pick(spot: all[next], at: nil)
        return all[next]
    }

    /// The number under the card shown moved on the page (`point`, its bottom left now): the card goes with it; none moves
    /// while no card shows (false).
    func moveCard(to point: CGPoint) -> Bool {
        guard picked?.at != nil else { return false }
        picked?.at = point
        return true
    }

    /// The card goes; the occurrence stays the pill's current one.
    func closeCard() {
        picked?.at = nil
    }

    /// «Скрыть номера» or «Показать номера»: hidden, nothing is picked.
    func hideNumbers(_ hidden: Bool) {
        numbersHidden = hidden
        if hidden { picked = nil }
    }

    // MARK: - Words

    /// The numbers for the page, above each occurrence's first bar, none while hidden: plum labels with the приём's number,
    /// its key and state, and what VoiceOver says of it.
    func markers(_ text: UIText) -> [[String: Any]] {
        guard !numbersHidden else { return [] }
        return spots.map { spot -> [String: Any] in
            [
                "bar": spot.occurrence.firstBar, "tick": spot.occurrence.onset, "text": String(spot.technique.number),
                "label": text.format("drafts.techniqueLabel", String(spot.technique.number), spot.technique.label, String(spot.occurrence.firstBar)),
                "technique": spot.key, "state": spot.occurrence.state,
            ]
        }
    }

    /// The place the pill's «↻ Место» loops among the draft's `places`: the `picked` occurrence's, else the one at the
    /// playhead (`tick`), else the first; nil without places.
    static func place(among places: [DraftViewAnswer.Place], picked: Spot?, at tick: Double) -> DraftViewAnswer.Place? {
        places.first { $0.id == picked?.place } ?? places.first { Double($0.onset) <= tick && tick < Double($0.end) } ?? places.first
    }

    /// One side of `spot` as its own music, the variant's (`variant`) or the base's (`drafts.open` with "variant", c-4b,
    /// asked on `piece`, the draft's view or the piece it belongs to): opened read-only, its playback score read, closed
    /// again; heard over `Spot.heard(variant:)`'s span. Nil when music-core does not open it.
    static func playback(_ spot: Spot, variant: Bool, on piece: Int32) async -> PlaybackAnswer? {
        await playback(of: spot.heard(variant: variant).variant, in: spot.draft, on: piece)
    }

    /// A variant's own music (`drafts.open` with "variant"): opened read-only on `piece`, its playback score read (its
    /// notes and bars in its own ticks), closed again; a bridge's candidate heard, a variant's bars to anchor to (v1.2).
    static func playback(of variant: String, in draft: String, on piece: Int32) async -> PlaybackAnswer? {
        guard let view = await PieceCore(piece: piece).openVariant(variant, of: draft).value else { return nil }
        let playback = await PieceCore(piece: view.piece).playback().value
        _ = await MusicCore.run { MusicCore.close(piece: view.piece) }
        return playback
    }

    /// The приём's name with its number: «Приём 2 · октава».
    static func title(_ spot: Spot, _ text: UIText) -> String {
        text.format("drafts.techniqueTitle", String(spot.technique.number), spot.technique.label)
    }

    /// How sure music-core is of the occurrence, how many of its notes are in place, and what was done with it:
    /// «уверенно · 3 из 4 нот на месте · взят».
    static func status(_ spot: Spot, _ text: UIText) -> String {
        let matched = SourceLayers.counted("drafts.matched", spot.occurrence.total, String(spot.occurrence.matched), text: text)
        let sure = text[spot.technique.sure ? "drafts.sure" : "drafts.unsure"] + " · " + matched
        switch spot.occurrence.state {
        case "taken": return sure + " · " + text["drafts.stateTaken"]
        case "left": return sure + " · " + text["drafts.stateLeft"]
        default: return sure
        }
    }
}
