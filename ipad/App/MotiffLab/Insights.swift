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
    /// The fact in one short line for the stream when the text lists what it counts; nil when the text is already one line.
    var brief: String?
    var basis: String
    /// "Rests on recognised notes still to check in bar 5." when the fact does (#352); nil otherwise.
    var doubt: String?
    var places: [Place]
    var tries: [Try]

    init?(answer: [String: Any]) {
        guard let id = answer["id"] as? String, let text = answer["text"] as? String else { return nil }
        self.id = id
        lens = answer["lens"] as? String ?? "structure"
        certainty = answer["certainty"] as? String ?? "inferred"
        self.text = text
        brief = answer["brief"] as? String
        basis = answer["basis"] as? String ?? ""
        doubt = answer["doubt"] as? String
        places = (answer["places"] as? [[String: Any]] ?? []).compactMap { place in
            guard let onset = (place["onset"] as? NSNumber)?.doubleValue, let end = (place["end"] as? NSNumber)?.doubleValue else { return nil }
            return Place(label: place["label"] as? String ?? "", onset: onset, end: end, noteIds: place["noteIds"] as? [String] ?? [])
        }
        tries = (answer["tryChips"] as? [[String: Any]] ?? []).compactMap { chip in
            guard let id = chip["id"] as? String, let label = chip["label"] as? String else { return nil }
            return Try(id: id, label: label)
        }
    }

    /// What the stream shows (music-core's `MotifFact.Claim`): the brief, else the text.
    var claim: String { brief ?? text }
}

/// What music-core understood about the selection (facade `understand`): every fact, in the order the web app lists them,
/// those the stream leads with under Main, and the three strongest.
struct Understanding: Equatable, Sendable {
    /// The lenses in the order the stream lists them (the web app's InsightsStream, testing item 14): the harmony and the
    /// character, which holds the texture, first, as Main leads with them; then the structure, the line, the rhythm, the key.
    static let lenses = ["harmony", "character", "structure", "melody", "rhythm", "key"]

    var facts: [Fact]
    /// The fact ids under Main (`FactRanking.Main`): the harmonic motion, the texture, the strongest of the rest.
    var main: [String]
    var strongest: [String]
    /// Every fact id in the order the stream lists a lens (`FactRanking.InStream`): those naming places first,
    /// interpretations last.
    var inStream: [String]

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        func read(_ value: Any?) -> [Fact] { (value as? [[String: Any]] ?? []).compactMap(Fact.init(answer:)) }
        let motif = object["motif"] as? [String: Any]
        let structure = object["structure"] as? [String: Any]
        let harmony = object["harmony"] as? [String: Any]
        facts = read(motif?["facts"]) + read(structure?["facts"]) + read(object["sentence"]) + read(object["character"]) + read(harmony?["facts"])
        main = object["main"] as? [String] ?? []
        strongest = object["strongest"] as? [String] ?? []
        inStream = object["inStream"] as? [String] ?? []
    }

    /// The lenses that have facts, in the stream's order.
    var lensesShown: [String] {
        Self.lenses.filter { lens in facts.contains { $0.lens == lens } }
    }

    /// The facts under Main, in music-core's order.
    var mainFacts: [Fact] {
        main.compactMap { id in facts.first { $0.id == id } }
    }

    /// A lens's facts under Details, those under Main left out, in music-core's stream order.
    func facts(of lens: String) -> [Fact] {
        let order = Dictionary(inStream.enumerated().map { ($1, $0) }, uniquingKeysWith: { first, _ in first })
        return facts.enumerated()
            .filter { $0.element.lens == lens && !main.contains($0.element.id) }
            .sorted { (order[$0.element.id] ?? Int.max, $0.offset) < (order[$1.element.id] ?? Int.max, $1.offset) }
            .map(\.element)
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
        /// A place of the circle of fifths opened from the fact: a chord, or the notes outside the scale.
        var custom: Fact.Place? = nil

        var places: [Fact.Place] {
            if let custom { return [custom] }
            return place.map { [fact.places[$0]] } ?? fact.places
        }
    }

    @Published private(set) var understanding: Understanding?
    @Published private(set) var loading = false
    @Published private(set) var problem: String?
    /// The lens the filter chips narrow the stream to; nil for all of them.
    @Published var lens: String?
    @Published private(set) var evidence: Set<String> = []
    /// The lenses under Details the player opened; the others are folded to their name and count.
    @Published var expanded: Set<String> = []
    @Published private(set) var chosen: Choice?
    /// The facts whose Try is open, listing their ways.
    @Published private(set) var tryOpen: Set<String> = []
    /// The way being tried, on the row of the fact it came from.
    @Published private(set) var trial: Trial?
    /// A way music-core is working out, by its id.
    @Published private(set) var starting: String?
    /// The circle of fifths for the selection (facade `circle`), read when it is first opened.
    @Published private(set) var circle: KeyCircle?
    /// The fact whose row holds the circle, opened with On the circle; nil when it is closed.
    @Published private(set) var circleFor: String?

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
    /// Hear the scale on the circle: the scale's notes, played once where they sound.
    var scaleReady: (@MainActor (_ notes: [PlaybackNote], _ range: Range<Double>) -> Void)?
    /// Stop the scale before its end.
    var scaleStop: (@MainActor () -> Void)?
    /// Whether the scale sounds now: its button then stops it.
    @Published var scaleHeard = false

    private var understood: ScoreSelection?
    private var piece: Int32 = 0

    /// Reads the selection's facts from music-core; again only when the selection is another one.
    func load(piece: Int32, selection: ScoreSelection?) async {
        self.piece = piece
        guard selection != understood || (selection != nil && understanding == nil && !loading) else { return }
        understood = selection
        chosen = nil
        evidence = []
        expanded = []
        circle = nil
        circleFor = nil
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

    /// The same selection read again, when what its facts rest on changed: a note fixed, or a bar checked or to check again,
    /// so a fact on recognised notes still to check says so, or no longer does (#352). Nothing when nothing was read.
    func reread() async {
        guard let selection = understood, trial == nil else { return }
        understood = nil
        await load(piece: piece, selection: selection)
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

    /// The facts the circle of fifths opens from (the web app's InsightsStream): the key and the chords.
    static func opensCircle(_ fact: Fact) -> Bool {
        fact.id == "key" || fact.id == "harmony.chords" || fact.id == "harmony.motion"
    }

    /// On the circle: the circle opens in the fact's row, read from music-core the first time; pressed again, it closes.
    func toggleCircle(_ fact: Fact) async {
        if circleFor == fact.id {
            circleFor = nil
            return
        }
        circleFor = fact.id
        guard circle == nil else { return }
        let piece = piece
        let selection = understood
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "circle") }
        guard understood == selection else { return }
        circle = KeyCircle(answer: answer)
        if circle == nil {
            circleFor = nil
            problem = facadeObject(answer)?["message"] as? String
        }
    }

    /// A chip of the circle: its notes are ringed and playback moves to them; pressed again, they are put away.
    func chooseCirclePlace(_ place: Fact.Place, of fact: Fact) {
        if chosen?.custom == place {
            chosen = nil
            return
        }
        chosen = Choice(fact: fact, custom: place)
        placeChosen?(place.onset)
    }

    /// Hear the scale: the key's scale loops from the selection's start.
    /// Hear the scale, or stop it while it sounds (testing, 2026-11-06: it went round with no way to stop it).
    func hearScale() {
        if scaleHeard {
            scaleStop?()
            return
        }
        guard let circle, let range = circle.scaleRange else { return }
        scaleReady?(circle.scale, range)
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
    /// The questions asked about the piece, which the assistant at the stream's foot shows (testing item 23).
    @ObservedObject var conversation: AskConversation
    @ObservedObject var account: Account
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
            if player.hasVariation, !player.secondIsVersion, let range {
                HearingSwitch(player: player, range: range, text: text)
                    .padding(.leading, CGFloat.ds("s-6"))
                    .padding(.top, CGFloat.ds("s-2"))
            }
            ScrollViewReader { reader in
                ScrollView {
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-6")) {
                        InsightsStream(insights: insights, text: text, hasSelection: !(score.selection?.noteIds.isEmpty ?? true))
                        // The assistant is the stream's last voice: the conversation the Ask popovers add to, and its field.
                        AskStreamFoot(conversation: conversation, account: account, text: text)
                    }
                    .padding(.leading, CGFloat.ds("s-6"))
                    .padding(.trailing, CGFloat.ds("s-5"))
                    .padding(.vertical, CGFloat.ds("s-4"))
                    .id("top")
                }
                .onChange(of: insights.understanding) { _, _ in reader.scrollTo("top", anchor: .top) }
            }
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

/// The stream itself: filter chips, Main (the harmonic motion, the texture, the strongest of the rest), then Details, the
/// other facts folded by lens with their count (testing item 14).
struct InsightsStream: View {
    @ObservedObject var insights: InsightsController
    let text: UIText
    let hasSelection: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            if let understanding = insights.understanding {
                let lenses = understanding.lensesShown
                if lenses.count > 1 {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: CGFloat.ds("s-2")) {
                            chip(text["insights.all"], on: insights.lens == nil) { insights.lens = nil }
                            ForEach(lenses, id: \.self) { lens in
                                chip(lensLabel(lens), on: insights.lens == lens) { insights.lens = insights.lens == lens ? nil : lens }
                            }
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
                let main = understanding.mainFacts.filter { insights.shows($0) }
                if !main.isEmpty {
                    group(text["insights.main"]) {
                        ForEach(main) { fact in
                            FactRow(fact: fact, insights: insights, text: text)
                        }
                    }
                }
                let details = lenses.filter { (insights.lens == nil || insights.lens == $0) && !understanding.facts(of: $0).isEmpty }
                if !details.isEmpty {
                    VStack(alignment: .leading, spacing: 0) {
                        Text(text["insights.details"])
                            .overline()
                            .padding(.bottom, CGFloat.ds("s-2"))
                            .accessibilityAddTraits(.isHeader)
                        ForEach(details, id: \.self) { lens in
                            let facts = understanding.facts(of: lens)
                            let open = opens(lens, facts: facts, understanding: understanding)
                            Button {
                                if !insights.expanded.insert(lens).inserted { insights.expanded.remove(lens) }
                            } label: {
                                HStack {
                                    Text(text.format("insights.lensCount", lensLabel(lens), text.count("understand.facts", facts.count)))
                                        .dsType(.body)
                                        .foregroundStyle(Color.ds("ink"))
                                    Spacer()
                                    Icon(name: "chevd", size: CGFloat.ds("icon-s"))
                                        .foregroundStyle(Color.ds("ink-3"))
                                        .rotationEffect(.degrees(open ? 180 : 0))
                                }
                                .frame(minHeight: CGFloat.ds("h-touch"))
                                .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .disabled(insights.lens == lens)
                            .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
                            .accessibilityAddTraits(.isHeader)
                            .accessibilityValue(open ? text["insights.lensOpen"] : text["insights.lensFolded"])
                            if open {
                                VStack(alignment: .leading, spacing: 2) {
                                    ForEach(facts) { fact in
                                        FactRow(fact: fact, insights: insights, text: text)
                                    }
                                }
                                .padding(.top, CGFloat.ds("s-2"))
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

    /// Whether a lens under Details is open: opened by the player or filtered to, or holding the fact chosen on the score
    /// or the trial when Main does not, so neither disappears.
    private func opens(_ lens: String, facts: [Fact], understanding: Understanding) -> Bool {
        if insights.lens == lens || insights.expanded.contains(lens) { return true }
        let trialInMain = understanding.mainFacts.contains { insights.trial?.factId == $0.id }
        return facts.contains { insights.chosen?.fact == $0 || (!trialInMain && insights.trial?.factId == $0.id) }
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
    @ObservedObject var insights: InsightsController
    let text: UIText

    var body: some View {
        let chosen = insights.chosen?.fact == fact
        let evidence = insights.evidence.contains(fact.id)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            if fact.places.isEmpty {
                Text(fact.claim).dsType(.body).foregroundStyle(Color.ds("ink"))
            } else {
                Button {
                    insights.choose(fact)
                } label: {
                    Text(fact.claim)
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
            if let doubt = fact.doubt {
                // The fact rests on recognised notes still to check (#352): said where, beside the uncertain tag.
                Text(doubt).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
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
                if InsightsController.opensCircle(fact) {
                    Button {
                        Task { await insights.toggleCircle(fact) }
                    } label: {
                        Text(text["insights.onCircle"]).dsType(.label).foregroundStyle(Color.ds("accent-hover"))
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(insights.circleFor == fact.id ? .isSelected : [])
                }
            }
            if evidence {
                if fact.brief != nil {
                    Text(fact.text).dsType(.caption).foregroundStyle(Color.ds("ink"))
                }
                Text(fact.basis).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            if chosen, insights.chosen?.custom == nil, !fact.places.isEmpty {
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
            if insights.circleFor == fact.id, let circle = insights.circle {
                KeyCircleCard(circle: circle, fact: fact, insights: insights, text: text)
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
    /// Another version's name when it stands in the variation's place (0022, section 5): Notes / Version / Alternate.
    var version: String?

    var body: some View {
        HStack(spacing: 0) {
            if let version {
                option(.original, "focus.notes", hint: text["compare.notesHint"])
                option(.variation, "focus.versionHear", hint: text.format("compare.versionHint", version))
                option(.alternate, "focus.alternate", hint: text.format("compare.alternateVersionHint", version))
            } else {
                option(.original, "focus.original", hint: text["compare.originalHint"])
                option(.variation, "focus.variation", hint: text["compare.variationHint"])
                option(.alternate, "focus.alternate", hint: text["compare.alternateHint"])
            }
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
        .accessibilityHint(hint)
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
