using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>Where a place lies in its draft's score: its ticks and the draft's own bar numbers, both ends included.</summary>
/// <param name="Key">The key its result has at its start, as its source states it; null when its source states none.</param>
internal sealed record DraftPlaceSpan(string Place, TickSpan Span, int FirstBar, int LastBar, KeySignature? Key);

/// <summary>
/// A draft as one score (decision record 0034, section 2): the places' results in order, each in its own meter, key and
/// tempo, nothing fitted to the place before or after it, the bars numbered from 1 through them. Each place starts with
/// its own tempo: the one its result has at its start, else the tempo a piece with none plays at (marked
/// <see cref="TempoSource.Default"/>). It starts with the key its result has at its start; a place with none states no key
/// (<see cref="DraftPlaceSpan.Key"/> null), and only where an earlier place's key would run on into it does the score
/// cancel that key, with no sharps or flats and no mode, so no key is shown as one its source states.
/// </summary>
internal sealed record DraftMusic(SourceMusic Music, IReadOnlyList<DraftPlaceSpan> Places)
{
    public static DraftMusic Of(Draft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<Note> notes = [];
        List<AssemblyBar> bars = [];
        List<PlaybackTempo> tempos = [];
        List<KeySignature> keys = [];
        List<(string Place, TickSpan Span, int FirstIndex, KeySignature? Key)> spans = [];
        long offset = 0;
        foreach (DraftPlace place in draft.Places)
        {
            SourceMusic result = draft.ResultOf(place.Id).Music;
            Tick start = new(offset);
            notes.AddRange(result.Notes.Select(note => note with { Span = new TickSpan(note.Span.Onset.Offset(offset), note.Span.Duration) }));
            KeySignature? own = result.Keys.FirstOrDefault(static key => key.At == Tick.Zero);
            spans.Add((place.Id, new TickSpan(start, Math.Max(1, result.End.Value)), bars.Count, own));
            bars.AddRange(AssemblyBars.Moved(result.Bars, offset));
            if (!result.Tempos.Any(static tempo => tempo.At == Tick.Zero))
            {
                tempos.Add(new PlaybackTempo(start, PlaybackTempo.PieceDefaultMicrosecondsPerQuarter, TempoSource.Default));
            }

            tempos.AddRange(result.Tempos.Select(tempo => tempo with { At = tempo.At.Offset(offset) }));
            if (own is null && keys.Count > 0)
            {
                keys.Add(new KeySignature(start, 0, null));
            }

            keys.AddRange(result.Keys.Select(key => key with { At = key.At.Offset(offset) }));
            offset += Math.Max(1, result.End.Value);
        }

        var music = new SourceMusic(notes, bars, tempos, keys);
        return new DraftMusic(music, [.. spans.Select((span, index) => new DraftPlaceSpan(
            span.Place,
            span.Span,
            music.NumberOf(span.FirstIndex),
            music.NumberOf((index + 1 < spans.Count ? spans[index + 1].FirstIndex : bars.Count) - 1),
            span.Key))]);
    }
}