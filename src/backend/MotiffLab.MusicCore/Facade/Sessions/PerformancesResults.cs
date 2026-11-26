using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>A version added over the piece: the project with it (for a scanned piece, with the Check flags it gives) and the version as kept.</summary>
internal sealed record AddedVersion(Project Project, SavedPerformance Performance);