import SwiftUI

/// Piece DNA (0018 section 11; testing item 22; the web app's PieceDnaMap): the third place, a full-screen map of the
/// whole piece read top to bottom: the form with arcs between sections that share material, the key, the harmony with the
/// closes as dots, the density, new material, register and dynamics folded, then a lane per idea with a mark at every
/// place it is heard, and a card per idea. A mark plays its passage; a second tap opens the score there. Ask about the
/// piece opens the assistant's popover at the button, over the map, which stays as it was. The map explains itself
/// (testing item 30): a line on what it shows, a line under each lane's name, the form coloured by letter, the harmony in
/// rows that turn on and off (chords, steps of the key, functions in their own colours), the closes in words that say what
/// they are when tapped, and the circle of fifths of a section from the key and harmony lanes.
struct DnaScreen: View {
    let map: DnaMap
    let title: String
    @ObservedObject var player: Player
    let text: UIText
    /// ‹ More when the map was opened from More; nil for the score.
    let back: (label: String, action: () -> Void)?
    let close: () -> Void
    /// Leaves for the score with the passage selected, and Insights open on it when asked.
    let showInScore: (Range<Double>, Bool) -> Void
    /// Add Structure bookmarks: answers how many were added (DnaBookmarks says it in words), nil when nothing could be.
    let addBookmarks: () async -> Int?
    /// Ask about the piece: the map closes for the score, with the assistant beside it on the whole piece.
    let ask: () -> Void

    // The view's own state, not private: its extensions in the other Dna*.swift files read and write it.
    @State var heard: Range<Double>? = nil
    @State var playing = false
    /// The passage a card's Hear plays once: that button reads Stop while it sounds, in place (#582 item 14).
    @State var cardHeard: Range<Double>? = nil
    @State var moreLanes = false
    @State var status: String? = nil
    @ObservedObject var settings = DeviceSettings.shared
    /// The circle of fifths open over the map, and the chord picked on it by number.
    @State var circle: DnaMap.SectionCircle? = nil
    @State var chosenChord: Int? = nil
    /// What a tapped close says, and its bar.
    @State var explained: String? = nil
    @State var explainedRange: Range<Double>? = nil

    /// The lane names' column, as the web app's `--name`.
    let nameWidth: CGFloat = 176
    /// The widest the map's own words run: the summary, what an action did.
    let wordsWidth: CGFloat = 640

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                if !map.summary.isEmpty {
                    Text(map.summary)
                        .dsType(.title)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: wordsWidth, alignment: .leading)
                }
                if let status { // what the header's last action did, at the top of the map, under the header
                    Text(status).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: wordsWidth, alignment: .leading)
                }
                lanes
                if !map.ideas.isEmpty {
                    cards
                }
            }
            .padding(.horizontal, CGFloat.ds("s-4"))
            .padding(.top, CGFloat.ds("h-touch") + CGFloat.ds("s-5"))
            .padding(.bottom, CGFloat.ds("s-8"))
        }
        .overlay(alignment: .top) { bar }
        .overlay(alignment: .bottomTrailing) {
            pop.padding(CGFloat.ds("s-4"))
        }
        .background(Color.ds("desk").ignoresSafeArea())
        .onDisappear { stop() }
    }

    /// The header, as the score's bar (Andrei, v0.8 finding 14): Back and the title at the left, the counts, then the map's
    /// own actions at the right by their icons, their words for VoiceOver and the pointer: Play or Stop from the playhead,
    /// Add Structure bookmarks, Ask about the piece. The counts give way first where the bar is short.
    private var bar: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button {
                stop()
                if let back { back.action() } else { close() }
            } label: {
                HStack(spacing: 4) {
                    Icon(name: "back", size: CGFloat.ds("icon-l"))
                    if let back { Text(back.label).dsType(.label) }
                }
                .frame(minWidth: 40, minHeight: 40)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(back.map { text.format("nav.backTo", $0.label) } ?? text["dna.back"])
            // The title as the score's bar shows it, in its face, size and place (Andrei, v0.8 finding 11).
            Text(title).barTitle().padding(.horizontal, CGFloat.ds("s-2"))
            Text(text["dna.title"]).dsType(.label).foregroundStyle(Color.ds("ink-2"))
            ViewThatFits(in: .horizontal) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Spacer(minLength: CGFloat.ds("s-2"))
                    Text(counts).dsType(.label).foregroundStyle(Color.ds("ink-2")).lineLimit(1).fixedSize()
                    barActions
                }
                HStack(spacing: CGFloat.ds("s-2")) {
                    Spacer(minLength: CGFloat.ds("s-2"))
                    barActions
                }
            }
            .frame(maxWidth: .infinity, alignment: .trailing)
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
        .padding(.horizontal, CGFloat.ds("s-4"))
        .padding(.top, CGFloat.ds("s-3"))
    }

    @ViewBuilder private var barActions: some View {
        let stops = DnaPlaying.map(playing: playing, card: cardHeard, state: player.state)
        barButton(stops ? "pause" : "play", text[stops ? "dna.stop" : "dna.play"], action: playFromPlayhead)
        if !map.form.isEmpty {
            barButton("bookmark", text["dna.addBookmarks"]) {
                Task { status = DnaBookmarks.said(added: await addBookmarks(), text) }
            }
        }
        barButton("insight", text["ask.aboutPiece"]) {
            stop()
            ask()
        }
    }

    /// An action of the header by its icon, as the score's bar has them.
    private func barButton(_ icon: String, _ words: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Icon(name: icon, size: CGFloat.ds("icon-l")).frame(width: CGFloat.ds("h-control"), height: CGFloat.ds("h-control"))
        }
        .buttonStyle(BarActionStyle())
        .accessibilityLabel(words)
        .help(words)
    }

    /// "106 bars · 4 ideas · A minor".
    private var counts: String {
        var parts = [text.count("dna.bars", map.lastBar - map.firstBar + 1)]
        if !map.ideas.isEmpty { parts.append(text.count("dna.ideas", map.ideas.count)) }
        if let key = map.keys.first { parts.append(keyName(key)) }
        return parts.joined(separator: " · ")
    }

    func keyName(_ key: DnaMap.Key) -> String {
        key.inferred ? text.format("dna.keyAssumed", key.label) : key.label
    }
}
