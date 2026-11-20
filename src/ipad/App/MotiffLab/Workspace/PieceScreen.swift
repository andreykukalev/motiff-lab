import SwiftUI

/// The screen of a piece the library opens, by what the piece is: its PDF (`PdfScreen`, 0023), its recording while it has
/// no notes (`RecordingScreen`, v1.1, finding 29), else its notes (`ScoreScreen`). Here, in the folder that knows every
/// feature, so the library names one screen.
struct PieceScreen: View {
    let piece: OpenPiece
    /// Opens the piece's other view in its place (PDF | Notes, 0023).
    let switchView: (Landing) async -> Void
    /// Opens a project file in this piece's place, at a place: the piece again, or its notes just read or heard.
    let openFile: (URL, Landing) async -> Void

    var body: some View {
        if piece.isDocument {
            PdfScreen(piece: piece, switchView: switchView, openRead: openFile)
        } else if piece.recording != nil {
            RecordingScreen(piece: piece, reopen: reopen)
        } else {
            ScoreScreen(piece: piece, switchView: switchView, reopen: reopen, openFile: openFile)
        }
    }

    /// Opens the piece's own project file again in its place; nil for a piece with none.
    private var reopen: ((Landing) async -> Void)? {
        guard let file = piece.file else { return nil }
        let openFile = openFile
        return { landing in await openFile(file, landing) }
    }
}
