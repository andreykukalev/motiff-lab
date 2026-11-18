using System.Text.Json;

using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// How a recording's bars are worked out (version 0.5, «Темп живого исполнения», contract 12, fields added): a
/// transcription's "tempo": "live" finds the beat afresh in each section (<see cref="RecordingBars.FindLive"/>), with "starts"
/// (milliseconds of the recording where pieces begin) and a "hint" from a score of the piece ({"signature": "3/4",
/// "quarterNotesPerMinute"}); without "tempo" the beat is found once, as before.
/// </summary>
internal static class LiveTempoRequest
{
    /// <summary>The bars of the notes heard, found as <paramref name="request"/> asks, and the piece on them, marked transcribed.</summary>
    /// <param name="imported">The engine's notes, imported from its MIDI.</param>
    public static (BarGrid? Bars, ImportedPiece Piece) Heard(ImportedPiece imported, Request request)
    {
        BarGrid? bars = RecordingBars.Find(imported.Midi!, search: Of(request));
        return (bars, Transcribed.Mark(bars is { } grid ? grid.Apply(imported) : imported));
    }

    public static GridSearch? Of(Request request)
    {
        string? tempo = request.Text("tempo");
        if (tempo is null or "once")
        {
            return null;
        }

        if (tempo != "live")
        {
            throw FacadeException.BadRequest("\"tempo\" is \"live\" or \"once\".");
        }

        long[] starts = [.. request.Elements("starts").Select(start => start.ValueKind == JsonValueKind.Number && start.TryGetInt64(out long ms) && ms >= 0
            ? ms * 1000
            : throw FacadeException.BadRequest("\"starts\" are milliseconds of the recording, from 0."))];
        Request hint = request.Object("hint");
        return new GridSearch
        {
            Starts = starts,
            Hint = request.Has("hint") ? new GridHint(SignatureOf(hint.Text("signature")), hint.Int("quarterNotesPerMinute")) : null,
        };
    }

    /// <summary>One of the bars a grid offers, written as "3/4"; null when none is sent.</summary>
    private static TimeSignature? SignatureOf(string? text) => text is null
        ? null
        : BarGrid.Signatures.Where(offered => offered.ToString() == text).Select(offered => (TimeSignature?)offered).FirstOrDefault()
            ?? throw FacadeException.BadRequest($"\"signature\" is one of {string.Join(", ", BarGrid.Signatures)}.");
}