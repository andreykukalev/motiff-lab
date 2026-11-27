import SwiftUI

/// The assistant in a draft's place (v1.2, ADR 0034 section 6, the plan's answer 7; the drafts spec «Ассистент», «Порядок
/// работы» step 8), on the place's card of the draft's map: «С ассистентом · Pro» opens it on that place. In a place with a
/// variant besides its base it assembles the place (`AssistantDraftPlace.role`) or explains how the variants differ and
/// where they came from (`explainer`, v1.4, F5), chosen before the first question; in a bridge place it chooses among the
/// candidates (`seamChooser`, v1.4, F5). Before the first question in a draft, its own consent, listing exactly what such a
/// question sends; then a conversation (v1.4, the plan's answer 8: questions in a row, each with the ones before it, as in
/// the assistant's panel) through the assistant's one client (`AssistantController.askInDraft`), each answer with the facts
/// it cites, and each step it proposes as a dashed plum draft, never applied (`DraftProposalCard`). Pro: what the API says
/// no question may go for (signing in, Pro, no model) is said here in words, as in the assistant's panel, and the rest of
/// the draft works as before. The model only chose a call of music-core's tools; the notes are music-core's.
struct DraftAssistantSection: View {
    @ObservedObject var assistant: AssistantController
    @ObservedObject var score: ScoreController
    let place: DraftViewAnswer.Place
    let draft: DraftViewAnswer.Draft
    @ObservedObject var owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    @State private var flow = AskFlow(surface: .contextual)
    @EnvironmentObject private var account: Account

    /// The place as the assistant names it.
    static func named(_ place: DraftViewAnswer.Place, of draft: DraftViewAnswer.Draft) -> AssistantDraftPlace {
        // Its base and the first variant besides it, as music-core's DraftScope reads them.
        let basis: [String] = place.bridge ? [] : [place.variants.first { $0.base }, place.variants.first { !$0.base }].compactMap { $0?.id }
        return AssistantDraftPlace(draft: draft.id, place: place.id, label: place.label, basis: basis)
    }

    /// Whether a place offers the assistant: a bridge place (its chooser), or a place with a variant besides its base
    /// (DRAFT-11); none in a place of the base alone.
    static func offered(_ place: DraftViewAnswer.Place) -> Bool {
        place.bridge || place.variants.contains { !$0.base }
    }

    private var here: AssistantDraftPlace { Self.named(place, of: draft) }
    private var roles: [String] { AssistantDraftPlace.roles(bridge: place.bridge) }
    /// The assistant's conversation here; nil while it is closed here.
    private var shownTalk: AssistantInDraft? { assistant.inDraft.flatMap { $0.place == here ? $0 : nil } }
    /// A question may go: none on its way, no step of the draft on its way (`SourcesController.stepping`), nothing the API
    /// says stops one.
    private var canAsk: Bool { !assistant.busy && !owner.stepping && !AssistantRecovery.stopsAsking(assistant.standing) }

    var body: some View {
        if let talk = shownTalk {
            section(talk)
        } else {
            Button { assistant.openInDraft(here, role: roles[0]) } label: { DraftAssistantLabel(text: text) }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            .accessibilityHint(text[place.bridge ? "drafts.withAssistantBridgeHint" : "drafts.withAssistantHint"])
        }
    }

    private func section(_ talk: AssistantInDraft) -> some View {
        let started = !talk.chat.exchanges.isEmpty || talk.asking != nil
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            head(talk, started: started)
            if !assistant.draftConsentGiven {
                AssistantConsent(text: text, wording: .draft) { assistant.consent($0, inDraft: true) }
            } else {
                if started {
                    conversation(talk)
                } else {
                    if roles.count > 1 { roleChips(talk) }
                    Button(text[Self.suggestion(talk.chat.role)]) { ask(text[Self.suggestion(talk.chat.role)]) }
                        .buttonStyle(DSChipStyle())
                        .disabled(!canAsk)
                }
                if let standing = assistant.standing, AssistantRecovery.stopsAsking(standing) {
                    // Why nothing may be asked now, said before anything is typed (the lead's decision 3).
                    AssistantProblemLine(note: standing, text: text, enabled: !assistant.checking, signIn: signIn) {
                        Task { await assistant.check(account.assistant) }
                    }
                }
                AskInputRow(flow: $flow, text: text, enabled: canAsk, send: ask)
                Text(text["drafts.assistantSentNote"]).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
            }
        }
        .keyboardLift()
        // The question typed before the card was laid out anew (a rotation, Split View) is still there (r-1).
        .onAppear {
            flow.apply(.open)
            flow.apply(.type(assistant.unsentInDraft))
        }
        .onChange(of: flow.draft) { _, typed in assistant.keepUnsent(typed, inDraft: true) }
        // What the API says the player may do, asked again once they sign in or out.
        .task(id: account.player) { await assistant.check(account.assistant) }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("drafts.assistantTitle", place.label))
    }

    /// Its title, what it does here once begun, a new conversation and Close.
    private func head(_ talk: AssistantInDraft, started: Bool) -> some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                Text(text.format("drafts.assistantTitle", place.label)).overline()
                if started, roles.count > 1 {
                    Text(text[Self.roleName(talk.chat.role)]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if started {
                Button { assistant.startInDraft(role: roles[0]) } label: {
                    Icon(name: "plus").frame(width: CGFloat.ds("s-8"), height: CGFloat.ds("s-8"))
                }
                .buttonStyle(BarActionStyle())
                .disabled(assistant.busy)
                .accessibilityLabel(text["padAssistant.new"])
            }
            Button {
                flow.apply(.close)
                assistant.closeInDraft()
            } label: {
                Icon(name: "close").frame(width: CGFloat.ds("s-8"), height: CGFloat.ds("s-8"))
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["drawer.close"])
        }
    }

    /// What the conversation does, chosen before its first question: assemble the place, or explain its variants.
    private func roleChips(_ talk: AssistantInDraft) -> some View {
        FlowRow(spacing: CGFloat.ds("s-2")) {
            ForEach(roles, id: \.self) { role in
                Button(text[Self.roleName(role)]) { assistant.startInDraft(role: role) }
                    .buttonStyle(DSChipStyle(on: role == talk.chat.role))
                    .accessibilityAddTraits(role == talk.chat.role ? .isSelected : [])
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["drafts.assistantRoles"])
    }

    /// The questions asked here and what came of them, the steps each proposes under it, and the question on its way; a
    /// new answer shows from its start (ChatFollow), the conversation scrolling within the card.
    private func conversation(_ talk: AssistantInDraft) -> some View {
        FollowingScroll(newest: talk.asking == nil ? talk.chat.exchanges.last?.id : "pending", asked: talk.asking != nil) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                ForEach(talk.chat.exchanges) { exchange in
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                        AssistantExchangeView(exchange: exchange, text: text, enabled: canAsk, signIn: signIn, retry: {
                            if let question = assistant.forget(exchange) { ask(question) }
                        })
                        ForEach(exchange.proposals ?? []) { proposal in
                            // «Принять» waits while a step of the draft is on its way (`SourcesController.stepping`).
                            DraftProposalCard(proposal: proposal, score: score, place: place, text: text, busy: assistant.busy || owner.stepping) {
                                accept(proposal)
                            } refuse: {
                                assistant.refuse(proposal)
                            }
                        }
                    }
                    .id(exchange.id)
                }
                if let asking = talk.asking {
                    AssistantPendingView(question: asking, phase: assistant.phase, text: text).id("pending")
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            // A press on the conversation's own ground puts the keyboard away; the card stays.
            .background(Color.clear.contentShape(Rectangle()).onTapGesture { flow.apply(.blur) })
        }
        .frame(maxHeight: AssistantHereCard.answerHeight)
        .scrollDismissesKeyboard(.never)
    }

    /// The name of a role in a draft, as its chip and the head say it.
    static func roleName(_ role: String) -> String {
        switch role {
        case AssistantDraftPlace.explainer: "drafts.assistantExplain"
        case AssistantDraftPlace.seamChooser: "drafts.assistantChoose"
        default: "drafts.assistantAssemble"
        }
    }

    /// The question a role offers before the first one.
    static func suggestion(_ role: String) -> String {
        switch role {
        case AssistantDraftPlace.explainer: "drafts.assistantExplainSuggest"
        case AssistantDraftPlace.seamChooser: "drafts.assistantBridgeSuggest"
        default: "drafts.assistantSuggest"
        }
    }

    /// A question about the place, asked on the draft's piece (the Sources' own number, which stays open while the draft's
    /// view opens again after a step, so a question on its way then still finds the draft).
    private func ask(_ question: String) {
        let piece = owner.piece
        guard piece != 0 else { return }
        let ask = AssistantAsk(
            piece: piece, selection: nil, wholePiece: false, scope: place.label, language: text.language, role: shownTalk?.chat.role ?? roles[0],
            relay: account.assistant, place: here
        )
        Task { await assistant.askInDraft(question, about: ask) }
    }

    /// «Принять», the player's tap: the step kept on the draft (`drafts.accept`, its toast's Undo as for any step) while its
    /// call still proposes what its card says (`proposedAgain`, v1.4), then the draft's view opens again at the place, the
    /// map and the conversation still open; refused, the proposal says why.
    private func accept(_ proposal: AssistantPlaceProposal) {
        let piece = owner.piece
        Task {
            let kept = await assistant.accept(proposal) {
                await owner.acceptProposal(proposal.callFields, of: proposal.draft, words: proposal.words) { await proposal.proposedAgain(on: piece) }
            }
            if kept { await owner.reopening { await reopen?(.tick(Double(place.onset))) } }
        }
    }

    private func signIn() {
        Task { await account.signIn() }
    }
}

/// «С ассистентом · Pro», the words of the assistant's button in a draft: on a place's card of the map, and since v1.5
/// (d-11) on the card «Новый черновик», which the screen hands it (`DraftSheet.assistLabel`).
struct DraftAssistantLabel: View {
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Icon(name: "insight", size: CGFloat.ds("icon-s"))
            Text(text["drafts.withAssistant"]).dsType(.label)
            ProTag()
        }
    }
}

/// A step the assistant proposes in a place, as a dashed plum draft: what the app composed, not applied (the kit's frame of
/// a variation, 02-design-system «Ossia», as Insights draws one): what it takes in the engine's words, «▶ До» (the place as
/// the draft has it now) and «▶ После» (as the proposal would leave it), and «Отказаться» and «Принять»; once decided, what
/// became of it, or why music-core did not take it.
struct DraftProposalCard: View {
    let proposal: AssistantPlaceProposal
    let score: ScoreController
    let place: DraftViewAnswer.Place
    let text: UIText
    var busy = false
    let accept: () -> Void
    let refuse: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("drafts.proposalOverline", place.label)).overline()
            if let title = proposal.title {
                Text(title).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
            }
            if !proposal.explanation.isEmpty {
                Text(proposal.explanation).dsType(.body).foregroundStyle(Color.ds("ink")).fixedSize(horizontal: false, vertical: true)
            }
            Text(text["drafts.proposalByEngine"]).dsType(.caption).foregroundStyle(Color.ds("ink-3")).fixedSize(horizontal: false, vertical: true)
            HStack(spacing: CGFloat.ds("s-2")) {
                hear("drafts.proposalBefore") { score.hearDraft(over: Double(place.onset)..<Double(place.end)) }
                hear("drafts.proposalAfter") { score.hearProposal(proposal) }
            }
            if let problem = proposal.problem {
                Text(problem.isEmpty ? text["drafts.proposalStale"] : problem).dsType(.caption).foregroundStyle(Color.ds("danger"))
            }
            decision
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .composedFrame()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("drafts.proposalOverline", place.label))
    }

    private func hear(_ key: String, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Label { Text(text[key]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
    }

    @ViewBuilder private var decision: some View {
        switch proposal.state {
        case .proposed:
            HStack(spacing: CGFloat.ds("s-2")) {
                Spacer(minLength: 0)
                Button(text["drafts.proposalRefuse"], action: refuse)
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                Button(text["drafts.proposalAccept"], action: accept)
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    .disabled(busy)
            }
        case .accepted:
            Text(text["drafts.proposalAccepted"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        case .cancelled:
            Text(text["drafts.proposalRefused"]).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
        }
    }
}
