using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The character facts as sentences in both languages (S1-A5): texture, tempo, density, register, velocity, articulation
/// and syncopation share, written from the measures of <c>MotifAnalyzer.Character.cs</c> and <c>MotifAnalyzer.Texture.cs</c>.
/// Nothing is measured here; the selection's notes are read only to name the places a measure points at.
/// </summary>
internal static partial class MotifAnalyzer
{
    private static string NotationLengthsBasis(Words w) => w.T(
        " A notation file gives written lengths, and staccato or tenuto marks are not read, so this reflects the written note values rather than how they are played.",
        " Нотный файл даёт записанные длительности, а знаки стаккато и тенуто не читаются, поэтому это отражает записанные длительности, а не то, как их играют.");

    private static MotifFact TextureFact(TextureMeasure measure, Words w)
    {
        if (measure.SingleLine)
        {
            return new MotifFact("character.texture", FactLens.Character, FactCertainty.Detected,
                w.T("Texture: one line with no accompaniment. The harmony is heard only in the line itself.",
                    "Фактура: одна линия без сопровождения. Гармония слышна только в самой линии."),
                w.T("Detected: no two notes sound at once.", "Обнаружено: никакие две ноты не звучат одновременно."))
            {
                Brief = w.T("One line with no accompaniment.", "Одна линия без сопровождения."),
                Places = [FactPlace.Of(w.T("the line", "линия"), measure.Upper)],
            };
        }

        bool byHands = measure.ByHands;
        Note[] upper = measure.Upper;
        Note[] lower = measure.Lower;
        Accompaniment kind = measure.Kind;
        string upperText = measure.InChords ? w.T("a melody in chords", "мелодия в аккордах") : w.T("a melody", "мелодия");
        string hand = byHands ? w.T(" in the left hand", " в левой руке") : "";
        string texture1 = kind switch
        {
            Accompaniment.BrokenChords => w.T($"{upperText} over broken chords{hand}", $"{upperText} над разложенными аккордами{hand}"),
            Accompaniment.BlockChords => w.T($"{upperText} over chords{hand}", $"{upperText} над аккордами{hand}"),
            Accompaniment.BassLine => w.T($"{upperText} over a bass line{hand}", $"{upperText} над басовой линией{hand}"),
            Accompaniment.LongNotes => w.T($"{upperText} over long bass notes{hand}", $"{upperText} над долгими нотами баса{hand}"),
            _ => w.T("a melody on top of its own chords, with no separate accompaniment", "мелодия поверх своих аккордов, без отдельного сопровождения"),
        };

        string interplay = measure.Interplay switch
        {
            Interplay.TakeTurns => w.T(" The two parts take turns: the accompaniment sounds in the melody's pauses.", " Голоса чередуются: сопровождение звучит в паузах мелодии."),
            Interplay.Together => w.T(" The two parts sound together, onset with onset.", " Голоса звучат вместе, атака к атаке."),
            _ => "",
        };

        string gives = kind switch
        {
            Accompaniment.BrokenChords => w.T(" The broken chords keep the harmony moving under the melody.", " Разложенные аккорды держат гармонию в движении под мелодией."),
            Accompaniment.BlockChords => w.T(" The chords mark the beats and the harmony at once.", " Аккорды отмечают сразу и доли, и гармонию."),
            Accompaniment.BassLine => w.T(" A second line answers the melody from below.", " Вторая линия отвечает мелодии снизу."),
            Accompaniment.LongNotes => w.T(" The long bass notes hold the harmony while the melody moves.", " Долгие ноты баса держат гармонию, пока движется мелодия."),
            _ => "",
        };

        string basis = byHands
            ? w.T(
                "Inferred from the hands: the right hand's notes are the melody, the left hand's the accompaniment, read as chords when half its onsets strike two notes or more, as broken chords when its single notes mostly leap, as taking turns when at most 4 in 10 of its onsets fall with the melody's, as long notes when they last a half note or more, otherwise as a bass line.",
                "Предположено по рукам: ноты правой руки — мелодия, левой — сопровождение; оно читается как аккорды, если половина его атак берёт две ноты и больше, как разложенные аккорды, если его одиночные ноты в основном идут скачками, как чередование, если с атаками мелодии совпадают не больше 4 из 10 его атак, как долгие ноты, если они длятся половинную и дольше, иначе как басовая линия.")
            : w.T(
                "Inferred: the melody is the top line, the accompaniment the notes below it, read as chords when half its onsets strike two notes or more, as broken chords when its single notes mostly leap, as taking turns when at most 4 in 10 of its onsets fall with the melody's, as long notes when they last a half note or more, otherwise as a bass line.",
                "Предположено: мелодия — верхняя линия, сопровождение — ноты под ней; оно читается как аккорды, если половина его атак берёт две ноты и больше, как разложенные аккорды, если его одиночные ноты в основном идут скачками, как чередование, если с атаками мелодии совпадают не больше 4 из 10 его атак, как долгие ноты, если они длятся половинную и дольше, иначе как басовая линия.");
        List<FactPlace> places = [FactPlace.Of(w.T("the melody", "мелодия"), upper)];
        if (lower.Length > 0)
        {
            places.Add(FactPlace.Of(w.T("the accompaniment", "сопровождение"), lower));
        }

        string sentence = char.ToUpperInvariant(texture1[0]) + texture1[1..];
        return new MotifFact("character.texture", FactLens.Character, FactCertainty.Inferred,
            w.T($"Texture: {texture1}.{interplay}{gives}", $"Фактура: {texture1}.{interplay}{gives}"), basis)
        {
            Brief = $"{sentence}.{gives}",
            Places = places,
        };
    }

    private static MotifFact TempoFact(TempoMeasure measure, CharacterContext context, Words w)
    {
        PlaybackTempo start = measure.Start;
        IReadOnlyList<TempoChange> changes = measure.Changes;
        if (context.TempoEstimated && changes.Count > 0)
        {
            // A tempo that follows the playing changes on every beat: its range and typical value say more than a list.
            int[] beats = [.. new[] { start.MicrosecondsPerQuarter }.Concat(changes.Select(change => change.Tempo.MicrosecondsPerQuarter)).Order()];
            string typical = Bpm(beats[beats.Length / 2], w), slowest = Bpm(beats[^1], w), fastest = Bpm(beats[0], w);
            return new MotifFact(
                "character.tempo",
                FactLens.Character,
                FactCertainty.Inferred,
                w.T(
                    $"Tempo: follows the playing, a quarter at about {typical} BPM, from {slowest} to {fastest} BPM.",
                    $"Темп: следует за игрой, четверть около {typical} BPM, от {slowest} до {fastest} BPM."),
                w.T(
                    "Worked out from the notes heard in the recording, with the bars: a beat that bends with the playing, in quarter notes per minute, rounded to a tenth for display; the typical value is the median beat.",
                    "Выведено из нот, услышанных в записи, вместе с тактами: доля, которая гнётся вместе с игрой, в четвертях в минуту, с округлением до десятых для показа; типичное значение — медианная доля."));
        }

        // Each change is a place: from where it takes effect to the next change or the end of the selection.
        FactPlace[] places = [.. changes.Select(change => new FactPlace(TempoAt(change, w), change.Span, []))];
        bool assumed = context.TempoAssumed && measure.AtFirst;
        string startBpm = Bpm(start.MicrosecondsPerQuarter, w);
        string text;
        string? brief = null;
        if (changes.Count == 0)
        {
            text = w.T($"Tempo: {startBpm} BPM throughout the motif.", $"Темп: {startBpm} BPM на всём мотиве.");
        }
        else
        {
            int[] values = [start.MicrosecondsPerQuarter, .. changes.Select(change => change.Tempo.MicrosecondsPerQuarter)];
            string list = w.Listed([.. changes.Select(change => TempoAt(change, w))], MaxListed, "; ", "; ");
            text = w.IsRussian
                ? $"Темп: начинается с {startBpm} BPM и меняется {w.Times(changes.Count)}: {list}. Он меняется от {Bpm(values.Max(), w)} до {Bpm(values.Min(), w)} BPM."
                : $"Tempo: starts at {startBpm} BPM and changes {w.Times(changes.Count)}: {list}. It ranges from {Bpm(values.Max(), w)} to {Bpm(values.Min(), w)} BPM.";
            brief = w.IsRussian
                ? $"Темп меняется {w.Times(changes.Count)}, от {Bpm(values.Max(), w)} до {Bpm(values.Min(), w)} BPM."
                : $"The tempo changes {w.Times(changes.Count)}, from {Bpm(values.Max(), w)} to {Bpm(values.Min(), w)} BPM.";
        }

        string basis = start.Source == TempoSource.Player
            ? w.T("The tempo you set for the piece, in quarter notes per minute; tempos the notes state later on stay as stated.", "Темп, который вы задали для пьесы, в четвертях в минуту; темпы, указанные в нотах дальше, остаются как указаны.")
            : w.T("Read from the file's tempo map, in quarter notes per minute, rounded to a tenth for display.", "Прочитано по темповой карте файла, в четвертях в минуту, с округлением до десятых для показа.");

        MotifFact fact = context.TempoEstimated && measure.AtFirst
            ? new MotifFact("character.tempo", FactLens.Character, FactCertainty.Inferred, text, w.T(
                "Worked out from the notes heard in the recording, with the bars: the beat their onsets fit best, in quarter notes per minute, rounded to a tenth for display.",
                "Выведено из нот, услышанных в записи, вместе с тактами: доля, к которой лучше всего подходят их атаки, в четвертях в минуту, с округлением до десятых для показа."))
            : assumed
            ? new MotifFact("character.tempo", FactLens.Character, FactCertainty.Inferred, text, w.T(
                $"The file states no tempo at its start, so the default of {startBpm} BPM is assumed. {basis}",
                $"В начале файла темп не указан, поэтому взят темп по умолчанию, {startBpm} BPM. {basis}"))
            : new MotifFact("character.tempo", FactLens.Character, FactCertainty.Detected, text, basis);
        return fact with { Places = places, Brief = brief };
    }

    /// <summary>"92 BPM at bar 3 beat 1"; "92 BPM, такт 3 доля 1".</summary>
    private static string TempoAt(TempoChange change, Words w) =>
        $"{Bpm(change.Tempo.MicrosecondsPerQuarter, w)} BPM{w.T(" at ", ", ")}{Place(change.At, w)}";

    private static MotifFact DensityFact(DensityMeasure measure, Note[] notes, Words w)
    {
        int noteCount = measure.Notes;
        int onsets = measure.Onsets;
        int beats = measure.Beats;
        int busiest = measure.Busiest;
        string text = w.IsRussian
            ? $"Плотность: {Ratio(onsets, beats, w)} атаки на долю ({w.Count(onsets, "", "", "атака", "атаки", "атак")} на {w.Count(beats, "", "", "долю", "доли", "долей")}); в самой плотной доле {w.Count(busiest, "", "", "атака", "атаки", "атак")}."
            : $"Density: {Ratio(onsets, beats, w)} onsets per beat ({Words.EnglishCount(onsets, "onset", "onsets")} over {Words.EnglishCount(beats, "beat", "beats")}); the busiest beat holds {busiest}.";
        if (noteCount > onsets)
        {
            text += w.T($" Counting every note of a chord, {Ratio(noteCount, beats, w)} notes per beat.", $" Если считать каждую ноту аккорда, {Ratio(noteCount, beats, w)} ноты на долю.");
        }

        return new MotifFact(
            "character.density",
            FactLens.Character,
            FactCertainty.Detected,
            text,
            w.T(
                "Counted over the beats of the selection, from the bar lines; notes that start together count as one onset.",
                "Посчитано по долям выделения, от тактовых черт; ноты, начинающиеся вместе, считаются одной атакой."))
        {
            Brief = w.IsRussian
                ? $"Плотность: {Ratio(onsets, beats, w)} атаки на долю."
                : $"Density: {Ratio(onsets, beats, w)} onsets per beat.",

            // Every beat that holds the most onsets, with the notes that start in it.
            Places = [.. measure.BusiestBeats.Select(beat => SlotPlace($"{w.T("busiest beat", "самая плотная доля")}, {Place(beat.At, w)}", beat.Span, notes))],
        };
    }

    private static MotifFact RegisterFact(RegisterMeasure measure, Note[] notes, MusicalKey key, Words w)
    {
        int low = measure.Low;
        int high = measure.High;
        int median = measure.Median;
        int above = measure.Above;
        int count = measure.Count;

        string where = w.IsRussian
            ? above == count
                ? "все ноты на до первой октавы или выше"
                : above == 0
                    ? "все ноты ниже до первой октавы"
                    : $"{above} {w.OutOf(count, "", "", "ноты", "нот")} на до первой октавы или выше"
            : above == count
                ? "every note is at or above middle C"
                : above == 0
                    ? "every note is below middle C"
                    : $"{above} of {Words.EnglishCount(count, "note", "notes")} are at or above middle C";
        string text = w.IsRussian
            ? low == high
                ? $"Регистр: одна высота, {key.Spell(low)}; {where}."
                : $"Регистр: от {key.Spell(low)} до {key.Spell(high)}, диапазон {w.Count(high - low, "", "", "полутон", "полутона", "полутонов")}; {where}, средняя высота {key.Spell(median)}."
            : low == high
                ? $"Register: one pitch, {key.Spell(low)}; {where}."
                : $"Register: {key.Spell(low)} to {key.Spell(high)}, a range of {Words.EnglishCount(high - low, "semitone", "semitones")}; {where}, and the middle pitch is {key.Spell(median)}.";
        return new MotifFact(
            "character.register",
            FactLens.Character,
            FactCertainty.Detected,
            text,
            w.T(
                $"Measured over every note of the motif; middle C is C4. The middle pitch is the median, the lower of the two middle ones for an even count. Spelled for {key.Name}.",
                $"Измерено по всем нотам мотива; до первой октавы — это C4. Средняя высота — медиана, при чётном числе нот нижняя из двух средних. Ноты названы для тональности {key.NameIn(w)}."))
        {
            Brief = low == high
                ? null
                : w.IsRussian
                    ? $"Регистр: от {key.Spell(low)} до {key.Spell(high)}; {where}."
                    : $"Register: {key.Spell(low)} to {key.Spell(high)}; {where}.",
            Places = low == high ? ExtremePlaces(notes, key, w) : [.. ExtremePlaces(notes, key, w), FactPlace.Of($"{w.T("middle pitch", "средняя высота")}, {key.Spell(median)}", notes.Where(note => note.Pitch.MidiNumber == median))],
        };
    }

    private static MotifFact VelocityFact(VelocityMeasure measure, Note[] notes, bool fromNotation, Words w)
    {
        int low = measure.Low;
        int high = measure.High;
        string text = low == high
            ? w.T($"Velocity: every note has {low}, on the MIDI scale of 1 to 127.", $"Громкость (velocity): у всех нот {low}, по шкале MIDI от 1 до 127.")
            : w.T(
                $"Velocity: {low} to {high}, median {measure.Median}, on the MIDI scale of 1 to 127.",
                $"Громкость (velocity): от {low} до {high}, медиана {measure.Median}, по шкале MIDI от 1 до 127.");
        string basis = fromNotation
            ? w.T(
                $"Read from the notes. A notation file holds no played loudness: velocities come from its dynamics marks, or the default of {MusicXmlImport.DefaultVelocity} where it has none.",
                $"Прочитано по нотам. В нотном файле нет сыгранной громкости: она берётся из динамических оттенков, а где их нет, принимается {MusicXmlImport.DefaultVelocity}.")
            : w.T("Read from the notes' MIDI velocities.", "Прочитано по MIDI-громкости нот.");
        int median = measure.Median;
        FactPlace[] places = low == high
            ? []
            :
            [
                FactPlace.Of($"{w.T("lowest velocity", "самая тихая")}, {low}", notes.Where(note => note.Velocity == low)),
                FactPlace.Of($"{w.T("highest velocity", "самая громкая")}, {high}", notes.Where(note => note.Velocity == high)),
                FactPlace.Of($"{w.T("median velocity", "медиана")}, {median}", notes.Where(note => note.Velocity == median)),
            ];
        return new MotifFact("character.velocity", FactLens.Character, FactCertainty.Detected, text, basis)
        {
            Places = places,
            Brief = low == high
                ? null
                : w.T($"Velocity: {low} to {high}.", $"Громкость (velocity): от {low} до {high}."),
        };
    }

    private static MotifFact ArticulationFact(ArticulationMeasure measure, bool fromNotation, Words w)
    {
        IReadOnlyList<decimal> percents = measure.Percents;
        string measuredBasis = w.T(
            "Measured as each note's length over the time from its onset to the next onset of the motif, counting at most the whole gap; notes of the last onset have no next onset and are not counted.",
            "Измерено как длина каждой ноты по отношению ко времени от её атаки до следующей атаки мотива, не больше всего промежутка; у нот последней атаки следующей атаки нет, они не считаются.");
        string basis = fromNotation ? measuredBasis + NotationLengthsBasis(w) : measuredBasis;
        if (percents.Count == 0)
        {
            return new MotifFact("character.articulation", FactLens.Character, FactCertainty.Detected, w.T("Articulation: not measured, because every note starts together.", "Артикуляция: не измерена, потому что все ноты начинаются вместе."), basis);
        }

        int held = measure.Held;
        int detached = measure.Detached;
        string text = w.IsRussian
            ? $"Артикуляция: в среднем ноты звучат {Percent(percents.Sum(), percents.Count)}% времени до следующей атаки; "
                + $"{held} {w.OutOf(percents.Count, "", "", "ноты", "нот")} {Words.RussianForm(held, "звучит", "звучат", "звучат")} до следующей атаки, а {detached} — меньше половины этого времени."
            : $"Articulation: notes sound for {Percent(percents.Sum(), percents.Count)}% of the time to the next onset on average; "
                + $"{held} of {Words.EnglishCount(percents.Count, "note", "notes")} {(held == 1 ? "sounds" : "sound")} until the next onset, and {detached} {(detached == 1 ? "sounds" : "sound")} for less than half of it.";

        // The two sets the text counts: the notes held until the next onset, and the detached ones.
        var places = new List<FactPlace>();
        if (held > 0)
        {
            places.Add(FactPlace.Of(w.T("until the next onset", "до следующей атаки"), measure.Measured.Where((note, i) => percents[i] == 100m)));
        }

        if (detached > 0)
        {
            places.Add(FactPlace.Of(w.T("less than half", "меньше половины"), measure.Measured.Where((note, i) => percents[i] < DetachedPercent)));
        }

        return new MotifFact("character.articulation", FactLens.Character, FactCertainty.Detected, text, basis)
        {
            Places = places,
            Brief = w.IsRussian
                ? $"Артикуляция: в среднем ноты звучат {Percent(percents.Sum(), percents.Count)}% времени до следующей атаки."
                : $"Articulation: notes sound for {Percent(percents.Sum(), percents.Count)}% of the time to the next onset on average.",
        };
    }

    private static MotifFact SyncopationShareFact(List<FactPlace> places, int onsets, bool offGrid, Words w)
    {
        int syncopated = places.Count;
        string text = w.IsRussian
            ? $"Доля синкоп: {syncopated} {w.OutOf(onsets, "", "", "атаки", "атак")} ({Percent(syncopated * 100m, onsets)}%)."
            : $"Syncopation share: {syncopated} of {Words.EnglishCount(onsets, "onset", "onsets")} ({Percent(syncopated * 100m, onsets)}%).";
        string basis = w.T("Onsets counted with the same rule as the syncopation fact. ", "Атаки посчитаны по тому же правилу, что и в факте о синкопах. ") + SyncopationBasis(w);
        MotifFact fact = offGrid
            ? new MotifFact("character.syncopation", FactLens.Character, FactCertainty.Uncertain, text, basis + PerformanceBasis(w))
            : new MotifFact("character.syncopation", FactLens.Character, FactCertainty.Detected, text, basis);
        return fact with { Places = places };
    }

    /// <summary>Quarter notes per minute, to a tenth: "96", "92.3".</summary>
    private static string Bpm(int microsecondsPerQuarter, Words w) =>
        w.Decimal(Math.Round(60_000_000m / microsecondsPerQuarter, 1, MidpointRounding.AwayFromZero));

    /// <summary>A ratio to a tenth: "2.5".</summary>
    private static string Ratio(int count, int of, Words w) =>
        w.Decimal(Math.Round((decimal)count / of, 1, MidpointRounding.AwayFromZero));

    /// <summary>A whole percentage of a total over a count.</summary>
    private static string Percent(decimal total, int count) =>
        Math.Round(total / count, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
}