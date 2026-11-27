using System.Globalization;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A phrase as an object of the project (0026 C1, #318): minted when something first attaches to the phrase (an idea, a
/// resume point open in phrase focus), its id made from the level and the bounds at that moment, so two devices working
/// offline mint the same id for the same phrase; the id never changes when <paramref name="Span"/> later moves.
/// </summary>
/// <param name="Id">"ph_" and 32 hex digits (<see cref="Mint"/>).</param>
/// <param name="Span">The phrase it stands for: the span it was minted on, then where the player's boundary edits carried it.</param>
public sealed record PhraseAnchor(string Id, TickSpan Span)
{
    /// <summary>
    /// The anchor of the phrase <paramref name="phrase"/>, its id <see cref="IdSource.Hashed"/> from "phrase|{onset}|{end}"
    /// in ticks, passing by the ids <paramref name="taken"/> (an anchor minted on these bounds that moved since).
    /// </summary>
    internal static PhraseAnchor Mint(TickSpan phrase, IEnumerable<string> taken) =>
        new(IdSource.Hashed("ph_", string.Create(CultureInfo.InvariantCulture, $"phrase|{phrase.Onset.Value}|{phrase.End.Value}"), taken), phrase);
}