namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// Ranks keys by how well a passage's pitch classes fit them: the Krumhansl–Kessler key profiles,
/// correlated with the time each pitch class sounds. Pure and deterministic; ties go to the key
/// listed first (C major, C minor, C♯ major, …).
/// </summary>
internal static class KeyFinder
{
    private static readonly double[] s_major = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] s_minor = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    /// <summary>Every major and minor key with its correlation, from −1 to 1, best first.</summary>
    /// <param name="weights">How long each pitch class sounds, indexed 0 (C) to 11 (B).</param>
    public static IReadOnlyList<(MusicalKey Key, double Score)> Rank(IReadOnlyList<double> weights)
    {
        var scores = new List<(MusicalKey Key, double Score, int Order)>(24);
        for (int tonic = 0; tonic < 12; tonic++)
        {
            scores.Add((MusicalKey.OnTonic(tonic, KeyMode.Major), Correlate(weights, s_major, tonic), tonic * 2));
            scores.Add((MusicalKey.OnTonic(tonic, KeyMode.Minor), Correlate(weights, s_minor, tonic), (tonic * 2) + 1));
        }

        return [.. scores.OrderByDescending(score => score.Score).ThenBy(score => score.Order).Select(score => (score.Key, score.Score))];
    }

    /// <summary>The correlation of one key, or of a key with the given signature and mode.</summary>
    public static double Score(IReadOnlyList<double> weights, MusicalKey key) =>
        Correlate(weights, key.Mode == KeyMode.Major ? s_major : s_minor, key.TonicPitchClass);

    private static double Correlate(IReadOnlyList<double> weights, double[] profile, int tonic)
    {
        double weightMean = weights.Average();
        double profileMean = profile.Average();
        double covariance = 0;
        double weightVariance = 0;
        double profileVariance = 0;
        for (int pitchClass = 0; pitchClass < 12; pitchClass++)
        {
            double weight = weights[pitchClass] - weightMean;
            double fit = profile[(pitchClass - tonic + 12) % 12] - profileMean;
            covariance += weight * fit;
            weightVariance += weight * weight;
            profileVariance += fit * fit;
        }

        return weightVariance == 0 ? 0 : covariance / Math.Sqrt(weightVariance * profileVariance);
    }
}