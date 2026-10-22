namespace MotiffLab.MusicCore.Events;

/// <summary>Where an event or annotation came from.</summary>
/// <remarks>
/// Uncertainty is not a provenance: it is expressed by <see cref="Attribution.Confidence"/>.
/// The app shows any event below its confidence threshold as uncertain, whatever its provenance.
/// </remarks>
public enum Provenance
{
    /// <summary>Stated by the source file (MIDI, MusicXML). Stage 1 source facts.</summary>
    Imported,

    /// <summary>Output of audio analysis (Stage 3).</summary>
    Detected,

    /// <summary>Derived by a rule from other events, for example hands of a single-track piano MIDI.</summary>
    Inferred,

    /// <summary>A value the player confirmed without changing it.</summary>
    UserConfirmed,

    /// <summary>A value the player entered or changed.</summary>
    UserEdited,
}