import Foundation

// The typed answers of music-core (step B5, blueprint 5.4, audit C3): each mirrors the JSON a facade writer writes
// (src/backend/MotiffLab.MusicCore/Facade/Answer*.cs), field for field with its name and type, for the fields the app reads.
// `FacadeAnswer.decode` reads an answer into one once; `PieceCore` makes the calls. A field that is not optional here is one
// music-core always writes: missing or of another type, the answer is not read and the broken contract is reported. An
// optional field is one music-core leaves out by design, or one the app read with a default before it was typed (step A7),
// kept so. tests/contracts/facade-answers holds the answers on the example piece; FacadeAnswerFixturesTests decodes them.

/// `select` (`Answer.Selection`): {"selection": {…}}.
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

/// The phrases of the piece (`Answer.Phrases`): `phrases` and `phrases.edit`. The units are read as before they were typed:
/// every field but "units" with the default it had, and a unit without its number, onset or end left out.
struct PhrasesAnswer: Decodable, Sendable {
    struct Unit: Decodable, Sendable {
        let number: Int?
        let name: String?
        let firstBar: Int?
        let lastBar: Int?
        let onset: Double?
        let end: Double?
        let selectable: SpanAnswer?
        let start: Start?
        let motifs: [Motif]?
    }

    /// A motif of a phrase (the phrases spec, decision 2), drawn in focus as a small comma that a tap selects: where it
    /// starts and ends, and its bars.
    struct Motif: Decodable, Equatable, Sendable {
        let onset: Double
        let end: Double
        let firstBar: Int
        let lastBar: Int
    }

    /// A phrase's start, its mark (the phrases spec, decision 1): how it was found ("imported", "inferred"; "userEdited",
    /// "userConfirmed" for the player's own), the bar and beat of its start note, «Почему здесь» in the player's
    /// language (the Russian completes «начинается …»), and for a found start the player moved, where it was found.
    struct Start: Decodable, Sendable {
        let provenance: String?
        let bar: Int?
        let beat: Int?
        let why: String?
        let found: Found?
    }

    /// Where a start the player moved was found: its tick, bar and beat.
    struct Found: Decodable, Equatable, Sendable {
        let at: Double
        let bar: Int
        let beat: Int
    }

    /// A section of the form (`Answer.FormSection`, A, B, A′…): its letter, where it is, the sections it shares its music
    /// with, and the span a tap selects (at most 32 bars). The app's type is `FormSection`.
    struct Section: Decodable, Sendable {
        let id: String
        let label: String
        let passage: Passage
        let selectable: SpanAnswer?
        let sharesWith: [String]?
    }

    struct Passage: Decodable, Sendable {
        let onset: Double
        let end: Double
        let firstBar: Int
        let lastBar: Int
    }

    /// A phrase an edit changed (`phrases.edit` "changed"): as it was, numbered as it was, and the phrase now that covers
    /// most of it, each with the span a tap selects.
    struct Change: Decodable, Equatable, Sendable {
        let before: Changed
        let after: Changed
    }

    struct Changed: Decodable, Equatable, Sendable {
        let number: Int
        let onset: Double
        let end: Double
        let selectable: SpanAnswer?

        /// The span a tap on it selects, [onset, end].
        var span: [Double] { [selectable?.onset ?? onset, selectable?.end ?? end] }
    }

    let units: [Unit]
    let sections: [Section]?
    /// The player's edits of the boundaries now, and (from `phrases.edit`) as they were before the edit: one step of Undo.
    let edits: PhraseLevelEdits?
    let previous: PhraseLevelEdits?
    /// How many boundaries are the player's own («Ваших границ: N»), and (from `phrases.edit`) the phrases the edit changed.
    let own: Int?
    let changed: [Change]?
}

/// The player's edits of the boundaries as music-core answers them ("edits", "previous"): each level's starts added and
/// removed, in ticks, sent back as they are to undo or redo a step (`phrases.edit` "restore" and "expected"; a level left
/// out has none).
struct PhraseLevelEdits: Codable, Equatable, Sendable {
    struct Edits: Codable, Equatable, Sendable {
        var added: [Int]
        var removed: [Int]
    }

    var short: Edits?
    var phrase: Edits?
    var sentence: Edits?

    /// The edits as JSON, for a request.
    var json: String {
        (try? JSONEncoder().encode(self)).flatMap { String(data: $0, encoding: .utf8) } ?? "{}"
    }
}

/// The playback score (`Answer.Playback`: `playback`, the reference's sounds): notes with
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

/// The piano roll's data (`Answer.Roll`, `PianoRollData`): `roll`, the notes and bars packed as `Scripts/roll/roll-model.ts`
/// reads them, and their names spelled for the keys. The app reads none of it: the roll page gets it whole
/// (`RollPage.load`), every field music-core writes.
struct RollAnswer: Decodable, Sendable {
    /// `RollNames`: one name per note, per key signature (from its tick) one per row, the same without the octave, and
    /// each octave's name in words.
    struct Names: Decodable, Sendable {
        let notes: [String]
        let keyTicks: [Int]
        let rows: [[String]]
        let short: [String]
        let shortRows: [[String]]
        let octaves: [String]
    }

    let ids: [String]
    /// Seven numbers a note: onset, duration, pitch, track, voice, hand and what is inferred.
    let notes: [Int]
    /// Four numbers a bar: onset, duration, beat length and number.
    let bars: [Int]
    let end: Int
    let names: Names
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

    /// A draft of the piece, for the Sources group «Черновики» (v1.1, ADR 0034): its name, the caption music-core words,
    /// how many places it has, how many variants wait on its shelf, and how many of the приёмы of all its places are taken
    /// (step 6, c-4b).
    struct Draft: Decodable, Sendable {
        struct Techniques: Decodable, Sendable {
            let taken: Int
            let total: Int
        }

        let id: String
        let name: String
        let caption: String
        let createdAt: String
        let places: Int
        let shelf: Int
        let techniques: Techniques
    }

    /// The working notes' id, null for a piece that is only its PDF.
    let working: String?
    let originals: [Original]
    let notes: [Notes]
    let views: [View]
    /// How many views are hidden from the switch.
    let hidden: Int
    /// The piece's drafts in the order they were made (since v1.1).
    let drafts: [Draft]

    private enum CodingKeys: String, CodingKey { case working, originals, notes, views, hidden, drafts }

    init(from decoder: Decoder) throws {
        let fields = try decoder.container(keyedBy: CodingKeys.self)
        working = try fields.decodeNullable(String.self, forKey: .working)
        originals = try fields.decode([Original].self, forKey: .originals)
        notes = try fields.decode([Notes].self, forKey: .notes)
        views = try fields.decode([View].self, forKey: .views)
        hidden = try fields.decode(Int.self, forKey: .hidden)
        drafts = try fields.decode([Draft].self, forKey: .drafts)
    }
}

/// A bar's notes to correct (`CorrectionsAnswer` in OpenPiece.Corrections.cs), the answer of every corrections call: the
/// bar's notes as they sound, the imported notes taken out anywhere, the note editor's draft, and the count of corrections
/// with the layer's fields (`edits`, read from the same object). "added" only `corrections.add` writes.
struct CorrectionsAnswer: Decodable, Sendable {
    struct Note: Decodable, Sendable {
        let id: String
        let pitch: Int
        let name: String
        let atBar: Int
        let beat: Int
        /// The rest of the beat in words, null on the beat.
        let offset: String?
        let length: String
        let velocity: Int
        let edited: Bool
        let taken: Bool
        let canEarlier: Bool
        let canShorter: Bool

        private enum CodingKeys: String, CodingKey {
            case id, pitch, name, atBar, beat, offset, length, velocity, edited, taken, canEarlier, canShorter
        }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            id = try fields.decode(String.self, forKey: .id)
            pitch = try fields.decode(Int.self, forKey: .pitch)
            name = try fields.decode(String.self, forKey: .name)
            atBar = try fields.decode(Int.self, forKey: .atBar)
            beat = try fields.decode(Int.self, forKey: .beat)
            offset = try fields.decodeNullable(String.self, forKey: .offset)
            length = try fields.decode(String.self, forKey: .length)
            velocity = try fields.decode(Int.self, forKey: .velocity)
            edited = try fields.decode(Bool.self, forKey: .edited)
            taken = try fields.decode(Bool.self, forKey: .taken)
            canEarlier = try fields.decode(Bool.self, forKey: .canEarlier)
            canShorter = try fields.decode(Bool.self, forKey: .canShorter)
        }
    }

    /// An imported note taken out; "offset" as a note's.
    struct Removed: Decodable, Sendable {
        let id: String
        let name: String
        let atBar: Int
        let beat: Int
        let offset: String?

        private enum CodingKeys: String, CodingKey { case id, name, atBar, beat, offset }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            id = try fields.decode(String.self, forKey: .id)
            name = try fields.decode(String.self, forKey: .name)
            atBar = try fields.decode(Int.self, forKey: .atBar)
            beat = try fields.decode(Int.self, forKey: .beat)
            offset = try fields.decodeNullable(String.self, forKey: .offset)
        }
    }

    /// The id of the note `corrections.add` added; none in the other answers.
    let added: String?
    let bar: Int
    let notes: [Note]
    let removed: [Removed]
    let canUndo: Bool
    let changed: Bool
    let edits: EditsLayerAnswer

    private enum CodingKeys: String, CodingKey { case added, bar, notes, removed, canUndo, changed }

    init(from decoder: Decoder) throws {
        let fields = try decoder.container(keyedBy: CodingKeys.self)
        added = try fields.decodeIfPresent(String.self, forKey: .added)
        bar = try fields.decode(Int.self, forKey: .bar)
        notes = try fields.decode([Note].self, forKey: .notes)
        removed = try fields.decode([Removed].self, forKey: .removed)
        canUndo = try fields.decode(Bool.self, forKey: .canUndo)
        changed = try fields.decode(Bool.self, forKey: .changed)
        edits = try EditsLayerAnswer(from: decoder)
    }
}

/// «Мои правки» as music-core answers it: "count", the corrections in the edit log, and with a layer (`LayerFields` in
/// OpenPiece.EditsLayer.cs) "layer" ("on" or "off"), "moved" and "unfit"; a piece opened strictly writes none of these
/// three. In every corrections answer, and as the open answer's "edits" (`OpenedEdits`).
struct EditsLayerAnswer: Decodable, Sendable {
    /// A correction laid on a near note of its pitch, not on the one it names.
    struct Moved: Decodable, Sendable {
        let noteId: String
        let to: String
        let bar: Int
    }

    /// A correction that no longer fits the notes, with music-core's reason and words.
    struct Unfit: Decodable, Sendable {
        let noteId: String
        let bar: Int
        let reason: String
        let text: String
    }

    let count: Int
    let layer: String?
    let moved: [Moved]?
    let unfit: [Unfit]?
}

/// The open answer's "edits" (`MusicCore.open`, OpenPiece.cs), written only for a piece opened with a layer.
struct OpenedEdits: Decodable, Sendable {
    let edits: EditsLayerAnswer?
}

/// A version compared with the piece bar by bar (`Answer.Performance`, decision record 0022): the answer of `performance`
/// and of the changes to a version. Read as before it was
/// typed: "performance" is needed, its fields each with the default it had, and a bar without its number left out.
struct PerformanceAnswer: Decodable, Sendable {
    struct Performance: Decodable, Sendable {
        let head: VersionHead
        let matched: Int?
        let printed: Int?
        let bars: [Bar]?

        private enum CodingKeys: String, CodingKey { case matched, printed, bars }

        init(from decoder: Decoder) throws {
            head = try VersionHead(from: decoder)
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            matched = try fields.decodeIfPresent(Int.self, forKey: .matched)
            printed = try fields.decodeIfPresent(Int.self, forKey: .printed)
            bars = try fields.decodeIfPresent([Bar].self, forKey: .bars)
        }
    }

    struct Bar: Decodable, Sendable {
        let bar: Int?
        let span: SpanAnswer?
        let aligned: Bool?
        let kind: String?
        let marker: String?
        let dismissed: Bool?
        let taken: Bool?
        let notHeard: [String]?
        /// Only how many there are is read.
        let played: [Counted]?
        let artefacts: [Counted]?
    }

    let performance: Performance
}

/// The versions of the piece (`Answer.Performances`): `performances`. A missing list is none.
struct PerformancesAnswer: Decodable, Sendable {
    let performances: [VersionHead]?
}

/// A version as `PerformanceHead` writes it, in `performances` and at the head of `performance`; read with the defaults it
/// had, and left out without its id.
struct VersionHead: Decodable, Sendable {
    let id: String?
    let name: String?
}

/// A span in ticks (`Answer.Span`), each end read as none when missing.
struct SpanAnswer: Decodable, Equatable, Sendable {
    let onset: Double?
    let end: Double?
}

/// An entry of a list the app only counts: any JSON value, read as nothing.
struct Counted: Decodable, Sendable {
    init(from decoder: Decoder) throws {}
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
