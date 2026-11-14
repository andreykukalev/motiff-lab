using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// «Для чтения: rit., ферматы, динамика» (an experiment, Settings · Experimental, version 0.5): notes heard in a recording,
/// written for reading, also carry the marks of the playing that matter, which music-core picks
/// (<see cref="PlayedMarks.Significant"/>): a dynamic where the playing gets markedly louder or softer, rit. and accel.
/// where it slows down or speeds up a lot, and fermatas over chords held. All of it is inferred, and the score's note says
/// so. With the experiment off the score for reading is as before.
/// </summary>
public partial class PlaybackLab
{
    public const string ReadingMarksSetting = "experimental.readingMarks";

    /// <summary>Whether the score for reading was last engraved with the marks.</summary>
    private bool _readingMarks;

    /// <summary>Notes heard, written for reading: with the marks that matter while the experiment is on.</summary>
    private GeneratedNotation ReadableNotation(IEnumerable<Note> notes, MeterMap meter, IEnumerable<KeyChange> keys, Tick end, string? title, PlaybackScore played, BarRange? bars = null, IReadOnlyList<NotationOssia>? ossias = null)
    {
        _readingMarks = SettingsStore.IsOn(ReadingMarksSetting);
        return _readingMarks
            ? NotationGenerator.Generate(notes, meter, keys, end, title, asHeard: true, playedTempos: played.Tempos, bars: bars, ossias: ossias, marks: PlayedMarks.Significant, tempos: played.Tempos)
            : NotationGenerator.Generate(notes, meter, keys, end, title, asHeard: true, bars: bars, ossias: ossias, tempos: played.Tempos);
    }

    /// <summary>The experiment switched while a score for reading is shown: it is engraved again.</summary>
    private void ReadingMarksChanged()
    {
        if (SettingsStore.IsOn(ReadingMarksSetting) == _readingMarks || !_heard || !_readable || _score is null || _notes is null)
        {
            return;
        }

        _generated = ScoreSource.FromNotes(Notation(_notes, _score.End));
        _focusCard = null;
        if (_variationScore is not null && _variation is { } variation)
        {
            _variationScore = VariationScore(variation);
        }
    }

    /// <summary>The note under the score for reading with the marks, when it carries any.</summary>
    private static bool HasReadingMarks(GeneratedNotation notation) => notation.DynamicCount + notation.TempoWordCount + notation.FermataCount > 0;
}