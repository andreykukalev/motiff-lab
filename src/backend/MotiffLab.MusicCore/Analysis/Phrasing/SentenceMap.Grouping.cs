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

    /// <summary>The rule that ends a sentence after a phrase or runs it on, in the order the grouping tries them; the player's decisions come first.</summary>
    private enum GroupRule
    {
        PieceEnds,
        PlayerEnded,
        PlayerJoined,
        RepeatSign,
        Consequent,
        FullClose,
        HalfClose,
        NoClose,
    }

    /// <summary>Why the grouping did what it did after phrase <paramref name="Phrase"/>; <paramref name="Next"/> is the phrase after it, 0 at the end of the piece.</summary>
    private readonly record struct GroupReason(GroupRule Rule, int Phrase, int Next);

    private static IReadOnlyList<Sentence> Group(IReadOnlyList<Phrase> phrases, IReadOnlyList<PhraseClose> closes, PhraseEdits edits, Words words)
    {
        var sentences = new List<Sentence>();
        var current = new List<Phrase>();
        var reasons = new List<GroupReason>();
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
            (GroupReason reason, bool ends, double? reading) = After(phrase, next, closes[i], current.Count == 1, forcedEnds, forcedRunsOn);
            reasons.Add(reason);
            edited |= reason.Rule is GroupRule.PlayerEnded or GroupRule.PlayerJoined;
            if (reading is { } read)
            {
                detected = false;
                confidence = Math.Min(confidence, Math.Min(GroupConfidence, read));
            }

            if (ends)
            {
                Phrase first = current[0];
                bool startsMarked = first.Number == 1 || first.Start?.Evidence.Contains(PhraseEvidence.RepeatSign) == true || forcedEnds.Contains(first.Span.Onset);
                Attribution found = detected && startsMarked
                    ? edited ? Attribution.UserEdited : new Attribution(Provenance.Detected, MarkedConfidence)
                    : new Attribution(Provenance.Inferred, Math.Min(confidence, GroupConfidence));
                sentences.Add(new Sentence(sentences.Count + 1, TickSpan.FromBounds(first.Span.Onset, phrase.Span.End), first.FirstBar, phrase.LastBar, [.. current], found, GroupBasis(reasons, current.Count == 1, words)));
                current.Clear();
                reasons.Clear();
                confidence = 1;
                detected = true;
                edited = false;
            }
        }

        return sentences;
    }

    /// <summary>
    /// The first rule that holds after <paramref name="phrase"/>, whether the sentence ends there, and the confidence of
    /// the reading it rests on (null when the file, the piece's end or the player decides it).
    /// </summary>
    private static (GroupReason Reason, bool Ends, double? Reading) After(Phrase phrase, Phrase? next, PhraseClose close, bool alone, HashSet<Tick> forcedEnds, HashSet<Tick> forcedRunsOn)
    {
        if (next is null)
        {
            return (new GroupReason(GroupRule.PieceEnds, phrase.Number, 0), true, null);
        }

        if (forcedEnds.Contains(next.Span.Onset))
        {
            return (new GroupReason(GroupRule.PlayerEnded, phrase.Number, next.Number), true, null);
        }

        if (forcedRunsOn.Contains(next.Span.Onset))
        {
            return (new GroupReason(GroupRule.PlayerJoined, phrase.Number, next.Number), false, null);
        }

        if (next.Start?.Evidence.Contains(PhraseEvidence.RepeatSign) == true)
        {
            return (new GroupReason(GroupRule.RepeatSign, phrase.Number, next.Number), true, null);
        }

        if (next.Start?.Evidence.Contains(PhraseEvidence.Repetition) == true && alone)
        {
            return (new GroupReason(GroupRule.Consequent, phrase.Number, next.Number), false, next.Start.Attribution.Confidence);
        }

        GroupRule rule = close.Kind switch
        {
            CloseKind.Full => GroupRule.FullClose,
            CloseKind.Half => GroupRule.HalfClose,
            _ => GroupRule.NoClose,
        };
        return (new GroupReason(rule, phrase.Number, next.Number), rule == GroupRule.FullClose, close.Attribution.Confidence);
    }
}