namespace MotiffLab.Web.Score;

/// <summary>The name that heads a score's first page and names its PDF (decision record 0017).</summary>
public static class ScoreTitle
{
    private static readonly string[] s_extensions = [".mid", ".midi", ".kar", ".musicxml", ".xml", ".mxl", ".json", ".motiff"];

    /// <summary>A piece's file name without the extensions of the files it comes from: "Für Elise.mid" heads its page as "Für Elise".</summary>
    public static string? Of(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        string title = fileName.Trim();
        // "piece.motiff.json" loses both.
        for (int pass = 0; pass < 2; pass++)
        {
            string extension = Path.GetExtension(title);
            if (extension.Length == 0 || !s_extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) || extension.Length == title.Length)
            {
                break;
            }

            title = title[..^extension.Length];
        }

        return title;
    }
}