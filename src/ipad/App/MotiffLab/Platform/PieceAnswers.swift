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
