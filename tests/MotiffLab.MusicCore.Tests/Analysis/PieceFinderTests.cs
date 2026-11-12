using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The pieces of a long score or recording (version 0.5): where they begin, found from the same facts as the automatic
/// bookmarks, but in a recording a silence alone is not a boundary (Andrei, 2026-11-12), and the player's number of
/// pieces makes up the rest; then added, moved, renamed and dismissed by hand.
/// </summary>
public class PieceFinderTests
{
    private const long Quarter = CanonicalTime.TicksPerQuarter;
    private const long Bar = 4 * Quarter;

    private static Note Note(int pitch, long onset, bool heard) => new()
    {
        Id = $"n_{onset}_{pitch}",
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), Quarter),
        Velocity = 80,
        Track = 0,
        Attribution = heard ? Attribution.Detected(0.4) : Attribution.Imported,
    };

    private static List<Note> Bars(int first, int last, int[] pitches, bool heard = true)
    {
        var notes = new List<Note>();
        for (int bar = first; bar <= last; bar++)
        {
            for (int beat = 0; beat < 4; beat++)
            {
                notes.Add(Note(pitches[beat % pitches.Length], ((bar - 1) * Bar) + (beat * Quarter), heard));
            }
        }

        return notes;
    }

    private static Tick At(int bar) => new((bar - 1) * Bar);

    /// <summary>
    /// A recording of 60 bars at 120 BPM (a bar lasts 2 s): bars 19–20 and 39–40 silent. With <paramref name="change"/>
    /// the music after the first silence is in E flat minor at 80 BPM; after the second, the same as before it.
    /// </summary>
    private static SectionEvidence Concert(bool change)
    {
        int[] home = [60, 64, 67, 72], away = [63, 66, 70, 75];
        var notes = Bars(1, 18, home);
        notes.AddRange(Bars(21, 38, change ? away : home));
        notes.AddRange(Bars(41, 60, change ? away : home));
        PlaybackTempo[] tempos =
        [
            .. Enumerable.Range(0, 18 * 4).Select(beat => new PlaybackTempo(new Tick(beat * Quarter), 500_000)),
            .. Enumerable.Range(20 * 4, 40 * 4).Select(beat => new PlaybackTempo(new Tick(beat * Quarter), change ? 750_000 : 500_000)),
        ];
        return new SectionEvidence(notes, MeterMap.Default, new Tick(60 * Bar)) { Heard = true, Tempos = tempos };
    }

    [Fact]
    public void In_a_recording_a_silence_alone_is_no_boundary_but_with_a_new_key_and_tempo_it_is()
    {
        FoundSections same = SectionFinder.FindPieces(Concert(change: false));
        FoundSection whole = Assert.Single(same.Items);
        Assert.True(same.IsPieces);
        Assert.Equal((1, 60), (whole.FirstBar, whole.LastBar));
        Assert.Equal("Piece 1", whole.PieceTitleIn(Words.English));
        Assert.Equal(3, SectionFinder.Find(Concert(change: false)).Items.Count);

        FoundSections changed = SectionFinder.FindPieces(Concert(change: true));
        Assert.Equal([1, 21], changed.Items.Select(section => section.FirstBar));
        FoundSection second = changed.Items[1];
        Assert.Contains(second.Cues, cue => cue.Kind == SectionCueKind.HeardTempo);
        Assert.False(second.IsOnlySilence);
        Assert.Equal("Пьеса 2", second.PieceTitleIn(Words.Russian));
    }

    [Fact]
    public void The_players_number_of_pieces_takes_the_silences_alone_longest_first_and_says_so()
    {
        // The second silence is made longer: bar 41's notes start a bar later.
        SectionEvidence evidence = Concert(change: false);
        evidence = evidence with { Notes = [.. evidence.Notes.Where(note => note.Span.Onset < At(41) || note.Span.Onset >= At(42))] };

        FoundSections two = SectionFinder.FindPieces(evidence, count: 2);
        Assert.Equal([1, 42], two.Items.Select(section => section.FirstBar));
        Assert.True(two.Items[1].IsOnlySilence);
        Assert.Equal("6 s of silence before, only a pause: the key and the tempo stay the same", two.Items[1].ReasonIn(Words.English));
        Assert.EndsWith("только пауза: тональность и темп те же", two.Items[1].ReasonIn(Words.Russian), StringComparison.Ordinal);

        Assert.Equal([1, 21, 42], SectionFinder.FindPieces(evidence, count: 3).Items.Select(section => section.FirstBar));
        Assert.Equal([1, 21, 42], SectionFinder.FindPieces(evidence, count: 9).Items.Select(section => section.FirstBar));
        Assert.Single(SectionFinder.FindPieces(evidence, count: 1).Items);
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionFinder.FindPieces(evidence, count: 0));
    }

    [Fact]
    public void A_strong_boundary_comes_before_a_silence_alone_when_the_number_is_reached()
    {
        FoundSections one = SectionFinder.FindPieces(Concert(change: true), count: 2);

        Assert.Equal([1, 21], one.Items.Select(section => section.FirstBar));
        Assert.False(one.Items[1].IsOnlySilence);
    }

    [Fact]
    public void In_notation_the_rules_stay_and_the_number_adds_the_weaker_boundaries()
    {
        var notes = Bars(1, 40, [60, 64, 67, 72], heard: false);
        var evidence = new SectionEvidence(notes, MeterMap.Default, new Tick(40 * Bar))
        {
            Barlines = [new MusicXmlBarline(At(21), 20, "light-light", false)],
        };

        Assert.Empty(SectionFinder.Find(evidence).Items);
        Assert.Single(SectionFinder.FindPieces(evidence).Items);
        FoundSections two = SectionFinder.FindPieces(evidence, count: 2);
        Assert.Equal([1, 21], two.Items.Select(section => section.FirstBar));
        Assert.Equal("double bar line", two.Items[1].ReasonIn(Words.English));
        Assert.Empty(SectionFinder.FindPieces(new SectionEvidence([], MeterMap.Default, Tick.Zero)).Items);
    }

    [Fact]
    public void A_boundary_added_by_hand_splits_the_piece_it_falls_in_and_is_the_players()
    {
        FoundSections found = SectionFinder.FindPieces(Concert(change: true));

        Assert.False(found.CanAdd(1));
        Assert.False(found.CanAdd(21));
        Assert.False(found.CanAdd(61));
        Assert.Throws<ArgumentOutOfRangeException>(() => found.Add(21));

        FoundSections added = found.Add(41);

        Assert.Equal([1, 21, 41], added.Items.Select(section => section.FirstBar));
        Assert.Equal([1, 2, 3], added.Items.Select(section => section.Number));
        Assert.Equal((21, 40), (added.Items[1].FirstBar, added.Items[1].LastBar));
        Assert.Equal(At(41), added.Items[1].Span.End);
        FoundSection third = added.Items[2];
        Assert.Equal(TickSpan.FromBounds(At(41), new Tick(60 * Bar)), third.Span);
        Assert.True(third.IsAdded);
        Assert.Equal(Provenance.UserEdited, third.Attribution.Provenance);
        Assert.Equal("добавлено вами", third.ReasonIn(Words.Russian));
        Assert.True(added.IsPieces);
    }

    [Fact]
    public void A_renamed_piece_keeps_its_title_through_moves_and_a_blank_title_gives_the_found_one_back()
    {
        FoundSections found = SectionFinder.FindPieces(Concert(change: true)).Rename(2, "  Nocturne  ");

        Assert.Equal("Nocturne", found.Items[1].PieceTitleIn(Words.English));
        Assert.Equal("Nocturne", found.Items[1].TitleIn(Words.English));
        FoundSections moved = found.Add(41).Move(2, 23);
        Assert.Equal("Nocturne", moved.Items[1].PieceTitleIn(Words.English));
        Assert.Equal(23, moved.Items[1].FirstBar);
        Assert.Equal("Piece 2", moved.Rename(2, " ").Items[1].PieceTitleIn(Words.English));
        Assert.Throws<ArgumentOutOfRangeException>(() => found.Rename(9, "x"));
    }

    [Fact]
    public void Dismissing_the_last_boundary_leaves_one_piece_and_the_first_piece_stays()
    {
        FoundSections found = SectionFinder.FindPieces(Concert(change: true));

        Assert.Same(found, found.Dismiss(1));
        FoundSections one = found.Dismiss(2);
        FoundSection whole = Assert.Single(one.Items);
        Assert.Equal((1, 60), (whole.FirstBar, whole.LastBar));
        Assert.Equal(new Tick(60 * Bar), whole.Span.End);
        Assert.Equal([1, 41], one.Add(41).Items.Select(section => section.FirstBar));
    }

    [Fact]
    public void The_same_evidence_and_number_give_the_same_pieces()
    {
        static string Read(FoundSections found) => string.Join(" | ", found.Items.Select(section => $"{section.Span} {section.Confidence} {section.ReasonIn(Words.English)}"));

        Assert.Equal(Read(SectionFinder.FindPieces(Concert(change: false), 3)), Read(SectionFinder.FindPieces(Concert(change: false), 3)));
    }

    [Fact]
    public void Two_pieces_in_one_score_are_found_as_two_pieces()
    {
        ImportedPiece piece = PieceImporter.Import("two-pieces.musicxml", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Analysis", "Fixtures", "two-pieces.musicxml"))).Piece!;

        FoundSections found = SectionFinder.FindPieces(SectionEvidence.Of(piece, piece.Notes, PlaybackScore.FromPiece(piece).Tempos));

        Assert.Equal([0, 106], found.Items.Select(section => section.FirstBar));
        Assert.Equal("Ode to Joy", found.Items[1].PieceTitleIn(Words.English));
    }
}