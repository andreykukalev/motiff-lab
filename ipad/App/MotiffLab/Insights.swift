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
/// ringed on the score and lit on the roll, or one place of it. A fact's Try opens the ways music-core offers for it; a
/// way sounds at once (facade `try`, then the piece with the variation in place, `playback` of `inPlace`), and the row
/// that holds it can Keep it as an idea (`idea.keep`) or Leave it (`variation.clear`).
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
    /// The facts whose Try is open, listing their ways.
    @Published private(set) var tryOpen: Set<String> = []
    /// The way being tried, on the row of the fact it came from.
    @Published private(set) var trial: Trial?
    /// A way music-core is working out, by its id.
    @Published private(set) var starting: String?

    /// A way tried: the fact it came from, the way, what changed in music-core's words, where the variation ends, and
    /// whether it was kept.
    struct Trial: Equatable {
        var factId: String
        var way: Fact.Try
        var explanation: String
        var end: Double
        var kept = false
    }

    /// A place chosen on its own: playback moves to its start, as on the web.
    var placeChosen: (@MainActor (Double) -> Void)?
    /// A way tried: the piece with the variation in place, to hear at once, and where the variation ends.
    var variationReady: (@MainActor (_ notes: [PlaybackNote], _ end: Double) -> Void)?
    /// The variation left: the original sounds again.
    var variationLeft: (@MainActor () -> Void)?

    private var understood: ScoreSelection?
    private var piece: Int32 = 0

    /// Reads the selection's facts from music-core; again only when the selection is another one.
    func load(piece: Int32, selection: ScoreSelection?) async {
        self.piece = piece
        guard selection != understood || (selection != nil && understanding == nil && !loading) else { return }
        understood = selection
        chosen = nil
        evidence = []
        tryOpen = []
        problem = nil
        // A variation belongs to the passage it was tried on.
        if trial != nil { await leave() }
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

    /// Try on a fact: its ways are listed, or put away.
    func toggleTry(_ fact: Fact) {
        if tryOpen.contains(fact.id) { tryOpen.remove(fact.id) } else { tryOpen.insert(fact.id) }
    }

    /// A way tried: music-core makes the variation, and the piece with it in place sounds at once.
    func tryWay(_ way: Fact.Try, of fact: Fact) async {
        guard starting == nil else { return }
        let piece = piece
        starting = way.id
        defer { starting = nil }
        problem = nil
        let request = Self.json(["chip": way.id])
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "try", request) }
        guard let state = facadeObject(answer), state["error"] == nil, let result = state["result"] as? [String: Any] else {
            problem = facadeObject(answer)?["message"] as? String
            return
        }
        let ends = (result["notes"] as? [[String: Any]] ?? []).compactMap { note -> Double? in
            guard let onset = (note["onset"] as? NSNumber)?.doubleValue, let duration = (note["duration"] as? NSNumber)?.doubleValue else { return nil }
            return onset + duration
        }
        let heard = await MusicCore.run { MusicCore.call(piece: piece, "playback", #"{"of": "inPlace"}"#) }
        guard let score = PlaybackScore.read(heard) else {
            problem = facadeObject(heard)?["message"] as? String
            return
        }
        trial = Trial(factId: fact.id, way: way, explanation: result["explanation"] as? String ?? "", end: ends.max() ?? 0)
        tryOpen.remove(fact.id)
        variationReady?(score.notes, ends.max() ?? 0)
    }

    /// Keeps the variation being tried as an idea, named after its way and filed with its fact (`idea.keep`).
    func keep() async {
        guard let trial, !trial.kept else { return }
        let piece = piece
        let request = Self.json(["name": trial.way.label, "factId": trial.factId, "now": ISO8601DateFormatter().string(from: Date())])
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "idea.keep", request) }
        guard let object = facadeObject(answer), object["error"] == nil else {
            problem = facadeObject(answer)?["message"] as? String
            return
        }
        self.trial?.kept = true
    }

    /// Leaves the variation: music-core forgets it, and the original sounds again.
    func leave() async {
        guard trial != nil else { return }
        trial = nil
        let piece = piece
        _ = await MusicCore.run { MusicCore.call(piece: piece, "variation.clear") }
        variationLeft?()
    }

    private static func json(_ object: [String: Any]) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: object) else { return "{}" }
        return String(decoding: data, as: UTF8.self)
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
    @ObservedObject var player: Player
    /// What loops while a variation is heard: the selection, to the variation's end.
    let range: Range<Double>?
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
            if player.hasVariation, let range {
                HearingSwitch(player: player, range: range, text: text)
                    .padding(.leading, CGFloat.ds("s-6"))
                    .padding(.top, CGFloat.ds("s-2"))
            }
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
                if let trial = insights.trial, !shown(understanding).contains(trial.factId) {
                    // The fact the variation came from is filtered out: the trial stays in reach at the top.
                    group(text["insights.trying"]) {
                        TrialRow(insights: insights, text: text)
                    }
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

    /// The ids of the facts the stream shows under the lens chosen.
    private func shown(_ understanding: Understanding) -> Set<String> {
        Set(understanding.facts.filter { insights.shows($0) }.map(\.id))
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
            if !fact.tries.isEmpty {
                TryRow(fact: fact, insights: insights, text: text)
            }
            if insights.trial?.factId == fact.id {
                TrialRow(insights: insights, text: text)
            }
        }
        .padding(CGFloat.ds("s-3"))
        .background(chosen ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(chosen ? Color.ds("accent") : .clear))
        .padding(.horizontal, -CGFloat.ds("s-3"))
        .accessibilityElement(children: .contain)
    }
}

/// A fact's Try (Andrei, 2026-11-04): one quiet button that opens the two or three ways music-core offers for the fact;
/// a way sounds at once. On the row that holds the variation, Keep follows the button.
struct TryRow: View {
    let fact: Fact
    @ObservedObject var insights: InsightsController
    let text: UIText

    var body: some View {
        let holds = insights.trial?.factId == fact.id
        let open = insights.tryOpen.contains(fact.id)
        FlowLayout(spacing: CGFloat.ds("s-2")) {
            Button {
                insights.toggleTry(fact)
            } label: {
                Text(text["insights.try"])
                    .font(DesignTokens.font(weight: 500, size: 13))
                    .foregroundStyle(Color.ds("accent"))
                    .padding(.horizontal, CGFloat.ds("s-3"))
                    .frame(minHeight: CGFloat.ds("h-compact"))
                    .background(open || holds ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .disabled(holds)
            .accessibilityAddTraits(open ? .isSelected : [])
            if open && !holds {
                ForEach(fact.tries, id: \.id) { way in
                    Button {
                        Task { await insights.tryWay(way, of: fact) }
                    } label: {
                        HStack(spacing: 6) {
                            if insights.starting == way.id { ProgressView().controlSize(.mini) }
                            Text(way.label)
                        }
                    }
                    .buttonStyle(DSChipStyle())
                    .disabled(insights.starting != nil)
                }
            }
            if holds, let trial = insights.trial, !trial.kept {
                Button {
                    Task { await insights.keep() }
                } label: {
                    Text(text["insights.keep"]).dsType(.label).foregroundStyle(Color.ds("accent-hover"))
                }
                .buttonStyle(.plain)
                .frame(minHeight: CGFloat.ds("h-compact"))
                .accessibilityLabel(text["explore.keep"])
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["insights.try"])
    }
}

/// The variation being tried (the web app's experiment card, in short): its way, what changed in music-core's words,
/// whether it is kept, and Leave it.
struct TrialRow: View {
    @ObservedObject var insights: InsightsController
    let text: UIText

    var body: some View {
        if let trial = insights.trial {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Tag(text: trial.way.label, kind: .saved)
                    if trial.kept {
                        Text(text["explore.keptWithProject"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                    }
                }
                if !trial.explanation.isEmpty {
                    (Text(text["explore.whatChanged"]).bold() + Text(" " + trial.explanation))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                Button {
                    Task { await insights.leave() }
                } label: {
                    Text(text["explore.leave"]).dsType(.label).foregroundStyle(Color.ds("accent-hover"))
                }
                .buttonStyle(.plain)
                .accessibilityHint(text["explore.leaveHint"])
            }
            .padding(CGFloat.ds("s-3"))
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("plum"), style: StrokeStyle(lineWidth: 1.5, dash: [4, 3])))
        }
    }
}

/// What sounds while a variation is there (0018 section 7; ds.css `.seg`): the original, the variation, or the two
/// looping one after the other.
struct HearingSwitch: View {
    @ObservedObject var player: Player
    let range: Range<Double>
    let text: UIText

    var body: some View {
        HStack(spacing: 0) {
            option(.original, "focus.original", hint: "compare.originalHint")
            option(.variation, "focus.variation", hint: "compare.variationHint")
            option(.alternate, "focus.alternate", hint: "compare.alternateHint")
        }
        .padding(2)
        .frame(height: CGFloat.ds("h-compact"))
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["focus.listen"])
    }

    private func option(_ hearing: Player.Hearing, _ key: String, hint: String) -> some View {
        let on = player.hearing == hearing
        return Button {
            player.hear(hearing, over: range)
        } label: {
            Text(text[key])
                .font(DesignTokens.font(weight: on ? 600 : 500, size: 13, relativeTo: .callout))
                .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                .padding(.horizontal, CGFloat.ds("s-3"))
                .frame(maxHeight: .infinity)
                .background(on ? Color.ds("accent-tint-strong") : .clear, in: Capsule())
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .accessibilityHint(text[hint])
        .accessibilityAddTraits(on ? .isSelected : [])
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
