using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The calls about what the project keeps with the piece (0018; contract 2): the marks, the bookmarks and their
/// categories, the resume points, the ideas, the piece's settings overrides and the Check flags. Each change
/// answers with the collection as it now is, and project.save writes all of it.
/// </summary>
internal sealed partial class OpenPiece
{
    private string CallOnProject(string name, Request request) => name switch
    {
        "marks" => MarksAnswer(),
        "ink.add" => Marks(marks => marks.AddInk(request.Elements("strokes").Select(ReadStroke))),
        "ink.remove" => Marks(marks => marks.RemoveInk(request.Texts("ids"))),
        "ink.clear" => Marks(marks => marks.WithInk([])),
        "note.add" => Marks(marks => marks.AddNote(new MarginNote(marks.NextNoteId(), Span(request), request.Required("text"), Now(request)))),
        "note.change" => Marks(marks => marks.ChangeNote(request.Required("id"), request.Required("text"))),
        "note.remove" => Marks(marks => marks.RemoveNote(request.Required("id"))),
        "highlight.add" => Marks(marks => marks.AddHighlight(new Highlight(marks.NextHighlightId(), Span(request), request.Texts("notes"), request.Enum<InkColour>("colour") ?? InkColour.Amber))),
        "highlight.remove" => Marks(marks => marks.RemoveHighlight(request.Required("id"))),
        "fingering.set" => Marks(marks => marks.SetFingering(new Fingering(request.Required("noteId"), request.Int("finger") ?? throw FacadeException.BadRequest("The fingering needs \"finger\", 1 to 5.")))),
        "fingering.remove" => Marks(marks => marks.RemoveFingering(request.Required("noteId"))),
        "bookmarks" => BookmarksAnswer(),
        "bookmark.add" => AddBookmark(request),
        "bookmark.change" => ChangeBookmark(request),
        "bookmark.move" => Bookmarks(project => project.MoveBookmark(request.Required("id"), request.Int("index") ?? throw FacadeException.BadRequest("Send \"index\", the place in your order."))),
        "bookmark.remove" => Bookmarks(project => project.RemoveBookmark(request.Required("id"))),
        "category.add" => Bookmarks(project => project.AddCategory(new BookmarkCategory(project.NextCategoryId(), request.Required("name"), request.Enum<InkColour>("ink") ?? InkColour.Graphite))),
        "category.rename" => Bookmarks(project => project.RenameCategory(request.Required("id"), request.Text("name"))),
        "category.remove" => Bookmarks(project => project.RemoveCategory(request.Required("id"))),
        "resume" => Answer.Write(json => Answer.ResumePoints(json, Project, Piece.Meter)),
        "resume.save" => SaveResumePoint(request),
        "resume.rename" => Resume(project => project.ChangeResumePoint(request.Required("id"), point => point.Rename(request.Text("name")))),
        "resume.remove" => Resume(project => project.RemoveResumePoint(request.Required("id"))),
        "ideas" => IdeasAnswer(),
        "idea.keep" => KeepIdea(request),
        "idea.rename" => Ideas(project => project.RenameObservation(request.Required("id"), request.Required("name"))),
        "idea.note" => Ideas(project => project.ChangeObservationNote(request.Required("id"), request.Text("note"))),
        "idea.remove" => Ideas(project => project.DeleteObservation(request.Required("id"))),
        "idea.open" => OpenIdea(request),
        "settings" => Answer.Write(json =>
        {
            Answer.SettingsCatalogue(json);
            Answer.Overrides(json, Project);
        }),
        "settings.override" => Change(project => project.OverrideSetting(request.Required("id"), request.Text("value")), json => Answer.Overrides(json, Project)),
        "check" => Answer.Write(json => Answer.Check(json, Project, Words)),
        "check.resolve" => Change(project => project.ResolveCheck(request.Int("bar") ?? throw FacadeException.BadRequest("Send \"bar\"."), request.Bool("resolved") ?? true), json => Answer.Check(json, Project, Words)),
        _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\"."),
    };

    /// <summary>
    /// The settings as a whole (piece 0): "values" as stored, then "preset" applied or one "set" {id, value},
    /// then a piece's "overrides" laid over; answers with the preset label and every value.
    /// </summary>
    public static string SettingsState(Request request)
    {
        SettingsState state = request.Has("values") ? Settings.SettingsState.From(Pairs(request.Element("values")!.Value)) : Settings.SettingsState.Default;
        try
        {
            if (request.Enum<Preset>("preset") is { } preset)
            {
                state = state.WithPreset(preset);
            }

            Request set = request.Object("set");
            if (!set.IsEmpty)
            {
                state = state.With(set.Required("id"), set.Required("value"));
            }

            if (request.Has("overrides"))
            {
                state = state.Resolve(Pairs(request.Element("overrides")!.Value).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            }
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return Answer.Write(json => Answer.SettingsState(json, state));
    }

    private static IEnumerable<KeyValuePair<string, string>> Pairs(JsonElement values) =>
        values.ValueKind == JsonValueKind.Object
            ? values.EnumerateObject().Select(property => property.Value.ValueKind == JsonValueKind.String
                ? new KeyValuePair<string, string>(property.Name, property.Value.GetString()!)
                : throw FacadeException.BadRequest($"\"{property.Name}\" must be text."))
            : throw FacadeException.BadRequest("Settings values are an object of text.");

    private string Change(Func<Project, Project> change, Action<Utf8JsonWriter> answer)
    {
        try
        {
            Project = change(Project);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or ProjectFileException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return Answer.Write(answer);
    }

    private string Marks(Func<Marks, Marks> change) => Change(project => project.ChangeMarks(change), json => Answer.Marks(json, Project.Marks, Piece.Meter));

    private string MarksAnswer() => Answer.Write(json => Answer.Marks(json, Project.Marks, Piece.Meter));

    private string Bookmarks(Func<Project, Project> change) => Change(change, json => Answer.Bookmarks(json, Project, Piece.Meter, Words));

    private string BookmarksAnswer() => Answer.Write(json => Answer.Bookmarks(json, Project, Piece.Meter, Words));

    private string Resume(Func<Project, Project> change) => Change(change, json => Answer.ResumePoints(json, Project, Piece.Meter));

    private string Ideas(Func<Project, Project> change) => Change(change, json => Answer.Ideas(json, Project, Piece.Meter));

    private string IdeasAnswer() => Answer.Write(json => Answer.Ideas(json, Project, Piece.Meter));

    private static InkStroke ReadStroke(JsonElement stroke)
    {
        try
        {
            return ProjectFile.ReadStroke(stroke);
        }
        catch (ProjectFileException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    /// <summary>The span a mark is about: "span" {onset, end}, "bars" {first, last} or "phrase" (a number).</summary>
    private TickSpan Span(Request request)
    {
        if (request.Span("span") is { } span)
        {
            return span;
        }

        if (request.Int("phrase") is int number)
        {
            Phrase phrase = Phrases.Phrases.FirstOrDefault(phrase => phrase.Number == number) ?? throw FacadeException.BadRequest($"There is no phrase {number}.");
            return phrase.Span;
        }

        Request bars = request.Object("bars");
        int first = bars.Int("first") ?? throw FacadeException.BadRequest("Send \"span\" {onset, end}, \"bars\" {first, last} or \"phrase\" (a number).");
        int last = bars.Int("last") ?? first;
        try
        {
            return MotifSelector.Bars(Piece.Meter, first, last, Piece.End);
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    private static DateTimeOffset Now(Request request) =>
        request.Text("now") is { } text
            ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : throw FacadeException.BadRequest("\"now\" must be an ISO 8601 time.")
            : DateTimeOffset.UtcNow;

    private string AddBookmark(Request request)
    {
        TickSpan span = Span(request);
        bool fromPhrase = request.Has("phrase") || Phrases.Phrases.Any(phrase => phrase.Span == span);
        string title = request.Text("title") ?? BarsTitle(span);
        return Bookmarks(project => project.AddBookmark(new Bookmark(project.NextBookmarkId(), title, request.Text("category") ?? BookmarkCategory.PractiseId, span, fromPhrase, Now(request))));
    }

    private string ChangeBookmark(Request request)
    {
        string id = request.Required("id");
        bool moves = request.Has("span") || request.Has("bars") || request.Has("phrase");
        TickSpan? span = moves ? Span(request) : null;
        return Bookmarks(project => project.ChangeBookmark(id, bookmark => new Bookmark(
            bookmark.Id,
            request.Text("title") ?? bookmark.Title,
            request.Text("category") ?? bookmark.CategoryId,
            span ?? bookmark.Span,
            span is { } moved ? request.Has("phrase") || Phrases.Phrases.Any(phrase => phrase.Span == moved) : bookmark.FromPhrase,
            bookmark.CreatedAt)));
    }

    /// <summary>"bars 1–8" / "такты 1–8": the title a bookmark starts with.</summary>
    private string BarsTitle(TickSpan span)
    {
        int first = Piece.Meter.BarAt(span.Onset).Number;
        int last = Piece.Meter.BarAt(new Tick(span.End.Value - 1)).Number;
        return first == last ? Words.T($"bar {first}", $"такт {first}") : Words.T($"bars {first}–{last}", $"такты {first}–{last}");
    }

    /// <summary>
    /// Saves where the player is: a new resume point, or the one "id" names brought up to date. Fields not sent
    /// keep the point's values; a new point starts at the selection, as written, with both hands.
    /// </summary>
    private string SaveResumePoint(Request request)
    {
        DateTimeOffset now = Now(request);
        SavedPassage? passage = null;
        bool hasPassage = request.Has("passage");
        if (hasPassage)
        {
            Request sent = request.Object("passage");
            TickSpan span = sent.Span("span") ?? throw FacadeException.BadRequest("The passage needs \"span\".");
            passage = new SavedPassage(span, sent.Filter("filter"));
        }

        string? id = request.Text("id");
        ResumePoint point;
        try
        {
            point = id is null ? ResumePoint.Start(Project.NextResumePointId(), now, request.Text("name")) : Project.FindResumePoint(id) ?? throw FacadeException.BadRequest($"There is no resume point {id}.");
            if (request.Has("name"))
            {
                point = point.Rename(request.Text("name"));
            }

            if (!hasPassage)
            {
                passage = point.Passage;
            }

            long at = request.Long("at") ?? passage?.Span.Onset.Value ?? point.At.Value;
            Hand? hands = request.Text("hands") is { } text ? text.Equals("both", StringComparison.OrdinalIgnoreCase) ? null : request.Enum<Hand>("hands") : point.Hands;
            point = point.Touch(
                at >= 0 ? new Tick(at) : throw FacadeException.BadRequest("\"at\" is a tick, 0 or more."),
                passage,
                request.Bool("focused") ?? point.Focused,
                request.Has("overlays") ? request.Enums<Overlay>("overlays") : point.Overlays,
                request.Int("tempoPercent") ?? point.TempoPercent,
                hands,
                request.Bool("loop") ?? point.Loop,
                now);
            Project = id is null ? Project.AddResumePoint(point) : Project.ChangeResumePoint(id, _ => point);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return Answer.Write(json =>
        {
            json.WritePropertyName("saved");
            Answer.ResumePoint(json, point, Piece.Meter);
            Answer.ResumePoints(json, Project, Piece.Meter);
        });
    }

    /// <summary>
    /// Keeps the variation heard now, with the experiment it came from and the fact that offered it, as an idea about the
    /// selection (0018, section 7); with "note" and no variation, keeps the note alone. The name defaults to the
    /// experiment's title or the steps' operations.
    /// </summary>
    private string KeepIdea(Request request)
    {
        MotifSelection selection = Selection ?? throw FacadeException.NoSelection();
        VariationChain? chain = Chain is { Result: not null } ? Chain : null;
        string? note = request.Text("note");
        if (chain is null && note is null)
        {
            throw FacadeException.Unavailable("There is no variation to keep; send \"note\" to keep a note alone.");
        }

        (string Id, int Alternative)? experiment = _trial is { } trial ? (trial.Experiment.Id, trial.Alternative) : null;
        string name = request.Text("name")
            ?? (_trial is { } tried ? tried.Experiment.Alternatives.Count > 1 ? $"{tried.Experiment.Title}: {tried.Experiment.Alternatives[tried.Alternative].Label}" : tried.Experiment.Title
                : chain is not null ? string.Join(" › ", chain.Steps.Select(step => step.Operation.Id))
                : BarsTitle(selection.Span));
        string? factId = request.Text("factId") ?? (_trial is { } offered ? offered.Experiment.FactIds.FirstOrDefault() : null);
        return Ideas(project => project.Keep(Observation.Create(
            project.NextObservationId(),
            SavedPassage.Of(selection),
            chain is null ? null : KeptVariation.Create(chain, selection.Filter),
            name,
            note,
            Now(request),
            chain is null ? null : experiment) with
        { FactId = factId }));
    }

    /// <summary>Brings an idea back: its phrase as the selection and its variation, when it keeps one, as the chain.</summary>
    private string OpenIdea(Request request)
    {
        string id = request.Required("id");
        Observation idea = Project.Observations.FirstOrDefault(kept => kept.Id == id) ?? throw FacadeException.BadRequest($"There is no idea {id}.");
        MotifSelection selection;
        VariationChain? chain;
        try
        {
            selection = MotifSelector.Select(Notes, Piece.Meter, idea.Phrase.Span, idea.Phrase.Filter, MotifSelection.MaxStructureBars);
            chain = idea.Restore(Notes, Piece.Meter);
        }
        catch (Exception exception) when (exception is ArgumentException or ProjectFileException)
        {
            throw FacadeException.Unavailable(exception.Message);
        }

        Selection = selection;
        _selectionKind = MotifSelectionKind.Range;
        _understood = null;
        _offered = null;
        _trial = null;
        Chain = chain;
        if (chain is not null)
        {
            Keep.Clear();
            Keep.UnionWith(chain.Context.Keep);
        }

        return Answer.Write(json =>
        {
            Answer.Selection(json, "selection", selection, KeySignature.InForce(Piece.Keys, selection.Span.Onset));
            json.WriteBoolean("hasVariation", chain is not null);
            if (chain is not null)
            {
                json.WritePropertyName("variation");
                json.WriteStartObject();
                Answer.Chain(json, chain, Words);
                json.WriteEndObject();
            }
        });
    }
}