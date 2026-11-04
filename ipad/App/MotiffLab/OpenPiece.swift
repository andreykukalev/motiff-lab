import Foundation

/// A piece open in music-core: its number in the facade, its title, its score's pages and its project file.
struct OpenPiece: Identifiable, Sendable {
    let id: Int32
    let title: String
    let pages: [String]
    /// The project file in Documents that the piece is saved to (ProjectFiles).
    var file: URL?

    /// Opens the file in music-core in the interface's language and engraves it, titled by its name unless given one.
    static func open(fileName: String, data: Data, language: String, title given: String? = nil) async -> Result<OpenPiece, EngraveError> {
        await MusicCore.run {
            let request = "{\"language\": \"\(language)\"}"
            guard let opened = facadeObject(MusicCore.open(fileName: fileName, data: data, request: request)) else {
                return .failure(EngraveError(message: "music-core did not answer."))
            }
            guard let piece = (opened["piece"] as? NSNumber)?.int32Value, piece > 0 else {
                return .failure(EngraveError(message: opened["message"] as? String ?? "This file could not be opened."))
            }
            let title = given ?? ProjectFiles.title(of: fileName)
            switch Engraver.pages(piece: piece, title: title) {
            case .success(let pages):
                return .success(OpenPiece(id: piece, title: title, pages: pages))
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
