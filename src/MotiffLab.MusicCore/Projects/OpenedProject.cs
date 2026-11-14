using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Projects;

/// <summary>A project opened for work: the source imported again, the edit log applied, and its motifs and variations rebuilt.</summary>
/// <param name="Project">The project, with its Check flags worked out when the file it came from had none (before version 12).</param>
/// <param name="Notes">The piece's notes with the project's corrections applied; <see cref="ImportedPiece.Notes"/> stays as imported.</param>
public sealed record OpenedProject(
    Project Project,
    ImportedPiece Piece,
    IReadOnlyList<Note> Notes,
    IReadOnlyList<Motif> Motifs,
    IReadOnlyList<(SavedVariation Saved, VariationChain Chain)> Variations)
{
    /// <summary>
    /// Imports the project's source and rebuilds what was made from it, every correction applied. Throws
    /// <see cref="ProjectFileException"/> when the source no longer imports or no longer matches.
    /// </summary>
    public static OpenedProject Open(Project project) => Open(project, EditsLayer.Strict);

    /// <summary>
    /// Imports the project's source and rebuilds what was made from it, the corrections laid over the notes as
    /// <paramref name="layer"/> says (version 0.5). <see cref="EditsLayer.Strict"/> opens as before. With
    /// <see cref="EditsLayer.Lenient"/> or <see cref="EditsLayer.Off"/>, a correction, motif, variation or idea that no
    /// longer fits the notes never stops the piece opening: it is left out of what opens and stays in
    /// <see cref="Project"/>, so saving keeps it. Throws <see cref="ProjectFileException"/> when the source no longer
    /// imports, or, strictly, no longer matches.
    /// </summary>
    public static OpenedProject Open(Project project, EditsLayer layer)
    {
        ArgumentNullException.ThrowIfNull(project);
        ImportedPiece piece = Read(project);
        bool assembled = project.Assembly.Applied is not null;
        EditsApplied edits = project.Edits.ApplyEach(piece.Notes);
        if (layer == EditsLayer.Strict && !edits.AllFit)
        {
            try
            {
                project.Edits.Apply(piece.Notes);
            }
            catch (InvalidOperationException exception)
            {
                throw new ProjectFileException($"The note corrections in this project no longer fit the piece: {exception.Message}");
            }
        }

        IReadOnlyList<Note> corrected = layer == EditsLayer.Off ? piece.Notes : edits.Notes;
        IReadOnlyList<Note> notes = assembled ? corrected : PerformanceTake.Apply(corrected, project.TakenBars);
        var rebuilt = new Rebuilding(project, piece, notes, layer, assembled);
        IReadOnlyList<Motif> motifs = rebuilt.Motifs();
        IReadOnlyList<(SavedVariation, VariationChain)> variations = rebuilt.Variations(motifs);
        IReadOnlyList<(Observation, VariationChain?)> observations = rebuilt.Observations();
        if (project.Check is null)
        {
            // A file from before version 12: the Check flags are worked out from the piece, a scan's resolved where the player confirmed the bar.
            project = project with
            {
                Check = project.Scan is { } scanned ? CheckFlag.FromScan(scanned.CheckOf(piece), scanned.ConfirmedBars)
                    : project.Transcription is { } heard ? CheckFlag.FromTranscription(heard, project.Source, piece)
                    : [],
            };
        }

        return new OpenedProject(project, piece, notes, motifs, variations)
        {
            Observations = observations,
            Layer = layer,
            Edits = edits,
            Hidden = rebuilt.Hidden,
        };
    }

    /// <summary>The observations, in the project's order, each with its variation's chain rebuilt; null for a note alone.</summary>
    public IReadOnlyList<(Observation Saved, VariationChain? Chain)> Observations { get; init; } = [];

    /// <summary>How the corrections were laid over the notes.</summary>
    public EditsLayer Layer { get; init; }

    /// <summary>
    /// The corrections laid one by one, as with <see cref="EditsLayer.Lenient"/>: what was applied, moved and did not fit.
    /// With <see cref="EditsLayer.Off"/> it says what the layer would do when switched on; <see cref="Notes"/> are as imported.
    /// </summary>
    public EditsApplied Edits { get; init; } = EditsApplied.None([]);

    /// <summary>The ids of the motifs, variations and ideas left out because they no longer fit the notes; they stay in the project.</summary>
    public IReadOnlyList<string> Hidden { get; init; } = [];

    /// <summary>A saved step proposed on the chain again; shared with <see cref="Observation.Restore"/>.</summary>
    internal static VariationChain Propose(VariationChain chain, SavedStep step)
    {
        IVariationOperation operation = VariationOperations.Find(step.Operation)
            ?? throw new ProjectFileException($"A variation uses an operation ({step.Operation}) this version of Motiff Lab does not have.");
        if (chain.UnavailableReason(operation, step.Parameters) is { } reason)
        {
            throw new ProjectFileException($"A variation can no longer be made: {reason}");
        }

        return chain.Propose(operation, step.Parameters);
    }

    /// <summary>A saved variation made again over its motif's passage in <paramref name="notes"/>; shared with the facade's layer switch.</summary>
    /// <exception cref="ProjectFileException">A step can no longer be made.</exception>
    internal static VariationChain Restore(SavedVariation saved, MotifSelection selection, IReadOnlyList<Note> notes)
    {
        VariationChain chain = VariationChain.Start(
            VariationFacts.MotifNotes(notes, selection),
            VariationContext.For(selection, saved.Keep.ToHashSet(), key: saved.Key));
        foreach (SavedStep step in saved.Steps)
        {
            chain = Propose(chain, step).Accept();
        }

        return saved.Pending is { } pending ? Propose(chain, pending) : chain;
    }

    /// <summary>The piece imported again, with its scan's Check, its transcription's bars and its assembly over it.</summary>
    private static ImportedPiece Read(Project project)
    {
        if (!project.HasNotes)
        {
            throw new ProjectFileException("This piece is a PDF read as it is: it has no notes to open.");
        }

        PieceImportResult imported = PieceImporter.Import(project.FileName, project.Source);
        if (imported.Piece is not { } piece)
        {
            throw new ProjectFileException($"The piece in this project cannot be read: {imported.Rejection?.Message}");
        }

        if (project.Scan is { } scan)
        {
            if (piece.MusicXml is null)
            {
                throw new ProjectFileException("This project says it was scanned, but its piece is not MusicXML.");
            }

            piece = scan.CheckOf(piece).Mark(piece, scan.ConfirmedBars);
        }

        if (project.Transcription is not null)
        {
            piece = Heard(project.Transcription, piece);
        }

        return project.Assembly.Over(piece) with { PlayerTempo = project.PlayerTempo };
    }

    private static ImportedPiece Heard(TranscriptionOrigin transcription, ImportedPiece piece)
    {
        if (piece.Midi is null)
        {
            throw new ProjectFileException("This project says it was transcribed from a recording, but its piece is not MIDI.");
        }

        if (transcription.Bars is { } bars)
        {
            try
            {
                piece = bars.Apply(piece);
            }
            catch (ArgumentException exception)
            {
                throw new ProjectFileException($"The bars worked out for this transcription no longer fit its notes: {exception.Message}");
            }
        }

        return Transcribed.Mark(piece);
    }

    /// <summary>The motifs, variations and ideas made again from the notes, those that no longer fit left out when the layer allows it.</summary>
    private sealed class Rebuilding(Project project, ImportedPiece piece, IReadOnlyList<Note> notes, EditsLayer layer, bool assembled)
    {
        private readonly List<string> _hidden = [];

        /// <summary>True when what no longer fits is left out instead of refusing the piece (not for an assembly's variations, as before).</summary>
        private bool Forgiving => layer != EditsLayer.Strict;

        public IReadOnlyList<string> Hidden => _hidden;

        public IReadOnlyList<Motif> Motifs()
        {
            var motifs = new List<Motif>();
            foreach (SavedMotif saved in project.Motifs)
            {
                MotifSelection selection = saved.Kind == MotifSelectionKind.Notes
                    ? MotifSelector.SelectNotes(notes, piece.Meter, saved.NoteIds)
                    : MotifSelector.Select(notes, piece.Meter, saved.Span, saved.Filter, MotifSelection.MaxStructureBars);
                if (!selection.NoteIds.SequenceEqual(saved.NoteIds, StringComparer.Ordinal))
                {
                    // An applied assembly may have changed the motif's notes (decision record 0024, section 7): it leaves the list.
                    // With the corrections laid one by one or off, the motif is left out and kept in the file (version 0.5).
                    if (assembled || Forgiving)
                    {
                        _hidden.Add(saved.Id);
                        continue;
                    }

                    throw new ProjectFileException($"\"{saved.Name}\" no longer matches the notes of the piece.");
                }

                motifs.Add(Motif.Create(saved.Id, motifs.Count + 1, selection, saved.Kind, saved.Mode, notes) with { Name = saved.Name });
            }

            return motifs;
        }

        public IReadOnlyList<(SavedVariation, VariationChain)> Variations(IReadOnlyList<Motif> motifs)
        {
            var variations = new List<(SavedVariation, VariationChain)>();
            foreach (SavedVariation saved in project.Variations)
            {
                Motif? found = motifs.FirstOrDefault(motif => motif.Id == saved.MotifId);
                if (found is null && (assembled || Forgiving))
                {
                    _hidden.Add(saved.Id);
                    continue;
                }

                Motif motif = found
                    ?? throw new ProjectFileException($"A variation refers to a motif ({saved.MotifId}) that is not in the project.");
                try
                {
                    variations.Add((saved, Restore(saved, motif.Selection, notes)));
                }
                catch (ProjectFileException) when (Forgiving)
                {
                    _hidden.Add(saved.Id);
                }
            }

            return variations;
        }

        public IReadOnlyList<(Observation, VariationChain?)> Observations()
        {
            var observations = new List<(Observation, VariationChain?)>();
            foreach (Observation observation in project.Observations)
            {
                try
                {
                    observations.Add((observation, observation.Restore(notes, piece.Meter)));
                }
                catch (ProjectFileException) when (Forgiving)
                {
                    _hidden.Add(observation.Id);
                }
            }

            return observations;
        }
    }
}