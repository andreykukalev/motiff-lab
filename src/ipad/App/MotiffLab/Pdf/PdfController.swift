import PDFKit
import SwiftUI

/// A page the player bookmarked in a PDF piece, as music-core answers `document.marks` (0023).
struct PageBookmarkEntry: Identifiable, Equatable, Sendable {
    var id: String
    var page: Int
    var name: String?
}

/// The PDF piece open on the screen (0023, the web app's PdfReader): the pages as the player added them, read by PDFKit,
/// the page under the bar, kept on the device to reopen at, and the bookmarked pages, kept with the project through
/// music-core's `document` calls. Nothing on the pages is music the app knows, so nothing here plays or selects. The pen
/// writes on the pages as on the score (PdfInk.swift): each stroke kept against its page, in the page's own spaces.
@MainActor
final class PdfController: NSObject, ObservableObject, PenHolder {
    @Published private(set) var document: PDFDocument?
    /// The page under the bar, from 1, and the last page in view with two to a row.
    @Published private(set) var page = 0
    @Published private(set) var lastPage = 0
    @Published private(set) var pageCount = 0
    @Published private(set) var bookmarks: [PageBookmarkEntry] = []
    @Published var problem: String?
    /// The PDF written to a temporary file under its own name, for Download the PDF (the share sheet).
    @Published private(set) var shareable: URL?
    /// The PDF exactly as the player added it, and its file name, for Read the notes.
    private(set) var original: Data?
    /// The PDF is the player's photos of pages (v0.4, «Фото страниц»), read page by page.
    @Published private(set) var isPhotos = false
    private(set) var fileName = "score.pdf"
    /// The pieces split from this one and where it was split from (v0.4, «Пьесы в файле»): the library's, then this
    /// screen's own split.
    @Published var family = PieceFamily()

    /// The bar's Pen: while it is on, the Pencil writes; on an iPad without a stylus one finger writes and two scroll
    /// (InputRules, #479 item 18). A finger always scrolls while the iPad is used with a stylus.
    @Published var penOn = false { didSet { applyInput() } }
    /// Whether the pages take ink: not on a PDF linked in Files, whose marks would be kept nowhere (version 0.3).
    var inkable = true
    /// The pen's one tool and each tool's settings (PenState, #495 UX-06), as last chosen on this device (PenMemory): the
    /// same as on the score, after PDF | Notes too.
    @Published var pen = PenMemory.saved() {
        didSet { if pen != oldValue { PenMemory.keep(pen) } }
    }
    @Published var canUndo = false
    /// Whether a step Undo took back can be done again (#533 9), as on the score.
    @Published var canRedo = false
    /// The pencil palette, opened by the Pencil's double tap or squeeze (#479 item 8); nil while it is closed.
    @Published var pencilPalette: PencilPaletteCall?
    /// The strokes on the pages, as music-core answered the last call.
    @Published private(set) var strokes: [DocumentStroke] = []

    /// The page to open at once the pages are laid out, or the place the player left the pages at (#511 section 10).
    private(set) var startPage = 1
    private(set) var startState: PdfViewState?
    weak var view: PDFView?
    /// The pen over the pages, and who writes and who scrolls (#479 items 7 and 18).
    let ink = InkInput()
    /// What the pen and the eraser did here, for Undo and Redo (#533 9): a stroke kept, or a press of the eraser (BUG-05).
    var steps = InkHistory()
    /// The eraser on a page while it is pressed, and the presses lifted and still being saved, shown as they left the
    /// pages (PdfInk.swift, BUG-05).
    var rubbing: PageEraser?
    var closing: [PageEraser] = []
    /// The ink's changes, one after another: the eraser's saves and Undo.
    let inkQueue = InkQueue()
    private(set) var piece: Int32 = 0
    private(set) var projectId: String?

    override init() {
        super.init()
        ProbeCounts.shared.add("pdf")
    }

    deinit {
        ProbeCounts.shared.add("pdf", -1)
    }

    /// The bookmark on the page under the bar, if any.
    var currentBookmark: PageBookmarkEntry? { bookmarks.first { $0.page == page } }

    /// The page the player was last on in this PDF, kept on the device as the web app keeps it (`pdf.page.<id>`).
    static func pageKey(_ id: String) -> String { ViewStates.pdfPageKey(id) }

    /// Loads the PDF piece's pages and marks; it opens at `page` when given (from PDF | Notes), else where the player left it.
    func load(piece: Int32, text: UIText, page: Int? = nil) async {
        self.piece = piece
        #if DEBUG
        ScreensProbe.pdf = self
        #endif
        let answers = await MusicCore.run { (PieceCore(piece: piece).send(.document), PieceCore(piece: piece).send(.project)) }
        ProbeCounts.shared.mark("document")
        guard let object = facadeObject(answers.0), object["error"] == nil,
              let base64 = object["pdf"] as? String, let data = Data(base64Encoded: base64),
              let read = PDFDocument(data: data), read.pageCount > 0 else {
            problem = text["pdf.unreadable"]
            return
        }
        projectId = (facadeObject(answers.1)?["project"] as? [String: Any])?["id"] as? String
        if let projectId {
            let kept = UserDefaults.standard.integer(forKey: Self.pageKey(projectId))
            startPage = kept > 0 ? min(kept, read.pageCount) : 1
            startState = page == nil ? ViewStates.pdf(projectId, shownPage: kept).flatMap { $0.page <= read.pageCount ? $0 : nil } : nil
        }
        if let page, page > 0 { startPage = min(page, read.pageCount) }
        bookmarks = Self.bookmarks(object["marks"])
        strokes = DocumentStroke.list(object["marks"])
        isPhotos = object["kind"] as? String == "images"
        let name = object["fileName"] as? String ?? "score.pdf"
        original = data
        fileName = name.isEmpty ? "score.pdf" : name
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(name.isEmpty ? "score.pdf" : name)
        if (try? data.write(to: url, options: .atomic)) != nil { shareable = url }
        pageCount = read.pageCount
        document = read
        ProbeCounts.shared.mark("pdfshown")
        markPages()
    }

    /// The pages in view changed: the page under the bar, kept on the device.
    func shown(first: Int, last: Int) {
        guard first > 0, first != page || last != lastPage else { return }
        page = first
        lastPage = max(first, last)
        if let projectId { UserDefaults.standard.set(first, forKey: Self.pageKey(projectId)) }
    }

    /// Brings a page to the top of the view, just under the bar (#495 UX-12), as the notes bring theirs.
    func go(to page: Int) {
        guard let view, let document, let target = document.page(at: max(0, min(page, document.pageCount) - 1)) else { return }
        if let pages = view as? BarPdfView {
            pages.rest(on: target)
        } else {
            view.go(to: target)
        }
    }

    /// The bookmark in the bar: marks the page under it, or takes its bookmark away.
    func toggleBookmark() async {
        guard page > 0 else { return }
        if let bookmark = currentBookmark {
            await change(.documentBookmarkRemove, ["id": bookmark.id])
        } else {
            await change(.documentBookmarkAdd, ["page": page, "now": ISO8601DateFormatter().string(from: Date())])
        }
    }

    func rename(_ bookmark: PageBookmarkEntry, _ name: String) async {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed != (bookmark.name ?? "") else { return }
        await change(.documentBookmarkRename, ["id": bookmark.id, "name": trimmed])
    }

    func remove(_ bookmark: PageBookmarkEntry) async {
        await change(.documentBookmarkRemove, ["id": bookmark.id])
    }

    /// A change through music-core; the pages are drawn from its answer unless `draws` is false, for the first of two calls
    /// that make one change, so they never show it half done.
    func change(_ call: PieceCore.Call, _ fields: [String: Any], draws: Bool = true) async {
        let request = facadeRequest(fields)
        let piece = piece
        let answer = await MusicCore.run { PieceCore(piece: piece).send(call, request) }
        guard let object = facadeObject(answer) else { return }
        if object["error"] != nil {
            problem = object["message"] as? String
            return
        }
        guard draws else { return }
        bookmarks = Self.bookmarks(object["marks"])
        strokes = DocumentStroke.list(object["marks"])
        markPages()
    }

    /// The strokes on each page and the carmine tab on each bookmarked page's right edge, near the top (the web app's
    /// `.pdf-page.bookmarked`), drawn by PDFKit with the pages; the strokes as the eraser left them while it is pressed.
    func markPages() {
        guard let document else { return }
        let marked = Set(bookmarks.map(\.page))
        let inked = Dictionary(grouping: shownStrokes, by: \.page)
        for index in 0..<document.pageCount {
            guard let page = document.page(at: index) else { continue }
            for annotation in page.annotations where annotation is BookmarkTab || annotation is InkMark {
                page.removeAnnotation(annotation)
            }
            for mark in InkMark.marks(inked[index + 1] ?? [], on: page) {
                page.addAnnotation(mark)
            }
            if marked.contains(index + 1) {
                page.addAnnotation(BookmarkTab(on: page))
            }
        }
        view?.documentView?.setNeedsDisplay()
    }

    static func bookmarks(_ marks: Any?) -> [PageBookmarkEntry] {
        let list = (marks as? [String: Any])?["bookmarks"] as? [[String: Any]] ?? []
        return list.compactMap { entry in
            guard let id = entry["id"] as? String, let page = (entry["page"] as? NSNumber)?.intValue else { return nil }
            return PageBookmarkEntry(id: id, page: page, name: entry["name"] as? String)
        }
    }
}

extension PdfController {
    /// Keeps where the pages are on the device (#511 section 10), with the page under the bar; nothing before they are laid out.
    func keepView() {
        guard let projectId, let pages = view as? BarPdfView, let state = pages.viewState() else { return }
        ViewStates.keep(state, projectId)
    }
}
