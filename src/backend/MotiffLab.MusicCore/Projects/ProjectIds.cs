using System.Globalization;

namespace MotiffLab.MusicCore.Projects;

/// <summary>A project's numbered id and the id it is re-keyed to (0026 S3 and C4, #317); the ids it had are <see cref="Project.FormerIds"/>.</summary>
internal static class ProjectIds
{
    /// <summary>
    /// Whether a project's id is numbered, "p_" and digits, as earlier builds made them by a counter in each run ("p_0001"):
    /// two different pieces, on two devices or on one, can carry the same one (V8).
    /// </summary>
    internal static bool IsNumbered(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Length > 2 && id.StartsWith("p_", StringComparison.Ordinal) && !id.AsSpan(2).ContainsAnyExceptInRange('0', '9');
    }

    /// <summary>
    /// The project under the id its numbered one is re-keyed to (0026 C4), the numbered one kept last in
    /// <see cref="Project.FormerIds"/>; the project itself when its id is not numbered. The new id is "p_" and the first 32
    /// hex digits of the SHA-256 of "{numbered id}|{createdAt}|{fingerprint}" in UTF-8, createdAt as the file writes it
    /// (ISO 8601, "O") and the fingerprint the SHA-256 of the working notes' music, else of the PDF, else the one the PDF's
    /// description keeps, else of no bytes, as 64 lowercase hex digits: the same file re-keyed on two devices gets one id, two
    /// different pieces that carried one numbered id get two, and re-keying again changes nothing.
    /// </summary>
    internal static Project Rekeyed(this Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!IsNumbered(project.Id))
        {
            return project;
        }

        string fingerprint = project.HasNotes ? PieceOriginal.Fingerprint(project.Source)
            : project.Document is { } pdf ? PieceOriginal.Fingerprint(pdf)
            : project.FindOriginal(PieceOriginal.DocumentId)?.Sha256 ?? PieceOriginal.Fingerprint([]);
        string key = string.Create(CultureInfo.InvariantCulture, $"{project.Id}|{project.CreatedAt:O}|{fingerprint}");
        return project with { Id = IdSource.Hashed("p_", key, []), FormerIds = [.. project.FormerIds, project.Id] };
    }
}