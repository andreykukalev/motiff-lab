using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Pieces;

/// <summary>A key signature as the source file states it.</summary>
/// <param name="Fifths">Sharps (positive) or flats (negative), −7 to 7.</param>
/// <param name="Mode">The mode when the file states it; null when it gives only the sharps or flats.</param>
public sealed record KeySignature(Tick At, int Fifths, KeyMode? Mode)
{
    /// <summary>A MIDI key signature always states its mode.</summary>
    public static IReadOnlyList<KeySignature> FromMidi(MidiImport import)
    {
        ArgumentNullException.ThrowIfNull(import);
        return [.. import.KeySignatures
            .Select(key => new KeySignature(key.At, Math.Clamp(key.Sharps, -7, 7), key.IsMinor ? KeyMode.Minor : KeyMode.Major))
            .OrderBy(key => key.At)];
    }

    /// <summary>The key signatures of the first part that has any, as the score view uses them. Modes other than major and minor count as not stated.</summary>
    public static IReadOnlyList<KeySignature> FromMusicXml(MusicXmlImport import)
    {
        ArgumentNullException.ThrowIfNull(import);
        int firstTrack = import.KeySignatures.Count == 0 ? 0 : import.KeySignatures.Min(key => key.Track);
        return [.. import.KeySignatures
            .Where(key => key.Track == firstTrack)
            .Select(key => new KeySignature(key.At, Math.Clamp(key.Fifths, -7, 7), ModeOf(key.Mode)))
            .OrderBy(key => key.At)];
    }

    /// <summary>The key signature in force at a position, or null before the first.</summary>
    public static KeySignature? InForce(IEnumerable<KeySignature> keys, Tick position)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.Where(key => key.At <= position).OrderBy(key => key.At).LastOrDefault();
    }

    private static KeyMode? ModeOf(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "major" or "ionian" => KeyMode.Major,
        "minor" or "aeolian" => KeyMode.Minor,
        _ => null,
    };
}