using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import;

/// <summary>
/// The import step of N2: recognises a dropped or picked file by its content, runs the matching
/// importer, and gives either the piece with its report or a plain reason and a tip (S1-I1, S1-I8).
/// </summary>
/// <remarks>
/// <para>
/// The content decides the format, not the file name, so a renamed PDF, image or recording gets a
/// reason that names what it is. The name only routes bytes that look like neither MIDI nor XML to
/// the importer it claims, so that importer can say what is wrong with them.
/// </para>
/// <para>
/// The import is pure and deterministic: the same name and bytes give the same result.
/// </para>
/// </remarks>
public static class PieceImporter
{
    /// <summary>The Stage 1 file size limit (S1-I1), the same for every format.</summary>
    public const int MaxFileBytes = MidiImporter.MaxFileBytes;

    /// <summary>The extensions the file picker offers.</summary>
    public const string AcceptedExtensions = ".mid,.midi,.musicxml,.mxl,.xml";

    private const string NotationTip = "Motiff Lab reads MIDI and MusicXML; most notation apps can export MusicXML.";

    private const string MidiDamagedTip = "Export the MIDI file again from the app that made it, or export MusicXML instead.";

    private const string RussianNotationTip = "Motiff Lab читает MIDI и MusicXML; большинство нотных редакторов умеют сохранять MusicXML.";

    private const string RussianMidiDamagedTip = "Сохраните файл MIDI заново в программе, где он сделан, или сохраните вместо него MusicXML.";

    private const string RussianMusicXmlTip = "Откройте партитуру в нотном редакторе и сохраните её в MusicXML ещё раз.";

    private static readonly byte[] s_png = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] s_jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] s_utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] s_utf16LittleEndianBom = [0xFF, 0xFE];
    private static readonly byte[] s_utf16BigEndianBom = [0xFE, 0xFF];

    /// <summary>A rejection for a file that is too large, so the caller need not read it first.</summary>
    public static ImportRejection? CheckSize(long bytes) => bytes switch
    {
        0 => Rejection(ImportRejectionReason.Empty),
        > MaxFileBytes => Rejection(ImportRejectionReason.TooLarge),
        _ => null,
    };

    public static PieceImportResult Import(string fileName, ReadOnlySpan<byte> data)
    {
        if (CheckSize(data.Length) is { } rejection)
        {
            return new(null, rejection);
        }

        SourceFormat? format = Recognise(fileName, data, out ImportRejectionReason? notMusic);
        if (format is null)
        {
            return new(null, Rejection(notMusic ?? ImportRejectionReason.NotMusic));
        }

        bool midiHeader = data.StartsWith("MThd"u8);
        try
        {
            ImportedPiece piece = format == SourceFormat.Midi ? FromMidi(fileName, data) : FromMusicXml(fileName, data);
            return piece.Notes.Count == 0
                ? new(null, Rejection(ImportRejectionReason.NoNotes))
                : new(piece, null);
        }
        catch (MidiImportException exception)
        {
            return new(null, exception.Failure switch
            {
                MidiImportFailure.NotMidi when midiHeader => new(ImportRejectionReason.Damaged, "This MIDI file breaks off inside its header.", MidiDamagedTip)
                {
                    RussianMessage = "Этот файл MIDI обрывается внутри заголовка.",
                    RussianTip = RussianMidiDamagedTip,
                },
                MidiImportFailure.NotMidi => Rejection(ImportRejectionReason.NotMusic, exception.Message),
                MidiImportFailure.Unsupported => new(ImportRejectionReason.Unsupported, exception.Message, "Export the piece again as a type 1 MIDI file, or as MusicXML.")
                {
                    RussianMessage = "Motiff Lab не читает этот вид файлов MIDI.",
                    RussianTip = "Сохраните пьесу заново как файл MIDI типа 1 или как MusicXML.",
                },
                MidiImportFailure.TooLarge => Rejection(ImportRejectionReason.TooLarge),
                _ => new(ImportRejectionReason.Damaged, exception.Message, MidiDamagedTip)
                {
                    RussianMessage = "Этот файл MIDI повреждён, и Motiff Lab не может его прочитать.",
                    RussianTip = RussianMidiDamagedTip,
                },
            });
        }
        catch (MusicXmlImportException exception)
        {
            return new(null, exception.Failure switch
            {
                MusicXmlImportFailure.NotMusicXml => Rejection(ImportRejectionReason.NotMusic, exception.Message),
                MusicXmlImportFailure.Unsupported => new(ImportRejectionReason.Unsupported, exception.Message, "Open the score in a notation app and export it as MusicXML again.")
                {
                    RussianMessage = "Motiff Lab не читает этот вид файлов MusicXML.",
                    RussianTip = RussianMusicXmlTip,
                },
                MusicXmlImportFailure.TooLarge => Rejection(ImportRejectionReason.TooLarge),
                _ => new(ImportRejectionReason.Damaged, exception.Message, "Open the score in a notation app and export it as MusicXML again.")
                {
                    RussianMessage = "Этот файл MusicXML повреждён, и Motiff Lab не может его прочитать.",
                    RussianTip = RussianMusicXmlTip,
                },
            });
        }
    }

    /// <summary>MIDI or MusicXML by content, then by name; otherwise null with what the file looks like.</summary>
    private static SourceFormat? Recognise(string fileName, ReadOnlySpan<byte> data, out ImportRejectionReason? notMusic)
    {
        notMusic = null;
        if (data.StartsWith("MThd"u8) || (data.StartsWith("RIFF"u8) && data.Length >= 12 && data[8..12].SequenceEqual("RMID"u8)))
        {
            return SourceFormat.Midi;
        }

        if (data.StartsWith("PK\u0003\u0004"u8) || LooksLikeXml(data))
        {
            return SourceFormat.MusicXml;
        }

        notMusic = Sniff(data);
        if (notMusic is not null)
        {
            return null;
        }

        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".mid" or ".midi" => SourceFormat.Midi,
            ".musicxml" or ".mxl" or ".xml" => SourceFormat.MusicXml,
            _ => null,
        };
    }

    /// <summary>Common files that are not notation, by their signatures.</summary>
    private static ImportRejectionReason? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF"u8))
        {
            return ImportRejectionReason.Pdf;
        }

        bool riff = data.StartsWith("RIFF"u8) && data.Length >= 12;
        if (data.StartsWith(s_png)
            || data.StartsWith(s_jpeg)
            || data.StartsWith("GIF8"u8)
            || data.StartsWith("II*\0"u8)
            || data.StartsWith("MM\0*"u8)
            || (riff && data[8..12].SequenceEqual("WEBP"u8))
            || (data.Length >= 12 && data[4..8].SequenceEqual("ftyp"u8) && (data[8..12].SequenceEqual("heic"u8) || data[8..12].SequenceEqual("avif"u8))))
        {
            return ImportRejectionReason.Image;
        }

        if (data.StartsWith("ID3"u8)
            || (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
            || (riff && data[8..12].SequenceEqual("WAVE"u8))
            || data.StartsWith("fLaC"u8)
            || data.StartsWith("OggS"u8)
            || (data.Length >= 8 && data[4..8].SequenceEqual("ftyp"u8)))
        {
            return ImportRejectionReason.Audio;
        }

        return null;
    }

    /// <summary>True when the first non-blank character, after any byte order mark, is '&lt;'.</summary>
    private static bool LooksLikeXml(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(s_utf8Bom))
        {
            data = data[3..];
        }
        else if (data.StartsWith(s_utf16LittleEndianBom) || data.StartsWith(s_utf16BigEndianBom))
        {
            return true;
        }

        foreach (byte b in data)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                continue;
            }

            return b == (byte)'<';
        }

        return false;
    }

    private static ImportedPiece FromMidi(string fileName, ReadOnlySpan<byte> data)
    {
        MidiImport import = MidiImporter.Import(data);
        var tracks = import.Tracks
            .Where(track => track.NoteCount > 0)
            .Select(track => new ImportedTrack(track.Index, track.Name, track.InstrumentName, track.NoteCount, HandsOf(import.Notes, track.Index)))
            .ToList();
        return new ImportedPiece(
            fileName,
            SourceFormat.Midi,
            tracks,
            import.Notes,
            import.Meter,
            KeySignature.FromMidi(import),
            import.End,
            import.Report,
            import,
            null,
            null);
    }

    private static ImportedPiece FromMusicXml(string fileName, ReadOnlySpan<byte> data)
    {
        byte[] score = MusicXmlImporter.ReadScoreXml(data);
        MusicXmlImport import = MusicXmlImporter.Import(score);
        var tracks = import.Parts
            .Where(part => part.NoteCount > 0)
            .Select(part => new ImportedTrack(part.Index, part.Name, part.InstrumentName, part.NoteCount, HandsOf(import.Notes, part.Index)))
            .ToList();
        return new ImportedPiece(
            fileName,
            SourceFormat.MusicXml,
            tracks,
            import.Notes,
            import.Meter,
            KeySignature.FromMusicXml(import),
            import.End,
            import.Report,
            null,
            import,
            score);
    }

    private static TrackHands HandsOf(IReadOnlyList<Note> notes, int track)
    {
        var hands = notes.Where(note => note.Track == track && note.Hand is not null).Select(note => note.Hand!).ToList();
        if (hands.Count == 0)
        {
            return TrackHands.None;
        }

        return hands.Any(hand => hand.Attribution.Provenance == Provenance.Inferred) ? TrackHands.Inferred : TrackHands.Imported;
    }

    private static ImportRejection Rejection(ImportRejectionReason reason, string? message = null) => reason switch
    {
        ImportRejectionReason.Empty => new(reason, message ?? "This file is empty.", "Export the piece again and pick the new file.")
        {
            RussianMessage = "Этот файл пуст.",
            RussianTip = "Сохраните пьесу заново и выберите новый файл.",
        },
        ImportRejectionReason.TooLarge => new(
            reason,
            message ?? "This file is larger than 50 MB, the most Motiff Lab can import.",
            "Export only the movement or the tracks you want to explore.")
        {
            RussianMessage = "Этот файл больше 50 МБ, а это предел для Motiff Lab.",
            RussianTip = "Сохраните только ту часть или те дорожки, которые хотите изучать.",
        },
        ImportRejectionReason.Pdf => new(reason, message ?? "This looks like a PDF.", NotationTip)
        {
            RussianMessage = "Похоже, это PDF.",
            RussianTip = RussianNotationTip,
        },
        ImportRejectionReason.Image => new(
            reason,
            message ?? "This looks like an image, such as a photo or scan of a score.",
            "Motiff Lab cannot read scanned scores yet. A notation app or a score-scanning app can turn the scan into MusicXML, which you can import.")
        {
            RussianMessage = "Похоже, это изображение, например фотография или скан нот.",
            RussianTip = "Здесь Motiff Lab не читает сканы нот. Нотный редактор или программа распознавания нот может превратить скан в MusicXML, который можно открыть.",
        },
        ImportRejectionReason.Audio => new(
            reason,
            message ?? "This looks like an audio recording.",
            "Motiff Lab reads notation files for now: MIDI and MusicXML. Starting from a recording comes later.")
        {
            RussianMessage = "Похоже, это аудиозапись.",
            RussianTip = "Здесь Motiff Lab читает нотные файлы: MIDI и MusicXML.",
        },
        ImportRejectionReason.NoNotes => new(reason, message ?? "This file holds no notes to play.", "Check that you exported the part with the music, not an empty template.")
        {
            RussianMessage = "В этом файле нет нот.",
            RussianTip = "Проверьте, что сохранили партию с музыкой, а не пустой шаблон.",
        },
        _ => new(
            ImportRejectionReason.NotMusic,
            message ?? "Motiff Lab cannot tell what kind of file this is.",
            $"It reads .mid, .musicxml and .mxl files. {NotationTip}")
        {
            RussianMessage = "Motiff Lab не может понять, что это за файл.",
            RussianTip = $"Он читает файлы .mid, .musicxml и .mxl. {RussianNotationTip}",
        },
    };
}

public enum SourceFormat
{
    Midi,
    MusicXml,
}

/// <summary>Either the imported piece or why the file could not be imported; exactly one is set.</summary>
public sealed record PieceImportResult(ImportedPiece? Piece, ImportRejection? Rejection);

/// <summary>Why a file could not be imported, in plain words, with what to try instead (N2).</summary>
/// <param name="Message">Why, in plain English; for a damaged or unsupported file, the reader's own words about where it failed.</param>
public sealed record ImportRejection(ImportRejectionReason Reason, string Message, string Tip)
{
    /// <summary>Why, in Russian; for a damaged or unsupported file, said in general without the reader's English detail.</summary>
    public string? RussianMessage { get; init; }

    /// <summary>What to try instead, in Russian.</summary>
    public string? RussianTip { get; init; }

    /// <summary>Why, in the language: the Russian when there is one, else the English.</summary>
    public string Text(Words words) => words.IsRussian && RussianMessage is { } russian ? russian : Message;

    /// <summary>What to try instead, in the language.</summary>
    public string TipIn(Words words) => words.IsRussian && RussianTip is { } russian ? russian : Tip;
}

public enum ImportRejectionReason
{
    /// <summary>The file has no bytes.</summary>
    Empty,

    /// <summary>The file is larger than <see cref="PieceImporter.MaxFileBytes"/> (S1-I1).</summary>
    TooLarge,

    Pdf,

    Image,

    Audio,

    /// <summary>Not MIDI or MusicXML, and not a kind of file Motiff Lab recognises.</summary>
    NotMusic,

    /// <summary>MIDI or MusicXML of a kind Stage 1 does not read.</summary>
    Unsupported,

    /// <summary>MIDI or MusicXML too damaged to import.</summary>
    Damaged,

    /// <summary>The file imported but holds no notes.</summary>
    NoNotes,
}

/// <summary>A piece imported from one source file: the shared event model, the report and the source facts.</summary>
/// <param name="Tracks">The tracks or parts that hold notes, in file order.</param>
/// <param name="Midi">The full MIDI import, when the source is MIDI.</param>
/// <param name="MusicXml">The full MusicXML import, when the source is MusicXML.</param>
/// <param name="ScoreXml">The score's XML as written, unpacked from an .mxl archive, for the score view.</param>
public sealed record ImportedPiece(
    string FileName,
    SourceFormat Format,
    IReadOnlyList<ImportedTrack> Tracks,
    IReadOnlyList<Note> Notes,
    MeterMap Meter,
    IReadOnlyList<KeySignature> Keys,
    Tick End,
    ImportReport Report,
    MidiImport? Midi,
    MusicXmlImport? MusicXml,
    byte[]? ScoreXml)
{
    /// <summary>For a transcription, the bars and tempo worked out from its notes (<see cref="Transcription.BarGrid.Apply"/>); null otherwise.</summary>
    public Transcription.BarGrid? Bars { get; init; }

    /// <summary>
    /// What the player can start with (N2 step 4): the whole piece, each hand the notes carry, and
    /// each track when there are several. A hand that is inferred says so.
    /// </summary>
    public IReadOnlyList<StartChoice> StartChoices
    {
        get
        {
            var choices = new List<StartChoice> { new("Whole piece", SelectionFilter.All, IsInferred: false) };
            foreach ((Hand hand, string label) in new[] { (Hand.Right, "Right hand"), (Hand.Left, "Left hand") })
            {
                var hands = Notes.Where(note => note.Hand?.Value == hand).ToList();
                if (hands.Count > 0)
                {
                    bool inferred = hands.Any(note => note.Hand!.Attribution.Provenance == Provenance.Inferred);
                    choices.Add(new(inferred ? $"{label} (inferred)" : label, SelectionFilter.ForHand(hand), inferred));
                }
            }

            if (Tracks.Count > 1)
            {
                choices.AddRange(Tracks.Select(track => new StartChoice(track.Label, SelectionFilter.ForTrack(track.Index), IsInferred: false)));
            }

            return choices;
        }
    }
}

/// <param name="Index">Zero-based track or part index, as on <see cref="Note.Track"/>.</param>
public sealed record ImportedTrack(int Index, string? Name, string? InstrumentName, int NoteCount, TrackHands Hands)
{
    /// <summary>"Track 2: Right hand", or "Track 2" when the file gives no name.</summary>
    public string Label => string.IsNullOrWhiteSpace(Name) ? $"Track {Index + 1}" : $"Track {Index + 1}: {Name}";
}

/// <summary>Where a track's hands come from (S1-I4).</summary>
public enum TrackHands
{
    /// <summary>The notes carry no hands.</summary>
    None,

    /// <summary>From the source, for example a grand staff.</summary>
    Imported,

    /// <summary>Worked out by <see cref="HandInference"/> for some or all notes.</summary>
    Inferred,
}

/// <summary>A place to start exploring, offered after import (N2 step 4).</summary>
public sealed record StartChoice(string Label, SelectionFilter Filter, bool IsInferred);