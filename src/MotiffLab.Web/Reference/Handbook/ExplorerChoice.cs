using MotiffLab.MusicCore;
using MotiffLab.MusicCore.Analysis;

namespace MotiffLab.Web.Reference.Handbook;

/// <summary>
/// What the harmony explorer holds (version 0.5), kept by the Reference while it is open: a key, a progression of its chords,
/// each in a position, and how the progression is played. It is a try-out: nothing of it is saved or written into a piece.
/// </summary>
public sealed class ExplorerChoice
{
    /// <summary>The tempos the explorer offers, quarters a minute.</summary>
    public static IReadOnlyList<int> Tempos => HarmonyExplorer.Tempos;

    public ExplorerChoice(MusicalKey key) => Start(key);

    public MusicalKey Key { get; private set; }

    public List<ExplorerStep> Steps { get; } = [];

    public ExplorerTexture Texture { get; set; } = ExplorerTexture.Alberti;

    public ChordSpacing Spacing { get; set; } = ChordSpacing.Close;

    /// <summary>Quarters a minute.</summary>
    public int Tempo { get; set; } = 72;

    public bool Loop { get; set; }

    /// <summary>The bar whose position and removal are offered; null when none is.</summary>
    public int? Selected { get; set; }

    public int MicrosecondsPerQuarter => 60_000_000 / Tempo;

    /// <summary>A new key starts from its tonic, subdominant, dominant and tonic.</summary>
    public void Start(MusicalKey key)
    {
        Key = key;
        Steps.Clear();
        Steps.AddRange(HarmonyExplorer.Start(key));
        Selected = null;
    }

    /// <summary>A chord of the key at the end, in root position, while there is room.</summary>
    public void Add(string chordId)
    {
        if (Steps.Count < HarmonyExplorer.MaxBars)
        {
            Steps.Add(new ExplorerStep(chordId, 0));
        }
    }

    public void Turn(int bar, int inversion) => Steps[bar] = Steps[bar] with { Inversion = inversion };

    public void Remove(int bar)
    {
        Steps.RemoveAt(bar);
        Selected = null;
    }

    /// <summary>The next tempo, round to the first after the last.</summary>
    public void NextTempo()
    {
        int index = Tempos.ToList().IndexOf(Tempo);
        Tempo = Tempos[(index + 1) % Tempos.Count];
    }
}