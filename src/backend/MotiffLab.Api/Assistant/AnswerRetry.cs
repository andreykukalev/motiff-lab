using System.Globalization;

using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Api.Assistant;

/// <summary>
/// The one retry of an answer the check refused (the assistant's option A, item 3): when the model's answer cites no fact,
/// cites one the turn did not give or is too long, the relay tells the model why and asks it once to rewrite the answer,
/// in the same turn and within the same time; a second failure gives the engine's template. An empty answer (a refusal,
/// an answer cut off) is not retried, nor a timeout or a provider's error. The device never sees the refused answer or the
/// note: they are added to the model's turn only.
/// </summary>
internal static class AnswerRetry
{
    /// <summary>
    /// The turn that asks the model to rewrite <paramref name="refused"/>: the same turn with the refused answer and the note
    /// after it, the context still beside the player's question; null when <paramref name="reason"/> is not one a rewrite can mend.
    /// </summary>
    public static ModelTurn? Turn(ModelTurn turn, string refused, string? reason, IReadOnlySet<string> known)
    {
        ArgumentNullException.ThrowIfNull(turn);
        return reason is not ("noCitation" or "unknownFact" or "tooLong") ? null : turn with { History = [.. turn.History, TurnEntry.Answered(refused), TurnEntry.Question(Note(reason, refused.Trim(), known))] };
    }

    /// <summary>What the model is told: why its answer was not shown, and how to rewrite it.</summary>
    internal static string Note(string reason, string refused, IReadOnlySet<string> known)
    {
        string why = reason switch
        {
            "noCitation" => "it cites no fact. Build it from the facts music-core gave for this question and cite at least one of them by its id in double square brackets, exactly as listed, such as [[id]]",
            "unknownFact" => $"it cites ids this question was not given: {string.Join(", ", AnswerCheck.Cited(refused).Where(id => !known.Contains(id)))}. Cite only ids the context of this question or a tool result lists, exactly as listed",
            _ => string.Create(CultureInfo.InvariantCulture, $"it is {refused.Length} characters long, more than {AnswerCheck.MaxLength}. Say it more briefly and keep its citations"),
        };
        return $"(From Motiff Lab, not the player.) Your answer was not shown to the player: {why}. Rewrite it once, in the language of the player's question, at most {AnswerCheck.MaxLength} characters, as plain text. If this answer fails the check too, the player sees the engine's facts instead.";
    }
}