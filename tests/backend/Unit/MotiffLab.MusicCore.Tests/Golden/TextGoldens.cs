using System.Runtime.CompilerServices;

namespace MotiffLab.MusicCore.Tests.Golden;

/// <summary>The text goldens of a test area (<c>&lt;area&gt;/Golden/*.txt</c>), read from the build output and written again on purpose.</summary>
internal static class TextGoldens
{
    /// <summary>
    /// That <paramref name="written"/> is the golden <paramref name="name"/> of <paramref name="area"/>; with
    /// MOTIFF_UPDATE_SNAPSHOTS=1 it writes the golden instead, when the output changes on purpose. A case that only checks
    /// its output against another case's golden passes <paramref name="writes"/> false, and is checked in either mode.
    /// </summary>
    /// <param name="area">The test folder the golden lives under, such as <c>Notation</c> or <c>Import/MusicXml</c>.</param>
    public static void AssertTextGolden(string area, string name, string written, bool writes = true)
    {
        if (writes && Environment.GetEnvironmentVariable("MOTIFF_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(Path.Combine(ProjectDirectory(), area, "Golden", name), written);
            return;
        }

        string kept = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, area, "Golden", name)).ReplaceLineEndings("\n");
        Assert.Equal(kept, written);
    }

    /// <summary>The test project's folder in the source tree, where a golden is written.</summary>
    private static string ProjectDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(Path.GetDirectoryName(source))!;
}