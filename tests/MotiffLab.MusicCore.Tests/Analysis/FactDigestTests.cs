using System.Runtime.CompilerServices;
using System.Text;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>The fact digest and its template explanations (S1-AI0, S1-AI1).</summary>
public class FactDigestTests
{
    /// <summary>Set to 1 to rewrite the snapshot after an intended change to a fact id or text.</summary>
    private const string UpdateVariable = "MOTIFF_UPDATE_SNAPSHOTS";

    private const string SnapshotName = "grand-staff-bars-1-2.digest.txt";

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", name));

    private static ImportedPiece Import(string fileName, byte[] data) =>
        PieceImporter.Import(fileName, data).Piece ?? throw new InvalidOperationException("The fixture did not import.");

    /// <summary>The facts the playback lab shows for bars 1–2 of a piece, from every analyser.</summary>
    private static (IReadOnlyList<MotifFact> Facts, MotifSelection Selection) Analyze(ImportedPiece piece, int firstBar = 1, int lastBar = 2)
    {
        TickSpan span = MotifSelector.Bars(piece.Meter, firstBar, lastBar, piece.End);
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, span, SelectionFilter.All);
        PhraseMarks marks = piece.MusicXml?.PhraseMarks ?? PhraseMarks.None;
        IReadOnlyList<PhraseBoundary> phrases = PhraseBoundaryFinder.Find(piece.Notes, piece.Meter, marks);
        MotifAnalysis motif = MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, KeySignature.InForce(piece.Keys, span.Onset), piece.MusicXml?.AccentMarks, phrases);
        StructureAnalysis structure = StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, phrases);
        IReadOnlyList<MotifFact> character = MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, KeySignature.InForce(piece.Keys, span.Onset), CharacterContext.FromPiece(piece));
        return ([.. motif.Facts, .. structure.Facts, .. character], selection);
    }

    private static FactDigest GrandStaffDigest() =>
        FactDigest.ForMotif(Analyze(Import("grand-staff.musicxml", Fixture("grand-staff.musicxml"))).Facts);

    private static MotifFact Fact(string id, FactLens lens, string text = "Text.", FactCertainty certainty = FactCertainty.Detected) =>
        new(id, lens, certainty, text, "Basis that stays on the device.");

    [Fact]
    public void Digest_of_a_fixed_file_matches_its_snapshot()
    {
        // A changed fact id fails here, so ids that notebooks, observations and AI cite stay stable (decision record 0009).
        string actual = GrandStaffDigest().ToText();
        string snapshot = Path.Combine(AppContext.BaseDirectory, "Analysis", "Snapshots", SnapshotName);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(SourceSnapshotPath(), actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(File.Exists(snapshot), $"No snapshot at {snapshot}; run the tests with {UpdateVariable}=1 to write it.");
        Assert.Equal(File.ReadAllText(snapshot).ReplaceLineEndings("\n"), actual);
    }

    [Fact]
    public void Same_facts_give_the_same_digest()
    {
        Assert.Equal(GrandStaffDigest().ToText(), GrandStaffDigest().ToText());
        Assert.Equal(GrandStaffDigest().Facts, GrandStaffDigest().Facts);
    }

    [Fact]
    public void Digest_holds_no_file_name_title_composer_part_name_or_note_ids()
    {
        string xml = Encoding.UTF8.GetString(Fixture("grand-staff.musicxml"))
            .Replace("</work>", "</work>\n  <identification><creator type=\"composer\">Clara Private</creator></identification>", StringComparison.Ordinal);
        ImportedPiece piece = Import("My secret lesson.musicxml", Encoding.UTF8.GetBytes(xml));
        Assert.Contains("<work-title>Grand staff waltz</work-title>", xml, StringComparison.Ordinal);

        string text = FactDigest.ForMotif(Analyze(piece).Facts).ToText();

        foreach (string secret in new[] { "My secret lesson", "secret", ".musicxml", "Grand staff waltz", "Clara Private", "Piano" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.All(piece.Notes, note => Assert.DoesNotContain($" {note.Id} ", text, StringComparison.Ordinal));
    }

    [Fact]
    public void Digest_holds_facts_only_and_not_the_notes_outside_the_motif()
    {
        ImportedPiece piece = Import("grand-staff.musicxml", Fixture("grand-staff.musicxml"));
        (IReadOnlyList<MotifFact> facts, _) = Analyze(piece);

        FactDigest digest = FactDigest.ForMotif(facts);

        // Every line after the two header lines is one analyser fact, and nothing else.
        string[] lines = digest.ToText().TrimEnd('\n').Split('\n');
        Assert.Equal([$"engine {FactDigest.CurrentEngineVersion}", "subject motif"], lines[..2]);
        Assert.Equal(facts.Count, lines.Length - 2);
        Assert.Equal(facts.OrderBy(fact => fact.Lens).Select(fact => fact.Id), digest.Facts.Select(fact => fact.Id));
        Assert.DoesNotContain("Basis that stays", digest.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Digest_carries_the_engine_version_and_groups_facts_by_lens()
    {
        FactDigest digest = FactDigest.ForMotif([Fact("structure.phrases", FactLens.Structure), Fact("melody.line", FactLens.Melody), Fact("key", FactLens.Key), Fact("rhythm.grid", FactLens.Rhythm)]);

        Assert.Equal(FactDigest.CurrentEngineVersion, digest.EngineVersion);
        Assert.Equal(FactDigest.MotifSubject, digest.Subject);
        Assert.Equal(["rhythm.grid", "melody.line", "key", "structure.phrases"], digest.Facts.Select(fact => fact.Id));
    }

    [Fact]
    public void Duplicate_fact_ids_are_refused()
    {
        Assert.Throws<ArgumentException>(() => FactDigest.ForMotif([Fact("key", FactLens.Key), Fact("key", FactLens.Key)]));
    }

    [Fact]
    public void Digest_of_a_fixed_file_stays_well_inside_the_request_limit()
    {
        // Decision record 0003 caps a request at 8 KB, and the digest shares it with mode, Keep choices and ids.
        Assert.InRange(Encoding.UTF8.GetByteCount(GrandStaffDigest().ToText()), 1, 6 * 1024);
    }

    [Fact]
    public void Variation_digest_adds_what_the_step_changed_and_kept()
    {
        ImportedPiece piece = Import("grand-staff.musicxml", Fixture("grand-staff.musicxml"));
        (IReadOnlyList<MotifFact> facts, MotifSelection selection) = Analyze(piece, 1, 1);
        Variation variation = RhythmReshape.Instance.Apply(piece.Notes, selection, new HashSet<KeepProperty> { KeepProperty.Pitches, KeepProperty.Melody }, new RhythmReshapeParameters(RhythmReshapeKind.HalfTime));

        FactDigest digest = FactDigest.ForVariation(facts, variation);

        Assert.Equal(FactDigest.VariationSubject, digest.Subject);
        Assert.Equal([FactDigest.OperationId, FactDigest.ChangesId, FactDigest.KeepId], digest.Facts.TakeLast(3).Select(fact => fact.Id));
        Assert.Equal("Operation: V1.", digest.Find(FactDigest.OperationId)!.Text);
        Assert.Equal(variation.Explanation, digest.Find(FactDigest.ChangesId)!.Text);
        Assert.Equal("Kept: pitches, melody.", digest.Find(FactDigest.KeepId)!.Text);
    }

    [Fact]
    public void Motif_template_cites_only_digest_facts_and_states_only_their_sentences()
    {
        FactDigest digest = GrandStaffDigest();

        TemplateExplanation explanation = FactTemplates.Explain(digest);

        Assert.InRange(explanation.Points.Count, 2, FactTemplates.MaxPoints);
        Assert.All(explanation.CitedIds, id => Assert.True(digest.Contains(id), $"{id} is not in the digest."));
        Assert.All(explanation.Points, point => Assert.Equal(string.Join(" ", point.FactIds.Select(id => digest.Find(id)!.Text)), point.Text));
        Assert.Equal("melody.contour", explanation.CitedIds[0]);
    }

    [Fact]
    public void Template_skips_facts_the_digest_lacks_and_reports_the_weakest_certainty()
    {
        FactDigest digest = FactDigest.ForMotif(
        [
            Fact("melody.contour", FactLens.Melody, "Rises.", FactCertainty.Inferred),
            Fact("melody.range", FactLens.Melody, "Range: C4 to G4."),
            Fact("rhythm.grouping", FactLens.Rhythm, "Onsets per beat: 1 1."),
        ]);

        TemplateExplanation explanation = FactTemplates.Explain(digest);

        Assert.Equal(2, explanation.Points.Count);
        Assert.Equal(new TemplatePoint("Rises. Range: C4 to G4.", ["melody.contour", "melody.range"], FactCertainty.Inferred), explanation.Points[0], new PointComparer());
        Assert.Equal(["rhythm.grouping"], explanation.Points[1].FactIds);
        Assert.Equal("Rises. Range: C4 to G4. Onsets per beat: 1 1.", explanation.Text);
    }

    [Fact]
    public void Variation_template_leads_with_the_change_and_the_keep_checks()
    {
        ImportedPiece piece = Import("grand-staff.musicxml", Fixture("grand-staff.musicxml"));
        (IReadOnlyList<MotifFact> facts, MotifSelection selection) = Analyze(piece, 1, 1);
        Variation variation = RhythmReshape.Instance.Apply(piece.Notes, selection, new HashSet<KeepProperty> { KeepProperty.Pitches }, new RhythmReshapeParameters(RhythmReshapeKind.DoubleTime));

        TemplateExplanation explanation = FactTemplates.Explain(FactDigest.ForVariation(facts, variation));

        Assert.Equal([FactDigest.ChangesId], explanation.Points[0].FactIds);
        Assert.Equal([FactDigest.KeepId], explanation.Points[1].FactIds);
        Assert.Equal(variation.Explanation, explanation.Points[0].Text);
        Assert.True(explanation.Points.Count <= FactTemplates.MaxPoints);
    }

    [Fact]
    public void Same_digest_gives_the_same_template()
    {
        Assert.Equal(FactTemplates.Explain(GrandStaffDigest()).Text, FactTemplates.Explain(GrandStaffDigest()).Text);
    }

    private static string SourceSnapshotPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Snapshots", SnapshotName);

    /// <summary>Records holding lists compare the lists by reference; this compares their items.</summary>
    private sealed class PointComparer : IEqualityComparer<TemplatePoint>
    {
        public bool Equals(TemplatePoint? x, TemplatePoint? y) =>
            x is not null && y is not null && x.Text == y.Text && x.Certainty == y.Certainty && x.FactIds.SequenceEqual(y.FactIds);

        public int GetHashCode(TemplatePoint obj) => obj.Text.GetHashCode(StringComparison.Ordinal);
    }
}