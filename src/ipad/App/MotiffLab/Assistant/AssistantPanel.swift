import SwiftUI

/// The full assistant (#582 item 17.1; every Ask opens it since version 0.6.3): a side panel beside the score, which gives
/// it room rather than covering it. Its head names it and holds the piece's conversations, a new one and Close; under it
/// the consent before the first question, else the conversation, with the question on its way and what is happening to
/// it; at its foot why nothing may be asked now, with what to do (sign in, try again), what the question looks at and
/// the field with Send. The chat's interaction model is AskFlow's (the panel closes only by its button) and the panel
/// keeps clear of the keyboard (KeyboardRoom).
struct AssistantPanel: View {
    @ObservedObject var assistant: AssistantController
    let text: UIText
    /// The selection, and its chip's words ("This selection · bars 3–4"); nil for none, when only the whole piece is offered.
    let selection: ScoreSelection?
    let label: String?
    let ask: (String) -> Void
    /// An answer's practice action tapped: the score and the transport behind the panel take it; the conversation stays.
    let practise: (AssistantPracticeOffer) -> Void
    @State private var flow = AskFlow(surface: .panel)
    @EnvironmentObject private var account: Account

    private var whole: Bool { assistant.scope.isWholePiece(selection) }
    private var canAsk: Bool { !assistant.busy && !AssistantRecovery.stopsAsking(assistant.standing) }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            head
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            if assistant.listShown {
                AssistantChatList(assistant: assistant, text: text)
            } else if !assistant.consentGiven {
                ScrollView { AssistantConsent(text: text, again: assistant.consentAskedAgain) { assistant.consent($0) }.padding(CGFloat.ds("s-4")) }
            } else {
                conversation
                foot
            }
        }
        .background(Color.ds("surface"))
        .keyboardRoom()
        .onAppear { flow.apply(.open) }
        // What the API says the player may do, asked again once they sign in or out.
        .task(id: account.player) { await assistant.check(account.assistant) }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["padAssistant.title"])
    }

    private var head: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Text(text["padAssistant.title"]).overline().frame(maxWidth: .infinity, alignment: .leading)
            // The piece's conversations, kept on this iPad (#582 item 17.1).
            Button {
                assistant.listShown.toggle()
            } label: {
                Text(text["padAssistant.conversations"]).dsType(.label).padding(.horizontal, CGFloat.ds("s-2")).frame(height: 32)
            }
            .buttonStyle(BarActionStyle(on: assistant.listShown))
            .accessibilityAddTraits(assistant.listShown ? .isSelected : [])
            Button {
                assistant.startNew()
            } label: {
                Icon(name: "plus").frame(width: 32, height: 32)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["padAssistant.new"])
            Button {
                flow.apply(.close)
                assistant.close()
            } label: {
                Icon(name: "close").frame(width: 32, height: 32)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["drawer.close"])
        }
        .padding(.leading, CGFloat.ds("s-4"))
        .padding(.trailing, CGFloat.ds("s-2"))
        .padding(.vertical, CGFloat.ds("s-2"))
    }

    private var conversation: some View {
        let exchanges = assistant.current?.exchanges ?? []
        // A new answer from its start, unless the player has scrolled up (ChatFollow); the question on its way always.
        return FollowingScroll(newest: assistant.pending == nil ? exchanges.last?.id : "pending", asked: assistant.pending != nil) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                if exchanges.isEmpty, assistant.pending == nil { empty }
                ForEach(exchanges) { exchange in
                    AssistantExchangeView(exchange: exchange, text: text, enabled: canAsk, signIn: signIn, retry: {
                        if let question = assistant.forget(exchange) { ask(question) }
                    }, practise: practise)
                    .id(exchange.id)
                    // The drafts its tool calls made, with «Помощник правит ноты» (#582 item 17.3).
                    ForEach(exchange.drafts ?? []) { draft in
                        AssistantDraftCard(draft: draft, text: text, busy: assistant.busy) {
                            Task { await assistant.accept(draft) }
                        } cancel: {
                            assistant.cancel(draft)
                        } undo: {
                            Task { await assistant.undo(draft) }
                        }
                    }
                }
                if let pending = assistant.pending {
                    AssistantPendingView(question: pending, phase: assistant.phase, text: text).id("pending")
                }
            }
            .padding(CGFloat.ds("s-4"))
            .frame(maxWidth: .infinity, alignment: .leading)
            // A press on the panel's own ground puts the keyboard away; the panel stays.
            .background(Color.clear.contentShape(Rectangle()).onTapGesture { flow.apply(.blur) })
        }
        .scrollDismissesKeyboard(.never)
    }

    /// Before the first question: what it does, and three questions to ask about what it looks at.
    private var empty: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["mentor.empty"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            FlowRow(spacing: CGFloat.ds("s-2")) {
                ForEach(suggestions, id: \.self) { suggestion in
                    Button(suggestion) { ask(suggestion) }
                        .buttonStyle(DSChipStyle())
                        .disabled(!canAsk)
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text["ask.suggestions"])
        }
    }

    /// Three questions to ask here, as the web app offers them: about the passage, or about the whole piece.
    private var suggestions: [String] {
        whole
            ? [text["ask.suggest.start"], text["ask.suggest.climax"], text["ask.suggest.form"]]
            : [text["ask.suggest.harmony"], text["ask.suggest.practise"], text["ask.suggest.returns"]]
    }

    private var foot: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            // Why nothing may be asked now, said before anything is typed (the lead's decision 3).
            if let standing = assistant.standing, AssistantRecovery.stopsAsking(standing) {
                AssistantProblemLine(note: standing, text: text, enabled: !assistant.checking, signIn: signIn) {
                    Task { await assistant.check(account.assistant) }
                }
            } else {
                Text(text["mentor.sentNote"]).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                if let label {
                    Button(label) { Task { await assistant.pickShowingBars(wholePiece: false, selection: selection) } }
                        .buttonStyle(DSChipStyle(on: !whole))
                        .accessibilityAddTraits(!whole ? .isSelected : [])
                }
                Button(text["ask.wholePiece"]) { Task { await assistant.pickShowingBars(wholePiece: true, selection: selection) } }
                    .buttonStyle(DSChipStyle(on: whole))
                    .accessibilityAddTraits(whole ? .isSelected : [])
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text["ask.scope"])
            AskInputRow(flow: $flow, text: text, enabled: canAsk, send: ask)
            Button(text["mentor.withdraw"]) { assistant.consent(false) }
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-3"))
        }
        .padding(CGFloat.ds("s-4"))
        .background(Color.ds("surface-2"))
    }

    private func signIn() {
        Task { await account.signIn() }
    }
}

/// The question on its way and what is happening to it (the API answers a turn whole, so this is the wait's one state:
/// sending, the assistant answering, looking in the score).
struct AssistantPendingView: View {
    let question: String
    let phase: AssistantPhase
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(question).dsType(.bodyStrong)
            AssistantPhaseLine(phase: phase, text: text)
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .accessibilityElement(children: .combine)
    }
}

/// What is happening to the question on its way, with a spinner; "Sending…" until the turn has gone.
struct AssistantPhaseLine: View {
    let phase: AssistantPhase
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            ProgressView().controlSize(.small).tint(Color.ds("ink-2"))
            Text(text[phase.key ?? "padAssistant.sending"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        }
    }
}

/// Why a question got no answer, or why none may be asked now, with what to do about it: Sign in, or Try again (no
/// connection, the server's or the model's error); the Pro gate in plum, as the API reports it.
struct AssistantProblemLine: View {
    let note: String
    let text: UIText
    var enabled = true
    let signIn: () -> Void
    let retry: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text[AssistantRecovery.shown(note)])
                .dsType(.caption)
                .foregroundStyle(Color.ds(AssistantRecovery.isPro(note) ? "plum" : "ink-2"))
                .fixedSize(horizontal: false, vertical: true)
            switch AssistantRecovery.of(note) {
            case .signIn:
                Button(text["settings.signIn"], action: signIn)
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            case .retry:
                Button(text["padAssistant.retry"], action: retry)
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                    .disabled(!enabled)
            case .nothing:
                EmptyView()
            }
        }
    }
}

/// A question and its answer: the answer's words with each fact it cites in the engine's words (in the accent, as a
/// pointer to the app's facts), or why there is none and what to do about it.
struct AssistantExchangeView: View {
    let exchange: AssistantExchange
    let text: UIText
    /// Whether Try again may ask now (nothing else on its way).
    var enabled = true
    var signIn: () -> Void = {}
    var retry: () -> Void = {}
    /// One of its practice actions tapped (`practice.suggest`).
    var practise: (AssistantPracticeOffer) -> Void = { _ in }

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(exchange.question).dsType(.bodyStrong)
            Text(exchange.scope).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
            if let note = exchange.note {
                AssistantProblemLine(note: note, text: text, enabled: enabled, signIn: signIn, retry: retry)
            } else {
                Text(text[exchange.fallback ? "mentor.engineFacts" : "mentor.interpretation"]).overline()
                // Why the model's answer was not shown, in words (v1.2, assistant option A item 4).
                if let reason = exchange.reasonKey {
                    Text(text[reason]).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
                }
                answer.dsType(.body).foregroundStyle(Color.ds("ink"))
                ForEach(Array((exchange.practice ?? []).enumerated()), id: \.offset) { _, offer in
                    AssistantPracticeChip(offer: offer) { practise(offer) }
                }
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
    }

    private var answer: Text {
        exchange.answer.reduce(Text("")) { words, piece in
            piece.cites == nil ? words + Text(piece.text) : words + Text("‹\(piece.text)›").foregroundStyle(Color.ds("accent"))
        }
    }
}

/// The consent before the first question (the web app's MentorPanel): exactly what each question sends, where it goes,
/// and what never goes; nothing is sent until the player agrees. Since v1.2 it lists the piece's counted facts too, and a
/// player who agreed to the list before is asked again (`AssistantController.consented`).
struct AssistantConsent: View {
    let text: UIText
    /// The player agreed to the list before it changed: the consent says why it asks again.
    var again = false
    let decide: (Bool) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["mentor.consentTitle"]).dsType(.heading)
            if again {
                Text(text["mentor.consentAgain"]).dsType(.body).fixedSize(horizontal: false, vertical: true)
            }
            Text(text["mentor.consentLead"]).dsType(.body)
            ForEach(["mentor.consentNotes", "mentor.consentDigest", "mentor.consentPiece", "mentor.consentQuestion"], id: \.self) { key in
                HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                    Text("•")
                    Text(text[key]).fixedSize(horizontal: false, vertical: true)
                }
                .dsType(.body)
            }
            Text(text["mentor.consentWhere"]).dsType(.body)
            Text(text["mentor.consentNot"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["mentor.agree"]) { decide(true) }
                    .buttonStyle(DSButtonStyle(kind: .primary))
                Button(text["mentor.notNow"]) { decide(false) }
                    .buttonStyle(DSButtonStyle(kind: .quiet))
            }
        }
        .foregroundStyle(Color.ds("ink"))
        .accessibilityElement(children: .contain)
    }
}

/// The piece's conversations kept on this iPad, newest first: each by its first question and when it was last asked. The
/// open one is tinted in a row's rounded rectangle (`DSItemStyle`'s `.rounded` outline, `r-s`), not a capsule (Andrei,
/// v0.8 finding 9).
struct AssistantChatList: View {
    @ObservedObject var assistant: AssistantController
    let text: UIText

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                if assistant.chats.isEmpty {
                    Text(text["padAssistant.noConversations"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
                ForEach(assistant.chats) { chat in
                    Button {
                        assistant.choose(chat.id)
                    } label: {
                        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                            Text(chat.title).dsType(.label).lineLimit(2).multilineTextAlignment(.leading)
                            Text(chat.updated.formatted(date: .abbreviated, time: .shortened))
                                .dsType(.caption).foregroundStyle(Color.ds("ink-3"))
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .buttonStyle(DSItemStyle(on: chat.id == assistant.currentId, outline: .rounded))
                    .accessibilityAddTraits(chat.id == assistant.currentId ? .isSelected : [])
                }
            }
            .padding(CGFloat.ds("s-3"))
        }
    }
}
