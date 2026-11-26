using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>What was understood about the selection (0018, section 7): the selection, its facts with their Try chips, its structure and harmony.</summary>
internal static partial class Answer
{
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
        JsonWrite.Texts(json, "noteIds", selection.NoteIds);
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
            json.WriteString("lens", JsonWrite.Name(fact.Lens));
            json.WriteString("certainty", JsonWrite.Name(fact.Certainty));
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
                PlaceOfFact(json, place);
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

    /// <summary>A place a fact or the circle points to: {"label", "onset", "end", "noteIds"}, as an element of a list.</summary>
    private static void PlaceOfFact(Utf8JsonWriter json, FactPlace place)
    {
        json.WriteStartObject();
        json.WriteString("label", place.Label);
        json.WriteNumber("onset", place.Span.Onset.Value);
        json.WriteNumber("end", place.Span.End.Value);
        JsonWrite.Texts(json, "noteIds", place.NoteIds);
        json.WriteEndObject();
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
        UnderstoodMotif(json, context.Motif, chips, words);
        UnderstoodStructure(json, context.Structure, chips, words);
        Facts(json, "character", context.Character, chips, words);
        Harmony(json, context.Harmony, context.Motif.Key?.Key, chips, words);
        Facts(json, "sentence", sentence, chips, words);
        Ranking(json, [.. context.Motif.Facts, .. context.Structure.Facts, .. context.Character, .. context.Harmony.Facts, .. sentence], words);
    }

    private static void UnderstoodMotif(Utf8JsonWriter json, MotifAnalysis motif, Func<MotifFact, IReadOnlyList<TryChip>> chips, Words words)
    {
        json.WriteStartObject("motif");
        JsonWrite.Texts(json, "line", motif.Line.Select(note => note.Id));
        json.WriteBoolean("lineIsTopNotes", motif.LineIsTopNotes);
        JsonWrite.Numbers(json, "intervals", motif.Intervals);
        JsonWrite.Texts(json, "contour", motif.Contour.Select(JsonWrite.Name));
        if (motif.Key is { } reading)
        {
            json.WriteStartObject("key");
            MusicalKey(json, "key", reading.Key);
            json.WriteString("certainty", JsonWrite.Name(reading.Certainty));
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
    }

    private static void UnderstoodStructure(Utf8JsonWriter json, StructureAnalysis structure, Func<MotifFact, IReadOnlyList<TryChip>> chips, Words words)
    {
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
            JsonWrite.Texts(json, "noteIds", recurrence.NoteIds);
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
        JsonWrite.Numbers(json, "newMaterialBars", structure.NewMaterialBars);
        Facts(json, "facts", structure.Facts, chips, words);
        json.WriteEndObject();
    }

    /// <summary>The three strongest facts for the Insights strip, the main ones, those in the stream and the one to hear first.</summary>
    private static void Ranking(Utf8JsonWriter json, MotifFact[] all, Words words)
    {
        JsonWrite.Texts(json, "strongest", FactRanking.Strongest(all).Select(fact => fact.Id));
        JsonWrite.Texts(json, "main", FactRanking.Main(all).Select(fact => fact.Id));
        JsonWrite.Texts(json, "inStream", FactRanking.InStream(all).Select(fact => fact.Id));
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
            json.WriteString("quality", JsonWrite.Name(chord.Chord.Quality));
            json.WriteNumber("bass", chord.Bass.MidiNumber);
            json.WriteNumber("inversion", chord.Inversion);
            json.WriteNumber("confidence", chord.Confidence);
            json.WriteString("certainty", JsonWrite.Name(chord.IsUncertain ? FactCertainty.Uncertain : FactCertainty.Inferred));
            Doubt(json, chord.Doubt);
            JsonWrite.Texts(json, "alternatives", chord.Alternatives.Select(alternative => alternative.Symbol(spelling, chord.Bass.PitchClass)));
            JsonWrite.Texts(json, "noteIds", chord.NoteIds);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        JsonWrite.Texts(json, "bass", harmony.BassLine.Select(static note => note.Id));
        Facts(json, "facts", harmony.Facts, chips, words);
        json.WriteEndObject();
    }
}