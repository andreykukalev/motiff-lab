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
    /// The pieces split from it and where it was split from, as the library's summaries tell (v0.4, «Пьесы в файле»).
    var family = PieceFamily()
    /// What music-core said of the piece as it opened it (the open answer's `project`, contract 4): whether its file holds
    /// notes and a PDF, and its originals; nil when it was not said. PDF | Notes takes the piece's two views from it
    /// (#495 BUG-02), and Sources its originals before they are read again.
    var own: PieceSummary?

    /// Opens the file in music-core in the interface's language and engraves it, titled by its name unless given one; a PDF,
    /// or a PDF piece's project file, opens as a PDF piece with nothing to engrave. With `heard`, a recording heard by the
    /// scan server, the new piece is its notes and `data` is not read (0014, 0016). With `asPdf`, a piece with notes and a
    /// PDF kept inside it opens in its PDF view (testing item 16, facade contract 6). With `pdf`, the PDF of a piece that
    /// is only a PDF and whose PDF is not in its file, read where it lies, `data` being the project file (version 0.5).
    static func open(fileName: String, data: Data, language: String, title given: String? = nil, heard: HeardRecording? = nil, asPdf: Bool = false, pdf: Data? = nil) async -> Result<OpenPiece, EngraveError> {
        var fields: [String: Any] = ["language": language]
        if let heard { fields["transcription"] = heard.request }
        if asPdf { fields["view"] = "pdf" }
        if let pdf { fields["pdf"] = pdf.base64EncodedString() }
        let request = (try? JSONSerialization.data(withJSONObject: fields)).map { String(decoding: $0, as: UTF8.self) } ?? "{}"
        let evenBars = await MainActor.run { DeviceSettings.shared.isOn("score.evenBars") }
        return await MusicCore.run {
            let answer = MusicCore.open(fileName: fileName, data: data, request: request)
            guard let opened = facadeObject(answer) else {
                return .failure(EngraveError(message: "music-core did not answer."))
            }
            guard let piece = (opened["piece"] as? NSNumber)?.int32Value, piece > 0 else {
                return .failure(EngraveError(message: opened["message"] as? String ?? "This file could not be opened."))
            }
            let title = given ?? ProjectFiles.title(of: fileName)
            let own = PieceSummary(answer: answer)
            if opened["document"] is [String: Any] {
                return .success(OpenPiece(id: piece, title: title, pages: [], timemap: "", isDocument: true, own: own))
            }
            switch Engraver.engrave(piece: piece, title: title, evenBars: evenBars) {
            case .success(let engraving):
                return .success(OpenPiece(id: piece, title: title, pages: engraving.pages, timemap: engraving.timemap, own: own))
            case .failure(let error):
                MusicCore.close(piece: piece)
                return .failure(error)
            }
        }
    }

    /// The example piece in the app: Für Elise, the whole piece, the web app's own (src/MotiffLab.Web/Playback/FurElise.musicxml).
    static var example: (fileName: String, data: Data, title: String)? {
        guard let url = Bundle.main.url(forResource: "FurElise", withExtension: "musicxml"),
              let data = try? Data(contentsOf: url) else { return nil }
        return ("FurElise.musicxml", data, "Für Elise")
    }
}
