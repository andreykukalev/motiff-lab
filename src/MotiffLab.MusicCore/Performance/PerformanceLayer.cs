using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>
/// The comparison layer (decision record 0022, section 4): what another version plays, in the score's time, to
/// engrave as small staves above every system. In each bar that lines up, the printed notes the version also plays
/// are written as printed, so what agrees reads the same; the notes it adds are written where they fall on the 16th
/// grid, and so are its likely artefacts. Bars that do not line up stay empty, since nothing is claimed in them.
/// Pure and deterministic.
/// </summary>
public static class PerformanceLayer
{
    /// <summary>The prefix of every note id in the layer, followed by the version's own note id.</summary>
    public const string IdPrefix = "v-";

    /// <summary>The version's notes for the layer, and which of them it adds and which are likely artefacts.</summary>
    public static PerformanceLayerNotes Of(PerformanceComparison comparison, IReadOnlyList<PlayedNote> played, IReadOnlyList<Note> printed, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(played);
        ArgumentNullException.ThrowIfNull(printed);
        ArgumentNullException.ThrowIfNull(meter);
        Dictionary<string, PlayedNote> byId = played.ToDictionary(note => note.Id, StringComparer.Ordinal);
        Dictionary<string, Note> printedById = printed.ToDictionary(note => note.Id, StringComparer.Ordinal);
        List<Note> notes = [];
        List<string> added = [];
        List<string> artefacts = [];
        foreach (BarComparison bar in comparison.Bars.Where(bar => bar.Aligned))
        {
            TickSpan span = meter.GetBar(bar.Bar).Span;
            List<Note> inBar = [.. printed.Where(note => span.Contains(note.Span.Onset))];
            foreach (NoteMatch match in bar.Matched)
            {
                if (printedById.TryGetValue(match.PrintedId, out Note? same) && byId.TryGetValue(match.PlayedId, out PlayedNote? heard))
                {
                    notes.Add(same with { Id = IdPrefix + heard.Id, Velocity = Math.Clamp(heard.Velocity, Note.MinVelocity, Note.MaxVelocity), Attribution = Attribution.Inferred(bar.Confidence / 100.0) });
                }
            }

            foreach ((PlacedNote note, List<string> kind) in bar.PlayedNotPrinted.Select(note => (note, added)).Concat(bar.LikelyArtefacts.Select(note => (note, artefacts))))
            {
                if (!byId.TryGetValue(note.Id, out PlayedNote? heard))
                {
                    continue;
                }

                (TickSpan place, int track, Hand hand) = PerformanceTake.Place(note, heard, span, comparison.Alignment, inBar);
                notes.Add(new Note
                {
                    Id = IdPrefix + note.Id,
                    Pitch = note.Pitch,
                    Span = place,
                    Velocity = Math.Clamp(note.Velocity, Note.MinVelocity, Note.MaxVelocity),
                    Track = track,
                    Hand = new Attributed<Hand>(hand, Attribution.Inferred(0.5)),
                    Attribution = Attribution.Inferred(bar.Confidence / 100.0),
                });
                kind.Add(IdPrefix + note.Id);
            }
        }

        return new(
            [.. notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch).ThenBy(note => note.Id, StringComparer.Ordinal)],
            [.. added.Order(StringComparer.Ordinal)],
            [.. artefacts.Order(StringComparer.Ordinal)]);
    }
}

/// <summary>The version's notes in score time for the comparison layer.</summary>
/// <param name="Notes">Every note to engrave, ids starting with <see cref="PerformanceLayer.IdPrefix"/>.</param>
/// <param name="Added">Ids of the notes the version plays and the print does not have: plum in the layer.</param>
/// <param name="Artefacts">Ids of the notes that are likely a hearing error: grey, and hidden unless asked for.</param>
public sealed record PerformanceLayerNotes(IReadOnlyList<Note> Notes, IReadOnlyList<string> Added, IReadOnlyList<string> Artefacts);