import SwiftUI

/// A draft the assistant's tool made for a question (#582 item 17.3, «Помощник правит ноты»; music-core's AssistantDraft as
/// a tool result writes it): what changes, in the engine's words, and the call that made it. Nothing is applied: Accept
/// has music-core make it again from that call over the notes as they are now (`corrections.acceptDraft`), which adds its
/// edits as ordinary corrections, so a draft of notes that have since changed is refused rather than applied.
struct AssistantDraftOffer: Codable, Equatable, Sendable, Identifiable {
    /// The player's decision on it.
    enum Decision: String, Codable, Sendable {
        case proposed, accepted, cancelled
    }

    /// A note's values as the draft names them: its pitch, bar, place in the bar and length.
    struct Values: Codable, Equatable, Sendable {
        var pitch: String
        var bar: Int
        var at: String
        var length: String
    }

    /// One note the draft changes, removes or adds, by its address in the bar.
    struct Edit: Codable, Equatable, Sendable {
        var kind: String
        var address: String?
        var before: Values?
        var after: Values?
    }

    var id: String
    /// The call that made it: the tool, its arguments and the draft's place among the call's drafts.
    var tool: String
    var parameters: JSONValue
    var index: Int
    var title: String?
    /// What changes, written by the engine from the diff.
    var explanation: String
    var edits: [Edit]
    /// The operation's rules it raises without being blocked, in the engine's words.
    var flags: [String]
    var state = Decision.proposed
    /// Why Accept did not take it, in music-core's words (empty when it gave none); nil otherwise.
    var problem: String?

    /// The first and last bars it touches.
    var firstBar: Int? { bars.min() }
    var lastBar: Int? { bars.max() }

    private var bars: [Int] { edits.flatMap { [$0.before?.bar, $0.after?.bar].compactMap { $0 } } }

    /// The drafts the question's tool calls made, in the order the calls came, each with the call it was made by, as
    /// music-core's AssistantSession.Resulted keeps them; a draft that changes nothing is left out.
    static func offers(in chat: AssistantChat) -> [AssistantDraftOffer] {
        let calls = chat.callsInQuestion
        return chat.resultsInQuestion.flatMap { result -> [AssistantDraftOffer] in
            guard let call = calls.first(where: { $0.id == result.id }) else { return [] }
            return result.content["drafts"]?.items.enumerated().compactMap { index, draft in offer(draft, call: call, index: index) } ?? []
        }
    }

    static func offer(_ draft: JSONValue, call: AssistantToolCall, index: Int) -> AssistantDraftOffer? {
        let edits = draft["edits"]?.items.compactMap { edit -> Edit? in
            guard let kind = edit["kind"]?.string else { return nil }
            return Edit(kind: kind, address: edit["address"]?.string, before: values(edit["before"]), after: values(edit["after"]))
        } ?? []
        guard !edits.isEmpty else { return nil }
        return AssistantDraftOffer(
            id: UUID().uuidString, tool: call.name, parameters: call.arguments, index: index, title: draft["title"]?.string,
            explanation: draft["explanation"]?.string ?? "", edits: edits, flags: draft["flags"]?.items.compactMap(\.string) ?? []
        )
    }

    private static func values(_ json: JSONValue?) -> Values? {
        guard let json, let pitch = json["pitch"]?.string, let bar = json["bar"]?.int else { return nil }
        return Values(pitch: pitch, bar: bar, at: json["at"]?.string ?? "", length: json["length"]?.string ?? "")
    }

    /// The request that accepts it (`corrections.acceptDraft`): the bar whose notes the answer lists, and the call.
    var acceptRequest: JSONValue? {
        guard let bar = firstBar else { return nil }
        return .object(["bar": .int(bar), "tool": .string(tool), "parameters": parameters, "index": .int(index)])
    }

    /// One edit in words: "Remove 3.R.2 (E5)", "3.R.1: E5 · 1/4 · 1/8 → F5 · 1/4 · 1/8", "Add G4 in bar 3".
    static func words(_ edit: Edit, _ text: UIText) -> String {
        let note = [edit.address, (edit.before ?? edit.after)?.pitch].compactMap { $0 }.joined(separator: " · ")
        switch edit.kind {
        case "remove":
            return text.format("padAssistant.editRemove", note)
        case "add":
            return text.format("padAssistant.editAdd", edit.after?.pitch ?? "", String(edit.after?.bar ?? 0))
        default:
            return text.format("padAssistant.editChange", edit.address ?? "", describe(edit.before), describe(edit.after))
        }
    }

    private static func describe(_ values: Values?) -> String {
        guard let values else { return "" }
        return [values.pitch, values.at, values.length].filter { !$0.isEmpty }.joined(separator: " · ")
    }
}

/// A draft of the assistant as the iPad shows it (#582 item 17.3): framed in plum, what the app composed, with the bars it
/// covers, what changes in the engine's words, the notes it changes, and Accept and Cancel; once decided, what became of
/// it. The draft is not engraved over the bars or heard before Accept here: the facade has no call for that yet.
struct AssistantDraftCard: View {
    let draft: AssistantDraftOffer
    let text: UIText
    var busy = false
    let accept: () -> Void
    let cancel: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("assistantDraft.overline", bars)).overline()
            if let title = draft.title {
                Text(title).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
            }
            if !draft.explanation.isEmpty {
                Text(draft.explanation).dsType(.body).foregroundStyle(Color.ds("ink")).fixedSize(horizontal: false, vertical: true)
            }
            ForEach(Array(draft.edits.enumerated()), id: \.offset) { _, edit in
                Text(AssistantDraftOffer.words(edit, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            ForEach(draft.flags, id: \.self) { flag in
                Text(flag).dsType(.caption).foregroundStyle(Color.ds("warning"))
            }
            Text(text["assistantDraft.byEngine"]).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
            if let problem = draft.problem {
                Text(problem.isEmpty ? text["assistantDraft.stale"] : problem).dsType(.caption).foregroundStyle(Color.ds("danger"))
            }
            decision
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("plum-tint"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("plum")))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("assistantDraft.cardLabel", bars))
    }

    @ViewBuilder private var decision: some View {
        switch draft.state {
        case .proposed:
            HStack(spacing: CGFloat.ds("s-2")) {
                Spacer(minLength: 0)
                Button(text["assistantDraft.cancel"], action: cancel)
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                Button(text["repair.accept"], action: accept)
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    .disabled(busy)
            }
        case .accepted:
            Text(text["padAssistant.accepted"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        case .cancelled:
            Text(text["padAssistant.cancelled"]).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
        }
    }

    private var bars: String {
        guard let first = draft.firstBar, let last = draft.lastBar else { return "" }
        return barsText(first, last, text)
    }
}
