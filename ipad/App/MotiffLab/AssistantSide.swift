import SwiftUI

extension ScoreScreen {
    /// The full assistant's width beside the score: the Insights sheet's in landscape, the Contents drawer's in portrait,
    /// so the score keeps most of a portrait screen.
    static func assistantWidth(landscape: Bool) -> CGFloat {
        landscape ? sheetWidth : contentsWidth
    }
}

/// The full assistant's panel where it goes (#582 item 17.1): at the right of the screen under the bar, above the roll's
/// drawer when it is open, the score narrowed beside it (ScoreScreen.layers), as a sheet at the side is drawn.
struct AssistantSide: View {
    @ObservedObject var assistant: AssistantController
    let text: UIText
    let selection: String?
    let landscape: Bool
    /// The roll's drawer's height when it is open (0 when not): the panel stays above it.
    let drawer: CGFloat
    let ask: (String) -> Void

    var body: some View {
        let gap = CGFloat.ds("s-3")
        AssistantPanel(assistant: assistant, text: text, selection: selection, ask: ask)
            .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            .dsShadow("shadow-sheet")
            .frame(width: ScoreScreen.assistantWidth(landscape: landscape))
            .padding(.top, gap + CGFloat.ds("h-touch") + CGFloat.ds("s-2"))
            .padding(.trailing, gap)
            .padding(.bottom, drawer > 0 ? drawer + gap : gap)
            .transition(.move(edge: .trailing).combined(with: .opacity))
    }

    /// Whether the assistant is on (Settings · Experimental «Разговор с помощником»).
    @MainActor static func on(_ settings: DeviceSettings) -> Bool {
        settings.isOn(AssistantController.setting)
    }

    /// The role a new conversation takes: the proposer, which may make drafts, with «Помощник правит ноты» on.
    @MainActor static func role(_ settings: DeviceSettings) -> String {
        settings.isOn(AssistantController.actionsSetting) ? "proposer" : "mentor"
    }
}
