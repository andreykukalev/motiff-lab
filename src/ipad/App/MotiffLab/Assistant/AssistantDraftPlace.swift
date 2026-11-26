import Foundation

/// The place of a draft a question is about (v1.2, ADR 0034 section 6, the plan's answer 7; the drafts spec «Ассистент»,
/// «Порядок работы» step 8): the draft and the place as music-core names them, and the place's label («① Ноты 1–4»). A
/// turn about it sends music-core's `drafts.context` (the notes of the place's base and of the variant it is built from, at
/// most 32 bars each, its meter and key, its приёмы and seams) and runs the model's tools by `drafts.call`, in the role the
/// conversation there takes (`roles`), only after the draft's own consent (`AssistantController.draftConsentGiven`).
struct AssistantDraftPlace: Equatable, Sendable {
    /// The place assembler's role, the one a place opens in: it reads the comparison and proposes the player's own takes.
    static let role = "placeAssembler"
    /// The explainer's role (v1.4, F5): it reads the приёмы, where the variants came from and the seams, and answers citing
    /// them; it proposes nothing.
    static let explainer = "explainer"
    /// The bridge chooser's role in a bridge place (v1.4, F5): it reads the candidates and proposes one as the base.
    static let seamChooser = "seamChooser"

    let draft: String
    let place: String
    let label: String
    /// The variants a question there reads, the base and the one built from (v1.4): another base, or that variant moved
    /// out, is another place to the assistant, so no conversation or proposal of the place as it was stays. Empty in a
    /// bridge place, whose base the chooser's own step changes.
    var basis: [String] = []

    /// The place in a request of the draft's calls.
    var fields: [String: JSONValue] { ["draft": .string(draft), "place": .string(place)] }

    /// The roles a conversation in a place may take, the first the one it opens in: in a bridge place the bridge chooser
    /// alone, in any other the assembler and the explainer.
    static func roles(bridge: Bool) -> [String] {
        bridge ? [seamChooser] : [role, explainer]
    }
}

/// The assistant in a draft's place (v1.2 step 8; a conversation since v1.4, the plan's answer 8): the place it was opened
/// on and its conversation there, the assistant panel's own model (`AssistantChat`) in one role, so each question goes with
/// the ones before it. Never kept on the iPad: it goes when the assistant closes there, opens on another place, or the
/// draft's screen goes.
struct AssistantInDraft: Equatable {
    var place: AssistantDraftPlace
    var chat: AssistantChat
    /// The question on its way, shown under the conversation until its answer joins it.
    var asking: String?

    /// The last question and what came of it.
    var exchange: AssistantExchange? { chat.exchanges.last }
}

/// A step the assistant proposes in a draft's place (music-core's DraftProposal, the one entry of a draft tool's "drafts"):
/// what it would take, in the engine's words, and the call that made it. Nothing is applied: «▶ После» opens the draft as
/// it would leave it (`drafts.proposal`), and only the player's «Принять» has music-core make the same call again on the
/// draft as it is then and keep what it proposes (`drafts.accept`), undone as any step of the draft; «Отказаться» only
/// says so. The model chose the call; the notes are music-core's.
struct AssistantPlaceProposal: Codable, Equatable, Sendable, Identifiable {
    var id: String
    var tool: String
    var parameters: JSONValue
    /// The role of the turn that asked for it, which music-core checks the call against again.
    var role: String
    var draft: String
    var place: String
    var title: String?
    /// What it takes, written by the engine.
    var explanation: String
    /// It changes the draft's places (a bridge put before the place), not only the place's steps.
    var structure: Bool
    var state = AssistantDraftOffer.Decision.proposed
    /// Why «Принять» did not take it, in music-core's words (empty when it gave none); nil otherwise.
    var problem: String?

    /// The steps the question's tool calls proposed in a draft's place, in the order the calls came; a draft of the notes
    /// (`AssistantDraftOffer`) is none of them.
    static func offers(in chat: AssistantChat) -> [AssistantPlaceProposal] {
        chat.proposedInQuestion.compactMap { proposed in
            let entry = proposed.draft
            guard let draft = entry["draft"]?.string, let place = entry["place"]?.string else { return nil }
            return AssistantPlaceProposal(
                id: UUID().uuidString, tool: proposed.call.name, parameters: proposed.call.arguments, role: chat.role, draft: draft,
                place: place, title: entry["title"]?.string, explanation: entry["explanation"]?.string ?? "",
                structure: entry["structure"]?.isTrue ?? false
            )
        }
    }

    /// The fields of `drafts.proposal` and `drafts.accept` that make its call again: the draft, the place, the tool, its
    /// parameters and the role.
    var callFields: [String: Any] {
        ["draft": draft, "place": place, "tool": tool, "parameters": parameters.foundation, "role": role]
    }

    /// Its words in a toast: its title, else what it takes.
    var words: String { title ?? explanation }

    /// Whether its call, made again on the draft as it is now (`drafts.call` on `piece`, the draft's piece or its view),
    /// still proposes what this card says (its title and what it takes): not once the draft changed under it (the приём
    /// taken, the variants aligned anew, another base), so «Принять» never keeps a step the card does not describe.
    func proposedAgain(on piece: Int32) async -> Bool {
        let call = AssistantToolCall(id: id, name: tool, arguments: parameters)
        let answer = await AssistantController.run(call, role: role, place: AssistantDraftPlace(draft: draft, place: place, label: ""), piece: piece)
        guard let now = answer["drafts"]?.items.first else { return false }
        return now["title"]?.string == title && (now["explanation"]?.string ?? "") == explanation
    }
}
