using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// What the facade answers of the phrases: the three levels, their edits and their units (0018, section 3; contract 3), and
/// the marks on the score (the phrases spec, «Engine»; within contract 13): each phrase's start, how many boundaries are the
/// player's own, and the sections of the form with their letters.
/// </summary>
internal static partial class Answer
{
    /// <summary>
    /// The three levels (0018, section 3; contract 3): the edits per level and the units of the level shown (contract 17:
    /// no longer the level shown and every level with its count, which no app read), a phrase with its mark (<see cref="Unit"/>); then "own", the player's boundaries as
    /// «Ваших границ: N» counts them (<see cref="PhraseMap.OwnCount"/>), and "sections", where each section of the form
    /// starts (the letter in its box): the section as Piece DNA writes it, with the phrase and the bar and beat it starts at;
    /// and the phrase anchors (#318, <see cref="Anchors"/>).
    /// </summary>
    public static void Phrases(Utf8JsonWriter json, PhraseLevels levels, IReadOnlyList<DnaSection> sections, PhraseLevel shown, Words words, MeterMap meter, IReadOnlyList<PhraseAnchor> anchors)
    {
        json.WriteString("evidence", JsonWrite.Name(levels.Phrases.Evidence));
        LevelEdits(json, "edits", levels.Edits);

        json.WriteStartArray("units");
        foreach (PhraseUnit unit in levels.Units(shown))
        {
            Unit(json, levels, unit, words, meter, anchors);
        }

        json.WriteEndArray();
        json.WriteNumber("own", levels.Phrases.OwnCount);
        json.WriteStartArray("sections");
        foreach (DnaSection section in sections)
        {
            json.WriteStartObject();
            FormSection(json, section, meter);
            Phrase phrase = levels.Phrases.At(section.Span.Onset);
            if (phrase.Span.Onset == section.Span.Onset)
            {
                json.WriteNumber("phrase", phrase.Number);
            }
            else
            {
                json.WriteNull("phrase");
            }

            BarAndBeat(json, meter, section.Span.Onset);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Anchors(json, levels.Phrases, anchors);
    }

    /// <summary>
    /// "anchors" (#318, within contract 16): each phrase anchor something is attached to, its id and span, and "phrase",
    /// the number of the phrase it is, or null when the notes or their analysis changed so that it is no phrase any more
    /// (phrases.reattach attaches those again); then "unmatched", how many are null.
    /// </summary>
    private static void Anchors(Utf8JsonWriter json, PhraseMap phrases, IReadOnlyList<PhraseAnchor> anchors)
    {
        int unmatched = 0;
        json.WriteStartArray("anchors");
        foreach (PhraseAnchor anchor in anchors)
        {
            json.WriteStartObject();
            json.WriteString("id", anchor.Id);
            json.WriteNumber("onset", anchor.Span.Onset.Value);
            json.WriteNumber("end", anchor.Span.End.Value);
            if (phrases.WithSpan(anchor.Span) is { } phrase)
            {
                json.WriteNumber("phrase", phrase.Number);
            }
            else
            {
                json.WriteNull("phrase");
                unmatched++;
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("unmatched", unmatched);
    }

    /// <summary>
    /// What an edit of the phrases changed (within contract 13), after the phrases: "previous", the edits before it, which
    /// phrases.edit "restore" puts back (Undo); and "changed", each phrase that is not one of the phrases any more, as it
    /// was ("before", numbered as it was) and the phrase now that covers most of it ("after"), so the app can offer to
    /// select a phrase that was selected by its new boundaries. The selection itself never changes.
    /// </summary>
    public static void PhraseEdit(Utf8JsonWriter json, PhraseLevels before, PhraseLevels after)
    {
        LevelEdits(json, "previous", before.Edits);
        json.WriteStartArray("changed");
        foreach ((Phrase was, Phrase now) in after.Phrases.ChangedFrom(before.Phrases))
        {
            json.WriteStartObject();
            PhraseSpan(json, "before", before.Phrases, was);
            PhraseSpan(json, "after", after.Phrases, now);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>A unit; a phrase with "anchor", the id of the anchor it is (#318), or null while nothing is attached to it.</summary>
    private static void Unit(Utf8JsonWriter json, PhraseLevels levels, PhraseUnit unit, Words words, MeterMap meter, IReadOnlyList<PhraseAnchor> anchors)
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
        JsonWrite.Texts(json, "evidence", unit.Evidence);
        Attribution(json, unit.Start);
        json.WriteString("description", unit.Description);
        if (unit.Level == PhraseLevel.Phrase)
        {
            Mark(json, levels, levels.Phrases.Phrases[unit.Number - 1], meter);
        }

        json.WriteEndObject();
        if (unit.Level == PhraseLevel.Phrase)
        {
            Motifs(json, levels, unit, meter);
            json.WriteString("anchor", anchors.FirstOrDefault(anchor => anchor.Span == unit.Span)?.Id);
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// A phrase's mark, in its start: the bar and beat of the exact start note; "found", the found boundary a moved one of
    /// the player's took the place of (its ghost and «Вернуть найденную»), or null; "close", how the phrase before ends
    /// (null for the first); and "why", «Почему здесь».
    /// </summary>
    private static void Mark(Utf8JsonWriter json, PhraseLevels levels, Phrase phrase, MeterMap meter)
    {
        BarAndBeat(json, meter, phrase.Span.Onset);
        if (levels.Phrases.Replaced.TryGetValue(phrase.Span.Onset, out Tick found))
        {
            json.WriteStartObject("found");
            json.WriteNumber("at", found.Value);
            BarAndBeat(json, meter, found);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("found");
        }

        if (levels.CloseBefore(phrase) is { } close)
        {
            json.WriteString("close", JsonWrite.Name(close));
        }
        else
        {
            json.WriteNull("close");
        }

        json.WriteString("why", levels.WhyHere(phrase));
    }

    /// <summary>The short units inside a phrase, its motifs (the focus card's small commas): where each starts and ends.</summary>
    private static void Motifs(Utf8JsonWriter json, PhraseLevels levels, PhraseUnit phrase, MeterMap meter)
    {
        json.WriteStartArray("motifs");
        foreach (PhraseUnit motif in levels.MotifsOf(phrase))
        {
            json.WriteStartObject();
            json.WriteNumber("onset", motif.Span.Onset.Value);
            json.WriteNumber("end", motif.Span.End.Value);
            json.WriteNumber("firstBar", motif.FirstBar);
            json.WriteNumber("lastBar", motif.LastBar);
            BarAndBeat(json, meter, motif.Span.Onset);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>A phrase's number, span and the span a tap selects, as the edit's "changed" names it.</summary>
    private static void PhraseSpan(Utf8JsonWriter json, string name, PhraseMap map, Phrase phrase)
    {
        json.WriteStartObject(name);
        json.WriteNumber("number", phrase.Number);
        json.WriteNumber("onset", phrase.Span.Onset.Value);
        json.WriteNumber("end", phrase.Span.End.Value);
        Span(json, "selectable", map.Selectable(phrase));
        json.WriteEndObject();
    }

    /// <summary>The edits at every level, {short, phrase, sentence} each {added, removed}, as phrases.edit "restore" takes them back.</summary>
    private static void LevelEdits(Utf8JsonWriter json, string name, LevelEdits edits)
    {
        json.WriteStartObject(name);
        foreach (PhraseLevel level in PhraseLevels.All)
        {
            json.WriteStartObject(PhraseLevels.Name(level));
            JsonWrite.Numbers(json, "added", edits[level].Added.Select(static tick => tick.Value));
            JsonWrite.Numbers(json, "removed", edits[level].Removed.Select(static tick => tick.Value));
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    /// <summary>The bar and beat a tick falls in, as <see cref="MeterMap.ToPosition"/> counts them, so an app writes bar.beat ("4.3").</summary>
    private static void BarAndBeat(Utf8JsonWriter json, MeterMap meter, Tick at)
    {
        MetricPosition place = meter.ToPosition(at);
        json.WriteNumber("bar", place.Bar);
        json.WriteNumber("beat", place.Beat);
    }
}