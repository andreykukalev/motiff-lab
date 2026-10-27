namespace MotiffLab.MusicCore.Projects;

/// <summary>What the player sat down to do in a session, which decides where the workspace opens.</summary>
public enum SessionIntent
{
    /// <summary>Open the piece and play: the workspace opens on Practise.</summary>
    Open,

    /// <summary>Play, loop and slow down a phrase.</summary>
    Practise,

    /// <summary>Find out what is happening in a passage and why.</summary>
    Understand,

    /// <summary>Try what else a passage could become.</summary>
    Explore,
}

/// <summary>
/// A work session on a piece (docs/decisions/0012-workspace-structure.md): one task the player came
/// with, the passage they are working on, and when. It is not a copy of the score: it refers to the
/// project's piece, and what the player keeps during it stays in the project's observations, marked
/// with the session's id, so the ideas outlive the session.
/// </summary>
/// <remarks>Times are passed in, never read from a clock, so building a session is pure.</remarks>
/// <param name="Id">Stable for the life of the project, for example "s_0001".</param>
/// <param name="Task">The task in the player's words, trimmed, or null to be named by the intent.</param>
/// <param name="Passage">The passage the player last worked on in this session, or null before they selected one.</param>
public sealed record Session(
    string Id,
    SessionIntent Intent,
    string? Task,
    SavedPassage? Passage,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActive)
{
    /// <summary>A session just started: no passage yet, active now. A blank task is none.</summary>
    /// <exception cref="ArgumentException">The id is blank.</exception>
    public static Session Start(string id, SessionIntent intent, string? task, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new(id, intent, Trimmed(task), null, now, now);
    }

    /// <summary>The session with the passage the player works on now, active at <paramref name="now"/>.</summary>
    public Session Touch(SavedPassage? passage, DateTimeOffset now) => this with { Passage = passage, LastActive = now };

    /// <summary>The session with another task; blank takes the task away, so the intent names it.</summary>
    public Session Rename(string? task) => this with { Task = Trimmed(task) };

    private static string? Trimmed(string? task) => string.IsNullOrWhiteSpace(task) ? null : task.Trim();
}