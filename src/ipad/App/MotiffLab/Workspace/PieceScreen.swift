import SwiftUI

/// The screen of a piece the library opens, by what the piece is: its PDF (`PdfScreen`, 0023), its recording while it has
/// no notes (`RecordingScreen`, v1.1, finding 29), else its notes (`ScoreScreen`). Here, in the folder that knows every
/// feature, so the library names one screen; and here the library's recognition is told to its screens
/// (`BackgroundReading`, Andrei, 2026-11-25).
struct PieceScreen: View {
    let piece: OpenPiece
    /// Opens the piece's other view in its place (PDF | Notes, 0023).
    let switchView: (Landing) async -> Void
    /// Opens a project file in this piece's place, at a place: the piece again, or its notes just read or heard.
    let openFile: (URL, Landing) async -> Void
    @ObservedObject private var queue = RecognitionQueue.shared

    var body: some View {
        screen.environment(\.backgroundReading, background)
    }

    @ViewBuilder private var screen: some View {
        if piece.isDocument {
            PdfScreen(piece: piece, switchView: switchView, openRead: openFile)
        } else if piece.recording != nil {
            RecordingScreen(piece: piece, reopen: reopen)
        } else {
            ScoreScreen(piece: piece, switchView: switchView, reopen: reopen, openFile: openFile)
        }
    }

    /// The library's recognition as this piece's screens see it: the piece recognised now, whether it is this one, its
    /// line under the PDF's bar, and sending it or letting its notes in.
    private var background: BackgroundReading {
        let file = piece.file
        let queue = queue
        let item = file.flatMap { queue.status(of: $0) }
        var line: (@MainActor (Bool, @escaping () -> Void) -> AnyView)?
        if let item {
            line = { opening, open in
                AnyView(BackgroundRecognitionLine(item: item, queue: queue, text: UIText.current, opening: opening, open: open))
            }
        }
        return BackgroundReading(
            file: file,
            busy: queue.busy?.title,
            reading: item?.state == .queued || item?.state == .reading,
            line: line,
            send: { file, address, hosted in
                await queue.append(file, address: address, hosted: hosted)
                return queue.status(of: file) != nil
            },
            release: { file in
                queue.released(file)
                return await queue.finished(file)?.state == .done
            }
        )
    }

    /// Opens the piece's own project file again in its place; nil for a piece with none.
    private var reopen: ((Landing) async -> Void)? {
        guard let file = piece.file else { return nil }
        let openFile = openFile
        return { landing in await openFile(file, landing) }
    }
}
