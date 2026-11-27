import Foundation
import PDFKit

/// A piece open in music-core: its number in the facade, its title, its score's pages and its project file.
struct OpenPiece: Identifiable, Sendable {
    let id: Int32
    let title: String
    let pages: [String]
    /// Verovio's timemap for the pages, as JSON: where each bar starts, for the marks (the score page).
    let timemap: String
    /// The bars the app wrote itself as the piece was engraved when it opened (`Engraving.appWrittenBars`); the score as
    /// last engraved keeps its own (`SourcesModel.engraved`).
    var appWrittenBars: Int?
    /// The project file in Documents that the piece is saved to (ProjectFiles).
    var file: URL?
    /// A PDF piece (0023): the pages as added and no notes, shown by PdfScreen; nothing is engraved.
    var isDocument = false
    /// A piece that is only its recording (v1.1, finding 29): the recording original's id, its sound kept on this iPad
    /// (`OriginalSounds`), and no notes yet, shown by RecordingScreen; nothing is engraved. Nil for any other piece.
    var recording: String?
    /// The piece's other view, PDF or notes, when the library holds one (PDF | Notes, 0023), and where this view opens.
    var other: OtherView?
    var landing: Landing?
    /// The pieces split from it and where it was split from, as the library's summaries tell (v0.4, «Пьесы в файле»).
    var family = PieceFamily()
    /// What music-core said of the piece as it opened it (the open answer's `project`, contract 4): whether its file holds
    /// notes and a PDF, and its originals; nil when it was not said. PDF | Notes takes the piece's two views from it
    /// (#495 BUG-02), and Sources its originals before they are read again.
    var own: PieceSummary?
    /// «Мои правки» as the piece opened (version 0.5, contract 12): whether the player's corrections are laid over the
    /// notes, how many there are and those that no longer fit; nil for a PDF piece and for notes opened only to be shown.
    var edits: EditsLayerState?
    /// A draft opened to be shown (`drafts.open`, v1.1): its name and places, whose labels stand above each place's start;
    /// nil for any other piece.
    var draft: DraftViewAnswer.Draft?
    /// Opened under a number of its own only to be shown over the piece ("readOnly": other Notes, a draft's view): the
    /// phrases' marks are not drawn on it (v1.2, they belong to the working notes, where they are edited).
    var shownOnly = false

    /// Opens the file in music-core in the interface's language and engraves it, titled as given, else as music-core names
    /// it (the player's title, a MusicXML score's work title, the file's name); a PDF,
    /// or a PDF piece's project file, opens as a PDF piece with nothing to engrave, and the project file of a piece that is
    /// only its recording opens as that recording (v1.1, finding 29). With `asPdf`, a piece with notes and a
    /// PDF kept inside it opens in its PDF view (testing item 16, facade contract 6). With `pdf`, the PDF of a piece that
    /// is only a PDF and whose PDF is not in its file, read where it lies, `data` being the project file (version 0.5).
    static func open(fileName: String, data: Data, language: String, title given: String? = nil, asPdf: Bool = false, pdf: Data? = nil) async -> Result<OpenPiece, EngraveError> {
        // E-library-2: a PDF this iPad cannot show is refused before it becomes a piece, as a damaged MusicXML is: its
        // screen would only say it cannot be read, and the library would keep a piece that never opens.
        if data.starts(with: pdfStart), readablePdf(data) == nil { return .failure(EngraveError(message: UIText.of(language)["pdf.unreadable"])) }
        let fields = openRequest(language: language, asPdf: asPdf, pdf: pdf)
        let request = facadeRequest(fields)
        let evenBars = await MainActor.run { DeviceSettings.shared.isOn("score.evenBars") }
        return await MusicCore.run {
            let answer = MusicCore.open(fileName: fileName, data: data, request: request)
            ProbeCounts.shared.mark("opened")
            guard let opened = facadeObject(answer) else {
                return .failure(EngraveError(message: UIText.of(language)["opening.noAnswer"]))
            }
            guard let piece = (opened["piece"] as? NSNumber)?.int32Value, piece > 0 else {
                return .failure(EngraveError(message: opened["message"] as? String ?? UIText.of(language)["opening.failed"]))
            }
            let own = PieceSummary(answer: answer)
            // The title the player set, else the score's own (a MusicXML work title, #330), else the file's name: music-core's
            // choice (`PieceDetails`), as the library's line shows it.
            let fileTitle = ProjectFiles.title(of: fileName)
            let title = given ?? own?.shownTitle(else: fileTitle) ?? fileTitle
            if opened["document"] is [String: Any] {
                return .success(OpenPiece(id: piece, title: title, pages: [], timemap: "", isDocument: true, own: own))
            }
            if let recording = (opened["recording"] as? [String: Any])?["id"] as? String {
                return .success(OpenPiece(id: piece, title: title, pages: [], timemap: "", recording: recording, own: own))
            }
            let edits = EditsLayerState.opened(answer)
            switch Engraver.engrave(piece: piece, title: title, evenBars: evenBars, text: UIText.of(language)) {
            case .success(let engraving):
                ProbeCounts.shared.mark("engraved")
                return .success(OpenPiece(id: piece, title: title, pages: engraving.pages, timemap: engraving.timemap, appWrittenBars: engraving.appWrittenBars, own: own, edits: edits))
            case .failure(let error):
                MusicCore.close(piece: piece)
                return .failure(error)
            }
        }
    }

    /// How a PDF's bytes start, as music-core tells a PDF.
    static let pdfStart = Data("%PDF".utf8)

    /// The bytes as a PDF this iPad can show and read page by page: one that starts as a PDF, opens, is not locked with a
    /// password and has a page; nil for any other. The one test of it: opening a file, sending one a page a request
    /// (`ScanServer.recognise`) and a folder import's facts (`FolderScan.facts`).
    static func readablePdf(_ data: Data) -> PDFDocument? {
        guard data.starts(with: pdfStart), let document = PDFDocument(data: data), !document.isLocked, document.pageCount > 0 else { return nil }
        return document
    }

    /// The open request's fields: the language and "edits": "piece", so a project file lays the player's corrections one
    /// by one, or leaves them out when its file says they are off (version 0.5), and opens even when some no longer fit.
    /// music-core reads "edits" only for a project file.
    static func openRequest(language: String, asPdf: Bool = false, pdf: Data? = nil) -> [String: Any] {
        var fields: [String: Any] = ["language": language, "edits": "piece"]
        if asPdf { fields["view"] = "pdf" }
        if let pdf { fields["pdf"] = pdf.base64EncodedString() }
        return fields
    }

    /// The example piece in the app: Für Elise, the whole piece, the web app's own (src/shared/Playback/FurElise.musicxml).
    static var example: (fileName: String, data: Data, title: String)? {
        guard let url = Bundle.main.url(forResource: "FurElise", withExtension: "musicxml"),
              let data = try? Data(contentsOf: url) else { return nil }
        return ("FurElise.musicxml", data, "Für Elise")
    }
}
