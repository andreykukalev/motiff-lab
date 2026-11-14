using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The tempo of the open piece (version 0.5, «Темп живого исполнения», contract 12, a call added): <see cref="TempoProfile"/>
/// for the whole piece and for each piece of its pieces layer, computed and never saved.
/// </summary>
internal static class TempoRequest
{
    /// <summary>
    /// → {"inferred", "usual", "low", "high" (tenths of a quarter note a minute), "text", "bars": [{"number", "tempo"}],
    /// "changes": [{"firstBar", "lastBar", "span", "kind" (slower / faster), "tempo", "text"}], "facts": [{"id", "text"}],
    /// "pieces": [{"id", "title", the same fields}]}; a piece without bars answers "bars": [] and nulls.
    /// </summary>
    public static void Write(Utf8JsonWriter json, Project project, ImportedPiece piece, Words words)
    {
        IReadOnlyList<PlaybackTempo> tempos = PlaybackScore.FromPiece(piece).Tempos;
        bool inferred = piece.Bars is not null;
        Profile(json, TempoProfile.Of(piece.Meter, tempos, piece.End, inferred), words);
        json.WriteStartArray("pieces");
        foreach (LayerPiece part in PieceLayer.Read(project, piece.Meter))
        {
            json.WriteStartObject();
            json.WriteString("id", part.BookmarkId);
            json.WriteString("title", part.Title);
            Profile(json, TempoProfile.Of(piece.Meter, tempos, piece.End, inferred, part.Span), words);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void Profile(Utf8JsonWriter json, TempoProfile? profile, Words words)
    {
        if (profile is null)
        {
            json.WriteBoolean("inferred", false);
            foreach (string name in new[] { "usual", "low", "high", "text" })
            {
                json.WriteNull(name);
            }

            foreach (string name in new[] { "bars", "changes", "facts" })
            {
                json.WriteStartArray(name);
                json.WriteEndArray();
            }

            return;
        }

        IReadOnlyList<MotifFact> facts = profile.Facts(words);
        json.WriteBoolean("inferred", profile.Inferred);
        json.WriteNumber("usual", profile.Usual);
        json.WriteNumber("low", profile.Low);
        json.WriteNumber("high", profile.High);
        json.WriteString("text", facts[0].Text);
        json.WriteStartArray("bars");
        foreach (BarTempo bar in profile.Bars)
        {
            json.WriteStartObject();
            json.WriteNumber("number", bar.Number);
            json.WriteNumber("tempo", bar.Tenths);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("changes");
        for (int i = 0; i < profile.Changes.Count; i++)
        {
            TempoChange change = profile.Changes[i];
            json.WriteStartObject();
            json.WriteNumber("firstBar", change.FirstBar);
            json.WriteNumber("lastBar", change.LastBar);
            Answer.Span(json, "span", change.Span);
            json.WriteString("kind", change.Slower ? "slower" : "faster");
            json.WriteNumber("tempo", change.Tenths);
            json.WriteString("text", facts[i + 1].Text);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("facts");
        foreach (MotifFact fact in facts)
        {
            json.WriteStartObject();
            json.WriteString("id", fact.Id);
            json.WriteString("text", fact.Text);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}