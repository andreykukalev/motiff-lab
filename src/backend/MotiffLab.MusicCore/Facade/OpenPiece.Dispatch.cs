using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The calls on an open piece with notes that the piece answers itself (<see cref="CallRegistry.OnPiece"/>).</summary>
internal sealed partial class OpenPiece
{
    /// <summary>The piece's calls by name, each with its handler here and its flags, a list to a feature.</summary>
    internal static partial class Calls
    {
        /// <summary>The piece: its phrases, notation, playback, roll, tempo and saving.</summary>
        public static readonly FacadeCall<OpenPiece>[] Piece =
        [
            new("piece", static (piece, _) => Answer.Write(json => Answer.Piece(json, piece.Piece, piece.Notes, withNotes: true, piece.Words)), CallFlags.Showing | CallFlags.OpenFirst),
            new("language", static (piece, request) => piece.Language(request), CallFlags.Showing),
            new("phrases", static (piece, request) => Answer.Write(json => Answer.Phrases(json, piece.Levels, piece.Structure.Sections, LevelOf(request) ?? piece.Level, piece.Words, piece.Piece.Meter)), CallFlags.Showing | CallFlags.OpenFirst),
            new("phrases.edit", static (piece, request) => piece.EditPhrases(request)),
            new("notation", static (piece, request) => piece.Notation(request), CallFlags.Showing | CallFlags.OpenFirst),
            new("playback", static (piece, request) => piece.Playback(request), CallFlags.Showing | CallFlags.OpenFirst),
            new("tempo.set", static (piece, request) => piece.SetTempo(request)),
            new("roll", static (piece, request) => piece.Roll(request), CallFlags.Showing | CallFlags.OpenFirst),
            new("project.save", static (piece, request) => piece.Save(request)),
            new("reference.key", static (piece, request) => ReferenceRequest.Key(request, PieceDna.OpeningKey(piece.Notes, piece.Piece.Meter, piece.Piece.Keys, piece.Piece.End))),
        ];

        /// <summary>What the project keeps with the piece: resume points, ideas, settings, Check, pieces, tempo (the marks and bookmarks: <see cref="Marks"/>).</summary>
        public static readonly FacadeCall<OpenPiece>[] Project =
        [
            new("resume", static (piece, _) => Answer.Write(json => Answer.ResumePoints(json, piece.Project, piece.Piece.Meter)), CallFlags.OpenFirst),
            new("resume.save", static (piece, request) => piece.SaveResumePoint(request)),
            new("resume.rename", static (piece, request) => piece.Resume(project => project.ChangeResumePoint(request.Required("id"), point => point.Rename(request.Text("name"))))),
            new("resume.remove", static (piece, request) => piece.Resume(project => project.RemoveResumePoint(request.Required("id")))),
            new("ideas", static (piece, _) => piece.IdeasAnswer(), CallFlags.OpenFirst),
            new("idea.keep", static (piece, request) => piece.KeepIdea(request)),
            new("idea.rename", static (piece, request) => piece.Ideas(project => project.RenameObservation(request.Required("id"), request.Required("name")))),
            new("idea.note", static (piece, request) => piece.Ideas(project => project.ChangeObservationNote(request.Required("id"), request.Text("note")))),
            new("idea.remove", static (piece, request) => piece.Ideas(project => project.DeleteObservation(request.Required("id")))),
            new("idea.open", static (piece, request) => piece.OpenIdea(request)),
            new("settings", static (piece, _) => Answer.Write(json =>
            {
                Answer.SettingsCatalogue(json);
                Answer.Overrides(json, piece.Project);
            })),
            new("settings.override", static (piece, request) => piece.Change(project => project.OverrideSetting(request.Required("id"), request.Text("value")), json => Answer.Overrides(json, piece.Project))),
            new("project.metadata", static (piece, request) => piece.Change(project => MetadataRequest.Apply(project, request), json => Answer.Project(json, "project", piece.Project))),
            new("check", static (piece, _) => Answer.Write(piece.CheckAnswer), CallFlags.Showing | CallFlags.OpenFirst),
            new("check.resolve", static (piece, request) => piece.Change(project => PiecesRequest.Resolve(project, request), piece.CheckAnswer)),
            new("pieces.find", static (piece, request) => Answer.Write(json => PiecesRequest.Find(json, piece.Project, piece.Piece, piece.Notes, piece.Words, request))),
            new("pieces.save", static (piece, request) => piece.Change(project => PiecesRequest.Save(project, piece.Piece, piece.Words, request.Now(), request), json => PiecesRequest.Layer(json, piece.Project, piece.Piece))),
            new("tempo", static (piece, _) => Answer.Write(json => TempoRequest.Write(json, piece.Project, piece.Piece, piece.Words))),
        ];
    }
}