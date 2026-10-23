using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.MusicXml;

/// <summary>The self-made files in Fixtures and what each must import as.</summary>
public class MusicXmlImportFixtureTests
{
    private static MusicXmlImport ImportFixture(string name) =>
        MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", name)));

    [Fact]
    public void Grand_staff_with_pickup_splits_hands_by_staff()
    {
        MusicXmlImport result = ImportFixture("importer-grand-staff.musicxml");
        const int bar = 3 * 480;

        Assert.Equal("4.0", result.Version);
        MusicXmlPartInfo part = Assert.Single(result.Parts);
        Assert.Equal(("P1", "Piano", "Acoustic Grand Piano", 2, true), (part.Id, part.Name, part.InstrumentName, part.Staves, part.HasHands));
        Assert.Equal(13, result.Notes.Count); // 14 written: the E5 tied across bars 2 and 3 is one note.

        // Pickup: one beat, bar 1 starts after it (0004).
        Assert.True(result.Meter.HasPickup);
        Assert.Equal(480, result.Meter.PickupDuration);
        Assert.Equal(new Tick(480), result.Meter.GetBar(1).Span.Onset);
        Assert.Equal(new MetricPosition(0, 3, 0), result.Meter.ToPosition(result.Notes[0].Span.Onset));
        Assert.Equal(new MusicXmlTimeSignature(Tick.Zero, 0, 3, 4, false), Assert.Single(result.TimeSignatures));
        Assert.Equal([true, false, false, false], result.Measures.Select(measure => measure.IsImplicit));
        Assert.Equal([0L, 480, 480 + bar, 480 + (2 * bar)], result.Measures.Select(measure => measure.Span.Onset.Value));

        // Hands come from the staff and are imported facts (S1-I4).
        Assert.All(result.Notes, note =>
        {
            Assert.Equal(Attribution.Imported, note.Hand!.Attribution);
            var source = Assert.IsType<MusicXmlSourceLocation>(note.Source);
            Assert.Equal(source.Staff == 1 ? Hand.Right : Hand.Left, note.Hand.Value);
        });
        Assert.Equal([43, 50, 43], result.Notes.Where(note => note.Hand!.Value == Hand.Left).Select(note => note.Pitch.MidiNumber));
        Assert.All(result.Notes.Where(note => note.Hand!.Value == Hand.Left), note => Assert.Equal(5, note.Voice));

        // The chord shares one onset; the A after it starts on beat 3.
        Assert.Equal(
            [(67, 480L, 960L), (71, 480, 960), (74, 480, 960), (69, 1440, 480)],
            result.Notes.Where(note => note.Hand!.Value == Hand.Right && note.Span.Onset.Value is >= 480 and < 1920)
                .Select(note => (note.Pitch.MidiNumber, note.Span.Onset.Value, note.Span.Duration)));

        // Triplet eighths after a divisions change are exact, and the tied E5 is one note (AC-S1-02).
        Assert.Equal(
            [(71, 1920L, 160L), (72, 2080, 160), (74, 2240, 160), (76, 2400, 1440)],
            result.Notes.Where(note => note.Hand!.Value == Hand.Right && note.Span.Onset.Value is >= 1920 and < 3360)
                .Select(note => (note.Pitch.MidiNumber, note.Span.Onset.Value, note.Span.Duration)));
        Assert.All(result.SourceNotes, note => Assert.True(note.IsExact));

        // Velocity: sound dynamics 70% of forte, and a note's own dynamics.
        Assert.Equal(99, Assert.Single(result.Notes, note => note.Span.Onset.Value == 3840).Velocity);
        Assert.All(result.Notes.Where(note => note.Span.Onset.Value != 3840), note => Assert.Equal(63, note.Velocity));

        var tempo = Assert.Single(result.Tempos);
        Assert.Equal((Tick.Zero, 96m, false, 625_000), (tempo.At, tempo.QuarterNotesPerMinute, tempo.IsAssumed, tempo.MicrosecondsPerQuarter));
        Assert.Equal(new MusicXmlKeySignature(Tick.Zero, 0, 0, 1, "major"), Assert.Single(result.KeySignatures));
        Assert.Equal(new Tick(480 + (3 * bar)), result.End);

        Assert.Equal(("MusicXML 4.0", 1, 13, 4), (result.Report.Format, result.Report.TrackCount, result.Report.NoteCount, result.Report.MeasureCount));
        Assert.Equal(
            [(MusicXmlIssueCodes.GraceNotesFlagged, 1)],
            result.Report.Issues.Select(issue => (issue.Code, issue.Count)));
    }

    [Fact]
    public void Two_parts_keep_their_own_divisions_keys_and_transposition()
    {
        MusicXmlImport result = ImportFixture("importer-two-parts.musicxml");

        Assert.Equal(["Clarinet in B♭", "Cello"], result.Parts.Select(part => part.Name));
        Assert.Equal([7, 4], result.Parts.Select(part => part.NoteCount));
        Assert.All(result.Notes, note => Assert.Null(note.Hand));

        // The clarinet's written pitches sound a tone lower.
        Assert.Equal(
            [74, 76, 77, 79, 81, 72, 74],
            result.Notes.Where(note => note.Track == 0).Select(note => note.Pitch.MidiNumber));
        Assert.Equal(
            [(48, 0L, 960L), (43, 960, 960), (48, 1920, 1920), (41, 3840, 1440)],
            result.Notes.Where(note => note.Track == 1).Select(note => (note.Pitch.MidiNumber, note.Span.Onset.Value, note.Span.Duration)));

        // The same tempo in both parts is one change; a metronome mark counts as a tempo.
        Assert.Equal([(0L, 72m), (1920L, 80m)], result.Tempos.Select(tempo => (tempo.At.Value, tempo.QuarterNotesPerMinute)));
        Assert.Equal(
            [new MusicXmlTimeSignature(Tick.Zero, 0, 4, 4, false), new MusicXmlTimeSignature(new Tick(3840), 2, 6, 8, false)],
            result.TimeSignatures);
        Assert.False(result.Meter.HasPickup);
        Assert.Equal(new MetricPosition(3, 4, 0), result.Meter.ToPosition(new Tick(3840 + 720)));
        Assert.Equal(
            [new MusicXmlKeySignature(Tick.Zero, 0, 0, 2, null), new MusicXmlKeySignature(Tick.Zero, 1, 0, 0, "major"), new MusicXmlKeySignature(new Tick(3840), 1, 2, -1, "minor")],
            result.KeySignatures);
        Assert.Equal(new Tick(3840 + 1440), result.End);

        // No playback dynamics anywhere: every note has the default velocity, and the report says so.
        Assert.All(result.Notes, note => Assert.Equal(MusicXmlImporter.DefaultVelocity, note.Velocity));
        ImportIssue issue = Assert.Single(result.Report.Issues);
        Assert.Equal((ImportIssueKind.Assumed, MusicXmlIssueCodes.VelocityDefault, 11), (issue.Kind, issue.Code, issue.Count));
        Assert.Equal(3, result.Report.MeasureCount);
    }

    [Fact]
    public void Ties_merge_triplets_are_exact_and_bar_1_follows_the_pickup()
    {
        MusicXmlImport result = ImportFixture("ties-triplets-pickup.musicxml");

        // AC-S1-02: 20 written notes, 18 after merging the two ties.
        Assert.Equal(18, result.Notes.Count);
        Assert.Equal(18, result.Parts[0].NoteCount);
        Assert.Equal(2, result.SourceNotes.Count(note => note.WrittenNotes == 2));

        // D5 quarter + eighth inside bar 1, and G5 across the bar line from bar 3 to bar 4.
        MusicXmlNote d5 = Assert.Single(result.SourceNotes, note => note.WrittenNotes == 2 && note.Pitch == 74);
        Assert.Equal((new QuarterFraction(9, 6), 1, new TickSpan(new Tick(480), 720)), (d5.Duration, d5.MeasureIndex, d5.Span));
        MusicXmlNote g5 = Assert.Single(result.SourceNotes, note => note.WrittenNotes == 2 && note.Pitch == 79);
        Assert.Equal((new QuarterFraction(18, 6), 3), (g5.Duration, g5.MeasureIndex));
        Assert.Equal(1440, Assert.Single(result.Notes, note => note.Span.Onset == g5.Span.Onset && note.Pitch.MidiNumber == 79).Span.Duration);

        // Triplet onsets are exact and flagged; bar 1 starts after the one-beat pickup.
        Assert.All(result.SourceNotes, note => Assert.True(note.IsExact));
        Assert.Equal(9, result.SourceNotes.Count(note => note.Tuplet == new MusicXmlTuplet(3, 2)));
        Assert.Equal(new Tick(480), result.Meter.GetBar(1).Span.Onset);
        Assert.Equal(0, result.Report.CountOf(MusicXmlIssueCodes.NotesRounded));
        Assert.Equal(0, result.Report.CountOf(MusicXmlIssueCodes.TieWithoutStart));
    }

    [Fact]
    public void Compressed_score_imports_like_the_uncompressed_one()
    {
        MusicXmlImport compressed = ImportFixture("grand-staff.mxl");
        MusicXmlImport plain = ImportFixture("grand-staff.musicxml");

        Assert.Equal(plain.Notes, compressed.Notes);
        Assert.Equal(plain.Rests, compressed.Rests);
        Assert.Equal(plain.Report, compressed.Report with { Issues = plain.Report.Issues });
        Assert.Equal(plain.Report.Issues, compressed.Report.Issues);
        Assert.Equal(26, compressed.Notes.Count);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "grand-staff.musicxml")),
            MusicXmlImporter.ReadScoreXml(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "grand-staff.mxl"))));
    }

    [Fact]
    public void Septuplets_are_rounded_by_at_most_half_a_tick_and_reported()
    {
        MusicXmlImport result = ImportFixture("importer-septuplets.musicxml");

        Assert.Equal(8, result.Notes.Count);
        Assert.Equal(7, result.Report.CountOf(MusicXmlIssueCodes.NotesRounded));
        Assert.Equal(1, result.Report.CountOf(MusicXmlIssueCodes.TempoDefault));
        Assert.True(result.Tempos[0].IsAssumed);

        // AC-S1-03: no boundary is more than half a tick from its exact position, and nothing drifts.
        Assert.All(result.SourceNotes, note =>
        {
            AssertWithinHalfTick(note.Onset, note.Span.Onset);
            AssertWithinHalfTick(note.Onset + note.Duration, note.Span.End);
        });
        Assert.Equal([0L, 69, 137, 206, 274, 343, 411, 480], result.Notes.Select(note => note.Span.Onset.Value));
        Assert.True(result.SourceNotes[^1].IsExact);
        Assert.Equal(new Tick(1920), result.End);
    }

    private static void AssertWithinHalfTick(QuarterFraction exact, Tick rounded)
    {
        QuarterFraction error = rounded.ToQuarters() - exact;
        QuarterFraction halfTick = new(1, 2 * CanonicalTime.TicksPerQuarter);
        Assert.True(error <= halfTick && error >= QuarterFraction.Zero - halfTick, $"{rounded} is {error} from {exact}.");
    }
}