using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>How the phrases are grouped into sentences: their closes read as scale degrees, then the rules in order, with the player's decisions first.</summary>
public sealed partial class SentenceMap
{
    private static PhraseClose ReadClose(Phrase phrase, Note[] notes, KeyReading key, Words words)
    {
        double confidence = key.Certainty == FactCertainty.Uncertain ? CloseConfidence / 2 : CloseConfidence;
        var attribution = new Attribution(Provenance.Inferred, confidence);
        Note[] inside = [.. notes.Where(note => phrase.Span.Contains(note.Span.Onset))];
        if (inside.Length == 0)
        {
            return new PhraseClose(phrase, CloseKind.None, null, null, attribution, null);
        }

        Tick last = inside.Max(note => note.Span.Onset);
        Note[] lastNotes = [.. inside.Where(note => note.Span.Onset == last).OrderByDescending(note => note.Pitch.MidiNumber)];
        Note top = lastNotes[0];
        Note? bass = lastNotes.Length > 1 ? lastNotes[^1] : null;
        int? topDegree = Degree(key.Key, top.Pitch.MidiNumber);
        int? bassDegree = bass is null ? null : Degree(key.Key, bass.Pitch.MidiNumber);
        CloseKind kind = (topDegree, bassDegree) switch
        {
            (1, _) => CloseKind.Full,
            (3 or 5, 1) => CloseKind.Full,
            (5 or 2 or 7, _) => CloseKind.Half,
            (_, 5) => CloseKind.Half,
            _ => CloseKind.None,
        };
        string name = key.Key.Spell(top.Pitch.MidiNumber);
        string place = words.T($"the last notes of phrase {phrase.Number}, {name}", $"последние ноты фразы {phrase.Number}, {name}");
        return new PhraseClose(phrase, kind, name, topDegree, attribution, FactPlace.Of(place, lastNotes));
    }

    /// <summary>The scale degree, 1 to 7, of a note in the key; null for a note outside the scale.</summary>
    private static int? Degree(MusicalKey key, int midiNumber)
    {
        ScalePosition position = ScaleSteps.Locate(key, midiNumber);
        return position.Alteration == 0 ? (((position.Step % ScaleSteps.StepsPerOctave) + ScaleSteps.StepsPerOctave) % ScaleSteps.StepsPerOctave) + 1 : null;
    }

    private static IReadOnlyList<Sentence> Group(IReadOnlyList<Phrase> phrases, IReadOnlyList<PhraseClose> closes, PhraseEdits edits, Words words)
    {
        var sentences = new List<Sentence>();
        var current = new List<Phrase>();
        var reasons = new List<string>();
        var forcedEnds = edits.Added.ToHashSet();
        var forcedRunsOn = edits.Removed.ToHashSet();
        double confidence = 1;
        bool detected = true;
        bool edited = false;
        for (int i = 0; i < phrases.Count; i++)
        {
            Phrase phrase = phrases[i];
            current.Add(phrase);
            Phrase? next = i + 1 < phrases.Count ? phrases[i + 1] : null;
            PhraseClose close = closes[i];
            bool ends;
            if (next is null)
            {
                ends = true;
                reasons.Add(current.Count == 1
                    ? words.T("it ends with the piece", "она заканчивается вместе с произведением")
                    : words.T($"phrase {phrase.Number} ends with the piece", $"фраза {phrase.Number} заканчивается вместе с произведением"));
            }
            else if (forcedEnds.Contains(next.Span.Onset))
            {
                ends = true;
                edited = true;
                reasons.Add(words.T($"you ended it after phrase {phrase.Number}", $"вы завершили его после фразы {phrase.Number}"));
            }
            else if (forcedRunsOn.Contains(next.Span.Onset))
            {
                ends = false;
                edited = true;
                reasons.Add(words.T($"you joined phrase {next.Number} to it", $"вы присоединили к нему фразу {next.Number}"));
            }
            else if (next.Start?.Evidence.Contains(PhraseEvidence.RepeatSign) == true)
            {
                ends = true;
                reasons.Add(words.T($"a repeat sign stands after phrase {phrase.Number}", $"после фразы {phrase.Number} стоит знак репризы"));
            }
            else if (next.Start?.Evidence.Contains(PhraseEvidence.Repetition) == true && current.Count == 1)
            {
                ends = false;
                detected = false;
                confidence = Math.Min(confidence, Math.Min(GroupConfidence, next.Start.Attribution.Confidence));
                reasons.Add(words.T(
                    $"phrase {next.Number} starts where phrase {phrase.Number} comes back note for note, so it reads as the consequent",
                    $"фраза {next.Number} начинается там, где фраза {phrase.Number} возвращается нота в ноту, поэтому читается как ответное построение"));
            }
            else if (close.Kind == CloseKind.Full)
            {
                ends = true;
                detected = false;
                confidence = Math.Min(confidence, Math.Min(GroupConfidence, close.Attribution.Confidence));
                reasons.Add(words.T($"phrase {phrase.Number} ends on the tonic, a full close", $"фраза {phrase.Number} заканчивается на тонике, полная каденция"));
            }
            else
            {
                ends = false;
                detected = false;
                confidence = Math.Min(confidence, Math.Min(GroupConfidence, close.Attribution.Confidence));
                reasons.Add(close.Kind == CloseKind.Half
                    ? words.T($"phrase {phrase.Number} ends on a half close, so it runs on", $"фраза {phrase.Number} заканчивается половинной каденцией, поэтому продолжается")
                    : words.T($"phrase {phrase.Number} reads no close, so it runs on", $"во фразе {phrase.Number} каденция не читается, поэтому она продолжается"));
            }

            if (ends)
            {
                Phrase first = current[0];
                bool startsMarked = first.Number == 1 || first.Start?.Evidence.Contains(PhraseEvidence.RepeatSign) == true || forcedEnds.Contains(first.Span.Onset);
                Attribution found = detected && startsMarked
                    ? edited ? Attribution.UserEdited : new Attribution(Provenance.Detected, MarkedConfidence)
                    : new Attribution(Provenance.Inferred, Math.Min(confidence, GroupConfidence));
                string basis = Words.Capitalize(string.Join("; ", reasons)) + ".";
                sentences.Add(new Sentence(sentences.Count + 1, TickSpan.FromBounds(first.Span.Onset, phrase.Span.End), first.FirstBar, phrase.LastBar, [.. current], found, basis));
                current.Clear();
                reasons.Clear();
                confidence = 1;
                detected = true;
                edited = false;
            }
        }

        return sentences;
    }
}