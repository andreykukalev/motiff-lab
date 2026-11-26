import Foundation

/// The map of the whole piece as music-core answers `dna` (0018 section 11; testing item 22): the form's sections lettered
/// by what they share, the key lane, a chord per bar with the closes, the density, new material, register and dynamics,
/// and the ideas with every place each is heard. Every lane says how far it is filled.
struct DnaMap: Sendable {
    struct Mark: Sendable, Equatable {
        /// "original", "exact", "transposed", "varied" or "inverted".
        var kind: String
        var onset: Double
        var end: Double
        var firstBar: Int
        var lastBar: Int
    }

    struct Idea: Sendable, Identifiable {
        var id: String
        var title: String
        var summary: String
        var fromSelection: Bool
        var onset: Double
        var end: Double
        var marks: [Mark]
    }

    struct Key: Sendable {
        var label: String
        var minor: Bool
        var inferred: Bool
        var onset: Double
        var end: Double
        /// Where the recognised notes still to check are when the key is read from them (#352); nil otherwise.
        var doubt: String? = nil
    }

    struct Chord: Sendable {
        var bar: Int
        var symbol: String
        var clear: Bool
        /// Its step of the bar's key as a Roman numeral: "i", "V7" (testing item 30).
        var degree = ""
        /// "tonic", "subdominant", "dominant" or "none".
        var function = "none"
        /// Why it is uncertain when it rests on recognised notes still to check (#352); nil otherwise.
        var doubt: String?
    }

    struct Cadence: Sendable {
        var at: Double
        var full: Bool
        var bar = 0
        /// What the close is, in words, said when it is tapped.
        var explanation = ""
    }

    /// The circle of fifths of one section of the form, or of one key stretch (testing item 30).
    struct SectionCircle: Sendable, Identifiable {
        var id: String
        var label: String
        var onset: Double
        var end: Double
        /// The key of the stretch before, when this one is in another.
        var fromName: String?
        var circle: KeyCircle
        /// Why the circle is drawn as uncertain when its key and chords rest on recognised notes still to check (#352).
        var doubt: String? = nil
    }

    struct Level: Sendable {
        var bar: Int
        var value: Double
    }

    struct Span: Sendable {
        var bar: Int
        var low: Int
        var high: Int
    }

    var firstBar = 1
    var lastBar = 1
    /// The map in one line, read before any lane.
    var summary = ""
    var circles: [SectionCircle] = []
    /// Where each bar starts, in ticks, from the first bar to one past the last.
    var barStarts: [Double] = []
    var form: [FormSection] = []
    var ideasStatus = "notYet"
    var ideas: [Idea] = []
    var keys: [Key] = []
    var harmony: [Chord] = []
    var cadences: [Cadence] = []
    var density: [Level] = []
    var newMaterial: [Int] = []
    var register: [Span] = []
    var dynamics: [Level] = []

    var start: Double { barStarts.first ?? 0 }
    var end: Double { barStarts.last ?? 0 }

    /// Where a tick lies along the map, from 0 at its start to 1 at its end.
    func fraction(of tick: Double) -> Double {
        let length = end - start
        guard length > 0 else { return 0 }
        return min(max((tick - start) / length, 0), 1)
    }

    /// The grid a dragged playhead goes to, as on the roll and the score: a sixteenth, 120 ticks at 480 to the quarter
    /// (playhead-drag.ts DRAG_GRID).
    static let dragGrid = 120.0

    /// The tick at a place along the map (0 its start, 1 its end) where the playhead is dragged and let go: inside the piece,
    /// on the nearest sixteenth counted from the start of the bar it lies in and no later than that bar's last one, as the
    /// roll's black handle goes (playhead-drag.ts snapTick; the map has no note starts to stand on besides). A whole tick.
    func tick(at fraction: Double) -> Double {
        let inside = min(start + min(max(fraction, 0), 1) * (end - start), max(start, end - 1))
        guard let index = barStarts.lastIndex(where: { $0 <= inside }), index + 1 < barStarts.count else { return inside.rounded() }
        let bar = barStarts[index]
        let last = max(0, ((barStarts[index + 1] - bar) / Self.dragGrid).rounded(.up) - 1)
        let steps = min(max(((inside - bar) / Self.dragGrid).rounded(), 0), last)
        return (bar + steps * Self.dragGrid).rounded()
    }

    /// Where a bar starts and ends, in ticks.
    func bar(_ number: Int) -> Range<Double>? {
        let index = number - firstBar
        guard index >= 0, index + 1 < barStarts.count else { return nil }
        return barStarts[index]..<barStarts[index + 1]
    }

    init() {}

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        func number(_ value: Any?) -> Double { (value as? NSNumber)?.doubleValue ?? 0 }
        func int(_ value: Any?) -> Int { (value as? NSNumber)?.intValue ?? 0 }
        func passage(_ value: Any?) -> (Double, Double, Int, Int) {
            let passage = value as? [String: Any] ?? [:]
            return (number(passage["onset"]), number(passage["end"]), int(passage["firstBar"]), int(passage["lastBar"]))
        }
        func lane(_ name: String) -> [String: Any] { object[name] as? [String: Any] ?? [:] }
        func levels(_ name: String) -> [Level] {
            (lane(name)["bars"] as? [[String: Any]] ?? []).map { Level(bar: int($0["bar"]), value: number($0["value"])) }
        }

        firstBar = int(object["firstBar"])
        lastBar = int(object["lastBar"])
        summary = object["summary"] as? String ?? ""
        circles = (object["circles"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let drawn = entry["circle"] as? [String: Any], let circle = KeyCircle(circle: drawn) else { return nil }
            let (onset, end, _, _) = passage(entry["passage"])
            return SectionCircle(id: entry["id"] as? String ?? "", label: entry["label"] as? String ?? "", onset: onset, end: end,
                          fromName: entry["fromName"] as? String, circle: circle, doubt: entry["doubt"] as? String)
        }
        barStarts = (object["barStarts"] as? [NSNumber] ?? []).map(\.doubleValue)
        form = (lane("form")["sections"] as? [[String: Any]] ?? []).map { section in
            let (onset, end, first, last) = passage(section["passage"])
            let selectable = section["selectable"] as? [String: Any]
            return FormSection(id: section["id"] as? String ?? "", label: section["label"] as? String ?? "", onset: onset, end: end,
                           firstBar: first, lastBar: last, sharesWith: section["sharesWith"] as? [String] ?? [],
                           selectableOnset: selectable.map { number($0["onset"]) } ?? onset,
                           selectableEnd: selectable.map { number($0["end"]) } ?? end)
        }
        ideasStatus = lane("ideas")["status"] as? String ?? "notYet"
        ideas = (lane("ideas")["items"] as? [[String: Any]] ?? []).map { idea in
            let (onset, end, _, _) = passage(idea["passage"])
            let marks = (idea["occurrences"] as? [[String: Any]] ?? []).map { mark in
                let (onset, end, first, last) = passage(mark["passage"])
                return Mark(kind: mark["kind"] as? String ?? "exact", onset: onset, end: end, firstBar: first, lastBar: last)
            }
            return Idea(id: idea["id"] as? String ?? "", title: idea["title"] as? String ?? "", summary: idea["summary"] as? String ?? "",
                        fromSelection: idea["fromSelection"] as? Bool ?? false, onset: onset, end: end, marks: marks)
        }
        keys = (lane("keys")["spans"] as? [[String: Any]] ?? []).map { key in
            let (onset, end, _, _) = passage(key["passage"])
            let mode = (key["key"] as? [String: Any])?["mode"] as? String
            return Key(label: key["label"] as? String ?? "", minor: mode == "minor",
                       inferred: (key["modeInferred"] as? Bool ?? false) || (key["keyInferred"] as? Bool ?? false), onset: onset, end: end,
                       doubt: key["doubt"] as? String)
        }
        harmony = (lane("harmony")["bars"] as? [[String: Any]] ?? []).map {
            Chord(bar: int($0["bar"]), symbol: $0["symbol"] as? String ?? "", clear: $0["clear"] as? Bool ?? true,
                  degree: $0["degree"] as? String ?? "", function: $0["function"] as? String ?? "none", doubt: $0["doubt"] as? String)
        }
        cadences = (lane("harmony")["cadences"] as? [[String: Any]] ?? []).map {
            Cadence(at: number($0["at"]), full: ($0["kind"] as? String) == "full", bar: int($0["bar"]),
                    explanation: $0["explanation"] as? String ?? "")
        }
        density = levels("density")
        newMaterial = (lane("newMaterial")["bars"] as? [NSNumber] ?? []).map(\.intValue)
        register = (lane("register")["bars"] as? [[String: Any]] ?? []).map {
            Span(bar: int($0["bar"]), low: int($0["low"]), high: int($0["high"]))
        }
        dynamics = levels("dynamics")
    }
}
