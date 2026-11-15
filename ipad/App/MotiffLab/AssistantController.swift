import Foundation

/// The model-backed assistant on the iPad (an experiment, Settings · Experimental «Разговор с помощником», the web's
/// `experimental.assistant`; #582 item 17): the full assistant in a side panel beside the score, with the piece's
/// conversations kept on this iPad and a list to go back to earlier ones (17.1). A question goes out only after the
/// player's consent to exactly what is sent (the web's list, the same key on the device), only when the API says a model
/// is on for the account, and only to the Motiff Lab API with the signed-in player's token: the selected bars' facts digest
/// and notes, or the whole piece's digest (music-core's `assistant.context`), with the conversation. The model's tool calls
/// run here in music-core (`assistant.call`); its answer cites the facts, shown in the engine's words.
@MainActor
final class AssistantController: ObservableObject {
    static let setting = "experimental.assistant"
    /// «Помощник правит ноты» (#582 item 17.3): the conversation is the proposer, whose tools make drafts.
    static let actionsSetting = "experimental.assistantActions"
    /// The consent, as the web app keeps it (MentorPanel.ConsentKey), on this iPad.
    static let consentKey = "ml-assistant-consent"
    static let consented = "yes"

    @Published var panelOpen = false
    /// The list of the piece's conversations shown in the panel in place of the one open.
    @Published var listShown = false
    /// What the next question looks at: the whole piece, or the selection when there is one.
    @Published var wholePiece = false
    @Published private(set) var chats: [AssistantChat] = []
    @Published private(set) var currentId: String?
    @Published private(set) var busy = false
    @Published private(set) var consentGiven: Bool

    private let store: AssistantStore
    private let defaults: UserDefaults
    private var piece: String?

    init(store: AssistantStore = .standard, defaults: UserDefaults = .standard) {
        self.store = store
        self.defaults = defaults
        consentGiven = defaults.string(forKey: Self.consentKey) == Self.consented
    }

    /// The conversation open in the panel: the one chosen, else a new one.
    var current: AssistantChat? { chats.first { $0.id == currentId } }

    /// The piece's conversations from the iPad; the newest is the one the panel opens on.
    func load(piece key: String) {
        guard key != piece else { return }
        piece = key
        chats = store.chats(of: key)
        currentId = chats.first?.id
        listShown = false
    }

    /// The panel opens on the newest conversation, looking at the selection when there is one.
    func open(wholePiece: Bool) {
        self.wholePiece = wholePiece
        listShown = false
        panelOpen = true
    }

    func close() {
        panelOpen = false
        listShown = false
    }

    /// A new conversation, empty until its first question.
    func startNew() {
        currentId = nil
        listShown = false
    }

    func choose(_ id: String) {
        currentId = id
        listShown = false
    }

    func consent(_ given: Bool) {
        if given { defaults.set(Self.consented, forKey: Self.consentKey) } else { defaults.removeObject(forKey: Self.consentKey) }
        consentGiven = given
    }

    /// A question asked about the piece open in music-core: its answer, or why there is none, joins the conversation.
    func ask(_ question: String, about ask: AssistantAsk) async {
        guard !busy else { return }
        busy = true
        defer { busy = false }
        var chat = current ?? AssistantChat(id: UUID().uuidString, started: Date(), updated: Date(), role: ask.role)
        let exchangeId = UUID().uuidString
        var exchange = AssistantExchange(id: exchangeId, question: question, scope: ask.scope)
        switch await answer(question, in: chat, about: ask) {
        case let .answered(answered, text, fallback, context):
            chat = answered
            exchange.answer = AssistantChat.pieces(of: text, context: context, results: answered.resultsInQuestion)
            exchange.fallback = fallback
        case let .unanswered(note):
            exchange.note = note
        }
        chat.exchanges.append(exchange)
        chat.updated = Date()
        keep(chat)
    }

    /// «Добавить в контекст» (#582 item 17.2, the lead's decision 5): a question and answer of the assistant at the
    /// selection join the open conversation of the full assistant, as asked and answered there, so the next question
    /// there has them.
    func add(_ exchange: AssistantExchange, answer text: String, role: String) {
        var chat = current ?? AssistantChat(id: UUID().uuidString, started: Date(), updated: Date(), role: role)
        if !text.isEmpty, chat.history.last?.kind ?? .answer == .answer {
            chat = chat.asking(exchange.question).answered(text)
        }
        var added = exchange
        added.added = true
        chat.exchanges.append(added)
        chat.updated = Date()
        keep(chat)
    }

    private func keep(_ chat: AssistantChat) {
        chats.removeAll { $0.id == chat.id }
        chats.insert(chat, at: 0)
        currentId = chat.id
        if let piece { store.keep(chats, of: piece) }
    }

    enum Answer {
        case answered(AssistantChat, text: String, fallback: Bool, context: JSONValue)
        case unanswered(String)
    }

    /// The answer, or why there is none: nothing is sent without consent, a model on for the account and a passage a turn
    /// can carry (the web app's MentorAnswerAsync).
    func answer(_ question: String, in chat: AssistantChat, about ask: AssistantAsk) async -> Answer {
        guard consentGiven else { return .unanswered("mentor.noConsent") }
        let (status, problem) = await ask.relay.status()
        guard let status else { return .unanswered((problem ?? .unreachable).note) }
        if let note = status.note { return .unanswered(note) }
        guard let context = await Self.context(piece: ask.piece, selection: ask.wholePiece ? nil : ask.selection, wholePiece: ask.wholePiece) else {
            return .unanswered("mentor.tooManyBars")
        }
        let piece = ask.piece
        let turns = AssistantTurns(
            send: { body in await ask.relay.turn(body) },
            run: { call in
                let request = JSONValue.object(["tool": .string(call.name), "parameters": call.arguments]).text
                let answer = await MusicCore.run { MusicCore.call(piece: piece, "assistant.call", request) }
                return AssistantTurns.content(ofFacade: answer, tool: call.name)
            }
        )
        switch await turns.ask(question, in: chat, context: context, language: ask.language) {
        case let .answered(answered, text, fallback):
            return .answered(answered, text: text, fallback: fallback, context: context)
        case let .unanswered(note):
            return .unanswered(note)
        }
    }

    /// What a turn sends as its context (music-core's `assistant.context`): the bars the selection touches with their notes,
    /// or the whole piece's digest; nil when the selection spans more bars than a question may hold.
    static func context(piece: Int32, selection: ScoreSelection?, wholePiece: Bool) async -> JSONValue? {
        var request: [String: JSONValue] = ["wholePiece": .bool(wholePiece)]
        if let selection {
            request["selection"] = .object(["onset": .int(Int(selection.onset.rounded())), "end": .int(Int(selection.end.rounded()))])
        }
        let body = JSONValue.object(request).text
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "assistant.context", body) }
        return JSONValue.parse(answer)?["context"]
    }
}

/// What a question is asked about: the piece open in music-core, the selection (nil for none), whether the whole piece is
/// asked about, the scope in words, the language, the role and the relay.
struct AssistantAsk {
    var piece: Int32
    var selection: ScoreSelection?
    var wholePiece: Bool
    var scope: String
    var language: String
    /// "mentor", or "proposer" with «Помощник правит ноты» on.
    var role: String
    var relay: AssistantRelay
}
