using System.Globalization;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>
/// The digest of the whole piece (assistant option A, item 2): the facts music-core already computed for the piece, each with
/// an id an answer can cite and the bars it is about, so a question about the piece is answered from them without reading
/// it in ranges. Only the engine's facts: no notes and nothing of the player's; the phrases and sentences are numbered as
/// the score shows them, and nothing says which boundaries the player set.
/// </summary>
public sealed partial class FactsDigest
{
    /// <summary>The densest bars the piece's digest lists, the densest first.</summary>
    public const int MaxDenseBars = 5;

    /// <summary>The most chords a section's line names, the ones held over most bars first.</summary>
    public const int MaxSectionChords = 6;

    /// <summary>
    /// The digest of the whole piece: its bars and meters; the keys; the map's summary line; the sections of the form with
    /// the sections they share material with; the tempo; each hand's range; the densest bars; the bars of new material; the
    /// phrases and the sentences as the score shows them; the chords of each section; the closes; the open Check flags.
    /// </summary>
    public static FactsDigest OfPiece(AssistantPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        Words w = piece.Words;
        PieceDna dna = piece.Doubt.On(PieceDna.Build(piece.Notes, piece.Meter, piece.Piece.Keys, piece.Piece.End, null, null, w, piece.Structure));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        List<DigestItem> items = [.. MapItems(dna, ids, w), .. TempoItems(piece, ids), .. Lines(piece.Notes, ids, w), .. DenseItems(dna.Density, ids, w)];
        items.AddRange(NewMaterialItems(dna.NewMaterialBars, ids, w));
        items.AddRange(PhrasingItems(piece, ids));
        items.AddRange(ChordItems(dna, ids, w));
        items.AddRange(dna.Cadences.Select(cadence => new DigestItem(
            Unique(string.Create(CultureInfo.InvariantCulture, $"cadence.{cadence.Bar}"), ids),
            "cadence",
            "inferred",
            cadence.Kind == CloseKind.Full ? w.T($"full close in bar {cadence.Bar}", $"полная каденция в такте {cadence.Bar}") : w.T($"half close in bar {cadence.Bar}", $"половинная каденция в такте {cadence.Bar}"))
        { Bars = (cadence.Bar, cadence.Bar) }));
        items.AddRange(ChecksIn(piece, piece.FirstBar, piece.LastBar, ids, openOnly: true));
        return new FactsDigest("piece", piece.FirstBar, piece.LastBar, null, items, [("meter", Meters(piece.Meter, piece.FirstBar, piece.LastBar))]);
    }

    /// <summary>The map's keys, its summary line and the sections of the form.</summary>
    private static IEnumerable<DigestItem> MapItems(PieceDna dna, HashSet<string> ids, Words w)
    {
        foreach ((DnaKey key, int index) in dna.Keys.Select(static (key, index) => (key, index)))
        {
            yield return new DigestItem(
                Unique(string.Create(CultureInfo.InvariantCulture, $"key.{index + 1}"), ids),
                "key",
                key.KeyInferred || key.ModeInferred ? "inferred" : "detected",
                $"{key.Key.NameIn(w)}, {Bars(key.FirstBar, key.LastBar, w)}")
            { Bars = (key.FirstBar, key.LastBar) };
        }

        if (dna.Summary.Length > 0)
        {
            yield return new DigestItem(Unique("summary", ids), "summary", "inferred", dna.Summary);
        }

        foreach (DnaSection section in dna.Form)
        {
            yield return SectionItem(section, dna.Form, ids, w);
        }
    }

    /// <summary>The phrases and the sentences, numbered as the marks on the score number them.</summary>
    private static IEnumerable<DigestItem> PhrasingItems(AssistantPiece piece, HashSet<string> ids)
    {
        Words w = piece.Words;
        PhraseLevels levels = piece.Structure.Levels;
        return [
            .. levels.Phrases.Phrases.Select(phrase => PhraseItem(phrase, piece, ids)),
            .. levels.Sentences.Sentences.Select(sentence => new DigestItem(
                Unique(string.Create(CultureInfo.InvariantCulture, $"sentence.{sentence.Number}"), ids),
                "sentence",
                Certainty(sentence.Found),
                w.T($"Sentence {sentence.Number}, {Bars(sentence.FirstBar, sentence.LastBar, w)}: {sentence.PhrasesIn(w)}", $"Предложение {sentence.Number}, {Bars(sentence.FirstBar, sentence.LastBar, w)}: {sentence.PhrasesIn(w)}"))
            { Bars = (sentence.FirstBar, sentence.LastBar) }),
        ];
    }

    /// <summary>"A′, bars 78–105, shares material with A (bars 0–8)": the section and the others of its letter (<see cref="DnaSection.SharesWith"/>).</summary>
    private static DigestItem SectionItem(DnaSection section, IReadOnlyList<DnaSection> form, HashSet<string> ids, Words w)
    {
        string[] shared = [.. form.Where(other => section.SharesWith.Contains(other.Id)).Select(other => $"{other.Label} ({Bars(other.FirstBar, other.LastBar, w)})")];
        string shares = shared.Length == 0 ? "" : w.T($", shares material with {w.Joined(shared)}", $", общий материал с {w.Joined(shared)}");
        return new DigestItem(Unique($"form.{section.Id}", ids), "form", "inferred", $"{section.Label}, {Bars(section.FirstBar, section.LastBar, w)}{shares}")
        {
            Bars = (section.FirstBar, section.LastBar),
            Label = section.Label,
        };
    }

    /// <summary>
    /// The tempo facts the Insights stream shows (<see cref="TempoProfile.Facts"/>): the usual tempo and range, then each
    /// change; of the whole piece (piece.tempo.*), or of the bars starting within <paramref name="within"/>, a selection's
    /// (here.tempo.*, so an answer about the selection does not read as about the piece).
    /// </summary>
    private static IEnumerable<DigestItem> TempoItems(AssistantPiece piece, HashSet<string> ids, TickSpan? within = null)
    {
        if (TempoProfile.OfPiece(piece.Piece, within) is not { } profile)
        {
            return [];
        }

        IReadOnlyList<MotifFact> facts = profile.Facts(piece.Words);
        return facts.Select((fact, index) => new DigestItem(Unique(within is null ? fact.Id : SelectionTempoId(fact.Id), ids), "tempo", ToolSchema.Camel(fact.Certainty.ToString()), fact.Text)
        {
            Bars = index == 0 ? null : (profile.Changes[index - 1].FirstBar, profile.Changes[index - 1].LastBar),
        });
    }

    /// <summary>piece.tempo.profile as a selection's here.tempo.profile, piece.tempo.change.N as here.tempo.change.N.</summary>
    private static string SelectionTempoId(string id) => $"here.{id["piece.".Length..]}";

    /// <summary>The <see cref="MaxDenseBars"/> bars with the most notes starting per beat, the densest first (the map's density lane).</summary>
    private static IEnumerable<DigestItem> DenseItems(IReadOnlyList<DnaBarLevel> density, HashSet<string> ids, Words w)
    {
        DnaBarLevel[] densest = [.. density.Where(static level => level.Value > 0).OrderByDescending(static level => level.Value).ThenBy(static level => level.Bar).Take(MaxDenseBars)];
        return densest.Select((level, index) =>
        {
            string perBeat = level.Value == Math.Floor(level.Value)
                ? w.Count((int)level.Value, "note", "notes", "нота", "ноты", "нот")
                : w.T($"{w.Decimal((decimal)level.Value)} notes", $"{w.Decimal((decimal)level.Value)} ноты");
            string rank = index == 0 ? w.T("the densest", "самый плотный") : w.T($"among the {densest.Length} densest", $"среди {densest.Length} самых плотных");
            return new DigestItem(
                Unique(string.Create(CultureInfo.InvariantCulture, $"density.{level.Bar}"), ids),
                "density",
                "detected",
                w.T($"Bar {level.Bar}, {rank}: {perBeat} per beat", $"Такт {level.Bar}, {rank}: {perBeat} на долю"))
            { Bars = (level.Bar, level.Bar) };
        });
    }

    /// <summary>Each run of bars whose line repeats no earlier bar (<see cref="PieceDna.NewMaterialBars"/>), by its first bar.</summary>
    private static IEnumerable<DigestItem> NewMaterialItems(IReadOnlyList<int> bars, HashSet<string> ids, Words w) =>
        StructureAnalyzer.Runs(bars).Select(run => new DigestItem(
            Unique(string.Create(CultureInfo.InvariantCulture, $"new.{run.First}"), ids),
            "newMaterial",
            "inferred",
            w.T($"new material in {Bars(run.First, run.Last, w)}", $"новый материал: {Bars(run.First, run.Last, w)}"))
        { Bars = run });

    /// <summary>"Phrase 2, bars 4–8, from bar 4, beat 3": numbered as the marks on the score number it.</summary>
    private static DigestItem PhraseItem(Phrase phrase, AssistantPiece piece, HashSet<string> ids)
    {
        Words w = piece.Words;
        MetricPosition start = piece.Meter.ToPosition(phrase.Span.Onset);
        return new DigestItem(
            Unique(string.Create(CultureInfo.InvariantCulture, $"phrase.{phrase.Number}"), ids),
            "phrase",
            phrase.Start is { } boundary ? Certainty(boundary.Attribution) : "detected",
            w.T(
                $"Phrase {phrase.Number}, {Bars(phrase.FirstBar, phrase.LastBar, w)}, from bar {start.Bar}, beat {start.Beat}",
                $"Фраза {phrase.Number}, {Bars(phrase.FirstBar, phrase.LastBar, w)}, начало: такт {start.Bar}, доля {start.Beat}"))
        { Bars = (phrase.FirstBar, phrase.LastBar) };
    }

    /// <summary>
    /// The chords of each section (of the whole piece without a form), as the map's harmony lane holds them, one a bar: each
    /// chord with its step and how many bars hold it, the most held first, at most <see cref="MaxSectionChords"/>.
    /// </summary>
    private static IEnumerable<DigestItem> ChordItems(PieceDna dna, HashSet<string> ids, Words w)
    {
        IEnumerable<(string Id, string? Label, int From, int To)> stretches = dna.Form.Count > 0
            ? dna.Form.Select(static section => (section.Id, (string?)section.Label, section.FirstBar, section.LastBar))
            : [("piece", null, dna.FirstBar, dna.LastBar)];
        foreach ((string id, string? label, int from, int to) in stretches)
        {
            var held = dna.Harmony
                .Where(chord => chord.Bar >= from && chord.Bar <= to)
                .GroupBy(static chord => (chord.Symbol, chord.Degree))
                .Select(static group => (group.Key.Symbol, group.Key.Degree, Bars: group.Count(), First: group.Min(static chord => chord.Bar)))
                .OrderByDescending(static chord => chord.Bars).ThenBy(static chord => chord.First)
                .ToList();
            if (held.Count == 0)
            {
                continue;
            }

            string chords = string.Join(", ", held.Take(MaxSectionChords).Select(static chord => string.Create(CultureInfo.InvariantCulture, $"{chord.Symbol} ({chord.Degree}) {chord.Bars}")));
            string more = held.Count > MaxSectionChords ? w.T($", and {held.Count - MaxSectionChords} more", $" и ещё {held.Count - MaxSectionChords}") : "";
            string of = label is null ? w.T("the piece", "пьесы") : w.T($"section {label}", $"части {label}");
            yield return new DigestItem(
                Unique($"chords.{id}", ids),
                "chords",
                "inferred",
                w.T($"Chords of {of} ({Bars(from, to, w)}), bars each: {chords}{more}", $"Аккорды {of} ({Bars(from, to, w)}), тактов у каждого: {chords}{more}"))
            { Bars = (from, to) };
        }
    }

    /// <summary>"detected" for what the file marks or a recording shows, else "inferred": a boundary the player set is not told apart (no player data is sent).</summary>
    private static string Certainty(Attribution attribution) => attribution.Provenance switch
    {
        Provenance.Imported or Provenance.Detected => "detected",
        _ => "inferred",
    };
}