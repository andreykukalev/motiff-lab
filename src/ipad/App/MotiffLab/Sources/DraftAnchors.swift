import CoreGraphics
import Foundation

/// The anchors of a draft's view (v1.2, ADR 0034 section 2; the plan's answer 4): each anchor the player confirmed on a
/// variant («Не тот такт? Сопоставить…» on a приём's card), as music-core's `drafts.open` lists it, a small amber mark over
/// its bar (amber: what the player marked), and the mark picked, whose menu offers «Открепить». Everything here is
/// music-core's answer; the alignment around the anchors is music-core's. Owned by the draft view's `ScoreController`;
/// its state is set only through its methods.
@MainActor
final class DraftAnchorsModel: ObservableObject {
    /// An anchor of a variant of a place.
    struct Mark: Equatable {
        let place: String
        let variant: DraftViewAnswer.Variant
        let anchor: DraftViewAnswer.Anchor

        /// The page's key of its mark: the place, the variant and the base's tick.
        var key: String { "\(place)/\(variant.id)/\(anchor.base)" }
    }

    /// The mark picked: its menu shows at the mark (`at`, its bottom left on the page).
    struct Pick: Equatable {
        let mark: Mark
        let at: CGPoint
    }

    @Published private(set) var picked: Pick?

    /// Every anchor of every variant of `draft`'s places, in the places' order; none for a view that is no draft's.
    static func marks(_ draft: DraftViewAnswer.Draft?) -> [Mark] {
        (draft?.places ?? []).flatMap { place in
            place.variants.flatMap { variant in (variant.anchors ?? []).map { Mark(place: place.id, variant: variant, anchor: $0) } }
        }
    }

    /// An anchor's mark tapped (`key`, at its bottom left): its menu opens there. Nil for a key no anchor of `draft` has.
    @discardableResult
    func pick(_ key: String, in draft: DraftViewAnswer.Draft?, at point: CGPoint) -> Mark? {
        guard let mark = Self.marks(draft).first(where: { $0.key == key }) else { return nil }
        picked = Pick(mark: mark, at: point)
        return mark
    }

    /// The draft as music-core answered it again (a step, its Undo): the mark picked stays picked only while its anchor is
    /// still there.
    func keep(_ draft: DraftViewAnswer.Draft?) {
        guard let pick = picked else { return }
        picked = Self.marks(draft).first { $0.key == pick.mark.key }.map { Pick(mark: $0, at: pick.at) }
    }

    /// The mark under the menu shown moved on the page (`point`, its bottom left now): the menu goes with it. False when
    /// none shows.
    func moveCard(to point: CGPoint) -> Bool {
        guard let pick = picked else { return false }
        picked = Pick(mark: pick.mark, at: point)
        return true
    }

    /// The menu goes.
    func close() {
        picked = nil
    }

    // MARK: - Words

    /// The marks for the page, over each anchor's bar in the draft, none while `hidden` («Скрыть номера» leaves the draft to
    /// practise on): its key and what VoiceOver says of it.
    static func markers(_ draft: DraftViewAnswer.Draft?, hidden: Bool, _ text: UIText) -> [[String: Any]] {
        guard !hidden else { return [] }
        return marks(draft).map { mark -> [String: Any] in
            ["bar": mark.anchor.bar, "tick": mark.anchor.at, "text": "", "anchor": mark.key, "label": label(mark, text)]
        }
    }

    /// What VoiceOver says of an anchor's mark, and its menu's title: «Опорная точка в такте 5 · Ноты 1–4».
    static func label(_ mark: Mark, _ text: UIText) -> String {
        text.format("drafts.anchorLabel", String(mark.anchor.bar), mark.variant.label)
    }
}
