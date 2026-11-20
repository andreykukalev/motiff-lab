import Foundation

// The typed answers of music-core's drafts calls (v1.1, ADR 0034), as PieceAnswers.swift's: each mirrors its facade writer
// field for field, for the fields the app reads; `PieceCore` makes the calls.

/// The «В черновик» sheet for a selection (`drafts.sheet`, DraftRequest.Sheet, v1.1): what can be taken there ("all",
/// "right", "left", "melody"), the part asked for, how many notes the fragment holds, the name a new draft takes, the
/// piece's drafts with each place's label and how much of the fragment is the same music there, and the place offered
/// first of all ("suggested", null when none).
struct DraftSheetAnswer: Decodable, Sendable {
    struct Place: Decodable, Sendable {
        let id: String
        let label: String
        let percent: Int
        let sameMusic: Bool
    }

    struct Draft: Decodable, Sendable {
        let id: String
        let name: String
        /// The place of this draft offered first, null when none is the same music.
        let suggested: String?
        let places: [Place]

        private enum CodingKeys: String, CodingKey { case id, name, suggested, places }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            id = try fields.decode(String.self, forKey: .id)
            name = try fields.decode(String.self, forKey: .name)
            suggested = try fields.decodeNullable(String.self, forKey: .suggested)
            places = try fields.decode([Place].self, forKey: .places)
        }
    }

    struct Suggested: Decodable, Sendable {
        let draft: String
        let place: String
        let percent: Int
    }

    let parts: [String]
    let part: String
    let notes: Int
    let newName: String
    let drafts: [Draft]
    let suggested: Suggested?

    private enum CodingKeys: String, CodingKey { case parts, part, notes, newName, drafts, suggested }

    init(from decoder: Decoder) throws {
        let fields = try decoder.container(keyedBy: CodingKeys.self)
        parts = try fields.decode([String].self, forKey: .parts)
        part = try fields.decode(String.self, forKey: .part)
        notes = try fields.decode(Int.self, forKey: .notes)
        newName = try fields.decode(String.self, forKey: .newName)
        drafts = try fields.decode([Draft].self, forKey: .drafts)
        suggested = try fields.decodeNullable(Suggested.self, forKey: .suggested)
    }
}

/// A draft opened to be shown (`drafts.open`, DraftRequest.View): its "draft" block, where the draft came from and its
/// places in order, in the draft's own ticks and bar numbers. The rest of the answer is `open`'s for the score made of
/// its places, whose summary tells nothing of where it came from (an imported MIDI file): only this block does.
struct DraftViewAnswer: Decodable, Sendable {
    struct Place: Decodable, Equatable, Sendable {
        let id: String
        /// «① Ноты 9–16 + Запись · 2 варианта», music-core's words.
        let label: String
        let onset: Int
        let end: Int
        let firstBar: Int
        let lastBar: Int
        let variants: Int
    }

    struct Draft: Decodable, Equatable, Sendable {
        let id: String
        let name: String
        let places: [Place]
    }

    let piece: Int32
    let draft: Draft
}

/// A place's приёмы (`drafts.techniques`, DraftTakeRequest.Techniques, v1.1 c-4 and c-4b): how many there are, how many are taken,
/// and each приём with its number, the variant it is in, its kind as the engine read it ("inferred", always true), its
/// label in words («октава»), how sure it is, its state ("open", "taken", "left") and where it occurs: the place's bar
/// ("bar", as `drafts.take` names it), the draft's own bars and ticks (an inserted bar not yet put in has onset = end);
/// since c-4b how many of its notes are in place, and how each side is heard: the place's base variant ("base") and the
/// occurrence in its ticks ("inBase", onset = end for bars not yet put in), the variant's own ticks, and the stretch of
/// the recording it was heard from (null when it was not, or its piece is not open).
struct DraftTechniquesAnswer: Decodable, Equatable, Sendable {
    struct Span: Decodable, Equatable, Sendable {
        let onset: Int
        let end: Int
    }

    /// A stretch of a recording, in milliseconds of its file (`OriginalSounds.file(project:original:)`).
    struct Recording: Decodable, Equatable, Sendable {
        let project: String
        let original: String
        let from: Int
        let to: Int
    }

    struct Occurrence: Decodable, Equatable, Sendable {
        let bar: Int
        let firstBar: Int
        let lastBar: Int
        let onset: Int
        let end: Int
        let matched: Int
        let total: Int
        let state: String
        let inBase: Span
        let inVariant: Span
        let recording: Recording?

        private enum CodingKeys: String, CodingKey { case bar, firstBar, lastBar, onset, end, matched, total, state, inBase, inVariant, recording }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            bar = try fields.decode(Int.self, forKey: .bar)
            firstBar = try fields.decode(Int.self, forKey: .firstBar)
            lastBar = try fields.decode(Int.self, forKey: .lastBar)
            onset = try fields.decode(Int.self, forKey: .onset)
            end = try fields.decode(Int.self, forKey: .end)
            matched = try fields.decode(Int.self, forKey: .matched)
            total = try fields.decode(Int.self, forKey: .total)
            state = try fields.decode(String.self, forKey: .state)
            inBase = try fields.decode(Span.self, forKey: .inBase)
            inVariant = try fields.decode(Span.self, forKey: .inVariant)
            recording = try fields.decodeNullable(Recording.self, forKey: .recording)
        }
    }

    struct Technique: Decodable, Equatable, Sendable {
        let number: Int
        let variant: String
        let kind: String
        let label: String
        let inferred: Bool
        let confidence: Int
        let sure: Bool
        let state: String
        let occurrences: [Occurrence]
    }

    let draft: String
    let place: String
    /// The place's base variant.
    let base: String
    let total: Int
    let taken: Int
    let sure: Int
    let techniques: [Technique]
}

/// A variant's own music opened to be heard (`drafts.open` with "variant", c-4b): the number it is open under, read-only;
/// the rest is `open`'s summary, which the app does not read here.
struct DraftVariantAnswer: Decodable, Sendable {
    let piece: Int32
    let variant: String
}
