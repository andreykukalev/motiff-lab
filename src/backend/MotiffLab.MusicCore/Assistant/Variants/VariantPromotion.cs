using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant.Variants;

/// <summary>
/// «Взять в работу» (the AI companion study, sections 2.4 and 4.6; decision record 0024): a variant becomes the working
/// notes of its phrase by a step of the assembly journal, a carry whose source is the variant's music, put in place of the
/// phrase. So «Вернуть что было» takes it back like any carry, a variant that changes the number of bars splices them
/// (<see cref="AssemblyBars.Splice"/>), and the notes before the assembly are kept («Ноты до сборки»). The original is
/// never changed. Pure: the same work, variant music and time give the same step.
/// </summary>
internal static class VariantPromotion
{
    /// <summary>What a variant's source id starts with in the journal: "variant:o_0003".</summary>
    public const string SourcePrefix = "variant:";

    /// <summary>The source id of a variant in the journal.</summary>
    public static string SourceIdOf(string variantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variantId);
        return SourcePrefix + variantId;
    }

    /// <summary>The variant a source id names, or null when it is a version's.</summary>
    public static string? VariantIdOf(string sourceId)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        return sourceId.StartsWith(SourcePrefix, StringComparison.Ordinal) && sourceId.Length > SourcePrefix.Length ? sourceId[SourcePrefix.Length..] : null;
    }

    /// <summary>
    /// The variant's music in the phrase's own time: its notes, with the notes of the passage the variant did not select
    /// (the other hand, a layer left out) as they are, in the phrase's bars; bars are added for notes the steps start after
    /// the phrase and for <see cref="PhraseVariant.BarChange"/>, or taken away for a negative one with the notes that start
    /// in them. Null when the variant cannot be made.
    /// </summary>
    public static SourceMusic? Music(VariantReplay replay, IReadOnlyList<Note> notes, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        if (replay.Chain is not { } chain)
        {
            return null;
        }

        TickSpan span = chain.Context.Span;
        IReadOnlyList<Note> made = chain.Heard;
        var selected = chain.Original.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        List<AssemblyBar> bars = [.. AssemblyBars.Cut(AssemblyBars.FromMeter(meter, span.End), span.Onset, span.End)];
        TimeSignature last = bars.Count > 0 ? bars[^1].Signature : TimeSignature.CommonTime;
        int change = replay.Variant.BarChange;
        if (change < 0)
        {
            bars.RemoveRange(Math.Max(1, bars.Count + change), Math.Min(-change, Math.Max(0, bars.Count - 1)));
        }
        else
        {
            long latest = made.Count == 0 ? 0 : made.Max(static note => note.Span.Onset.Value);
            for (int added = 0; added < change || latest >= End(bars, span).Value; added++)
            {
                bars.Add(new AssemblyBar(new TickSpan(End(bars, span), last.BarTicks), last));
            }
        }

        Tick end = End(bars, span);
        Note[] music = [.. notes.Where(note => note.Span.Onset >= span.Onset && note.Span.Onset < span.End && !selected.Contains(note.Id)), .. made];
        IReadOnlyList<Note> kept = [.. music.Where(note => note.Span.Onset < end).OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)];
        return new SourceMusic(kept, bars, [], []);
    }

    /// <summary>The span of a variant's music: from the phrase's start to the end of its last bar.</summary>
    public static TickSpan SpanOf(SourceMusic music)
    {
        ArgumentNullException.ThrowIfNull(music);
        return music.Bars.Count == 0 ? new TickSpan(Tick.Zero, 1) : TickSpan.FromBounds(music.Bars[0].Span.Onset, music.Bars[^1].Span.End);
    }

    /// <summary>
    /// Why the variant cannot be taken into the working notes now, in the player's language, or null when it can: it cannot
    /// be made, the notes under it changed (recompute it first), or the journal holds carries not yet made notes, which
    /// taking it would make notes with it.
    /// </summary>
    public static string? Refusal(VariantReplay replay, PieceAssembly kept, AssemblyWork work, Words words)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(work);
        return replay.Refusal
            ?? (replay.Result is null ? words.T("The variant changes nothing.", "Вариант ничего не меняет.") : null)
            ?? (replay.BaseChanged ? words.T("The notes under this variant changed: recompute it first.", "Ноты под этим вариантом изменились: сначала пересчитайте его.") : null)
            ?? (Pending(kept, work) ? words.T(
                "The assembly has carries not yet made notes: make them notes or return them first.",
                "В сборке есть переносы, которые ещё не сделаны нотами: сначала сделайте их нотами или верните.") : null);
    }

    /// <summary>Whether the journal builds notes other than the ones the piece plays: carries made and not yet applied.</summary>
    public static bool Pending(PieceAssembly kept, AssemblyWork work)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(work);
        if (kept.Applied is not { } applied)
        {
            return work.Changed;
        }

        IReadOnlyList<Note> built = work.Result.Music.Notes;
        return built.Count != applied.Notes.Count
            || built.Zip(applied.Notes).Any(static pair => pair.First.Id != pair.Second.Id || pair.First.Pitch != pair.Second.Pitch || pair.First.Span != pair.Second.Span);
    }

    /// <summary>
    /// The work with the variant carried in place of its phrase: the variant's music added to the sources under its name, and
    /// a carry from it to the phrase's place in the notes as the journal builds them, held by the notes there.
    /// </summary>
    public static (AssemblyWork Work, CarryStep Step) Promote(AssemblyWork work, PhraseVariant variant, SourceMusic music, string name, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(music);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string id = SourceIdOf(variant.Id);
        Dictionary<string, SourceMusic> sources = new(work.Sources, StringComparer.Ordinal) { [id] = music };
        Dictionary<string, string> names = new(work.Names, StringComparer.Ordinal) { [id] = name };
        AssemblyWork over = work.Over(work.Base, sources, names);
        CarryStep step = over.Carry(id, SpanOf(music), AssemblyPlace.Over(over.Result.Music.Notes, variant.Phrase.Span), 0, at);
        return (over.With(step), step);
    }

    /// <summary>The applied carry that took the variant into the notes, the last one, or null when it is not in them.</summary>
    public static CarryStep? StepOf(AssemblyWork work, string variantId)
    {
        ArgumentNullException.ThrowIfNull(work);
        string id = SourceIdOf(variantId);
        return work.Steps.OfType<CarryStep>().LastOrDefault(step => step.SourceId == id && work.Result.OutcomeOf(step.Number) is { Applied: true });
    }

    /// <summary>The variants the journal carries, by id: those whose music the work needs as a source.</summary>
    public static IReadOnlyList<string> Carried(IEnumerable<AssemblyStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return [.. steps.OfType<CarryStep>().Select(step => VariantIdOf(step.SourceId)).OfType<string>().Distinct(StringComparer.Ordinal)];
    }

    private static Tick End(List<AssemblyBar> bars, TickSpan span) => bars.Count == 0 ? span.End : bars[^1].Span.End;

    /// <summary>
    /// The journal's sources for the variants it carries (<c>variant:o_0003</c>), from the ideas that keep them: each made
    /// again over the notes it was built on (<see cref="VariantRecord.BuiltOn"/>: the working notes, else the notes before
    /// the assembly), so the carry is rebuilt after a reload as it was made. A carried variant that is no longer kept or
    /// cannot be made again has no source, as a removed version.
    /// </summary>
    /// <param name="notes">The working notes as they are.</param>
    public static IReadOnlyList<(string SourceId, SourceMusic Music)> Sources(Project project, IReadOnlyList<Note> notes, MeterMap meter, Words words)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        List<(IReadOnlyList<Note> Notes, MeterMap Meter)> candidates = [(notes, meter)];
        if (project.Assembly.Before is { } before)
        {
            candidates.Add((before.Notes, before.Meter));
        }

        List<(string, SourceMusic)> sources = [];
        foreach (string id in Carried(project.Assembly.Steps))
        {
            if (project.Observations.FirstOrDefault(observation => observation.Id == id) is not { } observation
                || VariantRecord.Read(observation, notes) is not { } variant)
            {
                continue;
            }

            (IReadOnlyList<Note> on, MeterMap bars) = VariantRecord.BuiltOn(variant, candidates);
            if (Music(PhraseVariants.Replay(variant, on, bars, words), on, bars) is { } music)
            {
                sources.Add((SourceIdOf(id), music));
            }
        }

        return sources;
    }
}