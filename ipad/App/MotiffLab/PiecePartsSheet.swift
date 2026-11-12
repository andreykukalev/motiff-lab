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

    init(pdf: PdfController, title: String) {
        collection = title
        playlist = title
        rows = PartRows.fromBookmarks(pdf.bookmarks, pageCount: pdf.pageCount)
        done = pdf.family.split
        origin = pdf.family.origin
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
        let answer = await PiecesLayer.withNotes(file: file, language: text.language) { (piece: Int32) -> String in MusicCore.call(piece: piece, "pieces.find") }
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
            let saved = MusicCore.call(piece: piece, "pieces.save", request)
            guard case .success = PiecesLayer.kept(saved) else { return (saved, nil) }
            return (saved, MusicCore.call(piece: piece, "project.save", now))
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

    func split(piece: Int32, pdf: PdfController, text: UIText) async {
        guard rows.problem == nil, making == nil, let original = pdf.original else { return }
        problem = nil
        let now = Date()
        let request = "{\"now\": \"\(ISO8601DateFormatter().string(from: now))\"}"
        let file = await MusicCore.run { MusicCore.call(piece: piece, "project.save", request) }
        // A refusal is a few words; the project file, with its PDF, can run to tens of megabytes and is not parsed here.
        if file.utf8.count < 4096, let failure = facadeObject(file), failure["error"] != nil {
            problem = failure["message"] as? String ?? text["parts.pdf.failedNoWord"]
            return
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
        if !made.isEmpty {
            // What the library will tell from the pieces' partOf once it reads them again.
            let link = SplitLink(playlist: name, parts: made)
            pdf.family.split = link
            done = link
        }
        making = nil
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
        guard let body = try? JSONSerialization.data(withJSONObject: fields) else { return .failure(failed(nil)) }
        let json = String(decoding: body, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "parts.split", json) }
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

/// «Пьесы в этом файле», opened from the PDF's Contents: a row «название · с–по» per piece, first filled from the page
/// bookmarks, «Добавить пьесу», the playlist, and «Разделить на пьесы». Afterwards it says «Разделено на N пьес · плейлист
/// «…»» with the pieces made; a piece made says where it came from. Finger-sized controls throughout.
struct PiecePartsSheet: View {
    let piece: OpenPiece
    @ObservedObject var pdf: PdfController
    let text: UIText
    /// Opens the piece's file again in the PDF view, once the pieces layer is written to it; nil where it cannot be.
    var reopen: (() async -> Void)? = nil
    @StateObject private var splitter: PieceSplitter
    @Environment(\.dismiss) private var dismiss

    init(piece: OpenPiece, pdf: PdfController, text: UIText, reopen: (() async -> Void)? = nil) {
        self.piece = piece
        _pdf = ObservedObject(wrappedValue: pdf)
        self.text = text
        self.reopen = reopen
        _splitter = StateObject(wrappedValue: PieceSplitter(pdf: pdf, title: piece.title))
    }

    /// The piece's file, when its notes were read from this PDF into it (version 0.5): «Предложить» and «Подтвердить пьесы».
    private var notesFile: URL? {
        guard reopen != nil, let file = piece.file, piece.other?.file == file else { return nil }
        return file
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                header
                if let origin = splitter.origin {
                    Text(PartsSplit.from(origin, text: text))
                        .dsType(.label)
                        .foregroundStyle(Color.ds("ink-2"))
                        .fixedSize(horizontal: false, vertical: true)
                }
                if let done = splitter.done {
                    PiecePartsDone(link: done, text: text) { splitter.again() }
                    if let problem = splitter.problem {
                        // A piece that could not be made after others were.
                        Text(problem)
                            .dsType(.label)
                            .foregroundStyle(Color.ds("danger"))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                } else {
                    form
                }
            }
            .padding(CGFloat.ds("s-8"))
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .interactiveDismissDisabled(splitter.making != nil)
    }

    private var header: some View {
        HStack(alignment: .center) {
            Text(text["parts.title"])
                .dsType(.title)
                .foregroundStyle(Color.ds("ink"))
                .accessibilityAddTraits(.isHeader)
            Spacer()
            Button(text["library.done"]) { dismiss() }
                .buttonStyle(DSButtonStyle(kind: .secondary))
                .disabled(splitter.making != nil)
        }
    }

    @ViewBuilder private var form: some View {
        Text(text["parts.pdf.how"])
            .dsType(.caption)
            .foregroundStyle(Color.ds("ink-2"))
            .fixedSize(horizontal: false, vertical: true)
        if let file = notesFile {
            layer(file)
        }
        ForEach($splitter.rows.rows) { $row in
            let number = (splitter.rows.rows.firstIndex { $0.id == row.id } ?? 0) + 1
            PiecePartRowView(
                row: $row,
                number: number,
                placeholder: PartsSplit.title(of: PartRow(title: "", first: row.first, last: row.last), number: number, collection: piece.title, text: text),
                text: text
            ) { first, step in
                splitter.rows.step(row.id, first: first, by: step)
            } remove: {
                splitter.rows.remove(row.id)
            }
        }
        Button {
            if let start = splitter.rows.suggestedStart(under: pdf.page) { splitter.rows.add(at: start) }
        } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                Icon(name: "plus")
                Text(text["parts.pdf.add"])
            }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary))
        .disabled(splitter.rows.suggestedStart(under: pdf.page) == nil || splitter.making != nil)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["parts.playlist"]).overline()
            TextField(piece.title, text: $splitter.playlist)
                .modifier(FieldLook())
                .accessibilityLabel(text["parts.playlist"])
        }
        if let problem = splitter.problem ?? splitter.rows.problem.map({ $0.text(text, pages: splitter.rows.pageCount) }) {
            // An empty list is where the player starts, not a mistake.
            let starting = splitter.problem == nil && splitter.rows.problem == .empty
            Text(problem)
                .dsType(.label)
                .foregroundStyle(starting ? Color.ds("ink-2") : Color.ds("danger"))
                .fixedSize(horizontal: false, vertical: true)
        }
        HStack(spacing: CGFloat.ds("s-3")) {
            Button(text["parts.split"]) {
                Task { await splitter.split(piece: piece.id, pdf: pdf, text: text) }
            }
            .buttonStyle(DSButtonStyle(kind: .primary))
            .disabled(splitter.rows.problem != nil || splitter.making != nil || pdf.original == nil)
            if let making = splitter.making {
                ProgressView()
                Text(text.format("parts.pdf.making", String(making.done), String(making.total)))
                    .dsType(.label)
                    .monospacedDigit()
                    .foregroundStyle(Color.ds("ink-2"))
            }
        }
    }
}

extension PiecePartsSheet {
    /// «Предложить» and «Подтвердить пьесы» for a PDF with notes read (version 0.5): the rows from the notes, and the rows
    /// kept as the pieces layer, which syncs with the web; then the PDF view opens the file again.
    @ViewBuilder func layer(_ file: URL) -> some View {
        Text(text["pieces.suggestHow"])
            .dsType(.caption)
            .foregroundStyle(Color.ds("ink-2"))
            .fixedSize(horizontal: false, vertical: true)
        HStack(spacing: CGFloat.ds("s-3")) {
            Button(text["pieces.suggest"]) {
                Task { await splitter.suggest(file: file, text: text) }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary))
            .disabled(splitter.layerBusy || splitter.making != nil)
            Button(text["pieces.confirmLayer"]) {
                Task {
                    if await splitter.confirm(document: piece.id, file: file, text: text) { await reopen?() }
                }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary))
            .disabled(splitter.layerBusy || splitter.making != nil || splitter.rows.problem != nil)
            if splitter.layerBusy { ProgressView() }
        }
        if let status = splitter.layerStatus {
            Text(status)
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// One piece in the list: its number and Remove, its title, and its first and last pages.
private struct PiecePartRowView: View {
    @Binding var row: PartRow
    let number: Int
    let placeholder: String
    let text: UIText
    let step: (Bool, Int) -> Void
    let remove: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack {
                Text(text.format("parts.piece", String(number))).overline()
                Spacer()
                Button(action: remove) {
                    Icon(name: "trash").frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text.format("parts.pdf.remove", String(number)))
            }
            TextField(placeholder, text: $row.title)
                .modifier(FieldLook())
                .accessibilityLabel(text.format("parts.titleOf", String(number)))
            ViewThatFits(in: .horizontal) {
                HStack(spacing: CGFloat.ds("s-5")) { steppers }
                VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) { steppers }
            }
        }
        .padding(CGFloat.ds("s-4"))
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .accessibilityElement(children: .contain)
    }

    @ViewBuilder private var steppers: some View {
        PageStepper(label: text["parts.pdf.from"], spoken: text.format("parts.pdf.firstOf", String(number)), value: row.first) { step(true, $0) }
        PageStepper(label: text["parts.pdf.to"], spoken: text.format("parts.pdf.lastOf", String(number)), value: row.last) { step(false, $0) }
    }
}

/// A page number with − and + at the size of a fingertip, adjustable as one element by VoiceOver.
private struct PageStepper: View {
    let label: String
    let spoken: String
    let value: Int
    let step: (Int) -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Text(label).dsType(.label).foregroundStyle(Color.ds("ink-2"))
            stepButton("−", by: -1)
            Text(String(value))
                .dsType(.bodyStrong)
                .monospacedDigit()
                .foregroundStyle(Color.ds("ink"))
                .frame(minWidth: CGFloat.ds("s-8"))
            stepButton("+", by: 1)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(spoken)
        .accessibilityValue(String(value))
        .accessibilityAdjustableAction { direction in
            switch direction {
            case .increment: step(1)
            case .decrement: step(-1)
            @unknown default: break
            }
        }
    }

    private func stepButton(_ sign: String, by amount: Int) -> some View {
        Button {
            step(amount)
        } label: {
            Text(sign)
                .dsType(.heading)
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
    }
}

/// What a split made: «Разделено на N пьес · плейлист «…»», each piece with its pages, and «Разделить ещё раз».
private struct PiecePartsDone: View {
    let link: SplitLink
    let text: UIText
    let again: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(PartsSplit.summary(count: link.parts.count, playlist: link.playlist, text: text))
                .dsType(.bodyStrong)
                .foregroundStyle(Color.ds("ink"))
                .fixedSize(horizontal: false, vertical: true)
            ForEach(Array(link.parts.enumerated()), id: \.offset) { _, part in
                HStack {
                    Text(part.title).dsType(.body).foregroundStyle(Color.ds("ink"))
                    Spacer(minLength: CGFloat.ds("s-4"))
                    if let pages = part.pages {
                        Text(PartsSplit.pages(first: pages.lowerBound, last: pages.upperBound, text: text))
                            .dsType(.label)
                            .monospacedDigit()
                            .foregroundStyle(Color.ds("ink-2"))
                    }
                }
                .frame(minHeight: CGFloat.ds("h-touch"))
                .accessibilityElement(children: .combine)
            }
            Text(text["parts.pdf.inLibrary"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
            Button(text["parts.pdf.again"], action: again)
                .buttonStyle(DSButtonStyle(kind: .quiet))
        }
    }
}

/// «Пьесы в этом файле» in the PDF's Contents, with what was split from this piece or where it came from.
struct PiecePartsEntry: View {
    let family: PieceFamily
    let text: UIText
    let open: () -> Void

    private var line: String? {
        if let split = family.split { return PartsSplit.summary(count: split.parts.count, playlist: split.playlist, text: text) }
        return family.origin.map { PartsSplit.from($0, text: text) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Button(action: open) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "pages")
                    Text(text["parts.title"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            if let line {
                Text(line)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}
