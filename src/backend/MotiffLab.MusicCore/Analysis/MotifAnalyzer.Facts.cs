using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The sentences every lens of <c>MotifAnalyzer</c> shares: the key fact, the hand's inference carried into each fact,
/// and a position as a place label. The lenses' own sentences are in <c>.Melody.Facts</c>, <c>.Rhythm.Facts</c> and
/// <c>.Character.Facts</c>.
/// </summary>
internal static partial class MotifAnalyzer
{
    /// <summary>Detected facts become inferred when the hand that picked the motif's notes is inferred.</summary>
    private static List<MotifFact> WithHand(List<MotifFact> facts, MotifSelection selection, Words w)
    {
        if (!selection.HandInferred)
        {
            return facts;
        }

        string handBasis = w.T(
            "The hand is inferred, so which notes the motif holds is inferred too. ",
            "Рука предположена, поэтому и состав нот мотива предположен. ");
        return [.. facts.Select(fact => fact.Certainty == FactCertainty.Detected
            ? fact with { Certainty = FactCertainty.Inferred, Basis = handBasis + fact.Basis }
            : fact)];
    }

    private static MotifFact KeyFact(KeyReading key, Pieces.KeySignature? signature, int noteCount, int pitchClasses, Words w)
    {
        string name = key.Key.NameIn(w);
        string? runnerUp = key.RunnerUp?.NameIn(w);
        string thin = noteCount < MinNotesForKey || pitchClasses < MinPitchClassesForKey
            ? w.IsRussian
                ? $" При {w.Count(noteCount, "", "", "ноте", "нотах", "нотах")} и {w.Count(pitchClasses, "", "", "звуковысотном классе", "звуковысотных классах", "звуковысотных классах")} это слабая догадка."
                : $" With {Words.EnglishCount(noteCount, "note", "notes")} and {Words.EnglishCount(pitchClasses, "pitch class", "pitch classes")}, that is a weak guess."
            : key.Certainty == FactCertainty.Uncertain ? w.T($" {runnerUp} fits almost as well.", $" {Words.Capitalize(runnerUp ?? "")} подходит почти так же.") : "";

        string basis = w.IsRussian
            ? signature switch
            {
                { Mode: not null } => $"В файле указаны ключевые знаки ({w.Accidentals(signature.Fifths)}) и лад.",
                not null => $"Ключевые знаки: {w.Accidentals(signature.Fifths)}, но лад не указан; ноты подходят к тональности {name} лучше, чем к тональности {runnerUp}.{thin}",
                null => $"В файле нет ключевых знаков; ноты лучше всего подходят к тональности {name}, затем к тональности {runnerUp}.{thin}",
            }
            : signature switch
            {
                { Mode: not null } => $"The file states the key signature ({w.Accidentals(signature.Fifths)}) and the mode.",
                not null => $"The key signature has {w.Accidentals(signature.Fifths)} but no mode; the notes fit {name} better than {runnerUp}.{thin}",
                null => $"The file has no key signature; the notes fit {name} best, then {runnerUp}.{thin}",
            };

        return new MotifFact("key", FactLens.Key, key.Certainty, w.T($"Key: {name}.", $"Тональность: {name}."), basis);
    }

    /// <summary>"bar 2 beat 1", or "bar 2 after beat 1" off the beat; "такт 2 доля 1", "такт 2 после доли 1".</summary>
    private static string Place(MetricPosition position, Words w) => w.IsRussian
        ? position.BeatOffset == 0 ? $"такт {position.Bar} доля {position.Beat}" : $"такт {position.Bar} после доли {position.Beat}"
        : position.BeatOffset == 0 ? $"bar {position.Bar} beat {position.Beat}" : $"bar {position.Bar} after beat {position.Beat}";
}