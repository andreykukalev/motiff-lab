using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>
/// The facts digest (the AI companion study, section 4.4; CLAUDE.md: AI output cites fact IDs from the digest): a short,
/// deterministic listing of what music-core found in a range or in the whole piece, each item with an id, how sure it is
/// and where it is (as note addresses, never internal ids). A model's answer may cite only these ids
/// (<see cref="Unknown"/> says which it cites wrongly); everything else it says is interpretation. The digest repeats what
/// the analysers found and adds nothing: no notes, no timing beyond bars and places, no analysis of its own.
/// </summary>
public sealed partial class FactsDigest
{
    /// <summary>The most facts a range's digest lists, the strongest first (<see cref="FactRanking"/>).</summary>
    public const int MaxFacts = 10;

    /// <summary>The most note addresses a place lists; it says how many more it has.</summary>
    public const int MaxPlaceNotes = 8;

    private readonly IReadOnlyList<DigestItem> _items;

    private FactsDigest(string scope, int firstBar, int lastBar, Hand? hand, IReadOnlyList<DigestItem> items, IReadOnlyList<(string Name, string Value)> header)
    {
        Scope = scope;
        FirstBar = firstBar;
        LastBar = lastBar;
        Hand = hand;
        _items = items;
        Header = header;
    }

    /// <summary>"selection" or "piece".</summary>
    public string Scope { get; }

    public int FirstBar { get; }

    public int LastBar { get; }

    /// <summary>The hand the range was read in, or null for both.</summary>
    public Hand? Hand { get; }

    /// <summary>The digest's plain fields (meter, key) in order.</summary>
    public IReadOnlyList<(string Name, string Value)> Header { get; }

    /// <summary>Every id the digest lists, in its order: the only ids an answer may cite.</summary>
    public IReadOnlyList<string> Ids => [.. _items.Select(static item => item.Id)];

    /// <summary>The ids in <paramref name="cited"/> the digest does not list, each once, in the order cited.</summary>
    public IReadOnlyList<string> Unknown(IEnumerable<string> cited)
    {
        ArgumentNullException.ThrowIfNull(cited);
        var known = _items.Select(static item => item.Id).ToHashSet(StringComparer.Ordinal);
        return [.. cited.Where(id => !known.Contains(id)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The digest of bars <paramref name="from"/> to <paramref name="to"/> (in one hand, or both): the meter and key, the
    /// strongest facts with their places, the chords, the Check flags and each hand's range.
    /// </summary>
    public static FactsDigest OfRange(AssistantPiece piece, int from, int to, Hand? hand = null)
    {
        ArgumentNullException.ThrowIfNull(piece);
        RangeReading reading = piece.Read(from, to, hand);
        Words w = piece.Words;
        var header = new List<(string, string)> { ("meter", Meters(piece.Meter, from, to)) };
        if (reading.Motif.Key is { } key)
        {
            header.Add(("key", key.Key.NameIn(w)));
            header.Add(("keyCertainty", ToolSchema.Camel(key.Certainty.ToString())));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<DigestItem>();
        items.AddRange(Ranked(reading.Facts).Select(fact => FactItem(fact, piece.Addresses, ids)));
        items.AddRange(reading.Harmony.Chords.Select(chord => ChordItem(chord, piece, reading, ids)));
        items.AddRange(ChecksIn(piece, from, to, ids, openOnly: false));
        items.AddRange(Lines(reading.NotesOf(piece.Notes), ids, w));
        return new FactsDigest("selection", from, to, hand, items, header);
    }

    /// <summary>Writes the digest as one JSON object: scope, bars, the header fields, the items by kind and "ids".</summary>
    public void Write(Utf8JsonWriter json)
    {
        ArgumentNullException.ThrowIfNull(json);
        json.WriteStartObject();
        json.WriteString("scope", Scope);
        json.WriteStartObject("bars");
        json.WriteNumber("from", FirstBar);
        json.WriteNumber("to", LastBar);
        json.WriteEndObject();
        json.WriteString("hand", Hand is { } hand ? ToolSchema.Camel(hand.ToString()) : "both");
        foreach ((string name, string value) in Header)
        {
            json.WriteString(name, value);
        }

        json.WriteStartArray("items");
        foreach (DigestItem item in _items)
        {
            item.Write(json);
        }

        json.WriteEndArray();
        json.WriteStartArray("ids");
        foreach (DigestItem item in _items)
        {
            json.WriteStringValue(item.Id);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>The facts to list: the stream's main facts first, then the rest in the stream's order, at most <see cref="MaxFacts"/>.</summary>
    private static IEnumerable<MotifFact> Ranked(IReadOnlyList<MotifFact> facts)
    {
        IReadOnlyList<MotifFact> main = FactRanking.Main(facts);
        return main.Concat(FactRanking.InStream(facts).Where(fact => !main.Contains(fact))).Take(MaxFacts);
    }

    private static DigestItem FactItem(MotifFact fact, NoteAddressBook addresses, HashSet<string> ids) =>
        new(Unique(fact.Id, ids), ToolSchema.Camel(fact.Lens.ToString()), ToolSchema.Camel(fact.Certainty.ToString()), fact.Claim)
        {
            Doubt = fact.Doubt,
            Places = [.. fact.Places.Select(place => new DigestPlace(place.Label, [.. place.NoteIds.Select(addresses.TextOf).OfType<string>()]))],
        };

    private static DigestItem ChordItem(ChordSpan chord, AssistantPiece piece, RangeReading reading, HashSet<string> ids)
    {
        MetricPosition at = piece.Meter.ToPosition(chord.Span.Onset);
        MusicalKey spelling = reading.Motif.Key?.Key ?? new MusicalKey(0, KeyMode.Major);
        string place = piece.Words.T($"bar {at.Bar}, beat {at.Beat}", $"такт {at.Bar}, доля {at.Beat}");
        return new DigestItem(
            Unique(string.Create(CultureInfo.InvariantCulture, $"chord.{at.Bar}.{at.Beat}"), ids),
            "chord",
            chord.IsUncertain ? "uncertain" : "inferred",
            $"{chord.Symbol(spelling)}, {place}")
        {
            Doubt = chord.Doubt,
            Bars = (at.Bar, at.Bar),
            Places = [new DigestPlace(place, [.. chord.NoteIds.Select(piece.Addresses.TextOf).OfType<string>()])],
        };
    }

    private static IEnumerable<DigestItem> ChecksIn(AssistantPiece piece, int from, int to, HashSet<string> ids, bool openOnly) => piece.Check
        .Where(flag => flag.Bar >= from && flag.Bar <= to && (!openOnly || !flag.Resolved))
        .OrderBy(static flag => flag.Bar).ThenBy(static flag => flag.Reason)
        .Select(flag => new DigestItem(
            Unique(string.Create(CultureInfo.InvariantCulture, $"check.{flag.Bar}.{ToolSchema.Camel(flag.Reason.ToString())}"), ids),
            "check",
            flag.Resolved ? "resolved" : "open",
            $"{CheckFlag.NameOf(flag.Reason, piece.Words)}, {piece.Words.T("bar", "такт")} {flag.Bar}")
        { Bars = (flag.Bar, flag.Bar) });

    /// <summary>Each hand's (or track's) notes in the range: how many, the lowest and the highest.</summary>
    private static IEnumerable<DigestItem> Lines(IReadOnlyList<Note> notes, HashSet<string> ids, Words w) => notes
        .GroupBy(static note => note.Hand is { } hand ? (hand.Value == Events.Hand.Right ? "R" : "L") : string.Create(CultureInfo.InvariantCulture, $"T{note.Track + 1}"))
        .OrderBy(static line => line.Key, StringComparer.Ordinal)
        .Select(line => new DigestItem(
            Unique($"range.{line.Key}", ids),
            "range",
            line.Any(static note => note.Hand?.Attribution.Provenance == Provenance.Inferred) ? "inferred" : "detected",
            w.T(
                $"{line.Key}: {line.Count()} notes from {line.Min(static note => note.Pitch)} to {line.Max(static note => note.Pitch)}",
                $"{line.Key}: {w.Count(line.Count(), "note", "notes", "нота", "ноты", "нот")}, от {line.Min(static note => note.Pitch)} до {line.Max(static note => note.Pitch)}")));

    private static string Meters(MeterMap meter, int from, int to) =>
        string.Join(", ", Enumerable.Range(from, to - from + 1).Select(bar => meter.GetBar(bar).Signature.ToString()).Distinct(StringComparer.Ordinal));

    private static string Bars(int from, int to, Words w) => from == to
        ? w.T($"bar {from}", $"такт {from}")
        : w.T($"bars {from}–{to}", $"такты {from}–{to}");

    /// <summary>The id, or the id with "~2", "~3"… when the digest already lists it, so every id is listed once.</summary>
    private static string Unique(string id, HashSet<string> ids)
    {
        string unique = id;
        for (int copy = 2; !ids.Add(unique); copy++)
        {
            unique = string.Create(CultureInfo.InvariantCulture, $"{id}~{copy}");
        }

        return unique;
    }
}

/// <summary>A place an item names: its label and up to <see cref="FactsDigest.MaxPlaceNotes"/> note addresses.</summary>
public sealed record DigestPlace(string Label, IReadOnlyList<string> Notes);

/// <summary>
/// One item of a digest: its id, kind (a fact's lens, "chord", "check", "range"; of the piece also "summary", "key", "form",
/// "tempo", "density", "newMaterial", "phrase", "sentence", "chords", "cadence"), how sure it is and its line.
/// </summary>
public sealed record DigestItem(string Id, string Kind, string Certainty, string Text)
{
    /// <summary>"Rests on recognised notes still to check in bar 5." when it does (#352).</summary>
    public string? Doubt { get; init; }

    public IReadOnlyList<DigestPlace> Places { get; init; } = [];

    /// <summary>The bars the item is about, when it names bars rather than notes (a key, a section, a close, a chord, a flag); null otherwise.</summary>
    public (int From, int To)? Bars { get; init; }

    /// <summary>A section's letter ("A′"), so a question that names the section finds it; null for other items.</summary>
    public string? Label { get; init; }

    internal void Write(Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteString("id", Id);
        json.WriteString("kind", Kind);
        json.WriteString("certainty", Certainty);
        json.WriteString("text", Text);
        if (Label is not null)
        {
            json.WriteString("label", Label);
        }

        if (Doubt is not null)
        {
            json.WriteString("doubt", Doubt);
        }

        if (Bars is { } bars)
        {
            json.WriteStartObject("bars");
            json.WriteNumber("from", bars.From);
            json.WriteNumber("to", bars.To);
            json.WriteEndObject();
        }

        if (Places.Count > 0)
        {
            json.WriteStartArray("places");
            foreach (DigestPlace place in Places)
            {
                json.WriteStartObject();
                json.WriteString("label", place.Label);
                json.WriteStartArray("notes");
                foreach (string note in place.Notes.Take(FactsDigest.MaxPlaceNotes))
                {
                    json.WriteStringValue(note);
                }

                json.WriteEndArray();
                if (place.Notes.Count > FactsDigest.MaxPlaceNotes)
                {
                    json.WriteNumber("more", place.Notes.Count - FactsDigest.MaxPlaceNotes);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }
}