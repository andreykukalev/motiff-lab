namespace MotiffLab.MusicCore.Pieces;

/// <summary>
/// One original, or a part of it, that Notes were made from (decision record 0030, section 2): its id in the piece's
/// originals (or in another piece's, with <see cref="Project"/>), its fingerprint when the notes were made, and the pages or
/// the stretch of a recording used.
/// </summary>
public sealed record ProvenanceSource
{
    public ProvenanceSource(string original)
    {
        Original = !string.IsNullOrWhiteSpace(original) ? original : throw new ArgumentException("A source names its original.", nameof(original));
    }

    /// <summary>The original's id: in this piece's originals, or in <see cref="Project"/>'s when that is set.</summary>
    public string Original { get; }

    /// <summary>The original's fingerprint when the notes were made from it (64 lower-case hex digits); null when not known.</summary>
    public string? Sha256
    {
        get;
        init => field = value is null || (value.Length == 64 && value.All(char.IsAsciiHexDigitLower)) ? value : throw new ArgumentException("A fingerprint is 64 lower-case hex digits.", nameof(Sha256));
    }

    /// <summary>The pages used, from 1, both ends included; null for the whole original or a recording.</summary>
    public (int First, int Last)? Pages
    {
        get;
        init => field = value is not var (first, last) || (first >= 1 && last >= first) ? value : throw new ArgumentOutOfRangeException(nameof(Pages));
    }

    /// <summary>The stretch of a recording used, in milliseconds from its start, from before to; null for the whole of it or pages.</summary>
    public (long From, long To)? Milliseconds
    {
        get;
        init => field = value is not var (from, to) || (from >= 0 && to > from) ? value : throw new ArgumentOutOfRangeException(nameof(Milliseconds));
    }

    /// <summary>The piece whose original it is, when it is not this piece's (notes read from a PDF piece, 2026-11-05 to 07); null otherwise.</summary>
    public string? Project { get; init; }
}

/// <summary>
/// Where Notes came from (decision record 0030, section 2; project file version 16): the operation, the originals and
/// other Notes it took, the engine and its version, what changed its result, and when. One shape for every Notes and for
/// a kept variation, so a later operation (the assistant's, with its request and model in <see cref="ParamsJson"/>) needs
/// no change to the format: an operation this build does not know is kept and written back as it came.
/// </summary>
/// <param name="Op">The operation's name: one of the constants here or a later build's own.</param>
public sealed record NotesProvenance(string Op)
{
    /// <summary>A PDF or photos of pages read by a scan engine.</summary>
    public const string PdfRecognition = "pdfRecognition";

    /// <summary>A recording heard by a transcription engine.</summary>
    public const string AudioRecognition = "audioRecognition";

    /// <summary>A MIDI or MusicXML file taken as it is.</summary>
    public const string Import = "import";

    /// <summary>Notes the player entered by hand, with no original.</summary>
    internal const string Manual = "manual";

    /// <summary>Notes assembled from other Notes (0024).</summary>
    public const string Assembly = "assembly";

    /// <summary>Notes cut from other Notes, such as a piece of a collection.</summary>
    public const string Split = "split";

    /// <summary>
    /// Notes music-core made by its own rules, with no AI: a variant of a phrase from a «Что если» chip, or a bridge candidate
    /// of a draft's seam (<c>DraftBridges</c>); what made them in the params.
    /// </summary>
    public const string Variation = "variation";

    /// <summary>Notes or a variation the assistant proposed and the player accepted (0028; research 4.6), with its request and model in the params.</summary>
    public const string Assistant = "assistant";

    public string Op { get; } = IsName(Op) ? Op : throw new ArgumentException("An operation is a name: ASCII letters and digits, from a letter.", nameof(Op));

    /// <summary>The originals the notes were made from, the one they were made from first; empty when not known.</summary>
    public IReadOnlyList<ProvenanceSource> From { get; init => field = [.. value]; } = [];

    /// <summary>The Notes these were made from, by id: only notes made from other Notes.</summary>
    public IReadOnlyList<string> Inputs
    {
        get;
        init => field = value.All(static input => !string.IsNullOrWhiteSpace(input)) ? [.. value] : throw new ArgumentException("An input names Notes.", nameof(Inputs));
    } = [];

    /// <summary>The algorithm and its version as it reported them, "homr 0.7.0"; null when there is none.</summary>
    public string? Engine { get; init => field = value is null || value.Trim().Length > 0 ? value : throw new ArgumentException("An engine has a name.", nameof(Engine)); }

    /// <summary>What changed the result, as the file holds it: a JSON object kept and written back as it is; null when there is none.</summary>
    public string? ParamsJson { get; init; }

    /// <summary>When the notes were made; null when not known.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>The fields of the provenance this build does not know, kept and written back (0026 S6).</summary>
    public IReadOnlyList<UnknownField> Unknown { get; init; } = [];

    /// <summary>Whether the operation read an original with an engine: a scan or a transcription.</summary>
    public bool IsRecognition => Op is PdfRecognition or AudioRecognition;

    /// <summary>The original the notes were made from in this piece: the first source that is not another piece's; null when none.</summary>
    public ProvenanceSource? Original => From.FirstOrDefault(static source => source.Project is null);

    /// <summary>
    /// The provenance kept for notes, against <paramref name="derived"/>, the one the notes' own data shows now (their scan,
    /// transcription or import): the kept one while it still tells of the same operation (the same kind, original, engine and
    /// time; for notes not recognised, any provenance that is not a recognition), with the fingerprints it lacks filled in
    /// from <paramref name="derived"/>; otherwise <paramref name="derived"/>, as for notes made again.
    /// </summary>
    public static NotesProvenance Kept(NotesProvenance? kept, NotesProvenance derived)
    {
        ArgumentNullException.ThrowIfNull(derived);
        if (kept is null || !kept.TellsOf(derived))
        {
            return derived;
        }

        return kept with
        {
            From = [.. kept.From.Select(source => source.Sha256 is null && derived.From.FirstOrDefault(each => each.Original == source.Original && each.Project == source.Project) is { Sha256: { } sha }
                ? source with { Sha256 = sha }
                : source)],
        };
    }

    private bool TellsOf(NotesProvenance derived) =>
        derived.IsRecognition
            ? Op == derived.Op && Engine == derived.Engine && At == derived.At && Original?.Original == derived.Original?.Original
              && From.FirstOrDefault(static source => source.Project is not null)?.Project == derived.From.FirstOrDefault(static source => source.Project is not null)?.Project
            : !IsRecognition;

    /// <summary>Whether a text is a name: ASCII letters and digits, from a letter.</summary>
    internal static bool IsName(string? text) => !string.IsNullOrEmpty(text) && char.IsAsciiLetter(text[0]) && text.All(char.IsAsciiLetterOrDigit);

    public bool Equals(NotesProvenance? other) =>
        other is not null && Op == other.Op && From.SequenceEqual(other.From) && Inputs.SequenceEqual(other.Inputs, StringComparer.Ordinal)
        && Engine == other.Engine && ParamsJson == other.ParamsJson && At == other.At && Unknown.SequenceEqual(other.Unknown);

    public override int GetHashCode() => HashCode.Combine(Op, From.Count, Engine, At);
}