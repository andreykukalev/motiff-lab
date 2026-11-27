namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>A note of a variant: its onset in the bar, its pitch (MIDI) and its length within the bar, in ticks.</summary>
internal sealed record ScanVariantNote(long Onset, int Pitch, long Length);

/// <summary>One staff of a variant: the part and the staff in it, from 1, and the notes the engines read there.</summary>
internal sealed record ScanVariantStaff(int Part, int Staff, IReadOnlyList<ScanVariantNote> Notes);

/// <summary>
/// Another reading of a bar to check (ADR 0035, section 6; v1.3 i-2): what some engines read on the bar's staves to check,
/// the same on each of them, and not what the merge took there. Only the bar itself: never another bar of the piece.
/// </summary>
internal sealed record ScanVariant(IReadOnlyList<string> Engines, IReadOnlyList<ScanVariantStaff> Staves);

/// <summary>Where a bar is on its page: its box in the page image's pixels and the image it is in, as the evidence gives them.</summary>
internal sealed record ScanCrop(ScanBox Box, ScanImage Image);

internal static partial class ScanMerge
{
    /// <summary>
    /// The variants of a bar to check: each reading paired with the bar, grouped by its notes on the staves no two engines
    /// agree on (on <paramref name="all"/> its staves for a bar read again), in the merge's order; the group whose notes the
    /// merge took on all of them is left out.
    /// </summary>
    private static List<ScanVariant> Variants(List<(Reading Reading, int Measure)> candidates, List<Choice> choices, bool all)
    {
        Choice[] open = [.. choices.Where(choice => all || choice.Agreeing.Count < AgreeingEngines)];
        string Key(Reading reading, int measure) => string.Join("|", open.Select(choice => reading.Content(measure, choice.Part, choice.Staff)));
        string taken = string.Join("|", open.Select(choice => choice.Source.Content(choice.SourceMeasure, choice.Part, choice.Staff)));
        return [.. candidates
            .GroupBy(candidate => Key(candidate.Reading, candidate.Measure), StringComparer.Ordinal)
            .Where(group => !string.Equals(group.Key, taken, StringComparison.Ordinal))
            .Select(group =>
            {
                (Reading reading, int measure) = group.First();
                return new ScanVariant(
                    [.. group.Select(candidate => candidate.Reading.Engine)],
                    [.. open.Select(choice => new ScanVariantStaff(choice.Part + 1, choice.Staff,
                        [.. reading.Notes(measure, choice.Part, choice.Staff).Order().Select(note => new ScanVariantNote(note.Onset, note.Pitch, note.Length))]))]);
            })];
    }

    /// <summary>The bar's choices with the variant's staves taken from its first engine's reading of the bar; the others as they are.</summary>
    private static List<Choice> Chosen(List<(Reading Reading, int Measure)> candidates, List<Choice> choices, ScanVariant variant)
    {
        (Reading reading, int measure) = candidates.FirstOrDefault(candidate => candidate.Reading.Engine == variant.Engines[0]);
        return reading is null
            ? throw new ArgumentException($"No engine {variant.Engines[0]} read the bar.", nameof(variant))
            : [.. choices.Select(choice => variant.Staves.Any(staff => staff.Part == choice.Part + 1 && staff.Staff == choice.Staff)
                ? choice with { Source = reading, SourceMeasure = measure }
                : choice)];
    }

    /// <summary>The staff spaces a crop reaches above the system's top line and below its bottom one: ledger lines, stems and beams.</summary>
    private const double CropSpaces = 4;

    internal sealed partial class Page
    {
        /// <summary>
        /// Where the backbone's bar is on the page: its box reaching <see cref="CropSpaces"/> staff spaces (its staves' widest)
        /// above and below its system's lines, inside the image; null when it was not found there or the evidence gives no box or image.
        /// </summary>
        public ScanCrop? CropOf(int measure)
        {
            if (_bars[measure] is not int bar || _evidence.Bars[bar].Box is not { } box || _evidence.Image is not { } image)
            {
                return null;
            }

            double reach = CropSpaces * _evidence.Bars[bar].Spaces.Values.DefaultIfEmpty(0).Max();
            return new ScanCrop(
                new ScanBox(Math.Clamp(box.Left, 0, image.Width), Math.Clamp(box.Top - reach, 0, image.Height), Math.Clamp(box.Right, 0, image.Width), Math.Clamp(box.Bottom + reach, 0, image.Height)),
                image);
        }
    }
}