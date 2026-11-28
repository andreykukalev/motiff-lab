using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>What a boundary between pieces, movements or large sections was found by (<see cref="SectionFinder"/>).</summary>
internal enum SectionCueKind
{
    /// <summary>The start of the piece or the recording: the first section's own start.</summary>
    Opening,

    /// <summary>A final bar line (thin and thick, no repeat) before the end of the piece.</summary>
    FinalBarline,

    /// <summary>A double bar line without a repeat.</summary>
    DoubleBarline,

    /// <summary>A heading written over the music, such as "Trio", "II." or a tempo word.</summary>
    Heading,

    /// <summary>A rehearsal mark, such as "B".</summary>
    Rehearsal,

    /// <summary>A new key signature.</summary>
    KeyChange,

    /// <summary>A new time signature.</summary>
    MeterChange,

    /// <summary>A new tempo that holds for a while.</summary>
    TempoChange,

    /// <summary>Whole bars without a sounding note before the boundary, in notation.</summary>
    Rest,

    /// <summary>Seconds without a sounding note before the boundary, in a recording.</summary>
    Silence,

    /// <summary>The music heard before and after a silence fits different keys.</summary>
    HeardKey,

    /// <summary>The playing before and after a silence keeps different tempos.</summary>
    HeardTempo,

    /// <summary>A silence in a recording with the same key and tempo on both sides: proposed only to make up the number of pieces.</summary>
    SameMusic,
}

/// <summary>
/// One fact a found boundary rests on, in both languages, with how much it counts towards the boundary's confidence.
/// Its text states what the score or the recording shows ("double bar line", "key changes to D minor"), nothing more.
/// </summary>
/// <param name="Weight">From 0 to 1: how strongly this fact alone says a new section starts.</param>
/// <param name="Label">For a heading, the heading as written; null for any other cue.</param>
internal sealed record SectionCue(SectionCueKind Kind, double Weight, string English, string Russian, string? Label = null)
{
    /// <summary>For a silence, how long it lasts in microseconds; null for any other cue.</summary>
    public long? Microseconds { get; init; }

    public string In(Words words) => words.T(English, Russian);
}

/// <summary>
/// A piece proposed for the pieces layer (version 0.5; Andrei, 2026-11-11, "automatic bookmarks"): a piece, a movement or
/// a large section of a long score or recording, from the bar its boundary was found at to the next one. It is an
/// inference, never a fact of the file (<see cref="Attribution"/>), and it is not saved: the player's pieces are saved as
/// the pieces layer.
/// </summary>
/// <param name="Number">The piece's place among the proposals, from 1.</param>
/// <param name="Cues">What its boundary was found by, strongest first; the first piece has the opening cue.</param>
internal sealed record FoundSection(int Number, TickSpan Span, int FirstBar, int LastBar, double Confidence, IReadOnlyList<SectionCue> Cues)
{
    /// <summary>Whether its boundary is only a silence with the same key and tempo on both sides (<see cref="SectionFinder.FindPieces"/>).</summary>
    public bool IsOnlySilence => Cues.Any(cue => cue.Kind == SectionCueKind.SameMusic);

    /// <summary>The heading written where it starts ("Trio", "Andante"), if any.</summary>
    public string? Heading => Cues.Select(cue => cue.Label).FirstOrDefault(label => label is not null);

    /// <summary>Inferred, at the confidence of its boundary.</summary>
    public Attribution Attribution => Attribution.Inferred(Confidence);

    /// <summary>Its title: the heading, else "Piece 2"; "Пьеса 2".</summary>
    public string PieceTitleIn(Words words) => Heading ?? words.T($"Piece {Number}", $"Пьеса {Number}");

    /// <summary>Why it was proposed, facts only: "double bar line, key changes to D minor".</summary>
    public string ReasonIn(Words words) => string.Join(", ", Cues.Select(cue => cue.In(words)));

    /// <summary>"likely", "possible", "uncertain"; "вероятно", "возможно", "не уверено".</summary>
    public string ConfidenceIn(Words words) => Confidence switch
    {
        >= 0.7 => words.T("likely", "вероятно"),
        >= 0.5 => words.T("possible", "возможно"),
        _ => words.T("uncertain", "не уверено"),
    };
}

/// <summary>The pieces proposed for a score or recording (<see cref="SectionFinder.FindPieces"/>); nothing here is saved.</summary>
internal sealed class FoundSections(IReadOnlyList<FoundSection> items)
{
    public static FoundSections None { get; } = new([]);

    /// <summary>The pieces in score order, numbered from 1; at least one, from the start, unless there are no notes.</summary>
    public IReadOnlyList<FoundSection> Items { get; } = items;
}