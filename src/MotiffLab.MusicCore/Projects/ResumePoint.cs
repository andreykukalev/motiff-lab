using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The overlays a resume point can find open again (0018, section 2).</summary>
public enum Overlay
{
    PianoRoll,
    Insights,
    Contents,
    Check,
}

/// <summary>
/// Where the player left off in a piece (0018, section 9): the place on the score, the selection or
/// focused phrase, the overlays that were open, the tempo and the hands. Opening a piece creates one and
/// the app updates it as the player works; it can be named, and several per piece are kept. Resume points
/// replace the sessions of 0012: a version 11 file's sessions open as resume points named by their task.
/// </summary>
/// <remarks>Times are passed in, never read from a clock, so building a resume point is pure.</remarks>
/// <param name="Id">Stable for the life of the project, for example "r_0001".</param>
/// <param name="Name">The player's name for it, trimmed, or null.</param>
/// <param name="At">The position the view was at: the start of the bar at the top of the page or strip.</param>
/// <param name="Passage">The selection, or the focused phrase, or null when nothing was selected.</param>
/// <param name="Focused">Whether the passage was open in phrase focus.</param>
/// <param name="Overlays">The overlays that were open, each once.</param>
/// <param name="TempoPercent">The playback tempo as a share of the piece's, 25 to 150.</param>
/// <param name="Hands">The hand heard alone, or null for both.</param>
/// <param name="Loop">Whether the passage was looping.</param>
public sealed record ResumePoint(
    string Id,
    string? Name,
    Tick At,
    SavedPassage? Passage,
    bool Focused,
    IReadOnlyList<Overlay> Overlays,
    int TempoPercent,
    Hand? Hands,
    bool Loop,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActive)
{
    public const int MinTempoPercent = 25;
    public const int MaxTempoPercent = 150;

    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A resume point needs an id.", nameof(Id));

    public string? Name { get; } = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();

    public IReadOnlyList<Overlay> Overlays { get; } = [.. (Overlays ?? throw new ArgumentNullException(nameof(Overlays))).Distinct().Order()];

    public int TempoPercent { get; } = TempoPercent is >= MinTempoPercent and <= MaxTempoPercent
        ? TempoPercent
        : throw new ArgumentOutOfRangeException(nameof(TempoPercent), TempoPercent, $"The tempo is {MinTempoPercent} to {MaxTempoPercent} percent.");

    /// <summary>A resume point for a piece just opened at its start, with the tempo as written and both hands.</summary>
    public static ResumePoint Start(string id, DateTimeOffset now, string? name = null) => new(id, name, Tick.Zero, null, false, [], 100, null, false, now, now);

    /// <summary>The resume point as the player left it at <paramref name="now"/>.</summary>
    public ResumePoint Touch(Tick at, SavedPassage? passage, bool focused, IEnumerable<Overlay> overlays, int tempoPercent, Hand? hands, bool loop, DateTimeOffset now) =>
        new(Id, Name, at, passage, focused, [.. overlays ?? throw new ArgumentNullException(nameof(overlays))], tempoPercent, hands, loop, CreatedAt, now);

    /// <summary>The resume point with the passage the player works on now, active at <paramref name="now"/>; the rest stays.</summary>
    public ResumePoint Touch(SavedPassage? passage, DateTimeOffset now) => this with { Passage = passage, At = passage?.Span.Onset ?? At, LastActive = now };

    /// <summary>The resume point under another name; blank takes the name away.</summary>
    public ResumePoint Rename(string? name) => new(Id, name, At, Passage, Focused, Overlays, TempoPercent, Hands, Loop, CreatedAt, LastActive);

    public bool Equals(ResumePoint? other) =>
        other is not null && Id == other.Id && Name == other.Name && At == other.At && Passage == other.Passage && Focused == other.Focused
        && TempoPercent == other.TempoPercent && Hands == other.Hands && Loop == other.Loop && CreatedAt == other.CreatedAt && LastActive == other.LastActive
        && Overlays.SequenceEqual(other.Overlays);

    public override int GetHashCode() => HashCode.Combine(Id, At, LastActive);
}