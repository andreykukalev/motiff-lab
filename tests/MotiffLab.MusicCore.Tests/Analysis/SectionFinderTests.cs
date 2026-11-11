using System.Text;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// Automatic bookmarks (an experiment; Andrei, 2026-11-11): the boundaries of the pieces, movements and large sections of a
/// long score or recording, found from what the file shows, proposed as sections to confirm, move or dismiss.
/// </summary>
public class SectionFinderTests
{
    private const long Quarter = CanonicalTime.TicksPerQuarter;
    private const long Bar = 4 * Quarter;

    private static Note Note(int pitch, long onset, long duration = Quarter, bool heard = false) => new()
    {
        Id = $"n_{onset}_{pitch}",
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = heard ? Attribution.Detected(0.4) : Attribution.Imported,
    };

    /// <summary>Four quarters in every bar from the first bar to the last, numbered from 1, on the pitches given per bar.</summary>
    private static List<Note> Bars(int first, int last, Func<int, int[]> pitches, bool heard = false)
    {
        var notes = new List<Note>();
        for (int bar = first; bar <= last; bar++)
        {
            int[] row = pitches(bar);
            for (int beat = 0; beat < 4; beat++)
            {
                notes.Add(Note(row[beat % row.Length], ((bar - 1) * Bar) + (beat * Quarter), heard: heard));
            }
        }

        return notes;
    }

    private static SectionEvidence Score(int bars, params MusicXmlBarline[] barlines) =>
        new(Bars(1, bars, _ => [60, 64, 67, 72]), MeterMap.Default, new Tick(bars * Bar)) { Barlines = barlines };

    private static MusicXmlBarline Before(int bar, string style, bool repeat = false) => new(new Tick((bar - 1) * Bar), bar - 1, style, repeat);

    private static Tick At(int bar) => new((bar - 1) * Bar);

    [Fact]
    public void A_double_bar_line_and_a_new_key_part_the_score_in_two_with_the_facts_in_both_languages()
    {
        SectionEvidence evidence = Score(40, Before(21, "light-light")) with
        {
            Keys = [new KeySignature(Tick.Zero, 0, KeyMode.Major), new KeySignature(At(21), -1, KeyMode.Minor)],
        };

        FoundSections found = SectionFinder.Find(evidence);

        Assert.Equal(2, found.Items.Count);
        FoundSection opening = found.Items[0], second = found.Items[1];
        Assert.True(opening.IsOpening);
        Assert.Equal((1, 20), (opening.FirstBar, opening.LastBar));
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, At(21)), opening.Span);
        Assert.Equal((21, 40), (second.FirstBar, second.LastBar));
        Assert.Equal(TickSpan.FromBounds(At(21), new Tick(40 * Bar)), second.Span);
        Assert.Equal(0.51, second.Confidence);
        Assert.Equal(second.Confidence, opening.Confidence);
        Assert.Equal(Provenance.Inferred, second.Attribution.Provenance);
        Assert.Equal("double bar line, key changes to D minor", second.ReasonIn(Words.English));
        Assert.Equal("двойная черта, смена тональности на ре минор", second.ReasonIn(Words.Russian));
        Assert.Equal("Part 2, bars 21–40", second.TitleIn(Words.English));
        Assert.Equal("Часть 2, такты 21–40", second.TitleIn(Words.Russian));
        Assert.Equal("начало пьесы", opening.ReasonIn(Words.Russian));
        Assert.Equal("possible", second.ConfidenceIn(Words.English));
    }

    [Fact]
    public void One_weak_fact_proposes_nothing_and_repeat_signs_are_not_boundaries()
    {
        Assert.Empty(SectionFinder.Find(Score(40, Before(21, "light-light"))).Items);
        Assert.Empty(SectionFinder.Find(Score(40, Before(21, "light-heavy", repeat: true), Before(21, "heavy-light", repeat: true))).Items);
        Assert.Empty(SectionFinder.Find(new SectionEvidence([], MeterMap.Default, Tick.Zero)).Items);
    }

    [Fact]
    public void A_final_bar_line_inside_the_score_with_a_movement_heading_names_the_movement()
    {
        SectionEvidence evidence = Score(60, Before(31, "light-heavy")) with
        {
            Texts = [new MusicXmlText(At(31), 0, 30, "II. Andante", IsRehearsal: false), new MusicXmlText(At(31).Offset(Quarter + 1), 0, 30, "dolce", IsRehearsal: false)],
        };

        FoundSection second = Assert.Single(SectionFinder.Find(evidence).Items, section => !section.IsOpening);

        Assert.Equal(31, second.FirstBar);
        Assert.Equal("II. Andante", second.Heading);
        Assert.Equal("II. Andante, bars 31–60", second.TitleIn(Words.English));
        Assert.Equal("надпись «II. Andante», заключительная черта", second.ReasonIn(Words.Russian));
        Assert.Equal("likely", second.ConfidenceIn(Words.English));
    }

    [Fact]
    public void Boundaries_closer_than_eight_bars_keep_the_stronger_and_none_sits_near_the_start_or_the_end()
    {
        SectionEvidence evidence = Score(48, Before(4, "light-heavy"), Before(17, "light-heavy"), Before(21, "light-heavy"), Before(45, "light-heavy")) with
        {
            Keys = [new KeySignature(Tick.Zero, 0, KeyMode.Major), new KeySignature(At(21), 2, KeyMode.Major)],
        };

        FoundSections found = SectionFinder.Find(evidence);

        Assert.Equal([1, 21], found.Items.Select(section => section.FirstBar));
        Assert.Equal("final bar line, key changes to D major", found.Items[1].ReasonIn(Words.English));
    }

    [Fact]
    public void A_new_time_signature_and_tempo_that_hold_mark_a_boundary_but_a_ritardando_does_not()
    {
        var meter = new MeterMap([new MeterChange(Tick.Zero, TimeSignature.CommonTime), new MeterChange(At(17), new TimeSignature(3, 4))]);
        var notes = Bars(1, 16, _ => [60, 64, 67, 72]);
        notes.AddRange(Enumerable.Range(0, 16 * 3).Select(beat => Note(62, At(17).Value + (beat * Quarter))));
        PlaybackTempo[] tempos = [new(Tick.Zero, 500_000), new(At(17), 750_000), new(At(28), 760_000), new(new Tick(At(28).Value + Quarter), 800_000)];
        var evidence = new SectionEvidence(notes, meter, new Tick(At(17).Value + (16 * 3 * Quarter))) { Tempos = tempos };

        FoundSection second = Assert.Single(SectionFinder.Find(evidence).Items, section => !section.IsOpening);

        Assert.Equal(17, second.FirstBar);
        Assert.Equal(32, second.LastBar);
        Assert.Equal("time signature changes to 3/4, tempo changes to quarter = 80", second.ReasonIn(Words.English));
        Assert.Equal("смена размера на 3/4, смена темпа: четверть = 80", second.ReasonIn(Words.Russian));
    }

    [Fact]
    public void Whole_bars_of_rest_count_with_a_new_key()
    {
        var notes = Bars(1, 18, _ => [60, 64, 67]);
        notes.AddRange(Bars(21, 40, _ => [62, 65, 69]));
        var evidence = new SectionEvidence(notes, MeterMap.Default, new Tick(40 * Bar))
        {
            Keys = [new KeySignature(Tick.Zero, 0, null), new KeySignature(At(21), -1, null)],
        };

        FoundSection second = Assert.Single(SectionFinder.Find(evidence).Items, section => !section.IsOpening);

        Assert.Equal(21, second.FirstBar);
        Assert.Equal("new key signature: 1 ♭, 2 bars of rest before", second.ReasonIn(Words.English));
        Assert.Equal("новые ключевые знаки: 1 ♭, перед этим 2 такта паузы", second.ReasonIn(Words.Russian));
    }

    [Fact]
    public void A_silence_of_some_seconds_parts_a_recording_and_a_short_breath_does_not()
    {
        // 120 BPM: a bar of 4/4 lasts 2 s. Bars 19 and 20 are silent (4 s plus the last note's release), bar 30 has a one-beat breath.
        var notes = Bars(1, 18, _ => [60, 64, 67, 72], heard: true);
        notes.AddRange(Bars(21, 40, bar => bar == 30 ? [60] : [60, 64, 67, 72], heard: true).Where(note => note.Span.Onset != new Tick((29 * Bar) + Quarter)));
        var evidence = new SectionEvidence(notes, MeterMap.Default, new Tick(40 * Bar)) { Heard = true };

        FoundSections found = SectionFinder.Find(evidence);

        FoundSection second = Assert.Single(found.Items, section => !section.IsOpening);
        Assert.Equal(21, second.FirstBar);
        Assert.Equal("4 s of silence before", second.ReasonIn(Words.English));
        Assert.Equal("перед этим 4 с тишины", second.ReasonIn(Words.Russian));
        Assert.Equal("начало записи", found.Items[0].ReasonIn(Words.Russian));
    }

    [Fact]
    public void Around_a_silence_a_different_key_and_tempo_are_said_as_heard()
    {
        // Before: C major at 120 BPM; after: E flat minor at 80 BPM, the tempo set on every beat as a fitted grid has it.
        var notes = Bars(1, 18, _ => [60, 64, 67, 72], heard: true);
        notes.AddRange(Bars(21, 40, _ => [63, 66, 70, 75], heard: true));
        PlaybackTempo[] tempos =
        [
            .. Enumerable.Range(0, 18 * 4).Select(beat => new PlaybackTempo(new Tick(beat * Quarter), 500_000)),
            .. Enumerable.Range(20 * 4, 20 * 4).Select(beat => new PlaybackTempo(new Tick(beat * Quarter), 750_000)),
        ];
        var evidence = new SectionEvidence(notes, MeterMap.Default, new Tick(40 * Bar)) { Heard = true, Tempos = tempos };

        FoundSection second = Assert.Single(SectionFinder.Find(evidence).Items, section => !section.IsOpening);

        Assert.Equal(21, second.FirstBar);
        Assert.Contains(second.Cues, cue => cue.Kind == SectionCueKind.HeardKey);
        Assert.Contains("tempo about 120 before and about 80 after", second.ReasonIn(Words.English), StringComparison.Ordinal);
        Assert.Contains("по звучанию: до этого", second.ReasonIn(Words.Russian), StringComparison.Ordinal);
        Assert.StartsWith("4 s of silence before", second.ReasonIn(Words.English), StringComparison.Ordinal);
    }

    [Fact]
    public void A_recording_without_a_silence_is_one_piece_whatever_its_fitted_bars_and_tempo_do()
    {
        var meter = new MeterMap([new MeterChange(Tick.Zero, TimeSignature.CommonTime), new MeterChange(At(17), new TimeSignature(3, 4))]);
        var notes = Bars(1, 16, _ => [60, 64, 67, 72], heard: true);
        notes.AddRange(Enumerable.Range(0, 16 * 3).Select(beat => Note(62, At(17).Value + (beat * Quarter), heard: true)));
        PlaybackTempo[] tempos = [new(Tick.Zero, 500_000), new(At(17), 750_000)];
        var evidence = new SectionEvidence(notes, meter, new Tick(At(17).Value + (16 * 3 * Quarter))) { Tempos = tempos, Heard = true };

        Assert.Empty(SectionFinder.Find(evidence).Items);
        Assert.NotEmpty(SectionFinder.Find(evidence with { Heard = false }).Items);
    }

    [Fact]
    public void Seconds_follow_the_tempo_map_in_integer_microseconds()
    {
        PlaybackTempo[] tempos = [new(Tick.Zero, 500_000), new(new Tick(2 * Quarter), 1_000_000)];

        Assert.Equal(500_000 + 2_000_000, SectionFinder.Microseconds(tempos, new Tick(Quarter), new Tick(4 * Quarter)));
        Assert.Equal(1_000_000, SectionFinder.Microseconds([], Tick.Zero, new Tick(2 * Quarter)));
    }

    [Fact]
    public void A_moved_boundary_moves_the_end_of_the_section_before_and_says_where_it_was_found()
    {
        FoundSections found = SectionFinder.Find(Score(40, Before(21, "light-heavy")));

        Assert.False(found.CanMove(1, 5));
        Assert.False(found.CanMove(2, 1));
        Assert.False(found.CanMove(2, 41));
        Assert.True(found.CanMove(2, 40));
        Assert.Throws<ArgumentOutOfRangeException>(() => found.Move(2, 1));

        FoundSections moved = found.Move(2, 23);

        Assert.Equal((1, 22), (moved.Items[0].FirstBar, moved.Items[0].LastBar));
        Assert.Equal(At(23), moved.Items[0].Span.End);
        Assert.Equal((23, 40), (moved.Items[1].FirstBar, moved.Items[1].LastBar));
        Assert.Equal(TickSpan.FromBounds(At(23), new Tick(40 * Bar)), moved.Items[1].Span);
        Assert.Equal("moved from bar 21; found by: final bar line", moved.Items[1].ReasonIn(Words.English));
        Assert.Equal(21, moved.Move(2, 25).Items[1].MovedFrom);
        Assert.Same(moved, moved.Move(2, 23));
    }

    [Fact]
    public void A_dismissed_section_joins_the_one_before_and_once_none_is_left_nothing_is_proposed()
    {
        FoundSections found = SectionFinder.Find(Score(48, Before(17, "light-heavy"), Before(33, "light-heavy")));
        Assert.Equal([1, 17, 33], found.Items.Select(section => section.FirstBar));

        FoundSections fewer = found.Dismiss(2);

        Assert.Equal([1, 33], fewer.Items.Select(section => section.FirstBar));
        Assert.Equal([1, 2], fewer.Items.Select(section => section.Number));
        Assert.Equal(32, fewer.Items[0].LastBar);
        Assert.Equal(At(33), fewer.Items[0].Span.End);
        Assert.Empty(fewer.Dismiss(2).Items);
        Assert.Same(fewer, fewer.Dismiss(9));
    }

    [Fact]
    public void A_section_a_bookmark_already_starts_at_is_no_longer_pending()
    {
        FoundSections found = SectionFinder.Find(Score(40, Before(21, "light-heavy")));
        var bookmark = new Bookmark("b_0001", found.Items[1].TitleIn(Words.English), BookmarkCategory.StructureId, found.Items[1].Span, FromPhrase: false, DateTimeOffset.UnixEpoch);

        Assert.Equal([1], found.Pending([bookmark]).Select(section => section.Number));
        Assert.Equal(2, found.Pending([]).Count);
    }

    [Fact]
    public void The_same_evidence_gives_the_same_sections()
    {
        SectionEvidence evidence = Score(48, Before(17, "light-heavy"), Before(33, "light-light")) with
        {
            Keys = [new KeySignature(Tick.Zero, 0, KeyMode.Major), new KeySignature(At(33), 3, KeyMode.Minor)],
        };

        static string Read(FoundSections found) => string.Join(" | ", found.Items.Select(section => $"{section.Span} {section.Confidence} {section.ReasonIn(Words.English)}"));

        Assert.Equal(Read(SectionFinder.Find(evidence)), Read(SectionFinder.Find(evidence)));
    }

    [Fact]
    public void A_musicxml_score_brings_its_bar_lines_and_texts_and_its_movements_are_found()
    {
        ImportedPiece piece = PieceImporter.Import("two-movements.musicxml", TwoMovements()).Piece!;

        Assert.Contains(piece.MusicXml!.Barlines, barline => barline.IsDouble && barline.At == At(9) && barline.MeasureIndex == 8);
        Assert.Contains(piece.MusicXml.Barlines, barline => barline.IsFinal && barline.At == new Tick(16 * Bar));
        Assert.Contains(piece.MusicXml.Texts, text => text.Text == "Trio" && text.At == At(9) && !text.IsRehearsal);
        Assert.Contains(piece.MusicXml.Texts, text => text.Text == "A" && text.IsRehearsal);

        FoundSections found = SectionFinder.Find(SectionEvidence.Of(piece, piece.Notes, PlaybackScore.FromPiece(piece).Tempos));

        FoundSection trio = Assert.Single(found.Items, section => !section.IsOpening);
        Assert.Equal((9, 16), (trio.FirstBar, trio.LastBar));
        Assert.Equal("Trio, bars 9–16", trio.TitleIn(Words.English));
        Assert.Equal("надпись «Trio», двойная черта, смена тональности на фа мажор", trio.ReasonIn(Words.Russian));
    }

    [Fact]
    public void Two_pieces_in_one_file_part_where_the_second_begins_and_fur_elise_alone_is_one_piece()
    {
        static FoundSections Read(string path)
        {
            ImportedPiece piece = PieceImporter.Import(Path.GetFileName(path), File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, path))).Piece!;
            return SectionFinder.Find(SectionEvidence.Of(piece, piece.Notes, PlaybackScore.FromPiece(piece).Tempos));
        }

        FoundSections found = Read(Path.Combine("Analysis", "Fixtures", "two-pieces.musicxml"));

        Assert.Equal(2, found.Items.Count);
        Assert.Equal((0, 105), (found.Items[0].FirstBar, found.Items[0].LastBar));
        FoundSection ode = found.Items[1];
        Assert.Equal((106, 121), (ode.FirstBar, ode.LastBar));
        Assert.Equal("Ode to Joy, bars 106–121", ode.TitleIn(Words.English));
        Assert.Equal("likely", ode.ConfidenceIn(Words.English));
        Assert.Equal(
            "final bar line, key changes to C major, heading “Ode to Joy”, time signature changes to 4/4, tempo changes to quarter = 100",
            ode.ReasonIn(Words.English));
        Assert.Empty(Read(Path.Combine("Import", "MusicXml", "Fixtures", "FurElise.musicxml")).Items);
    }

    /// <summary>Sixteen bars of 4/4 in C major; a double bar line before bar 9, where "Trio" begins in F major; a final bar line at the end.</summary>
    private static byte[] TwoMovements()
    {
        var xml = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><score-partwise version="4.0"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list><part id="P1">""");
        for (int measure = 1; measure <= 16; measure++)
        {
            xml.Append($"<measure number=\"{measure}\">");
            if (measure == 1)
            {
                xml.Append("<attributes><divisions>1</divisions><key><fifths>0</fifths><mode>major</mode></key><time><beats>4</beats><beat-type>4</beat-type></time></attributes>");
                xml.Append("<direction><direction-type><rehearsal>A</rehearsal></direction-type></direction>");
            }

            if (measure == 9)
            {
                xml.Append("<attributes><key><fifths>-1</fifths><mode>major</mode></key></attributes>");
                xml.Append("<direction placement=\"above\"><direction-type><words>Trio</words></direction-type></direction>");
            }

            xml.Append("<note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note>");
            if (measure is 8 or 16)
            {
                xml.Append($"<barline location=\"right\"><bar-style>{(measure == 8 ? "light-light" : "light-heavy")}</bar-style></barline>");
            }

            xml.Append("</measure>");
        }

        xml.Append("</part></score-partwise>");
        return Encoding.UTF8.GetBytes(xml.ToString());
    }
}