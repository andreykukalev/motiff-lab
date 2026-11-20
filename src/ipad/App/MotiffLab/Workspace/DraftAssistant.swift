import SwiftUI

/// The assistant in a draft's place (v1.2, ADR 0034 section 6, the plan's answer 7; the drafts spec «Ассистент», «Порядок
/// работы» step 8), on the place's card of the draft's map: «С ассистентом · Pro» opens it on that place, in the role of the
/// place assembler (`AssistantDraftPlace`). Before the first question in a draft, its own consent, listing exactly what such
/// a question sends; then one question at a time through the assistant's one client (`AssistantController.askInDraft`),
/// its answer with the facts it cites, and each step it proposes as a dashed plum draft, never applied
/// (`DraftProposalCard`). Pro: what the API says no question may go for (signing in, Pro, no model) is said here in words,
/// as in the assistant's panel, and the rest of the draft works as before. The model only chose a call of music-core's
/// tools; the notes are music-core's.
struct DraftAssistantSection: View {
    @ObservedObject var assistant: AssistantController
    @ObservedObject var score: ScoreController
    let place: DraftViewAnswer.Place
    let draft: DraftViewAnswer.Draft
    let owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    @State private var flow = AskFlow(surface: .contextual)
    @EnvironmentObject private var account: Account

    private var here: AssistantDraftPlace { AssistantDraftPlace(draft: draft.id, place: place.id, label: place.label) }
    private var canAsk: Bool { !assistant.busy && !AssistantRecovery.stopsAsking(assistant.standing) }

    var body: some View {
        if assistant.inDraft?.place == here {
            section
        } else {
            Button { assistant.openInDraft(here) } label: {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: "insight", size: CGFloat.ds("icon-s"))
                    Text(text["drafts.withAssistant"]).dsType(.label)
                    ProTag()
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            .accessibilityHint(text["drafts.withAssistantHint"])
        }
    }

    private var section: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            head
            if !assistant.draftConsentGiven {
                AssistantConsent(text: text, wording: .draft) { assistant.consent($0, inDraft: true) }
            } else {
                if let asking = assistant.inDraft?.asking {
                    AssistantPendingView(question: asking, phase: assistant.phase, text: text)
                } else if let exchange = assistant.inDraft?.exchange {
                    AssistantExchangeView(exchange: exchange, text: text, enabled: canAsk, signIn: signIn, retry: { ask(exchange.question) })
                    ForEach(exchange.proposals ?? []) { proposal in
                        DraftProposalCard(proposal: proposal, score: score, place: place, text: text, busy: assistant.busy) {
                            accept(proposal)
                        } refuse: {
                            assistant.refuse(proposal)
                        }
                    }
                } else {
                    Button(text["drafts.assistantSuggest"]) { ask(text["drafts.assistantSuggest"]) }
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
        .onAppear { flow.apply(.open) }
        // What the API says the player may do, asked again once they sign in or out.
        .task(id: account.player) { await assistant.check(account.assistant) }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("drafts.assistantTitle", place.label))
    }

    private var head: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Text(text.format("drafts.assistantTitle", place.label)).overline().frame(maxWidth: .infinity, alignment: .leading)
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

    /// A question about the place, from the draft's view, where music-core finds the draft's piece.
    private func ask(_ question: String) {
        guard let piece = score.pieceId else { return }
        let ask = AssistantAsk(
            piece: piece, selection: nil, wholePiece: false, scope: place.label, language: text.language, role: AssistantDraftPlace.role,
            relay: account.assistant, place: here
        )
        Task { await assistant.askInDraft(question, about: ask) }
    }

    /// «Принять», the player's tap: the step kept on the draft (`drafts.accept`, its toast's Undo as for any step), then the
    /// draft's view opens again at the place, the map still open; refused, the proposal says why.
    private func accept(_ proposal: AssistantPlaceProposal) {
        Task {
            let kept = await assistant.accept(proposal) {
                await owner.acceptProposal(proposal.callFields, of: proposal.draft, words: proposal.words)
            }
            if kept { await reopen?(.tick(Double(place.onset))) }
        }
    }

    private func signIn() {
        Task { await account.signIn() }
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
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("plum"), style: StrokeStyle(lineWidth: 1.5, dash: [4, 3])))
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
