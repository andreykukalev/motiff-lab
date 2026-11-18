using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Pieces;

public enum SourceFormat
{
    Midi,
    MusicXml,
}

/// <summary>A piece imported from one source file: the shared event model, the report and the source facts.</summary>
/// <param name="Tracks">The tracks or parts that hold notes, in file order.</param>
/// <param name="Midi">The full MIDI import, when the source is MIDI.</param>
/// <param name="MusicXml">The full MusicXML import, when the source is MusicXML.</param>
/// <param name="ScoreXml">The score's XML as written, unpacked from an .mxl archive, for the score view.</param>
public sealed record ImportedPiece(
    string FileName,
    SourceFormat Format,
    IReadOnlyList<ImportedTrack> Tracks,
    IReadOnlyList<Note> Notes,
    MeterMap Meter,
    IReadOnlyList<KeySignature> Keys,
    Tick End,
    ImportReport Report,
    MidiImport? Midi,
    MusicXmlImport? MusicXml,
    byte[]? ScoreXml)
{
    /// <summary>For a transcription, the bars and tempo worked out from its notes (<see cref="BarGrid.Apply"/>); null otherwise.</summary>
    public BarGrid? Bars { get; init; }

    /// <summary>The tempo of notes made by an assembly and applied to the piece (decision record 0024); null for a piece as its file has it.</summary>
    public IReadOnlyList<PlaybackTempo>? AssembledTempos { get; init; }

    /// <summary>
    /// The tempo the player set for the piece, in quarter notes a minute (project file "tempo", version 0.5.4): what it
    /// plays and is marked at from its start (<c>PlayerTempo</c>); null to play as the piece states.
    /// </summary>
    public int? PlayerTempo { get; init; }

    /// <summary>
    /// What the player can start with (N2 step 4): the whole piece, each hand the notes carry, and
    /// each track when there are several. A hand that is inferred says so.
    /// </summary>
    public IReadOnlyList<StartChoice> StartChoices
    {
        get
        {
            var choices = new List<StartChoice> { new("Whole piece", SelectionFilter.All, IsInferred: false) };
            foreach ((Hand hand, string label) in new[] { (Hand.Right, "Right hand"), (Hand.Left, "Left hand") })
            {
                var hands = Notes.Where(note => note.Hand?.Value == hand).ToList();
                if (hands.Count > 0)
                {
                    bool inferred = hands.Any(note => note.Hand!.Attribution.Provenance == Provenance.Inferred);
                    choices.Add(new(inferred ? $"{label} (inferred)" : label, SelectionFilter.ForHand(hand), inferred));
                }
            }

            if (Tracks.Count > 1)
            {
                choices.AddRange(Tracks.Select(track => new StartChoice(track.Label, SelectionFilter.ForTrack(track.Index), IsInferred: false)));
            }

            return choices;
        }
    }
}

/// <param name="Index">Zero-based track or part index, as on <see cref="Note.Track"/>.</param>
public sealed record ImportedTrack(int Index, string? Name, string? InstrumentName, int NoteCount, TrackHands Hands)
{
    /// <summary>"Track 2: Right hand", or "Track 2" when the file gives no name.</summary>
    public string Label => string.IsNullOrWhiteSpace(Name) ? $"Track {Index + 1}" : $"Track {Index + 1}: {Name}";
}

/// <summary>Where a track's hands come from (S1-I4).</summary>
public enum TrackHands
{
    /// <summary>The notes carry no hands.</summary>
    None,

    /// <summary>From the source, for example a grand staff.</summary>
    Imported,

    /// <summary>Worked out by <c>HandInference</c> for some or all notes.</summary>
    Inferred,
}

/// <summary>A place to start exploring, offered after import (N2 step 4).</summary>
public sealed record StartChoice(string Label, SelectionFilter Filter, bool IsInferred);