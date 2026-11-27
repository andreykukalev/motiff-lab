import Foundation

/// The model-backed assistant on the iPad (#582 item 17; since version 0.6.3 every Ask opens it, with no switch: the
/// lead's decision 3): the full assistant in a side panel beside the score, with the piece's conversations kept on this
/// iPad and a list to go back to earlier ones (17.1). A question goes out only after the
/// player's consent to exactly what is sent (the web's list, the same key on the device), only when the API says a model
/// is on for the account, and only to the Motiff Lab API with the signed-in player's token: the selected bars' facts digest
/// and notes, or the whole piece's digest (music-core's `assistant.context`), with the conversation. The model's tool calls
/// run here in music-core (`assistant.call`); its answer cites the facts, shown in the engine's words. The same questions
/// are asked at the selection, one at a time (17.2, 17.4), and the drafts the tools made («Помощник правит ноты») wait
/// for the player's Accept or Cancel (17.3).
@MainActor
final class AssistantController: ObservableObject {
    /// The consent, as the web app keeps it (MentorPanel.ConsentKey), on this iPad: the list agreed to. Version 1.2 sends
    /// the piece's counted facts too (its plan, answer 6), so a consent to the list before ("yes") is asked again, once.
    static let consentKey = "ml-assistant-consent"
    static let consented = "v1.2"
    /// The consent to what a question in a draft sends (v1.2, ADR 0034 section 6, the plan's answer 7): a list of its own,
    /// asked once before the first question in a draft, kept beside the other the same way.
    static let draftConsentKey = "ml-assistant-draft-consent"

    /// Whether the side panel shows: written here alone (`open`, `show`, `close`), read and observed elsewhere. Closed, the
    /// question typed in it and not sent stays for its field when it opens again (`unsentInPanel`, v1.5, answer 16).
    @Published private(set) var panelOpen = false
    /// The list of the piece's conversations shown in the panel in place of the one open.
    @Published var listShown = false
    /// What the next question looks at: the selection, or the whole piece while the selection it was chosen for stands.
    @Published private(set) var scope = AssistantScope.selection
    @Published private(set) var chats: [AssistantChat] = []
    @Published private(set) var currentId: String?
    @Published private(set) var busy = false
    /// Where the question asked now is on its way; `.idle` while none is.
    @Published private(set) var phase = AssistantPhase.idle
    /// The question on its way in the panel, shown at once under the conversation until its answer joins it.
    @Published private(set) var pending: String?
    /// Why no question may go now, from the API's status when the assistant opened or the last question's problem, as a UI
    /// text key; nil when one may (or nothing is known yet).
    @Published private(set) var standing: String?
    @Published private(set) var checking = false
    @Published private(set) var consentGiven: Bool
    /// Whether the player agreed to an earlier list, so the consent says it asks again; false once they decide.
    @Published private(set) var consentAskedAgain: Bool
    /// The assistant at the selection (#582 items 17.2 and 17.4): one question and its answer, never kept; nil while closed.
    @Published private(set) var here: AssistantHere?
    /// Whether the player agreed to what a question in a draft sends (`draftConsentKey`).
    @Published private(set) var draftConsentGiven: Bool
    /// The assistant in a draft's place (v1.2 step 8): its conversation there (v1.4), never kept; nil while closed. Closed,
    /// or open on another place, the question typed there and not sent goes.
    @Published private(set) var inDraft: AssistantInDraft? {
        didSet { if inDraft?.place != oldValue?.place { unsentInDraft = "" } }
    }
    /// The question typed and not sent yet, in the panel and in a draft's place (r-1): kept here, since a rotation or
    /// Split View lays the panel and the draft's card out anew, and their fields with them. Not observed: only the field
    /// made again reads it (`keepUnsent`). The panel's outlives closing the panel too (v1.5, answer 16) and goes once sent
    /// or with the piece (`load` of another); a draft's goes with the place's assistant (`inDraft`).
    private(set) var unsentInPanel = ""
    private(set) var unsentInDraft = ""

    private let store: AssistantStore
    private let defaults: UserDefaults
    private var piece: String?
    /// The score whose notes a draft is accepted on (`corrections.acceptDraft`).
    private weak var score: ScoreController?
    /// Whether it has served a screen yet (`load` with a score), and how many it has left: a draft's visit hands one
    /// controller to each screen its view opens again on (v1.4), and what belonged to the screen before goes.
    private var served = false
    private var screens = 0

    init(store: AssistantStore = .standard, defaults: UserDefaults = .standard) {
        self.store = store
        self.defaults = defaults
        let kept = defaults.string(forKey: Self.consentKey)
        consentGiven = kept == Self.consented
        consentAskedAgain = kept != nil && kept != Self.consented
        draftConsentGiven = defaults.string(forKey: Self.draftConsentKey) == Self.consented
    }

    /// The conversation open in the panel: the one chosen, or the one its first question starts (nil until then). Closing
    /// the panel keeps it.
    var current: AssistantChat? { chats.first { $0.id == currentId } }

    /// The piece's conversations from the iPad; the newest is the one open until an Ask opens the panel empty (`open`).
    /// `score` is the screen's, where a draft is accepted; another screen's score on the same visit drops what belonged to
    /// the screen before (`leaveScreen`, v1.4).
    func load(piece key: String, score: ScoreController? = nil) {
        if let score {
            // Another screen's score, or the one before already freed (nil), is another screen; the visit's first is none.
            if served, self.score !== score { leaveScreen() }
            served = true
            self.score = score
        }
        guard key != piece else { return }
        // A place's assistant opened as the visit began, before its first piece was read (a draft's view opened with it,
        // v1.5 d-11), stays; one of another piece goes.
        if piece != nil { inDraft = nil }
        piece = key
        here = nil
        unsentInPanel = ""
        chats = store.chats(of: key)
        currentId = chats.first?.id
        listShown = false
    }

    /// The same visit on a screen made again (a draft's view opened again after a step, v1.4): what belonged to the screen
    /// before goes (the assistant at its selection, what the next question looks at, the list shown), and a panel question
    /// still on its way, asked on that screen's piece, is dropped; the conversation in a draft's place, the piece's
    /// conversations and whether the panel is open stay.
    private func leaveScreen() {
        screens += 1
        here = nil
        scope = .selection
        listShown = false
    }

    /// An Ask (Andrei, 2026-11-18, v0.8 finding 4): the panel opens on no conversation, ready for a question, whatever was
    /// open before, its field still holding a question typed and not sent (`unsentInPanel`, v1.5, answer 16); no
    /// conversation is made until its first question goes (`ask`), and the earlier ones stay in the list. It looks at
    /// the selection when there is one, or at the whole piece when asked about it (for the selection that stands now: bars
    /// selected after it are what the next question is about).
    func open(wholePiece: Bool, selection: ScoreSelection?) {
        choose(wholePiece: wholePiece, selection: selection)
        startNew()
        panelOpen = true
    }

    /// What the next question looks at: the whole piece, or the selection.
    func choose(wholePiece: Bool, selection: ScoreSelection?) {
        scope = .choose(wholePiece: wholePiece, selection: selection)
    }

    /// The player's press on a scope chip: as `choose`, and the selected bars picked are brought into view on the score
    /// (its `reveal`), so the player sees them at once (Andrei, 2026-11-18); the whole piece moves nothing, and nor does
    /// opening the panel (`open`).
    func pickShowingBars(wholePiece: Bool, selection: ScoreSelection?) async {
        choose(wholePiece: wholePiece, selection: selection)
        guard !wholePiece, let selection else { return }
        await score?.reveal(selection.onset)
    }

    /// What the API says the player may do now (`/assistant/status`), so the panel says why before anything is typed:
    /// sign in, Pro, the day's questions; a connection problem is said too, and asking tries again.
    func check(_ relay: AssistantRelay) async {
        guard !checking else { return }
        checking = true
        defer { checking = false }
        let (status, problem) = await relay.status()
        standing = AssistantStanding.note(status: status, problem: problem)
    }

    func close() {
        panelOpen = false
        listShown = false
    }

    /// The question typed and not sent yet in the panel's field, or with `inDraft` in the draft's place's (r-1); empty once
    /// sent. Only an open field writes it: the panel's field emptied as the panel closes leaves the question kept for when
    /// it opens again (v1.5, answer 16), and a draft's place's with no assistant open there keeps nothing.
    func keepUnsent(_ typed: String, inDraft: Bool = false) {
        if inDraft {
            unsentInDraft = self.inDraft == nil ? "" : typed
        } else if panelOpen {
            unsentInPanel = typed
        }
    }

    /// The panel opened again on the conversation it had when it was closed (the pill's «Ассистент», Andrei's 0.8 test,
    /// finding 8, and his card of 2026-11-18 21:49 UTC); only an Ask opens it empty (`open`).
    func show() {
        panelOpen = true
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

    /// The player's answer to the consent: the piece's list, or with `inDraft` the draft's own (v1.2).
    func consent(_ given: Bool, inDraft: Bool = false) {
        let key = inDraft ? Self.draftConsentKey : Self.consentKey
        if given { defaults.set(Self.consented, forKey: key) } else { defaults.removeObject(forKey: key) }
        if inDraft {
            draftConsentGiven = given
            return
        }
        consentGiven = given
        consentAskedAgain = false
    }

    /// A question asked about the piece open in music-core: its answer, or why there is none, joins the conversation.
    func ask(_ question: String, about ask: AssistantAsk) async {
        guard !busy else { return }
        busy = true
        pending = question
        defer {
            busy = false
            pending = nil
            phase = .idle
        }
        let chat = current ?? AssistantChat(id: UUID().uuidString, started: Date(), updated: Date(), role: ask.role)
        let asked = screens
        let (exchange, answered, _) = await self.exchange(question, in: chat, about: ask)
        // Asked on a screen that has gone (a draft's view opened again): its piece's number is closed, so it is dropped.
        guard screens == asked else { return }
        var kept = chat
        kept.join(exchange, answered: answered)
        keep(kept)
    }

    /// Try again (the lead's decision 3): the question that got no answer leaves the open conversation (the panel's, or
    /// since v1.4 the one in a draft's place), to be asked again; its question, or nil when it had an answer or one is on
    /// its way.
    func forget(_ failed: AssistantExchange) -> String? {
        guard !busy, failed.note != nil else { return nil }
        if var open = inDraft, open.chat.exchanges.contains(where: { $0.id == failed.id }) {
            open.chat.exchanges.removeAll { $0.id == failed.id }
            inDraft = open
            return failed.question
        }
        guard var chat = current, chat.exchanges.contains(where: { $0.id == failed.id }) else { return nil }
        chat.exchanges.removeAll { $0.id == failed.id }
        keep(chat)
        return failed.question
    }

    /// The exchange a question makes: its answer with the facts it cites and the drafts its tool calls made, or why there
    /// is none; with the conversation that answered it and the answer's text, citations as [[id]].
    private func exchange(_ question: String, in chat: AssistantChat, about ask: AssistantAsk) async -> (AssistantExchange, AssistantChat?, String) {
        var exchange = AssistantExchange(id: UUID().uuidString, question: question, scope: ask.scope)
        phase = .sending
        let outcome = await answer(question, in: chat, about: ask)
        phase = .idle
        switch outcome {
        case let .answered(answered, text, fallback, reason, context):
            standing = nil
            exchange.answer = AssistantChat.pieces(of: text, context: context, results: answered.resultsInQuestion)
            exchange.fallback = fallback
            exchange.reason = reason
            let drafts = AssistantDraftOffer.offers(in: answered)
            exchange.drafts = drafts.isEmpty ? nil : drafts
            let practice = AssistantPracticeOffer.offers(in: answered)
            exchange.practice = practice.isEmpty ? nil : practice
            let proposals = AssistantPlaceProposal.offers(in: answered)
            exchange.proposals = proposals.isEmpty ? nil : proposals
            return (exchange, answered, text)
        case let .unanswered(note):
            exchange.note = note
            if AssistantRecovery.stopsAsking(note) { standing = note }
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
        here?.asking = question
        defer { here?.asking = nil }
        let asked = await askOnce(question, role: open.role, about: ask)
        guard here != nil else { return }
        here?.exchange = asked.exchange
        here?.answer = asked.text
    }

    /// One question with no conversation before it, in `role` (at the selection): its exchange and the answer's text.
    private func askOnce(_ question: String, role: String, about ask: AssistantAsk) async -> (exchange: AssistantExchange, text: String) {
        busy = true
        defer {
            busy = false
            phase = .idle
        }
        let chat = AssistantChat(id: UUID().uuidString, started: Date(), updated: Date(), role: role)
        let (exchange, _, text) = await self.exchange(question, in: chat, about: ask)
        return (exchange, text)
    }

    // MARK: - In a draft's place (v1.2, ADR 0034 section 6, the plan's answer 7; a conversation since v1.4, answer 8)

    /// «С ассистентом · Pro» on a place of the draft: the assistant opens there, empty, in `role` (the place assembler's
    /// unless said); whatever was open in another place goes.
    func openInDraft(_ place: AssistantDraftPlace, role: String = AssistantDraftPlace.role) {
        guard inDraft?.place != place else { return }
        inDraft = AssistantInDraft(place: place, chat: Self.chat(role))
    }

    /// A new conversation in the place the assistant is open on, in `role` (a role chosen, or «Новый разговор»): the one
    /// before goes, as it is never kept. Nothing while a question is on its way.
    func startInDraft(role: String) {
        guard !busy, inDraft?.asking == nil, inDraft != nil else { return }
        inDraft?.chat = Self.chat(role)
    }

    func closeInDraft() {
        inDraft = nil
    }

    /// The assistant in a draft's place closed when the draft no longer offers it there (`offered`: the places of the
    /// draft as it is now where it is offered, each as the view names it): the place gone, or changed so that its label or
    /// what it offers is another (a new base, a variant moved out), so no conversation or proposal of a place as it was stays.
    func closeInDraft(unlessOffered offered: [AssistantDraftPlace]) {
        guard let open = inDraft?.place, !offered.contains(open) else { return }
        inDraft = nil
    }

    /// A question about the place the assistant is open on (`ask.place`), in its conversation's role, with the questions
    /// before it; its answer, with the steps it proposes, joins the conversation. One at a time.
    func askInDraft(_ question: String, about ask: AssistantAsk) async {
        guard !busy, let open = inDraft, open.asking == nil else { return }
        busy = true
        inDraft?.asking = question
        defer {
            busy = false
            phase = .idle
            if inDraft?.chat.id == open.chat.id { inDraft?.asking = nil }
        }
        let (exchange, answered, _) = await self.exchange(question, in: open.chat, about: ask)
        // Closed, opened on another place or started anew meanwhile: the answer is not that conversation's.
        guard inDraft?.place == open.place, inDraft?.chat.id == open.chat.id else { return }
        inDraft?.chat.join(exchange, answered: answered)
    }

    private static func chat(_ role: String) -> AssistantChat {
        AssistantChat(id: UUID().uuidString, started: Date(), updated: Date(), role: role)
    }

    /// «Принять» on a step proposed in the place: `take` keeps it (the screen's step on the draft, music-core's
    /// `drafts.accept`); then the proposal says so and the others of its question go, or it says why music-core did not
    /// take it; held (another step of the draft on its way), it stays as it was. True when it was kept.
    @discardableResult
    func accept(_ proposal: AssistantPlaceProposal, by take: () async -> DraftAcceptance) async -> Bool {
        guard !busy else { return false }
        busy = true
        defer { busy = false }
        switch await take() {
        case .held:
            return false
        case .refused(let problem):
            mark(proposal.id, .proposed, problem: problem)
            return false
        case .kept:
            break
        }
        let question = inDraft?.chat.exchanges.first { shown in shown.proposals?.contains { $0.id == proposal.id } ?? false }
        for other in question?.proposals ?? [] where other.id != proposal.id && other.state == .proposed {
            mark(other.id, .cancelled)
        }
        mark(proposal.id, .accepted)
        return true
    }

    /// «Отказаться» on a step proposed in the place: nothing changes but the proposal, which says so.
    func refuse(_ proposal: AssistantPlaceProposal) {
        mark(proposal.id, .cancelled)
    }

    private func mark(_ proposal: String, _ state: AssistantDraftOffer.Decision, problem: String? = nil) {
        guard let exchanges = inDraft?.chat.exchanges,
              let shown = exchanges.firstIndex(where: { exchange in exchange.proposals?.contains { $0.id == proposal } ?? false }),
              let index = exchanges[shown].proposals?.firstIndex(where: { $0.id == proposal }) else { return }
        inDraft?.chat.exchanges[shown].proposals?[index].state = state
        inDraft?.chat.exchanges[shown].proposals?[index].problem = problem
    }

    /// «Продолжить в чате» at the selection (Andrei, 2026-11-18): its question and answer start a new conversation of
    /// the full assistant (`start`), whose panel opens on the selected bars to go on there; the chat at the selection
    /// closes. Without this press the chat at the selection keeps nothing.
    func continueHere() {
        guard let asked = here, let exchange = asked.exchange else { return }
        open(wholePiece: false, selection: asked.selection)
        start(exchange, answer: asked.answer, role: asked.role)
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
        guard problem == nil else {
            decide(draft.id, .proposed, problem: problem)
            return false
        }
        // One draft of a question is accepted, as on the web: the others offered with it go.
        for other in drafts(beside: draft.id) where other.state == .proposed { decide(other.id, .cancelled) }
        decide(draft.id, .accepted)
        return true
    }

    /// Undo on an accepted draft: the whole draft goes at once (`corrections.undoDraft`), while no other correction came
    /// after it, and it is offered again.
    func undo(_ draft: AssistantDraftOffer) async {
        guard !busy, let score else { return }
        busy = true
        defer { busy = false }
        let problem = await score.undoAssistantDraft(draft)
        decide(draft.id, problem == nil ? .proposed : .accepted, problem: problem)
    }

    /// The drafts offered with this one: those of its exchange, at the selection or in the open conversation.
    private func drafts(beside draft: String) -> [AssistantDraftOffer] {
        let exchanges = [here?.exchange].compactMap { $0 } + (current?.exchanges ?? [])
        return exchanges.first { shown in shown.drafts?.contains { $0.id == draft } ?? false }?.drafts?.filter { $0.id != draft } ?? []
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

    /// A new conversation of the full assistant that starts with a question and answer of the assistant at the selection
    /// («Продолжить в чате»), as asked and answered there, so the next question there has them.
    func start(_ exchange: AssistantExchange, answer text: String, role: String) {
        var chat = AssistantChat(id: UUID().uuidString, started: Date(), updated: Date(), role: role)
        if !text.isEmpty {
            chat = chat.asking(exchange.question).answered(text)
        }
        var added = exchange
        added.added = true
        // As on the web, the question and the checked answer move, not the tool calls or the drafts.
        added.drafts = nil
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
        case answered(AssistantChat, text: String, fallback: Bool, reason: String?, context: JSONValue)
        case unanswered(String)
    }

    /// The answer, or why there is none: nothing is sent without consent, a model on for the account and a passage a turn
    /// can carry (the web app's MentorAnswerAsync).
    func answer(_ question: String, in chat: AssistantChat, about ask: AssistantAsk) async -> Answer {
        // A question in a draft goes only on the draft's own consent (v1.2).
        guard ask.place == nil ? consentGiven : draftConsentGiven else { return .unanswered("mentor.noConsent") }
        let (status, problem) = await ask.relay.status()
        guard let status else { return .unanswered((problem ?? .unreachable).note) }
        if let note = status.note { return .unanswered(note) }
        let place = ask.place
        guard let context = await Self.context(piece: ask.piece, selection: ask.wholePiece ? nil : ask.selection, wholePiece: ask.wholePiece, place: place) else {
            return .unanswered(await Self.contextRefused(piece: ask.piece, place: place))
        }
        let piece = ask.piece
        let role = chat.role
        let turns = AssistantTurns(
            send: { [weak self] body in
                self?.step(.answering)
                return await ask.relay.turn(body)
            },
            run: { [weak self] call in
                self?.step(.lookingUp)
                return await Self.run(call, role: role, place: place, piece: piece)
            }
        )
        switch await turns.ask(question, in: chat, context: context, language: ask.language) {
        case let .answered(answered, text, fallback, reason):
            return .answered(answered, text: text, fallback: fallback, reason: reason, context: context)
        case let .unanswered(note):
            // Too large for one turn (its size, or the API's 413): in a draft no fewer bars can be selected.
            return .unanswered(place != nil && note == "mentor.tooLarge" ? "mentor.placeTooLarge" : note)
        }
    }

    /// A tool call run on the iPad in music-core, with the turn's role, which music-core checks it against as the relay
    /// does before it runs it: on the piece (`assistant.call`), or in a draft's `place` on that place (`drafts.call`); what
    /// the tool answered, or a refusal the model reads.
    static func run(_ call: AssistantToolCall, role: String, place: AssistantDraftPlace?, piece: Int32) async -> JSONValue {
        let fields: [String: JSONValue] = ["tool": .string(call.name), "parameters": call.arguments, "role": .string(role)]
        let request = JSONValue.object(fields.merging(place?.fields ?? [:]) { $1 }).text
        let name: PieceCore.Call = place == nil ? .assistantCall : .draftsCall
        let answer = await MusicCore.run { PieceCore(piece: piece).send(name, request) }
        return AssistantTurns.content(ofFacade: answer, tool: call.name)
    }

    /// Why a turn has no context (`context` answered nil): the selection is longer than a question takes; in a draft's
    /// place, the place or its variant is longer than 32 bars when the place is still there (`drafts.techniques` reads it,
    /// as music-core refuses only a place it cannot find or one too long), else the place could not be read.
    static func contextRefused(piece: Int32, place: AssistantDraftPlace?) async -> String {
        guard let place else { return "mentor.tooManyBars" }
        let there = await PieceCore(piece: piece).draftTechniques(draft: place.draft, place: place.place).value != nil
        return there ? "mentor.placeTooLong" : "mentor.placeUnread"
    }

    /// The question's phase now (a turn sent, a tool call run on the iPad).
    private func step(_ now: AssistantPhase) {
        phase = now
    }

    /// What a turn sends as its context (music-core's `assistant.context`): the bars the selection touches with their notes,
    /// or the whole piece's digest; nil when the selection spans more bars than a question may hold. In a draft's `place`
    /// (`drafts.context`, v1.2): the place's digest and the notes of its base and of the variant built from; nil when either
    /// is longer than 32 bars.
    static func context(piece: Int32, selection: ScoreSelection?, wholePiece: Bool, place: AssistantDraftPlace? = nil) async -> JSONValue? {
        var request: [String: JSONValue] = ["wholePiece": .bool(wholePiece)]
        if let selection {
            request["selection"] = .object(["onset": .int(Int(selection.onset.rounded())), "end": .int(Int(selection.end.rounded()))])
        }
        let body = JSONValue.object(place?.fields ?? request).text
        let name: PieceCore.Call = place == nil ? .assistantContext : .draftsContext
        let answer = await MusicCore.run { PieceCore(piece: piece).send(name, body) }
        return JSONValue.parse(answer)?["context"]
    }
}

/// The assistant at the selection (#582 items 17.2 and 17.4): the passage it was opened on, the role its question takes,
/// and its one exchange with the answer's text, which «Продолжить в чате» moves into a conversation of the full assistant.
struct AssistantHere: Equatable {
    var selection: ScoreSelection
    var role: String
    var exchange: AssistantExchange?
    var answer = ""
    /// The question on its way, shown until its answer replaces the last one.
    var asking: String?
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
    /// The draft's place it is asked about (v1.2); nil for the piece.
    var place: AssistantDraftPlace?
}
