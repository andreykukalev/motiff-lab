using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>
/// What the assistant's tools read (the AI companion study, section 4.2): the open piece, its notes as they are now (with
/// the player's corrections), its Check flags and the language. The tools never change it: every tool reads it and
/// answers, a proposing tool with a draft beside it. Reading a range is worked out once and kept.
/// </summary>
public sealed class AssistantPiece
{
    private NoteAddressBook? _addresses;
    private PieceStructure? _structure;
    private readonly Dictionary<(int From, int To, Hand? Hand), RangeReading> _readings = [];

    /// <param name="piece">The piece as imported, for its meter, keys, end and the file's own marks.</param>
    /// <param name="notes">The notes as the player sees them: the imported notes with the corrections applied.</param>
    /// <param name="check">The piece's Check flags, open and resolved.</param>
    /// <param name="words">The language of everything the tools write.</param>
    /// <param name="structure">The piece's phrases and form as the app reads them; worked out from the notes when null.</param>
    public AssistantPiece(ImportedPiece piece, IReadOnlyList<Note> notes, IReadOnlyList<CheckFlag> check, Words words = default, PieceStructure? structure = null)
    {
        Piece = piece ?? throw new ArgumentNullException(nameof(piece));
        Notes = notes ?? throw new ArgumentNullException(nameof(notes));
        Check = check ?? throw new ArgumentNullException(nameof(check));
        Words = words;
        _structure = structure;
    }

    public ImportedPiece Piece { get; }

    public IReadOnlyList<Note> Notes { get; }

    public IReadOnlyList<CheckFlag> Check { get; }

    public Words Words { get; }

    public MeterMap Meter => Piece.Meter;

    /// <summary>The first bar of the piece: 0 with a pickup, otherwise 1.</summary>
    public int FirstBar => Meter.FirstBarNumber;

    /// <summary>The last bar that holds the piece's end.</summary>
    public int LastBar
    {
        get
        {
            Tick end = Notes.Count == 0 ? Piece.End : Later(Piece.End, Notes.Max(static note => note.Span.End));
            return end == Tick.Zero ? FirstBar : Meter.BarAt(new Tick(end.Value - 1)).Number;
        }
    }

    /// <summary>Every note's address (<see cref="NoteAddress"/>).</summary>
    public NoteAddressBook Addresses => _addresses ??= NoteAddressBook.Of(Notes, Meter);

    /// <summary>The phrase levels and the form, read once.</summary>
    public PieceStructure Structure => _structure ??= PieceStructure.Of(
        Notes,
        Meter,
        PhraseLevels.Suggest(Notes, Meter, Piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, Piece.End, KeySignature.InForce(Piece.Keys, Tick.Zero), Words, LevelEdits.None),
        Words);

    /// <summary>The recognised notes still to check: those in bars with an open Check flag (#352).</summary>
    public NoteDoubt Doubt => NoteDoubt.Of(Notes, Meter, Check.Where(static flag => !flag.Resolved).Select(static flag => flag.Bar), Words);

    /// <summary>Why bars <paramref name="from"/> to <paramref name="to"/> cannot be read, or null when they can.</summary>
    public string? RangeRefusal(int from, int to)
    {
        if (from > to)
        {
            return Words.T($"The range starts at bar {from}, after its end at bar {to}.", $"Диапазон начинается с такта {from}, позже своего конца (такт {to}).");
        }

        if (from < FirstBar || to > LastBar)
        {
            return Words.T($"The piece has bars {FirstBar} to {LastBar}.", $"В пьесе такты с {FirstBar} по {LastBar}.");
        }

        return to - from + 1 > MotifSelection.MaxBars
            ? Words.T($"A range spans at most {MotifSelection.MaxBars} bars.", $"Диапазон — не больше {MotifSelection.MaxBars} тактов.")
            : null;
    }

    /// <summary>What music-core reads in bars <paramref name="from"/> to <paramref name="to"/>, in one hand or all: the same analysis as the Insights stream.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is not one <see cref="RangeRefusal"/> allows.</exception>
    public RangeReading Read(int from, int to, Hand? hand = null)
    {
        if (RangeRefusal(from, to) is { } refusal)
        {
            throw new ArgumentOutOfRangeException(nameof(to), refusal);
        }

        if (_readings.TryGetValue((from, to, hand), out RangeReading? kept))
        {
            return kept;
        }

        TickSpan span = TickSpan.FromBounds(Meter.GetBar(from).Span.Onset, Meter.GetBar(to).Span.End);
        MotifSelection selection = MotifSelector.Select(Notes, Meter, span, hand is { } only ? SelectionFilter.ForHand(only) : SelectionFilter.All);
        return _readings[(from, to, hand)] = RangeReading.Of(this, selection);
    }

    private static Tick Later(Tick a, Tick b) => a > b ? a : b;
}

/// <summary>
/// What music-core read in a range, as the Insights stream reads it (the facade's understand): the line and key, the
/// structure, the character and the harmony, with the doubt of recognised notes still to check laid over them.
/// </summary>
public sealed record RangeReading(MotifSelection Selection, MotifAnalysis Motif, HarmonyAnalysis Harmony, StructureAnalysis Structure, IReadOnlyList<MotifFact> Character)
{
    /// <summary>Every fact of the range: the line's, the structure's, the character's and the harmony's, in that order.</summary>
    public IReadOnlyList<MotifFact> Facts => [.. Motif.Facts, .. Structure.Facts, .. Character, .. Harmony.Facts];

    /// <summary>The range's notes, in the selection's order.</summary>
    public IReadOnlyList<Note> NotesOf(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var ids = Selection.NoteIds.ToHashSet(StringComparer.Ordinal);
        return [.. notes.Where(note => ids.Contains(note.Id)).OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
    }

    internal static RangeReading Of(AssistantPiece piece, MotifSelection selection)
    {
        NoteDoubt doubt = piece.Doubt;
        ImportedPiece imported = piece.Piece;
        KeySignature? key = KeySignature.InForce(imported.Keys, selection.Span.Onset);
        IReadOnlyList<PhraseBoundary> phrases = piece.Structure.Levels.Phrases.Boundaries;
        MotifAnalysis read = MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, key, imported.MusicXml?.AccentMarks, phrases, piece.Words);
        HarmonyAnalysis harmony = doubt.On(HarmonyAnalyzer.Analyze(piece.Notes, piece.Meter, selection, read.Key?.Key, piece.Words), selection);
        StructureAnalysis structure = doubt.On(StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, phrases, piece.Words), selection);
        IReadOnlyList<MotifFact> character = doubt.On(MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, key, CharacterContext.FromPiece(imported), piece.Words), selection);
        return new RangeReading(selection, doubt.On(read, selection), harmony, structure, character);
    }
}