using MotiffLab.MusicCore.Performance;

namespace MotiffLab.MusicCore.Projects;

/// <summary>Where another version's notes come from (decision record 0022, section 1).</summary>
public enum PerformanceSource
{
    /// <summary>Heard in the player's own recording by transcription; the recording's sound stays on the device.</summary>
    Recording,

    /// <summary>A MIDI file, such as a cover performer's MIDI or one played in on a digital piano.</summary>
    Midi,

    /// <summary>A written score, such as an official edition in MusicXML, kept as MIDI through its tempo marks.</summary>
    Score,
}

/// <summary>
/// A performance kept with a piece (decision record 0022, section 8; project file version 13): its notes as MIDI,
/// the anchors that line it up with the score, the bars the player said are not a difference, and the bars taken
/// into the score.
/// </summary>
/// <param name="Id">Stable within the project, for example "perf_1".</param>
/// <param name="Name">What the player calls it, for example "Live at the National Gallery".</param>
/// <param name="Midi">The notes: the transcription engine's MIDI or the player's MIDI file, as it was.</param>
/// <param name="RecordingStart">Where the stretch that was transcribed starts in the recording, in microseconds; 0 for a MIDI file or a whole recording.</param>
public sealed record SavedPerformance(string Id, string Name, PerformanceSource Source, byte[] Midi, long RecordingStart, IReadOnlyList<Anchor> Anchors)
{
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A performance needs an id.", nameof(Id));

    /// <summary>Trimmed; a rename goes through the same check.</summary>
    public string Name { get; init => field = Named(value); } = Named(Name);

    public long RecordingStart { get; } = RecordingStart >= 0 ? RecordingStart : throw new ArgumentOutOfRangeException(nameof(RecordingStart), RecordingStart, "A recording starts at 0 or later.");

    /// <summary>The engine that heard a recording, for example "transkun 2.0.1"; null for a MIDI file.</summary>
    public string? Engine { get; init; }

    /// <summary>Bars the player said are not a difference: their markers stay away.</summary>
    public IReadOnlyList<int> Dismissed { get; init; } = [];

    /// <summary>Bars taken into the score, in bar order.</summary>
    public IReadOnlyList<TakenBar> Taken { get; init; } = [];

    /// <summary>The notes, at their times in the recording.</summary>
    public IReadOnlyList<PlayedNote> Notes() => PlayedNotes.FromMidi(Midi, RecordingStart);

    /// <summary>The alignment from the kept anchors.</summary>
    public PerformanceAlignment Alignment() => PerformanceAlignment.FromAnchors(Anchors);

    /// <summary>The performance with a bar marked "not a difference", or the mark taken back.</summary>
    public SavedPerformance Dismiss(int bar, bool dismissed = true) =>
        this with { Dismissed = dismissed ? [.. Dismissed.Append(bar).Distinct().Order()] : [.. Dismissed.Where(kept => kept != bar)] };

    /// <summary>The performance with bars taken, replacing what was taken from the same bars before.</summary>
    public SavedPerformance TakeBars(IEnumerable<TakenBar> bars)
    {
        TakenBar[] taking = [.. bars];
        HashSet<int> numbers = [.. taking.Select(bar => bar.Bar)];
        return this with { Taken = [.. Taken.Where(bar => !numbers.Contains(bar.Bar)).Concat(taking).OrderBy(bar => bar.Bar)] };
    }

    /// <summary>The performance with the print restored in bars taken before.</summary>
    public SavedPerformance Restore(IEnumerable<int> bars)
    {
        HashSet<int> numbers = [.. bars];
        return this with { Taken = [.. Taken.Where(bar => !numbers.Contains(bar.Bar))] };
    }

    private static string Named(string name) => !string.IsNullOrWhiteSpace(name) ? name.Trim() : throw new ArgumentException("A performance needs a name.", nameof(name));

    public bool Equals(SavedPerformance? other) =>
        other is not null && Id == other.Id && Name == other.Name && Source == other.Source && Midi.AsSpan().SequenceEqual(other.Midi)
        && RecordingStart == other.RecordingStart && Anchors.SequenceEqual(other.Anchors) && Engine == other.Engine
        && Dismissed.SequenceEqual(other.Dismissed) && Taken.Count == other.Taken.Count
        && Taken.Zip(other.Taken).All(pair => pair.First.Bar == pair.Second.Bar && pair.First.Removed.SequenceEqual(pair.Second.Removed) && pair.First.Added.SequenceEqual(pair.Second.Added));

    public override int GetHashCode() => HashCode.Combine(Id, Name, Source, Midi.Length, Anchors.Count, Taken.Count);
}