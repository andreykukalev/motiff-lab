import SwiftUI

/// The full assistant (#582 item 17.1, behind «Разговор с помощником»): a side panel beside the score, which gives it
/// room rather than covering it. Its head names it and holds the piece's conversations, a new one and Close; under it the
/// consent before the first question, else the conversation; at its foot what the question looks at and the field with
/// Send. The chat's interaction model is AskFlow's (the panel closes only by its button) and the panel keeps clear of the
/// keyboard (KeyboardRoom).
struct AssistantPanel: View {
    @ObservedObject var assistant: AssistantController
    let text: UIText
    /// "This selection · bars 3–4" when there is a selection; nil for none, when only the whole piece is offered.
    let selection: String?
    let ask: (String) -> Void
    @State private var flow = AskFlow(surface: .panel)

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            head
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            if assistant.listShown {
                AssistantChatList(assistant: assistant, text: text)
            } else if !assistant.consentGiven {
                ScrollView { AssistantConsent(text: text) { assistant.consent($0) }.padding(CGFloat.ds("s-4")) }
            } else {
                conversation
                foot
            }
        }
        .background(Color.ds("surface"))
        .keyboardRoom()
        .onAppear { flow.apply(.open) }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["padAssistant.title"])
    }

    private var head: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Text(text["padAssistant.title"]).overline().frame(maxWidth: .infinity, alignment: .leading)
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
        ScrollViewReader { reader in
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                    let exchanges = assistant.current?.exchanges ?? []
                    if exchanges.isEmpty {
                        Text(text["mentor.empty"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                    }
                    ForEach(exchanges) { exchange in
                        AssistantExchangeView(exchange: exchange, text: text)
                    }
                    if assistant.busy {
                        Text(text["mentor.thinking"]).dsType(.caption).foregroundStyle(Color.ds("ink-2")).id("thinking")
                    }
                    Color.clear.frame(height: 1).id("end")
                }
                .padding(CGFloat.ds("s-4"))
                .frame(maxWidth: .infinity, alignment: .leading)
                // A press on the panel's own ground puts the keyboard away; the panel stays.
                .background(Color.clear.contentShape(Rectangle()).onTapGesture { flow.apply(.blur) })
            }
            .scrollDismissesKeyboard(.never)
            .onChange(of: assistant.current?.exchanges.count) { _, _ in
                withAnimation(.ds("t-fast")) { reader.scrollTo("end", anchor: .bottom) }
            }
            .onChange(of: assistant.busy) { _, _ in reader.scrollTo("end", anchor: .bottom) }
        }
    }

    private var foot: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["mentor.sentNote"]).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
            HStack(spacing: CGFloat.ds("s-2")) {
                if let selection {
                    Button(selection) { assistant.wholePiece = false }
                        .buttonStyle(DSChipStyle(on: !assistant.wholePiece))
                        .accessibilityAddTraits(!assistant.wholePiece ? .isSelected : [])
                }
                Button(text["ask.wholePiece"]) { assistant.wholePiece = true }
                    .buttonStyle(DSChipStyle(on: assistant.wholePiece || selection == nil))
                    .accessibilityAddTraits(assistant.wholePiece || selection == nil ? .isSelected : [])
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text["ask.scope"])
            AskInputRow(flow: $flow, text: text, enabled: !assistant.busy, send: ask)
            Button(text["mentor.withdraw"]) { assistant.consent(false) }
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-3"))
        }
        .padding(CGFloat.ds("s-4"))
        .background(Color.ds("surface-2"))
    }
}

/// A question and its answer: the answer's words with each fact it cites in the engine's words (in the accent, as a
/// pointer to the app's facts), or why there is none.
struct AssistantExchangeView: View {
    let exchange: AssistantExchange
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(exchange.question).dsType(.bodyStrong)
            Text(exchange.scope).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
            if let note = exchange.note {
                Text(text[note]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            } else {
                Text(text[exchange.fallback ? "mentor.engineFacts" : "mentor.interpretation"]).overline()
                answer.dsType(.body).foregroundStyle(Color.ds("ink"))
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
/// and what never goes; nothing is sent until the player agrees.
struct AssistantConsent: View {
    let text: UIText
    let decide: (Bool) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["mentor.consentTitle"]).dsType(.heading)
            Text(text["mentor.consentLead"]).dsType(.body)
            ForEach(["mentor.consentNotes", "mentor.consentDigest", "mentor.consentQuestion"], id: \.self) { key in
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

/// The piece's conversations kept on this iPad, newest first: each by its first question and when it was last asked.
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
                    .buttonStyle(DSItemStyle(on: chat.id == assistant.currentId))
                    .accessibilityAddTraits(chat.id == assistant.currentId ? .isSelected : [])
                }
            }
            .padding(CGFloat.ds("s-3"))
        }
    }
}
