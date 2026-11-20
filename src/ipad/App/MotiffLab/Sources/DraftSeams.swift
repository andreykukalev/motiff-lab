import CoreGraphics
import Foundation

/// The seams of a draft's view (v1.1, ADR 0034 section 5 and section 9 step 7; spec «Коллаж», «Экран (iPad)»): music-core's
/// `drafts.seams`, a diamond where two places meet and something differs (key, meter or tempo; none where nothing does,
/// as the spec draws none), and the seam picked, whose card shows the two sides and what music-core offers. Everything
/// here is music-core's answer; nothing is changed by itself. Owned by the draft view's `ScoreController`, or by the
/// draft's map; its state is set only through its methods.
@MainActor
final class DraftSeamsModel: ObservableObject {
    /// The seam picked: its card shows at its diamond on the page (`at`, the diamond's bottom left), or on the draft's map
    /// under the diamond between two places (`at` nil).
    struct Pick: Equatable {
        let seam: DraftSeamsAnswer.Seam
        var at: CGPoint?
    }

    /// The draft's seams, in order; empty on a view that is no draft's.
    @Published private(set) var seams: [DraftSeamsAnswer.Seam] = []
    @Published private(set) var picked: Pick?

    /// The seams of `draft`, asked on `piece` (its view or the piece it belongs to); none for a view that is no draft's.
    /// The seam picked stays picked when it is still there.
    func load(_ draft: DraftViewAnswer.Draft?, on piece: Int32) async {
        guard let draft else { return keep([]) }
        keep(await PieceCore(piece: piece).draftSeams(draft.id).value?.seams ?? [])
    }

    /// The seams music-core answered.
    func keep(_ read: [DraftSeamsAnswer.Seam]) {
        seams = read
        picked = picked.flatMap { pick in seam(Self.key(pick.seam)).map { Pick(seam: $0, at: pick.at) } }
    }

    /// The seams that show a diamond: those where something differs.
    var shown: [DraftSeamsAnswer.Seam] { seams.filter { !$0.changes.isEmpty } }

    /// The seam before the place `place` (a place's seam is the boundary with the one before it), when it shows.
    func before(_ place: String) -> DraftSeamsAnswer.Seam? {
        shown.first { $0.after == place }
    }

    func seam(_ key: String) -> DraftSeamsAnswer.Seam? {
        shown.first { Self.key($0) == key }
    }

    /// A diamond tapped (`at` its bottom left on the page, nil on the map): that seam's card opens there. Nil for a key no
    /// seam that shows has.
    @discardableResult
    func pick(_ key: String, at point: CGPoint?) -> DraftSeamsAnswer.Seam? {
        guard let seam = seam(key) else { return nil }
        picked = Pick(seam: seam, at: point)
        return seam
    }

    /// The card goes.
    func close() {
        picked = nil
    }

    /// The page's key of a seam's diamond: the place after it.
    static func key(_ seam: DraftSeamsAnswer.Seam) -> String { seam.after }

    // MARK: - Words

    /// The diamonds for the page, at the first bar of the place after each seam that shows: its key, its state ("open", or
    /// "left" once kept as it is) and what VoiceOver says of it.
    func markers(_ text: UIText) -> [[String: Any]] {
        shown.map { seam -> [String: Any] in
            [
                "bar": seam.firstBar, "tick": seam.at, "text": "", "seam": Self.key(seam), "state": seam.left ? "left" : "open",
                "label": Self.label(seam, text),
            ]
        }
    }

    /// What VoiceOver says of a seam's diamond, on the page and on the map: «Стык тактов 4 и 5: тональность, темп».
    static func label(_ seam: DraftSeamsAnswer.Seam, _ text: UIText) -> String {
        text.format("drafts.seamLabel", String(seam.lastBar), String(seam.firstBar), changes(seam, text))
    }

    /// What differs at the seam, in words: «тональность, темп».
    static func changes(_ seam: DraftSeamsAnswer.Seam, _ text: UIText) -> String {
        seam.changes.map { change in
            switch change {
            case "key": text["drafts.seamKey"]
            case "meter": text["drafts.seamMeter"]
            default: text["drafts.seamTempo"]
            }
        }
        .joined(separator: ", ")
    }

    /// The card's title: «Стык · такты 4 и 5».
    static func title(_ seam: DraftSeamsAnswer.Seam, _ text: UIText) -> String {
        text.format("drafts.seamTitle", String(seam.lastBar), String(seam.firstBar))
    }
}
