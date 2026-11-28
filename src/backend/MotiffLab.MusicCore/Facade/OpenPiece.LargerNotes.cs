using System.Text.Json;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>«Ноты крупнее», the step the player chooses for the piece (version 1.7, project file 19; contract 17, calls added).</summary>
internal sealed partial class OpenPiece
{
    /// <summary>
    /// "step": 0 (the A4 layout as it is) to <see cref="Projects.LargerNotes.MaxStep"/>; the project keeps it
    /// ("largerNotes"), saved with the piece by project.save. Nothing else of the piece changes: the layout is the app's.
    /// </summary>
    private string SetLargerNotes(Request request)
    {
        int step = request.RequiredInt("step");
        if (!Projects.LargerNotes.IsValid(step))
        {
            throw FacadeException.BadRequest($"\"step\" is 0 to {Projects.LargerNotes.MaxStep}.");
        }

        Project = Project.WithLargerNotes(step);
        return Answer.Write(LargerNotesAnswer);
    }

    /// <summary>"step" as the project keeps it, "percent" the notes' size it gives (100 at step 0) and "maxStep", the largest.</summary>
    private void LargerNotesAnswer(Utf8JsonWriter json)
    {
        json.WriteNumber("step", Project.LargerNotes);
        json.WriteNumber("percent", Projects.LargerNotes.Percent(Project.LargerNotes));
        json.WriteNumber("maxStep", Projects.LargerNotes.MaxStep);
    }
}