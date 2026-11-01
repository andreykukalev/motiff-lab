using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Projects;

/// <summary>A stretch of the piece and which of its notes count, as the player picked it.</summary>
public sealed record SavedPassage(TickSpan Span, SelectionFilter Filter)
{
    public static SavedPassage Of(MotifSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new(selection.Span, selection.Filter);
    }
}

/// <summary>
/// A variation as the player kept it: the passage its steps ran on and the chain of steps, saved
/// like <see cref="SavedVariation"/>. Steps are pure, so they are run again on open.
/// </summary>
/// <param name="Passage">Where the steps ran: the selection, or for an experiment its passage, which may be the motif's return.</param>
/// <param name="NoteIds">The passage's notes when it was kept, checked against the piece when the project opens.</param>
/// <param name="Steps">The accepted steps, oldest first; undone steps are not kept.</param>
/// <param name="Pending">The step that was being heard and not yet accepted, or null.</param>
/// <param name="Keep">The properties the steps ran with, in Keep table order.</param>
/// <param name="Key">
/// The key the steps counted scale steps in, or null when they ran without one. It is the reading
/// the steps ran with, kept so that the same notes come back on open; it is not shown as a fact.
/// </param>
public sealed record KeptVariation(
    SavedPassage Passage,
    IReadOnlyList<string> NoteIds,
    IReadOnlyList<SavedStep> Steps,
    SavedStep? Pending,
    IReadOnlyList<KeepProperty> Keep,
    MusicalKey? Key)
{
    /// <summary>The chain as it is heard, over the notes that <paramref name="filter"/> picked in the chain's span.</summary>
    public static KeptVariation Create(VariationChain chain, SelectionFilter filter)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(filter);
        if (chain.Result is null)
        {
            throw new ArgumentException("A kept variation needs at least one step.", nameof(chain));
        }

        return new(
            new SavedPassage(chain.Context.Span, filter),
            [.. chain.Original.Select(static note => note.Id)],
            [.. chain.Steps.Select(SavedStep.From)],
            chain.Pending is { } pending ? SavedStep.From(pending) : null,
            [.. chain.Context.Keep.Distinct().Order()],
            chain.Context.Key);
    }

    public bool Equals(KeptVariation? other) =>
        other is not null
        && Passage == other.Passage
        && Pending == other.Pending
        && Key == other.Key
        && NoteIds.SequenceEqual(other.NoteIds, StringComparer.Ordinal)
        && Steps.SequenceEqual(other.Steps)
        && Keep.SequenceEqual(other.Keep);

    public override int GetHashCode() => HashCode.Combine(Passage, Steps.Count, Key);
}

/// <summary>
/// What the player kept about a phrase (#78, docs/08-central-product-model.md, Recording
/// observations): a variation they made or tried, a note in their own words, or both. It slims
/// down the experiment record and process note of decision record 0009 to one record.
/// </summary>
/// <remarks>
/// The phrase is saved as its span and filter rather than its place in the list of phrases, so it
/// stays put when the player moves phrase boundaries or changes the phrase level. The note is the
/// player's own text: it stays on the device with the project and never goes into analytics. Times are
/// passed in, never read from a clock. Any number of ideas can be kept about one phrase (0018).
/// </remarks>
/// <param name="Id">Stable for the life of the project, for example "o_0001".</param>
/// <param name="Phrase">The phrase the player was working on.</param>
/// <param name="ExperimentId">The experiment the variation came from (<see cref="Experiments.ExperimentCatalogue.Ids"/>), or null for one made by hand.</param>
/// <param name="Alternative">The experiment's alternative, from 0; null exactly when <paramref name="ExperimentId"/> is.</param>
/// <param name="Variation">The variation kept, or null for a note alone. Its saved steps restore it, not the experiment's recipe.</param>
/// <param name="Name">The player's name for it, trimmed and never blank.</param>
/// <param name="Note">The player's note, trimmed, or null.</param>
public sealed record Observation(
    string Id,
    SavedPassage Phrase,
    string? ExperimentId,
    int? Alternative,
    KeptVariation? Variation,
    string Name,
    string? Note,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// The fact the idea was born from (0018, section 7): the id of the fact whose Try chip or experiment
    /// made the variation, as <see cref="Analysis.MotifFact.Id"/> names it, or null for an idea made by hand
    /// or a note alone. Fact ids are stable for the same passage, so the Insights stream can list the idea
    /// under its fact again.
    /// </summary>
    public string? FactId { get; init; }

    /// <summary>
    /// The phrase the idea belongs to among <paramref name="phrases"/>, the phrases of one level (0018,
    /// section 3; preparation item 4): the one its passage starts in; failing that, the one it overlaps
    /// most; null when it overlaps none. So after the level changes an idea shows under the phrase that
    /// contains its beginning, a merge keeps it and a split leaves it on the first part.
    /// </summary>
    public TickSpan? Home(IEnumerable<TickSpan> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        TickSpan? best = null;
        long bestOverlap = 0;
        foreach (TickSpan phrase in phrases)
        {
            if (phrase.Contains(Phrase.Span.Onset))
            {
                return phrase;
            }

            long overlap = Math.Min(phrase.End.Value, Phrase.Span.End.Value) - Math.Max(phrase.Onset.Value, Phrase.Span.Onset.Value);
            if (overlap > bestOverlap)
            {
                best = phrase;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    /// <summary>
    /// An observation from what the player kept. The name and note are trimmed, and a blank note
    /// is none.
    /// </summary>
    /// <param name="experiment">The experiment id and alternative the variation came from, or null.</param>
    /// <exception cref="ArgumentException">The name is blank, there is neither a variation nor a note, or an experiment has no variation.</exception>
    public static Observation Create(
        string id,
        SavedPassage phrase,
        KeptVariation? variation,
        string name,
        string? note,
        DateTimeOffset createdAt,
        (string Id, int Alternative)? experiment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(phrase);
        string? text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (variation is null && text is null)
        {
            throw new ArgumentException("An observation keeps a variation, a note or both.", nameof(note));
        }

        if (experiment is { } tried)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tried.Id, nameof(experiment));
            ArgumentOutOfRangeException.ThrowIfNegative(tried.Alternative, nameof(experiment));
            if (variation is null)
            {
                throw new ArgumentException("An experiment is kept with its variation.", nameof(experiment));
            }
        }

        return new(id, phrase, experiment?.Id, experiment?.Alternative, variation, Trimmed(name), text, createdAt);
    }

    /// <summary>Whether it is about the phrase: its phrase and <paramref name="phrase"/> share at least one tick.</summary>
    /// <remarks>
    /// Overlap rather than an exact match, so an observation still shows after the player moves a
    /// boundary, and shows on both phrases when a boundary is added inside it. Phrases that only
    /// touch do not overlap. The filter is not compared.
    /// </remarks>
    public bool IsAbout(TickSpan phrase) => Phrase.Span.Overlaps(phrase);

    /// <summary>The same observation under another name.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public Observation Rename(string name) => this with { Name = Trimmed(name) };

    /// <summary>The same observation with another note; a blank note is none.</summary>
    /// <exception cref="ArgumentException">The note is blank and there is no variation, so nothing would be kept.</exception>
    public Observation WithNote(string? note)
    {
        string? text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return Variation is null && text is null
            ? throw new ArgumentException("An observation keeps a variation, a note or both.", nameof(note))
            : this with { Note = text };
    }

    /// <summary>
    /// The variation's chain, over the same notes, Keep choices and key it ran with, so it is heard as
    /// it was kept; null for a note alone. The notes are those of its passage, or the notes it names
    /// when the player picked notes one by one.
    /// </summary>
    /// <param name="notes">The piece's notes, with the project's corrections applied.</param>
    /// <exception cref="ProjectFileException">The notes or a step no longer fit the piece; the message is a plain reason for the player.</exception>
    public VariationChain? Restore(IReadOnlyList<Note> notes, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        if (Variation is not { } kept)
        {
            return null;
        }

        MotifSelection? selection;
        try
        {
            selection = MotifSelector.Select(notes, meter, kept.Passage.Span, kept.Passage.Filter, MotifSelection.MaxStructureBars);
            if (!selection.NoteIds.SequenceEqual(kept.NoteIds, StringComparer.Ordinal))
            {
                MotifSelection picked = MotifSelector.SelectNotes(notes, meter, kept.NoteIds);
                selection = picked.Span == kept.Passage.Span && picked.NoteIds.SequenceEqual(kept.NoteIds, StringComparer.Ordinal) ? picked : null;
            }
        }
        catch (ArgumentException)
        {
            selection = null;
        }

        if (selection is null)
        {
            throw new ProjectFileException($"The kept variation \"{Name}\" no longer matches the notes of the piece.");
        }

        VariationChain chain = VariationChain.Start(
            VariationFacts.MotifNotes(notes, selection),
            VariationContext.For(selection, kept.Keep.ToHashSet(), key: kept.Key, meter: meter));
        foreach (SavedStep step in kept.Steps)
        {
            chain = OpenedProject.Propose(chain, step).Accept();
        }

        return kept.Pending is { } pending ? OpenedProject.Propose(chain, pending) : chain;
    }

    /// <summary>
    /// "Für Elise – bars 1–2 – Brighter answer.mid": the project's file name without its extension,
    /// the bars of the variation's passage, and the observation's name (<see cref="MidiExporter.FileName"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">It keeps a note alone, with no variation to export.</exception>
    public string MidiFileName(string projectFileName, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(projectFileName);
        ArgumentNullException.ThrowIfNull(meter);
        TickSpan span = (Variation ?? throw new InvalidOperationException("This observation keeps a note, not a variation.")).Passage.Span;
        int first = meter.BarAt(span.Onset).Number;
        int last = meter.BarAt(new Tick(span.End.Value - 1)).Number;
        string bars = first == last ? $"bar {first}" : $"bars {first}–{last}";
        return MidiExporter.FileName(Path.GetFileNameWithoutExtension(projectFileName), bars, Name);
    }

    private static string Trimmed(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }
}