using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// A fragment of a selection made into a variant (decision record 0034, section 3): in any view of notes, the notes that
/// start in the span, all of them, one hand, or the melody where the voices are clean, copied with their own bars, tempo
/// and key, from 0.
/// </summary>
public static class DraftFragment
{
    /// <summary>
    /// What of the notes can be taken: all of them always; a hand when the notes carry it; the melody only where the right
    /// hand's voices are cleanly split (<see cref="Melody"/>).
    /// </summary>
    public static IReadOnlyList<DraftPart> Parts(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        List<DraftPart> parts = [DraftPart.All];
        if (notes.Any(static note => note.Hand?.Value == Hand.Right))
        {
            parts.Add(DraftPart.Right);
        }

        if (notes.Any(static note => note.Hand?.Value == Hand.Left))
        {
            parts.Add(DraftPart.Left);
        }

        if (Melody(notes) is not null)
        {
            parts.Add(DraftPart.Melody);
        }

        return parts;
    }

    /// <summary>
    /// The melody of the notes: the top voice of the right hand (the highest note at each onset), marked inferred, as the
    /// engine found it, at the notes' own confidence. Only where the voices are cleanly split (decision record 0034, decided
    /// without Andrei): the line's notes never overlap one another (no split), no other note of the right hand sounds above
    /// a line note while it sounds (no crossing), and where the notes carry written voices the line stays in one of them.
    /// Null otherwise, and for notes with no right hand, so the choice is not offered.
    /// </summary>
    public static IReadOnlyList<Note>? Melody(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        Note[] right = [.. notes.Where(static note => note.Hand?.Value == Hand.Right)];
        Note[] line = StructureAnalyzer.Line(right);
        if (line.Length == 0)
        {
            return null;
        }

        bool split = line.Zip(line.Skip(1)).Any(static pair => pair.First.Span.End > pair.Second.Span.Onset)
            || line.Select(static note => note.Voice).Where(static voice => voice is not null).Distinct().Count() > 1;
        HashSet<string> top = [.. line.Select(static note => note.Id)];
        bool crossing = right.Any(other => !top.Contains(other.Id)
            && line.Any(note => note.Span.Overlaps(other.Span) && other.Pitch.MidiNumber > note.Pitch.MidiNumber));
        return split || crossing ? null : [.. line.Select(static note => note with { Attribution = Attribution.Inferred(note.Attribution.Confidence) })];
    }

    /// <summary>
    /// The music of <paramref name="span"/> of <paramref name="music"/>, from 0: the notes that start in it, of the part
    /// asked for, the bars cut at its ends, and the tempo and key in force at its start with the changes inside it.
    /// </summary>
    /// <exception cref="ArgumentException">The part is not offered for these notes, or it holds no notes.</exception>
    public static SourceMusic Of(SourceMusic music, TickSpan span, DraftPart part)
    {
        ArgumentNullException.ThrowIfNull(music);
        IReadOnlyList<Note> selected = music.NotesIn(span.Onset, span.End);
        IReadOnlyList<Note> taken = part switch
        {
            DraftPart.Right => [.. selected.Where(static note => note.Hand?.Value == Hand.Right)],
            DraftPart.Left => [.. selected.Where(static note => note.Hand?.Value == Hand.Left)],
            DraftPart.Melody => Melody(selected) ?? throw new ArgumentException("The melody is taken only where the voices are cleanly split.", nameof(part)),
            _ => selected,
        };
        if (taken.Count == 0)
        {
            throw new ArgumentException("The selection holds no notes of that part.", nameof(part));
        }

        long from = span.Onset.Value;
        Tick Moved(Tick tick) => new(Math.Max(0, tick.Value - from));
        PlaybackTempo[] tempos = [.. music.Tempos.Where(tempo => tempo.At <= span.Onset).TakeLast(1).Concat(music.Tempos.Where(tempo => tempo.At > span.Onset && tempo.At < span.End))];
        KeySignature[] keys = [.. (KeySignature.InForce(music.Keys, span.Onset) is { } start ? [start] : Array.Empty<KeySignature>()).Concat(music.Keys.Where(key => key.At > span.Onset && key.At < span.End))];
        return new SourceMusic(
            [.. taken.Select(note => note with { Span = new TickSpan(Moved(note.Span.Onset), note.Span.Duration) })],
            AssemblyBars.Moved(AssemblyBars.Cut(music.Bars, span.Onset, span.End), -from),
            [.. tempos.Select(tempo => tempo with { At = Moved(tempo.At) })],
            [.. keys.Select(key => key with { At = Moved(key.At) })]);
    }

    /// <summary>
    /// The stretch of the recording <paramref name="span"/> of the notes was played in, in milliseconds of the recording, when
    /// they were heard in one (<paramref name="project"/>, opened as <paramref name="piece"/>, has a transcription): from when
    /// its first bar line was played to when its last was (<see cref="HeardCut.StretchOfBars"/>), moved by where the stretch
    /// sent to the engine started. Null for notes not heard.
    /// </summary>
    public static (long From, long To)? Heard(Project project, ImportedPiece piece, TickSpan span)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (HeardClock.Of(project, piece) is not { } clock)
        {
            return null;
        }

        (long from, long to) = HeardCut.StretchOfBars(clock, span);
        long sent = project.Transcription?.SentFromMilliseconds ?? 0;
        return to > from ? (from + sent, to + sent) : null;
    }

    /// <summary>
    /// A variant of the fragment <paramref name="span"/> of the Notes <paramref name="notesId"/>, whose music as shown is
    /// <paramref name="music"/> and whose provenance is <paramref name="provenance"/>: the music copied (<see cref="Of"/>), its
    /// bars named for the label, and, from another piece (<paramref name="project"/>), every source of the provenance naming
    /// that piece, so the original's fingerprint follows the fragment.
    /// </summary>
    /// <param name="milliseconds">The stretch of the recording the span was played in, for notes heard; else null.</param>
    public static DraftVariant Variant(string id, DateTimeOffset at, SourceMusic music, TickSpan span, DraftPart part, string notesId, NotesProvenance provenance, string? project = null, (long From, long To)? milliseconds = null)
    {
        ArgumentNullException.ThrowIfNull(music);
        ArgumentNullException.ThrowIfNull(provenance);
        int first = music.BarAt(span.Onset).Number;
        int last = music.BarAt(new Tick(Math.Max(span.Onset.Value, span.End.Value - 1))).Number;
        NotesProvenance copied = project is null ? provenance : provenance with { From = [.. provenance.From.Select(source => source.Project is null ? source with { Project = project } : source)] };
        var from = new DraftSource(notesId, span) { Project = project, Bars = (first, last), Milliseconds = milliseconds, Part = part };
        return new DraftVariant(id, at, from, copied, Of(music, span, part));
    }
}