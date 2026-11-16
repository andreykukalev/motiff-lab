namespace MotiffLab.MusicCore.Import.Midi;

/// <summary>A MIDI file that cannot be imported at all. The message is a plain reason for the player (N2).</summary>
public sealed class MidiImportException(MidiImportFailure failure, string message) : Exception(message)
{
    public MidiImportFailure Failure { get; } = failure;
}

public enum MidiImportFailure
{
    /// <summary>The bytes are not a Standard MIDI File.</summary>
    NotMidi,

    /// <summary>A valid MIDI file of a kind Stage 1 does not read (type 2, SMPTE timing, RIFF wrapper).</summary>
    Unsupported,

    /// <summary>The header or structure is too damaged to recover anything.</summary>
    Damaged,

    /// <summary>The file is larger than the Stage 1 limit (S1-I1).</summary>
    TooLarge,
}