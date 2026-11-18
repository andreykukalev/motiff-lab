using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Assistant.Repairs;

/// <summary>The repairs of single notes: out of range, doubled at a seam, off the beat grid, crowded.</summary>
public static partial class CheckRepairs
{
    /// <summary>Two notes of one pitch starting closer than this at a seam are one note heard twice: a 16th.</summary>
    public const long SeamWindow = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>A note beyond the piano moved by octaves to the nearest key in range, or removed.</summary>
    private static (IReadOnlyList<RepairCandidate>, string?) OutOfRange(RepairContext context)
    {
        Words w = context.Piece.Words;
        Note[] beyond = [.. context.Notes.Where(static note => note.Pitch.MidiNumber is < OperationRules.LowestPitch or > OperationRules.HighestPitch)];
        if (beyond.Length == 0)
        {
            return ([], w.T("Every note of the bar is now within the piano's range.", "Все ноты такта теперь в диапазоне фортепиано."));
        }

        string pitches = string.Join(" ", beyond.Select(static note => note.Pitch));
        RepairCandidate?[] candidates =
        [
            Candidate(context, "octave", w.T($"Move {pitches} by octaves into the piano's range", $"Перенести {pitches} на октаву в диапазон фортепиано"), context.Notes, context.Notes.Select(note => beyond.Contains(note) ? note with { Pitch = new Pitch(InRange(note.Pitch.MidiNumber)), Attribution = Attribution.UserEdited } : note)),
            Candidate(context, "remove", w.T($"Remove {pitches}", $"Убрать {pitches}"), context.Notes, context.Notes.Where(note => !beyond.Contains(note))),
        ];
        return ([.. candidates.OfType<RepairCandidate>()], null);
    }

    private static int InRange(int pitch)
    {
        while (pitch < OperationRules.LowestPitch)
        {
            pitch += 12;
        }

        while (pitch > OperationRules.HighestPitch)
        {
            pitch -= 12;
        }

        return pitch;
    }

    /// <summary>
    /// A seam of a long recording (0028 section 6): a note heard twice across the seam (one pitch, starting less than a
    /// 16th apart or overlapping in one hand) merged into one, held to the later end. A note lost at the seam cannot be told.
    /// </summary>
    private static (IReadOnlyList<RepairCandidate>, string?) Seam(RepairContext context)
    {
        Words w = context.Piece.Words;
        var candidates = new List<RepairCandidate>();
        foreach (Note[] line in context.Lines)
        {
            for (int i = 0; i < line.Length; i++)
            {
                Note first = line[i];
                if (line.Skip(i + 1).FirstOrDefault(other => other.Pitch == first.Pitch && (other.Span.Onset.Value - first.Span.Onset.Value < SeamWindow || first.Span.Overlaps(other.Span))) is not { } second)
                {
                    continue;
                }

                long end = Math.Max(first.Span.End.Value, second.Span.End.Value);
                IEnumerable<Note> after = context.Notes.Where(note => note.Id != second.Id).Select(note => note.Id == first.Id ? Lasting(note, end - note.Span.Onset.Value) : note);
                string title = w.T($"Merge the doubled {first.Pitch} ({context.Address(first)}, {context.Address(second)})", $"Слить удвоенную {first.Pitch} ({context.Address(first)}, {context.Address(second)})");
                if (Candidate(context, $"merge.{context.Address(first)}", title, context.Notes, after) is { } candidate)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return (candidates, w.T(
            "No note is heard twice at the seam; a note lost there cannot be told from the notes. Listen to the recording here.",
            "На стыке нет удвоенной ноты; потерянную ноту по нотам не найти. Послушайте запись в этом месте."));
    }

    /// <summary>
    /// The tempo fitting the bar poorly: the bar's notes snapped to the 16th grid, or to the triplet-8th grid, from the
    /// bar's start; each note keeps at least one step. A grid that would put two notes of one pitch together is skipped.
    /// </summary>
    private static (IReadOnlyList<RepairCandidate>, string?) Snapped(RepairContext context)
    {
        Words w = context.Piece.Words;
        (string Id, long Step, string Title)[] grids =
        [
            ("sixteenths", CanonicalTime.TicksPerQuarter / 4, w.T("Snap the bar to 16ths", "Выровнять такт по шестнадцатым")),
            ("triplets", CanonicalTime.TicksPerQuarter / 3, w.T("Snap the bar to triplet 8ths", "Выровнять такт по триольным восьмым")),
        ];
        long origin = context.Bar.IsPickup ? context.Bar.Span.End.Value - context.Bar.Signature.BarTicks : context.Bar.Span.Onset.Value;
        var candidates = new List<RepairCandidate>();
        foreach ((string id, long step, string title) in grids)
        {
            Note[] after = [.. context.Notes.Select(note => Snap(note, origin, step))];
            bool collides = after.GroupBy(static note => (note.Pitch, note.Span.Onset)).Any(static same => same.Count() > 1);
            if (!collides && Candidate(context, id, title, context.Notes, after) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        return (candidates, w.T("The bar's notes already sit on the 16th and triplet grids.", "Ноты такта уже стоят на сетке шестнадцатых и триолей."));
    }

    private static Note Snap(Note note, long origin, long step)
    {
        long Near(long tick)
        {
            long offset = tick - origin;
            long steps = offset >= 0 ? (offset + (step / 2)) / step : -((-offset + (step / 2)) / step);
            return origin + (steps * step);
        }

        long onset = Near(note.Span.Onset.Value), end = Math.Max(onset + step, Near(note.Span.End.Value));
        return onset == note.Span.Onset.Value && end == note.Span.End.Value
            ? note
            : note with { Span = TickSpan.FromBounds(new Tick(Math.Max(0, onset)), new Tick(end)), Attribution = Attribution.UserEdited };
    }

    /// <summary>
    /// Crowded notes (0028 section 6), where a transcription often hears extra ones: the quietest of the crowded notes
    /// removed, one candidate each, quietest first. When they are all as loud, nothing tells which is extra.
    /// </summary>
    private static (IReadOnlyList<RepairCandidate>, string?) Crowded(RepairContext context)
    {
        Words w = context.Piece.Words;
        Note[] crowded = [.. Crowd(context.Notes)];
        if (crowded.Length == 0 || crowded.Select(static note => note.Velocity).Distinct().Count() < 2)
        {
            return ([], w.T(
                "The crowded notes are all as loud, so nothing in the notes tells which one is extra. Listen to the recording here.",
                "Ноты скопления звучат одинаково громко, поэтому по нотам не понять, какая лишняя. Послушайте запись в этом месте."));
        }

        int loudest = crowded.Max(static note => note.Velocity);
        IEnumerable<Note> quietest = crowded
            .Where(note => note.Velocity < loudest)
            .OrderBy(static note => note.Velocity).ThenBy(static note => note.Span.Duration).ThenBy(static note => note.Span.Onset).ThenBy(static note => note.Id, StringComparer.Ordinal);
        RepairCandidate?[] candidates = [.. quietest.Take(MostCandidates).Select(note => Candidate(
            context,
            $"quiet.{context.Address(note)}",
            w.T($"Remove the quiet {note.Pitch} ({context.Address(note)})", $"Убрать тихую {note.Pitch} ({context.Address(note)})"),
            context.Notes,
            context.Notes.Where(other => other.Id != note.Id)))];
        return ([.. candidates.OfType<RepairCandidate>()], null);
    }

    /// <summary>The crowded notes, as the flag finds them (<see cref="CheckFlag.ClusterOnsets"/> onsets within a 16th, or <see cref="CheckFlag.ClusterSeconds"/> seconds struck together).</summary>
    private static IEnumerable<Note> Crowd(IReadOnlyList<Note> notes)
    {
        long sixteenth = CanonicalTime.TicksPerQuarter / 4;
        Tick[] onsets = [.. notes.Select(static note => note.Span.Onset).Distinct().Order()];
        var crowded = new HashSet<Note>();
        for (int i = 0; i + CheckFlag.ClusterOnsets - 1 < onsets.Length; i++)
        {
            if (onsets[i + CheckFlag.ClusterOnsets - 1].Value - onsets[i].Value < sixteenth)
            {
                Tick from = onsets[i], to = onsets[i + CheckFlag.ClusterOnsets - 1];
                crowded.UnionWith(notes.Where(note => note.Span.Onset >= from && note.Span.Onset <= to));
            }
        }

        foreach (IGrouping<Tick, Note> chord in notes.GroupBy(static note => note.Span.Onset))
        {
            Note[] byPitch = [.. chord.OrderBy(static note => note.Pitch.MidiNumber)];
            for (int i = 0; i + CheckFlag.ClusterSeconds - 1 < byPitch.Length; i++)
            {
                Note[] run = byPitch[i..(i + CheckFlag.ClusterSeconds)];
                if (run.Zip(run.Skip(1)).All(static pair => pair.Second.Pitch.MidiNumber - pair.First.Pitch.MidiNumber is > 0 and <= 2))
                {
                    crowded.UnionWith(run);
                }
            }
        }

        return crowded.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber);
    }
}