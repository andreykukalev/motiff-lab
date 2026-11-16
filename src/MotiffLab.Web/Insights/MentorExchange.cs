using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Web.Insights;

/// <summary>
/// A question to the mentor and what came of it, as the conversation shows it: the checked answer cut into text and
/// citations with the facts it cites, or, when nothing was answered, the key of the line that says why (and then nothing
/// was sent, or the turn was dropped).
/// </summary>
/// <param name="Fallback">True when the answer is the engine's facts because the model's words failed the check.</param>
/// <param name="Note">The UI text key of why there is no answer; null with an answer.</param>
public sealed record MentorExchange(string Question, IReadOnlyList<AnswerPart> Parts, bool Fallback, IReadOnlyList<CitedFact> Cited, string? Note)
{
    public static MentorExchange Unanswered(string question, string note) => new(question, [], false, [], note);
}