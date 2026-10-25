using System.Text;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>Observations: a variation or a note the player keeps about a phrase, saved with the project (#78).</summary>
public class ObservationTests
{
    private static readonly DateTimeOffset s_created = new(2026, 10, 23, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_kept = new(2026, 10, 25, 18, 45, 30, TimeSpan.FromHours(1));
    private static readonly MusicalKey s_key = new(0, KeyMode.Major);

    private static readonly byte[] s_source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "importer-grand-staff.musicxml"));

    private static ImportedPiece Piece() => PieceImporter.Import("minuet.musicxml", s_source).Piece!;

    private static MotifSelection Bars(ImportedPiece piece, int first, int last, SelectionFilter? filter = null) =>
        MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, first, last, piece.End), filter ?? SelectionFilter.All);

    /// <summary>A chain over the selection, in the key and meter of the piece, with the steps accepted and one pending.</summary>
    private static VariationChain Chain(ImportedPiece piece, MotifSelection selection, IReadOnlySet<KeepProperty> keep, MusicalKey? key, (IVariationOperation, OperationParameters)[] steps, (IVariationOperation, OperationParameters)? pending = null)
    {
        VariationChain chain = VariationChain.Start(VariationFacts.MotifNotes(piece.Notes, selection), VariationContext.For(selection, keep, key: key, meter: piece.Meter));
        foreach ((IVariationOperation operation, OperationParameters parameters) in steps)
        {
            chain = chain.Propose(operation, parameters).Accept();
        }

        return pending is var (op, values) ? chain.Propose(op, values) : chain;
    }

    /// <summary>
    /// The minuet with three observations: an experiment kept on bars 1–2 with a note, a variation
    /// made by hand in the right hand of bars 1–2 with a step still pending, and a note alone on bar 3.
    /// </summary>
    private static Project Worked()
    {
        ImportedPiece piece = Piece();
        MotifSelection opening = Bars(piece, 1, 2);
        MotifSelection right = Bars(piece, 1, 2, SelectionFilter.ForHand(Hand.Right));
        VariationChain tried = Chain(piece, opening, new HashSet<KeepProperty>(), s_key, [(MelodicSequence.Instance, new MelodicSequenceParameters(1)), (Articulation.Instance, new ArticulationParameters(ArticulationKind.Detached) { Scope = OperationScope.Melody })]);
        VariationChain handMade = Chain(piece, right, new HashSet<KeepProperty> { KeepProperty.PitchOrder, KeepProperty.Pitches }, null, [(RhythmReshape.Instance, new RhythmReshapeParameters(RhythmReshapeKind.HalfTime))], (RhythmReshape.Instance, new RhythmReshapeParameters(RhythmReshapeKind.Displace, 2)));

        return Project.Create("p_test", "minuet.musicxml", s_source, s_created)
            .Keep(Observation.Create("o_0001", SavedPassage.Of(opening), KeptVariation.Create(tried, opening.Filter), "  Higher answer ", "Sounds like a question now.", s_kept, (ExperimentCatalogue.GrooveId, 1)))
            .Keep(Observation.Create("o_0002", SavedPassage.Of(opening), KeptVariation.Create(handMade, right.Filter), "Half time", null, s_kept.AddMinutes(5)))
            .Keep(Observation.Create("o_0003", SavedPassage.Of(Bars(piece, 3, 3)), null, "Bar 3", "The left hand leads here.", s_kept.AddMinutes(9)));
    }

    [Fact]
    public void Observations_round_trip_through_the_project_file()
    {
        Project project = Worked();

        Project reread = ProjectFile.Read(ProjectFile.Write(project));

        Assert.Equal(project.Observations, reread.Observations);
        Observation tried = reread.Observations[0];
        Assert.Equal(("Higher answer", "Sounds like a question now.", ExperimentCatalogue.GrooveId, 1), (tried.Name, tried.Note, tried.ExperimentId, tried.Alternative));
        Assert.Equal(s_key, tried.Variation!.Key);
        Assert.Equal(s_kept, tried.CreatedAt);
        Assert.Equal(OperationScope.Melody, tried.Variation.Steps[1].Parameters.Scope);
        Assert.Equal([KeepProperty.Pitches, KeepProperty.PitchOrder], reread.Observations[1].Variation!.Keep);
        Assert.NotNull(reread.Observations[1].Variation!.Pending);
        Assert.Equal((null, null, null), (reread.Observations[1].ExperimentId, reread.Observations[1].Alternative, reread.Observations[1].Variation!.Key));
        Assert.Null(reread.Observations[2].Variation);
    }

    [Fact]
    public void Writing_observations_is_deterministic_and_round_trips_byte_for_byte()
    {
        byte[] written = ProjectFile.Write(Worked());
        Assert.Equal(written, ProjectFile.Write(Worked()));
        Assert.Equal(written, ProjectFile.Write(ProjectFile.Read(written)));
    }

    [Fact]
    public void Opening_rebuilds_each_kept_variation_as_it_was_heard()
    {
        Project project = Worked();
        ImportedPiece piece = Piece();
        VariationChain tried = Chain(piece, Bars(piece, 1, 2), new HashSet<KeepProperty>(), s_key, [(MelodicSequence.Instance, new MelodicSequenceParameters(1)), (Articulation.Instance, new ArticulationParameters(ArticulationKind.Detached) { Scope = OperationScope.Melody })]);

        OpenedProject opened = OpenedProject.Open(ProjectFile.Read(ProjectFile.Write(project)));

        Assert.Equal(project.Observations, opened.Observations.Select(observation => observation.Saved));
        VariationChain first = opened.Observations[0].Chain!;
        Assert.Equal("V4 › V13", first.Result!.Operation);
        Assert.Equal(tried.Result!.Notes, first.Result.Notes);
        Assert.Equal(tried.Result.Explanation, first.Result.Explanation);
        Assert.Equal(s_key, first.Context.Key);
        Assert.NotNull(first.Context.Meter);
        VariationChain second = opened.Observations[1].Chain!;
        Assert.Single(second.Steps);
        Assert.NotNull(second.Pending);
        Assert.All(second.Original, note => Assert.True(SelectionFilter.ForHand(Hand.Right).Matches(note)));
        Assert.Null(opened.Observations[2].Chain);
    }

    [Fact]
    public void A_variation_over_picked_notes_is_rebuilt_from_its_notes()
    {
        ImportedPiece piece = Piece();
        MotifSelection picked = MotifSelector.SelectNotes(piece.Notes, piece.Meter, piece.Notes.Take(3).Select(note => note.Id));
        VariationChain chain = Chain(piece, picked, new HashSet<KeepProperty>(), null, [(RhythmReshape.Instance, new RhythmReshapeParameters(RhythmReshapeKind.HalfTime))]);
        Project project = Project.Create("p", "minuet.musicxml", s_source, s_created)
            .Keep(Observation.Create("o_1", SavedPassage.Of(Bars(piece, 0, 1)), KeptVariation.Create(chain, picked.Filter), "Three notes", null, s_kept));

        VariationChain rebuilt = OpenedProject.Open(ProjectFile.Read(ProjectFile.Write(project))).Observations[0].Chain!;

        Assert.Equal(chain.Original, rebuilt.Original);
        Assert.Equal(chain.Result!.Notes, rebuilt.Result!.Notes);
    }

    [Fact]
    public void A_kept_variation_that_no_longer_matches_the_piece_is_reported()
    {
        Project project = Worked();
        Observation first = project.Observations[0];
        Project broken = project with { Observations = [first with { Variation = first.Variation! with { NoteIds = ["n_9999"] } }] };

        var exception = Assert.Throws<ProjectFileException>(() => OpenedProject.Open(broken));

        Assert.Contains("Higher answer", exception.Message);
    }

    [Fact]
    public void A_kept_step_that_can_no_longer_run_is_reported()
    {
        Project project = Worked();
        Observation first = project.Observations[0];
        Project broken = project with { Observations = [first with { Variation = first.Variation! with { Key = null } }] };

        var exception = Assert.Throws<ProjectFileException>(() => OpenedProject.Open(broken));

        Assert.Contains("no longer be made", exception.Message);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Older_files_open_with_no_observations(int version)
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked() with { Observations = [] }))
            .Replace($"\"version\": {ProjectFile.Version}", $"\"version\": {version}", StringComparison.Ordinal)
            .Replace(",\n  \"observations\": []", "", StringComparison.Ordinal);
        Assert.DoesNotContain("observations", json);

        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(json));

        Assert.Empty(project.Observations);
        Assert.Empty(OpenedProject.Open(project).Observations);
    }

    [Fact]
    public void A_version_6_file_needs_its_observations()
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked() with { Observations = [] }))
            .Replace(",\n  \"observations\": []", "", StringComparison.Ordinal);

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("\"alternative\": 1", "\"alternative\": -1")]
    [InlineData("\"fifths\": 0", "\"fifths\": 9")]
    [InlineData("\"mode\": \"Major\"", "\"mode\": \"Lydian\"")]
    [InlineData("\"name\": \"Half time\"", "\"name\": \" \"")]
    [InlineData("\"id\": \"o_0002\"", "\"id\": \"o_0001\"")]
    [InlineData("\"note\": \"The left hand leads here.\"", "\"note\": \"   \"")]
    [InlineData("\"createdAt\": \"2026-10-25T18:45:30", "\"createdAt\": \"yesterday")]
    public void Damaged_observations_are_rejected(string field, string damaged)
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked()));
        Assert.Contains(field, json);

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace(field, damaged, StringComparison.Ordinal))));
    }

    [Fact]
    public void The_note_stays_out_of_the_file_when_there_is_none()
    {
        Project project = Worked();
        string json = Encoding.UTF8.GetString(ProjectFile.Write(project with { Observations = [project.Observations[1]] }));

        Assert.DoesNotContain("\"note\"", json);
        Assert.DoesNotContain("\"experiment\"", json);
        Assert.DoesNotContain("\"key\"", json);
    }

    [Fact]
    public void Creating_trims_the_name_and_the_note_and_drops_a_blank_note()
    {
        Project project = Worked();
        KeptVariation variation = project.Observations[1].Variation!;
        SavedPassage phrase = project.Observations[1].Phrase;

        Observation observation = Observation.Create("o_9", phrase, variation, "  Mine ", "  \n ", s_kept);

        Assert.Equal(("Mine", null), (observation.Name, observation.Note));
        Assert.Throws<ArgumentException>(() => Observation.Create("o_9", phrase, variation, " ", null, s_kept));
        Assert.Throws<ArgumentException>(() => Observation.Create("o_9", phrase, null, "Nothing", " ", s_kept));
        Assert.Throws<ArgumentException>(() => Observation.Create("o_9", phrase, null, "Tried", "A note", s_kept, (ExperimentCatalogue.GrooveId, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Observation.Create("o_9", phrase, variation, "Tried", null, s_kept, (ExperimentCatalogue.GrooveId, -1)));
    }

    [Fact]
    public void A_kept_variation_needs_a_step()
    {
        ImportedPiece piece = Piece();
        MotifSelection opening = Bars(piece, 1, 2);
        VariationChain empty = VariationChain.Start(VariationFacts.MotifNotes(piece.Notes, opening), VariationContext.For(opening, new HashSet<KeepProperty>()));

        Assert.Throws<ArgumentException>(() => KeptVariation.Create(empty, opening.Filter));
    }

    [Fact]
    public void The_phrase_shows_the_observations_that_overlap_it_in_the_order_they_were_kept()
    {
        Project project = Worked();
        ImportedPiece piece = Piece();
        TickSpan opening = MotifSelector.Bars(piece.Meter, 1, 2, piece.End);
        TickSpan bar2 = MotifSelector.Bars(piece.Meter, 2, 2, piece.End);
        TickSpan bars2To3 = MotifSelector.Bars(piece.Meter, 2, 3, piece.End);
        TickSpan pickup = MotifSelector.Bars(piece.Meter, 0, 0, piece.End);

        Assert.Equal(["o_0001", "o_0002"], project.ObservationsAbout(opening).Select(observation => observation.Id));
        Assert.Equal(["o_0001", "o_0002"], project.ObservationsAbout(bar2).Select(observation => observation.Id));
        Assert.Equal(["o_0001", "o_0002", "o_0003"], project.ObservationsAbout(bars2To3).Select(observation => observation.Id));
        Assert.Empty(project.ObservationsAbout(pickup));
    }

    [Fact]
    public void Renaming_changes_only_that_observation()
    {
        Project project = Worked();

        Project renamed = project.RenameObservation("o_0002", "  Slow version ");

        Assert.Equal(["Higher answer", "Slow version", "Bar 3"], renamed.Observations.Select(observation => observation.Name));
        Assert.Equal(project.Observations[1] with { Name = "Slow version" }, renamed.Observations[1]);
        Assert.Equal("Half time", project.Observations[1].Name);
        Assert.Throws<ArgumentException>(() => project.RenameObservation("o_0002", ""));
        Assert.Throws<KeyNotFoundException>(() => project.RenameObservation("o_9999", "Lost"));
    }

    [Fact]
    public void Deleting_removes_only_that_observation()
    {
        Project project = Worked();

        Project deleted = project.DeleteObservation("o_0001");

        Assert.Equal(["o_0002", "o_0003"], deleted.Observations.Select(observation => observation.Id));
        Assert.Equal(3, project.Observations.Count);
        Assert.Throws<KeyNotFoundException>(() => deleted.DeleteObservation("o_0001"));
    }

    [Fact]
    public void Keeping_the_same_id_twice_is_refused()
    {
        Project project = Worked();

        Assert.Throws<ArgumentException>(() => project.Keep(project.Observations[2]));
    }

    [Fact]
    public void A_kept_variation_exports_under_the_project_its_bars_and_its_name()
    {
        Project project = Worked();
        ImportedPiece piece = Piece();

        Assert.Equal("minuet – bars 1–2 – Higher answer.mid", project.Observations[0].MidiFileName(project.FileName, piece.Meter));
        Assert.Equal("minuet – bars 1–2 – Half_ time.mid", project.RenameObservation("o_0002", "Half: time").Observations[1].MidiFileName(project.FileName, piece.Meter));
        Assert.Throws<InvalidOperationException>(() => project.Observations[2].MidiFileName(project.FileName, piece.Meter));
    }

    [Fact]
    public void The_same_choices_give_the_same_observation()
    {
        Assert.Equal(Worked().Observations, Worked().Observations);
        Assert.Equal(OpenedProject.Open(Worked()).Observations[0].Chain!.Result!.Notes, OpenedProject.Open(Worked()).Observations[0].Chain!.Result!.Notes);
    }
}