import Foundation

/// One question's turns (0028 section 4), as the web app's mentor runs them: the device sends the turn, runs the tool
/// calls that come back in music-core and sends their results, until the answer. Nothing is applied to the piece; a draft
/// a tool made waits in its result for the player. The conversation keeps the question only once it is answered, so a
/// turn that fails leaves it as it was. The sending and the running are given, so the rule is tested without a server.
struct AssistantTurns {
    /// Sends a turn's body (JSON, format v1): the reply, or the problem.
    var send: (Data) async -> (AssistantReply?, RelayProblem?)
    /// Runs one tool call on the device (facade `assistant.call`): what the tool answered.
    var run: (AssistantToolCall) async -> JSONValue

    enum Outcome: Equatable {
        /// The conversation with the question and its answer, the answer's text (citing facts as [[id]]), whether it is
        /// the engine's template and why, when the API says.
        case answered(AssistantChat, text: String, fallback: Bool, reason: String?)
        /// Why there is no answer, as a UI text key; nothing changed.
        case unanswered(String)
    }

    func ask(_ question: String, in chat: AssistantChat, context: JSONValue, language: String) async -> Outcome {
        var session = chat.asking(question)
        for _ in 0...(AssistantChat.mostToolCalls + 1) {
            let body = session.request(language: language, context: context).data
            if body.count > AssistantChat.maxBytes { return .unanswered("mentor.tooLarge") }
            let (reply, problem) = await send(body)
            switch reply {
            case nil:
                return .unanswered((problem ?? .unreachable).note)
            case .notConfigured?:
                return .unanswered("mentor.notConfigured")
            case let .toolCalls(calls)?:
                let before = session.toolCallsInQuestion
                var results: [AssistantToolOutcome] = []
                for (index, call) in calls.enumerated() {
                    let content: JSONValue
                    if before + index >= AssistantChat.mostToolCalls {
                        content = Self.refusal(call.name, language: language)
                    } else {
                        content = await run(call)
                    }
                    results.append(AssistantToolOutcome(id: call.id, name: call.name, content: content))
                }
                session = session.called(calls).resulted(results)
            case let .answer(text, fallback, reason)?:
                return .answered(session.answered(text), text: text, fallback: fallback, reason: reason)
            }
        }
        return .unanswered("mentor.unreachable")
    }

    /// A call past the cap, answered as music-core's TurnRunner answers it, so the model is told and answers with what it has.
    static func refusal(_ tool: String, language: String) -> JSONValue {
        let words = language == "ru"
            ? "На один вопрос — не больше \(AssistantChat.mostToolCalls) вызовов инструментов; ответьте по тому, что уже есть."
            : "A question takes at most \(AssistantChat.mostToolCalls) tool calls; answer with what you have."
        return .object(["tool": .string(tool), "ok": .bool(false), "refusal": .string(words)])
    }

    /// A tool's answer from the facade, or a refusal the model reads when music-core could not run it.
    static func content(ofFacade answer: String, tool: String) -> JSONValue {
        guard let json = JSONValue.parse(answer), case .object = json else {
            return .object(["tool": .string(tool), "ok": .bool(false), "refusal": .string("The tool could not run.")])
        }
        if let error = json["error"] {
            return .object(["tool": .string(tool), "ok": .bool(false), "refusal": json["message"] ?? error])
        }
        return json
    }
}
