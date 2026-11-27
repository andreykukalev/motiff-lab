using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// The notes' source record («Как напечатано», step 1): how a MusicXML score, imported or read from a PDF (decision records
/// 0029 and 0030), writes each note, rest and space, the notes looked up by their ids, for <see cref="NotationGenerator"/>
/// to write the bars as printed. It reads what <see cref="MusicXmlImport"/> kept; it parses nothing itself.
/// </summary>
internal sealed class PrintedNotation
{
    private readonly Dictionary<string, (Note Note, MusicXmlNote Record, MusicXmlWrittenNote[] Written)> _notes;
    private readonly ILookup<(int Track, int Staff), (string Id, MusicXmlWrittenNote Piece)> _written;
    private readonly ILookup<(int Track, int Staff), MusicXmlRest> _rests;
    private readonly ILookup<(int Track, int Staff), MusicXmlSpace> _spaces;
    private readonly IReadOnlyList<MusicXmlPartInfo> _parts;

    private PrintedNotation(MusicXmlImport import)
    {
        ILookup<int, MusicXmlWrittenNote> written = import.WrittenNotes.ToLookup(piece => piece.SourceNote);
        _notes = new Dictionary<string, (Note, MusicXmlNote, MusicXmlWrittenNote[])>(StringComparer.Ordinal);
        for (int index = 0; index < import.SourceNotes.Count; index++)
        {
            _notes[import.Notes[index].Id] = (import.Notes[index], import.SourceNotes[index], [.. written[index]]);
        }

        _written = import.WrittenNotes
            .ToLookup(piece => (import.SourceNotes[piece.SourceNote].Track, piece.Staff), piece => (import.Notes[piece.SourceNote].Id, piece));
        _rests = import.Rests.ToLookup(rest => (rest.Track, rest.Staff));
        _spaces = import.Spaces.ToLookup(space => (space.Track, space.Staff));
        _parts = import.Parts;
    }

    /// <summary>The record of a MusicXML import's notes.</summary>
    public static PrintedNotation Of(MusicXmlImport import)
    {
        ArgumentNullException.ThrowIfNull(import);
        return new PrintedNotation(import);
    }

    /// <summary>The note as imported, its record and its written notes in order, by the note's id.</summary>
    internal bool TryGet(string id, out Note note, out MusicXmlNote record, out MusicXmlWrittenNote[] written)
    {
        bool found = _notes.TryGetValue(id, out (Note Note, MusicXmlNote Record, MusicXmlWrittenNote[] Written) entry);
        (note, record, written) = entry;
        written ??= [];
        return found;
    }

    /// <summary>
    /// The score's staff a staff of the notation shows: a grand staff's right hand its first and left hand its second, a
    /// part's only staff; null for a staff the score has not got as such (a part split at middle C, more than two staves).
    /// </summary>
    internal int? SourceStaff(StaffPlan staff)
    {
        if (staff.Track >= _parts.Count || staff.IsSplitInferred)
        {
            return null;
        }

        return (_parts[staff.Track].Staves, staff.Hand) switch
        {
            (1, null) => 1,
            (2, Hand.Right) => 1,
            (2, Hand.Left) => 2,
            _ => null,
        };
    }

    /// <summary>The written notes on a staff of a part that sound between two ticks, with the ids of their notes.</summary>
    internal IEnumerable<(string Id, MusicXmlWrittenNote Piece)> Written(int track, int staff, long start, long stop) =>
        _written[(track, staff)].Where(item => item.Piece.Span.Onset.Value < stop && item.Piece.Span.End.Value > start);

    /// <summary>The rests on a staff of a part between two ticks.</summary>
    internal IEnumerable<MusicXmlRest> Rests(int track, int staff, long start, long stop) =>
        _rests[(track, staff)].Where(rest => rest.Span.Onset.Value < stop && rest.Span.End.Value > start);

    /// <summary>The spaces on a staff of a part between two ticks.</summary>
    internal IEnumerable<MusicXmlSpace> Spaces(int track, int staff, long start, long stop) =>
        _spaces[(track, staff)].Where(space => space.Span.Onset.Value < stop && space.Span.End.Value > start);
}