import SwiftUI

/// Piece DNA's cards, one per idea with its Hear, and the map's actions under them.
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

    var actions: some View {
        let mapStops = DnaPlaying.map(playing: playing, card: cardHeard, state: player.state)
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            FlowRow(spacing: CGFloat.ds("s-3")) {
                Button(action: playMap) {
                    HStack(spacing: 6) {
                        Icon(name: mapStops ? "pause" : "play")
                        Text(mapStops ? text["dna.stop"] : text["dna.playMap"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .primary))
                if !map.form.isEmpty {
                    Button {
                        Task { status = DnaBookmarks.said(added: await addBookmarks(), text) }
                    } label: {
                        HStack(spacing: 6) {
                            Icon(name: "bookmark")
                            Text(text["dna.addBookmarks"])
                        }
                    }
                    .buttonStyle(DSButtonStyle(kind: .secondary))
                }
                Button {
                    stop()
                    ask()
                } label: {
                    HStack(spacing: 6) {
                        Icon(name: "insight")
                        Text(text["ask.aboutPiece"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary))
            }
            if let status { // what the last action did, under the buttons, where it wraps (a FlowRow never does)
                Text(status).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: wordsWidth, alignment: .leading)
            }
        }
    }
}
