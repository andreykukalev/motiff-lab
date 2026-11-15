import SwiftUI

/// The assistant at the selection (#582 items 17.2 and 17.4, with «Разговор с помощником» on): Ask in the selection's menu
/// turns the menu into a small chat where the menu was, which the page places beside the selection rather than over it.
/// It looks at the selected bars, takes one question at a time with no conversation before it (the field, Send and the
/// keyboard's own dictation), and shows the answer with the facts it cites and any draft the assistant made, to accept or
/// cancel. «Добавить в контекст» moves the question and answer into the full assistant's open conversation.
///
/// It closes by itself once its work is done: after «Добавить в контекст», after a draft is accepted or every draft is
/// cancelled, and when the selection goes or changes while its field is not in use; a selection changed while the
/// player types only moves what it looks at (AskFlow, a press outside never closes a chat being typed in). Close closes it.
struct AssistantHereCard: View {
    @ObservedObject var assistant: AssistantController
    @ObservedObject var score: ScoreController
    let text: UIText
    let ask: (String) -> Void
    @State private var flow = AskFlow(surface: .contextual)

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            head
            if !assistant.consentGiven {
                ScrollView { AssistantConsent(text: text) { assistant.consent($0) } }
                    .frame(maxHeight: Self.answerHeight)
            } else {
                if let exchange = assistant.here?.exchange {
                    ScrollView { answer(exchange) }
                        .frame(maxHeight: Self.answerHeight)
                        .scrollDismissesKeyboard(.never)
                }
                if assistant.busy {
                    Text(text["mentor.thinking"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
                AskInputRow(flow: $flow, text: text, enabled: !assistant.busy, send: ask)
                Text(text["mentor.sentNote"]).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
            }
        }
        .frame(width: ScoreScreen.contentsWidth)
        .padding(CGFloat.ds("s-3"))
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
        .keyboardLift()
        .onAppear { flow.apply(.open) }
        .onChange(of: score.selection) { _, now in
            if let now, flow.focused || !flow.question.isEmpty { assistant.follow(now) } else { close() }
        }
        .onDisappear { if score.selection == nil { assistant.closeHere() } }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["padAssistant.title"])
    }

    /// The answer's room: a card no taller than it is wide, so the menu's place beside the selection still fits it.
    static let answerHeight = ScoreScreen.contentsWidth * 3 / 4

    private var head: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                Text(text["padAssistant.title"]).overline()
                Text(score.askLabel ?? "").dsType(.caption).foregroundStyle(Color.ds("ink-2")).lineLimit(1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Button(action: close) {
                Icon(name: "close").frame(width: 32, height: 32)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["drawer.close"])
        }
    }

    private func answer(_ exchange: AssistantExchange) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            AssistantExchangeView(exchange: exchange, text: text)
            ForEach(exchange.drafts ?? []) { draft in
                AssistantDraftCard(draft: draft, text: text, busy: assistant.busy) {
                    Task {
                        // Accepted, the draft's work is done here; refused, it says why and stays.
                        if await assistant.accept(draft) { close() }
                    }
                } cancel: {
                    assistant.cancel(draft)
                    if assistant.here?.exchange?.drafts?.allSatisfy({ $0.state != .proposed }) ?? true { close() }
                }
            }
            if exchange.note == nil {
                Button(text["padAssistant.addToContext"]) {
                    flow.apply(.close)
                    assistant.addHere()
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                .accessibilityHint(text["padAssistant.addToContextHint"])
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        // A press on the card's own ground puts the keyboard away; the card stays.
        .background(Color.clear.contentShape(Rectangle()).onTapGesture { flow.apply(.blur) })
    }

    private func close() {
        flow.apply(.close)
        assistant.closeHere()
    }

    /// Ask in the selection's menu with the assistant on: it opens at the selection, in the role «Помощник правит ноты» sets.
    @MainActor static func open(_ assistant: AssistantController, score: ScoreController, settings: DeviceSettings) {
        guard let selection = score.selection else { return }
        assistant.openHere(on: selection, role: AssistantSide.role(settings))
    }

    /// What a question here is asked about: the selected bars of the piece open in music-core.
    @MainActor static func ask(score: ScoreController, text: UIText, role: String, relay: AssistantRelay) -> AssistantAsk? {
        guard let piece = score.pieceId, let selection = score.selection else { return nil }
        return AssistantAsk(
            piece: piece, selection: selection, wholePiece: false, scope: score.askLabel ?? "", language: text.language, role: role, relay: relay
        )
    }
}

/// Lifts a small chat at the selection above the iPad's keyboard where the keyboard would cover it (#582 item 17.4), as
/// KeyboardRoom keeps a panel's content clear of it; nothing else on the screen moves. The lift is worked out from where
/// the chat is laid out, which the lift does not move (its frame is read outside the offset), so it never feeds back.
struct KeyboardLift: ViewModifier {
    @State private var keyboard: CGRect?
    @State private var frame: CGRect = .zero

    func body(content: Content) -> some View {
        let lift = Self.lift(of: frame, keyboard: keyboard, gap: CGFloat.ds("s-3"))
        content
            .offset(y: -lift)
            .animation(.ds("t-fast"), value: lift)
            .background {
                GeometryReader { proxy in
                    Color.clear
                        .onAppear { frame = proxy.frame(in: .global) }
                        .onChange(of: proxy.frame(in: .global)) { _, now in frame = now }
                }
            }
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillChangeFrameNotification)) { note in
                keyboard = KeyboardRoom.frame(of: note)
            }
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillHideNotification)) { _ in
                keyboard = nil
            }
    }

    /// How far `view` goes up so the keyboard leaves it and a gap clear, both in the window's coordinates: 0 when the
    /// keyboard covers none of it (none, a floating one beside it, or one below it); never above the window's top.
    static func lift(of view: CGRect, keyboard: CGRect?, gap: CGFloat) -> CGFloat {
        guard let keyboard, !keyboard.isEmpty, !view.isEmpty,
              keyboard.minX < view.maxX, keyboard.maxX > view.minX,
              keyboard.minY < view.maxY, keyboard.maxY >= view.maxY
        else { return 0 }
        return max(0, min(view.maxY - keyboard.minY + gap, view.minY - gap))
    }
}

extension View {
    /// Lifts a small chat above the iPad's keyboard where it would cover it (KeyboardLift).
    func keyboardLift() -> some View {
        modifier(KeyboardLift())
    }
}
