using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The calls about what the project keeps with the piece (0018; contract 2): the resume points, the ideas, the piece's
/// settings overrides and the Check flags (the marks and the bookmarks: OpenPiece.Marks.cs). Each change answers with the
/// collection as it now is, and project.save writes all of it.
/// </summary>
internal sealed partial class OpenPiece
{
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

    /// <summary>The Check flags, a recording's uncertain lengths and a scan's pages as its page reports count them (version 0.4).</summary>
    private void CheckAnswer(Utf8JsonWriter json)
    {
        Answer.Check(json, Project, Words, Piece.Meter, Piece.End, HeardLengths.Of(Piece, Notes),
            Project.Scan is { Pages.Count: > 0 } scan && Piece.MusicXml is not null ? scan.CheckOf(Piece).Pages : []);
        PiecesRequest.Check(json, Project, Piece.Meter);
        // The working notes' music by its SHA-256 (v1.3 i-2), as scan.join names the score it merged: what the app keeps
        // beside a scan's Check bars on the device is found by it.
        json.WriteString("fingerprint", PieceOriginal.Fingerprint(Project.Source));
    }

    private string Resume(Func<Project, Project> change) => Change(change, json => Answer.ResumePoints(json, Project, Piece.Meter));

    private string Ideas(Func<Project, Project> change) => Change(change, json => Answer.Ideas(json, Project, Piece.Meter));

    private string IdeasAnswer() => Answer.Write(json => Answer.Ideas(json, Project, Piece.Meter));

    /// <summary>
    /// Saves where the player is: a new resume point, or the one "id" names brought up to date. Fields not sent
    /// keep the point's values; a new point starts at the selection, as written, with both hands. "passage": null
    /// (#327, within contract 12) says the player left no passage selected: the point keeps only its place.
    /// </summary>
    private string SaveResumePoint(Request request)
    {
        DateTimeOffset now = request.Now();
        SavedPassage? passage = null;
        bool hasPassage = request.Sends("passage");
        if (request.Has("passage"))
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
    /// Keeps the variation heard now, with the fact that offered it ("factId"), as an idea about the selection (0018,
    /// section 7); with "note" and no variation, keeps the note alone. The name defaults to the steps' operations.
    /// </summary>
    private string KeepIdea(Request request)
    {
        MotifSelection selection = Selection.Required();
        VariationChain? chain = Selection.Chain is { Result: not null } heard ? heard : null;
        string? note = request.Text("note");
        if (chain is null && note is null)
        {
            throw FacadeException.Unavailable("There is no variation to keep; send \"note\" to keep a note alone.");
        }

        string name = request.Text("name")
            ?? (chain is not null ? string.Join(" › ", chain.Steps.Select(step => step.Operation.Id)) : MarksSession.BarsTitle(State, selection.Span));
        string? factId = request.Text("factId");
        return Ideas(project => project.Keep(Observation.Create(
            project.NextObservationId(),
            SavedPassage.Of(selection),
            chain is null ? null : KeptVariation.Create(chain, selection.Filter),
            name,
            note,
            request.Now()) with
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

        Selection.Open(selection, chain);
        return Answer.Write(json =>
        {
            SelectionAnswer(json, selection);
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