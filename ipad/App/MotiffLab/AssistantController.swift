import Foundation

/// The model-backed assistant on the iPad (an experiment, Settings · Experimental «Разговор с помощником», the web's
/// `experimental.assistant`; #582 item 17): the full assistant in a side panel beside the score, with the piece's
/// conversations kept on this iPad and a list to go back to earlier ones (17.1). A question goes out only after the
/// player's consent to exactly what is sent (the web's list, the same key on the device), only when the API says a model
/// is on for the account, and only to the Motiff Lab API with the signed-in player's token: the selected bars' facts digest
/// and notes, or the whole piece's digest (music-core's `assistant.context`), with the conversation. The model's tool calls
/// run here in music-core (`assistant.call`); its answer cites the facts, shown in the engine's words. The same questions
/// are asked at the selection, one at a time (17.2, 17.4), and with «Помощник правит ноты» the drafts the tools made wait
/// for the player's Accept or Cancel (17.3).
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
    /// The assistant at the selection (#582 items 17.2 and 17.4): one question and its answer, never kept; nil while closed.
    @Published private(set) var here: AssistantHere?

    private let store: AssistantStore
    private let defaults: UserDefaults
    private var piece: String?
    /// The score whose notes a draft is accepted on (`corrections.acceptDraft`).
    private weak var score: ScoreController?

    init(store: AssistantStore = .standard, defaults: UserDefaults = .standard) {
        self.store = store
        self.defaults = defaults
        consentGiven = defaults.string(forKey: Self.consentKey) == Self.consented
    }

    /// The conversation open in the panel: the one chosen, else a new one.
    var current: AssistantChat? { chats.first { $0.id == currentId } }

    /// The piece's conversations from the iPad; the newest is the one the panel opens on. `score` is the screen's, where a
    /// draft is accepted.
    func load(piece key: String, score: ScoreController? = nil) {
        if let score { self.score = score }
        guard key != piece else { return }
        piece = key
        here = nil
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
        let chat = current ?? AssistantChat(id: UUID().uuidString, started: Date(), updated: Date(), role: ask.role)
        let (exchange, answered, _) = await self.exchange(question, in: chat, about: ask)
        var kept = answered ?? chat
        kept.exchanges.append(exchange)
        kept.updated = Date()
        keep(kept)
    }

    /// The exchange a question makes: its answer with the facts it cites and the drafts its tool calls made, or why there
    /// is none; with the conversation that answered it and the answer's text, citations as [[id]].
    private func exchange(_ question: String, in chat: AssistantChat, about ask: AssistantAsk) async -> (AssistantExchange, AssistantChat?, String) {
        var exchange = AssistantExchange(id: UUID().uuidString, question: question, scope: ask.scope)
        switch await answer(question, in: chat, about: ask) {
        case let .answered(answered, text, fallback, context):
            exchange.answer = AssistantChat.pieces(of: text, context: context, results: answered.resultsInQuestion)
            exchange.fallback = fallback
            let drafts = AssistantDraftOffer.offers(in: answered)
            exchange.drafts = drafts.isEmpty ? nil : drafts
            return (exchange, answered, text)
        case let .unanswered(note):
            exchange.note = note
            return (exchange, nil, "")
        }
    }

    /// The assistant at the selection opens over it, empty (#582 item 17.2).
    func openHere(on selection: ScoreSelection, role: String) {
        here = AssistantHere(selection: selection, role: role)
    }

    func closeHere() {
        here = nil
    }

    /// The assistant at the selection looks at the selection now, which the player changed while typing.
    func follow(_ selection: ScoreSelection) {
        here?.selection = selection
    }

    /// A question at the selection: one at a time, with no conversation before it; its answer replaces the last one.
    func askHere(_ question: String, about ask: AssistantAsk) async {
        guard !busy, let open = here else { return }
        busy = true
        defer { busy = false }
        let chat = AssistantChat(id: UUID().uuidString, started: Date(), updated: Date(), role: open.role)
        let (exchange, _, text) = await self.exchange(question, in: chat, about: ask)
        guard here != nil else { return }
        here?.exchange = exchange
        here?.answer = text
    }

    /// «Добавить в контекст» at the selection: its question and answer join the full assistant's open conversation, and
    /// it closes, its work done.
    func addHere() {
        guard let open = here, let exchange = open.exchange else { return }
        add(exchange, answer: open.answer, role: open.role)
        here = nil
    }

    /// Accept on a draft (#582 item 17.3): music-core makes it again from its call and adds it as corrections; the draft
    /// says it is accepted, or why it could not be. True when it was.
    @discardableResult
    func accept(_ draft: AssistantDraftOffer) async -> Bool {
        guard !busy, let score else { return false }
        busy = true
        defer { busy = false }
        let problem = await score.acceptAssistantDraft(draft)
        decide(draft.id, problem == nil ? .accepted : .proposed, problem: problem)
        return problem == nil
    }

    func cancel(_ draft: AssistantDraftOffer) {
        decide(draft.id, .cancelled)
    }

    private func decide(_ draft: String, _ state: AssistantDraftOffer.Decision, problem: String? = nil) {
        here?.exchange?.mark(draft, state, problem: problem)
        guard var chat = current, let index = chat.exchanges.firstIndex(where: { shown in shown.drafts?.contains { $0.id == draft } ?? false }) else { return }
        chat.exchanges[index].mark(draft, state, problem: problem)
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

/// The assistant at the selection (#582 items 17.2 and 17.4): the passage it was opened on, the role its question takes,
/// and its one exchange with the answer's text, which «Добавить в контекст» moves into the full assistant's conversation.
struct AssistantHere: Equatable {
    var selection: ScoreSelection
    var role: String
    var exchange: AssistantExchange?
    var answer = ""
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
