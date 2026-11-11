namespace MotiffLab.Acceptance.Tests;

/// <summary>
/// The scenarios that fail on main because of a product defect (README.md, "Known defects"). Each one is skipped only
/// after Andrei agreed; the task that fixes the defect removes its Skip, so the scenario then guards the fix.
/// </summary>
internal static class KnownDefect
{
    public const string NewerFileMessageInRussian =
        "Known defect (#310): the refusal of a newer project file is in English only. Skipped until a fix task turns it on.";
}