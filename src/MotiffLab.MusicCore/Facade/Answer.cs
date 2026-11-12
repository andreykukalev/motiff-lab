using System.Globalization;
using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// Writes the facade's answers (0020): every answer is one JSON object, written by hand so the
/// layout is fixed and the native build needs no reflection. Ticks are integers at 480 per quarter;
/// enums are lower camel case names.
/// </summary>
internal static partial class Answer
{
    public static string Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            body(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string Error(string code, string message) => Write(json =>
    {
        json.WriteString("error", code);
        json.WriteString("message", message);
    });

    /// <summary>An enum's name in lower camel case: "pitchOrder", "userEdited".</summary>
    public static string Name<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return name.Length > 0 ? char.ToLowerInvariant(name[0]) + name[1..] : name;
    }

    public static void Span(Utf8JsonWriter json, string name, TickSpan span)
    {
        json.WriteStartObject(name);
        json.WriteNumber("onset", span.Onset.Value);
        json.WriteNumber("end", span.End.Value);
        json.WriteEndObject();
    }

    public static void Attribution(Utf8JsonWriter json, Attribution attribution)
    {
        json.WriteString("provenance", Name(attribution.Provenance));
        json.WriteNumber("confidence", Math.Round(attribution.Confidence, 3));
    }

    public static void Note(Utf8JsonWriter json, Note note)
    {
        json.WriteStartObject();
        json.WriteString("id", note.Id);
        json.WriteNumber("pitch", note.Pitch.MidiNumber);
        json.WriteNumber("onset", note.Span.Onset.Value);
        json.WriteNumber("duration", note.Span.Duration);
        json.WriteNumber("velocity", note.Velocity);
        json.WriteNumber("track", note.Track);
        if (note.Voice is int voice)
        {
            json.WriteNumber("voice", voice);
        }

        if (note.Hand is { } hand)
        {
            json.WriteString("hand", Name(hand.Value));
            json.WriteBoolean("handInferred", hand.Attribution.Provenance == Provenance.Inferred);
        }

        json.WriteString("provenance", Name(note.Attribution.Provenance));
        json.WriteEndObject();
    }

    public static void Notes(Utf8JsonWriter json, string name, IEnumerable<Note> notes)
    {
        json.WriteStartArray(name);
        foreach (Note note in notes)
        {
            Note(json, note);
        }

        json.WriteEndArray();
    }

    public static void Texts(Utf8JsonWriter json, string name, IEnumerable<string> texts)
    {
        json.WriteStartArray(name);
        foreach (string text in texts)
        {
            json.WriteStringValue(text);
        }

        json.WriteEndArray();
    }

    public static void Numbers(Utf8JsonWriter json, string name, IEnumerable<int> numbers)
    {
        json.WriteStartArray(name);
        foreach (int number in numbers)
        {
            json.WriteNumberValue(number);
        }

        json.WriteEndArray();
    }

    public static void Ticks(Utf8JsonWriter json, string name, IEnumerable<Tick> ticks)
    {
        json.WriteStartArray(name);
        foreach (Tick tick in ticks)
        {
            json.WriteNumberValue(tick.Value);
        }

        json.WriteEndArray();
    }

    public static void Filter(Utf8JsonWriter json, string name, SelectionFilter filter)
    {
        json.WriteStartObject(name);
        if (filter.Hand is { } hand)
        {
            json.WriteString("hand", Name(hand));
        }

        if (filter.Track is int track)
        {
            json.WriteNumber("track", track);
        }

        if (filter.Voice is int voice)
        {
            json.WriteNumber("voice", voice);
        }

        json.WriteEndObject();
    }

    public static void Keep(Utf8JsonWriter json, string name, IEnumerable<KeepProperty> keep)
    {
        json.WriteStartArray(name);
        foreach (KeepProperty property in keep.Order())
        {
            json.WriteStringValue(Name(property));
        }

        json.WriteEndArray();
    }

    public static void KeySignature(Utf8JsonWriter json, string name, KeySignature? key)
    {
        if (key is null)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteNumber("at", key.At.Value);
        json.WriteNumber("fifths", key.Fifths);
        if (key.Mode is { } mode)
        {
            json.WriteString("mode", Name(mode));
        }

        json.WriteEndObject();
    }

    public static void MusicalKey(Utf8JsonWriter json, string name, MusicalKey key)
    {
        json.WriteStartObject(name);
        json.WriteString("name", key.Name);
        json.WriteNumber("fifths", key.Fifths);
        json.WriteString("mode", Name(key.Mode));
        json.WriteEndObject();
    }

    public static void Passage(Utf8JsonWriter json, string name, TickSpan span, int firstBar, int lastBar)
    {
        json.WriteStartObject(name);
        json.WriteNumber("onset", span.Onset.Value);
        json.WriteNumber("end", span.End.Value);
        json.WriteNumber("firstBar", firstBar);
        json.WriteNumber("lastBar", lastBar);
        json.WriteEndObject();
    }

    public static void Piece(Utf8JsonWriter json, ImportedPiece piece, IReadOnlyList<Note> notes, bool withNotes, Words words = default)
    {
        json.WriteString("fileName", piece.FileName);
        json.WriteString("format", Name(piece.Format));
        json.WriteNumber("noteCount", notes.Count);
        json.WriteNumber("barCount", MotifSelector.LastBar(piece.Meter, piece.End));
        json.WriteNumber("end", piece.End.Value);
        json.WriteBoolean("heard", Transcribed.IsHeard(piece));

        json.WriteStartArray("tracks");
        foreach (ImportedTrack track in piece.Tracks)
        {
            json.WriteStartObject();
            json.WriteNumber("index", track.Index);
            json.WriteString("label", track.Label);
            if (track.Name is { } trackName)
            {
                json.WriteString("name", trackName);
            }

            if (track.InstrumentName is { } instrument)
            {
                json.WriteString("instrument", instrument);
            }

            json.WriteNumber("noteCount", track.NoteCount);
            json.WriteString("hands", Name(track.Hands));
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("keys");
        foreach (KeySignature key in piece.Keys)
        {
            json.WriteStartObject();
            json.WriteNumber("at", key.At.Value);
            json.WriteNumber("fifths", key.Fifths);
            if (key.Mode is { } mode)
            {
                json.WriteString("mode", Name(mode));
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartObject("report");
        json.WriteString("format", piece.Report.Format);
        json.WriteNumber("tracks", piece.Report.TrackCount);
        json.WriteNumber("notes", piece.Report.NoteCount);
        json.WriteNumber("measures", piece.Report.MeasureCount);
        json.WriteStartArray("issues");
        foreach (ImportIssue issue in piece.Report.Issues)
        {
            json.WriteStartObject();
            json.WriteString("kind", Name(issue.Kind));
            json.WriteString("code", issue.Code);
            if (issue.Track is int track)
            {
                json.WriteNumber("track", track);
            }

            json.WriteNumber("count", issue.Count);
            json.WriteString("message", issue.Text(words));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        if (!withNotes)
        {
            return;
        }

        json.WriteStartArray("bars");
        foreach (Bar bar in piece.Meter.BarsBefore(piece.End))
        {
            json.WriteStartObject();
            json.WriteNumber("number", bar.Number);
            json.WriteNumber("onset", bar.Span.Onset.Value);
            json.WriteNumber("end", bar.Span.End.Value);
            json.WriteNumber("numerator", bar.Signature.Numerator);
            json.WriteNumber("denominator", bar.Signature.Denominator);
            json.WriteBoolean("pickup", bar.IsPickup);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("startChoices");
        foreach (StartChoice choice in piece.StartChoices)
        {
            json.WriteStartObject();
            json.WriteString("label", choice.Label);
            Filter(json, "filter", choice.Filter);
            json.WriteBoolean("inferred", choice.IsInferred);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Notes(json, "notes", notes);
    }

    public static void Project(Utf8JsonWriter json, string name, Project project)
    {
        json.WriteStartObject(name);
        json.WriteString("id", project.Id);
        json.WriteString("fileName", project.FileName);
        json.WriteString("createdAt", project.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("lastOpened", project.LastOpened.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("origin", project.Scan is not null ? "scanned" : project.Transcription is not null ? "transcribed" : "imported");
        json.WriteNumber("motifs", project.Motifs.Count);
        json.WriteNumber("variations", project.Variations.Count);
        json.WriteNumber("corrections", project.Edits.Edits.Count);
        json.WriteNumber("resumePoints", project.ResumePoints.Count);
        json.WriteNumber("ideas", project.Observations.Count);
        json.WriteNumber("marks", project.Marks.Count);
        json.WriteNumber("bookmarks", project.Bookmarks.Count);
        json.WriteNumber("openChecks", project.OpenChecks.Count);
        Details(json, project.Details);
        // Within contract 4, for the library's cards (0018 section 9): the bars still to check, whether the piece has notes
        // and a PDF (0023), and the resume point worked from last.
        json.WriteNumber("checkBars", project.Summary.CheckBars);
        json.WriteBoolean("hasNotes", project.HasNotes);
        json.WriteBoolean("document", project.Document is not null);
        // Within contract 12 (version 0.3): the originals and where their bytes are, for the iPad's badges.
        Originals(json, project);
        // Within contract 4 (2026-11-05): notes read from a PDF piece name it, and where each of its pages starts in the
        // notes, in ticks, for PDF | Notes.
        if (project.ReadFrom is { } readFrom)
        {
            json.WriteString("readFrom", readFrom.ProjectId);
            json.WriteStartArray("readFromPages");
            foreach (Tick start in readFrom.Pages.Starts)
            {
                json.WriteNumberValue(start.Value);
            }

            json.WriteEndArray();
        }

        // Contract 6 (testing item 16): a piece with notes and a PDF kept inside it, where each of the PDF's pages starts
        // in the notes, in ticks, for PDF | Notes within the one piece.
        if (project.HasNotes && project.DocumentPages is { } pages)
        {
            json.WriteStartArray("documentPages");
            foreach (Tick start in pages.Starts)
            {
                json.WriteNumberValue(start.Value);
            }

            json.WriteEndArray();
        }

        if (project.RecentResumePoints.FirstOrDefault() is { } latest)
        {
            json.WriteStartObject("latest");
            json.WriteString("id", latest.Id);
            if (latest.Name is { } pointName)
            {
                json.WriteString("name", pointName);
            }

            json.WriteNumber("at", latest.At.Value);
            json.WriteString("lastActive", latest.LastActive.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("latest");
        }

        json.WriteEndObject();
    }

    /// <summary>The three levels (0018, section 3; contract 3): the level shown, every level with its count, the edits per level and the units of the level shown.</summary>
    public static void Phrases(Utf8JsonWriter json, PhraseLevels levels, PhraseLevel shown, Words words)
    {
        json.WriteString("level", PhraseLevels.Name(shown));
        json.WriteStartArray("levels");
        foreach (PhraseLevel level in PhraseLevels.All)
        {
            json.WriteStartObject();
            json.WriteString("id", PhraseLevels.Name(level));
            json.WriteBoolean("available", true);
            json.WriteNumber("count", levels.Units(level).Count);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteString("evidence", Name(levels.Phrases.Evidence));
        json.WriteStartObject("edits");
        foreach (PhraseLevel level in PhraseLevels.All)
        {
            json.WriteStartObject(PhraseLevels.Name(level));
            Ticks(json, "added", levels.Edits[level].Added);
            Ticks(json, "removed", levels.Edits[level].Removed);
            json.WriteEndObject();
        }

        json.WriteEndObject();

        json.WriteStartArray("units");
        foreach (PhraseUnit unit in levels.Units(shown))
        {
            Unit(json, levels, unit, words);
        }

        json.WriteEndArray();
    }

    public static void Unit(Utf8JsonWriter json, PhraseLevels levels, PhraseUnit unit, Words words)
    {
        json.WriteStartObject();
        json.WriteNumber("number", unit.Number);
        json.WriteString("level", PhraseLevels.Name(unit.Level));
        json.WriteString("name", PhraseLevels.Describe(unit.Level, words));
        json.WriteNumber("firstBar", unit.FirstBar);
        json.WriteNumber("lastBar", unit.LastBar);
        json.WriteNumber("onset", unit.Span.Onset.Value);
        json.WriteNumber("end", unit.Span.End.Value);
        Span(json, "selectable", levels.Selectable(unit));
        json.WriteBoolean("long", unit.IsLong);
        if (levels.Parent(unit) is { } parent)
        {
            json.WriteNumber("parent", parent.Number);
        }
        else
        {
            json.WriteNull("parent");
        }

        json.WriteStartObject("start");
        json.WriteNumber("at", unit.Span.Onset.Value);
        Texts(json, "evidence", unit.Evidence);
        Attribution(json, unit.Start);
        json.WriteString("description", unit.Description);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    /// <summary>
    /// A selection: its span, its bars and, since #349 (within contract 12), the beat of the first bar it starts on and the
    /// beat of the last bar it ends in ("bar 5, beat 2 → bar 8, beat 3"), counted as <see cref="MeterMap.ToPosition"/> counts them.
    /// </summary>
    public static void Selection(Utf8JsonWriter json, string name, MotifSelection selection, KeySignature? key, MeterMap meter)
    {
        json.WriteStartObject(name);
        json.WriteNumber("onset", selection.Span.Onset.Value);
        json.WriteNumber("end", selection.Span.End.Value);
        json.WriteNumber("firstBar", selection.FirstBar);
        json.WriteNumber("lastBar", selection.LastBar);
        json.WriteNumber("firstBeat", meter.ToPosition(selection.Span.Onset).Beat);
        json.WriteNumber("lastBeat", meter.ToPosition(new Tick(Math.Max(selection.Span.Onset.Value, selection.Span.End.Value - 1))).Beat);
        json.WriteNumber("barCount", selection.BarCount);
        Filter(json, "filter", selection.Filter);
        Texts(json, "noteIds", selection.NoteIds);
        json.WriteBoolean("handInferred", selection.HandInferred);
        KeySignature(json, "keySignature", key);
        json.WriteEndObject();
    }

    public static void Facts(Utf8JsonWriter json, string name, IEnumerable<MotifFact> facts, Func<MotifFact, IReadOnlyList<TryChip>>? chips = null, Words words = default)
    {
        json.WriteStartArray(name);
        foreach (MotifFact fact in facts)
        {
            json.WriteStartObject();
            json.WriteString("id", fact.Id);
            json.WriteString("lens", Name(fact.Lens));
            json.WriteString("certainty", Name(fact.Certainty));
            json.WriteString("text", fact.Text);
            if (fact.Brief is { } brief)
            {
                json.WriteString("brief", brief);
            }

            json.WriteString("basis", fact.Basis);
            Doubt(json, fact.Doubt);
            json.WriteStartArray("places");
            foreach (FactPlace place in fact.Places)
            {
                json.WriteStartObject();
                json.WriteString("label", place.Label);
                json.WriteNumber("onset", place.Span.Onset.Value);
                json.WriteNumber("end", place.Span.End.Value);
                Texts(json, "noteIds", place.NoteIds);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (chips is not null)
            {
                Chips(json, "tryChips", chips(fact), words);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// "doubt": why a chord, key or fact rests on recognised notes still to check (#352), written only when it does, so an
    /// answer about imported notes reads as before (contract 11).
    /// </summary>
    private static void Doubt(Utf8JsonWriter json, string? doubt)
    {
        if (doubt is not null)
        {
            json.WriteString("doubt", doubt);
        }
    }

    /// <summary>Try chips (0018, section 7): id, operation, label in the language, Keep preset and the step's parameters in the project file's shape.</summary>
    public static void Chips(Utf8JsonWriter json, string name, IEnumerable<TryChip> chips, Words words)
    {
        json.WriteStartArray(name);
        foreach (TryChip chip in chips)
        {
            json.WriteStartObject();
            json.WriteString("id", chip.Id);
            json.WriteString("label", chip.LabelIn(words));
            json.WriteString("keepPreset", chip.KeepPresetId);
            ProjectFile.WriteStep(json, new SavedStep(chip.OperationId, chip.Parameters!));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// The circle of fifths for the selection (<see cref="KeyCircle"/>): the twelve places, the key with its certainty and
    /// the key that fits almost as well, its neighbours, the chords on the sectors of their roots, the notes outside the
    /// scale, the scale to hear and the lines that say what it shows; null when no key was read.
    /// </summary>
    public static void Circle(Utf8JsonWriter json, KeyCircleView? circle, Tick at = default, Tick? end = null, string name = "circle")
    {
        if (circle is null)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        MusicalKey(json, "key", circle.Key);
        json.WriteString("name", circle.Name);
        json.WriteString("certainty", Name(circle.Certainty));
        json.WriteNumber("position", circle.Position);
        if (circle.RunnerUp is { } runnerUp)
        {
            MusicalKey(json, "runnerUp", runnerUp);
            json.WriteNumber("runnerUpPosition", circle.RunnerUpPosition ?? 0);
            json.WriteString("runnerUpNote", circle.RunnerUpNote);
        }

        Sectors(json, circle.Sectors);
        json.WriteStartArray("neighbours");
        foreach (CircleNeighbour neighbour in circle.Neighbours)
        {
            json.WriteStartObject();
            json.WriteString("relation", Name(neighbour.Relation));
            MusicalKey(json, "key", neighbour.Key);
            json.WriteNumber("position", neighbour.Position);
            json.WriteString("name", neighbour.Name);
            json.WriteString("label", neighbour.Label);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("chords");
        foreach (CircleChord chord in circle.Chords)
        {
            json.WriteStartObject();
            json.WriteNumber("number", chord.Number);
            json.WriteString("symbol", chord.Symbol);
            json.WriteString("degree", chord.Degree);
            json.WriteNumber("position", chord.Position);
            json.WriteString("ring", Name(chord.Ring));
            json.WriteString("function", Name(chord.Function));
            json.WriteString("role", chord.Role);
            CirclePlace(json, "place", chord.Place);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        if (circle.Outside is { } outside)
        {
            CirclePlace(json, "outside", outside);
        }

        Numbers(json, "scale", circle.ScalePitches);
        json.WriteStartArray("scaleNotes");
        foreach (Note note in circle.ScaleNotes(at, end))
        {
            json.WriteStartObject();
            json.WriteNumber("pitch", note.Pitch.MidiNumber);
            json.WriteNumber("onset", note.Span.Onset.Value);
            json.WriteNumber("end", note.Span.End.Value);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Texts(json, "lines", circle.Lines);
        Texts(json, "zones", circle.Zones.Select(static zone => Name(zone)));
        json.WriteEndObject();
    }

    /// <summary>The circle's twelve places: each with its signature's fifths, its major and minor keys and the signature in short.</summary>
    public static void Sectors(Utf8JsonWriter json, IEnumerable<CircleSector> sectors)
    {
        json.WriteStartArray("sectors");
        foreach (CircleSector sector in sectors)
        {
            json.WriteStartObject();
            json.WriteNumber("position", sector.Position);
            json.WriteNumber("fifths", sector.Fifths);
            json.WriteString("major", sector.Major);
            json.WriteString("minor", sector.Minor);
            json.WriteString("signature", sector.Signature);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void CirclePlace(Utf8JsonWriter json, string name, FactPlace place)
    {
        json.WriteStartObject(name);
        json.WriteString("label", place.Label);
        json.WriteNumber("onset", place.Span.Onset.Value);
        json.WriteNumber("end", place.Span.End.Value);
        Texts(json, "noteIds", place.NoteIds);
        json.WriteEndObject();
    }

    /// <summary>
    /// What was understood about the selection: the motif's analysis, the structure around it, its character and
    /// its place among the sentences, each fact with its Try chips (0018, section 7), then the three strongest facts
    /// for the Insights strip and the one to hear first (preparation item 9).
    /// </summary>
    public static void Understanding(Utf8JsonWriter json, InvitationContext context, IReadOnlyList<MotifFact> sentence, Func<MotifFact, IReadOnlyList<TryChip>> chips)
    {
        Words words = context.Words;
        MotifAnalysis motif = context.Motif;
        json.WriteStartObject("motif");
        Texts(json, "line", motif.Line.Select(note => note.Id));
        json.WriteBoolean("lineIsTopNotes", motif.LineIsTopNotes);
        Numbers(json, "intervals", motif.Intervals);
        Texts(json, "contour", motif.Contour.Select(Name));
        if (motif.Key is { } reading)
        {
            json.WriteStartObject("key");
            MusicalKey(json, "key", reading.Key);
            json.WriteString("certainty", Name(reading.Certainty));
            if (reading.RunnerUp is { } runnerUp)
            {
                MusicalKey(json, "runnerUp", runnerUp);
            }

            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("key");
        }

        Facts(json, "facts", motif.Facts, chips, words);
        json.WriteEndObject();

        StructureAnalysis structure = context.Structure;
        json.WriteStartObject("structure");
        json.WriteStartArray("recurrences");
        foreach (Recurrence recurrence in structure.Recurrences)
        {
            json.WriteStartObject();
            json.WriteNumber("onset", recurrence.Span.Onset.Value);
            json.WriteNumber("end", recurrence.Span.End.Value);
            json.WriteNumber("firstBar", recurrence.FirstBar);
            json.WriteNumber("lastBar", recurrence.LastBar);
            json.WriteNumber("transposition", recurrence.Transposition);
            Texts(json, "noteIds", recurrence.NoteIds);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("phrases");
        foreach (PhraseSpan phrase in structure.Phrases)
        {
            json.WriteStartObject();
            json.WriteNumber("onset", phrase.Span.Onset.Value);
            json.WriteNumber("end", phrase.Span.End.Value);
            json.WriteNumber("firstBar", phrase.FirstBar);
            json.WriteNumber("lastBar", phrase.LastBar);
            if (phrase.Start is { } start)
            {
                Attribution(json, start);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        Numbers(json, "newMaterialBars", structure.NewMaterialBars);
        Facts(json, "facts", structure.Facts, chips, words);
        json.WriteEndObject();

        Facts(json, "character", context.Character, chips, words);
        Harmony(json, context.Harmony, motif.Key?.Key, chips, words);
        Facts(json, "sentence", sentence, chips, words);

        MotifFact[] all = [.. motif.Facts, .. structure.Facts, .. context.Character, .. context.Harmony.Facts, .. sentence];
        Texts(json, "strongest", FactRanking.Strongest(all).Select(fact => fact.Id));
        Texts(json, "main", FactRanking.Main(all).Select(fact => fact.Id));
        Texts(json, "inStream", FactRanking.InStream(all).Select(fact => fact.Id));
        if (FactRanking.KeyObservation(all) is { } key)
        {
            json.WriteStartObject("keyObservation");
            json.WriteString("id", key.Id);
            json.WriteString("reason", FactRanking.Reason(key, words));
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("keyObservation");
        }
    }

    /// <summary>
    /// The selection's harmony (S1-A3): each chord with its symbol, root, quality, bass, inversion, confidence and
    /// alternatives, every one inferred; the bass line's note ids; and the Harmony facts with their Try chips.
    /// </summary>
    public static void Harmony(Utf8JsonWriter json, HarmonyAnalysis harmony, MusicalKey? key, Func<MotifFact, IReadOnlyList<TryChip>> chips, Words words)
    {
        MusicalKey spelling = key ?? new MusicalKey(0, KeyMode.Major);
        json.WriteStartObject("harmony");
        json.WriteStartArray("chords");
        foreach (ChordSpan chord in harmony.Chords)
        {
            json.WriteStartObject();
            json.WriteNumber("onset", chord.Span.Onset.Value);
            json.WriteNumber("end", chord.Span.End.Value);
            json.WriteString("symbol", chord.Symbol(spelling));
            json.WriteNumber("root", chord.Chord.Root);
            json.WriteString("quality", Name(chord.Chord.Quality));
            json.WriteNumber("bass", chord.Bass.MidiNumber);
            json.WriteNumber("inversion", chord.Inversion);
            json.WriteNumber("confidence", chord.Confidence);
            json.WriteString("certainty", Name(chord.IsUncertain ? FactCertainty.Uncertain : FactCertainty.Inferred));
            Doubt(json, chord.Doubt);
            Texts(json, "alternatives", chord.Alternatives.Select(alternative => alternative.Symbol(spelling, chord.Bass.PitchClass)));
            Texts(json, "noteIds", chord.NoteIds);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Texts(json, "bass", harmony.BassLine.Select(static note => note.Id));
        Facts(json, "facts", harmony.Facts, chips, words);
        json.WriteEndObject();
    }

    /// <summary>
    /// Piece DNA (0018 section 11): every lane with its status (<c>notYet</c>, <c>partial</c>, <c>ready</c>), so the
    /// screen shows an empty state for a lane not analysed yet.
    /// </summary>
    public static void Dna(Utf8JsonWriter json, PieceDna dna, Words words, Tick end = default, MeterMap? meter = null)
    {
        json.WriteString("status", Name(dna.Status));
        json.WriteString("summary", dna.Summary);
        json.WriteNumber("firstBar", dna.FirstBar);
        json.WriteNumber("lastBar", dna.LastBar);
        json.WriteStartArray("barStarts");
        foreach (long start in dna.BarStarts)
        {
            json.WriteNumberValue(start);
        }

        json.WriteEndArray();

        json.WriteStartObject("form");
        json.WriteString("status", Name(dna.FormStatus));
        json.WriteStartArray("sections");
        foreach (DnaSection section in dna.Form)
        {
            json.WriteStartObject();
            json.WriteString("id", section.Id);
            json.WriteString("label", section.Label);
            Passage(json, "passage", section.Span, section.FirstBar, section.LastBar);
            Span(json, "selectable", meter is null ? section.Span : FormAnalyzer.Selectable(section, meter));
            Texts(json, "sharesWith", section.SharesWith);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteStartObject("ideas");
        json.WriteString("status", Name(dna.IdeasStatus));
        json.WriteStartArray("items");
        foreach (DnaIdea idea in dna.Ideas)
        {
            json.WriteStartObject();
            json.WriteString("id", idea.Id);
            json.WriteBoolean("fromSelection", idea.FromSelection);
            json.WriteString("title", idea.Title);
            json.WriteString("summary", idea.Summary);
            Passage(json, "passage", idea.Span, idea.FirstBar, idea.LastBar);
            Texts(json, "noteIds", idea.NoteIds);
            json.WriteStartArray("occurrences");
            foreach (IdeaOccurrence occurrence in idea.Occurrences)
            {
                json.WriteStartObject();
                json.WriteString("kind", Name(occurrence.Kind));
                Passage(json, "passage", occurrence.Span, occurrence.FirstBar, occurrence.LastBar);
                json.WriteNumber("transposition", occurrence.Transposition);
                Texts(json, "noteIds", occurrence.NoteIds);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteStartObject("keys");
        json.WriteString("status", Name(dna.KeysStatus));
        json.WriteStartArray("spans");
        foreach (DnaKey key in dna.Keys)
        {
            json.WriteStartObject();
            MusicalKey(json, "key", key.Key);
            json.WriteString("label", key.Key.NameIn(words));
            json.WriteBoolean("modeInferred", key.ModeInferred);
            json.WriteBoolean("keyInferred", key.KeyInferred);
            Doubt(json, key.Doubt);
            Passage(json, "passage", key.Span, key.FirstBar, key.LastBar);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteStartObject("newMaterial");
        json.WriteString("status", Name(dna.NewMaterialStatus));
        Numbers(json, "bars", dna.NewMaterialBars);
        json.WriteEndObject();

        json.WriteStartObject("harmony");
        json.WriteString("status", Name(dna.HarmonyStatus));
        json.WriteStartArray("bars");
        foreach (DnaChord chord in dna.Harmony)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", chord.Bar);
            json.WriteString("symbol", chord.Symbol);
            json.WriteBoolean("clear", chord.Clear);
            json.WriteString("degree", chord.Degree);
            json.WriteString("function", Name(chord.Function));
            Doubt(json, chord.Doubt);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("cadences");
        foreach (DnaCadence cadence in dna.Cadences)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", cadence.Bar);
            json.WriteNumber("at", cadence.At.Value);
            json.WriteString("kind", cadence.Kind == CloseKind.Full ? "full" : "half");
            json.WriteString("explanation", cadence.Explanation);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteStartArray("circles");
        foreach (DnaCircle circle in dna.Circles)
        {
            json.WriteStartObject();
            json.WriteString("id", circle.Id);
            json.WriteString("label", circle.Label);
            Passage(json, "passage", circle.Span, circle.FirstBar, circle.LastBar);
            if (circle.From is { } from)
            {
                MusicalKey(json, "from", from);
                json.WriteString("fromName", from.NameIn(words));
                json.WriteNumber("fromPosition", circle.FromPosition ?? 0);
            }

            Circle(json, circle.Circle, circle.Span.Onset, end == default ? null : end);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        Levels(json, "density", dna.DensityStatus, dna.Density);

        json.WriteStartObject("register");
        json.WriteString("status", Name(dna.DensityStatus));
        json.WriteStartArray("bars");
        foreach (DnaRange range in dna.Register)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", range.Bar);
            json.WriteNumber("low", range.Low);
            json.WriteNumber("high", range.High);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        Levels(json, "dynamics", dna.DynamicsStatus, dna.Dynamics);
    }

    private static void Levels(Utf8JsonWriter json, string name, DnaStatus status, IReadOnlyList<DnaBarLevel> levels)
    {
        json.WriteStartObject(name);
        json.WriteString("status", Name(status));
        json.WriteStartArray("bars");
        foreach (DnaBarLevel level in levels)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", level.Bar);
            json.WriteNumber("value", level.Value);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    public static void Chain(Utf8JsonWriter json, VariationChain chain, Words words)
    {
        Keep(json, "keep", chain.Context.Keep);
        json.WriteNumber("seed", chain.Context.Seed);
        json.WriteStartArray("steps");
        foreach (VariationStep step in chain.Steps)
        {
            Step(json, step);
        }

        json.WriteEndArray();
        if (chain.Pending is { } pending)
        {
            json.WriteStartObject("pending");
            ProjectFile.WriteStep(json, new SavedStep(pending.Operation.Id, pending.Parameters));
            json.WriteString("explanation", pending.Result.Explanation);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("pending");
        }

        json.WriteNumber("undone", chain.Undone.Count);
        json.WriteBoolean("canUndo", chain.CanUndo);
        json.WriteBoolean("canRedo", chain.CanRedo);
        json.WriteString("acceptDisabledReason", chain.AcceptDisabledReason);
        json.WriteString("editDisabledReason", chain.EditDisabledReason);
        Notes(json, "original", chain.Original);
        Notes(json, "current", chain.Current);
        Notes(json, "heard", chain.Heard);
        Result(json, "result", chain.Pending?.Result ?? chain.Result);

        json.WriteStartArray("operations");
        foreach (IVariationOperation operation in VariationOperations.All.Append(HandEdit.Instance))
        {
            json.WriteStartObject();
            json.WriteString("id", operation.Id);
            json.WriteString("name", operation.NameIn(words));
            json.WriteString("disabledReason", operation.DisabledReason(chain.Context.Keep, words));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void Step(Utf8JsonWriter json, VariationStep step)
    {
        json.WriteStartObject();
        ProjectFile.WriteStep(json, new SavedStep(step.Operation.Id, step.Parameters));
        json.WriteString("explanation", step.Result.Explanation);
        json.WriteEndObject();
    }

    private static void Result(Utf8JsonWriter json, string name, Variation? variation)
    {
        if (variation is null)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteString("operation", variation.Operation);
        Notes(json, "notes", variation.Notes);
        json.WriteStartArray("changes");
        foreach (NoteChange change in variation.Changes)
        {
            json.WriteStartObject();
            json.WritePropertyName("before");
            Note(json, change.Before);
            json.WritePropertyName("after");
            Note(json, change.After);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Notes(json, "added", variation.Added);
        Notes(json, "removed", variation.Removed);
        Checks(json, "checks", variation.Checks);
        Checks(json, "properties", variation.Properties);
        Texts(json, "flags", variation.Flags);
        json.WriteString("explanation", variation.Explanation);
        json.WriteEndObject();
    }

    private static void Checks(Utf8JsonWriter json, string name, IEnumerable<KeepCheck> checks)
    {
        json.WriteStartArray(name);
        foreach (KeepCheck check in checks)
        {
            json.WriteStartObject();
            json.WriteString("property", Name(check.Property));
            json.WriteString("status", Name(check.Status));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    public static void Notation(Utf8JsonWriter json, GeneratedNotation notation, IReadOnlyList<PerformanceLayerNotes?>? versions = null)
    {
        json.WriteString("mei", notation.Mei);
        Staves(json, notation.Staves);
        if (notation.Excerpt is { } excerpt)
        {
            json.WriteStartObject("excerpt");
            json.WriteNumber("firstBar", excerpt.FirstBar);
            json.WriteNumber("lastBar", excerpt.LastBar);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("excerpt");
        }

        json.WriteStartArray("ossias");
        int index = 0;
        foreach (GeneratedOssia ossia in notation.Ossias)
        {
            json.WriteStartObject();
            json.WriteString("label", ossia.Label);
            Staves(json, ossia.Staves);
            json.WriteNumber("noteCount", ossia.NoteCount);
            json.WriteString("idPrefix", ossia.IdPrefix);
            // Another version's ossia: the engraved ids of the notes it adds (plum) and of its likely artefacts (grey).
            if (versions?.ElementAtOrDefault(index) is { } version)
            {
                json.WriteStartArray("added");
                foreach (string id in version.Added)
                {
                    json.WriteStringValue(ossia.IdPrefix + id);
                }

                json.WriteEndArray();
                json.WriteStartArray("artefacts");
                foreach (string id in version.Artefacts)
                {
                    json.WriteStringValue(ossia.IdPrefix + id);
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
            index++;
        }

        json.WriteEndArray();
        json.WriteNumber("noteCount", notation.NoteCount);
        json.WriteNumber("approximatedNoteCount", notation.ApproximatedNoteCount);
        json.WriteNumber("shortenedNoteCount", notation.ShortenedNoteCount);
        json.WriteNumber("lengthenedNoteCount", notation.LengthenedNoteCount);
        json.WriteNumber("cutNoteCount", notation.CutNoteCount);
        json.WriteNumber("graceNoteCount", notation.GraceNoteCount);
        json.WriteNumber("tripletCount", notation.TripletCount);
        json.WriteNumber("slurCount", notation.SlurCount);
        json.WriteNumber("pedalCount", notation.PedalCount);
        json.WriteBoolean("writtenAsHeard", notation.WrittenAsHeard);
    }

    private static void Staves(Utf8JsonWriter json, IReadOnlyList<NotationStaff> staves)
    {
        json.WriteStartArray("staves");
        foreach (NotationStaff staff in staves)
        {
            json.WriteStartObject();
            json.WriteNumber("number", staff.Number);
            json.WriteNumber("track", staff.Track);
            json.WriteString("clef", Name(staff.Clef));
            if (staff.Hand is { } hand)
            {
                json.WriteString("hand", Name(hand));
            }

            json.WriteBoolean("splitInferred", staff.IsSplitInferred);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>The piano roll's data, with the field names the roll reads (Scripts/roll/roll-model.ts, RollPayload).</summary>
    public static void Roll(Utf8JsonWriter json, RollPayload payload, RollNames names)
    {
        static void Numbers(Utf8JsonWriter json, string name, long[] values)
        {
            json.WriteStartArray(name);
            foreach (long value in values)
            {
                json.WriteNumberValue(value);
            }

            json.WriteEndArray();
        }

        static void Texts(Utf8JsonWriter json, string? name, string[] values)
        {
            if (name is null)
            {
                json.WriteStartArray();
            }
            else
            {
                json.WriteStartArray(name);
            }

            foreach (string value in values)
            {
                json.WriteStringValue(value);
            }

            json.WriteEndArray();
        }

        Texts(json, "ids", payload.Ids);
        Numbers(json, "notes", payload.Notes);
        Numbers(json, "bars", payload.Bars);
        json.WriteNumber("end", payload.End);
        json.WriteStartObject("names");
        Texts(json, "notes", names.Notes);
        Numbers(json, "keyTicks", names.KeyTicks);
        json.WriteStartArray("rows");
        foreach (string[] row in names.Rows)
        {
            Texts(json, null, row);
        }

        json.WriteEndArray();
        Texts(json, "short", names.Short);
        json.WriteStartArray("shortRows");
        foreach (string[] row in names.ShortRows)
        {
            Texts(json, null, row);
        }

        json.WriteEndArray();
        Texts(json, "octaves", names.Octaves);
        json.WriteEndObject();
    }

    public static void Playback(Utf8JsonWriter json, PlaybackScore score)
    {
        json.WriteNumber("end", score.End.Value);
        json.WriteStartArray("notes");
        foreach (PlaybackNote note in score.Notes)
        {
            json.WriteStartObject();
            json.WriteNumber("onset", note.Span.Onset.Value);
            json.WriteNumber("duration", note.Span.Duration);
            json.WriteNumber("pitch", note.Pitch);
            json.WriteNumber("velocity", note.Velocity);
            json.WriteNumber("track", note.Track);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("pedal");
        foreach (PlaybackPedal held in score.Pedal)
        {
            json.WriteStartObject();
            json.WriteNumber("onset", held.Span.Onset.Value);
            json.WriteNumber("duration", held.Span.Duration);
            json.WriteNumber("track", held.Track);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("tempos");
        foreach (PlaybackTempo tempo in score.Tempos)
        {
            json.WriteStartObject();
            json.WriteNumber("at", tempo.At.Value);
            json.WriteNumber("microsecondsPerQuarter", tempo.MicrosecondsPerQuarter);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("beats");
        foreach (PlaybackBeat beat in score.Beats)
        {
            json.WriteStartObject();
            json.WriteNumber("at", beat.At.Value);
            json.WriteBoolean("downbeat", beat.IsDownbeat);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("bars");
        foreach (PlaybackBar bar in score.Bars)
        {
            json.WriteStartObject();
            json.WriteNumber("number", bar.Number);
            json.WriteNumber("onset", bar.Span.Onset.Value);
            json.WriteNumber("end", bar.Span.End.Value);
            json.WriteNumber("beatTicks", bar.BeatTicks);
            json.WriteNumber("beatsPerBar", bar.BeatsPerBar);
            json.WriteBoolean("pickup", bar.IsPickup);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Which hands the piece's notes carry and whether any was inferred: {"hands": {right, left, inferred}} (#328).</summary>
    public static void Hands(Utf8JsonWriter json, PlaybackHands hands)
    {
        json.WriteStartObject("hands");
        json.WriteBoolean("right", hands.Right);
        json.WriteBoolean("left", hands.Left);
        json.WriteBoolean("inferred", hands.Inferred);
        json.WriteEndObject();
    }

    public static void Catalogue(Utf8JsonWriter json, Words words)
    {
        json.WriteStartObject("keep");
        json.WriteStartArray("properties");
        foreach (KeepProperty property in Enum.GetValues<KeepProperty>())
        {
            json.WriteStartObject();
            json.WriteString("id", Name(property));
            json.WriteString("label", words.IsRussian ? KeepTable.RussianName(property) : property.ToString());
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("presets");
        foreach (KeepPreset preset in KeepTable.Presets)
        {
            json.WriteStartObject();
            json.WriteString("id", preset.Id);
            json.WriteString("label", preset.Label);
            Keep(json, "keep", preset.Keep);
            Texts(json, "operations", preset.Operations);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteStartArray("operations");
        foreach (IVariationOperation operation in VariationOperations.All.Append(HandEdit.Instance))
        {
            KeepRow? row = KeepTable.Find(operation.Id);
            json.WriteStartObject();
            json.WriteString("id", operation.Id);
            json.WriteString("name", operation.NameIn(words));
            if (row is not null)
            {
                json.WriteString("whatItChanges", words.IsRussian ? row.RussianWhatItChanges : row.WhatItChanges);
                Keep(json, "compatible", row.Compatible);
                json.WriteStartArray("footnotes");
                foreach ((KeepProperty property, KeepFootnote footnote) in row.Footnotes.OrderBy(pair => pair.Key))
                {
                    json.WriteStartObject();
                    json.WriteString("property", Name(property));
                    json.WriteNumber("number", footnote.Number);
                    json.WriteString("text", footnote.Text);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            Texts(json, "scopes", Enum.GetValues<OperationScope>().Where(operation.Supports).Select(Name));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}