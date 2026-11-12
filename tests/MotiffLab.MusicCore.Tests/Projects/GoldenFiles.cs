using System.Text;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>The golden project files of the format tests (Golden/README.md), read from the build output and written again on purpose.</summary>
internal static class GoldenFiles
{
    /// <summary>A file's text with one kind of line ending, to compare.</summary>
    public static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n");

    /// <summary>A golden file as the tests see it.</summary>
    public static byte[] Kept(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", name));

    /// <summary>
    /// That <paramref name="written"/> is the golden and that the golden reads back and is written unchanged; with
    /// MOTIFF_UPDATE_SNAPSHOTS=1 it writes the golden instead, when the format changes on purpose.
    /// </summary>
    public static void AssertIs(string name, byte[] written, [System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        if (Environment.GetEnvironmentVariable("MOTIFF_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source))!, "Golden", name), written);
            return;
        }

        byte[] kept = Kept(name);
        Assert.Equal(Text(kept), Text(written));
        Assert.Equal(Text(kept), Text(ProjectFile.Write(ProjectFile.Read(kept))));
    }
}
