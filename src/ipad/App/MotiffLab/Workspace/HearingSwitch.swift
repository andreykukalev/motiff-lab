import SwiftUI

// What sounds while a variation or another version is there: Expand's bar (FocusBar) and the Insights sheet show it over
// the player. A view only; what each choice plays is the player's (`Player.hear`).

/// What sounds while a variation is there (0018 section 7; ds.css `.seg`): the original, the variation, or the two
/// looping one after the other.
struct HearingSwitch: View {
    @ObservedObject var player: Player
    let range: Range<Double>
    let text: UIText
    /// Another version's name when it stands in the variation's place (0022, section 5): Notes / Version / Alternate.
    var version: String?

    var body: some View {
        HStack(spacing: 0) {
            if let version {
                option(.original, "focus.notes", hint: text["compare.notesHint"])
                option(.variation, "focus.versionHear", hint: text.format("compare.versionHint", version))
                option(.alternate, "focus.alternate", hint: text.format("compare.alternateVersionHint", version))
            } else {
                option(.original, "focus.original", hint: text["compare.originalHint"])
                option(.variation, "focus.variation", hint: text["compare.variationHint"])
                option(.alternate, "focus.alternate", hint: text["compare.alternateHint"])
            }
        }
        .padding(2)
        .frame(height: CGFloat.ds("h-compact"))
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["focus.listen"])
    }

    private func option(_ hearing: Player.Hearing, _ key: String, hint: String) -> some View {
        let on = player.hearing == hearing
        return Button {
            player.hear(hearing, over: range)
        } label: {
            SteadyWord(text: text[key], on: on)
                .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                .modifier(SegmentFill(on: on))
        }
        .buttonStyle(.plain)
        .accessibilityHint(hint)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}
