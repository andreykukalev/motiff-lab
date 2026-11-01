using System.Diagnostics;
using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore;
using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.CoreBridge;

/// <summary>
/// Runs the same music-core pipeline the web app runs when a piece opens and a phrase is picked
/// (PlaybackLab.razor): import, phrases, analysis of every phrase, the Try experiments of the
/// first phrase, the notation of the whole piece and a project file round trip. It times each
/// stage so the native build can be compared with the web build.
/// </summary>
public static class CoreBenchmark
{
    private static readonly string[] s_stages = ["import", "phrases", "analysis", "experiments", "notation", "projectFile"];

    public static string Run(string fileName, byte[] data, int iterations, Words words)
    {
        iterations = Math.Clamp(iterations, 1, 200);
        var times = s_stages.ToDictionary(stage => stage, _ => new List<double>());
        PipelineCounts counts = default;
        var total = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            counts = RunOnce(fileName, data, words, times);
        }

        total.Stop();
        return Json(json =>
        {
            json.WriteString("runtime", RuntimeDescription());
            json.WriteString("file", fileName);
            json.WriteNumber("bytes", data.Length);
            json.WriteNumber("iterations", iterations);
            json.WriteNumber("notes", counts.Notes);
            json.WriteNumber("bars", counts.Bars);
            json.WriteNumber("phrases", counts.Phrases);
            json.WriteNumber("facts", counts.Facts);
            json.WriteNumber("experiments", counts.Experiments);
            json.WriteNumber("variationNotes", counts.VariationNotes);
            json.WriteNumber("meiBytes", counts.MeiBytes);
            json.WriteStartObject("stagesMs");
            double cold = 0, warm = 0;
            foreach (string stage in s_stages)
            {
                List<double> runs = times[stage];
                double first = runs[0];
                double median = Median(runs.Count > 1 ? runs.Skip(1) : runs);
                cold += first;
                warm += median;
                json.WriteStartObject(stage);
                json.WriteNumber("cold", Math.Round(first, 2));
                json.WriteNumber("median", Math.Round(median, 2));
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteNumber("coldTotalMs", Math.Round(cold, 2));
            json.WriteNumber("warmTotalMs", Math.Round(warm, 2));
            json.WriteNumber("wallMs", Math.Round(total.Elapsed.TotalMilliseconds, 2));
        });
    }

    /// <summary>The phrases of a piece as JSON: the shape a native screen would read from music-core.</summary>
    public static string Phrases(string fileName, byte[] data)
    {
        ImportedPiece piece = Import(fileName, data);
        PhraseMap map = PhraseMap.Suggest(piece.Notes, piece.Meter, piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, piece.End);
        return Json(json =>
        {
            json.WriteNumber("notes", piece.Notes.Count);
            json.WriteStartArray("phrases");
            foreach (Phrase phrase in map.Phrases)
            {
                json.WriteStartObject();
                json.WriteNumber("number", phrase.Number);
                json.WriteNumber("firstBar", phrase.FirstBar);
                json.WriteNumber("lastBar", phrase.LastBar);
                json.WriteNumber("onset", phrase.Span.Onset.Value);
                json.WriteNumber("end", phrase.Span.End.Value);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    /// <summary>
    /// The notation of the whole piece as MEI, the text the score view hands to Verovio, with the
    /// time it took. The MEI is a JSON string so the result stays one JSON object.
    /// </summary>
    public static string Mei(string fileName, byte[] data)
    {
        var watch = Stopwatch.StartNew();
        ImportedPiece piece = Import(fileName, data);
        GeneratedNotation notation = piece.MusicXml is { } xml
            ? NotationGenerator.FromMusicXml(xml, Path.GetFileNameWithoutExtension(fileName))
            : NotationGenerator.FromMidi(piece.Midi!, Path.GetFileNameWithoutExtension(fileName));
        double milliseconds = watch.Elapsed.TotalMilliseconds;
        return Json(json =>
        {
            json.WriteNumber("notes", notation.NoteCount);
            json.WriteNumber("ms", Math.Round(milliseconds, 2));
            json.WriteString("mei", notation.Mei);
        });
    }

    public static string Error(Exception error) => Json(json =>
    {
        json.WriteString("error", error.GetType().Name);
        json.WriteString("message", error.Message);
    });

    public static string RuntimeDescription() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
        $"{System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}; " +
        (OperatingSystem.IsBrowser() ? "WebAssembly interpreter"
            : System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT");

    private static PipelineCounts RunOnce(string fileName, byte[] data, Words words, Dictionary<string, List<double>> times)
    {
        var watch = Stopwatch.StartNew();
        ImportedPiece piece = Import(fileName, data);
        Lap(times, "import", watch);

        PhraseMap phrases = PhraseMap.Suggest(piece.Notes, piece.Meter, piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, piece.End);
        Lap(times, "phrases", watch);

        int facts = 0;
        InvitationContext? first = null;
        foreach (Phrase phrase in phrases.Phrases)
        {
            InvitationContext context = Analyse(piece, phrases, phrase, words);
            facts += context.Motif.Facts.Count + context.Structure.Facts.Count + context.Character.Count;
            first ??= context;
        }

        Lap(times, "analysis", watch);

        int experiments = 0, variationNotes = 0;
        if (first is not null)
        {
            IReadOnlyList<Experiment> offered = ExperimentCatalogue.Offer(first);
            experiments = offered.Count;
            foreach (Experiment experiment in offered)
            {
                VariationChain chain = ExperimentCatalogue.Start(experiment, 0, first);
                variationNotes += chain.Current.Count;
            }
        }

        Lap(times, "experiments", watch);

        GeneratedNotation? notation = piece.MusicXml is { } xml
            ? NotationGenerator.FromMusicXml(xml, "Benchmark")
            : piece.Midi is { } midi ? NotationGenerator.FromMidi(midi, "Benchmark") : null;
        Lap(times, "notation", watch);

        Project project = Project.Create("benchmark", fileName, data, new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero));
        Project read = ProjectFile.Read(ProjectFile.Write(project));
        Lap(times, "projectFile", watch);
        if (read.Source.Length != data.Length)
        {
            throw new InvalidOperationException("The project file did not keep the source.");
        }

        return new PipelineCounts(
            piece.Notes.Count,
            piece.Meter.BarAt(piece.End).Number,
            phrases.Phrases.Count,
            facts,
            experiments,
            variationNotes,
            notation is null ? 0 : Encoding.UTF8.GetByteCount(notation.Mei));
    }

    private static InvitationContext Analyse(ImportedPiece piece, PhraseMap phrases, Phrase phrase, Words words)
    {
        TickSpan span = MotifSelector.Bars(piece.Meter, phrase.FirstBar, Math.Min(phrase.LastBar, phrase.FirstBar + 7), piece.End);
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, span, SelectionFilter.All);
        KeySignature? key = KeySignature.InForce(piece.Keys, selection.Span.Onset);
        MotifAnalysis motif = MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, key, piece.MusicXml?.AccentMarks, phrases.Boundaries, words);
        StructureAnalysis structure = StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, phrases.Boundaries, words);
        IReadOnlyList<MotifFact> character = MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, key, CharacterContext.FromPiece(piece), words);
        return new InvitationContext(piece.Notes, piece.Meter, selection, motif, structure, character, words);
    }

    private static ImportedPiece Import(string fileName, byte[] data)
    {
        PieceImportResult result = PieceImporter.Import(fileName, data);
        return result.Piece ?? throw new InvalidOperationException($"The piece was not imported: {result.Rejection?.Reason}.");
    }

    private static void Lap(Dictionary<string, List<double>> times, string stage, Stopwatch watch)
    {
        times[stage].Add(watch.Elapsed.TotalMilliseconds);
        watch.Restart();
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string Json(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            body(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private readonly record struct PipelineCounts(int Notes, int Bars, int Phrases, int Facts, int Experiments, int VariationNotes, int MeiBytes);
}