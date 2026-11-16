using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Invitations;

/// <summary>What the experiment catalogue and the Try chips read: a motif, the piece around it, and what the engine measured.</summary>
/// <param name="Notes">The notes of the piece; the motif's notes are picked by id.</param>
/// <param name="Character">The motif's character facts (<see cref="MotifAnalyzer.AnalyzeCharacter"/>).</param>
public sealed record InvitationContext(
    IReadOnlyList<Note> Notes,
    MeterMap Meter,
    MotifSelection Selection,
    MotifAnalysis Motif,
    StructureAnalysis Structure,
    IReadOnlyList<MotifFact> Character,
    Words Words = default)
{
    /// <summary>The motif's chords and bass (<see cref="HarmonyAnalyzer.Analyze"/>, S1-A3); none on a single line.</summary>
    public HarmonyAnalysis Harmony { get; init; } = HarmonyAnalysis.None;
}