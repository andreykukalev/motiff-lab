import Combine
import Foundation

/// The Insights stream's state and its rules (0018 section 6): what music-core understood about the selection, the lens
/// the filter chips narrow it to, the lenses under Details opened, the facts whose Evidence is open, and the fact whose
/// places are ringed on the score and lit on the roll, or one place of it. A fact's Try opens the ways music-core offers
/// for it; a way sounds at once (facade `try`, then the piece with the variation in place, `playback` of `inPlace`), and
/// the row that holds it can Keep it as an idea (`idea.keep`) or Leave it (`variation.clear`). Each state is written only
/// by its methods here; the views (InsightsStream.swift, FactRow.swift, KeyCircleCard.swift) show it and call
/// them, and the score's workspace hands in the callbacks to the player. No view and no player belong here.
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
    @Published private(set) var lens: String?
    @Published private(set) var evidence: Set<String> = []
    /// The lenses under Details the player opened; the others are folded to their name and count.
    @Published private(set) var expanded: Set<String> = []
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

    /// A place chosen on its own: playback moves to its start.
    var placeChosen: (@MainActor (Double) -> Void)?
    /// A way tried: the piece with the variation in place, to hear at once, and where the variation ends.
    var variationReady: (@MainActor (_ notes: [PlaybackNote], _ end: Double) -> Void)?
    /// The variation left: the original sounds again.
    var variationLeft: (@MainActor () -> Void)?
    /// Hear the scale on the circle: the scale's notes, played once where they sound.
    var scaleReady: (@MainActor (_ notes: [PlaybackNote], _ range: Range<Double>) -> Void)?
    /// Stop the scale before its end.
    var scaleStop: (@MainActor () -> Void)?
    /// Whether the scale sounds now: its button then stops it. The player's, told by `hearingAloneChanged`.
    @Published private(set) var scaleHeard = false

    private var understood: ScoreSelection?
    private var piece: Int32 = 0
    /// Keep is on its way to music-core: a second tap meanwhile keeps nothing more (E-insights-3).
    private var keeping = false
    /// Counts each other selection read and each variation left: a way still being made when it moves on is left,
    /// whether the sheet is open or not (E-insights-2).
    private var generation = 0

    /// Reads the selection's facts from music-core; again only when the selection is another one, or `again` (`reread`).
    func load(piece: Int32, selection: ScoreSelection?, again: Bool = false) async {
        self.piece = piece
        guard again || selection != understood || (selection != nil && understanding == nil && !loading) else { return }
        if selection != understood { generation += 1 }
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
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.understand) }
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
        // The same passage: a way being made for it stays.
        await load(piece: piece, selection: selection, again: true)
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

    /// A filter chip: the stream narrows to its lens; pressed again, or All (nil), it lists every lens.
    func filter(_ lens: String?) {
        self.lens = self.lens == lens ? nil : lens
    }

    /// A lens's head under Details: its facts unfold, or fold back to its name and count.
    func fold(_ lens: String) {
        if !expanded.insert(lens).inserted { expanded.remove(lens) }
    }

    /// Whether the circle's scale sounds now (the player's `hearingAlone`, which the workspace follows).
    func hearingAloneChanged(_ alone: Bool) {
        scaleHeard = alone
    }

    /// The facts the circle of fifths opens from: the key and the chords.
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
        // E-insights-1: what the last step's failure said goes with the next step.
        problem = nil
        guard circle == nil else { return }
        let piece = piece
        let selection = understood
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.circle) }
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

    /// Hear the scale, which loops from the selection's start, or stop it while it sounds (testing, 2026-11-06: it went round with no way to stop it).
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
        let generation = generation
        starting = way.id
        problem = nil
        let request = facadeRequest(["chip": way.id])
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.tryWay, request) }
        guard let state = facadeObject(answer), state["error"] == nil, let result = state["result"] as? [String: Any] else {
            starting = nil
            // E-insights-2: a refusal for a passage no longer selected is not said over the next one's facts.
            if self.generation == generation { problem = facadeObject(answer)?["message"] as? String }
            return
        }
        let ends = (result["notes"] as? [[String: Any]] ?? []).compactMap { note -> Double? in
            guard let onset = (note["onset"] as? NSNumber)?.doubleValue, let duration = (note["duration"] as? NSNumber)?.doubleValue else { return nil }
            return onset + duration
        }
        let heard = await PieceCore(piece: piece).playback(#"{"of": "inPlace"}"#)
        // E-insights-2: the selection moved on, or the variation was left, while music-core made it; it belongs to the
        // passage it was tried on (as `load` leaves a trial): it goes, and nothing sounds or shows it. `starting` is not
        // left on meanwhile, so no way shows as busy; it is cleared here, not on exit, as the next way may set it.
        guard self.generation == generation else {
            starting = nil
            _ = await MusicCore.run { PieceCore(piece: piece).send(.variationClear) }
            return
        }
        starting = nil
        guard let score = heard.value.map(PlaybackScore.init) else {
            problem = heard.message
            return
        }
        trial = Trial(factId: fact.id, way: way, explanation: result["explanation"] as? String ?? "", end: ends.max() ?? 0)
        tryOpen.remove(fact.id)
        variationReady?(score.notes, ends.max() ?? 0)
    }

    /// Keeps the variation being tried as an idea, named after its way and filed with its fact (`idea.keep`).
    func keep() async {
        guard let trial, !trial.kept, !keeping else { return }
        keeping = true
        defer { keeping = false }
        // E-insights-1, as On the circle.
        problem = nil
        let piece = piece
        let request = facadeRequest(["name": trial.way.label, "factId": trial.factId, "now": ISO8601DateFormatter().string(from: Date())])
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.ideaKeep, request) }
        guard let object = facadeObject(answer), object["error"] == nil else {
            problem = facadeObject(answer)?["message"] as? String
            return
        }
        self.trial?.kept = true
    }

    /// Leaves the variation: music-core forgets it, and the original sounds again.
    func leave() async {
        // E-insights-2: a way still being made is left too, wherever the leave comes from (another selection with the
        // sheet closed, Sources opened, the screen handed over).
        generation += 1
        guard trial != nil else { return }
        trial = nil
        // E-insights-1: Leave is a next step.
        problem = nil
        let piece = piece
        _ = await MusicCore.run { PieceCore(piece: piece).send(.variationClear) }
        variationLeft?()
    }

    func shows(_ fact: Fact) -> Bool {
        lens == nil || lens == fact.lens
    }

    /// The facts under Main the lens chosen shows, in music-core's order.
    var mainShown: [Fact] {
        understanding?.mainFacts.filter { shows($0) } ?? []
    }

    /// The lenses under Details the lens chosen shows, those with facts not under Main, in the stream's order.
    var detailsShown: [String] {
        guard let understanding else { return [] }
        return understanding.lensesShown.filter { (lens == nil || lens == $0) && !understanding.facts(of: $0).isEmpty }
    }

    /// Whether a lens under Details is open: opened by the player or filtered to, or holding the fact chosen on the score
    /// or the trial when Main does not, so neither disappears.
    func opens(_ lens: String) -> Bool {
        if self.lens == lens || expanded.contains(lens) { return true }
        guard let understanding else { return false }
        let trialInMain = understanding.mainFacts.contains { trial?.factId == $0.id }
        return understanding.facts(of: lens).contains { chosen?.fact == $0 || (!trialInMain && trial?.factId == $0.id) }
    }

    /// Whether the fact the variation came from is filtered out: the trial then stays in reach at the top of the stream.
    var trialFilteredOut: Bool {
        guard let trial, let understanding else { return false }
        return !understanding.facts.contains { $0.id == trial.factId && shows($0) }
    }
}
