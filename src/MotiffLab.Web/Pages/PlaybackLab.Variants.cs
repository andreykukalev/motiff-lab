using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Assistant.Variants;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Variations;
using MotiffLab.Web.Compare;
using MotiffLab.Web.Score;
using MotiffLab.Web.Workspace;

namespace MotiffLab.Web.Pages;

/// <summary>
/// «Что если» variants of a phrase (an experiment, Settings · Experimental, version 0.6; the AI companion study, sections 2.4
/// and 4.6): in phrase focus, a panel under the card makes variants without a model, by an engine operation picked from the
/// «Что если» chips, from the original or from the variant shown. Each is a draft heard as the plum ossia over the phrase,
/// compared Original / Variant / Alternate, kept as an idea (the last four as chips in the card's header), and taken into
/// work: a carry of the assembly journal in place of the phrase, made notes at once, with Undo in the toast and «Вернуть что
/// было» in the journal; the notes before the assembly are kept. music-core makes every note (<see cref="PhraseVariants"/>,
/// <see cref="VariantPromotion"/>); nothing is sent anywhere.
/// </summary>
public partial class PlaybackLab
{
    public const string VariantsSetting = "experimental.assistantVariants";

    /// <summary>How many kept variants the card's header shows: the last four (the study, 4.12 question 2).</summary>
    private const int VariantChipsShown = 4;

    /// <summary>The variants kept this session, by id, with their lineage and provenance (an idea keeps only the steps until v16).</summary>
    private readonly Dictionary<string, PhraseVariant> _variants = new(StringComparer.Ordinal);

    /// <summary>The variant made and not kept yet; one at a time.</summary>
    private VariantReplay? _variantDraft;

    /// <summary>Each kept variant made again over the notes, once for the notes and its name.</summary>
    private readonly Dictionary<string, (IReadOnlyList<MusicCore.Events.Note> Notes, string Name, VariantReplay Replay)> _variantReplays = new(StringComparer.Ordinal);

    /// <summary>The music of each variant taken into work this session, the journal's source for it.</summary>
    private readonly Dictionary<string, SourceMusic> _variantMusic = new(StringComparer.Ordinal);

    /// <summary>Whether the next chip starts from the original although a variant is shown.</summary>
    private bool _variantFromOriginal;

    private string? _variantStatus;

    /// <summary>The project the session's variants belong to.</summary>
    private string? _variantsOf;

    private (PhraseCardSource Card, Variation Variation, PhraseCardSource Tinted)? _variantTinted;

    private bool VariantsOn => SettingsStore.IsOn(VariantsSetting);

    /// <summary>Whether the panel shows: the experiment is on and a passage with notes is in focus.</summary>
    private bool VariantsShown => VariantsOn && Focused && _selection is { NoteIds.Count: > 0 } && _notes is not null && _meter is not null;

    /// <summary>The variant heard now: the draft, or the kept variant whose chain is heard; null for any other variation.</summary>
    private VariantReplay? ShownVariant
    {
        get
        {
            if (!VariantsOn || _chain is null || _notes is null || _meter is null)
            {
                return null;
            }

            if (_variantDraft is { } draft && ReferenceEquals(draft.Chain, _chain))
            {
                return draft;
            }

            return HeardKeptId is { } id && Kept.FirstOrDefault(observation => observation.Id == id) is { } kept ? KeptReplay(kept) : null;
        }
    }

    /// <summary>A kept variant made again over the notes as they are.</summary>
    private VariantReplay? KeptReplay(Observation observation)
    {
        if (_variantReplays.TryGetValue(observation.Id, out var cached) && ReferenceEquals(cached.Notes, _notes) && cached.Name == observation.Name)
        {
            return cached.Replay;
        }

        PhraseVariant? variant = _variants.TryGetValue(observation.Id, out PhraseVariant? known) ? known with { Name = observation.Name } : PhraseVariant.FromObservation(observation, _notes!);
        if (variant is null)
        {
            return null;
        }

        VariantReplay replay = PhraseVariants.Replay(variant, _notes!, _meter!, Words);
        _variantReplays[observation.Id] = (_notes!, observation.Name, replay);
        return replay;
    }

    /// <summary>The «Что если» chips: the engine operations the Try buttons offer, with their fixed parameters.</summary>
    private IReadOnlyList<WhatIfChip> WhatIfChips =>
        [.. TryChips.Offered.Select(TryChips.Find).OfType<TryChip>().Where(chip => chip.IsBuilt).Select(chip => new WhatIfChip(chip.Id, chip.LabelIn(Words)))];

    /// <summary>The variant the next chip builds on, or null for the original.</summary>
    private PhraseVariant? VariantBase => _variantFromOriginal ? null : ShownVariant?.Variant;

    /// <summary>The name of the variant shown, for the From switch; null when none is shown.</summary>
    private string? ShownVariantName => ShownVariant?.Variant.Name;

    /// <summary>The chips in the card's header: the last four kept variants about the passage and the draft.</summary>
    private IReadOnlyList<VariantChip> VariantChips
    {
        get
        {
            if (!VariantsOn || FocusSpan is not { } span)
            {
                return [];
            }

            string? heard = HeardKeptId;
            List<VariantChip> chips = [.. FocusVariations.TakeLast(VariantChipsShown).Select(kept => new VariantChip(kept.Id, kept.Name, kept.Id == heard, false, InWork(kept.Id)))];
            if (_variantDraft is { } draft && draft.Variant.Phrase.Span.Overlaps(span))
            {
                chips.Add(new VariantChip(draft.Variant.Id, draft.Variant.Name, ReferenceEquals(draft.Chain, _chain), true, false));
            }

            return chips;
        }
    }

    /// <summary>How many kept variants about the passage the header leaves out.</summary>
    private int VariantsHidden => VariantsOn ? Math.Max(0, FocusVariations.Count - VariantChipsShown) : 0;

    /// <summary>Whether the card has a row of chips over its ossia.</summary>
    private bool FocusHasChips => VariantsOn ? VariantChips.Count > 0 : FocusVariations.Count > 0;

    /// <summary>Whether the variant is in the working notes: a carry from it holds in the journal.</summary>
    private bool InWork(string id) => Work is { } work && VariantPromotion.StepOf(work, id) is not null;

    /// <summary>The panel's card for the variant shown.</summary>
    private VariantCard? ShownVariantCard
    {
        get
        {
            if (ShownVariant is not { Result: { } result } shown)
            {
                return null;
            }

            PhraseVariant variant = shown.Variant;
            bool draft = ReferenceEquals(shown, _variantDraft);
            string overline = variant.ParentId is { } parent && NameOf(parent) is { } parentName
                ? Text.Format("variants.overlineChild", variant.Name, FromName(parentName))
                : variant.Name;
            string title = string.Join(" › ", variant.Steps.Select(StepTitle));
            VariantCheck[] checks = [.. result.Checks.Select(check => new VariantCheck(
                Text.Format($"variants.check.{check.Status}", KeepLabel(check.Property)),
                check.Status switch { KeepStatus.Kept => true, KeepStatus.NoLongerKept => false, _ => null }))];
            string? refusal = _project is not { } project ? Text["variants.noProject"]
                : Work is { } work ? VariantPromotion.Refusal(shown, project.Assembly, work, Words)
                : null;
            return new VariantCard(overline, title, result.Explanation, checks, draft, !draft && InWork(variant.Id), shown.BaseChanged, refusal);
        }
    }

    /// <summary>Original / the variant's name / One after the other, as the workspace hears them.</summary>
    private CompareView VariantCompare => new(ComparingOriginal, ComparingVariation, _alternating, SoundingVariation, Text["term.original"], ShownVariantName ?? Text["term.variation"]);

    /// <summary>The card, with the variant's notes tinted in the ossia: what the app composed plum, what it kept grey.</summary>
    private PhraseCardSource? FocusCardShown
    {
        get
        {
            PhraseCardSource? card = FocusCard;
            if (card is null || _variation is not { } variation || ShownVariant is null)
            {
                return card;
            }

            if (_variantTinted is { } cached && ReferenceEquals(cached.Card, card) && ReferenceEquals(cached.Variation, variation))
            {
                return cached.Tinted;
            }

            string prefix = NotationGenerator.OssiaIdPrefix(0);
            var composed = variation.Changes.Where(change => MusicCore.Events.NoteValues.Of(change.Before) != MusicCore.Events.NoteValues.Of(change.After)).Select(change => change.After.Id)
                .Concat(variation.Added.Select(note => note.Id)).ToHashSet(StringComparer.Ordinal);
            PhraseCardSource tinted = card with
            {
                Tints = variation.Notes.ToDictionary(note => System.Xml.XmlConvert.EncodeLocalName(prefix + note.Id)!, note => composed.Contains(note.Id) ? "version-added" : "repair-kept", StringComparer.Ordinal),
            };
            _variantTinted = (card, variation, tinted);
            return tinted;
        }
    }

    /// <summary>A step as the player reads it: the chip's label when a chip made it, else the operation's name.</summary>
    private string StepTitle(SavedStep step) =>
        TryChips.Chips.FirstOrDefault(chip => chip.OperationId == step.Operation && chip.Parameters == step.Parameters) is { } chip ? chip.LabelIn(Words)
        : VariationOperations.Find(step.Operation)?.NameIn(Words) ?? step.Operation;

    /// <summary>The name of a variant of this session or a kept idea, by id.</summary>
    private string? NameOf(string id) =>
        Kept.FirstOrDefault(observation => observation.Id == id)?.Name ?? (_variants.TryGetValue(id, out PhraseVariant? variant) ? variant.Name : null);

    /// <summary>«варианта 1» after «из» for «Вариант 1»; another name in quotes.</summary>
    private string FromName(string name) =>
        Words.IsRussian && name.StartsWith("Вариант ", StringComparison.Ordinal) ? "варианта " + name["Вариант ".Length..]
        : Words.IsRussian ? $"«{name}»"
        : name;

    private void ChooseVariantBase(bool fromOriginal) => _variantFromOriginal = fromOriginal;

    /// <summary>A «Что если» chip: a new variant from the original or from the variant shown, heard at once as the draft.</summary>
    private async Task MakeVariantAsync(string chipId)
    {
        if (!VariantsShown || TryChips.Find(chipId) is not { } chip || _selection is not { } selection)
        {
            return;
        }

        PhraseVariant? parent = VariantBase;
        if (parent is not null && _variantDraft is { } unkept && unkept.Variant == parent)
        {
            // A variant made from the draft keeps the draft, so its parent stays.
            parent = await KeepDraftAsync() ?? parent;
        }

        string id = NextObservationId();
        string name = PhraseVariants.NextName(Kept.Select(observation => observation.Name), Words);
        VariantReplay made = parent is null
            ? PhraseVariants.FromChip(chip, id, name, selection, _notes!, _meter!, Key, DateTimeOffset.Now, Words)
            : PhraseVariants.FromChip(chip, parent, id, name, _notes!, _meter!, DateTimeOffset.Now, Words);
        if (made.Refusal is { } refusal)
        {
            _variantStatus = refusal;
            return;
        }

        if (made.Chain is not { Result: not null } chain)
        {
            _variantStatus = Text["variants.nothing"];
            return;
        }

        _variantDraft = made;
        _variantFromOriginal = false;
        _variantStatus = null;
        _trial = null;
        _keep.Clear();
        _keep.UnionWith(chain.Context.Keep);
        _scoreChoice = ScoreChoice.Variation;
        await ShowAsync(chain, play: true);
    }

    /// <summary>«Оставить»: the draft becomes an idea about the phrase, a chip in the card's header.</summary>
    private async Task KeepVariantAsync()
    {
        if (await KeepDraftAsync() is { } kept)
        {
            _variantStatus = Text.Format("variants.keptStatus", kept.Name);
        }
    }

    private async Task<PhraseVariant?> KeepDraftAsync()
    {
        if (_variantDraft is not { Chain: not null } draft)
        {
            return null;
        }

        string id = Kept.Any(observation => observation.Id == draft.Variant.Id) ? NextObservationId() : draft.Variant.Id;
        PhraseVariant variant = draft.Variant with { Id = id };
        Observation observation = VariantRecord.ToObservation(variant, notes: null, DateTimeOffset.Now);
        _variants[id] = variant;
        _variantDraft = null;
        await SaveObservationsAsync(notebook => notebook.Keep(observation), Text.Format("kept.kept", variant.Name));
        return variant;
    }

    /// <summary>«Убрать» on the draft: it goes, and the original is heard again.</summary>
    private async Task DiscardVariantAsync()
    {
        _variantDraft = null;
        _variantStatus = null;
        await ClearVariationAsync();
    }

    /// <summary>«Удалить» on a kept variant; one in the working notes stays, so the journal keeps its source.</summary>
    private async Task DeleteVariantAsync()
    {
        if (ShownVariant is not { } shown || Kept.FirstOrDefault(observation => observation.Id == shown.Variant.Id) is not { } observation)
        {
            return;
        }

        if (Work is { } work && VariantPromotion.Carried(work.Steps).Contains(observation.Id))
        {
            _variantStatus = Text["variants.deleteInWork"];
            return;
        }

        _variants.Remove(observation.Id);
        await ClearVariationAsync();
        await DeleteObservationAsync(observation);
        _variantStatus = Text.Format("kept.deleted", observation.Name);
    }

    /// <summary>«Пересчитать»: the variant over the notes under its passage as they are now, kept again under the same id.</summary>
    private async Task RecomputeVariantAsync()
    {
        if (ShownVariant is not { BaseChanged: true } shown || _notes is null || _meter is null)
        {
            return;
        }

        PhraseVariant rebased;
        try
        {
            rebased = PhraseVariants.Rebase(shown.Variant, _notes, _meter);
        }
        catch (InvalidOperationException exception)
        {
            _variantStatus = exception.Message;
            return;
        }

        VariantReplay replay = PhraseVariants.Replay(rebased, _notes, _meter, Words);
        if (replay.Chain is not { Result: not null } chain)
        {
            _variantStatus = replay.Refusal ?? Text["variants.nothing"];
            return;
        }

        if (ReferenceEquals(shown, _variantDraft))
        {
            _variantDraft = replay;
        }
        else if (Kept.FirstOrDefault(observation => observation.Id == rebased.Id) is { } old)
        {
            _variants[rebased.Id] = rebased;
            Observation observation = VariantRecord.ToObservation(rebased with { Name = old.Name }, old.Notes, old.CreatedAt) with { Note = old.Note };
            await SaveObservationsAsync(notebook => notebook.DeleteObservation(old.Id).Keep(observation), Text.Format("variants.recomputed", old.Name));
        }

        _variantStatus = Text.Format("variants.recomputed", rebased.Name);
        await ShowAsync(chain, play: true);
    }

    /// <summary>
    /// «Взять в работу»: the variant (kept first, if it is the draft) is carried by the assembly journal in place of its
    /// phrase and made notes at once; the notes before the assembly are kept, the toast's Undo puts the piece back, and
    /// «Вернуть что было» in the journal takes the carry back later.
    /// </summary>
    private async Task PromoteVariantAsync()
    {
        if (ShownVariant is null || _notes is null || _meter is null || _project is null)
        {
            _variantStatus = _project is null ? Text["variants.noProject"] : null;
            return;
        }

        if (ReferenceEquals(ShownVariant, _variantDraft))
        {
            await KeepDraftAsync();
        }

        if (ShownVariant is not { } shown || _project is not { } project || Work is not { } work)
        {
            return;
        }

        string? refusal = VariantPromotion.Refusal(shown, project.Assembly, work, Words);
        if (refusal is not null || VariantPromotion.Music(shown, _notes, _meter) is not { } music)
        {
            _variantStatus = refusal ?? Text["variants.nothing"];
            return;
        }

        string name = shown.Variant.Name;
        _variantMusic[shown.Variant.Id] = music;
        DateTimeOffset now = DateTimeOffset.Now;
        (AssemblyWork promoted, _) = VariantPromotion.Promote(work, shown.Variant, music, Text.Format("variants.sourceName", name), now);
        Project changed = project.WithJournal(promoted.Steps, promoted.Next).ApplyAssembly(promoted.Result.Music, promoted.Base, now);
        _variantStatus = null;
        await ClearVariationAsync();
        await LeaveFocusAsync();
        await ReopenAsync(changed);
        _mergeToast = (Text.Format("variants.promoted", name), () => ReopenAsync(project));
    }

    /// <summary>A chip in the card's header: the variant heard and shown; one in the working notes says where it is.</summary>
    private async Task ShowVariantChipAsync(VariantChip chip)
    {
        if (chip.Draft)
        {
            if (_variantDraft is { Chain: { } chain } && !ReferenceEquals(chain, _chain))
            {
                _scoreChoice = ScoreChoice.Variation;
                await ShowAsync(chain, play: true);
            }

            return;
        }

        if (chip.InWork)
        {
            _variantStatus = Text.Format("variants.inWorkStatus", chip.Name);
            return;
        }

        if (Kept.FirstOrDefault(observation => observation.Id == chip.Id) is { } kept)
        {
            _variantStatus = null;
            await ShowKeptVariationAsync(kept);
        }
    }

    /// <summary>The music of each variant the journal carries, as a source of the work: this session's, or made again from its idea over the notes it was built on.</summary>
    private void AddVariantSources(Project project, Dictionary<string, SourceMusic> sources, Dictionary<string, string> names)
    {
        IReadOnlyList<string> carried = VariantPromotion.Carried(project.Assembly.Steps);
        if (carried.Any(id => !_variantMusic.ContainsKey(id)) && _notes is not null && _meter is not null)
        {
            // Each carried variant made again over the notes it was built on, from the idea that keeps its record.
            foreach ((string source, SourceMusic music) in VariantPromotion.Sources(project, _notes, _meter, Words))
            {
                _variantMusic.TryAdd(VariantPromotion.VariantIdOf(source)!, music);
            }
        }

        foreach (string id in carried)
        {
            if (project.Observations.FirstOrDefault(observation => observation.Id == id) is not { } observation || !_variantMusic.TryGetValue(id, out SourceMusic? music))
            {
                continue;
            }

            string source = VariantPromotion.SourceIdOf(id);
            sources[source] = music;
            names[source] = Text.Format("variants.sourceName", observation.Name);
        }
    }

    /// <summary>A new piece starts with no variants of this session; the same piece opened again (after «Взять в работу») keeps their lineage and music.</summary>
    private void ForgetVariants()
    {
        if (CurrentPiece.Project?.Id is not { } id || id != _variantsOf)
        {
            _variants.Clear();
            _variantMusic.Clear();
        }

        _variantsOf = CurrentPiece.Project?.Id;
        _variantReplays.Clear();
        _variantDraft = null;
        _variantStatus = null;
        _variantFromOriginal = false;
        _variantTinted = null;
    }
}