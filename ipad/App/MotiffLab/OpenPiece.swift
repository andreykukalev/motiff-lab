import Foundation

/// A piece open in music-core: its number in the facade, its title, its score's pages and its project file.
struct OpenPiece: Identifiable, Sendable {
    let id: Int32
    let title: String
    let pages: [String]
    /// Verovio's timemap for the pages, as JSON: where each bar starts, for the marks (Web/score.js).
    let timemap: String
    /// The project file in Documents that the piece is saved to (ProjectFiles).
    var file: URL?
    /// A PDF piece (0023): the pages as added and no notes, shown by PdfScreen; nothing is engraved.
    var isDocument = false
    /// The piece's other view, PDF or notes, when the library holds one (PDF | Notes, 0023), and where this view opens.
    var other: OtherView?
    var landing: Landing?

    /// Opens the file in music-core in the interface's language and engraves it, titled by its name unless given one; a PDF,
    /// or a PDF piece's project file, opens as a PDF piece with nothing to engrave. With `heard`, a recording heard by the
    /// scan server, the new piece is its notes and `data` is not read (0014, 0016).
    static func open(fileName: String, data: Data, language: String, title given: String? = nil, heard: HeardRecording? = nil) async -> Result<OpenPiece, EngraveError> {
        var fields: [String: Any] = ["language": language]
        if let heard { fields["transcription"] = heard.request }
        let request = (try? JSONSerialization.data(withJSONObject: fields)).map { String(decoding: $0, as: UTF8.self) } ?? "{}"
        return await MusicCore.run {
            guard let opened = facadeObject(MusicCore.open(fileName: fileName, data: data, request: request)) else {
                return .failure(EngraveError(message: "music-core did not answer."))
            }
            guard let piece = (opened["piece"] as? NSNumber)?.int32Value, piece > 0 else {
                return .failure(EngraveError(message: opened["message"] as? String ?? "This file could not be opened."))
            }
            let title = given ?? ProjectFiles.title(of: fileName)
            if opened["document"] is [String: Any] {
                return .success(OpenPiece(id: piece, title: title, pages: [], timemap: "", isDocument: true))
            }
            switch Engraver.engrave(piece: piece, title: title) {
            case .success(let engraving):
                return .success(OpenPiece(id: piece, title: title, pages: engraving.pages, timemap: engraving.timemap))
            case .failure(let error):
                MusicCore.close(piece: piece)
                return .failure(error)
            }
        }
    }

    /// The example piece in the app: Für Elise, 97 bars (design/kit/elise.musicxml).
    static var example: (fileName: String, data: Data, title: String)? {
        guard let url = Bundle.main.url(forResource: "elise", withExtension: "musicxml"),
              let data = try? Data(contentsOf: url) else { return nil }
        return ("elise.musicxml", data, "Für Elise")
    }
}
