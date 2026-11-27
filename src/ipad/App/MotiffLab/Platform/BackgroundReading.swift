import SwiftUI

/// The library's recognition as the screens of one piece see it (Andrei, 2026-11-25): one recognition at a time, and
/// «Распознать ноты» on an open PDF goes to it, so the job is kept on this iPad and goes on after the app is closed.
/// `PieceScreen` builds it, in the folder that knows the library's recognition, and the PDF and Sources read it from the
/// environment, so their folders name none of it.
struct BackgroundReading {
    /// The piece's project file this is about; nil for none, as on a screen no piece screen shows.
    var file: URL?
    /// The title of the piece recognised now, this one or another: nothing new is sent meanwhile; nil when none is.
    var busy: String?
    /// This piece is being recognised: its line stands under the PDF's bar in place of «Распознать ноты».
    var reading = false
    /// The line under the PDF's bar while this piece is recognised, failed or its notes wait: given whether the notes are
    /// being opened and what «Открыть ноты» does. Nil when the piece is not in the library's recognition.
    var line: (@MainActor (_ opening: Bool, _ open: @escaping () -> Void) -> AnyView)?
    /// Sends the piece's project file to the scan server's address, Motiff Lab's or not, to be recognised in the
    /// background: false when it was not taken.
    var send: @MainActor (_ file: URL, _ address: URL, _ hosted: Bool) async -> Bool = { _, _, _ in false }
    /// Lets the library's recognition write the notes into the piece, whose screen writes it no more, and answers once
    /// they are in (true) or are not (false).
    var release: @MainActor (_ file: URL) async -> Bool = { _ in false }
}

private struct BackgroundReadingKey: EnvironmentKey {
    static var defaultValue: BackgroundReading { BackgroundReading() }
}

extension EnvironmentValues {
    /// The library's recognition as the screens of the piece shown see it.
    var backgroundReading: BackgroundReading {
        get { self[BackgroundReadingKey.self] }
        set { self[BackgroundReadingKey.self] = newValue }
    }
}
