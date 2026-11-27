import SwiftUI

// The Insights sheet on the score's screen: it composes the stream (Insights/) with the selection it names, what sounds
// while a variation is there (the player's) and the assistant's foot, so it lives with the screen; the stream's state and
// rules are InsightsController's.

/// The Insights sheet (0018 section 6; design/kit/ds.css `.sheet` and `.fact`): the right third of the screen in landscape,
/// half the height from the bottom in portrait. Its head names the selection, and one stream lists what music-core
/// understood about it, grouped by lens, the one to hear first on top; filter chips narrow it without switching it. A
/// fact's claim rings its places on the score, Evidence opens in place, and its places can be ringed one at a time.
struct InsightsSheet: View {
    @ObservedObject var insights: InsightsController
    @ObservedObject var selectionModel: SelectionModel
    @ObservedObject var player: Player
    /// What loops while a variation is heard: the selection, to the variation's end.
    let range: Range<Double>?
    let text: UIText
    /// «Привязать заново» on the line «Границы фраз изменились» (#318); nil on a view only shown (E-insights-5).
    let reattach: (() -> Void)?
    let close: () -> Void
    @EnvironmentObject private var assistant: AssistantController

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-2")) {
                Text(heading)
                    .overline()
                    .padding(.top, CGFloat.ds("s-2"))
                    .frame(maxWidth: .infinity, alignment: .leading)
                Button(action: close) {
                    Icon(name: "close").frame(width: 32, height: 32)
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["drawer.close"])
            }
            .padding(.leading, CGFloat.ds("s-6"))
            .padding(.trailing, CGFloat.ds("s-4"))
            .padding(.top, CGFloat.ds("s-4"))
            if player.hasVariation, !player.secondIsVersion, let range {
                HearingSwitch(player: player, range: range, text: text)
                    .padding(.leading, CGFloat.ds("s-6"))
                    .padding(.top, CGFloat.ds("s-2"))
            }
            ScrollViewReader { reader in
                ScrollView {
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-6")) {
                        InsightsStream(
                            insights: insights, text: text, hasSelection: !(selectionModel.selection?.noteIds.isEmpty ?? true),
                            noNotes: selectionModel.selection?.noteIds.isEmpty ?? false, phrases: selectionModel.phrases, reattach: reattach
                        )
                        // The assistant is the stream's last voice: its foot leads to the assistant's panel (#582 item 17.1).
                        Button(text["padAssistant.open"] + " ›") { assistant.open(wholePiece: false, selection: selectionModel.selection) }
                            .dsType(.label)
                            .foregroundStyle(Color.ds("accent"))
                    }
                    .padding(.leading, CGFloat.ds("s-6"))
                    .padding(.trailing, CGFloat.ds("s-5"))
                    .padding(.vertical, CGFloat.ds("s-4"))
                    .id("top")
                }
                .onChange(of: insights.understanding) { _, _ in reader.scrollTo("top", anchor: .top) }
                // The sheet ignores the keyboard with the rest of the screen; its stream keeps clear of it (#582 item 16).
                .keyboardRoom()
            }
        }
        .background(Color.ds("surface"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["bar.insights"])
    }

    /// The selection named, as the roll's drawer names it: "bars 9–16".
    private var heading: String {
        guard let selection = selectionModel.selection else { return text["bar.insights"] }
        return barsText(selection.firstBar, selection.lastBar, text)
    }
}
