import SwiftUI

extension ScoreScreen {
    /// The full assistant's width beside the score: the Insights sheet's in landscape, the Contents drawer's in portrait,
    /// so the score keeps most of a portrait screen.
    static func assistantWidth(landscape: Bool) -> CGFloat {
        landscape ? sheetWidth : contentsWidth
    }
}

/// The full assistant's panel where it goes (#582 item 17.1): at the right of the page between the strips (v1.7 e-1), above
/// the roll's drawer when it is open, the score narrowed beside it (ScoreScreen.layers), as a sheet at the side is drawn.
struct AssistantSide: View {
    @ObservedObject var assistant: AssistantController
    let text: UIText
    /// The selection, and its chip's words ("This selection · bars 3–4"); nil for none.
    let selection: ScoreSelection?
    let label: String?
    let landscape: Bool
    /// The roll's drawer's height when it is open (0 when not): the panel stays above it.
    let drawer: CGFloat
    let ask: (String) -> Void
    let practise: (AssistantPracticeOffer) -> Void

    var body: some View {
        let gap = CGFloat.ds("s-3")
        AssistantPanel(assistant: assistant, text: text, selection: selection, label: label, ask: ask, practise: practise)
            .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            .dsShadow("shadow-sheet")
            .frame(width: ScoreScreen.assistantWidth(landscape: landscape))
            .padding(.top, gap)
            .padding(.trailing, gap)
            .padding(.bottom, drawer > 0 ? drawer + gap : gap)
            .transition(.move(edge: .trailing).combined(with: .opacity))
    }

    /// The role a new conversation takes: the proposer, which explains and may make drafts («Помощник правит ноты», #582
    /// item 17.3; the only role on the iPad since v0.6.5).
    @MainActor static func role(_ settings: DeviceSettings) -> String {
        "proposer"
    }
}
