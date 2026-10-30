using System.Globalization;
using System.Xml;
using System.Xml.Linq;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>
/// Imports a partwise MusicXML score, uncompressed or <c>.mxl</c>, onto the canonical timeline
/// (S1-I1, S1-I2, S1-I3, S1-I4, S1-I8). Decision record 0006 describes the policies.
/// </summary>
/// <remarks>
/// <para>
/// Each part is walked in document order. Note, <c>&lt;forward&gt;</c> and <c>&lt;backup&gt;</c>
/// durations convert through the part's current <see cref="MusicXmlDivisions"/> to exact
/// <see cref="QuarterFraction"/>s and are summed exactly, so a divisions change or a septuplet adds
/// no error. Measures are aligned across parts by index; positions round to ticks only in
/// <see cref="CanonicalTime"/>, onset and end separately.
/// </para>
/// <para>
/// Notes map to the shared <see cref="Note"/> as imported facts, with tied notes merged into one.
/// A part with two staves is a grand staff: staff 1 is the right hand and staff 2 the left, both
/// imported. Grace notes, rests, slurs and repeat signs are kept as source facts beside the notes.
/// </para>
/// <para>
/// The import is pure and deterministic: the same bytes give the same result and report.
/// </para>
/// </remarks>
public static class MusicXmlImporter
{
    /// <summary>The Stage 1 file size limit (S1-I1).</summary>
    public const int MaxFileBytes = 50 * 1024 * 1024;

    /// <summary>Velocity for notes the score gives no playback dynamics: between mp and mf.</summary>
    public const int DefaultVelocity = 80;

    /// <summary>MusicXML dynamics are a percentage of forte, which is velocity 90.</summary>
    private const int ForteVelocity = 90;

    private static readonly int[] s_stepSemitones = [9, 11, 0, 2, 4, 5, 7]; // A to G

    public static MusicXmlImport Import(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes)
        {
            throw new MusicXmlImportException(MusicXmlImportFailure.TooLarge, "This file is larger than 50 MB, the most Motiff Lab can import.");
        }

        XElement root = Load(MxlArchive.IsArchive(data) ? MxlArchive.ReadScore(data) : data);
        switch (root.Name.LocalName)
        {
            case "score-partwise":
                break;
            case "score-timewise":
                throw new MusicXmlImportException(
                    MusicXmlImportFailure.Unsupported,
                    "This MusicXML file is arranged measure by measure (timewise), which Motiff Lab cannot read yet. Most notation apps export the usual part-by-part layout.");
            default:
                throw NotMusicXml();
        }

        var issues = new ImportIssueCollector();
        var listed = ReadPartList(root);
        var parts = new List<PartReader>();
        foreach (XElement part in root.Elements("part"))
        {
            int index = parts.Count;
            string? id = Trimmed(part.Attribute("id")?.Value);
            if (id is null || !listed.TryGetValue(id, out (string? Name, string? Instrument) names))
            {
                issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.PartNotListed, index);
                names = (null, null);
            }

            var reader = new PartReader(index, id ?? $"P{index + 1}", names.Name, names.Instrument, issues);
            reader.Read(part);
            parts.Add(reader);
        }

        if (parts.Count == 0 || parts.All(part => part.Measures.Count == 0))
        {
            throw new MusicXmlImportException(MusicXmlImportFailure.Damaged, "This MusicXML file has no parts or measures, so there is nothing to import.");
        }

        return Build(root, parts, issues);
    }

    /// <summary>
    /// The score's XML: the root file of a compressed <c>.mxl</c> archive, or the bytes as given.
    /// Lets a caller show the file as written without unpacking it itself.
    /// </summary>
    public static byte[] ReadScoreXml(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes)
        {
            throw new MusicXmlImportException(MusicXmlImportFailure.TooLarge, "This file is larger than 50 MB, the most Motiff Lab can import.");
        }

        return MxlArchive.IsArchive(data) ? MxlArchive.ReadScore(data) : data.ToArray();
    }

    private static MusicXmlImport Build(XElement root, List<PartReader> parts, ImportIssueCollector issues)
    {
        int measureCount = parts.Max(part => part.Measures.Count);

        // Measures align across parts by index; each lasts as long as its longest part.
        var times = parts
            .SelectMany(part => part.Times)
            .OrderBy(time => time.MeasureIndex)
            .ThenBy(time => time.Offset)
            .ThenBy(time => time.Track)
            .ThenBy(time => time.Order)
            .ToList();
        var starts = new QuarterFraction[measureCount + 1];
        TimeSignature inForce = TimeSignature.CommonTime;
        int nextTime = 0;
        for (int i = 0; i < measureCount; i++)
        {
            while (nextTime < times.Count && (times[nextTime].MeasureIndex < i || (times[nextTime].MeasureIndex == i && times[nextTime].Offset == QuarterFraction.Zero)))
            {
                inForce = times[nextTime].Value;
                nextTime++;
            }

            QuarterFraction length = parts
                .Where(part => i < part.Measures.Count)
                .Select(part => part.Measures[i].Length)
                .Max();

            // A measure with no content at all (not even a rest) lasts a full bar.
            if (length == QuarterFraction.Zero)
            {
                length = new QuarterFraction(inForce.BarTicks, CanonicalTime.TicksPerQuarter);
            }

            starts[i + 1] = starts[i] + length;
        }

        QuarterFraction At(int measureIndex, QuarterFraction offset) => starts[measureIndex] + offset;

        // Notes
        var sourceNotes = new List<MusicXmlNote>();
        var noteCounts = new int[parts.Count];
        foreach (PartReader part in parts)
        {
            List<TiedNote> merged = MergeTies(part, At, issues);
            noteCounts[part.Index] = merged.Count;
            foreach ((PartNote note, QuarterFraction onset, QuarterFraction noteEnd, int written) in merged)
            {
                QuarterFraction duration = noteEnd - onset;
                TickSpan span;
                bool exact;
                if (duration == QuarterFraction.Zero)
                {
                    issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.ZeroLengthWidened, part.Index);
                    span = new TickSpan(CanonicalTime.ToTick(onset).Tick, 1);
                    exact = false;
                }
                else
                {
                    ConvertedSpan converted = CanonicalTime.ToSpan(onset, noteEnd);
                    span = converted.Span;
                    exact = converted.IsExact;
                    if (!exact)
                    {
                        issues.Add(ImportIssueKind.Rounded, MusicXmlIssueCodes.NotesRounded);
                    }
                }

                sourceNotes.Add(new MusicXmlNote(
                    part.Index,
                    part.Id,
                    note.MeasureIndex,
                    note.NoteIndex,
                    note.Staff,
                    note.Voice,
                    note.Pitch,
                    note.Velocity ?? DefaultVelocity,
                    note.Velocity is null,
                    onset,
                    duration,
                    span,
                    exact,
                    written,
                    note.Tuplet,
                    note.Accent));
            }
        }

        sourceNotes.Sort(static (a, b) =>
        {
            int order = a.Span.Onset.CompareTo(b.Span.Onset);
            if (order == 0)
            {
                order = a.Track.CompareTo(b.Track);
            }

            if (order == 0)
            {
                order = a.Pitch.CompareTo(b.Pitch);
            }

            if (order == 0)
            {
                order = a.MeasureIndex.CompareTo(b.MeasureIndex);
            }

            return order != 0 ? order : a.NoteIndex.CompareTo(b.NoteIndex);
        });

        issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.VelocityDefault, count: sourceNotes.Count(note => note.IsVelocityAssumed));

        // Grace notes and rests: source facts, not sounding notes.
        var graceNotes = parts
            .SelectMany(part => part.GraceNotes.Select(grace =>
            {
                QuarterFraction at = At(grace.MeasureIndex, grace.Offset);
                return new MusicXmlGraceNote(part.Index, part.Id, grace.MeasureIndex, grace.NoteIndex, grace.Staff, grace.Voice, grace.Pitch, at, CanonicalTime.ToTick(at).Tick, grace.IsSlashed);
            }))
            .OrderBy(grace => grace.Onset)
            .ThenBy(grace => grace.Track)
            .ThenBy(grace => grace.MeasureIndex)
            .ThenBy(grace => grace.NoteIndex)
            .ToList();
        var rests = parts
            .SelectMany(part => part.Rests.Select(rest =>
            {
                QuarterFraction onset = At(rest.MeasureIndex, rest.Offset);
                return new MusicXmlRest(part.Index, part.Id, rest.MeasureIndex, rest.NoteIndex, rest.Staff, rest.Voice, onset, rest.Duration, CanonicalTime.ToSpan(onset, onset + rest.Duration).Span, rest.IsMeasureRest);
            }))
            .OrderBy(rest => rest.Onset)
            .ThenBy(rest => rest.Track)
            .ThenBy(rest => rest.MeasureIndex)
            .ThenBy(rest => rest.NoteIndex)
            .ToList();

        // Slurs and repeat signs: the source's phrase evidence (S1-I5).
        var slurs = parts
            .SelectMany(part => PairSlurs(part, At, issues))
            .OrderBy(slur => slur.Start)
            .ThenBy(slur => slur.Track)
            .ThenBy(slur => slur.Stop)
            .ToList();
        var repeats = parts
            .SelectMany(part => part.Repeats)
            .Select(repeat => new MusicXmlRepeat(
                CanonicalTime.ToTick(starts[repeat.IsAtEnd ? repeat.MeasureIndex + 1 : repeat.MeasureIndex]).Tick,
                repeat.MeasureIndex,
                repeat.Direction))
            .Distinct()
            .OrderBy(repeat => repeat.At)
            .ThenBy(repeat => repeat.Direction)
            .ToList();

        // Tempo
        var tempos = Merge(
                parts.SelectMany(part => part.Tempos),
                At,
                issues,
                MusicXmlIssueCodes.TempoSuperseded)
            .Select(tempo => new MusicXmlTempoChange(tempo.Tick, tempo.MeasureIndex, tempo.Value, IsAssumed: false))
            .ToList();
        if (tempos.Count == 0 || tempos[0].At != Tick.Zero)
        {
            issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.TempoDefault);
            tempos.Insert(0, new MusicXmlTempoChange(Tick.Zero, 0, MusicXmlTempoChange.DefaultQuarterNotesPerMinute, IsAssumed: true));
        }

        // Meter
        var timeSignatures = new List<MusicXmlTimeSignature>();
        foreach (Placed<TimeSignature> time in Merge(times, At, issues, MusicXmlIssueCodes.MeterSuperseded))
        {
            if (timeSignatures.Count == 0 || timeSignatures[^1].Numerator != time.Value.Numerator || timeSignatures[^1].Denominator != time.Value.Denominator)
            {
                timeSignatures.Add(new MusicXmlTimeSignature(time.Tick, time.MeasureIndex, time.Value.Numerator, time.Value.Denominator, IsAssumed: false));
            }
        }

        if (timeSignatures.Count == 0 || timeSignatures[0].At != Tick.Zero)
        {
            issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.MeterDefault);
            timeSignatures.Insert(0, new MusicXmlTimeSignature(Tick.Zero, 0, 4, 4, IsAssumed: true));
        }

        var boundaries = starts.Select(start => CanonicalTime.ToTick(start).Tick).ToArray();
        long pickup = 0;
        var firstSignature = new TimeSignature(timeSignatures[0].Numerator, timeSignatures[0].Denominator);
        if (measureCount > 1 && boundaries[1].Value < firstSignature.BarTicks)
        {
            pickup = boundaries[1].Value;
        }

        var changes = new SortedList<long, TimeSignature>();
        foreach (MusicXmlTimeSignature time in timeSignatures)
        {
            if (time.At.Value > 0 && time.At.Value < pickup)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MeterInPickup);
                continue;
            }

            changes[time.At.Value] = new TimeSignature(time.Numerator, time.Denominator);
        }

        timeSignatures.RemoveAll(time => time.At.Value > 0 && time.At.Value < pickup);
        AlignBarsToMeasures(changes, boundaries, pickup, issues);
        var meter = new MeterMap(changes.Select(change => new MeterChange(new Tick(change.Key), change.Value)), pickup);

        // Keys, per part
        var keys = new List<MusicXmlKeySignature>();
        foreach (PartReader part in parts)
        {
            foreach (Placed<(int Fifths, string? Mode)> key in Merge(part.Keys, At, issues, supersededCode: null))
            {
                MusicXmlKeySignature placed = new(key.Tick, part.Index, key.MeasureIndex, key.Value.Fifths, key.Value.Mode);
                MusicXmlKeySignature? previous = keys.Count > 0 && keys[^1].Track == part.Index ? keys[^1] : null;
                if (previous is null || previous.Fifths != placed.Fifths || previous.Mode != placed.Mode)
                {
                    keys.Add(placed);
                }
            }
        }

        // Measures and end of the piece
        var measures = new List<MusicXmlMeasure>(measureCount);
        for (int i = 0; i < measureCount; i++)
        {
            MeasureInfo info = parts.First(part => i < part.Measures.Count).Measures[i];
            measures.Add(new MusicXmlMeasure(i, info.Number, info.IsImplicit, starts[i], CanonicalTime.ToSpan(starts[i], starts[i + 1]).Span));
        }

        Tick end = boundaries[^1];
        if (sourceNotes.Count > 0)
        {
            Tick lastNoteEnd = sourceNotes.Max(note => note.Span.End);
            end = lastNoteEnd > end ? lastNoteEnd : end;
        }

        var partInfos = parts
            .Select(part => new MusicXmlPartInfo(part.Index, part.Id, part.Name, part.InstrumentName, part.Staves, noteCounts[part.Index]))
            .ToList();
        var events = sourceNotes.Select((note, index) => ToNote(note, index, partInfos[note.Track])).ToList();
        string? version = Trimmed(root.Attribute("version")?.Value);

        var report = new ImportReport(
            Format: version is null ? "MusicXML" : $"MusicXML {version}",
            TrackCount: parts.Count,
            NoteCount: sourceNotes.Count,
            MeasureCount: meter.BarsBefore(end).Count(),
            Issues: issues.Build(MusicXmlIssueCodes.Describe));

        return new MusicXmlImport(version, partInfos, events, sourceNotes, graceNotes, rests, slurs, repeats, measures, tempos, timeSignatures, meter, keys, end, report);
    }

    /// <summary>
    /// Pairs the slur starts and stops of a part by their <c>number</c>, in document order. A start
    /// whose number is already open replaces the open one; a stop with nothing open and a start never
    /// stopped are left out and reported.
    /// </summary>
    private static List<MusicXmlSlur> PairSlurs(PartReader part, Func<int, QuarterFraction, QuarterFraction> at, ImportIssueCollector issues)
    {
        var slurs = new List<MusicXmlSlur>();
        var open = new Dictionary<int, PartSlurMark>();
        foreach (PartSlurMark mark in part.Slurs)
        {
            if (mark.IsStart)
            {
                if (open.ContainsKey(mark.Number))
                {
                    issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.SlurUnmatched, part.Index);
                }

                open[mark.Number] = mark;
                continue;
            }

            if (!open.Remove(mark.Number, out PartSlurMark? start))
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.SlurUnmatched, part.Index);
                continue;
            }

            Tick first = CanonicalTime.ToTick(at(start.MeasureIndex, start.Offset)).Tick;
            Tick last = CanonicalTime.ToTick(at(mark.MeasureIndex, mark.Offset)).Tick;
            (first, last) = first <= last ? (first, last) : (last, first);
            slurs.Add(new MusicXmlSlur(part.Index, part.Id, mark.Number, start.MeasureIndex, first, last));
        }

        issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.SlurUnmatched, part.Index, open.Count);
        return slurs;
    }

    /// <summary>
    /// Merges each chain of tied notes in a part into its first note (S1-I2, AC-S1-02). A tie stop
    /// continues the open tie of the same pitch that ends where it starts, else the one in the same
    /// voice and staff; with neither, the note stays on its own and is reported.
    /// </summary>
    private static List<TiedNote> MergeTies(PartReader part, Func<int, QuarterFraction, QuarterFraction> at, ImportIssueCollector issues)
    {
        var merged = new List<TiedNote>();
        var open = new List<int>(); // indexes into merged of notes whose tie continues
        foreach (PartNote note in part.Notes)
        {
            QuarterFraction onset = at(note.MeasureIndex, note.Offset);
            QuarterFraction end = onset + note.Duration;
            int match = -1;
            if (note.TieStop)
            {
                match = open.FindIndex(i => merged[i].Note.Pitch == note.Pitch && merged[i].End == onset);
                if (match < 0)
                {
                    match = open.FindIndex(i => merged[i].Note.Pitch == note.Pitch && merged[i].Note.Voice == note.Voice && merged[i].Note.Staff == note.Staff);
                }
            }

            if (match >= 0)
            {
                int target = open[match];
                TiedNote tied = merged[target];
                merged[target] = tied with { End = end > tied.End ? end : tied.End, Written = tied.Written + 1 };
                if (!note.TieStart)
                {
                    open.RemoveAt(match);
                }

                continue;
            }

            if (note.TieStop)
            {
                issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.TieWithoutStart, part.Index);
            }

            // A new note of the same pitch in the same voice ends a tie that was never stopped.
            open.RemoveAll(i => merged[i].Note.Pitch == note.Pitch && merged[i].Note.Voice == note.Voice && merged[i].Note.Staff == note.Staff);
            merged.Add(new TiedNote(note, onset, end, 1));
            if (note.TieStart)
            {
                open.Add(merged.Count - 1);
            }
        }

        return merged;
    }

    /// <summary>
    /// Restates the time signature after any measure that is shorter or longer than a full bar, so
    /// the next measure starts on a bar line of the meter map (decision record 0004: a change ends
    /// the bar in progress). Measures that contain a time signature change are left as they are.
    /// </summary>
    private static void AlignBarsToMeasures(SortedList<long, TimeSignature> changes, Tick[] boundaries, long pickup, ImportIssueCollector issues)
    {
        for (int i = 0; i + 2 < boundaries.Length; i++)
        {
            long start = boundaries[i].Value;
            long end = boundaries[i + 1].Value;
            if ((i == 0 && pickup > 0) || changes.Keys.Any(position => position > start && position <= end))
            {
                continue;
            }

            TimeSignature signature = changes.Last(change => change.Key <= start).Value;
            long length = end - start;
            if (length == signature.BarTicks)
            {
                continue;
            }

            issues.Add(ImportIssueKind.Assumed, length < signature.BarTicks ? MusicXmlIssueCodes.MeasureShort : MusicXmlIssueCodes.MeasureLong);
            changes[end] = signature;
        }
    }

    private static Note ToNote(MusicXmlNote source, int index, MusicXmlPartInfo part) => new()
    {
        Id = $"n_{index + 1:D4}",
        Pitch = new Pitch(source.Pitch),
        Span = source.Span,
        Velocity = source.Velocity,
        Track = source.Track,
        Voice = int.TryParse(source.Voice, NumberStyles.None, CultureInfo.InvariantCulture, out int voice) && voice >= 1 ? voice : null,
        Hand = part.HasHands && source.Staff is 1 or 2
            ? new Attributed<Hand>(source.Staff == 1 ? Hand.Right : Hand.Left, Attribution.Imported)
            : null,
        Attribution = Attribution.Imported,
        Source = new MusicXmlSourceLocation(source.PartId, source.MeasureIndex, source.NoteIndex, source.Staff, source.Voice),
    };

    /// <summary>
    /// Orders values by exact position (ties keep part order, then document order), rounds them to
    /// canonical ticks and keeps one value per tick. Repeats of the same value (a tempo mark copied
    /// into every part) merge silently; a different value at the same tick replaces the earlier one.
    /// </summary>
    private static List<Placed<T>> Merge<T>(
        IEnumerable<Timed<T>> values,
        Func<int, QuarterFraction, QuarterFraction> at,
        ImportIssueCollector issues,
        string? supersededCode)
    {
        var ordered = values
            .Select(value => (Value: value, Position: at(value.MeasureIndex, value.Offset)))
            .OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.Value.Track)
            .ThenBy(entry => entry.Value.Order)
            .ToList();

        var merged = new List<Placed<T>>();
        foreach (var (value, position) in ordered)
        {
            ConvertedTick converted = CanonicalTime.ToTick(position);
            if (!converted.IsExact)
            {
                issues.Add(ImportIssueKind.Rounded, MusicXmlIssueCodes.MetaRounded);
            }

            var placed = new Placed<T>(converted.Tick, value.MeasureIndex, value.Value);
            if (merged.Count > 0 && merged[^1].Tick == placed.Tick)
            {
                if (!EqualityComparer<T>.Default.Equals(merged[^1].Value, placed.Value))
                {
                    if (supersededCode is not null)
                    {
                        issues.Add(ImportIssueKind.Dropped, supersededCode);
                    }

                    merged[^1] = placed;
                }
            }
            else
            {
                merged.Add(placed);
            }
        }

        return merged;
    }

    private static XElement Load(ReadOnlySpan<byte> data)
    {
        if (!LooksLikeXml(data))
        {
            throw NotMusicXml();
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader).Root ?? throw NotMusicXml();
        }
        catch (XmlException e)
        {
            throw new MusicXmlImportException(
                MusicXmlImportFailure.Damaged,
                $"This MusicXML file is damaged: its XML breaks off or is malformed near line {e.LineNumber}. Try exporting it again.");
        }
    }

    /// <summary>True when the first character after any byte order mark and white space is '&lt;'.</summary>
    private static bool LooksLikeXml(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || data.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return true;
        }

        if (data.StartsWith("\uFEFF"u8))
        {
            data = data[3..];
        }

        int first = data.IndexOfAnyExcept(" \t\r\n"u8);
        return first >= 0 && data[first] == (byte)'<';
    }

    private static MusicXmlImportException NotMusicXml() => new(
        MusicXmlImportFailure.NotMusicXml,
        "This file is not a MusicXML score. Motiff Lab reads MIDI and MusicXML; most notation apps can export MusicXML.");

    private static Dictionary<string, (string? Name, string? Instrument)> ReadPartList(XElement root)
    {
        var parts = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (XElement scorePart in root.Element("part-list")?.Elements("score-part") ?? [])
        {
            if (Trimmed(scorePart.Attribute("id")?.Value) is string id)
            {
                parts.TryAdd(id, (
                    Trimmed(scorePart.Element("part-name")?.Value),
                    Trimmed(scorePart.Element("score-instrument")?.Element("instrument-name")?.Value)));
            }
        }

        return parts;
    }

    private static string? Trimmed(string? text)
    {
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool TryParseInt(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    /// <summary>A value read at a position within a measure, in document order.</summary>
    private sealed record Timed<T>(int Track, int Order, int MeasureIndex, QuarterFraction Offset, T Value);

    private sealed record Placed<T>(Tick Tick, int MeasureIndex, T Value);

    private sealed record PartNote(
        int MeasureIndex,
        int NoteIndex,
        QuarterFraction Offset,
        QuarterFraction Duration,
        int Pitch,
        int? Velocity,
        int Staff,
        string Voice,
        bool TieStart,
        bool TieStop,
        MusicXmlTuplet? Tuplet,
        AccentKind? Accent);

    /// <summary>A note with the written notes tied to it: exact onset and end, and how many were merged.</summary>
    private sealed record TiedNote(PartNote Note, QuarterFraction Onset, QuarterFraction End, int Written);

    private sealed record PartGraceNote(int MeasureIndex, int NoteIndex, QuarterFraction Offset, int Pitch, int Staff, string Voice, bool IsSlashed);

    private sealed record PartRest(int MeasureIndex, int NoteIndex, QuarterFraction Offset, QuarterFraction Duration, int Staff, string Voice, bool IsMeasureRest);

    /// <summary>A slur start or stop on a note, at the note's onset.</summary>
    private sealed record PartSlurMark(int MeasureIndex, QuarterFraction Offset, int Number, bool IsStart);

    /// <summary>A repeat sign on the left (start) or right (end) bar line of a measure.</summary>
    private sealed record PartRepeat(int MeasureIndex, bool IsAtEnd, MusicXmlRepeatDirection Direction);

    private sealed record MeasureInfo(string? Number, bool IsImplicit, QuarterFraction Length);

    /// <summary>Walks one part: keeps a position cursor per measure and collects notes, attributes and directions.</summary>
    private sealed class PartReader(int index, string id, string? name, string? instrumentName, ImportIssueCollector issues)
    {
        private MusicXmlDivisions? _divisions;
        private int _transpose;
        private int? _velocity;
        private int _order;

        private int _measureIndex;
        private string? _measureNumber;
        private int _noteIndex;
        private QuarterFraction _cursor;
        private QuarterFraction _measureLength;
        private QuarterFraction _lastOnset;

        public int Index => index;

        public string Id => id;

        public string? Name => name;

        public string? InstrumentName => instrumentName;

        public int Staves { get; private set; } = 1;

        public List<MeasureInfo> Measures { get; } = [];

        public List<PartNote> Notes { get; } = [];

        public List<PartGraceNote> GraceNotes { get; } = [];

        public List<PartRest> Rests { get; } = [];

        public List<PartSlurMark> Slurs { get; } = [];

        public List<PartRepeat> Repeats { get; } = [];

        public List<Timed<decimal>> Tempos { get; } = [];

        public List<Timed<TimeSignature>> Times { get; } = [];

        public List<Timed<(int Fifths, string? Mode)>> Keys { get; } = [];

        private MusicXmlDivisions Divisions
        {
            get
            {
                if (_divisions is null)
                {
                    issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.DivisionsDefault, index);
                    _divisions = new MusicXmlDivisions(1);
                }

                return _divisions;
            }
        }

        public void Read(XElement part)
        {
            foreach (XElement measure in part.Elements("measure"))
            {
                _measureIndex = Measures.Count;
                _measureNumber = Trimmed(measure.Attribute("number")?.Value);
                _noteIndex = 0;
                _cursor = QuarterFraction.Zero;
                _measureLength = QuarterFraction.Zero;
                _lastOnset = QuarterFraction.Zero;

                foreach (XElement element in measure.Elements())
                {
                    switch (element.Name.LocalName)
                    {
                        case "attributes":
                            ReadAttributes(element);
                            break;
                        case "note":
                            ReadNote(element);
                            _noteIndex++;
                            break;
                        case "backup":
                            if (ReadDuration(element) is QuarterFraction back)
                            {
                                if (back > _cursor)
                                {
                                    issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.BackupBeforeMeasure, index);
                                    _cursor = QuarterFraction.Zero;
                                }
                                else
                                {
                                    _cursor -= back;
                                }
                            }

                            break;
                        case "forward":
                            if (ReadDuration(element) is QuarterFraction forward)
                            {
                                Advance(forward);
                            }

                            break;
                        case "direction":
                            ReadDirection(element);
                            break;
                        case "sound":
                            ReadSound(element);
                            break;
                        case "barline":
                            ReadBarline(element);
                            break;
                    }
                }

                bool isImplicit = string.Equals(measure.Attribute("implicit")?.Value.Trim(), "yes", StringComparison.Ordinal);
                Measures.Add(new MeasureInfo(_measureNumber, isImplicit, _measureLength));
            }
        }

        private void Advance(QuarterFraction duration)
        {
            _cursor += duration;
            if (_cursor > _measureLength)
            {
                _measureLength = _cursor;
            }
        }

        private void ReadNote(XElement note)
        {
            if (note.Element("grace") is XElement grace)
            {
                ReadGraceNote(note, grace);
                return;
            }

            if (ReadDuration(note) is not QuarterFraction duration)
            {
                return;
            }

            bool isChord = note.Element("chord") is not null;
            QuarterFraction onset = isChord ? _lastOnset : _cursor;
            if (!isChord)
            {
                _lastOnset = _cursor;
                Advance(duration);
            }

            if (note.Element("cue") is not null)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.CueNotesIgnored, index);
                return;
            }

            int staff = TryParseInt(note.Element("staff")?.Value, out int s) && s >= 1 ? s : 1;
            string voice = Trimmed(note.Element("voice")?.Value) ?? "1";
            if (note.Element("rest") is XElement rest)
            {
                bool isMeasureRest = string.Equals(rest.Attribute("measure")?.Value.Trim(), "yes", StringComparison.Ordinal);
                Rests.Add(new PartRest(_measureIndex, _noteIndex, onset, duration, staff, voice, isMeasureRest));
                Staves = Math.Max(Staves, staff);
                return;
            }

            if (note.Element("unpitched") is not null)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.UnpitchedIgnored, index);
                return;
            }

            if (ReadPitch(note.Element("pitch")) is not int pitch)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.PitchInvalid, index);
                return;
            }

            Staves = Math.Max(Staves, staff);
            int? velocity = ToVelocity(note.Attribute("dynamics")?.Value) ?? _velocity;
            (bool tieStart, bool tieStop) = ReadTies(note);

            Notes.Add(new PartNote(_measureIndex, _noteIndex, onset, duration, pitch, velocity, staff, voice, tieStart, tieStop, ReadTuplet(note), ReadAccent(note)));
            ReadSlurs(note, onset);
        }

        /// <summary>The accent written on a note (S1-A1); a strong accent wins when both are written.</summary>
        private static AccentKind? ReadAccent(XElement note)
        {
            IEnumerable<XElement> articulations = note.Elements("notations").Elements("articulations").Elements();
            return articulations.Any(mark => mark.Name.LocalName == "strong-accent") ? AccentKind.StrongAccent
                : articulations.Any(mark => mark.Name.LocalName == "accent") ? AccentKind.Accent
                : null;
        }

        /// <summary>Slur starts and stops on a note; a <c>continue</c> neither opens nor closes a slur.</summary>
        private void ReadSlurs(XElement note, QuarterFraction onset)
        {
            foreach (XElement slur in note.Elements("notations").Elements("slur"))
            {
                string? type = slur.Attribute("type")?.Value.Trim();
                if (type is not ("start" or "stop"))
                {
                    continue;
                }

                int number = TryParseInt(slur.Attribute("number")?.Value, out int n) ? n : 1;
                Slurs.Add(new PartSlurMark(_measureIndex, onset, number, type == "start"));
            }
        }

        private void ReadBarline(XElement barline)
        {
            if (barline.Element("repeat")?.Attribute("direction")?.Value.Trim() is not string direction)
            {
                return;
            }

            string location = barline.Attribute("location")?.Value.Trim() ?? "right";
            if (location == "middle")
            {
                return;
            }

            MusicXmlRepeatDirection? parsed = direction switch
            {
                "forward" => MusicXmlRepeatDirection.Forward,
                "backward" => MusicXmlRepeatDirection.Backward,
                _ => null,
            };
            if (parsed is { } value)
            {
                Repeats.Add(new PartRepeat(_measureIndex, location == "right", value));
            }
        }

        /// <summary>A grace note takes no time: it is listed at the cursor, before the note it ornaments.</summary>
        private void ReadGraceNote(XElement note, XElement grace)
        {
            if (note.Element("cue") is not null)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.CueNotesIgnored, index);
                return;
            }

            if (note.Element("rest") is not null || note.Element("unpitched") is not null)
            {
                return;
            }

            if (ReadPitch(note.Element("pitch")) is not int pitch)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.PitchInvalid, index);
                return;
            }

            int staff = TryParseInt(note.Element("staff")?.Value, out int s) && s >= 1 ? s : 1;
            Staves = Math.Max(Staves, staff);
            string voice = Trimmed(note.Element("voice")?.Value) ?? "1";
            bool isSlashed = string.Equals(grace.Attribute("slash")?.Value.Trim(), "yes", StringComparison.Ordinal);
            GraceNotes.Add(new PartGraceNote(_measureIndex, _noteIndex, _cursor, pitch, staff, voice, isSlashed));
            ReadSlurs(note, _cursor);
            issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.GraceNotesFlagged, index);
        }

        /// <summary>
        /// Whether a tie starts or stops at the note: the sounding <c>&lt;tie&gt;</c>, or the notated
        /// <c>&lt;tied&gt;</c> when the file has only that.
        /// </summary>
        private static (bool Start, bool Stop) ReadTies(XElement note)
        {
            var types = note.Elements("tie").Select(tie => tie.Attribute("type")?.Value.Trim()).ToList();
            if (types.Count == 0)
            {
                types = note.Elements("notations").Elements("tied").Select(tied => tied.Attribute("type")?.Value.Trim()).ToList();
            }

            bool isContinue = types.Contains("continue");
            return (types.Contains("start") || isContinue, types.Contains("stop") || isContinue);
        }

        private static MusicXmlTuplet? ReadTuplet(XElement note)
        {
            XElement? modification = note.Element("time-modification");
            return modification is not null
                && TryParseInt(modification.Element("actual-notes")?.Value, out int actual) && actual >= 1
                && TryParseInt(modification.Element("normal-notes")?.Value, out int normal) && normal >= 1
                && actual != normal
                ? new MusicXmlTuplet(actual, normal)
                : null;
        }

        private int? ReadPitch(XElement? pitch)
        {
            string? step = Trimmed(pitch?.Element("step")?.Value);
            if (pitch is null || step is not { Length: 1 } || step[0] is < 'A' or > 'G' || !TryParseInt(pitch.Element("octave")?.Value, out int octave))
            {
                return null;
            }

            int alter = 0;
            if (pitch.Element("alter") is XElement alterElement)
            {
                if (!TryParseDecimal(alterElement.Value, out decimal exact))
                {
                    return null;
                }

                alter = (int)Math.Round(exact, MidpointRounding.AwayFromZero);
                if (alter != exact)
                {
                    issues.Add(ImportIssueKind.Assumed, MusicXmlIssueCodes.MicrotoneRounded, index);
                }
            }

            long midi = ((octave + 1L) * 12) + s_stepSemitones[step[0] - 'A'] + alter + _transpose;
            return midi is >= 0 and <= Pitch.MaxMidiNumber ? (int)midi : null;
        }

        private void ReadAttributes(XElement attributes)
        {
            if (attributes.Element("divisions") is XElement divisions)
            {
                if (long.TryParse(divisions.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value >= 1)
                {
                    _divisions = new MusicXmlDivisions(value);
                }
                else if (TryParseDecimal(divisions.Value, out decimal fractional) && fractional > 0)
                {
                    throw FractionalTiming();
                }
                else
                {
                    issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.DivisionsInvalid, index);
                }
            }

            if (attributes.Element("staves") is XElement staves && TryParseInt(staves.Value, out int count) && count >= 1)
            {
                Staves = Math.Max(Staves, count);
            }

            if (attributes.Element("transpose") is XElement transpose)
            {
                TryParseInt(transpose.Element("chromatic")?.Value, out int chromatic);
                TryParseInt(transpose.Element("octave-change")?.Value, out int octaves);
                _transpose = chromatic + (12 * octaves);
            }

            if (attributes.Element("key") is XElement key)
            {
                ReadKey(key);
            }

            if (attributes.Element("time") is XElement time)
            {
                ReadTime(time);
            }
        }

        private void ReadKey(XElement key)
        {
            XElement? fifths = key.Element("fifths");
            if (fifths is null)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaUnsupported, index);
                return;
            }

            if (!TryParseInt(fifths.Value, out int value) || value is < -7 or > 7)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
                return;
            }

            Keys.Add(new Timed<(int, string?)>(index, _order++, _measureIndex, _cursor, (value, Trimmed(key.Element("mode")?.Value))));
        }

        private void ReadTime(XElement time)
        {
            var beats = time.Elements("beats").ToList();
            var beatTypes = time.Elements("beat-type").ToList();
            if (time.Element("senza-misura") is not null)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaUnsupported, index);
                return;
            }

            if (beats.Count == 0 || beats.Count != beatTypes.Count)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
                return;
            }

            var denominators = new List<int>();
            foreach (XElement beatType in beatTypes)
            {
                if (!TryParseInt(beatType.Value, out int denominator))
                {
                    issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
                    return;
                }

                denominators.Add(denominator);
            }

            if (denominators.Distinct().Count() > 1)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaUnsupported, index);
                return;
            }

            // Additive numerators (3+2/8) and repeated pairs with one beat type (3/8 + 2/8) are given as their sum.
            long numerator = 0;
            foreach (string part in beats.SelectMany(beat => beat.Value.Split('+')))
            {
                if (!TryParseInt(part, out int value) || value < 1)
                {
                    issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
                    return;
                }

                numerator += value;
            }

            int den = denominators[0];
            if (numerator > TimeSignature.MaxNumerator || den < 1 || den > TimeSignature.MaxDenominator || !int.IsPow2(den))
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
                return;
            }

            Times.Add(new Timed<TimeSignature>(index, _order++, _measureIndex, _cursor, new TimeSignature((int)numerator, den)));
        }

        private void ReadDirection(XElement direction)
        {
            bool hasSoundTempo = false;
            foreach (XElement sound in direction.Elements("sound"))
            {
                hasSoundTempo |= ReadSound(sound);
            }

            // A metronome mark stands for the tempo only when no <sound tempo> says otherwise.
            if (!hasSoundTempo && direction.Elements("direction-type").Elements("metronome").FirstOrDefault() is XElement metronome)
            {
                ReadMetronome(metronome);
            }
        }

        /// <summary>Reads playback tempo and dynamics; true when the sound sets a tempo.</summary>
        private bool ReadSound(XElement sound)
        {
            if (ToVelocity(sound.Attribute("dynamics")?.Value) is int velocity)
            {
                _velocity = velocity;
            }

            if (sound.Attribute("tempo") is not XAttribute tempo)
            {
                return false;
            }

            if (!TryParseDecimal(tempo.Value, out decimal bpm) || bpm <= 0)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
                return false;
            }

            Tempos.Add(new Timed<decimal>(index, _order++, _measureIndex, _cursor, bpm));
            return true;
        }

        private void ReadMetronome(XElement metronome)
        {
            var units = metronome.Elements("beat-unit").ToList();
            string? perMinute = Trimmed(metronome.Element("per-minute")?.Value);
            if (units.Count != 1 || perMinute is null)
            {
                // Metric modulations (one beat unit equals another) and text marks set no number.
                return;
            }

            // "c. 120" or "120-132": the first number is the tempo.
            int start = perMinute.AsSpan().IndexOfAnyInRange('0', '9');
            if (start < 0)
            {
                return;
            }

            int length = perMinute.AsSpan(start).IndexOfAnyExcept("0123456789.");
            string digits = length < 0 ? perMinute[start..] : perMinute.Substring(start, length);
            decimal? unit = BeatUnitQuarters(Trimmed(units[0].Value), metronome.Elements("beat-unit-dot").Count());
            if (!TryParseDecimal(digits, out decimal value) || value <= 0 || unit is null)
            {
                issues.Add(ImportIssueKind.Dropped, MusicXmlIssueCodes.MetaInvalid, index);
                return;
            }

            Tempos.Add(new Timed<decimal>(index, _order++, _measureIndex, _cursor, value * unit.Value));
        }

        private static decimal? BeatUnitQuarters(string? unit, int dots)
        {
            decimal? quarters = unit switch
            {
                "long" => 16,
                "breve" => 8,
                "whole" => 4,
                "half" => 2,
                "quarter" => 1,
                "eighth" => 0.5m,
                "16th" => 0.25m,
                "32nd" => 0.125m,
                "64th" => 0.0625m,
                _ => null,
            };

            // Each dot adds half of the previous value.
            decimal added = quarters ?? 0;
            for (int i = 0; i < dots && quarters is not null; i++)
            {
                added /= 2;
                quarters += added;
            }

            return quarters;
        }

        private QuarterFraction? ReadDuration(XElement element)
        {
            string? text = Trimmed(element.Element("duration")?.Value);
            if (text is null)
            {
                issues.Add(ImportIssueKind.Repaired, MusicXmlIssueCodes.NoteWithoutDuration, index);
                return null;
            }

            if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long duration))
            {
                return Divisions.ToQuarters(duration);
            }

            if (TryParseDecimal(text, out decimal fractional) && fractional >= 0)
            {
                throw FractionalTiming();
            }

            string where = _measureNumber is null ? $"measure {_measureIndex + 1}" : $"measure {_measureNumber}";
            throw new MusicXmlImportException(
                MusicXmlImportFailure.Damaged,
                $"This MusicXML file is damaged: a duration in {where} of {name ?? id} is not a number, so the notes after it cannot be placed.");
        }

        private static int? ToVelocity(string? dynamics)
        {
            if (!TryParseDecimal(dynamics, out decimal percent) || percent < 0)
            {
                return null;
            }

            decimal velocity = Math.Round(percent * ForteVelocity / 100, MidpointRounding.AwayFromZero);
            return (int)Math.Clamp(velocity, Note.MinVelocity, Note.MaxVelocity);
        }

        private static MusicXmlImportException FractionalTiming() => new(
            MusicXmlImportFailure.Unsupported,
            "This MusicXML file uses fractional durations, which Motiff Lab cannot read yet. Exporting it again from a notation app usually fixes this.");
    }
}