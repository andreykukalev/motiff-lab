using System.Text.Json;

using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>What of a selection a fragment takes (decision record 0034, section 3): all of it, one hand, or the melody.</summary>
public enum DraftPart
{
    /// <summary>Every note of the selection.</summary>
    All,

    /// <summary>The right hand's notes.</summary>
    Right,

    /// <summary>The left hand's notes.</summary>
    Left,

    /// <summary>The top voice of the right hand, only where the voices are cleanly split (<see cref="DraftFragment.Melody"/>).</summary>
    Melody,
}

/// <summary>
/// Where a variant was copied from (decision record 0034, section 2; docs/project-file.md, "Drafts"): the Notes, of this
/// piece or of another one, the span in their ticks, its bars for the label, a recording's stretch, and the part taken.
/// </summary>
/// <param name="Notes">The id of the Notes it was copied from.</param>
/// <param name="Span">The span in those Notes' ticks.</param>
public sealed record DraftSource(string Notes, TickSpan Span)
{
    public string Notes { get; } = !string.IsNullOrWhiteSpace(Notes) ? Notes : throw new ArgumentException("A fragment names the Notes it was copied from.", nameof(Notes));

    /// <summary>The other piece's id when the Notes are another piece's; null for this piece's own.</summary>
    public string? Project { get; init; }

    /// <summary>The span's bar numbers in those Notes, both ends included, for the place's label; null when not known.</summary>
    public (int First, int Last)? Bars
    {
        get;
        init => field = value is not var (first, last) || (first >= 0 && last >= first) ? value : throw new ArgumentOutOfRangeException(nameof(Bars), value, "Bars run from the first to the last.");
    }

    /// <summary>The stretch of the recording the Notes were heard in, in milliseconds, from before to; null for notes not heard.</summary>
    public (long From, long To)? Milliseconds
    {
        get;
        init => field = value is not var (from, to) || (from >= 0 && to > from) ? value : throw new ArgumentOutOfRangeException(nameof(Milliseconds), value, "A stretch runs forward from 0.");
    }

    public DraftPart Part { get; init; } = DraftPart.All;
}

/// <summary>A tick of a place's base and the tick of a variant the player confirmed sound together («такт 9 = 1:12»).</summary>
public readonly record struct DraftAnchor(Tick Place, Tick Variant);

/// <summary>
/// A copy of a fragment with where it came from (decision record 0034, section 2): its own notes, bars, tempo and key from
/// 0, so it stays whole when its Notes, their original or their piece is removed, and the provenance of the Notes copied,
/// so the original's fingerprint travels with it. In a place its notes are named <see cref="NoteId"/>.
/// </summary>
/// <param name="Provenance">The provenance of the Notes copied, as it was; from another piece, each source names that piece.</param>
/// <param name="Music">The fragment's own music, from 0.</param>
public sealed record DraftVariant(string Id, DateTimeOffset AddedAt, DraftSource From, NotesProvenance Provenance, SourceMusic Music)
{
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A variant has an id.", nameof(Id));

    public DraftSource From { get; } = From ?? throw new ArgumentNullException(nameof(From));

    public NotesProvenance Provenance { get; } = Provenance ?? throw new ArgumentNullException(nameof(Provenance));

    public SourceMusic Music { get; } = Music is { Notes.Count: > 0 } ? Music : throw new ArgumentException("A fragment has notes.", nameof(Music));

    /// <summary>The ticks the player confirmed match the base, both strictly increasing; only on a variant that is not its place's base.</summary>
    public IReadOnlyList<DraftAnchor> Anchors
    {
        get;
        init => field = value.Zip(value.Skip(1)).All(static pair => pair.First.Place < pair.Second.Place && pair.First.Variant < pair.Second.Variant)
            ? [.. value]
            : throw new ArgumentException("Anchors strictly increase.", nameof(Anchors));
    } = [];

    /// <summary>The id a note of this variant has in a place: the variant's id and the note's (<c>v_2.n_0042</c>).</summary>
    public string NoteId(string noteId) => $"{Id}.{noteId}";

    /// <summary>
    /// For a variant music-core made (<see cref="NotesProvenance.Variation"/>: a bridge candidate, a carried приём), the text
    /// its provenance's params hold under <paramref name="param"/>, the rule or kind that made it; null for any other variant.
    /// </summary>
    internal string? MadeBy(string param)
    {
        using JsonDocument? made = Made();
        return made is not null && made.RootElement.ValueKind == JsonValueKind.Object && made.RootElement.TryGetProperty(param, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Whether music-core made this variant with the params <paramref name="parameters"/>, compared as JSON values: a project
    /// file read back holds them as it wrote them, indented.
    /// </summary>
    internal bool MadeWith(string parameters)
    {
        using JsonDocument? made = Made();
        using var other = JsonDocument.Parse(parameters);
        return made is not null && JsonElement.DeepEquals(made.RootElement, other.RootElement);
    }

    /// <summary>The params of a variant music-core made, parsed; null for any other variant or params that are not JSON.</summary>
    private JsonDocument? Made()
    {
        if (Provenance.Op != NotesProvenance.Variation || Provenance.ParamsJson is not { } parameters)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(parameters);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}