using System.Globalization;
using System.Text;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// Where a new entity's id comes from (0026 S3, #317): a motif, a variation, an idea, a resume point, a bookmark (of the
/// notes or of a PDF's page), a category, a margin note, a highlight, a draft with its places and variants, a group of
/// steps, Notes, a version compared and an original but the first PDF. The caller gives it, so music-core
/// stays deterministic: the iPad gives <see cref="Random"/>, whose ids two devices working offline never both make, and
/// tests and goldens <see cref="Numbered"/>, the same ids for the same calls. An id is only ever compared whole, so ids
/// of either kind, and those of earlier builds, stay valid (V1).
/// </summary>
public abstract class IdSource
{
    private protected IdSource()
    {
    }

    /// <summary>The prefix and the 32 lowercase hex digits of a new GUID, as a project's id is (S3): "o_3f9c…".</summary>
    public static IdSource Random { get; } = new RandomIds();

    /// <summary>
    /// The prefix and one more than the highest number of the ids taken with it, in four digits at least ("o_0003") or as
    /// many as the caller asks ("n_2"), as builds before 1.6 made every id; also the rule where an id must come out the
    /// same on every read (a variation kept hidden, a carried note).
    /// </summary>
    public static IdSource Numbered { get; } = new NumberedIds();

    /// <summary>
    /// An id two devices make alike from the same <paramref name="key"/> (0026 C1 and C4): <paramref name="prefix"/> and the
    /// first 32 hex digits of the SHA-256 of the key in UTF-8; when one of <paramref name="taken"/> is that id already, of
    /// the key, "|" and 2, then 3, and so on, the first that none is.
    /// </summary>
    internal static string Hashed(string prefix, string key, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(key);
        HashSet<string> used = [.. taken];
        string id = Hash(prefix, key);
        for (int more = 2; used.Contains(id); more++)
        {
            id = Hash(prefix, string.Create(CultureInfo.InvariantCulture, $"{key}|{more}"));
        }

        return id;
    }

    private static string Hash(string prefix, string key) => prefix + PieceOriginal.Fingerprint(Encoding.UTF8.GetBytes(key))[..32];

    /// <summary>A new id with <paramref name="prefix"/> that none of <paramref name="taken"/> is.</summary>
    /// <param name="digits">The least digits of a numbered id: 4 for most, 1 where builds before 1.6 wrote "n_2".</param>
    internal abstract string Next(string prefix, IEnumerable<string> taken, int digits = 4);

    /// <summary>
    /// A new id of one of several of a kind named by <paramref name="stem"/> ("src_rec") that none of <paramref name="taken"/>
    /// is: random, the stem, "_" and 32 hex digits; numbered, the stem itself while it is free, else the stem, "_" and one
    /// more than the highest number, as the stem counts as the first ("src_rec_2").
    /// </summary>
    internal abstract string Stemmed(string stem, IReadOnlySet<string> taken);

    private sealed class RandomIds : IdSource
    {
        internal override string Next(string prefix, IEnumerable<string> taken, int digits = 4)
        {
            HashSet<string> used = [.. taken];
            string id;
            do
            {
                id = string.Create(CultureInfo.InvariantCulture, $"{prefix}{Guid.NewGuid():N}");
            }
            while (used.Contains(id));

            return id;
        }

        internal override string Stemmed(string stem, IReadOnlySet<string> taken) => Next(stem + "_", taken);
    }

    private sealed class NumberedIds : IdSource
    {
        internal override string Next(string prefix, IEnumerable<string> taken, int digits = 4)
        {
            int highest = taken
                .Select(id => id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0)
                .DefaultIfEmpty(0)
                .Max();
            return prefix + (highest + 1).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        internal override string Stemmed(string stem, IReadOnlySet<string> taken) =>
            taken.Contains(stem) ? Next(stem + "_", [.. taken, stem + "_1"], digits: 1) : stem;
    }
}