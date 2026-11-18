using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>The parameter sets the explanation tests run each catalogue operation with: every kind it offers.</summary>
internal static class ParameterSets
{
    public static IEnumerable<OperationParameters> Every(IVariationOperation operation) => operation switch
    {
        RhythmReshape => [
            .. Enum.GetValues<RhythmReshapeKind>().Where(static kind => kind != RhythmReshapeKind.Displace).Select(static kind => new RhythmReshapeParameters(kind)),
            new RhythmReshapeParameters(RhythmReshapeKind.Displace, 1),
            new RhythmReshapeParameters(RhythmReshapeKind.Displace, 2),
        ],
        Articulation => Enum.GetValues<ArticulationKind>().Select(static kind => new ArticulationParameters(kind)),
        DensityAndRests => [
            .. Enum.GetValues<DensityKind>().Select(static kind => new DensityParameters(kind)),
            .. Enum.GetValues<DensityKind>().Select(static kind => new DensityParameters(kind, PatternUnit.Sixteenths)),
        ],
        AccentShift => [
            .. Enum.GetValues<AccentPattern>().Where(static pattern => pattern != AccentPattern.Custom).Select(static pattern => new AccentShiftParameters(pattern)),
            new AccentShiftParameters(AccentPattern.Custom, new IntervalSet([0, 2])),
        ],
        MelodicSequence => [new MelodicSequenceParameters(1, 1), new MelodicSequenceParameters(-2, 3), new MelodicSequenceParameters(2, 1, Diatonic: false)],
        AccompanimentPattern => [
            .. Enum.GetValues<AccompanimentPatternKind>().Select(static kind => new AccompanimentPatternParameters(kind)),
            .. Enum.GetValues<AccompanimentPatternKind>().Select(static kind => new AccompanimentPatternParameters(kind, PatternUnit.Sixteenths)),
        ],
        BassPattern => Enum.GetValues<BassPatternKind>().Select(static kind => new BassPatternParameters(kind)),
        VoicingAndRegister => Enum.GetValues<VoicingKind>().Select(static kind => new VoicingParameters(kind)),
        Simplify => Enumerable.Range(SimplifyParameters.MinLevel, SimplifyParameters.MaxLevel).Select(static level => new SimplifyParameters(level)),
        IntervalReshape => [new IntervalReshapeParameters(1, null), new IntervalReshapeParameters(-2, null), new IntervalReshapeParameters(-1, new IntervalSet([0]))],
        ChangeDirection => [new ChangeDirectionParameters(Diatonic: true, null), new ChangeDirectionParameters(Diatonic: false, null)],
        PassingNotes => Enum.GetValues<PassingNoteKind>().Select(static kind => new PassingNotesParameters(kind)),
        Ornament => Enum.GetValues<OrnamentKind>().Select(static kind => new OrnamentParameters(kind)),
        Reharmonise => Enum.GetValues<ReharmoniseKind>().Select(static kind => new ReharmoniseParameters(kind)),
        _ => [],
    };
}