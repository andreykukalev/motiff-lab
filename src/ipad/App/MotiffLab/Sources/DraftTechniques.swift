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
        let technique: DraftTechniquesAnswer.Technique
        let occurrence: DraftTechniquesAnswer.Occurrence

        /// The page's key of its number: the place, the приём's number and the place's bar.
        var key: String { "\(place)/\(technique.number)/\(occurrence.bar)" }
        /// Where it is in the draft's ticks; empty for bars of a recording not yet put in (they would go there).
        var span: Range<Double> { Double(occurrence.onset)..<Double(max(occurrence.onset, occurrence.end)) }
        var isTaken: Bool { occurrence.state == "taken" }
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
                technique.occurrences.map { Spot(draft: answer.draft, place: answer.place, technique: technique, occurrence: $0) }
            }
        }
        .sorted { ($0.occurrence.onset, $0.technique.number) < ($1.occurrence.onset, $1.technique.number) }
    }

    func spot(_ key: String) -> Spot? {
        spots.first { $0.key == key }
    }

    /// How many приёмы the draft has, and how many are taken (the Sources caption's «взято N приёмов из M»).
    var total: Int { places.map(\.total).reduce(0, +) }
    var taken: Int { places.map(\.taken).reduce(0, +) }

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
        return spots.map { spot in
            [
                "bar": spot.occurrence.firstBar, "tick": spot.occurrence.onset, "text": String(spot.technique.number),
                "label": text.format("drafts.techniqueLabel", String(spot.technique.number), spot.technique.label, String(spot.occurrence.firstBar)),
                "technique": spot.key, "state": spot.occurrence.state,
            ]
        }
    }

    /// The приём's name with its number: «Приём 2 · октава».
    static func title(_ spot: Spot, _ text: UIText) -> String {
        text.format("drafts.techniqueTitle", String(spot.technique.number), spot.technique.label)
    }

    /// How sure music-core is of the occurrence, and what was done with it: «уверенно · 90 %», «взят».
    static func status(_ spot: Spot, _ text: UIText) -> String {
        let sure = text.format(spot.technique.sure ? "drafts.sure" : "drafts.unsure", String(spot.occurrence.confidence))
        switch spot.occurrence.state {
        case "taken": return sure + " · " + text["drafts.stateTaken"]
        case "left": return sure + " · " + text["drafts.stateLeft"]
        default: return sure
        }
    }
}
