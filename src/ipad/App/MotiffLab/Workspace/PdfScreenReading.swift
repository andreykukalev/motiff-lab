import SwiftUI

/// Recognise notes from the PDF's screen: the panel, the scan server it goes to, the library's recognition in the
/// background, and the notes read put into this same piece, which opens on them.
extension PdfScreen {
    /// Recognise notes in the bar, while none have been read from this PDF on this device.
    var readNotesAction: (() -> Void)? {
        guard openRead != nil, piece.other == nil else { return nil }
        return {
            more = false
            readingOpen = true
        }
    }

    /// The library's recognition as this piece sees it; none for the PDF linked in Files, which is not recognised here.
    var background: BackgroundReading {
        linked || backgroundReading.file != piece.file ? BackgroundReading() : backgroundReading
    }

    /// «Открыть ноты» once the notes are back (Andrei, 2026-11-25): this screen saves the piece and writes it no more, the
    /// library's recognition writes the notes into it, and the piece opens on its notes where the page under the bar starts.
    func openBackgroundNotes(_ file: URL) async {
        guard let openRead, !handingOver else { return }
        handingOver = true
        defer { handingOver = false }
        pdf.keepView()
        try? await ProjectFiles.save(piece: piece.id, to: file)
        guard await background.release(file) else { return }
        let starts = await PieceSummary.read(file)?.documentPages ?? []
        await openRead(file, .tick(PageMap(starts: starts).start(of: max(1, pdf.page))))
    }

    /// Whether recognising the notes goes to Motiff Lab's server, which is Pro.
    var hosted: Bool { scanAddress != nil && scanAddress == ScanServer.hosted(account) }

    /// The scan server the PDF would go to: the player's own, or Motiff Lab's in a build with sign-in.
    var scanAddress: URL? { scan.address(hosted: ScanServer.hosted(account)) }

    /// Sends the PDF to the scan server and puts the notes it read into this same piece (testing item 16), which opens as
    /// notes where the page under the bar starts (the web app's `ReadNotesAsync`). Answers why not, or nil once they open.
    /// The server's report on the pages goes with the notes (v0.4, «Фото страниц»). A PDF longer than the server
    /// reads at once goes in parts, with the panel's bar (issue #479).
    func readNotes() async -> String? {
        guard let address = scanAddress, let data = pdf.original else { return text["pdf.readNone"] }
        // With four engines the piece, photos of pages too (v1.5 i-7), is read in the background by the library's recognition
        // (Andrei, 2026-11-25): its job is kept on this iPad, so it goes on after the app is closed, and the bar's line says how far.
        if ScanServer.jobs, let file = piece.file {
            try? await ProjectFiles.save(piece: piece.id, to: file)
            guard await background.send(file, address, hosted) else { return text["pdf.readNone"] }
            readingOpen = false
            return nil
        }
        switch await ScanServer.recognise(fileName: pdf.fileName, data: data, address: address, account: account) {
        case .failure(let problem): return problem.text(text)
        case .success(let read):
            return await fill(DocumentRead.request(read, pages: max(1, pdf.pageCount), language: text.language, now: Date()))
        }
    }

    /// Read the notes, as the panel offers it: what leaves the iPad and to where, and what to do on Send.
    func readPanel(_ read: @escaping () async -> String?, heading: String? = nil, keeps: String? = nil, line: String? = nil, pdf: Data? = nil) -> ReadNotesPanel {
        ReadNotesPanel(
            address: scanAddress,
            hosted: hosted,
            signedIn: account.player != nil,
            pro: account.allows(.scan),
            text: text,
            heading: heading,
            keeps: keeps,
            line: line,
            settings: {
                settingsNext = true
                readingOpen = false
            },
            read: read, pdf: pdf, busy: background.busy
        )
    }

    /// The notes read from the pages (`DocumentRead.request`) into this same piece, which then opens as notes: answers why
    /// not, or nil. The page reports (v0.4), one per page of the notes, are kept with them, or left out when music-core
    /// refuses them, and so are the pages no engine read (project file 18).
    func fill(_ request: [String: Any]) async -> String? {
        guard let openRead else { return text["pdf.readNone"] }
        let answer = await DocumentRead.call(piece: piece.id, request)
        guard let object = facadeObject(answer) else { return text["pdf.readNone"] }
        if object["error"] != nil { return object["message"] as? String ?? text["pdf.readNone"] }
        guard let file = object["file"] as? String else { return text["pdf.readNone"] }
        let starts = ((object["project"] as? [String: Any])?["documentPages"] as? [NSNumber] ?? []).map(\.doubleValue)
        // The same piece's project file, now with notes: the PDF, its marks and bookmarks stay in it.
        let url = piece.file ?? ProjectFiles.newURL(for: piece.title)
        do {
            try Data(file.utf8).write(to: url, options: .atomic)
        } catch {
            return error.localizedDescription
        }
        readingOpen = false
        await openRead(url, .tick(PageMap(starts: starts).start(of: max(1, pdf.page))))
        return nil
    }
}
