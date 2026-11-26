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

    /// <summary>The id the selection is saved under as the working motif.</summary>
    public string MotifId { get; private set; } = "m_0001";

    /// <summary>
    /// The working motif's variation that could not be made again over the notes shown (the layer off, or a correction that
    /// no longer fits): saving keeps it until the player picks another passage, idea or chip.
    /// </summary>
    public SavedVariation? HiddenVariation { get; private set; }

    /// <summary>The working motif's variation as the project file keeps it: the chain's when it has a result, else the hidden one.</summary>
    public SavedVariation? WorkingVariation => Chain is { Result: not null } chain
        ? SavedVariation.Create(MotifId.Replace("m_", "v_", StringComparison.Ordinal), MotifId, chain)
        : HiddenVariation;

    /// <summary>The selection when it holds notes: what the analyses of the piece read about it.</summary>
    private MotifSelection? NotedSelection => Current is { NoteIds.Count: > 0 } selection ? selection : null;

    /// <summary>The refusal of a passage with no notes, and of a call that reads the notes of a selection that holds none.</summary>
    public static FacadeException NoNotes() => FacadeException.Unavailable("There are no notes in that passage.");

    /// <summary>The selection, refused when there is none.</summary>
    public MotifSelection Required() => Current ?? throw FacadeException.NoSelection();

    /// <summary>The working motif a saved project opens on: its selection, and its variation when it keeps one.</summary>
    public void Start(Motif motif, VariationChain? chain)
    {
        MotifId = motif.Id;
        Current = motif.Selection;
        Kind = motif.Kind;
        Chain = chain;
    }

    /// <summary>Every saved motif is hidden: the passage picked from now on is saved beside them, never over one.</summary>
    public void SaveBeside(IReadOnlyList<SavedMotif> motifs)
    {
        int number = 1;
        while (motifs.Any(motif => motif.Id == $"m_{number:D4}"))
        {
            number++;
        }

        MotifId = $"m_{number:D4}";
    }

    /// <summary>The working motif's variation left out when the piece opened (<paramref name="hidden"/>), kept for saving while no chain is shown.</summary>
    public void HideLeftOut(IReadOnlyList<SavedVariation> variations, IReadOnlyList<string> hidden) =>
        HiddenVariation = Chain is null ? variations.FirstOrDefault(variation => variation.MotifId == MotifId && hidden.Contains(variation.Id)) : null;

    /// <summary>The selection is now <paramref name="selection"/>: what was understood, the chip, the chain and the hidden variation go.</summary>
    public void Select(MotifSelection selection, MotifSelectionKind kind)
    {
        Current = selection;
        Kind = kind;
        _understood = null;
        Chip = null;
        Chain = null;
        HiddenVariation = null;
    }

    /// <summary>An idea brought back: its passage as the selection and its variation, when it keeps one, as the chain.</summary>
    public void Open(MotifSelection selection, VariationChain? chain)
    {
        Current = selection;
        Kind = MotifSelectionKind.Range;
        _understood = null;
        Chain = chain;
        HiddenVariation = null;
    }

    /// <summary>
    /// The notes changed (a correction, the layer switched, a correction dropped): the selection keeps its passage over
    /// them, none when no note is left there unless it held none to begin with (a passage of time, v0.6.4); the chain made
    /// from the old notes and its chip go.
    /// </summary>
    public void FollowNotes(SessionPiece piece)
    {
        if (Current is { } selection)
        {
            MotifSelection again = MotifSelector.Select(piece.Notes, piece.Piece.Meter, selection.Span, selection.Filter, MotifSelection.MaxStructureBars);
            Current = again.NoteIds.Count > 0 || selection.NoteIds.Count == 0 ? again : null;
        }

        Chain = null;
        Chip = null;
    }

    /// <summary>The working variation <paramref name="working"/> made again over the selection on the notes as they are now; hidden when it no longer fits.</summary>
    public void Remake(SavedVariation? working, IReadOnlyList<Note> notes)
    {
        HiddenVariation = null;
        if (working is not null && Current is { } selection)
        {
            try
            {
                Chain = OpenedProject.Restore(working, selection, notes);
            }
            catch (ProjectFileException)
            {
                HiddenVariation = working;
            }
        }
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

    /// <summary>try: the chip <paramref name="chipId"/> sounded (0018, section 7), one accepted step over the selection with its Keep preset, as the chain.</summary>
    public VariationChain Try(SessionPiece piece, string chipId)
    {
        TryChip chip = TryChips.Find(chipId) ?? throw FacadeException.BadRequest($"There is no chip \"{chipId}\".");
        VariationChain chain = TryChips.Start(chip, Understood(piece), out string? reason) ?? throw FacadeException.Unavailable(reason ?? "The chip cannot run on this selection.");
        Chain = chain;
        Chip = chip;
        HiddenVariation = null;
        return chain;
    }

    /// <summary>The chain, refused when there is none.</summary>
    public VariationChain Variation() => Chain ?? throw FacadeException.NoVariation();

    /// <summary>variation.clear: the chain and its chip go.</summary>
    public void Clear()
    {
        Chain = null;
        Chip = null;
    }

    /// <summary>The variation the player sees and hears: the pending step's result, else the accepted chain's.</summary>
    public Variation Shown()
    {
        VariationChain chain = Variation();
        return chain.Pending?.Result ?? chain.Result ?? throw FacadeException.Unavailable("The variation has no step yet.");
    }

    /// <summary>
    /// The motifs and variations project.save writes: the project's, with the selection as the working motif and its
    /// variation first; with the corrections off (<paramref name="layerOff"/>) the project's as they were (version 0.5), since
    /// the selection is over notes they would change. A pick of one note, or of more than a motif of single notes holds (the
    /// iPad, v0.6.2), is saved as no selection is.
    /// </summary>
    public (IReadOnlyList<SavedMotif> Motifs, IReadOnlyList<SavedVariation> Variations) Saved(Project project, IReadOnlyList<Note> notes, bool layerOff)
    {
        if (layerOff)
        {
            return ([.. project.Motifs], [.. project.Variations]);
        }

        var motifs = project.Motifs.Where(motif => motif.Id != MotifId).ToList();
        var variations = project.Variations.Where(variation => variation.MotifId != MotifId).ToList();
        if (NotedSelection is { } selection && Motif.Holds(selection, Kind))
        {
            motifs.Insert(0, SavedMotif.From(Motif.Create(MotifId, 1, selection, Kind, MotifMode.Discovery, notes)));
            if (WorkingVariation is { } working)
            {
                variations.Insert(0, working);
            }
        }

        return (motifs, variations);
    }
}