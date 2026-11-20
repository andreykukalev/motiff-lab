using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Assistant.Variants;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Tests.Projects;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>A kept variant's record in the project file (version 16; the AI companion study, section 4.6), and the journal's carry of it after a reload.</summary>
public class VariantRecordTests
{
    private static readonly DateTimeOffset s_at = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly ImportedPiece s_furElise = FurElise();

    private static TryChip Chip(string id) => TryChips.Find(id)!;

    private static MotifSelection Bars(OpenedProject opened, int first, int last) =>
        MotifSelector.Select(opened.Notes, opened.Piece.Meter, TickSpan.FromBounds(opened.Piece.Meter.GetBar(first).Span.Onset, opened.Piece.Meter.GetBar(last).Span.End), SelectionFilter.All, MotifSelection.MaxStructureBars);

    /// <summary>The first twelve bars of Für Elise as a MIDI piece, so the file stays small.</summary>
    private static Project Piece()
    {
        Tick end = s_furElise.Meter.GetBar(12).Span.End;
        Note[] notes = [.. s_furElise.Notes.Where(note => note.Span.Onset < end)];
        return Project.Create("p_1", "elise.mid", MidiExporter.Export(notes, [], s_furElise.Meter, "elise"), s_at);
    }

    /// <summary>Variant 1, a falling arpeggio over bars 5–8, and Variant 2 from it with a pedal bass and one bar more.</summary>
    private static (VariantReplay First, VariantReplay Second) Variants(OpenedProject opened)
    {
        VariantReplay first = PhraseVariants.FromChip(Chip("v2.arpeggioDown"), "o_0001", "Вариант 1", Bars(opened, 5, 8), opened.Notes, opened.Piece.Meter, null, s_at, Words.Russian);
        VariantReplay second = PhraseVariants.FromChip(Chip("v12.pedal"), first.Variant, "o_0002", "Вариант 2", opened.Notes, opened.Piece.Meter, s_at.AddMinutes(1), Words.Russian);
        second = PhraseVariants.Replay(second.Variant with { BarChange = 1 }, opened.Notes, opened.Piece.Meter, Words.Russian);
        Assert.NotNull(first.Result);
        Assert.NotNull(second.Result);
        return (first, second);
    }

    [Fact]
    public void A_kept_variant_comes_back_from_the_file_with_its_lineage_base_and_provenance()
    {
        Project project = Piece();
        OpenedProject opened = OpenedProject.Open(project);
        (VariantReplay first, VariantReplay second) = Variants(opened);
        project = project.Keep(VariantRecord.ToObservation(first.Variant, null, s_at)).Keep(VariantRecord.ToObservation(second.Variant, null, s_at.AddMinutes(1)));

        Project read = ProjectFile.Read(ProjectFile.Write(project));
        OpenedProject again = OpenedProject.Open(read);

        PhraseVariant back = VariantRecord.Read(read.Observations.Single(observation => observation.Id == "o_0002"), again.Notes)!;
        Assert.Equal(second.Variant, back);
        Assert.Equal("o_0001", back.ParentId);
        Assert.Equal(1, back.BarChange);
        Assert.Equal("v12.pedal", back.Provenance.ChipId);
        Assert.Equal("variation", Assert.IsType<NotesProvenance>(read.Observations[1].Provenance).Op);
        Assert.Equal(second.Result!.Notes, PhraseVariants.Replay(back, again.Notes, again.Piece.Meter, Words.Russian).Result!.Notes);
        Assert.Equal(first.Variant, VariantRecord.Read(read.Observations[0], again.Notes));
    }

    [Fact]
    public void An_idea_with_no_record_reads_as_before_with_the_notes_under_it_as_they_are()
    {
        VariantReplay made = PhraseVariants.FromChip(Chip("v2.arpeggioDown"), "o_0001", "Вариант 1", MotifSelector.Select(s_furElise.Notes, s_furElise.Meter, TickSpan.FromBounds(s_furElise.Meter.GetBar(5).Span.Onset, s_furElise.Meter.GetBar(8).Span.End), SelectionFilter.All, MotifSelection.MaxStructureBars), s_furElise.Notes, s_furElise.Meter, null, s_at, Words.Russian);
        KeptVariation kept = made.Variant.ToKept();
        Observation idea = Observation.Create("o_0001", kept.Passage, kept, "Вариант 1", null, s_at);

        PhraseVariant back = VariantRecord.Read(idea, s_furElise.Notes)!;

        Assert.Equal(made.Variant with { Provenance = new VariantProvenance(VariantOrigin.Variation, s_at) }, back);
        Assert.Null(back.ParentId);
        // A provenance that is not a variant's, or params that do not read, leave the idea as it was.
        Assert.Equal(back, VariantRecord.Read(idea with { Provenance = new NotesProvenance("variation") { ParamsJson = "{\"base\": 3}" } }, s_furElise.Notes));
        Assert.Equal(back, VariantRecord.Read(idea with { Provenance = new NotesProvenance(NotesProvenance.Manual) }, s_furElise.Notes));
    }

    [Fact]
    public void The_record_keeps_the_assistant_request_model_and_seed_and_writes_the_same_bytes()
    {
        VariantReplay made = PhraseVariants.FromChip(Chip("v2.arpeggioDown"), "o_0003", "Вариант 3", MotifSelector.Select(s_furElise.Notes, s_furElise.Meter, TickSpan.FromBounds(s_furElise.Meter.GetBar(5).Span.Onset, s_furElise.Meter.GetBar(8).Span.End), SelectionFilter.All, MotifSelection.MaxStructureBars), s_furElise.Notes, s_furElise.Meter, null, s_at, Words.Russian);
        PhraseVariant asked = made.Variant with { Provenance = VariantProvenance.FromAssistant("Сделай аккомпанемент легче", "model-a", s_at) with { Seed = 7 } };

        NotesProvenance provenance = VariantRecord.ProvenanceOf(asked);

        Assert.Equal(NotesProvenance.Assistant, provenance.Op);
        Assert.Equal(provenance, VariantRecord.ProvenanceOf(asked));
        Assert.StartsWith("{\"request\":\"", provenance.ParamsJson, StringComparison.Ordinal);
        PhraseVariant back = VariantRecord.Read(VariantRecord.ToObservation(asked, "n_1", s_at), s_furElise.Notes)!;
        Assert.Equal(asked, back);
        Assert.Equal(7, back.Provenance.Seed);
        Assert.Equal("model-a", back.Provenance.Model);
    }

    [Fact]
    public void A_variant_is_made_again_over_the_notes_it_was_built_on_when_they_changed()
    {
        VariantReplay made = PhraseVariants.FromChip(Chip("v2.arpeggioDown"), "o_0001", "Вариант 1", MotifSelector.Select(s_furElise.Notes, s_furElise.Meter, TickSpan.FromBounds(s_furElise.Meter.GetBar(5).Span.Onset, s_furElise.Meter.GetBar(8).Span.End), SelectionFilter.All, MotifSelection.MaxStructureBars), s_furElise.Notes, s_furElise.Meter, null, s_at, Words.Russian);
        string moved = made.Variant.Base[0].Id;
        Note[] changed = [.. s_furElise.Notes.Select(note => note.Id == moved ? note with { Pitch = new Pitch(note.Pitch.MidiNumber + 1) } : note)];
        Assert.True(PhraseVariants.Replay(made.Variant, changed, s_furElise.Meter, Words.Russian).BaseChanged);

        (IReadOnlyList<Note> notes, MeterMap meter) = VariantRecord.BuiltOn(made.Variant, [(changed, s_furElise.Meter)]);
        VariantReplay again = PhraseVariants.Replay(made.Variant, notes, meter, Words.Russian);

        Assert.False(again.BaseChanged);
        Assert.Equal(made.Result!.Notes, again.Result!.Notes);
        // With the notes as they were among the candidates, those are taken as they are.
        Assert.Same(s_furElise.Notes, VariantRecord.BuiltOn(made.Variant, [(changed, s_furElise.Meter), (s_furElise.Notes, s_furElise.Meter)]).Notes);
    }

    [Fact]
    public void After_a_reload_the_journal_carries_the_variant_from_its_record_as_it_was_taken_into_work()
    {
        Project project = Piece();
        OpenedProject opened = OpenedProject.Open(project);
        (VariantReplay first, VariantReplay second) = Variants(opened);
        project = project.Keep(VariantRecord.ToObservation(first.Variant, null, s_at)).Keep(VariantRecord.ToObservation(second.Variant, null, s_at.AddMinutes(1)));
        var work = new AssemblyWork(SourceMusic.OfPiece(opened.Piece, opened.Notes), new Dictionary<string, SourceMusic>(), new Dictionary<string, string>(), [], 1);
        (AssemblyWork promoted, _) = VariantPromotion.Promote(work, second.Variant, VariantPromotion.Music(second, opened.Notes, opened.Piece.Meter)!, "«Вариант 2»", s_at.AddMinutes(2));
        project = project.WithJournal(promoted.Steps, promoted.Next).ApplyAssembly(promoted.Result.Music, promoted.Base, s_at.AddMinutes(2));

        byte[] written = ProjectFile.Write(project);
        Assert.Equal(written, ProjectFile.Write(project));
        GoldenFiles.AssertIs("v17-variants.motiff.json", written, ["v16-variants.motiff.json"]);

        Project read = ProjectFile.Read(written);
        OpenedProject again = OpenedProject.Open(read);
        IReadOnlyList<(string SourceId, SourceMusic Music)> sources = VariantPromotion.Sources(read, again.Notes, again.Piece.Meter, Words.Russian);
        Assert.Equal(["variant:o_0002"], sources.Select(source => source.SourceId));
        var rebuilt = new AssemblyWork(read.Assembly.Before!, sources.ToDictionary(source => source.SourceId, source => source.Music), new Dictionary<string, string>(), read.Assembly.Steps, read.Assembly.Next);
        Assert.Equal(
            promoted.Result.Music.Notes.Select(note => (note.Id, note.Pitch, note.Span)),
            rebuilt.Result.Music.Notes.Select(note => (note.Id, note.Pitch, note.Span)));
        Assert.Equal(promoted.Result.Music.Bars.Count, rebuilt.Result.Music.Bars.Count);
        Assert.Equal(VariantState.InWork, VariantRecord.StateOf(read, "o_0002"));
        Assert.Equal(VariantState.Kept, VariantRecord.StateOf(read, "o_0001"));
        Assert.Equal(VariantState.Draft, VariantRecord.StateOf(read, "o_0009"));
    }
}