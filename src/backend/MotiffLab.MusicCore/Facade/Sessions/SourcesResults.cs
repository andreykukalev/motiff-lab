using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>A PDF or photos kept in the piece's file, as sources.pdf sends them to the scan server: its file name and its bytes as added.</summary>
internal sealed record KeptFile(string FileName, byte[] Bytes);

/// <summary>The notes the scan server read from a PDF: its MusicXML with the engine's mistakes put right, and the piece read from it.</summary>
internal sealed record ReadScore(byte[] MusicXml, ImportedPiece Piece);

/// <summary>The notes read from the PDF kept in the piece, as another version (sources.documentNotes): the piece read and its notes as MIDI.</summary>
internal sealed record DocumentVersion(ImportedPiece Read, byte[] Midi);