import SwiftUI

/// PDF | Notes (0023; Andrei, 2026-11-04): the two views of a piece with notes and a PDF kept inside it (testing item 16,
/// since 2026-11-07), or, for notes read between 2026-11-05 and 2026-11-07, a PDF piece and the notes read from it, linked
/// by the notes' `readFrom`. The switch in the bar goes from one to the other, landing roughly on the same stretch of music
/// by where each of the PDF's pages starts in the notes, as the web app's `ViewSwitch` does.
struct PageMap: Equatable, Sendable {
    /// Where each page starts in the notes, in ticks, from page 1.
    let starts: [Double]

    /// Where a page starts; a page past the last starts where the last does.
    func start(of page: Int) -> Double {
        guard !starts.isEmpty else { return 0 }
        return starts[min(max(page, 1), starts.count) - 1]
    }

    /// The page a place in the notes is on, from 1: the last page that starts at or before it.
    func page(of tick: Double) -> Int {
        var page = 1
        while page < starts.count && starts[page] <= tick {
            page += 1
        }
        return page
    }
}

/// The piece's other view: the project file to open and how the PDF's pages match the notes.
struct OtherView: Equatable, Sendable {
    let file: URL
    let pages: PageMap

    /// The other view of the project at `file`, from the library's summaries: for a piece with notes and a PDF, the same
    /// file in its other view; for notes read from a PDF piece, that PDF piece; for a PDF piece, the notes read from it most
    /// recently opened. Nil when there is none on the device.
    static func of(_ file: URL, in summaries: [URL: PieceSummary]) -> OtherView? {
        guard let summary = summaries[file] else { return nil }
        if summary.hasNotes && summary.document {
            return OtherView(file: file, pages: PageMap(starts: summary.documentPages))
        }
        if let pdf = summary.readFrom {
            guard let found = summaries.first(where: { $0.value.id == pdf && !$0.value.hasNotes }) else { return nil }
            return OtherView(file: found.key, pages: PageMap(starts: summary.readFromPages))
        }
        guard !summary.hasNotes, let id = summary.id else { return nil }
        let notes = summaries
            .filter { $0.value.readFrom == id && $0.value.hasNotes }
            .max { $0.value.lastOpened < $1.value.lastOpened }
        return notes.map { OtherView(file: $0.key, pages: PageMap(starts: $0.value.readFromPages)) }
    }
}

/// Where the other view opens: a PDF at a page, from 1, or the notes at a place in ticks.
enum Landing: Equatable, Sendable {
    case page(Int)
    case tick(Double)

    var page: Int? {
        if case .page(let page) = self { return page }
        return nil
    }

    var tick: Double? {
        if case .tick(let tick) = self { return tick }
        return nil
    }
}

/// The two views of a recognised PDF in the bar (the web app's `ViewSwitch`): the printed pages as they are, and the notes
/// read from them, as PDF | Notes («PDF | Ноты»). The words are the same in both views and on any width, so the switch
/// keeps its size and place when the view changes (#479 item 11; it said Original PDF where the bar had room, which made
/// it jump). Picking the other one calls `other`.
struct PdfNotesSwitch: View {
    let onPdf: Bool
    let text: UIText
    let other: () -> Void

    var body: some View {
        SegmentedChoice(options: [(true, text["pdf.viewPdfShort"]), (false, text["pdf.viewNotes"])], chosen: onPdf, label: text["pdf.views"]) { pdf in
            if pdf != onPdf { other() }
        }
    }
}
