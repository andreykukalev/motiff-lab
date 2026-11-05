import SwiftUI

/// A fact about the selection, as music-core states it (facade `understand`): a short claim in the piece's language, its
/// lens and how certain it is, the evidence it rests on, the places in the score it names, and the Try chips music-core
/// offers for it. The app never words or works out a fact itself (0013).
struct Fact: Identifiable, Equatable, Sendable {
    struct Place: Equatable, Sendable {
        var label: String
        var onset: Double
        var end: Double
        var noteIds: [String]
    }

    /// A way to try something with the fact (0018 section 6): music-core's chip id and its label.
    struct Try: Equatable, Sendable {
        var id: String
        var label: String
    }

    var id: String
    /// "rhythm", "melody", "key", "structure", "character" or "harmony".
    var lens: String
    /// "detected", "inferred", "uncertain" or "interpretation".
    var certainty: String
    var text: String
    var basis: String
    var places: [Place]
    var tries: [Try]

    init?(answer: [String: Any]) {
        guard let id = answer["id"] as? String, let text = answer["text"] as? String else { return nil }
        self.id = id
        lens = answer["lens"] as? String ?? "structure"
        certainty = answer["certainty"] as? String ?? "inferred"
        self.text = text
        basis = answer["basis"] as? String ?? ""
        places = (answer["places"] as? [[String: Any]] ?? []).compactMap { place in
            guard let onset = (place["onset"] as? NSNumber)?.doubleValue, let end = (place["end"] as? NSNumber)?.doubleValue else { return nil }
            return Place(label: place["label"] as? String ?? "", onset: onset, end: end, noteIds: place["noteIds"] as? [String] ?? [])
        }
        tries = (answer["tryChips"] as? [[String: Any]] ?? []).compactMap { chip in
            guard let id = chip["id"] as? String, let label = chip["label"] as? String else { return nil }
            return Try(id: id, label: label)
        }
    }
}

/// What music-core understood about the selection (facade `understand`): every fact, in the order the web app lists them,
/// the one to hear first with why, and the three strongest.
struct Understanding: Equatable, Sendable {
    /// The lenses in music-core's order (FactLens), which is the order the stream groups them in.
    static let lenses = ["rhythm", "melody", "key", "structure", "character", "harmony"]

    var facts: [Fact]
    var keyObservation: String?
    var keyReason: String?
    var strongest: [String]

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        func read(_ value: Any?) -> [Fact] { (value as? [[String: Any]] ?? []).compactMap(Fact.init(answer:)) }
        let motif = object["motif"] as? [String: Any]
        let structure = object["structure"] as? [String: Any]
        let harmony = object["harmony"] as? [String: Any]
        facts = read(motif?["facts"]) + read(structure?["facts"]) + read(object["sentence"]) + read(object["character"]) + read(harmony?["facts"])
        let key = object["keyObservation"] as? [String: Any]
        keyObservation = key?["id"] as? String
        keyReason = key?["reason"] as? String
        strongest = object["strongest"] as? [String] ?? []
    }

    /// The lenses that have facts, in music-core's order.
    var lensesShown: [String] {
        Self.lenses.filter { lens in facts.contains { $0.lens == lens } }
    }

    var key: Fact? {
        keyObservation.flatMap { id in facts.first { $0.id == id } }
    }
}

/// The Insights stream's state (0018 section 6, the web app's InsightsStream): what music-core understood about the
/// selection, the lens the filter chips narrow it to, the facts whose Evidence is open, and the fact whose places are
/// ringed on the score and lit on the roll, or one place of it.
@MainActor
final class InsightsController: ObservableObject {
    struct Choice: Equatable {
        var fact: Fact
        /// The one place chosen, by its index; nil for all of them.
        var place: Int?

        var places: [Fact.Place] {
            place.map { [fact.places[$0]] } ?? fact.places
        }
    }

    @Published private(set) var understanding: Understanding?
    @Published private(set) var loading = false
    @Published private(set) var problem: String?
    /// The lens the filter chips narrow the stream to; nil for all of them.
    @Published var lens: String?
    @Published private(set) var evidence: Set<String> = []
    @Published private(set) var chosen: Choice?

    /// A place chosen on its own: playback moves to its start, as on the web.
    var placeChosen: (@MainActor (Double) -> Void)?

    private var understood: ScoreSelection?

    /// Reads the selection's facts from music-core; again only when the selection is another one.
    func load(piece: Int32, selection: ScoreSelection?) async {
        guard selection != understood || (selection != nil && understanding == nil && !loading) else { return }
        understood = selection
        chosen = nil
        evidence = []
        problem = nil
        guard let selection, !selection.noteIds.isEmpty else {
            understanding = nil
            loading = false
            return
        }
        loading = true
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "understand") }
        // The selection moved on while music-core read it: the newer call shows its own.
        guard understood == selection else { return }
        loading = false
        understanding = Understanding(answer: answer)
        if understanding == nil {
            problem = facadeObject(answer)?["message"] as? String
        }
        if let lens, !(understanding?.lensesShown.contains(lens) ?? false) {
            self.lens = nil
        }
    }

    /// The claim tapped: its places are ringed; tapped again, they are cleared.
    func choose(_ fact: Fact) {
        chosen = chosen?.fact == fact ? nil : Choice(fact: fact)
    }

    /// A place chip: only that place is ringed and playback moves to its start; pressed again, all of them are.
    func choosePlace(_ index: Int, of fact: Fact) {
        if chosen?.fact == fact, chosen?.place == index {
            chosen = Choice(fact: fact)
            return
        }
        chosen = Choice(fact: fact, place: index)
        placeChosen?(fact.places[index].onset)
    }

    func clearChoice() {
        chosen = nil
    }

    func toggleEvidence(_ fact: Fact) {
        if evidence.contains(fact.id) { evidence.remove(fact.id) } else { evidence.insert(fact.id) }
    }

    func shows(_ fact: Fact) -> Bool {
        lens == nil || lens == fact.lens
    }
}

/// The Insights sheet (0018 section 6; design/kit/ds.css `.sheet` and `.fact`): the right third of the screen in landscape,
/// half the height from the bottom in portrait. Its head names the selection, and one stream lists what music-core
/// understood about it, grouped by lens, the one to hear first on top; filter chips narrow it without switching it. A
/// fact's claim rings its places on the score, Evidence opens in place, and its places can be ringed one at a time.
struct InsightsSheet: View {
    @ObservedObject var insights: InsightsController
    @ObservedObject var score: ScoreController
    let text: UIText
    let close: () -> Void

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
            ScrollViewReader { reader in
                ScrollView {
                    InsightsStream(insights: insights, text: text, hasSelection: !(score.selection?.noteIds.isEmpty ?? true))
                        .padding(.leading, CGFloat.ds("s-6"))
                        .padding(.trailing, CGFloat.ds("s-5"))
                        .padding(.vertical, CGFloat.ds("s-4"))
                        .id("top")
                }
                .onChange(of: insights.understanding) { _, _ in reader.scrollTo("top", anchor: .top) }
            }
            Text(text["insights.hint"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .padding(.vertical, CGFloat.ds("s-3"))
                .padding(.leading, CGFloat.ds("s-6"))
                .padding(.trailing, CGFloat.ds("s-5"))
                .frame(maxWidth: .infinity, alignment: .leading)
                .overlay(alignment: .top) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
        }
        .background(Color.ds("surface"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["bar.insights"])
    }

    /// The selection named, as the roll's drawer names it: "bars 9–16".
    private var heading: String {
        guard let selection = score.selection else { return text["bar.insights"] }
        return selection.firstBar == selection.lastBar
            ? text.format("bars.one", String(selection.firstBar))
            : text.format("bars.range", String(selection.firstBar), String(selection.lastBar))
    }
}

/// The stream itself: filter chips, the fact to hear first, then the facts by lens.
struct InsightsStream: View {
    @ObservedObject var insights: InsightsController
    let text: UIText
    let hasSelection: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            if let understanding = insights.understanding {
                let lenses = understanding.lensesShown
                if lenses.count > 1 {
                    FlowLayout(spacing: CGFloat.ds("s-2")) {
                        chip(text["insights.all"], on: insights.lens == nil) { insights.lens = nil }
                        ForEach(lenses, id: \.self) { lens in
                            chip(lensLabel(lens), on: insights.lens == lens) { insights.lens = insights.lens == lens ? nil : lens }
                        }
                    }
                    .padding(.bottom, CGFloat.ds("s-3"))
                    .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
                    .accessibilityElement(children: .contain)
                    .accessibilityLabel(text["insights.filter"])
                }
                if understanding.facts.isEmpty {
                    caption(text["understand.nothingYet"])
                }
                if let key = understanding.key, insights.shows(key) {
                    group(text["understand.keyObservation"]) {
                        FactRow(fact: key, why: understanding.keyReason, insights: insights, text: text)
                    }
                }
                ForEach(lenses.filter { insights.lens == nil || insights.lens == $0 }, id: \.self) { lens in
                    let facts = understanding.facts.filter { $0.lens == lens && $0.id != understanding.keyObservation }
                    if !facts.isEmpty {
                        group(text.format("insights.lensCount", lensLabel(lens), text.count("understand.facts", facts.count))) {
                            ForEach(facts) { fact in
                                FactRow(fact: fact, why: nil, insights: insights, text: text)
                            }
                        }
                    }
                }
            } else if insights.loading {
                ProgressView().frame(maxWidth: .infinity)
            } else if let problem = insights.problem {
                Text(problem).dsType(.label).foregroundStyle(Color.ds("danger"))
            } else if !hasSelection {
                caption(text["insights.selectFirst"])
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["insights.stream"])
    }

    private func group<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title)
                .overline()
                .padding(.bottom, CGFloat.ds("s-2"))
                .accessibilityAddTraits(.isHeader)
            content()
        }
    }

    private func chip(_ label: String, on: Bool, action: @escaping () -> Void) -> some View {
        Button(label, action: action)
            .buttonStyle(DSChipStyle(on: on))
            .accessibilityAddTraits(on ? .isSelected : [])
    }

    private func caption(_ words: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
    }

    private func lensLabel(_ lens: String) -> String {
        text["term.lens.\(lens)"]
    }
}

/// A fact in the stream (ds.css `.fact`): the claim, which rings its places on the score; its certainty, how many places
/// and Evidence, which opens in place; while it is chosen, its places as chips, each ringing only itself.
struct FactRow: View {
    let fact: Fact
    /// Why this is the one to hear first, under Evidence; nil for the other facts.
    let why: String?
    @ObservedObject var insights: InsightsController
    let text: UIText

    var body: some View {
        let chosen = insights.chosen?.fact == fact
        let evidence = insights.evidence.contains(fact.id)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            if fact.places.isEmpty {
                Text(fact.text).dsType(.body).foregroundStyle(Color.ds("ink"))
            } else {
                Button {
                    insights.choose(fact)
                } label: {
                    Text(fact.text)
                        .dsType(.body)
                        .foregroundStyle(Color.ds("ink"))
                        .multilineTextAlignment(.leading)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityHint(text["insights.claimHint"])
                .accessibilityAddTraits(chosen ? .isSelected : [])
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Tag(text: text["term.certainty.\(fact.certainty)"], kind: fact.certainty == "detected" ? .detected : .assumed)
                    .background(chosen ? Color.ds("surface") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
                if !fact.places.isEmpty {
                    Text(text.count("understand.places", fact.places.count))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                Button {
                    insights.toggleEvidence(fact)
                } label: {
                    Text(text["insights.evidence"]).dsType(.label).foregroundStyle(Color.ds("accent-hover"))
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(evidence ? .isSelected : [])
            }
            if evidence {
                Text(fact.basis).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                if let why {
                    (Text(text["understand.whyThisOne"]).bold() + Text(" " + why))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
            }
            if chosen {
                FlowLayout(spacing: CGFloat.ds("s-2")) {
                    ForEach(Array(fact.places.enumerated()), id: \.offset) { index, place in
                        let on = insights.chosen?.place == index
                        Button(place.label) {
                            insights.choosePlace(index, of: fact)
                        }
                        .buttonStyle(DSChipStyle(on: on))
                        .accessibilityHint(text["understand.placeHint"])
                        .accessibilityAddTraits(on ? .isSelected : [])
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["understand.placesLabel"])
            }
        }
        .padding(CGFloat.ds("s-3"))
        .background(chosen ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(chosen ? Color.ds("accent") : .clear))
        .padding(.horizontal, -CGFloat.ds("s-3"))
        .accessibilityElement(children: .contain)
    }
}

/// Views laid out in rows, wrapping to the next row when one is full, as the web app's chips wrap.
struct FlowLayout: Layout {
    var spacing: CGFloat

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = arrange(width: proposal.width ?? .infinity, subviews: subviews)
        let width = rows.map(\.width).max() ?? 0
        let height = rows.map(\.height).reduce(0, +) + spacing * CGFloat(max(0, rows.count - 1))
        return CGSize(width: proposal.width ?? width, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var y = bounds.minY
        for row in arrange(width: bounds.width, subviews: subviews) {
            var x = bounds.minX
            for index in row.indices {
                let size = subviews[index].sizeThatFits(.unspecified)
                subviews[index].place(at: CGPoint(x: x, y: y + (row.height - size.height) / 2), proposal: ProposedViewSize(size))
                x += size.width + spacing
            }
            y += row.height + spacing
        }
    }

    private struct Row {
        var indices: [Int] = []
        var width: CGFloat = 0
        var height: CGFloat = 0
    }

    private func arrange(width: CGFloat, subviews: Subviews) -> [Row] {
        var rows: [Row] = []
        var row = Row()
        for index in subviews.indices {
            let size = subviews[index].sizeThatFits(.unspecified)
            let needed = row.indices.isEmpty ? size.width : row.width + spacing + size.width
            if needed > width, !row.indices.isEmpty {
                rows.append(row)
                row = Row()
            }
            row.width = row.indices.isEmpty ? size.width : row.width + spacing + size.width
            row.height = max(row.height, size.height)
            row.indices.append(index)
        }
        if !row.indices.isEmpty { rows.append(row) }
        return rows
    }
}
