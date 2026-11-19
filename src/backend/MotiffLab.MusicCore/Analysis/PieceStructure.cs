using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The structure of the piece, computed once and read by every view (decision record 0027, section 5; #347): the phrase
/// levels the brackets show, the sections of the form the Contents and Piece DNA show, and the landmarks that exist today
/// beside them, the closes of the phrases and the motifs heard again across the piece. The levels are kept apart: a
/// section of the form is not a sentence, a close is not a boundary and a motif is not a phrase, though each is read
/// from the same phrases.
/// </summary>
/// <remarks>
/// <para>
/// Every part is inferred and says so where it is shown: the phrases keep their evidence, the sections are lettered by
/// the material they share (<see cref="FormAnalyzer"/>), the closes are readings of the last notes
/// (<see cref="SentenceMap"/>) and the motifs are openings heard again (<see cref="IdeaFinder"/>).
/// </para>
/// <para>
/// Pure: the same notes, meter, phrase levels and language give the same structure. The sections and the motifs are read
/// the first time they are asked for and then kept, so a view that needs only the phrases pays for nothing else.
/// </para>
/// </remarks>
public sealed class PieceStructure
{
    private readonly IReadOnlyList<Note> _notes;
    private readonly MeterMap _meter;
    private readonly Words _words;
    private IReadOnlyList<DnaSection>? _sections;
    private IReadOnlyList<DnaIdea>? _motifs;

    private PieceStructure(IReadOnlyList<Note> notes, MeterMap meter, PhraseLevels levels, Words words)
    {
        _notes = notes;
        _meter = meter;
        _words = words;
        Levels = levels;
    }

    /// <summary>The phrase levels: short units, phrases and sentences, with the player's edits.</summary>
    public PhraseLevels Levels { get; }

    /// <summary>The sections of the form, in time order, lettered by the material they share; empty without notes.</summary>
    public IReadOnlyList<DnaSection> Sections => _sections ??= FormAnalyzer.Sections(_notes, _meter, Levels.Sentences);

    /// <summary>How each phrase ends, full, half or no close: the harmonic landmarks the phrases have today.</summary>
    public IReadOnlyList<PhraseClose> Closes => Levels.Sentences.Closes;

    /// <summary>The motifs heard again across the piece, the one heard most often first: the openings of the sentences and phrases that come back.</summary>
    public IReadOnlyList<DnaIdea> Motifs => _motifs ??= IdeaFinder.Find(_notes, _meter, Levels.Sentences, Sections, _words);

    /// <summary>
    /// The motifs written in <paramref name="words"/>: the ones kept when it is the language the structure was read in,
    /// otherwise read again, so a view in a language chosen later never shows them in the old one.
    /// </summary>
    public IReadOnlyList<DnaIdea> MotifsIn(Words words) =>
        words == _words ? Motifs : IdeaFinder.Find(_notes, _meter, Levels.Sentences, Sections, words);

    /// <summary>The structure of <paramref name="notes"/> read at <paramref name="levels"/>, in <paramref name="words"/>.</summary>
    public static PieceStructure Of(IReadOnlyList<Note> notes, MeterMap meter, PhraseLevels levels, Words words = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(levels);
        return new PieceStructure(notes, meter, levels, words);
    }

    /// <summary>The same notes read at other phrase levels, for example after the player changed a boundary.</summary>
    public PieceStructure With(PhraseLevels levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return ReferenceEquals(levels, Levels) ? this : new PieceStructure(_notes, _meter, levels, _words);
    }
}