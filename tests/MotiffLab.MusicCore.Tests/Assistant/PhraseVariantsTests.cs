using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Assistant.Variants;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>The assistant's «что если» variants of a phrase and «Взять в работу» (the AI companion study, sections 2.4 and 4.6).</summary>
public class PhraseVariantsTests
{
    private static readonly DateTimeOffset s_at = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly ImportedPiece s_piece = FurElise();

    private static TryChip Chip(string id) => TryChips.Find(id)!;

    /// <summary>Bars <paramref name="first"/> to <paramref name="last"/> of Für Elise, all notes.</summary>
    private static MotifSelection Bars(int first, int last, IReadOnlyList<Note>? notes = null) =>
        MotifSelector.Select(notes ?? s_piece.Notes, s_piece.Meter, TickSpan.FromBounds(s_piece.Meter.GetBar(first).Span.Onset, s_piece.Meter.GetBar(last).Span.End), SelectionFilter.All, MotifSelection.MaxStructureBars);

    /// <summary>Bars 5–8 with a falling arpeggio in the accompaniment (keep the melody).</summary>
    private static VariantReplay Arpeggio() =>
        PhraseVariants.FromChip(Chip("v2.arpeggioDown"), "o_0001", "Вариант 1", Bars(5, 8), s_piece.Notes, s_piece.Meter, null, s_at, Words.Russian);

    private static AssemblyWork Work(IReadOnlyList<Note> notes) =>
        new(SourceMusic.Of(notes, s_piece.Meter, s_piece.End, [new PlaybackTempo(Tick.Zero, 500_000)], s_piece.Keys), new Dictionary<string, SourceMusic>(), new Dictionary<string, string>(), [], 1);

    [Fact]
    public void A_chip_makes_the_same_variant_every_time_and_never_changes_the_phrase()
    {
        Note[] before = [.. s_piece.Notes];
        VariantReplay first = Arpeggio();
        VariantReplay again = Arpeggio();

        Assert.Null(first.Refusal);
        Assert.False(first.BaseChanged);
        Assert.NotNull(first.Result);
        Assert.Equal(first.Variant, again.Variant);
        Assert.Equal(first.Result!.Notes, again.Result!.Notes);
        Assert.Equal(before, s_piece.Notes);
        Assert.Equal("variation", first.Variant.Provenance.Op);
        Assert.Equal([new KeyValuePair<string, string>("chip", "v2.arpeggioDown")], first.Variant.Provenance.Params);
        Assert.Equal([KeepProperty.Melody], first.Variant.Keep);
        Assert.Null(first.Variant.ParentId);
    }

    [Fact]
    public void A_variant_is_saved_as_the_idea_its_chain_would_be_and_comes_back_from_it()
    {
        VariantReplay made = Arpeggio();
        KeptVariation kept = made.Variant.ToKept();

        Assert.Equal(KeptVariation.Create(made.Chain!, SelectionFilter.All), kept);

        Observation idea = Observation.Create("o_0001", kept.Passage, kept, made.Variant.Name, null, s_at);
        PhraseVariant back = PhraseVariant.FromObservation(idea, s_piece.Notes)!;
        Assert.Equal(made.Variant.Base, back.Base);
        Assert.Equal(made.Result!.Notes, PhraseVariants.Replay(back, s_piece.Notes, s_piece.Meter, Words.Russian).Result!.Notes);
    }

    [Fact]
    public void A_variant_from_a_variant_carries_the_whole_chain_and_names_its_parent()
    {
        VariantReplay parent = Arpeggio();
        VariantReplay child = PhraseVariants.FromChip(Chip("v12.pedal"), parent.Variant, "o_0002", "Вариант 2", s_piece.Notes, s_piece.Meter, s_at, Words.Russian);
        VariantReplay ruledOut = PhraseVariants.FromChip(Chip("v14.ornament"), parent.Variant, "o_0002", "Вариант 2", s_piece.Notes, s_piece.Meter, s_at, Words.Russian);

        Assert.Null(child.Refusal);
        Assert.StartsWith("«Орнамент»", ruledOut.Refusal, StringComparison.Ordinal);
        Assert.Equal("o_0001", child.Variant.ParentId);
        Assert.Equal(parent.Variant.Keep, child.Variant.Keep);
        Assert.Equal([.. parent.Variant.Steps, PhraseVariants.StepOf(Chip("v12.pedal"))], child.Variant.Steps);
        Assert.Equal(parent.Result!.Notes, child.Chain!.Steps[0].Result.Notes);
    }

    [Fact]
    public void When_the_notes_under_a_variant_change_it_says_so_and_recomputing_takes_the_notes_as_they_are()
    {
        VariantReplay made = Arpeggio();
        string id = made.Variant.BaseIds[0];
        Note[] corrected = [.. s_piece.Notes.Select(note => note.Id == id ? note with { Pitch = new Pitch(note.Pitch.MidiNumber + 1) } : note)];

        VariantReplay heard = PhraseVariants.Replay(made.Variant, corrected, s_piece.Meter, Words.Russian);
        Assert.True(heard.BaseChanged);
        Assert.NotNull(heard.Chain);

        PhraseVariant recomputed = PhraseVariants.Rebase(made.Variant, corrected, s_piece.Meter);
        Assert.False(PhraseVariants.Replay(recomputed, corrected, s_piece.Meter, Words.Russian).BaseChanged);
    }

    [Fact]
    public void Taking_a_variant_into_work_carries_it_in_place_of_the_phrase_and_returning_takes_it_back()
    {
        VariantReplay made = Arpeggio();
        AssemblyWork work = Work(s_piece.Notes);
        Assert.Null(VariantPromotion.Refusal(made, PieceAssembly.Empty, work, Words.Russian));
        SourceMusic music = VariantPromotion.Music(made, s_piece.Notes, s_piece.Meter)!;

        (AssemblyWork promoted, CarryStep step) = VariantPromotion.Promote(work, made.Variant, music, "«Вариант 1»", s_at);

        TickSpan span = made.Chain!.Context.Span;
        Assert.Equal("variant:o_0001", step.SourceId);
        Assert.Equal("o_0001", VariantPromotion.VariantIdOf(step.SourceId));
        Assert.Equal(["o_0001"], VariantPromotion.Carried(promoted.Steps));
        Assert.Equal(step, VariantPromotion.StepOf(promoted, "o_0001"));
        Assert.Equal(
            made.Result!.Notes.Select(note => (note.Pitch, note.Span)).OrderBy(pair => pair.Span.Onset).ThenBy(pair => pair.Pitch.MidiNumber),
            promoted.Result.Music.NotesIn(span.Onset, span.End).Select(note => (note.Pitch, note.Span)).OrderBy(pair => pair.Span.Onset).ThenBy(pair => pair.Pitch.MidiNumber));
        Assert.Equal(work.Result.Music.Bars, promoted.Result.Music.Bars);
        Assert.True(VariantPromotion.Pending(PieceAssembly.Empty, promoted));
        Assert.NotNull(VariantPromotion.Refusal(made, PieceAssembly.Empty, promoted, Words.Russian));

        AssemblyWork returned = promoted.Revert(step.Number);
        Assert.Equal(
            work.Result.Music.Notes.Select(note => (note.Id, note.Pitch, note.Span)),
            returned.Result.Music.Notes.Select(note => (note.Id, note.Pitch, note.Span)));
        Assert.Null(VariantPromotion.StepOf(returned, "o_0001"));
    }

    [Fact]
    public void A_project_opens_after_a_variant_is_made_notes_and_keeps_the_idea_it_replaced()
    {
        byte[] file = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Project project = Project.Create("p_1", "FurElise.musicxml", file, s_at);
        OpenedProject opened = OpenedProject.Open(project);
        VariantReplay made = PhraseVariants.FromChip(Chip("v2.arpeggioDown"), "o_0001", "Вариант 1", Bars(5, 8, opened.Notes), opened.Notes, opened.Piece.Meter, null, s_at, Words.Russian);
        KeptVariation kept = made.Variant.ToKept();
        project = project.Keep(Observation.Create("o_0001", kept.Passage, kept, made.Variant.Name, null, s_at));
        AssemblyWork work = new(SourceMusic.OfPiece(opened.Piece, opened.Notes), new Dictionary<string, SourceMusic>(), new Dictionary<string, string>(), [], 1);
        (AssemblyWork promoted, _) = VariantPromotion.Promote(work, made.Variant, VariantPromotion.Music(made, opened.Notes, opened.Piece.Meter)!, "«Вариант 1»", s_at);

        Project changed = project.WithJournal(promoted.Steps, promoted.Next).ApplyAssembly(promoted.Result.Music, promoted.Base, s_at);
        OpenedProject again = OpenedProject.Open(changed);

        Assert.Equal(["o_0001"], again.Hidden);
        Assert.Equal(["o_0001"], again.Project.Observations.Select(observation => observation.Id));
        Assert.Equal(["o_0001"], VariantPromotion.Carried(again.Project.Assembly.Steps));
    }

    [Fact]
    public void A_variant_that_adds_a_bar_moves_the_bars_after_it_in_the_working_notes()
    {
        VariantReplay made = Arpeggio();
        VariantReplay longer = PhraseVariants.Replay(made.Variant with { BarChange = 1 }, s_piece.Notes, s_piece.Meter, Words.Russian);
        SourceMusic music = VariantPromotion.Music(longer, s_piece.Notes, s_piece.Meter)!;
        AssemblyWork work = Work(s_piece.Notes);
        long bar = s_piece.Meter.GetBar(9).Signature.BarTicks;

        (AssemblyWork promoted, _) = VariantPromotion.Promote(work, longer.Variant, music, "«Вариант 1»", s_at);

        Assert.Equal(work.Result.Music.Bars.Count + 1, promoted.Result.Music.Bars.Count);
        Note ninth = work.Result.Music.Notes.First(note => note.Span.Onset == s_piece.Meter.GetBar(9).Span.Onset);
        Assert.Equal(ninth.Span.Onset.Offset(bar), promoted.Result.Music.Notes.Single(note => note.Id == ninth.Id).Span.Onset);

        VariantReplay shorter = PhraseVariants.Replay(made.Variant with { BarChange = -1 }, s_piece.Notes, s_piece.Meter, Words.Russian);
        (AssemblyWork cut, _) = VariantPromotion.Promote(work, shorter.Variant, VariantPromotion.Music(shorter, s_piece.Notes, s_piece.Meter)!, "«Вариант 1»", s_at);
        Assert.Equal(work.Result.Music.Bars.Count - 1, cut.Result.Music.Bars.Count);
        Assert.NotNull(PhraseVariants.Replay(made.Variant with { BarChange = PhraseVariant.MaxBarChange + 1 }, s_piece.Notes, s_piece.Meter, Words.English).Refusal);
    }

    [Fact]
    public void Note_edits_of_a_draft_replay_as_hand_edits_of_a_variant()
    {
        AssistantPiece piece = Open(s_piece);
        ToolResult edited = AssistantTools.Call(piece, "notes.edit", Json("""{"bars": {"from": 5, "to": 8}, "edits": [{"kind": "change", "address": "5.R.1", "bar": null, "hand": null, "start": null, "length": null, "pitch": 77, "velocity": null}]}"""));
        Assert.True(edited.Ok, edited.Refusal);
        AssistantDraft draft = edited.Drafts[0];

        VariantReplay made = PhraseVariants.Start("o_0001", "Variant 1", Bars(5, 8), s_piece.Notes, s_piece.Meter, null, new HashSet<KeepProperty>(), PhraseVariants.StepsOf(draft), VariantProvenance.FromAssistant("higher", "model", s_at), Words.English);

        Assert.Null(made.Refusal);
        Assert.Equal("assistant", made.Variant.Provenance.Op);
        Assert.Contains(made.Result!.Notes, note => note.Pitch.MidiNumber == 77 && note.Span.Onset == draft.Edits[0].After!.Value.Span.Onset);
    }

    [Fact]
    public void Names_count_on_from_the_highest_variant_and_the_proposer_is_served()
    {
        Assert.Equal("Вариант 3", PhraseVariants.NextName(["Вариант 2", "Идея", "Вариант 1"], Words.Russian));
        Assert.Equal("Variant 1", PhraseVariants.NextName([], Words.English));
        Assert.True(AssistantRoles.Allows(AssistantRole.Proposer, "notes.edit"));
        Assert.True(AssistantRoles.Allows(AssistantRole.Proposer, "variation.simplify"));
        Assert.False(AssistantRoles.Allows(AssistantRole.Mentor, "variation.simplify"));
        Assert.Equal([AssistantRole.Mentor, AssistantRole.Proposer], AssistantRoles.Served);
        Assert.Equal(AssistantRole.Proposer, AssistantRoles.Parse("proposer"));
    }
}