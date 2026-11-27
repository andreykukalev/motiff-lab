import Foundation

// The ideas' typed answer (#318, v1.6 i-18), beside PieceAnswers.swift and by its rules: a field that is not optional is
// one music-core always writes; `decodeNullable` reads one it writes as null when it has no value.

/// The ideas kept with the project (`Answer.Ideas`): `ideas`, and `idea.keep`, which answers them with the one it kept:
/// each idea's id, its name, the passage it was kept on ("phrase") and the phrase anchor it is attached to (#318, null for
/// none).
struct IdeasAnswer: Decodable, Sendable {
    struct Idea: Decodable, Sendable {
        let id: String
        let name: String
        let phrase: Place
        let anchor: String?

        private enum CodingKeys: String, CodingKey { case id, name, phrase, anchor }

        init(from decoder: Decoder) throws {
            let fields = try decoder.container(keyedBy: CodingKeys.self)
            id = try fields.decode(String.self, forKey: .id)
            name = try fields.decode(String.self, forKey: .name)
            phrase = try fields.decode(Place.self, forKey: .phrase)
            anchor = try fields.decodeNullable(String.self, forKey: .anchor)
        }
    }

    /// A passage as `Answer.Place` writes it: its span in ticks and its first and last bar.
    struct Place: Decodable, Sendable {
        struct Span: Decodable, Sendable {
            let onset: Double
            let end: Double
        }

        struct Bars: Decodable, Sendable {
            let first: Int
            let last: Int
        }

        let span: Span
        let bars: Bars
    }

    let ideas: [Idea]
}
