using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analytics;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;
using MotiffLab.Web.Explore;
using MotiffLab.Web.Observations;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Recording observations (#78, docs/08-central-product-model.md): Keep this variation keeps the phrase, the
/// experiment and alternative, the variation's steps and an optional note in the player's words as an
/// <see cref="Observation"/>; the phrase lists what was kept about it, and opening one puts its selection and
/// variation back to hear and compare. With a project they are saved with it on the device. Für Elise and files
/// opened directly have no project, so what is kept there lasts only while the page is open, and the page says so.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>What is kept while the piece has no project: never saved, and forgotten when another piece loads.</summary>
    private Project? _session;

    /// <summary>The experiment that was kept and the observation it was kept as, for the note on the Explore card.</summary>
    private (ExploreTrial Trial, string Id)? _keptTrial;

    /// <summary>What the last action on a kept observation did, or why it could not, and the phrase it was said on.</summary>
    private (string? Text, TickSpan? On) _observationStatus;

    /// <summary>What the list says about the last action, while the same phrase is listed.</summary>
    private string? ObservationStatus => _observationStatus.On == ListedPhrase?.Span ? _observationStatus.Text : null;

    /// <summary>The kept observation the variation heard now is, cached for the chain it was worked out for.</summary>
    private (VariationChain? Chain, IReadOnlyList<Observation> Kept, string? Id) _heardKept;

    /// <summary>Everything kept about the piece's phrases, in the order it was kept.</summary>
    private IReadOnlyList<Observation> Kept => (_project ?? _session)?.Observations ?? [];

    /// <summary>Whether what is kept lasts only while the page is open.</summary>
    private bool KeptForSessionOnly => _project is null;

    /// <summary>Said on the Explore card once a variation is kept.</summary>
    private string KeptText => Text[KeptForSessionOnly ? "explore.keptForSession" : "explore.keptWithProject"];

    /// <summary>The observation the kept experiment on the card was saved as; null when the card shows no kept experiment.</summary>
    private Observation? KeptOnCard =>
        _keptTrial is { } kept && ActiveTrial is { Kept: true } trial && ReferenceEquals(kept.Trial, trial)
            ? Kept.FirstOrDefault(observation => observation.Id == kept.Id)
            : null;

    /// <summary>The phrase whose kept observations are listed: the one selected, or the one the selection lies mostly in.</summary>
    private Phrase? ListedPhrase => _selection is { } selection && _phrases is { } map ? CurrentPhrase ?? map.Covering(selection.Span) : null;

    private string ListedHeading => ListedPhrase is { } phrase ? Text.Format("kept.forPhrase", phrase.Number) : Text["kept.forThisPhrase"];

    /// <summary>The observations about the listed phrase (<see cref="Observation.IsAbout"/>), in the order they were kept.</summary>
    private IReadOnlyList<ObservationRow> KeptRows
    {
        get
        {
            if (ListedPhrase is not { } phrase || _meter is null)
            {
                return [];
            }

            string? heard = HeardKeptId;
            return [.. Kept.Where(observation => observation.IsAbout(phrase.Span)).Select(observation => new ObservationRow(
                observation,
                BarsOf((observation.Variation?.Passage ?? observation.Phrase).Span),
                Text[observation.Variation is null ? "obs.source.note" : observation.ExperimentId is null ? "obs.source.steps" : "obs.source.experiment"],
                observation.Id == heard))];
        }
    }

    /// <summary>How many observations are about the phrase, for its chip.</summary>
    private int KeptCount(Phrase phrase) => Kept.Count(observation => observation.IsAbout(phrase.Span));

    /// <summary>The id of the kept observation whose variation is the one heard now, or null.</summary>
    private string? HeardKeptId
    {
        get
        {
            IReadOnlyList<Observation> kept = Kept;
            if (!ReferenceEquals(_heardKept.Chain, _chain) || !ReferenceEquals(_heardKept.Kept, kept))
            {
                KeptVariation? heard = _chain is { Result: not null } chain && _selection is { } selection ? KeptVariation.Create(chain, selection.Filter) : null;
                _heardKept = (_chain, kept, heard is null ? null : kept.FirstOrDefault(observation => observation.Variation == heard)?.Id);
            }

            return _heardKept.Id;
        }
    }

    /// <summary>A variation of the player's own steps that is not kept yet. An experiment being tried is kept from its card.</summary>
    private bool CanKeepHeard => _chain is { Result: not null } && _selection is { NoteIds.Count: > 0 } && ActiveTrial is not { Kept: false } && HeardKeptId is null;

    private void Say(string? text) => _observationStatus = (text, ListedPhrase?.Span);

    /// <summary>Forgets what was kept without a project; a new piece starts with nothing kept.</summary>
    private void ForgetSessionObservations()
    {
        _session = null;
        _keptTrial = null;
        Say(null);
    }

    /// <summary>
    /// Keep this variation on the Explore card: the experiment's variation becomes an observation about the
    /// selection it was offered for, named after the experiment and the alternative. The page saves it next.
    /// </summary>
    private void KeepObservation(ExploreTrial trial)
    {
        string name = trial.Experiment.Alternatives.Count > 1 ? $"{trial.Experiment.Title}: {trial.Current.Label}" : trial.Experiment.Title;
        Observation observation = Observation.Create(
            NextObservationId(),
            SavedPassage.Of(trial.OfferedIn.Selection),
            KeptVariation.Create(trial.Chain, trial.Experiment.Filter),
            name,
            note: null,
            DateTimeOffset.Now,
            (trial.Experiment.Id, trial.Alternative));
        Change(notebook => notebook.Keep(observation));
        _keptTrial = (trial, observation.Id);
        Say(null);
    }

    /// <summary>Keeps the variation heard now, made with the player's own steps, named by its steps.</summary>
    private Task KeepHeardAsync()
    {
        if (!CanKeepHeard || _chain is not { } chain || _selection is not { } selection)
        {
            return Task.CompletedTask;
        }

        string name = string.Join(" › ", [.. chain.Steps.Select(StepLabel), .. chain.Pending is { } pending ? [StepLabel(pending)] : Array.Empty<string>()]);
        Observation observation = Observation.Create(NextObservationId(), SavedPassage.Of(selection), KeptVariation.Create(chain, selection.Filter), name, note: null, DateTimeOffset.Now);
        return SaveObservationsAsync(notebook => notebook.Keep(observation), Text.Format("kept.kept", observation.Name));
    }

    /// <summary>A note alone about the selection, in the player's words.</summary>
    private Task WriteNoteAsync(string note)
    {
        if (_selection is not { } selection || string.IsNullOrWhiteSpace(note))
        {
            return Task.CompletedTask;
        }

        Observation observation = Observation.Create(NextObservationId(), SavedPassage.Of(selection), variation: null, Text.Format("kept.noteOn", BarsOf(selection.Span)), note, DateTimeOffset.Now);
        return SaveObservationsAsync(notebook => notebook.Keep(observation), Text["kept.noteKept"]);
    }

    /// <summary>The note on the kept experiment of the Explore card.</summary>
    private Task SaveKeptNoteAsync(string note) =>
        KeptOnCard is { } observation ? NoteObservationAsync(new ObservationText(observation.Id, note)) : Task.CompletedTask;

    private Task NoteObservationAsync(ObservationText change)
    {
        if (Kept.FirstOrDefault(observation => observation.Id == change.Id) is not { } observation)
        {
            return Task.CompletedTask;
        }

        if (observation.Variation is null && string.IsNullOrWhiteSpace(change.Text))
        {
            Say(Text.Format("kept.blankNote", observation.Name));
            return Task.CompletedTask;
        }

        return SaveObservationsAsync(notebook => notebook.ChangeObservationNote(change.Id, change.Text), Text.Format("kept.noteSaved", observation.Name));
    }

    private Task RenameObservationAsync(ObservationText change) =>
        string.IsNullOrWhiteSpace(change.Text)
            ? Task.CompletedTask
            : SaveObservationsAsync(notebook => notebook.RenameObservation(change.Id, change.Text), Text.Format("kept.renamed", change.Text.Trim()));

    private Task DeleteObservationAsync(Observation observation)
    {
        if (_keptTrial?.Id == observation.Id)
        {
            _keptTrial = null;
        }

        return SaveObservationsAsync(notebook => notebook.DeleteObservation(observation.Id), Text.Format("kept.deleted", observation.Name));
    }

    /// <summary>
    /// Opens a kept variation: its passage is selected with the notes it ran on, its Keep choices come back,
    /// and its steps are heard at once, as when it was kept, so it can be compared with the original.
    /// </summary>
    private async Task OpenObservationAsync(Observation observation)
    {
        if (_notes is null || _meter is null || observation.Variation is not { } kept)
        {
            return;
        }

        VariationChain? chain;
        try
        {
            chain = observation.Restore(_notes, _meter);
        }
        catch (ProjectFileException exception)
        {
            Say(exception.Message);
            return;
        }

        if (chain?.Result is null)
        {
            return;
        }

        await MoveSelectionAsync(kept.Passage.Span, kept.Passage.Filter, Text.Format("kept.opened", observation.Name));
        _keep.Clear();
        _keep.UnionWith(chain.Context.Keep);
        _scoreChoice = ScoreChoice.Variation;
        Say(null);
        await ShowAsync(chain, play: true);
    }

    /// <summary>Hands the player the kept variation as a MIDI file (S1-X1), named by <see cref="Observation.MidiFileName"/>.</summary>
    private async Task ExportObservationAsync(Observation observation)
    {
        if (_notes is null || _meter is null || _score is null || observation.Variation is not { } kept)
        {
            return;
        }

        VariationChain? chain;
        try
        {
            chain = observation.Restore(_notes, _meter);
        }
        catch (ProjectFileException exception)
        {
            Say(exception.Message);
            return;
        }

        if (chain?.Result is not { } variation)
        {
            return;
        }

        string fileName = _project?.FileName ?? _title ?? "";
        byte[] file = MidiExporter.Export(variation.Notes, _score.Tempos, _meter, Path.GetFileNameWithoutExtension(fileName), kept.Passage.Span.Onset);
        await Projects.DownloadAsync(observation.MidiFileName(fileName, _meter), file, "audio/midi");
        await Analytics.TrackAsync(AnalyticsEvent.MidiExported(variation: true, chain.Steps.Count + (chain.Pending is null ? 0 : 1)));
    }

    /// <summary>Changes what is kept and saves it with the project at once; without a project, it is kept for the page only.</summary>
    private async Task SaveObservationsAsync(Func<Project, Project> change, string status)
    {
        Change(change);
        Say(status);
        if (_project is { } project)
        {
            CurrentPiece.Saved(project);
            await Projects.SaveAsync(project);
        }
    }

    /// <summary>Applies a change to the project's observations, or to the page's while there is no project.</summary>
    private void Change(Func<Project, Project> change)
    {
        if (_project is { } project)
        {
            _project = change(project);
        }
        else
        {
            _session = change(_session ?? Project.Create("session", _title ?? "", [], DateTimeOffset.Now));
        }
    }

    /// <summary>"o_0004": one more than the highest number kept, so ids stay stable for the life of the project.</summary>
    private string NextObservationId() => (_project ?? _session)?.NextObservationId() ?? "o_0001";

    /// <summary>"bar 5", "bars 5–6", "the pickup", "the pickup and bars 1–4", as the Explore card names a passage.</summary>
    private string BarsOf(TickSpan span)
    {
        int first = _meter!.BarAt(span.Onset).Number;
        int last = _meter.BarAt(new Tick(Math.Max(span.Onset.Value, span.End.Value - 1))).Number;
        return (first, last) switch
        {
            (0, 0) => Text["bars.pickup"],
            (0, 1) => Text["bars.pickupAndOne"],
            (0, _) => Text.Format("bars.pickupAndRange", last),
            _ when first == last => Text.Format("bars.one", first),
            _ => Text.Format("bars.range", first, last),
        };
    }
}