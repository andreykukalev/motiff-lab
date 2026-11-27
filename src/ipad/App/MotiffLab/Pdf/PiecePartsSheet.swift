import SwiftUI

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
        _splitter = StateObject(wrappedValue: PieceSplitter(rows: PartRows.fromBookmarks(pdf.bookmarks, pageCount: pdf.pageCount), family: pdf.family, title: piece.title))
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
                pages: splitter.rows.pages,
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
                guard let original = pdf.original else { return }
                Task {
                    if let made = await splitter.split(piece: piece.id, original: original, text: text) { pdf.family.split = made }
                }
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
    /// The file's pages, which the counters keep within (`PartRows.pages`).
    let pages: ClosedRange<Int>
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
        PageStepper(label: text["parts.pdf.from"], spoken: text.format("parts.pdf.firstOf", String(number)), value: row.first, range: pages) { step(true, $0) }
        PageStepper(label: text["parts.pdf.to"], spoken: text.format("parts.pdf.lastOf", String(number)), value: row.last, range: pages) { step(false, $0) }
    }
}

/// A page number with − and + at the size of a fingertip, adjustable as one element by VoiceOver; since v1.4 also a
/// draft's place's first and last bar (`DraftLengthRow`). A − or + held steps on and on, faster the longer it is held, as
/// the platform's steppers do (v1.5, answer 15: a place of a 300-bar piece is not 300 taps); within `range` (the file's
/// pages, a place's bars), where given, the − at its lower end and the + at its upper end are off, which also ends a hold
/// there.
struct PageStepper: View {
    let label: String
    let spoken: String
    let value: Int
    var range: ClosedRange<Int>?
    let step: (Int) -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Text(label).dsType(.label).foregroundStyle(Color.ds("ink-2"))
            stepButton("−", by: -1, off: range.map { value <= $0.lowerBound } ?? false)
            Text(String(value))
                .dsType(.bodyStrong)
                .monospacedDigit()
                .foregroundStyle(Color.ds("ink"))
                .frame(minWidth: CGFloat.ds("s-8"))
            stepButton("+", by: 1, off: range.map { value >= $0.upperBound } ?? false)
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

    private func stepButton(_ sign: String, by amount: Int, off: Bool) -> some View {
        Button {
            step(amount)
        } label: {
            Text(sign)
                .dsType(.heading)
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
        .buttonRepeatBehavior(.enabled)
        .disabled(off)
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
