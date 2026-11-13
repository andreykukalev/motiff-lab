import SwiftUI

/// The modes in the bar (version 0.5, the experiment Two playheads and modes; the web app's Score/ModeGroup.razor; the
/// approved mockup v0.5.2, #479 item 21): Navigate, Select, Edit notes and Pen in one capsule, the chosen one with its
/// icon and its name, the others with the icon alone, and H, S, E and P on a keyboard. No caption over it.
struct ModeCapsule: View {
    let chosen: InteractionMode
    let text: UIText
    /// The modes this view does not have (Select and Edit notes on a PDF's pages), shown and not chosen.
    var unavailable: Set<InteractionMode> = []
    let choose: (InteractionMode) -> Void

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

    private func button(_ mode: InteractionMode) -> some View {
        let on = chosen == mode
        let available = !unavailable.contains(mode)
        return Button {
            choose(mode)
        } label: {
            HStack(spacing: 6) {
                Icon(name: mode.icon, size: CGFloat.ds("icon-l"))
                if on {
                    Text(text[mode.label]).dsType(.label).lineLimit(1).fixedSize()
                }
            }
            .padding(.horizontal, on ? CGFloat.ds("s-2") : 0)
            .frame(minWidth: 40, minHeight: 40)
        }
        .buttonStyle(BarActionStyle(on: on))
        .disabled(!available)
        .keyboardShortcut(available ? KeyboardShortcut(KeyEquivalent(mode.key), modifiers: []) : nil)
        .accessibilityLabel(text[mode.label])
        .accessibilityHint(text[available ? Self.hint(mode) : "modePad.pdfOnly"])
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    /// What the mode does on the iPad, where the stylus follows the tool (#479 item 18); the web's hints speak of a mouse.
    static func hint(_ mode: InteractionMode) -> String {
        "modePad.\(mode.rawValue)Hint"
    }
}

/// The score's modes: the Pen is the bar's Pen being on.
struct ModeGroup: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        ModeCapsule(chosen: score.penOn ? .pen : score.mode ?? .navigate, text: text) { mode in
            if mode == .pen {
                score.penOn = true
            } else {
                score.mode = mode
                score.penOn = false
            }
        }
    }
}
