using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Web.Observations;

/// <summary>One kept observation as the list shows it.</summary>
/// <param name="Where">The bars its variation changes, or for a note alone the bars it is about: "bar 1", "the pickup and bars 1–4".</param>
/// <param name="Source">Where the variation came from: "an experiment", "your steps", or "a note" for a note alone.</param>
/// <param name="Heard">Whether it is the variation the page has now, so opening it again would change nothing.</param>
public sealed record ObservationRow(Observation Observation, string Where, string Source, bool Heard);

/// <summary>A new name or note for a kept observation, from the list.</summary>
public sealed record ObservationText(string Id, string Text);