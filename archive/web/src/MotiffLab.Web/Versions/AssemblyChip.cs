namespace MotiffLab.Web.Versions;

/// <summary>
/// A source's chip in the pill while comparing (decision record 0024, section 6; the mockups' one row of chips): the name
/// shows the source, ▶ hears it.
/// </summary>
/// <param name="Id">The source: "notes", "before" or a version's id.</param>
/// <param name="Tone">"graphite", "plum", "teal", "amber" or "green", as its line in «Происхождение».</param>
/// <param name="Detail">What is selected in it: «9 нот»; null with no selection.</param>
/// <param name="Key">The digit that shows it, from 1.</param>
public sealed record AssemblyChip(string Id, string Name, string Tone, string? Detail, int Key, bool Shown, bool Heard);