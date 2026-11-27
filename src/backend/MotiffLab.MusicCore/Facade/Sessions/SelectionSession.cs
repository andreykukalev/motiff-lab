using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>
/// The selection and what is understood about it (audit W1.7, the first typed session): the one primary selection
/// (0013), what music-core read for it, the Try chip that started the variation chain, the chain, and the working motif
/// they are saved as. The session owns this state: every change goes through a method here that says what it changes.
/// It reads the piece through <see cref="SessionPiece"/>, takes typed values, never a request, and returns typed results;
/// it writes no JSON. OpenPiece.Selection.cs is its JSON adapter: it reads the requests and writes the answers.
/// </summary>
internal sealed class SelectionSession
{
    private InvitationContext? _understood;

    /// <summary>The Check flags and notes <see cref="_understood"/> was read from.</summary>
    private (IReadOnlyList<CheckFlag>? Check, IReadOnlyList<Note> Notes)? _understoodFor;

    /// <summary>The selection; since v0.6.4 (#614) it may hold no notes, when the iPad selected a passage of time ("empty").</summary>
    public MotifSelection? Current { get; private set; }

    /// <summary>How the selection was made: a range of the score, or notes picked.</summary>
    public MotifSelectionKind Kind { get; private set; } = MotifSelectionKind.Range;

    /// <summary>The variation chain a Try chip started, or the one a saved motif or an idea brought back.</summary>
    public VariationChain? Chain { get; private set; }

    /// <summary>The Try chip that started <see cref="Chain"/>.</summary>
    public TryChip? Chip { get; private set; }

    /// <summary>The id the selection is saved under as the working motif: a saved motif's (<see cref="Start"/>) or a new one (<see cref="SaveBeside"/>), set as the piece opens.</summary>
    public string MotifId { get; private set; } = string.Empty;

    /// <summary>
    /// The id the working variation is saved under: its own when the working motif opened on one or keeps one hidden, else
    /// one no variation of the file has; null for the one named after the motif.
    /// </summary>
    private string? _variationId;

    /// <summary>
    /// The working motif's variation that could not be made again over the notes shown (the layer off, or a correction that
    /// no longer fits): saving writes it until the player picks another passage, idea or chip, and then leaves it as saved.
    /// </summary>
    public SavedVariation? HiddenVariation { get; private set; }

    /// <summary>The working motif's variation as the project file keeps it: the chain's when it has a result, else the hidden one.</summary>
    public SavedVariation? WorkingVariation => Chain is { Result: not null } chain
        ? SavedVariation.Create(VariationId, MotifId, chain)
        : HiddenVariation;

    /// <summary>
    /// The id the working variation is saved under (#821: a save writes over only the working chain's own earlier save, made
    /// before any other chain was shown, never another saved variation): the one named after the motif (v_0002 for m_0002)
    /// unless <see cref="_variationId"/> says.
    /// </summary>
    private string VariationId => _variationId ?? VariationIdOf(MotifId);

    private static string VariationIdOf(string motifId) => motifId.Replace("m_", "v_", StringComparison.Ordinal);

    /// <summary>The selection when it holds notes: what the analyses of the piece read about it.</summary>
    private MotifSelection? NotedSelection => Current is { NoteIds.Count: > 0 } selection ? selection : null;

    /// <summary>The refusal of a passage with no notes, and of a call that reads the notes of a selection that holds none.</summary>
    public static FacadeException NoNotes() => FacadeException.Unavailable("There are no notes in that passage.");

    /// <summary>The selection, refused when there is none.</summary>
    public MotifSelection Required() => Current ?? throw FacadeException.NoSelection();

    /// <summary>The working motif a saved project opens on: its selection, and its variation (<paramref name="saved"/>, made again as <paramref name="chain"/>) when it keeps one.</summary>
    public void Start(SessionPiece piece, Motif motif, SavedVariation? saved, VariationChain? chain)
    {
        MotifId = motif.Id;
        if (saved is null)
        {
            SaveVariationBeside(piece);
        }
        else
        {
            _variationId = saved.Id;
        }

        Current = motif.Selection;
        Kind = motif.Kind;
        Chain = chain;
    }

    /// <summary>
    /// No saved motif is worked on: the passage picked from now on is saved beside them, never over one, under a new id from
    /// the piece's source that no motif of the file has, known to this build or not (a later build's motif and its variations
    /// are never replaced), and its variation under one no variation has.
    /// </summary>
    public void SaveBeside(SessionPiece piece)
    {
        Project project = piece.Project;
        MotifId = piece.Ids.Next("m_", [.. project.Motifs.Select(static motif => motif.Id), .. Project.UnknownIds(project.Unknown, "motifs")]);
        SaveVariationBeside(piece);
    }

    /// <summary>
    /// The working variation is saved from now on under an id no variation of the file has, known to this build or not: the
    /// one named after the motif when it is free, else a new one from the piece's source. What was saved before stays as it was (#821).
    /// </summary>
    private void SaveVariationBeside(SessionPiece piece)
    {
        List<string> taken = [.. piece.Project.Variations.Select(static variation => variation.Id), .. Project.UnknownIds(piece.Project.Unknown, "variations")];
        _variationId = null;
        if (taken.Contains(VariationId))
        {
            _variationId = piece.Ids.Next("v_", taken);
        }
    }

    /// <summary>The working motif's variation left out when the piece opened (<paramref name="hidden"/>), kept for saving while no chain is shown, under its own id.</summary>
    public void HideLeftOut(SessionPiece piece, IReadOnlyList<string> hidden)
    {
        HiddenVariation = Chain is null ? piece.Project.Variations.FirstOrDefault(variation => variation.MotifId == MotifId && hidden.Contains(variation.Id)) : null;
        _variationId = HiddenVariation?.Id ?? _variationId;
    }

    /// <summary>
    /// The selection is now <paramref name="selection"/>: what was understood, the chip, the chain and the hidden variation go
    /// from the session while their saved copy stays in the file (#821), and on another passage than the working motif's saved
    /// one the working motif moves beside it (<see cref="KeepBeside"/>).
    /// </summary>
    public void Select(SessionPiece piece, MotifSelection selection, MotifSelectionKind kind)
    {
        KeepBeside(piece, selection, kind);
        SaveVariationBeside(piece);
        Current = selection;
        Kind = kind;
        _understood = null;
        Chip = null;
        Chain = null;
        HiddenVariation = null;
    }

    /// <summary>
    /// An idea brought back: its passage as the selection and its variation, when it keeps one, as the chain, which no Try
    /// chip started here (the chip of an earlier try goes), saved beside the working motif's saved variations; on another
    /// passage the working motif moves beside as on select.
    /// </summary>
    public void Open(SessionPiece piece, MotifSelection selection, VariationChain? chain)
    {
        KeepBeside(piece, selection, MotifSelectionKind.Range);
        SaveVariationBeside(piece);
        Current = selection;
        Kind = MotifSelectionKind.Range;
        _understood = null;
        Chip = null;
        Chain = chain;
        HiddenVariation = null;
    }

    /// <summary>
    /// Another passage chosen while the working motif has variations saved: the motif stays in the file as it was, with all of
    /// them, and the passage is saved beside it (#821, the lead's decision: a save never loses a saved variation). The same
    /// passage again stays the working motif.
    /// </summary>
    private void KeepBeside(SessionPiece piece, MotifSelection selection, MotifSelectionKind kind)
    {
        Project project = piece.Project;
        if (project.Motifs.FirstOrDefault(motif => motif.Id == MotifId) is { } saved
            && project.Variations.Any(variation => variation.MotifId == MotifId)
            && !(saved.Kind == kind && saved.Span == selection.Span && saved.NoteIds.SequenceEqual(selection.NoteIds, StringComparer.Ordinal)))
        {
            SaveBeside(piece);
        }
    }

    /// <summary>
    /// The notes changed (a correction, the layer switched, a correction dropped): the selection keeps its passage over
    /// them, none when no note is left there unless it held none to begin with (a passage of time, v0.6.4); the chain made
    /// from the old notes and its chip go, while its saved copy stays in the file (#821).
    /// </summary>
    public void FollowNotes(SessionPiece piece)
    {
        FollowPassage(piece);
        SaveVariationBeside(piece);
    }

    /// <summary>
    /// The corrections' layer was switched: the selection follows the notes as <see cref="FollowNotes"/> says, and the
    /// working variation <paramref name="working"/> (<see cref="WorkingVariation"/> before the switch) is made again over it
    /// under the same id, kept hidden when it no longer fits.
    /// </summary>
    public void FollowLayer(SessionPiece piece, SavedVariation? working)
    {
        FollowPassage(piece);
        HiddenVariation = null;
        if (working is not null && Current is { } selection)
        {
            try
            {
                Chain = OpenedProject.Restore(working, selection, piece.Notes);
            }
            catch (ProjectFileException)
            {
                HiddenVariation = working;
            }
        }
    }

    /// <summary>The selection keeps its passage over the notes as they are now (<see cref="FollowNotes"/>); the chain and its chip go.</summary>
    private void FollowPassage(SessionPiece piece)
    {
        if (Current is { } selection)
        {
            MotifSelection again = MotifSelector.Select(piece.Notes, piece.Piece.Meter, selection.Span, selection.Filter, MotifSelection.MaxStructureBars);
            Current = again.NoteIds.Count > 0 || selection.NoteIds.Count == 0 ? again : null;
        }

        Chain = null;
        Chip = null;
    }

    /// <summary>What was understood is read again at the next call: the notes, the language, the phrases or the tempo changed.</summary>
    public void ForgetUnderstanding() => _understood = null;

    /// <summary>What music-core reads about the selection: the motif, its structure, character and harmony, with the doubt of the notes still to check.</summary>
    public InvitationContext Understood(SessionPiece piece)
    {
        MotifSelection selection = Required();
        if (selection.NoteIds.Count == 0)
        {
            throw NoNotes();
        }

        // Fixing a note or resolving a Check flag changes the doubt, so the understanding is read again (#352).
        if (_understood is { } understood && _understoodFor is { } from && ReferenceEquals(from.Check, piece.Project.Check) && ReferenceEquals(from.Notes, piece.Notes))
        {
            return understood;
        }

        ImportedPiece imported = piece.Piece;
        IReadOnlyList<Note> notes = piece.Notes;
        PhraseMap phrases = piece.Structure.Levels.Phrases;
        NoteDoubt doubt = piece.Doubt();
        KeySignature? key = KeySignature.InForce(imported.Keys, selection.Span.Onset);
        MotifAnalysis read = MotifAnalyzer.Analyze(notes, imported.Meter, selection, key, imported.MusicXml?.AccentMarks, phrases.Boundaries, piece.Words);
        HarmonyAnalysis harmony = doubt.On(HarmonyAnalyzer.Analyze(notes, imported.Meter, selection, read.Key?.Key, piece.Words), selection);
        MotifAnalysis motif = doubt.On(read, selection);
        StructureAnalysis structure = doubt.On(StructureAnalyzer.Analyze(notes, imported.Meter, selection, phrases.Boundaries, piece.Words), selection);
        IReadOnlyList<MotifFact> character = doubt.On(MotifAnalyzer.AnalyzeCharacter(notes, imported.Meter, selection, key, CharacterContext.FromPiece(imported), piece.Words), selection);
        _understoodFor = (piece.Project.Check, notes);
        return _understood = new InvitationContext(notes, imported.Meter, selection, motif, structure, character, piece.Words) { Harmony = harmony };
    }

    /// <summary>understand: what was understood, with the sentence map's facts about the phrase or the sentence the selection is (none for another range).</summary>
    public SelectionUnderstanding Understand(SessionPiece piece)
    {
        InvitationContext context = Understood(piece);
        MotifSelection selection = Required();
        PhraseLevels levels = piece.Structure.Levels;
        IReadOnlyList<MotifFact> facts = levels.Phrases.Matching(selection.Span) is { } phrase ? levels.Sentences.Facts(phrase)
            : levels.Sentences.Matching(selection.Span) is { } sentence ? levels.Sentences.Facts(sentence)
            : [];
        return new SelectionUnderstanding(context, piece.Doubt().On(facts, selection));
    }

    /// <summary>circle: the circle of fifths for the selection, from the key and chords music-core read for it.</summary>
    public SelectionCircle Circle(SessionPiece piece)
    {
        InvitationContext context = Understood(piece);
        KeyCircleView? view = KeyCircle.For(context.Selection, context.Notes, context.Meter, context.Motif.Key, context.Harmony, context.Words);
        return new SelectionCircle(view, context.Selection.Span.Onset);
    }

    /// <summary>dna: Piece DNA of the notes and the selection, with the musical ideas when <paramref name="musicalIdeas"/>.</summary>
    public PieceDna Dna(SessionPiece piece, bool musicalIdeas)
    {
        ImportedPiece imported = piece.Piece;
        return piece.Doubt().On(PieceDna.Build(piece.Notes, imported.Meter, imported.Keys, imported.End, NotedSelection, NotedSelection is null ? null : Understood(piece).Structure, piece.Words, piece.Structure, musicalIdeas));
    }

    /// <summary>
    /// try: the chip <paramref name="chipId"/> sounded (0018, section 7), one accepted step over the selection with its Keep
    /// preset, as the chain; the chain or hidden variation it replaces keeps its saved copy, and the new one is saved beside
    /// it (#821), under the same id as long as nothing was saved under it.
    /// </summary>
    public VariationChain Try(SessionPiece piece, string chipId)
    {
        TryChip chip = TryChips.Find(chipId) ?? throw FacadeException.BadRequest($"There is no chip \"{chipId}\".");
        VariationChain chain = TryChips.Start(chip, Understood(piece), out string? reason) ?? throw FacadeException.Unavailable(reason ?? "The chip cannot run on this selection.");
        SaveVariationBeside(piece);
        Chain = chain;
        Chip = chip;
        HiddenVariation = null;
        return chain;
    }

    /// <summary>The chain, refused when there is none.</summary>
    public VariationChain Variation() => Chain ?? throw FacadeException.NoVariation();

    /// <summary>variation.clear: the chain and its chip go, while its saved copy stays in the file (#821).</summary>
    public void Clear(SessionPiece piece)
    {
        Chain = null;
        Chip = null;
        SaveVariationBeside(piece);
    }

    /// <summary>The variation the player sees and hears: the pending step's result, else the accepted chain's.</summary>
    public Variation Shown()
    {
        VariationChain chain = Variation();
        return chain.Pending?.Result ?? chain.Result ?? throw FacadeException.Unavailable("The variation has no step yet.");
    }

    /// <summary>
    /// The motifs and variations project.save writes: the project's, with the selection as the working motif and its
    /// variation first, written over its own earlier save and no other saved variation (#821); with the corrections off
    /// (<paramref name="layerOff"/>) the project's as they were (version 0.5), since the selection is over notes they would
    /// change. A pick of one note, or of more than a motif of single notes holds (the iPad, v0.6.2), is saved as no selection
    /// is: the working motif's saved passage stays only while variations of it do, which cannot open without it.
    /// </summary>
    public (IReadOnlyList<SavedMotif> Motifs, IReadOnlyList<SavedVariation> Variations) Saved(SessionPiece piece, bool layerOff)
    {
        Project project = piece.Project;
        if (layerOff)
        {
            return ([.. project.Motifs], [.. project.Variations]);
        }

        MotifSelection? selection = NotedSelection is { } noted && Motif.Holds(noted, Kind) ? noted : null;
        SavedVariation? working = selection is null ? null : WorkingVariation;
        // Only the working variation's own earlier save is written over; every other saved variation stays as it was.
        var variations = project.Variations.Where(variation => variation.Id != working?.Id).ToList();
        if (selection is null)
        {
            bool kept = variations.Any(variation => variation.MotifId == MotifId);
            return ([.. project.Motifs.Where(motif => kept || motif.Id != MotifId)], variations);
        }

        var motifs = project.Motifs.Where(motif => motif.Id != MotifId).ToList();
        motifs.Insert(0, SavedMotif.From(Motif.Create(MotifId, 1, selection, Kind, MotifMode.Discovery, piece.Notes)));
        if (working is not null)
        {
            variations.Insert(0, working);
        }

        return (motifs, variations);
    }
}