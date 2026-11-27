using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import;

/// <summary>Which import raised a Check flag.</summary>
public enum CheckSource
{
    Scan,
    Transcription,

    /// <summary>A performance compared with a recognised scan (0022, section 6).</summary>
    Performance,
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

    /// <summary>A performance plays a hand's notes an octave away over two bars or more: an octave clef may be lost (performance, 0022).</summary>
    RecordingOctave,

    /// <summary>A performance plays a bar the scan read too long or short in the meter's length (performance, 0022).</summary>
    RecordingBarLength,

    /// <summary>The bar is on a page the scan server's report calls poor (scan, version 0.4, within version 15).</summary>
    PageQuality,

    /// <summary>No two of the engines that read the page agree on some staff of the bar (scan, version 1.3, within version 17; ADR 0035).</summary>
    EnginesDisagree,

    /// <summary>
    /// A correction the player made on an earlier reading of the same pages that the new reading gives neither as the
    /// player found it nor as they corrected it (scan, «Как напечатано» step 2, version 1.5, within version 17): it stays on
    /// the earlier reading, beside these notes, and the player decides.
    /// </summary>
    CorrectionUnfit,

    /// <summary>
    /// A mark «что не так» the player made on an earlier reading of the same pages that the new reading could not take: its
    /// bar is not there, or the reading has no such staff or clef (scan, «Как напечатано» step 3, version 1.6, project file
    /// 18). The mark stays on the notes; the player checks the bar.
    /// </summary>
    MarkUnfit,

    /// <summary>
    /// A bar the player marked as read badly, which the new reading of its page takes from another engine than the one taken
    /// before (scan, «Как напечатано» step 3, version 1.6, project file 18): the player compares it with the page and the
    /// engines' other readings.
    /// </summary>
    ReadAgain,
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

    /// <summary>
    /// The flags of a scan's check, one per flagged bar and reason, resolved where the player confirmed the bar. A bar on a
    /// poor page (<see cref="ScanFlag.PageQuality"/>, from the page reports the check was given) raises
    /// <see cref="CheckReason.PageQuality"/>, a value of the project file since version 0.4 that a build before it keeps
    /// whole as an unknown flag (S6); a bar a scan merge marked (<see cref="ScanFlag.EnginesDisagree"/>) raises
    /// <see cref="CheckReason.EnginesDisagree"/>, a value since version 1.3 (Andrei, 2026-11-24), kept so by a build before it.
    /// </summary>
    public static IReadOnlyList<CheckFlag> FromScan(ScanCheck check, IReadOnlyCollection<int> confirmedBars)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(confirmedBars);
        return [.. check.Flagged.SelectMany(bar => bar.Flags
            .Select(flag => new CheckFlag(bar.Bar, CheckSource.Scan, Of(flag), confirmedBars.Contains(bar.Bar))))];
    }

    /// <summary>The most bars flagged for one reason in a transcription; beyond it the reason says nothing about a bar in particular.</summary>
    internal const double MostFlaggedShare = 0.25;

    /// <summary>An onset further than this from the 16th and the triplet-8th grids sits badly in its bar: a 64th.</summary>
    internal const long OffGridTicks = CanonicalTime.TicksPerQuarter / 16;

    /// <summary>The fewest onsets a bar needs before its fit to the tempo says anything.</summary>
    internal const int FewestOnsetsForTempo = 4;

    /// <summary>The share of a bar's onsets off the grids that makes its tempo doubtful.</summary>
    internal const double OffGridShare = 0.5;

    /// <summary>A cluster: this many separate onsets within a 16th…</summary>
    public const int ClusterOnsets = 4;

    /// <summary>…or this many notes starting together, each a second above the next, where a transcription often hears extra notes.</summary>
    public const int ClusterSeconds = 3;

    /// <summary>A hand inferred with less confidence than this was guessed from the pitch alone (<see cref="HandInference"/>).</summary>
    internal const double GuessedHandConfidence = 0.6;

    /// <summary>The fewest notes with a guessed hand that flag a bar.</summary>
    internal const int FewestGuessedHands = 2;

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
            long origin = bar.Origin;
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

    /// <summary>
    /// Whether the flag doubts the notes themselves (their pitch, onset or length), so what is read from them rests on notes
    /// to check (#352): every reason but <see cref="CheckReason.HandsGuessed"/>, which doubts only the hand that plays them.
    /// </summary>
    public bool DoubtsNotes => Reason != CheckReason.HandsGuessed;

    /// <summary>The bars whose recognised notes are still to check: those with an open flag that doubts the notes (<see cref="DoubtsNotes"/>), in order.</summary>
    public static IReadOnlyList<int> DoubtfulBars(IEnumerable<CheckFlag>? flags) =>
        [.. (flags ?? []).Where(static flag => !flag.Resolved && flag.DoubtsNotes).Select(static flag => flag.Bar).Distinct().Order()];

    /// <summary>The bars the player said look right: flagged, every flag on them resolved, in order.</summary>
    public static IReadOnlyList<int> CheckedBars(IEnumerable<CheckFlag>? flags) =>
        [.. (flags ?? []).GroupBy(static flag => flag.Bar).Where(static bar => bar.All(static flag => flag.Resolved)).Select(static bar => bar.Key).Order()];

    public static string Describe(CheckReason reason, Words words) => reason switch
    {
        CheckReason.BarTooLong => ScanBar.Describe(ScanFlag.BarTooLong, words),
        CheckReason.BarTooShort => ScanBar.Describe(ScanFlag.BarTooShort, words),
        CheckReason.StavesDiffer => ScanBar.Describe(ScanFlag.StavesDiffer, words),
        CheckReason.OutOfPianoRange => ScanBar.Describe(ScanFlag.OutOfPianoRange, words),
        CheckReason.GraceNotes => ScanBar.Describe(ScanFlag.GraceNotes, words),
        CheckReason.PageQuality => ScanBar.Describe(ScanFlag.PageQuality, words),
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
        CheckReason.RecordingOctave => words.T(
            "The recording plays these notes an octave away from the recognised ones.",
            "Запись звучит здесь на октаву иначе, чем распознанные ноты."),
        CheckReason.RecordingBarLength => words.T(
            "The recording plays this bar in the length of its time signature.",
            "Запись играет этот такт ровно по размеру."),
        CheckReason.EnginesDisagree => ScanBar.Describe(ScanFlag.EnginesDisagree, words),
        CheckReason.ReadAgain => ScanBar.Describe(ScanFlag.ReadAgain, words),
        CheckReason.MarkUnfit => words.T(
            "Your mark on this bar could not be used in this reading: the reading has no such bar, staff or clef here, its notes do not add up as the mark says, or no engine read the bar otherwise. The mark stays; check the bar against the page.",
            "Вашу отметку на этом такте не удалось учесть в этом чтении: в нём нет такого такта, стана или ключа, ноты не складываются так, как говорит отметка, или ни один движок не прочитал такт иначе. Отметка осталась; сверьте такт со страницей."),
        CheckReason.CorrectionUnfit => words.T(
            "Your correction on the earlier reading of these pages does not fit this reading: it reads the note neither as you found it nor as you corrected it. The earlier reading keeps the correction; compare the two in Sources.",
            "Ваше исправление не подошло к новому чтению: оно читает эту ноту не так, как вы её нашли, и не так, как исправили. Исправление осталось в прежнем чтении; сравните их в «Источниках»."),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown check reason."),
    };

    /// <summary>The reason in a few words, for the flag on the score and the Check drawer: "Bar too long", «Руки угаданы».</summary>
    public static string NameOf(CheckReason reason, Words words)
    {
        return reason switch
        {
            CheckReason.BarTooLong => words.T("Bar too long", "Такт длиннее размера"),
            CheckReason.BarTooShort => words.T("Bar too short", "Такт короче размера"),
            CheckReason.StavesDiffer => words.T("Hands differ in length", "Руки разной длины"),
            CheckReason.OutOfPianoRange => words.T("Note beyond the piano", "Нота вне диапазона фортепиано"),
            CheckReason.GraceNotes => words.T("Grace notes", "Форшлаги"),
            CheckReason.PageQuality => words.T("Poor page", "Плохая страница"),
            CheckReason.EnginesDisagree => words.T("Engines disagree", "Движки разошлись"),
            CheckReason.ReadAgain => words.T("Read by another engine", "Прочитан другим движком"),
            CheckReason.CorrectionUnfit => words.T("Your correction did not fit", "Исправление не подошло"),
            CheckReason.MarkUnfit => words.T("Your mark not used", "Отметка не учтена"),
            CheckReason.PartSeam => words.T("Parts joined here", "Стык частей записи"),
            CheckReason.TempoUncertain => words.T("Tempo fits poorly", "Темп плохо ложится на такт"),
            CheckReason.DenseCluster => words.T("Crowded notes", "Скопление нот"),
            CheckReason.HandsGuessed => words.T("Hands guessed", "Руки угаданы"),
            CheckReason.RecordingOctave => words.T("Recording: other octave", "Запись: другая октава"),
            CheckReason.RecordingBarLength => words.T("Recording: other bar length", "Запись: другая длина такта"),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown Check reason."),
        };
    }

    /// <summary>What the reason means for the bar, in plain words for the player: what probably went wrong and what to look at.</summary>
    public static string Explain(CheckReason reason, Words words)
    {
        return reason switch
        {
            CheckReason.BarTooLong => ScanBar.Describe(ScanFlag.BarTooLong, words),
            CheckReason.BarTooShort => ScanBar.Describe(ScanFlag.BarTooShort, words),
            CheckReason.StavesDiffer => ScanBar.Describe(ScanFlag.StavesDiffer, words),
            CheckReason.OutOfPianoRange => ScanBar.Describe(ScanFlag.OutOfPianoRange, words),
            CheckReason.GraceNotes => ScanBar.Describe(ScanFlag.GraceNotes, words),
            CheckReason.PageQuality => ScanBar.Describe(ScanFlag.PageQuality, words),
            CheckReason.PartSeam => words.T(
                "Two parts of the recording were heard separately and joined in this bar: a note may be doubled or lost.",
                "Две части записи распознавались отдельно и стыкуются в этом такте: нота может удвоиться или потеряться."),
            CheckReason.TempoUncertain => words.T(
                "Most notes start between the beats, so the tempo worked out from the playing may not fit this bar.",
                "Большинство нот начинается между долями, поэтому темп, выведенный из игры, может не подходить этому такту."),
            CheckReason.DenseCluster => words.T(
                "Many notes start close together, where a transcription often hears notes that were not played.",
                "Много нот начинается почти одновременно: в таких местах расшифровка часто слышит лишние ноты."),
            CheckReason.HandsGuessed => words.T(
                "Which hand plays these notes was guessed from their pitch alone.",
                "Какой рукой играются эти ноты, угадано только по высоте."),
            CheckReason.RecordingOctave => words.T(
                "In the recording this hand sounds an octave away for two bars or more: the scan may have lost an octave sign (8va or 8vb) or a clef. Fix from the recording moves the notes; Keep as printed leaves them.",
                "В записи эта рука звучит на октаву иначе два такта и больше: при распознавании мог потеряться знак октавы (8va, 8vb) или ключ. «Исправить по записи» переносит ноты, «Оставить как в нотах» не трогает их."),
            CheckReason.RecordingBarLength => words.T(
                "The scan reads this bar longer or shorter than its time signature, while the recording plays it exactly in it: a note or rest was probably misread. Compare the bar with the page and the recording.",
                "При распознавании такт вышел длиннее или короче размера, а в записи он играется ровно по размеру: вероятно, неверно прочитана нота или пауза. Сравните такт со страницей и с записью."),
            CheckReason.EnginesDisagree => ScanBar.Describe(ScanFlag.EnginesDisagree, words),
            CheckReason.ReadAgain => ScanBar.Describe(ScanFlag.ReadAgain, words),
            CheckReason.CorrectionUnfit => Describe(CheckReason.CorrectionUnfit, words),
            CheckReason.MarkUnfit => Describe(CheckReason.MarkUnfit, words),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown Check reason."),
        };
    }

    private static CheckReason Of(ScanFlag flag) => flag switch
    {
        ScanFlag.BarTooLong => CheckReason.BarTooLong,
        ScanFlag.BarTooShort => CheckReason.BarTooShort,
        ScanFlag.StavesDiffer => CheckReason.StavesDiffer,
        ScanFlag.OutOfPianoRange => CheckReason.OutOfPianoRange,
        ScanFlag.GraceNotes => CheckReason.GraceNotes,
        ScanFlag.PageQuality => CheckReason.PageQuality,
        ScanFlag.EnginesDisagree => CheckReason.EnginesDisagree,
        ScanFlag.ReadAgain => CheckReason.ReadAgain,
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown scan flag."),
    };
}