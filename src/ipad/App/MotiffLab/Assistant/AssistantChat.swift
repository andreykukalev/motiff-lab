import Foundation

/// A JSON value as music-core and the Motiff Lab API write it, kept as it came (integers stay integers), so the device can
/// keep a conversation, send it back and look into it without knowing every shape.
enum JSONValue: Codable, Equatable, Sendable {
    case null
    case bool(Bool)
    case int(Int)
    case double(Double)
    case string(String)
    case array([JSONValue])
    case object([String: JSONValue])

    init(from decoder: Decoder) throws {
        let value = try decoder.singleValueContainer()
        if value.decodeNil() {
            self = .null
        } else if let bool = try? value.decode(Bool.self) {
            self = .bool(bool)
        } else if let int = try? value.decode(Int.self) {
            self = .int(int)
        } else if let double = try? value.decode(Double.self) {
            self = .double(double)
        } else if let string = try? value.decode(String.self) {
            self = .string(string)
        } else if let array = try? value.decode([JSONValue].self) {
            self = .array(array)
        } else {
            self = .object(try value.decode([String: JSONValue].self))
        }
    }

    func encode(to encoder: Encoder) throws {
        var value = encoder.singleValueContainer()
        switch self {
        case .null: try value.encodeNil()
        case let .bool(bool): try value.encode(bool)
        case let .int(int): try value.encode(int)
        case let .double(double): try value.encode(double)
        case let .string(string): try value.encode(string)
        case let .array(array): try value.encode(array)
        case let .object(object): try value.encode(object)
        }
    }

    /// The value in a JSON text; nil when it is not JSON.
    static func parse(_ text: String) -> JSONValue? {
        parse(Data(text.utf8))
    }

    static func parse(_ data: Data) -> JSONValue? {
        try? JSONDecoder().decode(JSONValue.self, from: data)
    }

    /// The value as JSON text, keys in a stable order.
    var data: Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return (try? encoder.encode(self)) ?? Data("null".utf8)
    }

    var text: String { String(decoding: data, as: UTF8.self) }

    /// The value as Foundation's JSON objects, for a request built with `facadeRequest`.
    var foundation: Any {
        (try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])) ?? NSNull()
    }

    subscript(key: String) -> JSONValue? {
        if case let .object(object) = self { return object[key] }
        return nil
    }

    var string: String? {
        if case let .string(string) = self { return string }
        return nil
    }

    var int: Int? {
        if case let .int(int) = self { return int }
        return nil
    }

    var items: [JSONValue] {
        if case let .array(array) = self { return array }
        return []
    }

    var isTrue: Bool { self == .bool(true) }

    /// The first object anywhere in the value with this "id" and a "text": a fact of a digest or of a tool result,
    /// which a citation names.
    func fact(_ id: String) -> (text: String, bars: ClosedRange<Int>?)? {
        switch self {
        case let .object(object):
            if object["id"]?.string == id, let text = object["text"]?.string {
                let bars = object["bars"].flatMap { bars -> ClosedRange<Int>? in
                    guard let from = bars["from"]?.int, let to = bars["to"]?.int, from <= to else { return nil }
                    return from...to
                }
                return (text, bars)
            }
            for key in object.keys.sorted() {
                if let found = object[key]?.fact(id) { return found }
            }
            return nil
        case let .array(array):
            for item in array {
                if let found = item.fact(id) { return found }
            }
            return nil
        default:
            return nil
        }
    }
}

/// A tool call of the model, to run on the device, and what it answered (music-core's ToolCall and ToolOutcome).
struct AssistantToolCall: Codable, Equatable, Sendable {
    var id: String
    var name: String
    var arguments: JSONValue
}

struct AssistantToolOutcome: Codable, Equatable, Sendable {
    var id: String
    var name: String
    var content: JSONValue
}

/// One entry of the history a turn sends (format v1, music-core's TurnEntry): a question, the model's tool calls, the
/// device's results, or an answer.
struct AssistantEntry: Codable, Equatable, Sendable {
    enum Kind: String, Codable, Sendable {
        case question, toolCalls, toolResults, answer
    }

    var kind: Kind
    var text: String?
    var calls: [AssistantToolCall]?
    var results: [AssistantToolOutcome]?

    static func question(_ text: String) -> AssistantEntry { AssistantEntry(kind: .question, text: text) }
    static func called(_ calls: [AssistantToolCall]) -> AssistantEntry { AssistantEntry(kind: .toolCalls, calls: calls) }
    static func resulted(_ results: [AssistantToolOutcome]) -> AssistantEntry { AssistantEntry(kind: .toolResults, results: results) }
    static func answered(_ text: String) -> AssistantEntry { AssistantEntry(kind: .answer, text: text) }
}

/// The server's reply to a turn (music-core's TurnReply): tool calls to run here, the checked answer, or no model. A
/// fallback answer says why the model's answer was not shown (`reason`, v1.2: AnswerCheck's reasons), when the API names it.
enum AssistantReply: Equatable, Sendable {
    case toolCalls([AssistantToolCall])
    case answer(text: String, fallback: Bool, reason: String? = nil)
    case notConfigured

    init?(_ json: JSONValue) {
        switch json["kind"]?.string {
        case "notConfigured":
            self = .notConfigured
        case "answer":
            guard let text = json["text"]?.string else { return nil }
            let fallback = json["fallback"]?.isTrue ?? false
            self = .answer(text: text, fallback: fallback, reason: fallback ? json["reason"]?.string : nil)
        case "toolCalls":
            guard let entry = json["entry"], entry["kind"]?.string == "toolCalls" else { return nil }
            let calls = entry["calls"]?.items.compactMap { call -> AssistantToolCall? in
                guard let id = call["id"]?.string, let name = call["name"]?.string, let arguments = call["arguments"] else { return nil }
                return AssistantToolCall(id: id, name: name, arguments: arguments)
            } ?? []
            guard !calls.isEmpty else { return nil }
            self = .toolCalls(calls)
        default:
            return nil
        }
    }
}

/// A piece of an answer: its words, and the fact it cites (its id and the engine's line for it) where it is a citation.
struct AnswerPiece: Codable, Equatable, Sendable {
    var text: String
    var cites: String?
    /// The bars the cited fact names, when it names some.
    var bars: ClosedRange<Int>?
}

/// A question and what came of it, as the conversation shows it.
struct AssistantExchange: Codable, Equatable, Sendable, Identifiable {
    var id: String
    var question: String
    /// What it looked at: "This selection · bars 3–4" or "The whole piece".
    var scope: String
    var answer: [AnswerPiece] = []
    /// The answer is the engine's template, the model's having failed the check.
    var fallback = false
    /// Why it is the template, as the API names it (`AssistantReply.answer`); nil when it named none or the model's answer
    /// was shown.
    var reason: String?
    /// Why there is no answer, as a UI text key ("mentor.signIn"); nil once answered.
    var note: String?
    /// Moved here from the assistant at the selection («Продолжить в чате»), not asked here.
    var added = false
    /// The drafts the question's tool calls made (#582 item 17.3), to accept or cancel; nil when none.
    var drafts: [AssistantDraftOffer]?
    /// The practice actions its tool calls suggested (`practice.suggest`, v1.2), one button each; nil when none.
    var practice: [AssistantPracticeOffer]?
    /// The steps its tool calls proposed in a draft's place (v1.2 step 8), to hear and accept or refuse; nil when none.
    var proposals: [AssistantPlaceProposal]?

    /// The text key that says in words why the answer is the engine's template (v1.2, assistant option A item 4): one for
    /// each reason the API names, a general one for a reason this iPad does not know; nil when there is no fallback or the
    /// API named no reason (an answer kept from before).
    var reasonKey: String? {
        guard fallback, let reason else { return nil }
        switch reason {
        case "empty": return "mentor.reason.empty"
        case "tooLong": return "mentor.reason.tooLong"
        case "noCitation": return "mentor.reason.noCitation"
        case "unknownFact": return "mentor.reason.unknownFact"
        case "timeout": return "mentor.reason.timeout"
        case "modelError": return "mentor.reason.modelError"
        case "toolNotAllowed": return "mentor.reason.toolNotAllowed"
        case "badArguments": return "mentor.reason.badArguments"
        case "tooManyCalls": return "mentor.reason.tooManyCalls"
        default: return "mentor.reason.other"
        }
    }

    /// The exchange with the player's decision on one of its drafts, and why it could not be accepted when it could not.
    mutating func mark(_ draft: String, _ state: AssistantDraftOffer.Decision, problem: String? = nil) {
        guard let index = drafts?.firstIndex(where: { $0.id == draft }) else { return }
        drafts?[index].state = state
        drafts?[index].problem = problem
    }
}

/// A conversation with the assistant about one piece, as the device keeps it (the lead's decision 4; music-core's
/// AssistantSession, 0028 section 4): the history the next turn sends and the exchanges it shows. The server keeps
/// nothing between turns. The history holds only answered questions: a turn that fails leaves it as it was.
struct AssistantChat: Codable, Equatable, Sendable, Identifiable {
    /// The most entries one turn sends, and the largest turn in bytes (music-core's TurnRequest).
    static let maxEntries = 40
    static let maxBytes = 128 * 1024
    /// The most tool calls for one question (AssistantRoles.MostToolCalls).
    static let mostToolCalls = 8

    var id: String
    var started: Date
    var updated: Date
    /// "mentor" or "proposer" (the assistant edits the notes).
    var role: String
    var history: [AssistantEntry] = []
    var exchanges: [AssistantExchange] = []

    /// The conversation's name in the list: its first question.
    var title: String { exchanges.first?.question ?? "" }

    /// The tool calls made since the last question.
    var toolCallsInQuestion: Int {
        history[lastQuestion...].reduce(0) { $0 + ($1.calls?.count ?? 0) }
    }

    /// The tool calls since the last question, with the arguments a draft is made again from.
    var callsInQuestion: [AssistantToolCall] {
        history[lastQuestion...].flatMap { $0.calls ?? [] }
    }

    /// The tool results since the last question: with the context, where an answer's citations are found.
    var resultsInQuestion: [AssistantToolOutcome] {
        history[lastQuestion...].flatMap { $0.results ?? [] }
    }

    /// What the tool calls since the last question proposed, in the order the calls came: each entry of a result's "drafts"
    /// with the call that made it and its place among the call's drafts, as music-core's AssistantSession.Resulted keeps
    /// them (a draft of the notes, or since v1.2 a step in a draft's place).
    var proposedInQuestion: [(call: AssistantToolCall, draft: JSONValue, index: Int)] {
        let calls = callsInQuestion
        return resultsInQuestion.flatMap { result -> [(call: AssistantToolCall, draft: JSONValue, index: Int)] in
            guard let call = calls.first(where: { $0.id == result.id }) else { return [] }
            return result.content["drafts"]?.items.enumerated().map { (call: call, draft: $1, index: $0) } ?? []
        }
    }

    private var lastQuestion: Int {
        history.lastIndex { $0.kind == .question } ?? history.count
    }

    /// The conversation with the question asked; the history keeps it once it is answered (`answered`).
    func asking(_ question: String) -> AssistantChat {
        var next = self
        next.history.append(.question(question))
        return next
    }

    func called(_ calls: [AssistantToolCall]) -> AssistantChat {
        var next = self
        next.history.append(.called(calls))
        return next
    }

    func resulted(_ results: [AssistantToolOutcome]) -> AssistantChat {
        var next = self
        next.history.append(.resulted(results))
        return next
    }

    func answered(_ text: String) -> AssistantChat {
        var next = self
        next.history.append(.answered(text))
        return next
    }

    /// The turn's body (format v1): the role, the language, the context and the history, the oldest finished exchanges
    /// left out while there are more than `maxEntries` entries, as music-core's AssistantSession.Request does, and (v1.4)
    /// while the body is larger than a turn takes (`maxBytes`), so a long conversation goes on and only a question whose
    /// context alone is too large is refused.
    func request(language: String, context: JSONValue) -> JSONValue {
        var history = history
        while history.count > Self.maxEntries, let next = Self.nextQuestion(in: history) {
            history = Array(history[next...])
        }
        var body = request(language: language, context: context, history: history)
        while body.data.count > Self.maxBytes, let next = Self.nextQuestion(in: history) {
            history = Array(history[next...])
            body = request(language: language, context: context, history: history)
        }
        return body
    }

    private func request(language: String, context: JSONValue, history: [AssistantEntry]) -> JSONValue {
        let entries = (JSONValue.parse((try? JSONEncoder().encode(history)) ?? Data()) ?? .array([]))
        return .object([
            "version": .int(1), "role": .string(role), "language": .string(language), "context": context, "history": entries,
        ])
    }

    /// Where the question after the first one starts; nil when the history holds no other.
    private static func nextQuestion(in history: [AssistantEntry]) -> Int? {
        history.indices.dropFirst().first { history[$0].kind == .question }
    }

    /// The conversation with a question's exchange joined, after the decisions taken on it meanwhile: the history the
    /// answer left (`answered`; unchanged when the question got none), and the exchange last.
    mutating func join(_ exchange: AssistantExchange, answered: AssistantChat?) {
        if let answered { history = answered.history }
        exchanges.append(exchange)
        updated = Date()
    }

    /// An answer as the conversation shows it: its words and each [[id]] it cites, found where the turn gave it (the
    /// context's digest or a tool result), as the engine's line for that fact; an id found nowhere is left out.
    static func pieces(of answer: String, context: JSONValue, results: [AssistantToolOutcome]) -> [AnswerPiece] {
        var pieces: [AnswerPiece] = []
        var rest = Substring(answer)
        while let open = rest.range(of: "[["), let close = rest.range(of: "]]", range: open.upperBound..<rest.endIndex) {
            if open.lowerBound > rest.startIndex { pieces.append(AnswerPiece(text: String(rest[..<open.lowerBound]))) }
            let id = String(rest[open.upperBound..<close.lowerBound])
            if let fact = context.fact(id) ?? results.lazy.compactMap({ $0.content.fact(id) }).first {
                pieces.append(AnswerPiece(text: fact.text, cites: id, bars: fact.bars))
            }
            rest = rest[close.upperBound...]
        }
        if !rest.isEmpty { pieces.append(AnswerPiece(text: String(rest))) }
        return pieces
    }
}
