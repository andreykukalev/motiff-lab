using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The texture of a selection (testing item 14): what the melody sounds over (broken chords, block chords, a bass line
/// or long bass notes, or nothing) and whether the two parts take turns or sound together, read from the hands when every
/// note has one, otherwise from the top line and the notes below it. Its sentence is in <c>MotifAnalyzer.Character.Facts.cs</c>.
/// </summary>
internal static partial class MotifAnalyzer
{
    private enum Accompaniment
    {
        None,
        BrokenChords,
        BlockChords,
        BassLine,
        LongNotes,
    }

    /// <summary>Whether the accompaniment takes turns with the melody, sounds with it onset for onset, or neither clearly.</summary>
    private enum Interplay
    {
        Neither,
        TakeTurns,
        Together,
    }

    /// <summary>
    /// The texture as read: one line, or a melody (<paramref name="Upper"/>, the right hand's notes when <paramref name="ByHands"/>,
    /// else the top line) and the notes under it, whether the melody is in chords, what the accompaniment does and how
    /// it sounds against the melody. For one line, <paramref name="Upper"/> is every note.
    /// </summary>
    private sealed record TextureMeasure(bool SingleLine, bool ByHands, Note[] Upper, Note[] Lower, bool InChords, Accompaniment Kind, Interplay Interplay);

    /// <summary>The texture, or null when the selection keeps only some of the notes (one hand, track or voice).</summary>
    private static TextureMeasure? MeasureTexture(Note[] chosen, MotifSelection selection)
    {
        if (chosen.Length == 0 || selection.Filter.Hand is not null || selection.Filter.Track is not null || selection.Filter.Voice is not null)
        {
            return null;
        }

        MotifTexture texture = MotifTexture.Read(chosen);
        if (texture.IsSingleLine)
        {
            return new TextureMeasure(true, false, chosen, [], false, Accompaniment.None, Interplay.Neither);
        }

        bool byHands = chosen.All(static note => note.Hand is not null)
            && chosen.Any(static note => note.Hand!.Value == Hand.Right)
            && chosen.Any(static note => note.Hand!.Value == Hand.Left);
        Note[] upper;
        Note[] lower;
        if (byHands)
        {
            upper = [.. chosen.Where(static note => note.Hand!.Value == Hand.Right)];
            lower = [.. chosen.Where(static note => note.Hand!.Value == Hand.Left)];
        }
        else
        {
            var melody = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
            upper = [.. chosen.Where(note => melody.Contains(note.Id))];
            lower = [.. chosen.Where(note => !melody.Contains(note.Id))];
        }

        IGrouping<long, Note>[] upperOnsets = [.. upper.GroupBy(static note => note.Span.Onset.Value)];
        IGrouping<long, Note>[] lowerOnsets = [.. lower.GroupBy(static note => note.Span.Onset.Value)];
        bool inChords = byHands && upperOnsets.Length > 0 && upperOnsets.Count(static onset => onset.Count() >= 2) * 10 >= upperOnsets.Length * 3;
        Accompaniment kind = KindOf(lowerOnsets, upper);
        Interplay interplay = Interplay.Neither;
        if (kind != Accompaniment.None && lowerOnsets.Length > 0)
        {
            var upperAt = upperOnsets.Select(static onset => onset.Key).ToHashSet();
            int together = lowerOnsets.Count(onset => upperAt.Contains(onset.Key));
            interplay = together * 10 <= lowerOnsets.Length * 4 ? Interplay.TakeTurns
                : together * 10 >= lowerOnsets.Length * 7 ? Interplay.Together
                : Interplay.Neither;
        }

        return new TextureMeasure(false, byHands, upper, lower, inChords, kind, interplay);
    }

    /// <summary>What the notes under the melody do, from their onsets: chords, broken chords, long notes or a bass line.</summary>
    private static Accompaniment KindOf(IGrouping<long, Note>[] onsets, Note[] upper)
    {
        if (onsets.Length == 0)
        {
            return Accompaniment.None;
        }

        if (onsets.Count(static onset => onset.Count() >= 2) * 2 >= onsets.Length)
        {
            return Accompaniment.BlockChords;
        }

        Note[] line = [.. onsets.Select(static onset => onset.MinBy(static note => note.Pitch.MidiNumber)!)];
        int[] moves = [.. line.Zip(line.Skip(1), static (a, b) => Math.Abs(b.Pitch.MidiNumber - a.Pitch.MidiNumber))];
        if (moves.Length >= 2 && moves.Count(static move => move > 2) * 10 >= moves.Length * 6
            && line.Select(static note => note.Pitch.PitchClass).Distinct().Count() >= 2)
        {
            return Accompaniment.BrokenChords;
        }

        long[] lengths = [.. line.Select(static note => note.Span.Duration).Order()];
        long[] upperLengths = [.. upper.Select(static note => note.Span.Duration).Order()];
        long median = lengths[lengths.Length / 2];
        long upperMedian = upperLengths.Length == 0 ? 0 : upperLengths[upperLengths.Length / 2];
        return median >= 960 && median >= upperMedian * 2 ? Accompaniment.LongNotes : Accompaniment.BassLine;
    }
}