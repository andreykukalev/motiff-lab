import Foundation

// The typed answers of music-core (step B5, blueprint 5.4, audit C3): each mirrors the JSON a facade writer writes
// (src/backend/MotiffLab.MusicCore/Facade/Answer*.cs), field for field with its name and type, for the fields the app reads.
// `FacadeAnswer.decode` reads an answer into one once; `PieceCore` makes the calls. A field that is not optional here is one
// music-core always writes: missing or of another type, the answer is not read and the broken contract is reported. An
// optional field is one music-core leaves out by design, or one the app read with a default before it was typed (step A7),
// kept so. tests/contracts/facade-answers holds the answers on the example piece; FacadeAnswerFixturesTests decodes them.

/// `select`, and `phrases.level` with its selection (`Answer.Selection`): {"selection": {…}}, null in `phrases.level`
/// with none selected.
struct SelectionAnswer: Decodable, Sendable {
    struct Selection: Decodable, Sendable {
        let onset: Double
        let end: Double
        let firstBar: Int
        let lastBar: Int
        let firstBeat: Int
        let lastBeat: Int
        let noteIds: [String]
        let filter: Filter
    }

    /// `Answer.Filter`: names a hand only when the selection is narrowed to one.
    struct Filter: Decodable, Sendable {
        let hand: String?
    }

    /// Always written, null with none.
    let selection: Selection?

    private enum CodingKeys: String, CodingKey { case selection }

    init(from decoder: Decoder) throws {
        selection = try decoder.container(keyedBy: CodingKeys.self).decodeNullable(Selection.self, forKey: .selection)
    }
}

/// The playback score (`Answer.Playback`: `playback`, `performance.merge.playback`, the reference's sounds): notes with
/// the pedal already in their lengths, tempos, the tempo shown, beats and bars; "hands" only `playback` writes.
struct PlaybackAnswer: Decodable, Sendable {
    struct Note: Decodable, Sendable {
        let onset: Double
        let duration: Double
        let pitch: Int
        let velocity: Int
        let track: Int
    }

    struct TempoChange: Decodable, Sendable {
        let at: Double
        let microsecondsPerQuarter: Double
    }

    /// A beat; "bar" and "beat" (within contract 12) were read with none when missing, so they stay optional.
    struct Beat: Decodable, Sendable {
        let at: Double
        let downbeat: Bool
        let bar: Int?
        let beat: Int?
    }

    struct Bar: Decodable, Sendable {
        let number: Int
        let onset: Double
        let beatTicks: Double
        let beatsPerBar: Double
    }

    /// "hands": each read as false when missing.
    struct Hands: Decodable, Sendable {
        let right: Bool?
        let left: Bool?
        let inferred: Bool?
    }

    /// "tempo" (`Answer.PlaybackTempoAnswer`, v0.5.4): read as no tempo when its source or the piece's is missing.
    struct StartTempo: Decodable, Sendable {
        let perMinute: Double?
        let source: String?
        let beat: String?
        let beatsPerMinute: Int?
        let piece: PieceTempo?
        let player: Int?
    }

    struct PieceTempo: Decodable, Sendable {
        let perMinute: Int?
        let source: String?
        let beatsPerMinute: Int?
    }

    let end: Double
    let notes: [Note]
    let tempos: [TempoChange]
    let tempo: StartTempo?
    let beats: [Beat]
    let bars: [Bar]
    let hands: Hands?
}

/// The piece's sources by the layers of decision record 0030 (`Answer.SourceLayers`): `sources.layers`, and the answer of
/// each of the Sources sheet's operations, which adds the "id" of what it made. music-core writes every field; the ones
/// with no value it writes null (`decodeNullable`).
struct SourceLayersAnswer: Decodable, Sendable {
    struct Original: Decodable, Sendable {
        let id: String
        let kind: String
        let shown: String
        let fileName: String
        let state: String
        /// ISO 8601, null unless the original was removed.
        let removedAt: String?
        let kept: String
        let pages: Int?
        let durationMs: Int?
        let hidden: Bool
        let notes: [String]
        let pageMarks: Int

        private enum CodingKeys: String, CodingKey {
            case id, kind, shown, fileName, state, removedAt, kept, pages, durationMs, hidden, notes, pageMarks
        }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            id = try fields.decode(String.self, forKey: .id)
            kind = try fields.decode(String.self, forKey: .kind)
            shown = try fields.decode(String.self, forKey: .shown)
            fileName = try fields.decode(String.self, forKey: .fileName)
            state = try fields.decode(String.self, forKey: .state)
            removedAt = try fields.decodeNullable(String.self, forKey: .removedAt)
            kept = try fields.decode(String.self, forKey: .kept)
            pages = try fields.decodeNullable(Int.self, forKey: .pages)
            durationMs = try fields.decodeNullable(Int.self, forKey: .durationMs)
            hidden = try fields.decode(Bool.self, forKey: .hidden)
            notes = try fields.decode([String].self, forKey: .notes)
            pageMarks = try fields.decode(Int.self, forKey: .pageMarks)
        }
    }

    struct Notes: Decodable, Sendable {
        let id: String
        let role: String
        let name: String?
        let op: String
        let engine: String?
        /// ISO 8601, the provenance's time; null without one.
        let at: String?
        /// Null for Notes of no original (a version).
        let original: String?
        let state: String?
        let edits: Int
        let taken: Int
        let hidden: Bool

        private enum CodingKeys: String, CodingKey { case id, role, name, op, engine, at, original, state, edits, taken, hidden }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            id = try fields.decode(String.self, forKey: .id)
            role = try fields.decode(String.self, forKey: .role)
            name = try fields.decodeNullable(String.self, forKey: .name)
            op = try fields.decode(String.self, forKey: .op)
            engine = try fields.decodeNullable(String.self, forKey: .engine)
            at = try fields.decodeNullable(String.self, forKey: .at)
            original = try fields.decodeNullable(String.self, forKey: .original)
            state = try fields.decodeNullable(String.self, forKey: .state)
            edits = try fields.decode(Int.self, forKey: .edits)
            taken = try fields.decode(Int.self, forKey: .taken)
            hidden = try fields.decode(Bool.self, forKey: .hidden)
        }
    }

    struct View: Decodable, Sendable {
        let id: String
        let kind: String
        /// Null for Notes of no original here.
        let group: String?
        let working: Bool
        let hidden: Bool
        let op: String

        private enum CodingKeys: String, CodingKey { case id, kind, group, working, hidden, op }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            id = try fields.decode(String.self, forKey: .id)
            kind = try fields.decode(String.self, forKey: .kind)
            group = try fields.decodeNullable(String.self, forKey: .group)
            working = try fields.decode(Bool.self, forKey: .working)
            hidden = try fields.decode(Bool.self, forKey: .hidden)
            op = try fields.decode(String.self, forKey: .op)
        }
    }

    /// The working notes' id, null for a piece that is only its PDF.
    let working: String?
    let originals: [Original]
    let notes: [Notes]
    let views: [View]
    /// How many views are hidden from the switch.
    let hidden: Int

    private enum CodingKeys: String, CodingKey { case working, originals, notes, views, hidden }

    init(from decoder: Decoder) throws {
        let fields = try decoder.container(keyedBy: CodingKeys.self)
        working = try fields.decodeNullable(String.self, forKey: .working)
        originals = try fields.decode([Original].self, forKey: .originals)
        notes = try fields.decode([Notes].self, forKey: .notes)
        views = try fields.decode([View].self, forKey: .views)
        hidden = try fields.decode(Int.self, forKey: .hidden)
    }
}

extension KeyedDecodingContainer {
    /// A field music-core always writes, null when it has no value: nil for null; a missing key throws, as for any field
    /// it always writes.
    func decodeNullable<T: Decodable>(_ type: T.Type, forKey key: Key) throws -> T? {
        guard contains(key) else {
            throw DecodingError.keyNotFound(key, .init(codingPath: codingPath, debugDescription: "music-core always writes \"\(key.stringValue)\""))
        }
        return try decodeNil(forKey: key) ? nil : decode(type, forKey: key)
    }
}
