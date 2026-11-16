using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Playback;

/// <summary>
/// Which hands a piece's notes carry, for hearing one hand alone (#328): a hand is offered only when some notes are
/// marked with it, and the offer says when any hand was inferred (<see cref="Import.HandInference"/>) rather than read
/// from the file, so the player knows it is a reading of the notes. Pure: the same notes give the same answer.
/// </summary>
/// <param name="Right">Whether any note is marked for the right hand.</param>
/// <param name="Left">Whether any note is marked for the left hand.</param>
/// <param name="Inferred">Whether any note's hand was inferred.</param>
public readonly record struct PlaybackHands(bool Right, bool Left, bool Inferred)
{
    /// <summary>Whether either hand can be heard alone: the notes carry both.</summary>
    public bool CanSeparate => Right && Left;

    public static PlaybackHands Of(IEnumerable<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        bool right = false;
        bool left = false;
        bool inferred = false;
        foreach (Note note in notes)
        {
            if (note.Hand is not { } hand)
            {
                continue;
            }

            right |= hand.Value == Hand.Right;
            left |= hand.Value == Hand.Left;
            inferred |= hand.Attribution.Provenance == Provenance.Inferred;
        }

        return new PlaybackHands(right, left, inferred);
    }
}