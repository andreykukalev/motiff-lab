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
/// places in order, in the draft's own ticks and bar numbers, and since step 7 its map: each place's variants and base,
/// and the shelf; since v1.2 (c-5) a bridge place and its candidates, and the anchors the player confirmed. The rest of
/// the answer is `open`'s for the score made of its places, whose summary tells nothing of where it came from (an
/// imported MIDI file): only this block does.
struct DraftViewAnswer: Decodable, Sendable {
    /// An anchor the player confirmed on a variant («Сопоставить…»): a tick of the place's base ("base", what
    /// `drafts.unanchor` names) and the variant's tick that sounds with it, where it stands in the draft ("at", its tick,
    /// and "bar", its bar).
    struct Anchor: Decodable, Equatable, Sendable {
        let base: Int
        let variant: Int
        let at: Int
        let bar: Int
    }

    /// A variant of a place: music-core's words for it («Запись 9–12 · правая рука»), whether it is the base, and how many
    /// steps take from it or leave it (they go with it when it leaves the place); in a bridge place the candidate's kind
    /// ("dominant", "commonChord", "bassWalk") and the chords it sounds, and on a variant that is not the base its anchors.
    struct Variant: Decodable, Equatable, Sendable {
        let id: String
        let label: String
        let base: Bool
        let steps: Int
        let bridge: String?
        let explanation: String?
        let anchors: [Anchor]?
    }

    /// A variant on the shelf, not yet in a place.
    struct Shelved: Decodable, Equatable, Sendable {
        let id: String
        let label: String
    }

    struct Place: Decodable, Equatable, Sendable {
        let id: String
        /// «① Ноты 9–16 + Запись · 2 варианта», music-core's words.
        let label: String
        let onset: Int
        let end: Int
        let firstBar: Int
        let lastBar: Int
        /// The journal's steps of the place, which a new base removes.
        let steps: Int
        /// A bridge between two places (v1.2), its variants the candidates.
        let bridge: Bool
        let variants: [Variant]
    }

    struct Draft: Decodable, Equatable, Sendable {
        let id: String
        let name: String
        let places: [Place]
        let shelf: [Shelved]
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

/// A draft's seams (`drafts.seams`, DraftSeamRequest.Seams, c-5): each boundary of two places in order, where it is (the
/// draft's tick and bars either side), the bar either side to hear it with the draft view's own playback ("listen"),
/// whether the player kept it as it is ("left"), what differs ("key", "meter", "tempo", since v1.2 "register"), each side
/// as its source states it, what music-core offers for the place after it (a transposition, an octave, the tempo before
/// it), each with its words, and since v1.2 (c-5) the register's words and the bridge it can put where the keys differ.
struct DraftSeamsAnswer: Decodable, Equatable, Sendable {
    struct Span: Decodable, Equatable, Sendable {
        let onset: Int
        let end: Int
    }

    struct Key: Decodable, Equatable, Sendable {
        let fifths: Int
        /// "major", "minor", or null where the source states only the signature.
        let mode: String?
        let name: String

        private enum CodingKeys: String, CodingKey { case fifths, mode, name }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            fifths = try fields.decode(Int.self, forKey: .fifths)
            mode = try fields.decodeNullable(String.self, forKey: .mode)
            name = try fields.decode(String.self, forKey: .name)
        }
    }

    struct Meter: Decodable, Equatable, Sendable {
        let beats: Int
        let beatType: Int
        let label: String
    }

    struct Tempo: Decodable, Equatable, Sendable {
        let perMinute: Int
        /// "notated", "inferred", "player", "default" or "assumed".
        let source: String
        /// "quarter" or "dottedQuarter", the meter's beat.
        let beat: String
        let beatsPerMinute: Int
        /// The mark as the score writes it after the beat's note («= 92», «≈ 92»); null for a recording's own clock.
        let mark: String?

        private enum CodingKeys: String, CodingKey { case perMinute, source, beat, beatsPerMinute, mark }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            perMinute = try fields.decode(Int.self, forKey: .perMinute)
            source = try fields.decode(String.self, forKey: .source)
            beat = try fields.decode(String.self, forKey: .beat)
            beatsPerMinute = try fields.decode(Int.self, forKey: .beatsPerMinute)
            mark = try fields.decodeNullable(String.self, forKey: .mark)
        }
    }

    struct Side: Decodable, Equatable, Sendable {
        let place: String
        /// The place's number, from 1 («①»).
        let number: Int
        let variant: String
        let key: Key?
        let meter: Meter
        let tempo: Tempo

        private enum CodingKeys: String, CodingKey { case place, number, variant, key, meter, tempo }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            place = try fields.decode(String.self, forKey: .place)
            number = try fields.decode(Int.self, forKey: .number)
            variant = try fields.decode(String.self, forKey: .variant)
            key = try fields.decodeNullable(Key.self, forKey: .key)
            meter = try fields.decode(Meter.self, forKey: .meter)
            tempo = try fields.decode(Tempo.self, forKey: .tempo)
        }
    }

    /// «② в соль мажор» ("transpose", with "semitones") or «Темп как в ①» ("tempo", with "quarterNotesPerMinute").
    struct Offer: Decodable, Equatable, Sendable {
        let kind: String
        let place: String
        let semitones: Int?
        let quarterNotesPerMinute: Int?
        let label: String
    }

    /// Where the places' mean pitches are an octave or more apart: which way the place after it lies ("higher", "lower")
    /// and music-core's words, «② на октаву выше».
    struct Register: Decodable, Equatable, Sendable {
        let direction: String
        let label: String
    }

    /// The bridge music-core can put before the place after the seam ("place"), where the keys differ: how many candidates
    /// and its words, «Связка · 3 кандидата».
    struct Bridge: Decodable, Equatable, Sendable {
        let place: String
        let candidates: Int
        let label: String
    }

    struct Seam: Decodable, Equatable, Sendable {
        let before: String
        let after: String
        let at: Int
        let lastBar: Int
        let firstBar: Int
        let listen: Span
        let left: Bool
        let changes: [String]
        let register: Register?
        let sides: [Side]
        let offers: [Offer]
        let bridge: Bridge?
    }

    let draft: String
    let seams: [Seam]
}

/// Another piece's drafts (`project.drafts` on piece 0 with its project file, DraftRequest.Of, v1.2 collage): its drafts as
/// its Sources list them; its summary, "project", is read by `PieceSummary(answer:)`. music-core writes nothing.
struct PieceDraftsAnswer: Decodable, Sendable {
    let drafts: [SourceLayersAnswer.Draft]
}

/// What «Принять» on the assistant's proposal in a draft came to (`SourcesController.acceptProposal`, v1.4): kept; refused
/// by music-core, with why in its words; or held, not sent, as another step of the draft was on its way, which leaves the
/// proposal as it was (`AssistantController.accept(_:by:)`).
enum DraftAcceptance: Equatable, Sendable {
    case kept
    case refused(String)
    case held
}

/// What «Найти на записи» finds for a place (`drafts.findOnRecording`, DraftRecordingRequest.Find, v1.4 c-3, F2), nothing
/// changed: on each recording of the piece heard as notes, the stretch that is the place's music, the most found first:
/// the recording's Notes, music-core's words for the variant it would make («Запись 9–16»), how much of it is in the
/// place's base, and the place's variant made of that very stretch already (null when none). Empty when no recording has
/// the place's music, and for a bridge place. Found for a new draft (`DraftNewRecordingAnswer`, v1.5 d-11), each also
/// tells how many приёмы the new draft's first place would have with it ("techniques"; nil for a place's).
struct DraftRecordingAnswer: Decodable, Equatable, Sendable {
    struct Found: Decodable, Equatable, Sendable {
        let notes: String
        let label: String
        let percent: Int
        let variant: String?
        let techniques: Int?

        private enum CodingKeys: String, CodingKey { case notes, label, percent, variant, techniques }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            notes = try fields.decode(String.self, forKey: .notes)
            label = try fields.decode(String.self, forKey: .label)
            percent = try fields.decode(Int.self, forKey: .percent)
            variant = try fields.decodeNullable(String.self, forKey: .variant)
            techniques = try fields.decodeIfPresent(Int.self, forKey: .techniques)
        }
    }

    let draft: String
    let place: String
    let found: [Found]
}

/// What the card «Новый черновик» finds on the piece's recordings for the passage selected, before the draft is made
/// (`drafts.findNewOnRecording`, DraftRecordingRequest.FindNew, v1.5 d-11), nothing changed: how many recordings were
/// searched (the Notes the passage is taken from left out: 0, nothing to say), and what was found, as for a place, each
/// with "techniques".
struct DraftNewRecordingAnswer: Decodable, Equatable, Sendable {
    let recordings: Int
    let found: [DraftRecordingAnswer.Found]
}

/// A place's length (`drafts.length`, DraftLengthRequest.Length, v1.4 c-3b), nothing changed: the bars of its source its
/// base was cut from ("bars", null when not known), the bars those Notes have ("most", null when they are not at hand),
/// whether it can be made longer or shorter, and why not in music-core's words ("reason", null when it can).
struct DraftLengthAnswer: Decodable, Equatable, Sendable {
    struct Bars: Decodable, Equatable, Sendable {
        let first: Int
        let last: Int
    }

    let draft: String
    let place: String
    let bars: Bars?
    let most: Bars?
    let resizable: Bool
    let reason: String?

    private enum CodingKeys: String, CodingKey { case draft, place, bars, most, resizable, reason }

    init(from decoder: Decoder) throws {
        let fields = try decoder.container(keyedBy: CodingKeys.self)
        draft = try fields.decode(String.self, forKey: .draft)
        place = try fields.decode(String.self, forKey: .place)
        bars = try fields.decodeNullable(Bars.self, forKey: .bars)
        most = try fields.decodeNullable(Bars.self, forKey: .most)
        resizable = try fields.decode(Bool.self, forKey: .resizable)
        reason = try fields.decodeNullable(String.self, forKey: .reason)
    }
}

/// Where a приём of a place can be carried (`drafts.carryPlaces`, DraftCarryRequest.Places, v1.4 c-3b), nothing changed:
/// each other place of the draft that is not a bridge, in order, with its label as the map names it and its own bars
/// (from 1) where the приём's shape fits, empty where it fits nowhere.
struct DraftCarryPlacesAnswer: Decodable, Equatable, Sendable {
    struct Place: Decodable, Equatable, Sendable {
        let place: String
        let label: String
        let bars: [Int]
    }

    let draft: String
    let place: String
    let technique: Int
    let places: [Place]
}

/// A fragment added to another piece's draft (`drafts.add` with "file", DraftRequest.AddTo, v1.2 collage): the draft, and
/// "file", that piece's project file changed, to save in place of the one sent exactly as music-core wrote it.
struct DraftAddedElsewhereAnswer: Decodable, Sendable {
    let draft: String
    let file: String
}
