import SwiftUI

/// A practice action the assistant suggested for a question (v1.2, assistant option A item 6: music-core's `practice.suggest`,
/// a tool of kind "suggestAction"): the bars, the hand, the tempo and whether to loop, with its button's words in the
/// engine's language («Зациклить такты 3–4 в темпе 60 % · левая рука»). Nothing changes until the player taps it; then the
/// app's own loop does it (Workspace's `ScoreController.practise`).
struct AssistantPracticeOffer: Codable, Equatable, Sendable {
    var first: Int
    var last: Int
    /// "right", "left" or "both", as music-core writes it.
    var hand: String
    /// The tempo, 25 % to 150 % of the piece's.
    var tempo: Int
    var loop: Bool
    var label: String

    /// The actions the question's tool calls suggested, in the order they came; a refused call suggests none.
    static func offers(in chat: AssistantChat) -> [AssistantPracticeOffer] {
        chat.resultsInQuestion.compactMap { offer($0.content) }
    }

    static func offer(_ content: JSONValue) -> AssistantPracticeOffer? {
        guard content["kind"]?.string == "suggestAction", content["ok"]?.isTrue ?? false, let result = content["result"],
              let action = result["action"], let label = result["label"]?.string,
              let first = action["bars"]?["from"]?.int, let last = action["bars"]?["to"]?.int, first <= last,
              let tempo = action["tempo"]?.int
        else { return nil }
        return AssistantPracticeOffer(
            first: first, last: last, hand: action["hand"]?.string ?? "both", tempo: tempo, loop: action["loop"]?.isTrue ?? false, label: label
        )
    }
}

/// An answer's action chip (0018 «Answers that act»): the suggested practice in the engine's words, which a tap sets on
/// the score and the transport behind the conversation.
struct AssistantPracticeChip: View {
    let offer: AssistantPracticeOffer
    let practise: () -> Void

    var body: some View {
        Button(action: practise) {
            HStack(spacing: CGFloat.ds("s-1")) {
                Icon(name: "loop", size: CGFloat.ds("icon-s"))
                Text(offer.label).lineLimit(2).multilineTextAlignment(.leading)
            }
        }
        .buttonStyle(DSChipStyle())
        .accessibilityLabel(offer.label)
    }
}
