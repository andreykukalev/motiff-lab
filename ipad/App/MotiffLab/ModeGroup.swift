import SwiftUI

/// The modes in the bar (version 0.5, the experiment Two playheads and modes; the web app's Score/ModeGroup.razor): Navigate,
/// Select, Edit notes and Pen, the chosen one named, and H, S, E and P on a keyboard. The Pen is the bar's Pen being on.
struct ModeGroup: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        HStack(spacing: 2) {
            ForEach(InteractionMode.allCases, id: \.self) { mode in
                button(mode)
            }
        }
        .padding(2)
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["mode.label"])
    }

    private var chosen: InteractionMode? { score.penOn ? .pen : score.mode }

    private func button(_ mode: InteractionMode) -> some View {
        let on = chosen == mode
        return Button {
            choose(mode)
        } label: {
            HStack(spacing: 6) {
                Icon(name: mode.icon, size: CGFloat.ds("icon-l"))
                if on {
                    Text(text[mode.label]).dsType(.label)
                }
            }
            .padding(.horizontal, on ? CGFloat.ds("s-2") : 0)
            .frame(minWidth: 40, minHeight: 40)
        }
        .buttonStyle(BarActionStyle(on: on))
        .keyboardShortcut(KeyEquivalent(mode.key), modifiers: [])
        .accessibilityLabel(text[mode.label])
        .accessibilityHint(text[mode.hint])
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    private func choose(_ mode: InteractionMode) {
        if mode == .pen {
            score.penOn = true
        } else {
            score.mode = mode
            score.penOn = false
        }
    }
}
