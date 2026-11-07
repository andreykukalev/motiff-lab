using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// A version removed while the piece is open, put back where it was («Версия убрана · Вернуть»). The merge draft of
/// contract 7 that lived here gave way to the assembly (0024, contract 9; OpenPiece.Assembly.cs).
/// </summary>
internal sealed partial class OpenPiece
{
    /// <summary>The version removed last, where it was and the Check flags performances had: "Версия убрана · Вернуть".</summary>
    private (SavedPerformance Performance, int Index, IReadOnlyList<CheckFlag> Flags)? _removedVersion;

    /// <summary>A version removed last put back where it was, with its taken bars and Check flags.</summary>
    private string PutBackVersion()
    {
        if (_removedVersion is not { } removed)
        {
            throw FacadeException.Unavailable("No version was removed while the piece is open.");
        }

        Project = Project.PutBackPerformance(removed.Performance, removed.Index, removed.Flags);
        _removedVersion = null;
        if (removed.Performance.Taken.Count > 0)
        {
            RefreshNotes();
        }

        return PerformanceAnswer(removed.Performance);
    }
}