namespace MotiffLab.MusicCore.Time;

/// <summary>
/// The MusicXML <c>&lt;divisions&gt;</c> value: how many duration units make a quarter note.
/// </summary>
/// <remarks>
/// <para>
/// Scope: divisions are declared in a part's <c>&lt;attributes&gt;</c> and apply from that point
/// in that part until the part declares a new value. Different parts can use different values.
/// MusicXML durations are lengths, not positions; an importer builds positions by summing
/// note, <c>&lt;forward&gt;</c> and <c>&lt;backup&gt;</c> durations. It should sum the exact
/// <see cref="QuarterFraction"/> results of <see cref="ToQuarters"/> so that a divisions change
/// never introduces rounding, and round to ticks only through <see cref="CanonicalTime"/>.
/// </para>
/// <para>
/// MusicXML allows decimal divisions and durations; real files use integers, and only integers
/// are supported here. The importer reports non-integer values as unsupported.
/// </para>
/// </remarks>
public sealed record MusicXmlDivisions
{
    public MusicXmlDivisions(long divisionsPerQuarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(divisionsPerQuarter, 1);
        DivisionsPerQuarter = divisionsPerQuarter;
    }

    public long DivisionsPerQuarter { get; }

    /// <summary>Exact length in quarter notes of a duration given in these divisions.</summary>
    public QuarterFraction ToQuarters(long duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(duration);
        return new QuarterFraction(duration, DivisionsPerQuarter);
    }
}