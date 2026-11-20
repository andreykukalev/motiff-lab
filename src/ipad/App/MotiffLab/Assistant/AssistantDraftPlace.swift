import Foundation

/// The place of a draft a question is about (v1.2, ADR 0034 section 6, the plan's answer 7; the drafts spec «Ассистент»,
/// «Порядок работы» step 8): the draft and the place as music-core names them, and the place's label («① Ноты 1–4»). A
/// turn about it sends music-core's `drafts.context` (the notes of the place's base and of the variant it is built from, at
/// most 32 bars each, its meter and key, its приёмы and seams) and runs the model's tools by `drafts.call`, in the role of
/// the place assembler, only after the draft's own consent (`AssistantController.draftConsentGiven`).
struct AssistantDraftPlace: Equatable, Sendable {
    /// The role a question in a draft's place takes: it reads the comparison and proposes the player's own takes.
    static let role = "placeAssembler"

    let draft: String
    let place: String
    let label: String

    /// The place in a request of the draft's calls.
    var fields: [String: JSONValue] { ["draft": .string(draft), "place": .string(place)] }
}

/// The assistant in a draft's place (v1.2 step 8): the place it was opened on, its one question and answer, never kept,
/// with the steps the answer's tools proposed.
struct AssistantInDraft: Equatable {
    var place: AssistantDraftPlace
    var exchange: AssistantExchange?
    /// The question on its way, shown until its answer replaces the last one.
    var asking: String?
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
}
