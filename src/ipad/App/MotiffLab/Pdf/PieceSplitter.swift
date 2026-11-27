import PDFKit
import SwiftUI

/// The pages of a part copied out of the collection's PDF by PDFKit, as a PDF of their own; nil when the PDF or the pages
/// cannot be read. The collection's bytes are read again here, so the copy carries none of the marks the screen draws.
enum PdfPartCopy {
    static func pages(_ data: Data, first: Int, last: Int) -> Data? {
        guard let source = PDFDocument(data: data), first >= 1, last >= first, last <= source.pageCount else { return nil }
        let copy = PDFDocument()
        for index in (first - 1)...(last - 1) {
            guard let page = source.page(at: index)?.copy() as? PDFPage else { return nil }
            copy.insert(page, at: copy.pageCount)
        }
        return copy.dataRepresentation()
    }
}

/// «Разделить на пьесы» for a PDF or photo piece (v0.4, experiment «Пьесы в файле»): each listed piece's pages copied,
/// `parts.split` one piece at a time, each project file saved as a new piece of the library in the playlist. The
/// collection is not changed. Each piece's project file says where it is in the collection (`partOf`, written by
/// music-core), so the library tells what was split from what (`PieceFamily`).
@MainActor
final class PieceSplitter: ObservableObject {
    @Published var rows: PartRows
    @Published var playlist: String
    /// How many pieces are made of how many, while splitting.
    @Published private(set) var making: (done: Int, total: Int)?
    @Published private(set) var problem: String?
    /// What this iPad made from this piece before, and where this piece came from if it was made from another.
    @Published private(set) var done: SplitLink?
    @Published private(set) var origin: PartOrigin?
    let collection: String

    /// The rows to start from (the page bookmarks), and what the library knows of the piece's collection.
    init(rows: PartRows, family: PieceFamily, title: String) {
        collection = title
        playlist = title
        self.rows = rows
        done = family.split
        origin = family.origin
    }

    func again() {
        done = nil
        problem = nil
    }

    /// What «Предложить» or «Подтвердить пьесы» last said (version 0.5): the pieces kept, or why not.
    @Published private(set) var layerStatus: String?
    @Published private(set) var layerBusy = false

    /// «Предложить»: the rows from the pieces layer the piece keeps, else from music-core's proposals over the notes read
    /// from this PDF (`pieces.find`), by their pages.
    func suggest(file: URL, text: UIText) async {
        guard !layerBusy, making == nil else { return }
        layerBusy = true
        defer { layerBusy = false }
        let answer = await PiecesLayer.withNotes(file: file, language: text.language) { (piece: Int32) -> String in PieceCore(piece: piece).send(.piecesFind) }
        guard let answer, let found = PiecesLayer.rows(answer, pageCount: rows.pageCount) else {
            layerStatus = text["pieces.noSuggestion"]
            return
        }
        rows = found
        layerStatus = nil
    }

    /// «Подтвердить пьесы»: what the PDF view holds is saved to the piece's file first, then the rows are kept there as the
    /// pieces layer (`pieces.save`), in one write. True once the file is written, for the PDF view to open it again.
    func confirm(document: Int32, file: URL, text: UIText) async -> Bool {
        guard !layerBusy, making == nil, rows.problem == nil else { return false }
        layerBusy = true
        defer { layerBusy = false }
        do {
            try await ProjectFiles.save(piece: document, to: file)
        } catch {
            layerStatus = (error as? ProjectError)?.message ?? error.localizedDescription
            return false
        }
        let request = PiecesLayer.saveRequest(rows.rows, now: Date())
        let now = "{\"now\": \"\(ISO8601DateFormatter().string(from: Date()))\"}"
        let answers = await PiecesLayer.withNotes(file: file, language: text.language) { (piece: Int32) -> (saved: String, file: String?) in
            let saved = PieceCore(piece: piece).send(.piecesSave, request)
            guard case .success = PiecesLayer.kept(saved) else { return (saved, nil) }
            return (saved, PieceCore(piece: piece).send(.projectSave, now))
        }
        guard let answers else {
            layerStatus = text["parts.pdf.failedNoWord"]
            return false
        }
        switch PiecesLayer.kept(answers.saved) {
        case .failure(let failure):
            layerStatus = failure.message ?? text["parts.pdf.failedNoWord"]
            return false
        case .success(let count):
            guard let project = answers.file, !(project.utf8.count < 4096 && facadeObject(project)?["error"] != nil) else {
                layerStatus = text["parts.pdf.failedNoWord"]
                return false
            }
            do {
                try Data(project.utf8).write(to: file, options: .atomic)
            } catch {
                layerStatus = error.localizedDescription
                return false
            }
            layerStatus = text.count("pieces.kept", count)
            return true
        }
    }

    /// Splits the collection, its PDF as the player added it (`original`): the pieces made, for the PDF's family, or nil
    /// when none was.
    func split(piece: Int32, original: Data, text: UIText) async -> SplitLink? {
        guard rows.problem == nil, making == nil else { return nil }
        problem = nil
        let now = Date()
        let request = "{\"now\": \"\(ISO8601DateFormatter().string(from: now))\"}"
        let file = await MusicCore.run { PieceCore(piece: piece).send(.projectSave, request) }
        // A refusal is a few words; the project file, with its PDF, can run to tens of megabytes and is not parsed here.
        if file.utf8.count < 4096, let failure = facadeObject(file), failure["error"] != nil {
            problem = failure["message"] as? String ?? text["parts.pdf.failedNoWord"]
            return nil
        }
        let list = rows.rows
        let name = PartsSplit.playlist(playlist, collection: collection)
        var made: [PartLink] = []
        making = (0, list.count)
        for (index, row) in list.enumerated() {
            switch await make(row, number: index + 1, file: file, original: original, playlist: name, now: now, text: text) {
            case .success(let part): made.append(part)
            case .failure(let failure): problem = failure.message
            }
            guard problem == nil else { break }
            making = (index + 1, list.count)
        }
        making = nil
        guard !made.isEmpty else { return nil }
        // What the library will tell from the pieces' partOf once it reads them again.
        let link = SplitLink(playlist: name, parts: made)
        done = link
        return link
    }

    /// One piece: its pages copied off the main thread, `parts.split`, its project file (with its `partOf`) written beside
    /// the others as music-core answers it.
    private func make(_ row: PartRow, number: Int, file: String, original: Data, playlist: String, now: Date, text: UIText) async -> Result<PartLink, PartsSplitFailure> {
        let (first, last) = (row.first, row.last)
        let failed = { (why: String?) in PartsSplitFailure(message: text.format("parts.pdf.failed", String(number), why ?? text["parts.pdf.failedNoWord"])) }
        guard let pdf = await Task.detached(priority: .userInitiated, operation: { PdfPartCopy.pages(original, first: first, last: last) }).value else {
            return .failure(failed(text.format("parts.pdf.copyFailed", PartsSplit.pages(first: first, last: last, text: text))))
        }
        let fields = PartsSplit.request(file: file, row: row, number: number, pdf: pdf, pages: rows.pageCount, playlist: playlist, collection: collection, now: now, text: text)
        let json = facadeRequest(fields)
        let answer = await MusicCore.run { PieceCore.sendWithoutPiece(.partsSplit, json) }
        switch PartsSplit.made(answer) {
        case .failure(let failure):
            return .failure(failed(failure.message))
        case .success(let part):
            do {
                try Data(part.file.utf8).write(to: ProjectFiles.newURL(for: part.title), options: .atomic)
            } catch {
                return .failure(failed(error.localizedDescription))
            }
            return .success(PartLink(id: part.id, title: part.title, pages: part.first > 0 && part.last >= part.first ? part.first...part.last : nil))
        }
    }
}
