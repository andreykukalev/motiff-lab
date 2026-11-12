import AVFoundation
import SwiftUI

/// The Reference (an experiment, Settings · Experimental; Andrei, 2026-11-11): the circle of fifths with any key's card, as
/// music-core writes them (facade `reference.key` on piece 0, `KeyReference`). The app only lays them out.
struct ReferenceAnswer {
    /// A choice of the lookup: a number of sharps or flats.
    struct Choice: Identifiable, Equatable {
        var fifths: Int
        var label: String
        var spoken: String
        var id: Int { fifths }
    }

    /// The fifteen key signatures: none, one to seven sharps, one to seven flats.
    var signatures: [Choice]
    /// One of the circle's twelve places: the major's signature, which a tap on either ring opens.
    struct Place {
        var position: Int
        var fifths: Int
    }

    /// The twelve places of the circle, with their keys' fifths.
    var sectors: [Place]
    /// The circle drawn for the chosen key, or with no key when none is chosen.
    var circle: KeyCircle
    var card: ReferenceCard?

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        signatures = (object["signatures"] as? [[String: Any]] ?? []).compactMap { choice in
            guard let fifths = choice["fifths"] as? Int else { return nil }
            return Choice(fifths: fifths, label: choice["label"] as? String ?? "", spoken: choice["spoken"] as? String ?? "")
        }
        let rawSectors = object["sectors"] as? [[String: Any]] ?? []
        sectors = rawSectors.compactMap { sector -> Place? in
            guard let position = sector["position"] as? Int, let fifths = sector["fifths"] as? Int else { return nil }
            return Place(position: position, fifths: fifths)
        }
        // No key chosen yet: the bare circle, no place marked.
        let bare: [String: Any] = ["name": "", "position": -1, "certainty": "detected", "sectors": rawSectors]
        guard let circle = (object["circle"] as? [String: Any]).flatMap(KeyCircle.init(circle:)) ?? KeyCircle(circle: bare) else { return nil }
        self.circle = circle
        card = (object["card"] as? [String: Any]).flatMap(ReferenceCard.init)
    }
}

/// A key's card (`KeyCard`): the key, its relative, parallel and enharmonic keys, the signature, the scales and the main chords.
struct ReferenceCard: Equatable {
    struct Related: Equatable {
        var fifths: Int
        var minor: Bool
        var name: String
    }

    /// A scale or a chord: what it is called, its notes, and the keys of the small keyboard that show it.
    struct Sound: Equatable, Identifiable {
        var id: String
        var title: String
        var notes: [String]
        var pitches: [Int]
        var low: Int
        var high: Int
        /// "I", "iv", "V" for a chord; nil for a scale.
        var numeral: String?
        /// "tonic", "subdominant", "dominant" in words, for a chord.
        var functionName: String?
        /// "tonic", "subdominant", "dominant", for its colour.
        var function: String?
    }

    var fifths: Int
    var minor: Bool
    var name: String
    var signatureCount: String
    var accidentals: [String]
    var trebleSteps: [Int]
    var relative: Related
    var parallel: Related
    var enharmonic: Related?
    var scales: [Sound]
    var chords: [Sound]

    init?(_ card: [String: Any]) {
        guard let key = card["key"] as? [String: Any], let fifths = key["fifths"] as? Int, let name = card["name"] as? String,
              let signature = card["signature"] as? [String: Any],
              let relative = Self.related(card["relative"]), let parallel = Self.related(card["parallel"]) else { return nil }
        self.fifths = fifths
        minor = key["mode"] as? String == "minor"
        self.name = name
        signatureCount = signature["count"] as? String ?? ""
        accidentals = signature["accidentals"] as? [String] ?? []
        trebleSteps = signature["trebleSteps"] as? [Int] ?? []
        self.relative = relative
        self.parallel = parallel
        enharmonic = Self.related(card["enharmonic"])
        scales = (card["scales"] as? [[String: Any]] ?? []).compactMap { Self.sound($0, title: $0["title"] as? String) }
        chords = (card["chords"] as? [[String: Any]] ?? []).compactMap { Self.sound($0, title: $0["symbol"] as? String) }
    }

    /// "5 sharps: F♯, C♯, G♯, D♯, A♯", or the count alone without any.
    var signatureLine: String {
        accidentals.isEmpty ? signatureCount : "\(signatureCount): \(accidentals.joined(separator: ", "))"
    }

    private static func related(_ value: Any?) -> Related? {
        guard let key = value as? [String: Any], let fifths = key["fifths"] as? Int else { return nil }
        return Related(fifths: fifths, minor: key["mode"] as? String == "minor", name: key["name"] as? String ?? "")
    }

    private static func sound(_ item: [String: Any], title: String?) -> Sound? {
        guard let id = item["id"] as? String, let low = item["keyboardLow"] as? Int, let high = item["keyboardHigh"] as? Int else { return nil }
        return Sound(id: id, title: title ?? "", notes: item["notes"] as? [String] ?? [], pitches: item["pitches"] as? [Int] ?? [],
                     low: low, high: high, numeral: item["numeral"] as? String, functionName: item["functionName"] as? String,
                     function: item["function"] as? String)
    }
}

/// The Reference's state: the circle and the chosen key's card from music-core, and the piano that plays a scale or chord.
@MainActor
final class ReferenceController: ObservableObject {
    @Published private(set) var answer: ReferenceAnswer?
    /// The scale or chord shown on the keyboard: the card's first scale until another is played.
    @Published private(set) var shown: ReferenceCard.Sound?
    private let piano = ReferencePiano()

    /// The circle with no key chosen, or the card of a key ("fifths", major or minor).
    func load(fifths: Int? = nil, minor: Bool = false, language: String, naming: String) async {
        var request: [String: Any] = ["language": language, "naming": naming]
        if let fifths {
            request["fifths"] = fifths
            request["mode"] = minor ? "minor" : "major"
        }
        let body = Self.json(request)
        let text = await MusicCore.run { MusicCore.call(piece: 0, "reference.key", body) }
        guard let next = ReferenceAnswer(answer: text) else { return }
        answer = next
        shown = next.card?.scales.first
    }

    /// Shows a scale or chord on the keyboard and plays it once on the app's piano.
    func hear(_ sound: ReferenceCard.Sound) async {
        shown = sound
        guard let card = answer?.card else { return }
        let body = Self.json(["fifths": card.fifths, "mode": card.minor ? "minor" : "major", "id": sound.id])
        let text = await MusicCore.run { MusicCore.call(piece: 0, "reference.sound", body) }
        guard let score = PlaybackScore.read(text) else { return }
        await piano.play(score)
    }

    func stop() {
        piano.stop()
    }

    private static func json(_ object: [String: Any]) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: object) else { return "{}" }
        return String(decoding: data, as: UTF8.self)
    }
}

/// The app's piano for the Reference, which may open with no piece loaded (from the library): the same engine as `Player`
/// (`PlaybackEngine` through `Player.Renderer`), on its own audio engine, playing one short score at a time.
@MainActor
final class ReferencePiano {
    private let audio = AVAudioEngine()
    private var renderer: Player.Renderer?
    /// The start under way, so two quick taps start the audio once.
    private var starting: Task<Player.Renderer?, Never>?

    func play(_ score: PlaybackScore) async {
        if starting == nil {
            starting = Task { await self.start() }
        }
        guard let renderer = await starting?.value else {
            starting = nil
            return
        }
        renderer.send(.pause)
        renderer.send(.timeline(score.timeline))
        renderer.send(.source(id: "reference", notes: score.notes))
        renderer.send(.activeSources(["reference"]))
        renderer.send(.play)
    }

    /// Stops the sound and the audio: the sheet calls this as it closes.
    func stop() {
        renderer?.send(.pause)
        audio.stop()
    }

    /// Starts the audio and loads the piano.
    private func start() async -> Player.Renderer? {
        do {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.playback, mode: .default)
            try session.setActive(true)
            let sampleRate = audio.outputNode.outputFormat(forBus: 0).sampleRate
            let renderer = Player.Renderer(sampleRate: sampleRate > 0 ? sampleRate : 48_000)
            guard let format = AVAudioFormat(standardFormatWithSampleRate: renderer.sampleRate, channels: 2) else { return nil }
            let node = Self.sourceNode(format: format, renderer: renderer)
            audio.attach(node)
            audio.connect(node, to: audio.mainMixerNode, format: format)
            try audio.start()
            let samples = try await Task.detached(priority: .userInitiated) { try Piano.load() }.value
            renderer.send(.samples(samples))
            self.renderer = renderer
            return renderer
        } catch {
            return nil
        }
    }

    nonisolated private static func sourceNode(format: AVAudioFormat, renderer: Player.Renderer) -> AVAudioSourceNode {
        AVAudioSourceNode(format: format) { _, _, frameCount, buffers in
            renderer.render(UnsafeMutableAudioBufferListPointer(buffers), frames: Int(frameCount))
            return noErr
        }
    }
}
