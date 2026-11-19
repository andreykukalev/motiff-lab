import SwiftUI

/// Piece DNA's cards, one per idea with its Hear, In the score and In Insights.
extension DnaScreen {
    var cards: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 260), spacing: CGFloat.ds("s-4"))], alignment: .leading, spacing: CGFloat.ds("s-4")) {
            ForEach(map.ideas) { idea in
                VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                    Text(idea.title).dsType(.heading)
                    Text(idea.summary).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
                    FlowRow(spacing: CGFloat.ds("s-2")) {
                        hearButton(idea.onset..<idea.end)
                        Button(text["dna.inScore"]) { leave(idea.onset..<idea.end, insights: false) }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        Button(text["dna.inInsights"]) { leave(idea.onset..<idea.end, insights: true) }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    }
                }
                .padding(CGFloat.ds("s-4"))
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
                .accessibilityElement(children: .contain)
            }
        }
    }

    /// A card's Hear: it plays the passage once and shows Stop while it sounds, until it ends, is stopped or another starts
    /// (#582 item 14); by its icon alone, so it keeps its width, its words for VoiceOver (Andrei, v0.8 finding 13).
    func hearButton(_ range: Range<Double>) -> some View {
        let sounding = DnaPlaying.card(range, heard: cardHeard, loop: player.loop, state: player.state)
        return Button {
            if sounding { stop() } else { hearOnce(range) }
        } label: {
            Icon(name: sounding ? "pause" : "play")
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
        .accessibilityLabel(text[sounding ? "dna.stop" : "dna.hear"])
        .help(text[sounding ? "dna.stop" : "dna.hear"])
    }
}
