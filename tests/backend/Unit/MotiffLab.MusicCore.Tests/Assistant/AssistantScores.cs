using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>Small scores for the assistant's tests, written as MusicXML.</summary>
internal static class AssistantScores
{
    /// <summary>A one-part grand staff in 3/4 with a quarter as 2 divisions; each string is the content of one measure.</summary>
    public static ImportedPiece Score(params string[] measures)
    {
        var xml = new StringBuilder("<score-partwise><part-list><score-part id=\"P1\"><part-name>Piano</part-name></score-part></part-list><part id=\"P1\">");
        for (int i = 0; i < measures.Length; i++)
        {
            string attributes = i == 0 ? "<attributes><divisions>2</divisions><key><fifths>0</fifths></key><staves>2</staves><time><beats>3</beats><beat-type>4</beat-type></time></attributes>" : "";
            xml.Append($"<measure number=\"{i + 1}\">{attributes}{measures[i]}</measure>");
        }

        return PieceImporter.Import("score.musicxml", Encoding.UTF8.GetBytes(xml.Append("</part></score-partwise>").ToString())).Piece!;
    }

    /// <summary>A note: duration in divisions (2 to a quarter); <paramref name="extra"/> goes first (chord, grace), <paramref name="after"/> last (tie).</summary>
    public static string Note(string step, int octave, int duration, int staff = 1, string extra = "", string after = "") =>
        $"<note>{extra}<pitch><step>{step}</step><octave>{octave}</octave></pitch>{(extra.Contains("grace", StringComparison.Ordinal) ? "" : $"<duration>{duration}</duration>")}{after}<staff>{staff}</staff></note>";

    public static string Rest(int duration, int staff) => $"<note><rest/><duration>{duration}</duration><staff>{staff}</staff></note>";

    public static string Backup(int duration) => $"<backup><duration>{duration}</duration></backup>";

    /// <summary>The example piece of both apps, Für Elise.</summary>
    public static ImportedPiece FurElise() =>
        PieceImporter.Import("FurElise.musicxml", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"))).Piece!;

    public static AssistantPiece Open(ImportedPiece piece, IReadOnlyList<CheckFlag>? check = null, Words words = default) =>
        new(piece, piece.Notes, check ?? [], words);

    public static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    /// <summary>What a tool answers, as the facade writes it.</summary>
    public static JsonElement Answer(AssistantPiece piece, string tool, string parameters)
    {
        ToolResult result = AssistantTools.Call(piece, tool, Json(parameters));
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            AssistantTools.WriteResult(json, piece, tool, result);
            json.WriteEndObject();
        }

        return Json(Encoding.UTF8.GetString(buffer.ToArray()));
    }
}