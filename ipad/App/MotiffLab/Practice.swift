import Foundation

/// Which hands sound (#328, an experiment, Settings · Experimental): both, or one alone, as music-core gives that hand's
/// notes (`playback` with "hands"). The hands are the ones the notes carry, read from the file or inferred by music-core;
/// the app never works out a hand itself.
enum PlayerHands: String, CaseIterable, Sendable {
    case both, right, left

    /// The pill's words for it, the web app's.
    var labelKey: String {
        switch self {
        case .both: "listen.bothHands"
        case .right: "filter.rightHand"
        case .left: "filter.leftHand"
        }
    }
}

/// Which hands the piece's notes carry, as music-core's `playback` answers ("hands"), and whether any was inferred, so
/// the pill can say the hands are a reading of the notes rather than the file's.
struct HandsHeld: Equatable, Sendable {
    var right = false
    var left = false
    var inferred = false

    /// Whether one hand can be heard alone: the notes carry both.
    var canSeparate: Bool { right && left }

    init(right: Bool = false, left: Bool = false, inferred: Bool = false) {
        self.right = right
        self.left = left
        self.inferred = inferred
    }

    /// The hands in a `playback` answer; none when it names none.
    init(answer object: [String: Any]?) {
        let hands = object?["hands"] as? [String: Any]
        self.init(right: hands?["right"] as? Bool ?? false, left: hands?["left"] as? Bool ?? false, inferred: hands?["inferred"] as? Bool ?? false)
    }
}

/// Where the player stopped in a piece (#327, an experiment, Settings · Experimental): the latest of the project's resume
/// points, as music-core's `resume` lists them, most recent first. The web app saves the same points in the same project
/// file, so a piece left on the web resumes here at its passage too. The point is kept with `resume.save` when the piece
/// closes or the app goes to the background; the project file's format does not change.
struct ResumePlace: Equatable, Sendable {
    var id: String
    /// Where the player was, in ticks: the passage's start, or the place in the piece when nothing was selected.
    var at: Double
    var passage: Range<Double>?
    var focused: Bool
    var tempoPercent: Int
    var hands: PlayerHands
    var loop: Bool

    /// The latest resume point in a `resume` answer; nil for a piece with none, which opens at its start.
    init?(answer: String) {
        guard let points = facadeObject(answer)?["resumePoints"] as? [[String: Any]], let latest = points.first else { return nil }
        self.init(point: latest)
    }

    /// One resume point as music-core writes it (`resume`, and `saved` in `resume.save`).
    init?(point: [String: Any]) {
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        guard let id = point["id"] as? String else { return nil }
        self.id = id
        at = number(point["at"]) ?? 0
        let span = (point["passage"] as? [String: Any])?["span"] as? [String: Any]
        if let onset = number(span?["onset"]), let end = number(span?["end"]), onset < end {
            passage = onset..<end
        } else {
            passage = nil
        }
        focused = point["focused"] as? Bool ?? false
        tempoPercent = (point["tempoPercent"] as? NSNumber)?.intValue ?? 100
        hands = PlayerHands(rawValue: point["hands"] as? String ?? "both") ?? .both
        loop = point["loop"] as? Bool ?? false
    }

    /// Where the player is when the piece closes or the app goes to the background, taken before the player stops: the
    /// passage selected, the playhead, focus, the tempo, the hands when hands separately is on, and whether it loops.
    struct Now: Equatable, Sendable {
        var passage: Range<Double>?
        var tick: Double
        var focused: Bool
        var tempoPercent: Int
        var hands: PlayerHands?
        var loop: Bool
    }

    /// The tempo a resume point can hold (music-core's `ResumePoint`): 25 to 150 percent.
    static let tempoRange = 25...150

    /// What `resume.save` is sent when the piece closes: the point `id` brought up to date (a new one without it), with the
    /// passage selected or none, the place, focus, the tempo, the loop and, when given, the hands; hands not given stay as
    /// the point has them (#327 item 5).
    static func saveRequest(id: String?, passage: Range<Double>?, at: Double, focused: Bool, tempoPercent: Int, hands: PlayerHands?, loop: Bool, now: Date) -> String {
        var fields: [String: Any] = [
            "at": Int(max(0, passage?.lowerBound ?? at)),
            "focused": focused && passage != nil,
            "tempoPercent": min(tempoRange.upperBound, max(tempoRange.lowerBound, tempoPercent)),
            "loop": loop && passage != nil,
            "now": ISO8601DateFormatter().string(from: now),
        ]
        if let id { fields["id"] = id }
        fields["passage"] = passage.map { ["span": ["onset": Int($0.lowerBound), "end": Int($0.upperBound)]] } ?? NSNull()
        if let hands { fields["hands"] = hands.rawValue }
        let data = (try? JSONSerialization.data(withJSONObject: fields)) ?? Data("{}".utf8)
        return String(decoding: data, as: UTF8.self)
    }
}
