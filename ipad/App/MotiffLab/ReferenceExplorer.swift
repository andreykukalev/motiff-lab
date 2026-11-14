import Foundation

/// What the harmony explorer holds (v0.5, «Исследователь»; the web's ExplorerChoice), kept while the Reference is open: a
/// key, a progression of its chords, each in a position, and how the progression is played. It is a try-out: nothing of
/// it is saved or written into a piece. A tool at the instrument to sketch a progression and a texture (Andrei, 2026-11-13).
struct ExplorerChoice: Equatable {
    /// A bar of the progression: a chord of the key by its id ("V7") and its position (0 for root position).
    struct Step: Equatable {
        var chord: String
        var inversion = 0
    }

    var fifths = 0
    var minor = false
    /// The progression; nil until music-core gave the key's start (I–IV–V–I).
    var steps: [Step]?
    /// "together", "arpeggio", "broken", "alberti".
    var texture = "alberti"
    /// "close" or "open".
    var spacing = "close"
    /// Quarters a minute.
    var tempo = 72
    var loop = false
    /// The bar whose position and removal are offered; nil when none is.
    var selected: Int?

    /// A new key starts from its tonic, subdominant, dominant and tonic, as music-core gives them.
    mutating func start(fifths: Int, minor: Bool) {
        self.fifths = fifths
        self.minor = minor
        steps = nil
        selected = nil
    }

    /// A chord of the key at the end, in root position, while there is room.
    mutating func add(_ chord: String, maxBars: Int) {
        var next = steps ?? []
        guard next.count < maxBars else { return }
        next.append(Step(chord: chord))
        steps = next
    }

    /// A bar in another position.
    mutating func turn(bar: Int, inversion: Int) {
        guard var next = steps, next.indices.contains(bar) else { return }
        next[bar].inversion = inversion
        steps = next
    }

    mutating func remove(bar: Int) {
        guard var next = steps, next.indices.contains(bar) else { return }
        next.remove(at: bar)
        steps = next
        selected = nil
    }

    /// A tap on a bar offers its positions; a second tap puts them away.
    mutating func select(bar: Int) {
        selected = selected == bar ? nil : bar
    }

    /// The next tempo offered, round to the first after the last.
    mutating func nextTempo(_ tempos: [Int]) {
        guard !tempos.isEmpty else { return }
        let index = tempos.firstIndex(of: tempo) ?? -1
        tempo = tempos[(index + 1) % tempos.count]
    }

    /// The `reference.explorer` request: without steps, the key's start.
    func request(language: String, naming: String) -> [String: Any] {
        var fields: [String: Any] = ["fifths": fifths, "mode": minor ? "minor" : "major", "texture": texture, "spacing": spacing,
                                     "tempo": tempo, "language": language, "naming": naming]
        if let steps {
            fields["steps"] = steps.map { ["chord": $0.chord, "inversion": $0.inversion] as [String: Any] }
        }
        return fields
    }

    /// The interface's key for a choice music-core names in lower camel case: "upAndDown" is "reference.pattern.UpAndDown".
    static func key(_ id: String) -> String {
        id.prefix(1).uppercased() + id.dropFirst()
    }
}

/// The explorer as music-core answers `reference.explorer` on piece 0: the thirty keys, the key's chords with their
/// positions, the progression as it sounds bar by bar, its notation and its sound, and the choices offered.
struct ExplorerAnswer {
    struct Key: Identifiable, Equatable {
        var fifths: Int
        var minor: Bool
        var name: String
        var id: String { "\(fifths).\(minor)" }
    }

    struct Position: Equatable, Identifiable {
        var inversion: Int
        var title: String
        var figure: String?
        var id: Int { inversion }
    }

    struct Chord: Identifiable, Equatable {
        var id: String
        var numeral: String
        var symbol: String
        var positions: [Position]
    }

    /// A bar over the notes: the chord's numeral with its figures, its symbol with the bass ("E7/G♯") and the keys it sounds.
    struct Bar: Equatable {
        var chord: String
        var numeral: String
        var inversion: Int
        var figure: String?
        var symbol: String
        var pitches: [Int]
    }

    var keys: [Key]
    var key: Key
    var maxBars: Int
    var chords: [Chord]
    var steps: [ExplorerChoice.Step]
    var bars: [Bar]
    var mei: String
    var low: Int
    var high: Int
    var textures: [String]
    var spacings: [String]
    var tempos: [Int]
    var score: PlaybackScore?

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil, let key = Self.key(object["key"]) else { return nil }
        keys = (object["keys"] as? [Any] ?? []).compactMap { Self.key($0) }
        self.key = key
        maxBars = object["maxBars"] as? Int ?? 8
        chords = (object["chords"] as? [[String: Any]] ?? []).compactMap(Self.chord)
        steps = (object["steps"] as? [[String: Any]] ?? []).compactMap { step in
            guard let chord = step["chord"] as? String else { return nil }
            return ExplorerChoice.Step(chord: chord, inversion: step["inversion"] as? Int ?? 0)
        }
        bars = (object["progression"] as? [[String: Any]] ?? []).compactMap { bar in
            guard let chord = bar["chord"] as? String else { return nil }
            return Bar(chord: chord, numeral: bar["numeral"] as? String ?? "", inversion: bar["inversion"] as? Int ?? 0,
                       figure: bar["figure"] as? String, symbol: bar["symbol"] as? String ?? "", pitches: bar["pitches"] as? [Int] ?? [])
        }
        mei = object["mei"] as? String ?? ""
        low = object["keyboardLow"] as? Int ?? 36
        high = object["keyboardHigh"] as? Int ?? 83
        textures = object["textures"] as? [String] ?? []
        spacings = object["spacings"] as? [String] ?? []
        tempos = object["tempoChoices"] as? [Int] ?? []
        score = PlaybackScore.read(answer)
    }

    /// A chord of the key by its id.
    func chord(_ id: String) -> Chord? {
        chords.first { $0.id == id }
    }

    /// The keys a bar sounds, for the small keyboard.
    func sound(of bar: Int) -> ReferenceCard.Sound? {
        guard bars.indices.contains(bar) else { return nil }
        return ReferenceCard.Sound(id: "bar.\(bar)", title: bars[bar].symbol, notes: [], pitches: bars[bar].pitches, low: low, high: high)
    }

    private static func key(_ value: Any?) -> Key? {
        guard let key = value as? [String: Any], let fifths = key["fifths"] as? Int else { return nil }
        return Key(fifths: fifths, minor: key["mode"] as? String == "minor", name: key["name"] as? String ?? "")
    }

    private static func chord(_ item: [String: Any]) -> Chord? {
        guard let id = item["id"] as? String else { return nil }
        let positions = (item["positions"] as? [[String: Any]] ?? []).compactMap { position -> Position? in
            guard let inversion = position["inversion"] as? Int else { return nil }
            return Position(inversion: inversion, title: position["title"] as? String ?? "", figure: position["figure"] as? String)
        }
        return Chord(id: id, numeral: item["numeral"] as? String ?? id, symbol: item["symbol"] as? String ?? "", positions: positions)
    }
}

extension ReferenceController {
    /// Asks music-core for the progression as the choices give it; a change sounds at once while the music plays, else it
    /// waits for Play (the web's ListeningTab).
    func loadExplorer(language: String, naming: String) async {
        let request = ReferenceController.json(explorer.request(language: language, naming: naming))
        let text = await MusicCore.run { MusicCore.call(piece: 0, "reference.explorer", request) }
        guard let next = ExplorerAnswer(answer: text) else { return }
        progression = next
        explorer.steps = next.steps
        if let selected = explorer.selected, selected >= next.steps.count {
            explorer.selected = nil
        }
        if playing == "explorer" {
            await listen("explorer", next.score, loop: explorer.loop)
        }
    }

    /// A change of the explorer's choices, heard at once while it plays.
    func setExplorer(_ change: (inout ExplorerChoice) -> Void, language: String, naming: String) async {
        change(&explorer)
        await loadExplorer(language: language, naming: naming)
    }

    /// A tap on a bar: its positions offered, or put away. Nothing sounds different.
    func selectBar(_ bar: Int) {
        explorer.select(bar: bar)
    }

    /// Play or Pause.
    func playExplorer() async {
        if playing == "explorer" {
            pause()
        } else {
            await listen("explorer", progression?.score, loop: explorer.loop)
        }
    }

    /// The loop on or off, heard at once while it plays.
    func loopExplorer() async {
        explorer.loop.toggle()
        if playing == "explorer" {
            await listen("explorer", progression?.score, loop: explorer.loop)
        }
    }
}
