using MotiffLab.MusicCore.Import.Scan;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// Marks a project whose source was read from a photo or PDF by sheet music recognition (decision
/// record 0011): which engine read it, and the bars the player checked and said look right.
/// </summary>
/// <remarks>
/// The project's source is the MusicXML the engine wrote; the picture itself is never kept. On every
/// open the notes are marked from <see cref="ScanCheck"/> and these bars, so the marks need not be stored.
/// Compares by value.
/// </remarks>
/// <param name="Engine">The engine and its version as it reported them, for example "homr 0.7.0".</param>
public sealed record ScanOrigin(string Engine, IReadOnlyList<int> ConfirmedBars)
{
    public string Engine { get; } = !string.IsNullOrWhiteSpace(Engine) ? Engine : throw new ArgumentException("The engine is required.", nameof(Engine));

    /// <summary>The bars the player said look right, in bar order, each once.</summary>
    public IReadOnlyList<int> ConfirmedBars { get; } = [.. (ConfirmedBars ?? throw new ArgumentNullException(nameof(ConfirmedBars))).Distinct().Order()];

    /// <summary>The original the notes were read from (<see cref="PieceOriginal.Id"/>, version 0.3); null when not kept.</summary>
    public string? From { get; init; }

    /// <summary>When the notes were read (version 0.3); null when not kept.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>A scan just read, with no bar checked yet.</summary>
    public static ScanOrigin Read(string engine) => new(engine, []);

    public bool IsConfirmed(int bar) => ConfirmedBars.Contains(bar);

    /// <summary>The origin with the bars added to the confirmed ones.</summary>
    public ScanOrigin Confirm(IEnumerable<int> bars) => new(Engine, [.. ConfirmedBars, .. bars]) { From = From, At = At };

    /// <summary>The origin with the bar taken out of the confirmed ones, so its notes are detected again.</summary>
    public ScanOrigin Unconfirm(int bar) => new(Engine, [.. ConfirmedBars.Where(confirmed => confirmed != bar)]) { From = From, At = At };

    public bool Equals(ScanOrigin? other) => other is not null && Engine == other.Engine && ConfirmedBars.SequenceEqual(other.ConfirmedBars) && From == other.From && At == other.At;

    public override int GetHashCode() => ConfirmedBars.Aggregate(Engine.GetHashCode(StringComparison.Ordinal), HashCode.Combine);
}