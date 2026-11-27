import Foundation

// The Insights stream's facts as music-core states them (facade `understand`), decoded once: values only, no state and
// no view (the stream's state is InsightsController's).

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

/// What music-core understood about the selection (facade `understand`): every fact (the motif's, the structure's, the
/// sentence's, the character's, the harmony's), those the stream leads with under Main, and the three strongest.
struct Understanding: Equatable, Sendable {
    /// The lenses in the order the stream lists them (testing item 14): the harmony and the character, which holds the
    /// texture, first, as Main leads with them; then the structure, the line, the rhythm, the key.
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
