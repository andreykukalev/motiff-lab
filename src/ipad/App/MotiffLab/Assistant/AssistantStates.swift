import Foundation

/// What a question looks at (music-core's AskScope, 0028 section 5; the bug "the assistant does not see the selected
/// bars"): the selection, unless the player chose the whole piece while that very selection stood. Bars selected after
/// that are what the next question is about, whatever was chosen before, so the bars just selected always reach the
/// model; with nothing selected it is the whole piece. Ask about the piece chooses the whole piece for the selection that
/// stands then.
struct AssistantScope: Equatable, Sendable {
    /// The passage (onset and end in ticks) that stood when the player chose the whole piece; nil while the selection is
    /// the scope.
    private(set) var wholeFor: [Double]?

    /// The selection, whichever it is.
    static let selection = AssistantScope(wholeFor: nil)

    /// The player's choice: the whole piece (true) or the selection, while `selection` stands.
    static func choose(wholePiece: Bool, selection: ScoreSelection?) -> AssistantScope {
        guard wholePiece, let selection else { return .selection }
        return AssistantScope(wholeFor: [selection.onset, selection.end])
    }

    /// Whether a question now is about the whole piece, with `selection` selected (nil: nothing is).
    func isWholePiece(_ selection: ScoreSelection?) -> Bool {
        guard let selection else { return true }
        return wholeFor == [selection.onset, selection.end]
    }
}

/// Where a question is on its way, as the panel and the chat at the selection say it. The API answers a turn whole (no
/// streaming), so the answer's wait is one clear state: sending (the API's status and the passage's context), the
/// model answering, and looking in the score while music-core runs the model's tool calls on the iPad.
enum AssistantPhase: Equatable, Sendable {
    case idle, sending, answering, lookingUp

    /// The UI text key that says it; nil while nothing is on its way.
    var key: String? {
        switch self {
        case .idle: nil
        case .sending: "padAssistant.sending"
        case .answering: "padAssistant.answering"
        case .lookingUp: "padAssistant.lookingUp"
        }
    }
}

/// What the player can do about a question that got no answer (the lead's decision 3 of version 0.6.3): sign in, try
/// again (no connection, the server's or the model's error), or nothing here (Pro, the account not on the list, the day's
/// questions used, a passage too long, a question the assistant could not take).
enum AssistantRecovery: Equatable, Sendable {
    case signIn, retry, nothing

    static func of(_ note: String) -> AssistantRecovery {
        switch note {
        case "mentor.signIn": .signIn
        case "mentor.unreachable", "mentor.notConfigured", "padAssistant.serverError": .retry
        default: .nothing
        }
    }

    /// Whether the note stops a question being asked at all until something changes (signing in, the plan, tomorrow): the
    /// field waits then. A connection or server problem never stops one, as asking again is how it is tried again.
    static func stopsAsking(_ note: String?) -> Bool {
        guard let note else { return false }
        return ["mentor.signIn", "mentor.notAllowed", "mentor.pro", "mentor.dailyCap", "mentor.notConfigured"].contains(note)
    }

    /// The text key that says the note on the iPad: signing in in the assistant's own words, the rest as the web says it.
    static func shown(_ note: String) -> String {
        note == "mentor.signIn" ? "padAssistant.signIn" : note
    }

    /// Whether the note is the Pro gate, which is said in plum.
    static func isPro(_ note: String) -> Bool { note == "mentor.pro" }
}

/// What the assistant may do for the signed-in player now, from the API's `/assistant/status` asked when it opens: nil
/// when a question may go, else why not, as a UI text key (the same that a question would get).
enum AssistantStanding {
    static func note(status: AssistantStatus?, problem: RelayProblem?) -> String? {
        guard let status else { return (problem ?? .unreachable).note }
        return status.note
    }
}
