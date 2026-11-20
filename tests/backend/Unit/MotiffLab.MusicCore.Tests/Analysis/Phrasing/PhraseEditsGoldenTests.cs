using System.Globalization;
using System.Text;

using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Tests.Golden;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis.Phrasing;

/// <summary>
/// Every edit of the three phrase levels on real pieces, kept tick for tick (Golden/*.txt): a fixed run of splits, made
/// units, joins and resets at each level, each from the state the one before left, with the edits each level saves and
/// the units each level then has; and edits a saved file may hold that no edit makes today (outside the piece, at a
/// boundary of a coarser level, set and taken away at once), as they read and as the next edit leaves them. It pins
/// the edit algebra the levels share (D13), so moving it changes nothing. With MOTIFF_UPDATE_SNAPSHOTS=1 the golden is
/// written again, when the edits change on purpose.
/// </summary>
public class PhraseEditsGoldenTests
{
    private const int Steps = 54;

    /// <summary>The golden's name and the piece: the example both apps open, the design kit's 97 bars, and a short piece in another key.</summary>
    public static TheoryData<string, string> Pieces => new()
    {
        { "fur-elise.txt", Path.Combine("Import", "MusicXml", "Fixtures", "FurElise.musicxml") },
        { "elise.txt", Path.Combine("Golden", "Analysis", "elise.musicxml") },
        { "ode-to-joy.txt", Path.Combine("Experiments", "Fixtures", "ode-to-joy.musicxml") },
    };

    [Theory]
    [MemberData(nameof(Pieces))]
    public void Every_edit_at_every_level_gives_the_edits_and_units_it_gave(string golden, string path)
    {
        ImportedPiece piece = PieceImporter.Import(Path.GetFileName(path), File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, path))).Piece!;
        PhraseLevels Suggest(LevelEdits? edits) => PhraseLevels.Suggest(
            piece.Notes, piece.Meter, piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, piece.End, KeySignature.InForce(piece.Keys, Tick.Zero), Words.English, edits);

        var written = new StringBuilder();
        PhraseLevels levels = Suggest(null);
        written.Append(CultureInfo.InvariantCulture, $"# {Path.GetFileName(path)}, end {piece.End.Value}\n");
        written.Append(CultureInfo.InvariantCulture, $"found phrases {Ticks(levels.Phrases.Found.Select(static boundary => boundary.At))}\n");
        written.Append(CultureInfo.InvariantCulture, $"found cuts {Ticks(levels.Shorts.Found.Select(static boundary => boundary.At))}\n");
        Write(written, levels);

        for (int step = 0; step < Steps; step++)
        {
            (string what, LevelEdits edits) = Edit(levels, step);
            levels = levels.With(edits);
            written.Append(CultureInfo.InvariantCulture, $"\n## {step + 1}. {what}\n");
            Write(written, levels);
            Assert.Equal(levels.Edits, Suggest(levels.Edits).Edits);
        }

        foreach ((string what, LevelEdits edits) in Saved(levels, piece.End))
        {
            PhraseLevels read = Suggest(edits);
            written.Append(CultureInfo.InvariantCulture, $"\n## saved: {what}\n");
            Write(written, read);
            foreach (PhraseLevel level in PhraseLevels.All)
            {
                IReadOnlyList<PhraseUnit> units = read.Units(level);
                PhraseLevels joined = read.With(read.JoinWithNext(level, units[0]));
                written.Append(CultureInfo.InvariantCulture, $"\n### then join {PhraseLevels.Name(level)} 1\n");
                Write(written, joined);
                written.Append(CultureInfo.InvariantCulture, $"\n### or split {PhraseLevels.Name(level)} at {units[^1].Span.Onset.Value + 1}\n");
                Write(written, read.With(read.SplitAt(level, new Tick(units[^1].Span.Onset.Value + 1))));
            }
        }

        TextGoldens.AssertTextGolden(Path.Combine("Analysis", "Phrasing"), golden, written.ToString());
    }

    /// <summary>The edit of step <paramref name="step"/>: the levels in turn, finest first, and a kind of edit that changes every three steps.</summary>
    private static (string What, LevelEdits Edits) Edit(PhraseLevels levels, int step)
    {
        PhraseLevel level = PhraseLevels.All[step % 3];
        string name = PhraseLevels.Name(level);
        IReadOnlyList<PhraseUnit> units = levels.Units(level);
        PhraseUnit unit = units[(step * 7) % units.Count];
        Tick end = levels.Phrases.End;
        Tick at = new(((end.Value * ((step * 37) % 100)) / 100) + ((step * 53) % 480));
        switch ((step / 3) % 6)
        {
            case 0:
                return ($"split {name} at {at.Value}", levels.SplitAt(level, at));
            case 1:
                {
                    TickSpan span = TickSpan.FromBounds(at, new Tick(Math.Min(end.Value + 480, at.Value + (1920 * (1 + (step % 5))) + 120)));
                    return ($"make {name} {span.Onset.Value}–{span.End.Value}", levels.MakeUnit(level, span));
                }

            case 2:
                return ($"join {name} {unit.Number}", levels.JoinWithNext(level, unit));
            case 3:
                {
                    // A unit's own span, a little in from both edges, as a selection snapped to beats would be.
                    TickSpan span = unit.Span.Duration > 400 ? TickSpan.FromBounds(unit.Span.Onset.Offset(200), new Tick(unit.Span.End.Value - 200)) : unit.Span;
                    return ($"make {name} {span.Onset.Value}–{span.End.Value} (unit {unit.Number}, inside)", levels.MakeUnit(level, span));
                }

            case 4:
                return step % 2 == 0
                    ? ($"join {name} {units[^1].Number} (the last)", levels.JoinWithNext(level, units[^1]))
                    : ($"reset {name}", levels.Reset(level));
            default:
                {
                    // Near a boundary in effect, or at the start or the end of the piece.
                    Tick near = step % 4 == 0 ? Tick.Zero
                        : step % 4 == 1 ? end
                        : unit.Span.Onset.Offset(step % 2 == 0 ? 150 : -150 + (unit.Span.Onset.Value < 150 ? 300 : 0));
                    return ($"split {name} at {near.Value} (near unit {unit.Number})", levels.SplitAt(level, near));
                }
        }
    }

    /// <summary>Edits a saved file may hold that no edit makes: outside the piece, at a boundary of a coarser level, off the boundaries, and set and taken away at once.</summary>
    private static IEnumerable<(string What, LevelEdits Edits)> Saved(PhraseLevels levels, Tick end)
    {
        Tick[] phraseFound = [.. levels.Phrases.Found.Select(static boundary => boundary.At)];
        Tick[] cutsFound = [.. levels.Shorts.Found.Select(static boundary => boundary.At)];
        Tick[] phraseStarts = [.. levels.Phrases.Boundaries.Select(static boundary => boundary.At)];
        Tick firstPhrase = phraseFound.Length > 0 ? phraseFound[0] : new Tick(1920);
        Tick firstCut = cutsFound.Length > 0 ? cutsFound[0] : new Tick(960);
        Tick firstStart = phraseStarts.Length > 0 ? phraseStarts[0] : new Tick(1920);
        Tick odd = new(firstPhrase.Value + 37);
        Tick after = new(end.Value + 480);

        yield return ("outside the piece", new LevelEdits(
            new PhraseEdits([Tick.Zero, after], [Tick.Zero]),
            new PhraseEdits([Tick.Zero, end, after], [after]),
            new PhraseEdits([Tick.Zero, end], [after])));
        yield return ("set and taken away at once", new LevelEdits(
            new PhraseEdits([firstCut, odd], [firstCut, odd]),
            new PhraseEdits([firstPhrase, odd], [firstPhrase, odd]),
            new PhraseEdits([firstStart], [firstStart])));
        yield return ("off the boundaries and at a coarser level's", new LevelEdits(
            new PhraseEdits([firstStart, odd], [odd, new Tick(odd.Value + 1)]),
            new PhraseEdits([], [odd, new Tick(odd.Value + 1)]),
            new PhraseEdits([odd], [new Tick(odd.Value + 1), firstStart])));
    }

    private static void Write(StringBuilder written, PhraseLevels levels)
    {
        foreach (PhraseLevel level in PhraseLevels.All)
        {
            PhraseEdits edits = levels.Edits[level];
            written.Append(CultureInfo.InvariantCulture, $"{PhraseLevels.Name(level)} +{Ticks(edits.Added)} -{Ticks(edits.Removed)}\n");
            written.Append("  ").AppendJoin(" ", levels.Units(level).Select(Unit)).Append('\n');
        }
    }

    private static string Unit(PhraseUnit unit) => string.Create(
        CultureInfo.InvariantCulture,
        $"{unit.Span.Onset.Value}:{unit.FirstBar}-{unit.LastBar}:{Short(unit.Start)}{(unit.Evidence.Count > 0 ? ":" + string.Join(",", unit.Evidence) : "")}");

    private static string Short(Attribution attribution) => attribution.Provenance switch
    {
        Provenance.Imported => "imp",
        Provenance.Detected => string.Create(CultureInfo.InvariantCulture, $"det{attribution.Confidence:0.##}"),
        Provenance.Inferred => string.Create(CultureInfo.InvariantCulture, $"inf{attribution.Confidence:0.##}"),
        Provenance.UserConfirmed => "conf",
        Provenance.UserEdited => "edit",
        _ => attribution.Provenance.ToString(),
    };

    private static string Ticks(IEnumerable<Tick> ticks) => "[" + string.Join(",", ticks.Select(static tick => tick.Value.ToString(CultureInfo.InvariantCulture))) + "]";
}