using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>What the player kept about the piece's phrases (#78): the ideas.</summary>
public sealed partial record Project
{
    /// <summary>A new idea's id from <paramref name="ids"/>, one no idea of the file has, known to this build or not.</summary>
    public string NextObservationId(IdSource ids) => ids.Next("o_", Observations.Select(observation => observation.Id).Concat(UnknownIds(Unknown, "observations")));

    /// <summary>What the player kept about the piece's phrases (#78), the ideas, in the order it was kept.</summary>
    public IReadOnlyList<Observation> Observations { get; init; } = [];

    /// <summary>The observations about the phrase, in the order they were kept (<see cref="Observation.IsAbout"/>).</summary>
    internal IReadOnlyList<Observation> ObservationsAbout(TickSpan phrase) => [.. Observations.Where(observation => observation.IsAbout(phrase))];

    /// <summary>The project with the observation kept after the others.</summary>
    /// <exception cref="ArgumentException">An observation with the same id is already kept.</exception>
    public Project Keep(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return Observations.Any(kept => kept.Id == observation.Id)
            ? throw new ArgumentException($"The project already keeps an observation {observation.Id}.", nameof(observation))
            : this with { Observations = [.. Observations, observation] };
    }

    /// <summary>The project with one observation renamed; the others stay as they are.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    /// <exception cref="KeyNotFoundException">No observation has the id.</exception>
    public Project RenameObservation(string id, string name)
    {
        Observation renamed = FindObservation(id).Rename(name);
        return this with { Observations = [.. Observations.Select(kept => kept.Id == id ? renamed : kept)] };
    }

    /// <summary>The project without the observation; the others keep their order.</summary>
    /// <exception cref="KeyNotFoundException">No observation has the id.</exception>
    public Project DeleteObservation(string id)
    {
        _ = FindObservation(id);
        return this with { Observations = [.. Observations.Where(kept => kept.Id != id)] };
    }

    /// <summary>The project with the observation's note changed to <paramref name="note"/>; blank takes the note away.</summary>
    /// <exception cref="KeyNotFoundException">No observation has the id.</exception>
    /// <exception cref="ArgumentException">The note is blank and the observation keeps no variation.</exception>
    public Project ChangeObservationNote(string id, string? note)
    {
        Observation changed = FindObservation(id).WithNote(note);
        return this with { Observations = [.. Observations.Select(kept => kept.Id == id ? changed : kept)] };
    }

    private Observation FindObservation(string id) =>
        Observations.FirstOrDefault(observation => observation.Id == id) ?? throw new KeyNotFoundException($"The project keeps no observation {id}.");
}