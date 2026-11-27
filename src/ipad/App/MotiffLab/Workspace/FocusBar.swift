import SwiftUI

/// The focus bar (0018 section 4, the web app's focus bar): Back to the page with its word, where the phrase is, Notes |
/// Piano roll and Ask; no Play (#511 section 8.1: the pill's is the one). Escape on a keyboard goes back too. While a
/// variation is tried, Original / Variation / Alternate choose what sounds; with another version over the phrase and no
/// variation, Notes / Version / Alternate, and Take into the score or Restore these notes for the phrase's bars (0022,
/// section 5). Reading / As played comes with transcriptions on the iPad.
struct FocusBar: View {
    @ObservedObject var score: ScoreController
    /// The card's position names the selection, from both models.
    @ObservedObject var selectionModel: SelectionModel
    @ObservedObject var focusModel: FocusModel
    @ObservedObject var player: Player
    let variationRange: Range<Double>?
    let text: UIText
    @EnvironmentObject private var assistant: AssistantController

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            // The way back by the way in (#521 17): «К странице» from the score, «Свернуть» from the roll; Escape too.
            let entry = focusModel.entry
            Button {
                score.leaveFocus()
            } label: {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: entry.backIcon, size: CGFloat.ds("icon-l"))
                    Text(text[entry.backKey]).dsType(.label)
                }
                .padding(.leading, CGFloat.ds("s-1"))
                .padding(.trailing, CGFloat.ds("s-3"))
                .frame(height: CGFloat.ds("h-control"))
            }
            .buttonStyle(BarActionStyle())
            .keyboardShortcut(.cancelAction)
            .help(text[entry.backHintKey])
            .accessibilityHint(text[entry.backHintKey])
            Text(focusModel.position(in: text))
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
                .monospacedDigit()
                .lineLimit(1)
                .truncationMode(.tail)
            FocusShowAs(score: score, focusModel: focusModel, text: text)
            Spacer(minLength: CGFloat.ds("s-2"))
            if player.hasVariation, let variationRange {
                HearingSwitch(player: player, range: variationRange, text: text, version: player.secondIsVersion ? focusModel.version?.name : nil)
            }
            Button {
                // The assistant's panel opens on the phrase in focus (#582 item 17.1).
                assistant.open(wholePiece: false, selection: selectionModel.selection)
            } label: {
                Text(text["ask.menu"]).dsType(.label).padding(.horizontal, CGFloat.ds("s-2")).frame(height: 40)
            }
            .buttonStyle(BarActionStyle(on: assistant.panelOpen))
            // No Play here (#511 section 8.1): the pill's blue Play is the one, and the pill always shows in focus.
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
    }
}

/// Notes | Piano roll in the focus bar (testing item 12), drawn as the hearing switch is.
struct FocusShowAs: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var focusModel: FocusModel
    let text: UIText

    var body: some View {
        HStack(spacing: 0) {
            option(false, "focus.asNotes")
            option(true, "focus.asRoll")
        }
        .padding(2)
        .frame(height: CGFloat.ds("h-compact"))
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["focus.showAs"])
    }

    private func option(_ roll: Bool, _ key: String) -> some View {
        let on = focusModel.asRoll == roll
        return Button {
            focusModel.showAsRoll(roll)
        } label: {
            SteadyWord(text: text[key], on: on)
                .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                .modifier(SegmentFill(on: on))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// The phrase as a roll in focus (testing item 12): a card over the page's card, as wide and as tall as the stage between the bar
/// and the pill, with the roll inside and the zoom on each axis floating over the notes. Expand inspects the passage (#521
/// 19.3): no mode switch and no note editor here, no arrows to another phrase; a draft of note edits waits in the drawer.
struct FocusRollCard: View {
    @ObservedObject var roll: RollController
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        RollSurface(roll: roll, place: .focus, text: text)
            .padding(CGFloat.ds("s-3"))
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("accent"), lineWidth: 2))
            .dsShadow("shadow-sheet")
            // The stage's insets, as Web/score.css `#focus .stage` has them.
            .padding(.horizontal, CGFloat.ds("s-4"))
            .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-5"))
            .padding(.bottom, 52 + CGFloat.ds("s-4") * 2)
    }
}
