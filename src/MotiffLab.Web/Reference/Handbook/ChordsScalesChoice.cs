using MotiffLab.MusicCore.Analysis;

namespace MotiffLab.Web.Reference.Handbook;

/// <summary>
/// What «Аккорды и гаммы» shows (version 0.5), kept by the Reference while it is open so a tab chosen again finds it as it
/// was: what sounds (a chord or a scale from a root) apart from how it sounds. Nothing of it is saved.
/// </summary>
public sealed class ChordsScalesChoice
{
    public bool Scale { get; set; }

    public SpelledName Root { get; set; } = new(4, 0);

    public ChordType Type { get; set; } = ChordType.Dominant7;

    public ScaleForm Form { get; set; } = ScaleForm.Major;

    public int Inversion { get; set; }

    public ChordSpacing Spacing { get; set; } = ChordSpacing.Close;

    public ChordPattern Pattern { get; set; } = ChordPattern.UpAndDown;

    public ScalePattern ScalePattern { get; set; } = ScalePattern.UpAndDown;

    public int Octaves { get; set; } = 1;

    public bool Loop { get; set; }
}