using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>Which import raised a Check flag.</summary>
public enum CheckSource
{
    Scan,
    Transcription,
}

/// <summary>
/// Why a bar is worth checking (0018, section 9): the scan checks of 0011 and the doubts of a
/// transcription (preparation item 12), one list so the Check layer reads them the same way.
/// </summary>
public enum CheckReason
{
    /// <summary>The notes add up to more than the time signature allows (scan).</summary>
    BarTooLong,

    /// <summary>The notes add up to less than the time signature (scan).</summary>
    BarTooShort,

    /// <summary>The two hands do not add up to the same length (scan).</summary>
    StavesDiffer,

    /// <summary>A note is outside the piano's range (scan).</summary>
    OutOfPianoRange,

    /// <summary>The bar has grace notes, which are often misread (scan).</summary>
    GraceNotes,

    /// <summary>The bar lies where two parts of a long recording were joined (transcription).</summary>
    PartSeam,

    /// <summary>The tempo worked out from the playing fits the bar poorly (transcription).</summary>
    TempoUncertain,

    /// <summary>Many notes close together, where a transcription often hears extra ones (transcription).</summary>
    DenseCluster,

    /// <summary>The hands were guessed from the pitches alone (transcription).</summary>
    HandsGuessed,
}

/// <summary>
/// One Check flag (0018, section 9; preparation item 6): a bar, why it is doubtful, which import said so, and
/// whether the player has resolved it. One shape for scans and transcriptions; the Check layer shows the open
/// flags and disappears when every flag is resolved.
/// </summary>
/// <param name="Bar">The bar number, as the workspace shows it.</param>
public sealed record CheckFlag(int Bar, CheckSource Source, CheckReason Reason, bool Resolved)
{
    public int Bar { get; } = Bar >= 0 ? Bar : throw new ArgumentOutOfRangeException(nameof(Bar), Bar, "A bar number is not negative.");

    /// <summary>The flags of a scan's check, one per flagged bar and reason, resolved where the player confirmed the bar.</summary>
    public static IReadOnlyList<CheckFlag> FromScan(ScanCheck check, IReadOnlyCollection<int> confirmedBars)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(confirmedBars);
        return [.. check.Flagged.SelectMany(bar => bar.Flags.Select(flag => new CheckFlag(bar.Bar, CheckSource.Scan, Of(flag), confirmedBars.Contains(bar.Bar))))];
    }

    /// <summary>The most bars flagged for one reason in a transcription; beyond it the reason says nothing about a bar in particular.</summary>
    public const double MostFlaggedShare = 0.25;

    /// <summary>An onset further than this from the 16th and the triplet-8th grids sits badly in its bar: a 64th.</summary>
    public const long OffGridTicks = CanonicalTime.TicksPerQuarter / 16;

    /// <summary>The fewest onsets a bar needs before its fit to the tempo says anything.</summary>
    public const int FewestOnsetsForTempo = 4;

    /// <summary>The share of a bar's onsets off the grids that makes its tempo doubtful.</summary>
    public const double OffGridShare = 0.5;

    /// <summary>A cluster: this many separate onsets within a 16th…</summary>
    public const int ClusterOnsets = 4;

    /// <summary>…or this many notes starting together, each a second above the next, where a transcription often hears extra notes.</summary>
    public const int ClusterSeconds = 3;

    /// <summary>A hand inferred with less confidence than this was guessed from the pitch alone (<see cref="HandInference"/>).</summary>
    public const double GuessedHandConfidence = 0.6;

    /// <summary>The fewest notes with a guessed hand that flag a bar.</summary>
    public const int FewestGuessedHands = 2;

    /// <summary>
    /// The doubts of a transcription (0018, section 9; preparation item 12), one flag per bar and reason, all open:
    /// where two parts of a long recording were joined (<see cref="TranscriptionOrigin.Seams"/>), where the tempo
    /// worked out from the playing fits the bar poorly (onsets off the 16th and triplet-8th grids), where notes
    /// crowd together (onsets closer than a 16th, or seconds struck at once), and where the hands were guessed. A
    /// reason that would flag more than a quarter of the bars is left out, since it then says nothing about a bar.
    /// Deterministic: the same notes give the same flags.
    /// </summary>
    /// <param name="origin">The transcription's origin: its seams and whether bars were worked out.</param>
    /// <param name="engineMidi">The engine's MIDI, the project's source, on the recording's clock.</param>
    /// <param name="piece">The piece as opened: on the bars worked out, if any, with the notes marked detected.</param>
    public static IReadOnlyList<CheckFlag> FromTranscription(TranscriptionOrigin origin, byte[] engineMidi, ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(engineMidi);
        ArgumentNullException.ThrowIfNull(piece);

        Note[] notes = [.. piece.Notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
        int barCount = Math.Max(1, piece.Meter.BarsBefore(piece.End).Count());
        int BarOf(Tick tick) => piece.Meter.BarAt(tick).Number;

        var flags = new List<CheckFlag>();
        void Add(CheckReason reason, IEnumerable<int> bars, bool always = false)
        {
            int[] flagged = [.. bars.Distinct().Order()];
            if (always || flagged.Length <= Math.Max(1, (int)(barCount * MostFlaggedShare)))
            {
                flags.AddRange(flagged.Select(bar => new CheckFlag(bar, CheckSource.Transcription, reason, false)));
            }
        }

        // Seams: the bars of the last note before and the first note after each, found by id in the engine's notes.
        if (origin.Seams.Count > 0 && notes.Length > 0)
        {
            MidiImport engine = MidiImporter.Import(engineMidi);
            var onsets = notes.ToDictionary(note => note.Id, note => note.Span.Onset, StringComparer.Ordinal);
            Note[] heard = [.. engine.Notes.Where(note => onsets.ContainsKey(note.Id)).OrderBy(note => note.Span.Onset).ThenBy(note => note.Id, StringComparer.Ordinal)];
            var seamBars = new List<int>();
            foreach (long milliseconds in origin.Seams)
            {
                Tick at = TickAt(engine.Tempos, milliseconds * 1000);
                Note? before = heard.LastOrDefault(note => note.Span.Onset < at);
                Note? after = heard.FirstOrDefault(note => note.Span.Onset >= at);
                seamBars.AddRange(new[] { before, after }.OfType<Note>().Select(note => BarOf(onsets[note.Id])));
            }

            // A seam is a fact about the recording, not a statistic: every one is flagged.
            Add(CheckReason.PartSeam, seamBars, always: true);
        }

        // The tempo: only bars worked out from the playing have a fit to judge.
        if (piece.Bars is not null)
        {
            Add(CheckReason.TempoUncertain, notes
                .GroupBy(note => BarOf(note.Span.Onset))
                .Where(bar => OffGrid(bar.Select(note => note.Span.Onset).Distinct().ToArray(), piece.Meter))
                .Select(bar => bar.Key));
        }

        Add(CheckReason.DenseCluster, Clusters(notes).Select(BarOf));

        Add(CheckReason.HandsGuessed, notes
            .Where(note => note.Hand is { Attribution: { Provenance: Provenance.Inferred, Confidence: < GuessedHandConfidence } })
            .GroupBy(note => BarOf(note.Span.Onset))
            .Where(bar => bar.Count() >= FewestGuessedHands)
            .Select(bar => bar.Key));

        return [.. flags.OrderBy(flag => flag.Bar).ThenBy(flag => flag.Reason)];
    }

    /// <summary>Whether most of a bar's onsets sit off both the 16th and the triplet-8th grids.</summary>
    private static bool OffGrid(Tick[] onsets, MeterMap meter)
    {
        if (onsets.Length < FewestOnsetsForTempo)
        {
            return false;
        }

        long sixteenth = CanonicalTime.TicksPerQuarter / 4, tripletEighth = CanonicalTime.TicksPerQuarter / 3;
        int off = onsets.Count(onset =>
        {
            Bar bar = meter.BarAt(onset);
            long origin = bar.IsPickup ? bar.Span.End.Value - bar.Signature.BarTicks : bar.Span.Onset.Value;
            long from = onset.Value - origin;
            return Distance(from, sixteenth) > OffGridTicks && Distance(from, tripletEighth) > OffGridTicks;
        });
        return off >= onsets.Length * OffGridShare;
    }

    private static long Distance(long value, long step)
    {
        long rest = ((value % step) + step) % step;
        return Math.Min(rest, step - rest);
    }

    /// <summary>Where notes crowd: the first onset of every run of <see cref="ClusterOnsets"/> onsets within a 16th, and every onset with seconds struck together.</summary>
    private static IEnumerable<Tick> Clusters(Note[] notes)
    {
        long sixteenth = CanonicalTime.TicksPerQuarter / 4;
        Tick[] onsets = [.. notes.Select(note => note.Span.Onset).Distinct().Order()];
        for (int i = 0; i + ClusterOnsets - 1 < onsets.Length; i++)
        {
            if (onsets[i + ClusterOnsets - 1].Value - onsets[i].Value < sixteenth)
            {
                yield return onsets[i];
            }
        }

        foreach (IGrouping<Tick, Note> chord in notes.GroupBy(note => note.Span.Onset))
        {
            int[] pitches = [.. chord.Select(note => note.Pitch.MidiNumber).Distinct().Order()];
            // A run of notes each a second (one or two semitones) above the one below.
            int run = 1;
            for (int i = 1; i < pitches.Length && run < ClusterSeconds; i++)
            {
                run = pitches[i] - pitches[i - 1] <= 2 ? run + 1 : 1;
            }

            if (run >= ClusterSeconds)
            {
                yield return chord.Key;
            }
        }
    }

    /// <summary>The tick of the engine's MIDI at a time in the recording, through its tempo changes.</summary>
    private static Tick TickAt(IReadOnlyList<MidiTempoChange> tempos, long microseconds)
    {
        long at = 0, elapsed = 0, perQuarter = MidiTempoChange.DefaultMicrosecondsPerQuarter;
        foreach (MidiTempoChange tempo in tempos.OrderBy(tempo => tempo.At))
        {
            long next = elapsed + ((tempo.At.Value - at) * perQuarter / CanonicalTime.TicksPerQuarter);
            if (next > microseconds)
            {
                break;
            }

            (at, elapsed, perQuarter) = (tempo.At.Value, next, tempo.MicrosecondsPerQuarter);
        }

        return new Tick(at + ((microseconds - elapsed) * CanonicalTime.TicksPerQuarter / perQuarter));
    }

    /// <summary>The reason as the Check layer says it, in the language.</summary>
    public string Describe(Words words) => Describe(Reason, words);

    public static string Describe(CheckReason reason, Words words) => reason switch
    {
        CheckReason.BarTooLong => ScanBar.Describe(ScanFlag.BarTooLong, words),
        CheckReason.BarTooShort => ScanBar.Describe(ScanFlag.BarTooShort, words),
        CheckReason.StavesDiffer => ScanBar.Describe(ScanFlag.StavesDiffer, words),
        CheckReason.OutOfPianoRange => ScanBar.Describe(ScanFlag.OutOfPianoRange, words),
        CheckReason.GraceNotes => ScanBar.Describe(ScanFlag.GraceNotes, words),
        CheckReason.PartSeam => words.T(
            "Two parts of the recording were joined here, so a note may be missing or doubled.",
            "Здесь склеены две части записи: нота могла потеряться или удвоиться."),
        CheckReason.TempoUncertain => words.T(
            "The tempo worked out from the playing fits this bar poorly, so its rhythm may be written wrong.",
            "Темп, снятый с исполнения, плохо ложится на этот такт: его ритм мог записаться неверно."),
        CheckReason.DenseCluster => words.T(
            "Many notes sound close together here, where a transcription often hears extra ones.",
            "Здесь много нот подряд: транскрипция в таких местах часто слышит лишние."),
        CheckReason.HandsGuessed => words.T(
            "The hands in this bar were guessed from the pitches alone.",
            "Руки в этом такте угаданы только по высоте нот."),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown check reason."),
    };

    private static CheckReason Of(ScanFlag flag) => flag switch
    {
        ScanFlag.BarTooLong => CheckReason.BarTooLong,
        ScanFlag.BarTooShort => CheckReason.BarTooShort,
        ScanFlag.StavesDiffer => CheckReason.StavesDiffer,
        ScanFlag.OutOfPianoRange => CheckReason.OutOfPianoRange,
        ScanFlag.GraceNotes => CheckReason.GraceNotes,
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown scan flag."),
    };
}