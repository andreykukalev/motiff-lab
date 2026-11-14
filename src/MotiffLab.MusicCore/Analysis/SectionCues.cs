using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The facts <see cref="SectionFinder"/> reads a boundary from, each with its weight and its words in both languages. A
/// cue says only what the file shows at the bar: the bar line, the heading as written, the new key, time signature or
/// tempo, the bars of rest or the seconds of silence before it.
/// </summary>
internal static partial class SectionCues
{
    private const double FinalWeight = 0.45;
    private const double DoubleWeight = 0.3;
    private const double MovementWeight = 0.5;
    private const double TempoWordWeight = 0.2;
    private const double RehearsalWeight = 0.15;
    private const double KeyWeight = 0.3;
    private const double MeterWeight = 0.2;
    private const double TempoWeight = 0.2;
    private const double RestWeight = 0.2;
    private const double LongRestWeight = 0.3;
    private const double SilenceWeight = 0.45;
    private const double LongSilenceWeight = 0.6;
    private const double HeardWeight = 0.15;

    /// <summary>The bars a new tempo must hold for to count: a ritardando's steps do not.</summary>
    private const int TempoHoldsBars = 2;

    /// <summary>Headings that name a movement or a section, as scores write them (Italian, French, German, English, Russian).</summary>
    private static readonly string[] s_movements =
    [
        "menuet", "menuetto", "minuetto", "minuet", "trio", "scherzo", "rondo", "finale", "coda", "var.", "variation", "variazione",
        "thema", "tema", "theme", "aria", "prelude", "prélude", "preludio", "fugue", "fuga", "allemande", "courante", "sarabande",
        "gigue", "gavotte", "bourrée", "bourree", "polonaise", "mazurka", "valse", "waltz", "walzer", "intermezzo", "movement",
        "mvt", "satz", "часть", "вариация", "тема", "кода",
    ];

    /// <summary>Tempo words, which a new movement or section often starts with; any other heading set in bold counts as much.</summary>
    private static readonly string[] s_tempoWords =
    [
        "allegro", "allegretto", "andante", "andantino", "adagio", "adagietto", "largo", "larghetto", "lento", "grave", "presto",
        "prestissimo", "vivace", "vivo", "moderato", "maestoso", "tempo i", "tempo primo", "tempo di",
    ];

    /// <summary>
    /// The cues the notation states: bar lines, headings, rehearsal marks, keys, time signatures and tempos. A recording
    /// states none: its bars and tempo are fitted to the playing (<c>BarGrid</c>), so their changes say nothing of its form.
    /// </summary>
    public static void Notation(SectionEvidence evidence, Action<Tick, SectionCue> add)
    {
        if (evidence.Heard)
        {
            return;
        }

        foreach (MusicXmlBarline barline in evidence.Barlines.Where(barline => !barline.IsRepeat && (barline.IsFinal || barline.IsDouble)).DistinctBy(barline => barline.At))
        {
            add(barline.At, evidence.Barlines.Any(other => other.At == barline.At && other.IsFinal)
                ? new SectionCue(SectionCueKind.FinalBarline, FinalWeight, "final bar line", "заключительная черта")
                : new SectionCue(SectionCueKind.DoubleBarline, DoubleWeight, "double bar line", "двойная черта"));
        }

        Texts(evidence, add);
        Keys(evidence, add);
        IReadOnlyList<MeterChange> meters = evidence.Meter.Changes;
        for (int index = 1; index < meters.Count; index++)
        {
            if (meters[index].Signature != meters[index - 1].Signature && meters[index].Position.Value > evidence.Meter.PickupDuration)
            {
                string signature = meters[index].Signature.ToString();
                add(meters[index].Position, new SectionCue(SectionCueKind.MeterChange, MeterWeight, $"time signature changes to {signature}", $"смена размера на {signature}"));
            }
        }

        Tempos(evidence, add);
    }

    /// <summary>Whole bars without a sounding note between the end of one note and the onset of the next; null for none.</summary>
    public static SectionCue? Rest(MeterMap meter, Tick silent, Tick next)
    {
        int bars = 0;
        for (Bar bar = meter.BarAt(silent); bar.Span.End <= next; bar = meter.GetBar(bar.Number + 1))
        {
            bars += bar.Span.Onset >= silent ? 1 : 0;
        }

        return bars == 0 ? null : new SectionCue(
            SectionCueKind.Rest,
            bars == 1 ? RestWeight : LongRestWeight,
            $"{Words.English.Count(bars, "bar", "bars", "", "", "")} of rest before",
            $"перед этим {Words.Russian.Count(bars, "", "", "такт", "такта", "тактов")} паузы");
    }

    /// <summary>Seconds of silence in a recording before the boundary.</summary>
    public static SectionCue Silence(long microseconds, bool isLong)
    {
        long seconds = (microseconds + 500_000) / 1_000_000;
        return new SectionCue(SectionCueKind.Silence, isLong ? LongSilenceWeight : SilenceWeight, $"{seconds} s of silence before", $"перед этим {seconds} с тишины") { Microseconds = microseconds };
    }

    /// <summary>
    /// A silence that alone is not a boundary between pieces (Andrei, 2026-11-12: a pause without a change of key or tempo
    /// is not one), proposed only to make up the number of pieces the player gave.
    /// </summary>
    public static SectionCue SameMusic() => new(
        SectionCueKind.SameMusic,
        0,
        "only a pause: the key and the tempo stay the same",
        "только пауза: тональность и темп те же");

    /// <summary>A boundary the player set.</summary>
    public static SectionCue Added() => new(SectionCueKind.Added, 0, "added by you", "добавлено вами");

    /// <summary>A boundary of the pieces layer, as the player confirmed it.</summary>
    public static SectionCue Kept() => new(SectionCueKind.Kept, 0, "confirmed by you", "подтверждено вами");

    /// <summary>The keys the music around a silence fits best: a hearing, said as one.</summary>
    public static SectionCue HeardKey(MusicalKey before, MusicalKey after) => new(
        SectionCueKind.HeardKey,
        HeardWeight,
        $"sounds like {before.NameIn(Words.English)} before and {after.NameIn(Words.English)} after",
        $"по звучанию: до этого {before.NameIn(Words.Russian)}, после — {after.NameIn(Words.Russian)}");

    /// <summary>The tempos kept before and after a silence, as microseconds per quarter.</summary>
    public static SectionCue HeardTempo(int before, int after) => new(
        SectionCueKind.HeardTempo,
        HeardWeight,
        $"tempo about {Bpm(before)} before and about {Bpm(after)} after",
        $"темп: до этого около {Bpm(before)}, после — около {Bpm(after)}");

    public static SectionCue Opening(bool heard) => heard
        ? new SectionCue(SectionCueKind.Opening, 0, "the start of the recording", "начало записи")
        : new SectionCue(SectionCueKind.Opening, 0, "the start of the piece", "начало пьесы");

    /// <summary>The headings and rehearsal marks written at the start of a bar, each text once.</summary>
    private static void Texts(SectionEvidence evidence, Action<Tick, SectionCue> add)
    {
        foreach (MusicXmlText text in evidence.Texts.DistinctBy(text => (text.At, text.Text)))
        {
            Bar bar = evidence.Meter.BarAt(text.At);
            if (text.At.Value - bar.Span.Onset.Value >= bar.Signature.BeatTicks || Heading(text) is not { } cue)
            {
                continue;
            }

            add(text.At, cue);
        }
    }

    private static SectionCue? Heading(MusicXmlText text)
    {
        string written = text.Text.Trim();
        string lower = written.ToLowerInvariant();
        if (text.IsRehearsal)
        {
            return new SectionCue(SectionCueKind.Rehearsal, RehearsalWeight, $"rehearsal mark {written}", $"репетиционная буква {written}");
        }

        double weight = Numbered().IsMatch(written) || s_movements.Any(word => StartsWithWord(lower, word))
            ? MovementWeight
            : s_tempoWords.Any(word => StartsWithWord(lower, word)) || text.IsBold ? TempoWordWeight : 0;
        return weight == 0 ? null : new SectionCue(SectionCueKind.Heading, weight, $"heading “{written}”", $"надпись «{written}»", written);
    }

    private static bool StartsWithWord(string text, string word) =>
        text.StartsWith(word, StringComparison.Ordinal) && (text.Length == word.Length || !char.IsLetter(text[word.Length]));

    /// <summary>"II. Andante", "2. Menuet": a movement's number.</summary>
    [GeneratedRegex(@"^(?:[IVX]{1,4}|\d{1,2})\.\s", RegexOptions.CultureInvariant)]
    private static partial Regex Numbered();

    /// <summary>Each new key signature: its key when the mode is written, else its sharps or flats.</summary>
    private static void Keys(SectionEvidence evidence, Action<Tick, SectionCue> add)
    {
        KeySignature[] keys = [.. evidence.Keys.OrderBy(key => key.At)];
        for (int index = 1; index < keys.Length; index++)
        {
            KeySignature key = keys[index];
            if (key.Fifths == keys[index - 1].Fifths && key.Mode == keys[index - 1].Mode)
            {
                continue;
            }

            add(key.At, key.Mode is { } mode
                ? new SectionCue(SectionCueKind.KeyChange, KeyWeight, $"key changes to {new MusicalKey(key.Fifths, mode).NameIn(Words.English)}", $"смена тональности на {new MusicalKey(key.Fifths, mode).NameIn(Words.Russian)}")
                : new SectionCue(SectionCueKind.KeyChange, KeyWeight, $"new key signature: {Signs(key.Fifths, "no sharps or flats")}", $"новые ключевые знаки: {Signs(key.Fifths, "без знаков")}"));
        }
    }

    private static string Signs(int fifths, string none) => fifths switch
    {
        0 => none,
        > 0 => $"{fifths} ♯",
        _ => $"{-fifths} ♭",
    };

    /// <summary>Each new tempo a tenth or more from the one before that holds for <see cref="TempoHoldsBars"/> bars.</summary>
    private static void Tempos(SectionEvidence evidence, Action<Tick, SectionCue> add)
    {
        PlaybackTempo[] tempos = [.. evidence.Tempos.OrderBy(tempo => tempo.At)];
        for (int index = 1; index < tempos.Length; index++)
        {
            long before = tempos[index - 1].MicrosecondsPerQuarter, now = tempos[index].MicrosecondsPerQuarter;
            bool changes = before * 10 >= now * 11 || now * 10 >= before * 11;
            bool holds = index + 1 == tempos.Length
                || evidence.Meter.BarAt(tempos[index + 1].At).Number - evidence.Meter.BarAt(tempos[index].At).Number >= TempoHoldsBars;
            if (changes && holds)
            {
                int bpm = Bpm(tempos[index].MicrosecondsPerQuarter);
                add(tempos[index].At, new SectionCue(SectionCueKind.TempoChange, TempoWeight, $"tempo changes to quarter = {bpm}", $"смена темпа: четверть = {bpm}"));
            }
        }
    }

    private static int Bpm(int microsecondsPerQuarter) => (int)((60_000_000L + (microsecondsPerQuarter / 2)) / microsecondsPerQuarter);
}