import SwiftUI

/// The questions asked about the open piece, in order (testing item 23): one conversation, which the popover where Ask was
/// pressed and the foot of the Insights stream both show. Nothing is sent anywhere until the assistant is built (part C).
@MainActor
final class AskConversation: ObservableObject {
    @Published private(set) var questions: [String] = []

    func ask(_ question: String) {
        let asked = question.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !asked.isEmpty else { return }
        questions.append(asked)
    }
}

/// What Ask looks at and what it is given, where it was pressed: the selection or phrase with its label, or the whole
/// piece, and the Piece DNA map when it came from there.
struct AskContext {
    /// "This phrase · bars 9–16"; nil with nothing selected, when only the whole piece is offered.
    var selection: String?
    var wholePiece: Bool
    var title: String
    /// The map in words when Ask about the piece came from Piece DNA: "The Piece DNA map: 4 ideas".
    var map: String?
    /// The form's sections, for a question comparing a return with the first.
    var form: [DnaMap.Section] = []
}

/// The assistant as a popover where Ask was pressed (0018 section 7; testing item 23; the web app's AssistantPanel as a
/// popover): the scope chips, what it knows, the conversation, three questions to ask, the field with the microphone, and
/// Open in Insights, which goes on in the stream. The place under it stays as it was. It needs an AI model, so it is Pro
/// in full: the free tier gets one plum line and nothing is sent; in Pro a question joins the conversation and the answers
/// are said to be coming.
struct AskPopover: View {
    @ObservedObject var conversation: AskConversation
    @ObservedObject var account: Account
    let text: UIText
    @State var context: AskContext
    let openInInsights: (() -> Void)?
    let close: () -> Void
    @State private var draft = ""
    @FocusState private var typing: Bool

    private var allowed: Bool { account.allows(.assistant) }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                HStack {
                    Text(text["ask.title"]).overline()
                    Spacer()
                    Button(action: close) {
                        Icon(name: "close").frame(width: 32, height: 32)
                    }
                    .buttonStyle(BarActionStyle())
                    .accessibilityLabel(text["drawer.close"])
                }
                HStack(spacing: CGFloat.ds("s-2")) {
                    if let selection = context.selection {
                        Button(selection) { context.wholePiece = false }
                            .buttonStyle(DSChipStyle(on: !context.wholePiece))
                            .accessibilityAddTraits(!context.wholePiece ? .isSelected : [])
                    }
                    Button(text["ask.wholePiece"]) { context.wholePiece = true }
                        .buttonStyle(DSChipStyle(on: context.wholePiece || context.selection == nil))
                        .accessibilityAddTraits(context.wholePiece ? .isSelected : [])
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["ask.scope"])
                DisclosureGroup(text.format("ask.knows", text.count("ask.items", knows.count))) {
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                        ForEach(knows, id: \.self) { item in
                            Text(item).dsType(.caption)
                        }
                    }
                    .padding(.top, CGFloat.ds("s-1"))
                }
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .tint(Color.ds("ink-2"))
                conversationView
                FlowRow(spacing: CGFloat.ds("s-2")) {
                    ForEach(suggestions, id: \.self) { suggestion in
                        Button(suggestion) { conversation.ask(suggestion) }
                            .buttonStyle(DSChipStyle())
                            .disabled(!allowed)
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["ask.suggestions"])
                HStack(spacing: CGFloat.ds("s-2")) {
                    TextField(text["ask.placeholder"], text: $draft)
                        .dsType(.body)
                        .foregroundStyle(Color.ds("ink"))
                        .focused($typing)
                        .submitLabel(.send)
                        .onSubmit {
                            conversation.ask(draft)
                            draft = ""
                        }
                        .disabled(!allowed)
                    Icon(name: "mic")
                        .foregroundStyle(Color.ds("ink-3"))
                        .accessibilityLabel(text["ask.speak"])
                }
                .padding(.horizontal, CGFloat.ds("s-4"))
                .frame(minHeight: CGFloat.ds("h-touch"))
                .background(Color.ds("surface"), in: Capsule())
                .overlay(Capsule().strokeBorder(Color.ds("hairline-strong")))
                if let openInInsights {
                    HStack {
                        Text(text["ask.keptInInsights"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                        Spacer()
                        Button(text["ask.openInInsights"] + " ›", action: openInInsights)
                            .dsType(.label)
                            .foregroundStyle(Color.ds("accent"))
                    }
                }
            }
            .padding(CGFloat.ds("s-4"))
        }
        .frame(width: 340)
        .frame(maxHeight: 520)
        .background(Color.ds("surface"))
        .presentationCompactAdaptation(.popover)
        .onAppear { if allowed { typing = true } }
    }

    @ViewBuilder private var conversationView: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            ForEach(Array(conversation.questions.enumerated()), id: \.offset) { _, question in
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    Text(question).dsType(.bodyStrong)
                    Text(text[allowed ? "ask.notYet" : "ask.notSent"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
                .padding(CGFloat.ds("s-3"))
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            }
            if !allowed {
                (Text(text["ask.pro"]).foregroundStyle(Color.ds("plum")).fontWeight(.semibold) + Text(" " + text["ask.offer"]))
                    .dsType(.body)
                    .padding(CGFloat.ds("s-3"))
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.ds("plum-tint"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            } else if conversation.questions.isEmpty {
                Text(text["ask.notYet"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        }
    }

    /// What it would be given, in words: the piece, the map, the passage.
    private var knows: [String] {
        var items = [text.format("ask.knowsPiece", context.title)]
        if let map = context.map { items.append(map) }
        if !context.wholePiece, let selection = context.selection {
            items.append(text.format("ask.knowsSelection", selection))
        }
        return items
    }

    /// Three questions to ask here, as the web app offers them.
    private var suggestions: [String] {
        if !context.wholePiece, context.selection != nil {
            return [text["ask.suggest.harmony"], text["ask.suggest.practise"], text["ask.suggest.returns"]]
        }
        var asked: [String] = []
        if let back = context.form.first(where: { $0.label.count > 1 }),
           let first = context.form.first(where: { back.sharesWith.contains($0.id) }) {
            asked.append(text.format("ask.suggest.compare", back.label, first.label))
        }
        asked.append(text["ask.suggest.start"])
        asked.append(text["ask.suggest.climax"])
        if asked.count < 3 { asked.append(text["ask.suggest.form"]) }
        return asked
    }
}

/// The foot of the Insights stream (0018 section 7): the conversation the popovers add to, and the field that asks more.
struct AskStreamFoot: View {
    @ObservedObject var conversation: AskConversation
    @ObservedObject var account: Account
    let text: UIText
    @State private var draft = ""

    private var allowed: Bool { account.allows(.assistant) }

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["ask.title"]).overline()
            ForEach(Array(conversation.questions.enumerated()), id: \.offset) { _, question in
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    Text(question).dsType(.bodyStrong)
                    Text(text[allowed ? "ask.notYet" : "ask.notSent"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
            }
            if !allowed {
                (Text(text["ask.pro"]).foregroundStyle(Color.ds("plum")).fontWeight(.semibold) + Text(" " + text["ask.offer"]))
                    .dsType(.caption)
            }
            TextField(text["ask.placeholder"], text: $draft)
                .dsType(.body)
                .foregroundStyle(Color.ds("ink"))
                .submitLabel(.send)
                .onSubmit {
                    conversation.ask(draft)
                    draft = ""
                }
                .disabled(!allowed)
                .padding(.horizontal, CGFloat.ds("s-4"))
                .frame(minHeight: CGFloat.ds("h-touch"))
                .overlay(Capsule().strokeBorder(Color.ds("hairline-strong")))
        }
    }
}
