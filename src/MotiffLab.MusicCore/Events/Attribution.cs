namespace MotiffLab.MusicCore.Events;

/// <summary>
/// The provenance of an event or annotation and how sure the system is of it.
/// </summary>
/// <remarks>
/// Confidence runs from 0 to 1. Imported facts and the player's own confirmations and edits are
/// certain, so their confidence is always 1. Only detected and inferred values can be less sure.
/// Use the factories; the constructor rejects combinations that break these rules.
/// </remarks>
public sealed record Attribution
{
    public Attribution(Provenance provenance, double confidence)
    {
        if (!Enum.IsDefined(provenance))
        {
            throw new ArgumentOutOfRangeException(nameof(provenance), provenance, "Unknown provenance.");
        }

        if (double.IsNaN(confidence) || confidence < 0 || confidence > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be between 0 and 1.");
        }

        if (provenance is Provenance.Imported or Provenance.UserConfirmed or Provenance.UserEdited && confidence != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, $"A {provenance} value is certain; its confidence must be 1.");
        }

        Provenance = provenance;
        Confidence = confidence;
    }

    public static Attribution Imported { get; } = new(Provenance.Imported, 1);

    public static Attribution UserConfirmed { get; } = new(Provenance.UserConfirmed, 1);

    public static Attribution UserEdited { get; } = new(Provenance.UserEdited, 1);

    public Provenance Provenance { get; }

    public double Confidence { get; }

    public static Attribution Detected(double confidence) => new(Provenance.Detected, confidence);

    public static Attribution Inferred(double confidence) => new(Provenance.Inferred, confidence);

    public override string ToString() => $"{Provenance} ({Confidence:0.##})";
}

/// <summary>A value together with where it came from, for fields that can be imported or inferred.</summary>
public sealed record Attributed<T>(T Value, Attribution Attribution)
{
    public Attribution Attribution { get; init; } = Attribution ?? throw new ArgumentNullException(nameof(Attribution));
}