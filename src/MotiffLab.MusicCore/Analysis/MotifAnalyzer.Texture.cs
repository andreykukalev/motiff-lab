using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The texture of a selection (testing item 14): what the melody sounds over (broken chords, block chords, a bass line
/// or long bass notes, or nothing), whether the two parts take turns or sound together, and what that gives. Read from
/// the hands when every note has one, otherwise from the top line and the notes below it.
/// </summary>
public static partial class MotifAnalyzer
{
    private enum Accompaniment
    {
        None,
        BrokenChords,
        BlockChords,
        BassLine,
        LongNotes,
    }

    /// <summary>The texture fact, or null when the selection keeps only some of the notes (one hand, track or voice).</summary>
    internal static MotifFact? TextureFact(Note[] chosen, MotifSelection selection, Words w)
    {
        if (chosen.Length == 0 || selection.Filter.Hand is not null || selection.Filter.Track is not null || selection.Filter.Voice is not null)
        {
            return null;
        }

        MotifTexture texture = MotifTexture.Read(chosen);
        if (texture.IsSingleLine)
        {
            return new MotifFact("character.texture", FactLens.Character, FactCertainty.Detected,
                w.T("Texture: one line with no accompaniment. The harmony is heard only in the line itself.",
                    "Фактура: одна линия без сопровождения. Гармония слышна только в самой линии."),
                w.T("Detected: no two notes sound at once.", "Обнаружено: никакие две ноты не звучат одновременно."))
            {
                Brief = w.T("One line with no accompaniment.", "Одна линия без сопровождения."),
                Places = [FactPlace.Of(w.T("the line", "линия"), chosen)],
            };
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

        string upperText = inChords ? w.T("a melody in chords", "мелодия в аккордах") : w.T("a melody", "мелодия");
        string hand = byHands ? w.T(" in the left hand", " в левой руке") : "";
        string texture1 = kind switch
        {
            Accompaniment.BrokenChords => w.T($"{upperText} over broken chords{hand}", $"{upperText} над разложенными аккордами{hand}"),
            Accompaniment.BlockChords => w.T($"{upperText} over chords{hand}", $"{upperText} над аккордами{hand}"),
            Accompaniment.BassLine => w.T($"{upperText} over a bass line{hand}", $"{upperText} над басовой линией{hand}"),
            Accompaniment.LongNotes => w.T($"{upperText} over long bass notes{hand}", $"{upperText} над долгими нотами баса{hand}"),
            _ => w.T("a melody on top of its own chords, with no separate accompaniment", "мелодия поверх своих аккордов, без отдельного сопровождения"),
        };

        string interplay = "";
        if (kind != Accompaniment.None && lowerOnsets.Length > 0)
        {
            var upperAt = upperOnsets.Select(static onset => onset.Key).ToHashSet();
            int together = lowerOnsets.Count(onset => upperAt.Contains(onset.Key));
            interplay = together * 10 <= lowerOnsets.Length * 4
                ? w.T(" The two parts take turns: the accompaniment sounds in the melody's pauses.", " Голоса чередуются: сопровождение звучит в паузах мелодии.")
                : together * 10 >= lowerOnsets.Length * 7
                    ? w.T(" The two parts sound together, onset with onset.", " Голоса звучат вместе, атака к атаке.")
                    : "";
        }

        string gives = kind switch
        {
            Accompaniment.BrokenChords => w.T(" The broken chords keep the harmony moving under the melody.", " Разложенные аккорды держат гармонию в движении под мелодией."),
            Accompaniment.BlockChords => w.T(" The chords mark the beats and the harmony at once.", " Аккорды отмечают сразу и доли, и гармонию."),
            Accompaniment.BassLine => w.T(" A second line answers the melody from below.", " Вторая линия отвечает мелодии снизу."),
            Accompaniment.LongNotes => w.T(" The long bass notes hold the harmony while the melody moves.", " Долгие ноты баса держат гармонию, пока движется мелодия."),
            _ => "",
        };

        string basis = byHands
            ? w.T(
                "Inferred from the hands: the right hand's notes are the melody, the left hand's the accompaniment, read as chords when half its onsets strike two notes or more, as broken chords when its single notes mostly leap, as taking turns when at most 4 in 10 of its onsets fall with the melody's, as long notes when they last a half note or more, otherwise as a bass line.",
                "Предположено по рукам: ноты правой руки — мелодия, левой — сопровождение; оно читается как аккорды, если половина его атак берёт две ноты и больше, как разложенные аккорды, если его одиночные ноты в основном идут скачками, как чередование, если с атаками мелодии совпадают не больше 4 из 10 его атак, как долгие ноты, если они длятся половинную и дольше, иначе как басовая линия.")
            : w.T(
                "Inferred: the melody is the top line, the accompaniment the notes below it, read as chords when half its onsets strike two notes or more, as broken chords when its single notes mostly leap, as taking turns when at most 4 in 10 of its onsets fall with the melody's, as long notes when they last a half note or more, otherwise as a bass line.",
                "Предположено: мелодия — верхняя линия, сопровождение — ноты под ней; оно читается как аккорды, если половина его атак берёт две ноты и больше, как разложенные аккорды, если его одиночные ноты в основном идут скачками, как чередование, если с атаками мелодии совпадают не больше 4 из 10 его атак, как долгие ноты, если они длятся половинную и дольше, иначе как басовая линия.");
        List<FactPlace> places = [FactPlace.Of(w.T("the melody", "мелодия"), upper)];
        if (lower.Length > 0)
        {
            places.Add(FactPlace.Of(w.T("the accompaniment", "сопровождение"), lower));
        }

        string sentence = char.ToUpperInvariant(texture1[0]) + texture1[1..];
        return new MotifFact("character.texture", FactLens.Character, FactCertainty.Inferred,
            w.T($"Texture: {texture1}.{interplay}{gives}", $"Фактура: {texture1}.{interplay}{gives}"), basis)
        {
            Brief = $"{sentence}.{gives}",
            Places = places,
        };
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