using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Score;

/// <summary>
/// The focused phrase as the card engraves it (decision record 0018, section 4): an excerpt music-core wrote,
/// where it starts in the piece, and the ossias above it. A new instance is engraved again.
/// </summary>
/// <param name="Mei">The excerpt as MEI, from <see cref="NotationGenerator.Generate"/> with its bars and ossias.</param>
/// <param name="Offset">Where its first bar starts in the piece: the card's timemap starts at 0.</param>
/// <param name="Ossias">The ossias above the original, top first.</param>
public sealed record PhraseCardSource(string Mei, Tick Offset, IReadOnlyList<CardOssia> Ossias)
{
    /// <summary>The card for an excerpt; <paramref name="label"/> gives each ossia's overline.</summary>
    public static PhraseCardSource From(GeneratedNotation notation, Tick offset, Func<GeneratedOssia, string> label) =>
        new(notation.Mei, offset, [.. notation.Ossias.Select(ossia => new CardOssia([.. ossia.Staves.Select(staff => staff.Number)], ossia.IdPrefix, label(ossia)))]);
}

/// <summary>An ossia on the card: its staves in the document, what its notes' ids start with, and the overline that names it.</summary>
public sealed record CardOssia(IReadOnlyList<int> Staves, string Prefix, string Label);