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

/// A place's приёмы (`drafts.techniques`, DraftTakeRequest.Techniques, v1.1 c-4): how many there are, how many are taken,
/// and each приём with its number, the variant it is in, its kind as the engine read it ("inferred", always true), its
/// label in words («октава»), how sure it is, its state ("open", "taken", "left") and where it occurs: the place's bar
/// ("bar", as `drafts.take` names it), the draft's own bars and ticks (an inserted bar not yet put in has onset = end).
struct DraftTechniquesAnswer: Decodable, Equatable, Sendable {
    struct Occurrence: Decodable, Equatable, Sendable {
        let bar: Int
        let firstBar: Int
        let lastBar: Int
        let onset: Int
        let end: Int
        let confidence: Int
        let state: String
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
    let total: Int
    let taken: Int
    let sure: Int
    let techniques: [Technique]
}
