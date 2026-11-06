using MotiffLab.MusicCore.Import;

namespace MotiffLab.Web.Playback;

/// <summary>
/// Beethoven's Für Elise (WoO 59), the whole piece, for trying the lab without a file: the pilot piece
/// (docs/07-product-definition.md). One piano part on two staves, 3/8 in A minor with a 16th pickup,
/// with the edition's repeats and endings written as signs. The notes are the Mutopia Project's
/// edition (Mutopia-2015/08/18-931), placed in the public domain by its typesetter;
/// <c>tools/fur-elise/build.mjs</c> writes them as <c>FurElise.musicxml</c>, embedded in the app and
/// bundled in the iPad app, so both open the same notes as any MusicXML file the player opens.
/// </summary>
internal static class FurElise
{
    public const string Title = "Für Elise";

    /// <summary>The piece, imported as a MusicXML file is.</summary>
    public static ImportedPiece Import()
    {
        using Stream stream = typeof(FurElise).Assembly.GetManifestResourceStream("MotiffLab.Web.Playback.FurElise.musicxml")
            ?? throw new InvalidOperationException("Für Elise is embedded in the app.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return PieceImporter.Import("FurElise.musicxml", memory.ToArray()).Piece
            ?? throw new InvalidOperationException("Für Elise imports as MusicXML.");
    }
}