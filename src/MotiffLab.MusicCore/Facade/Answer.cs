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

    public static void Piece(Utf8JsonWriter json, ImportedPiece piece, IReadOnlyList<Note> notes, bool withNotes)
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
            json.WriteString("message", issue.Message);
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

    public static void Selection(Utf8JsonWriter json, string name, MotifSelection selection, KeySignature? key)
    {
        json.WriteStartObject(name);
        json.WriteNumber("onset", selection.Span.Onset.Value);
        json.WriteNumber("end", selection.Span.End.Value);
        json.WriteNumber("firstBar", selection.FirstBar);
        json.WriteNumber("lastBar", selection.LastBar);
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
            json.WriteString("basis", fact.Basis);
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
        Facts(json, "sentence", sentence, chips, words);

        MotifFact[] all = [.. motif.Facts, .. structure.Facts, .. context.Character, .. sentence];
        Texts(json, "strongest", FactRanking.Strongest(all).Select(fact => fact.Id));
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

    public static void Experiments(Utf8JsonWriter json, string name, IEnumerable<Experiment> experiments)
    {
        json.WriteStartArray(name);
        foreach (Experiment experiment in experiments)
        {
            json.WriteStartObject();
            json.WriteString("id", experiment.Id);
            json.WriteString("topic", Name(experiment.Topic));
            json.WriteString("title", experiment.Title);
            json.WriteString("change", experiment.Change);
            json.WriteString("question", experiment.Question);
            json.WriteString("listeningFocus", experiment.ListeningFocus);
            Texts(json, "factIds", experiment.FactIds);
            Passage(json, "passage", experiment.Passage.Span, experiment.Passage.FirstBar, experiment.Passage.LastBar);
            Filter(json, "filter", experiment.Filter);
            json.WriteString("keepPreset", experiment.KeepPresetId);
            json.WriteStartArray("alternatives");
            foreach (ExperimentAlternative alternative in experiment.Alternatives)
            {
                json.WriteStartObject();
                json.WriteString("label", alternative.Label);
                json.WriteString("keeps", alternative.Keeps);
                json.WriteStartArray("steps");
                foreach (ExperimentStep step in alternative.Steps)
                {
                    json.WriteStartObject();
                    ProjectFile.WriteStep(json, new SavedStep(step.OperationId, step.Parameters));
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
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

    public static void Notation(Utf8JsonWriter json, GeneratedNotation notation)
    {
        json.WriteString("mei", notation.Mei);
        json.WriteStartArray("staves");
        foreach (NotationStaff staff in notation.Staves)
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