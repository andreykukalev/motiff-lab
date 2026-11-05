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
/// music-core's `document` calls. Nothing on the pages is music the app knows, so nothing here plays or selects.
@MainActor
final class PdfController: ObservableObject {
    @Published private(set) var document: PDFDocument?
    /// The page under the bar, from 1, and the last page in view with two to a row.
    @Published private(set) var page = 0
    @Published private(set) var lastPage = 0
    @Published private(set) var pageCount = 0
    @Published private(set) var bookmarks: [PageBookmarkEntry] = []
    @Published var problem: String?
    /// The PDF written to a temporary file under its own name, for Download the PDF (the share sheet).
    @Published private(set) var shareable: URL?

    /// The page to open at once the pages are laid out.
    private(set) var startPage = 1
    weak var view: PDFView?
    private var piece: Int32 = 0
    private var projectId: String?

    /// The bookmark on the page under the bar, if any.
    var currentBookmark: PageBookmarkEntry? { bookmarks.first { $0.page == page } }

    /// The page the player was last on in this PDF, kept on the device as the web app keeps it (`pdf.page.<id>`).
    static func pageKey(_ id: String) -> String { "motiff.pdf.page.\(id)" }

    func load(piece: Int32, text: UIText) async {
        self.piece = piece
        let answers = await MusicCore.run { (MusicCore.call(piece: piece, "document"), MusicCore.call(piece: piece, "project")) }
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
        }
        bookmarks = Self.bookmarks(object["marks"])
        let name = object["fileName"] as? String ?? "score.pdf"
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(name.isEmpty ? "score.pdf" : name)
        if (try? data.write(to: url, options: .atomic)) != nil { shareable = url }
        pageCount = read.pageCount
        document = read
        markPages()
    }

    /// The pages in view changed: the page under the bar, kept on the device.
    func shown(first: Int, last: Int) {
        guard first > 0, first != page || last != lastPage else { return }
        page = first
        lastPage = max(first, last)
        if let projectId { UserDefaults.standard.set(first, forKey: Self.pageKey(projectId)) }
    }

    func go(to page: Int) {
        guard let view, let document, let target = document.page(at: max(0, min(page, document.pageCount) - 1)) else { return }
        view.go(to: target)
    }

    /// The bookmark in the bar: marks the page under it, or takes its bookmark away.
    func toggleBookmark() async {
        guard page > 0 else { return }
        if let bookmark = currentBookmark {
            await change("document.bookmark.remove", ["id": bookmark.id])
        } else {
            await change("document.bookmark.add", ["page": page, "now": ISO8601DateFormatter().string(from: Date())])
        }
    }

    func rename(_ bookmark: PageBookmarkEntry, _ name: String) async {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed != (bookmark.name ?? "") else { return }
        await change("document.bookmark.rename", ["id": bookmark.id, "name": trimmed])
    }

    func remove(_ bookmark: PageBookmarkEntry) async {
        await change("document.bookmark.remove", ["id": bookmark.id])
    }

    private func change(_ call: String, _ fields: [String: Any]) async {
        guard let body = try? JSONSerialization.data(withJSONObject: fields), let request = String(data: body, encoding: .utf8) else { return }
        let piece = piece
        let answer = await MusicCore.run { MusicCore.call(piece: piece, call, request) }
        guard let object = facadeObject(answer) else { return }
        if object["error"] != nil {
            problem = object["message"] as? String
            return
        }
        bookmarks = Self.bookmarks(object["marks"])
        markPages()
    }

    /// The carmine tab on each bookmarked page's right edge, near the top (the web app's `.pdf-page.bookmarked`).
    private func markPages() {
        guard let document else { return }
        let marked = Set(bookmarks.map(\.page))
        for index in 0..<document.pageCount {
            guard let page = document.page(at: index) else { continue }
            for annotation in page.annotations where annotation is BookmarkTab {
                page.removeAnnotation(annotation)
            }
            if marked.contains(index + 1) {
                page.addAnnotation(BookmarkTab(on: page))
            }
        }
    }

    static func bookmarks(_ marks: Any?) -> [PageBookmarkEntry] {
        let list = (marks as? [String: Any])?["bookmarks"] as? [[String: Any]] ?? []
        return list.compactMap { entry in
            guard let id = entry["id"] as? String, let page = (entry["page"] as? NSNumber)?.intValue else { return nil }
            return PageBookmarkEntry(id: id, page: page, name: entry["name"] as? String)
        }
    }
}

/// A bookmarked page's tab: a carmine strip on the page's right edge at 6% from the top, drawn by PDFKit with the page and
/// kept only in memory (the PDF is never written back; the bookmarks live in the project).
final class BookmarkTab: PDFAnnotation {
    private var fill = UIColor.systemRed.cgColor
    private var radius: CGFloat = 2

    init(on page: PDFPage) {
        let box = page.bounds(for: .cropBox)
        let width = max(6, box.width * 0.016)
        let height = width * 3.2
        let bounds = CGRect(x: box.maxX - width, y: box.maxY - box.height * 0.06 - height, width: width, height: height)
        super.init(bounds: bounds, forType: .square, withProperties: nil)
        // The page is paper in both appearances, so the tab takes the daylight carmine.
        fill = DesignTokens.dynamicColor("ink-carmine").resolvedColor(with: UITraitCollection(userInterfaceStyle: .light)).cgColor
        radius = min(width / 2, DesignTokens.length("r-xs"))
        shouldDisplay = true
        shouldPrint = false
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
    }

    override func draw(with box: PDFDisplayBox, in context: CGContext) {
        context.saveGState()
        let path = UIBezierPath(roundedRect: bounds, byRoundingCorners: [.topLeft, .bottomLeft], cornerRadii: CGSize(width: radius, height: radius))
        context.addPath(path.cgPath)
        context.setFillColor(fill)
        context.fillPath()
        context.restoreGState()
    }
}

/// The pages in one continuously scrolling document (0018 section 2): one column in portrait, two to a row in landscape,
/// on the desk, each at its own proportions; PDFKit draws pages as they come near and lets go of them when far.
struct PdfPages: UIViewRepresentable {
    let document: PDFDocument
    let twoUp: Bool
    /// The room the floating bar takes over the first page.
    let top: CGFloat
    let controller: PdfController

    func makeCoordinator() -> Coordinator { Coordinator(controller: controller) }

    func makeUIView(context: Context) -> PDFView {
        let view = PDFView()
        view.displayDirection = .vertical
        view.displayMode = twoUp ? .twoUpContinuous : .singlePageContinuous
        view.displaysPageBreaks = true
        view.pageShadowsEnabled = true
        view.pageBreakMargins = UIEdgeInsets(top: CGFloat.ds("s-4"), left: CGFloat.ds("s-4"), bottom: CGFloat.ds("s-4"), right: CGFloat.ds("s-4"))
        view.backgroundColor = DesignTokens.dynamicColor("desk")
        view.document = document
        view.autoScales = true
        controller.view = view
        context.coordinator.watch(view)
        inset(view)
        if let start = document.page(at: controller.startPage - 1) {
            DispatchQueue.main.async { view.go(to: start) }
        }
        return view
    }

    func updateUIView(_ view: PDFView, context: Context) {
        let mode: PDFDisplayMode = twoUp ? .twoUpContinuous : .singlePageContinuous
        if view.displayMode != mode {
            let at = view.currentPage
            view.displayMode = mode
            view.autoScales = true
            if let at { view.go(to: at) }
        }
        inset(view)
        // The tabs of pages bookmarked since are drawn with the pages again.
        view.setNeedsDisplay()
        view.documentView?.setNeedsDisplay()
    }

    /// The first page starts below the bar, which floats over the pages as it does over the score.
    private func inset(_ view: PDFView) {
        guard let scroll = view.subviews.compactMap({ $0 as? UIScrollView }).first, scroll.contentInset.top != top else { return }
        scroll.contentInset.top = top
    }

    @MainActor
    final class Coordinator: NSObject {
        let controller: PdfController
        private weak var view: PDFView?

        init(controller: PdfController) {
            self.controller = controller
        }

        func watch(_ view: PDFView) {
            self.view = view
            NotificationCenter.default.addObserver(self, selector: #selector(pageChanged), name: .PDFViewPageChanged, object: view)
            NotificationCenter.default.addObserver(self, selector: #selector(pageChanged), name: .PDFViewVisiblePagesChanged, object: view)
        }

        @objc func pageChanged(_ notification: Notification) {
            guard let view, let document = view.document else { return }
            let visible = view.visiblePages.map { document.index(for: $0) + 1 }.sorted()
            let first = view.currentPage.map { document.index(for: $0) + 1 } ?? visible.first ?? 0
            let last = view.displayMode == .twoUpContinuous ? (visible.filter { $0 >= first }.prefix(2).last ?? first) : first
            controller.shown(first: first, last: last)
        }
    }
}

/// A PDF piece's screen (0023, the web app's `/pdf`): the score's paper with the printed pages on it, the bar over the top
/// with the way back, the title, which opens the bookmarked pages, the page under the bar, the PDF tag, the bookmark for
/// the page and the overflow with Download the PDF. Nothing on it plays; the pen on the pages and the metronome come next.
struct PdfScreen: View {
    let piece: OpenPiece
    private let text = UIText.current
    @Environment(\.dismiss) private var dismiss
    @Environment(\.scenePhase) private var scenePhase
    @StateObject private var pdf = PdfController()
    @State private var contentsOpen = false
    @State private var more = false

    var body: some View {
        GeometryReader { screen in
            ZStack(alignment: .top) {
                Color.ds("desk").ignoresSafeArea()
                if let document = pdf.document {
                    PdfPages(
                        document: document,
                        twoUp: screen.size.width > screen.size.height,
                        top: CGFloat.ds("h-touch") + CGFloat.ds("s-3"),
                        controller: pdf
                    )
                    .ignoresSafeArea(edges: .bottom)
                } else if pdf.problem == nil {
                    ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
                }
                if let problem = pdf.problem {
                    Text(problem)
                        .dsType(.label)
                        .foregroundStyle(Color.ds("danger"))
                        .padding(CGFloat.ds("s-4"))
                        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                        .padding(.top, CGFloat.ds("h-touch") + CGFloat.ds("s-6"))
                        .accessibilityAddTraits(.isStaticText)
                }
                bar
                    .padding(.horizontal, CGFloat.ds("s-3"))
                    .padding(.top, CGFloat.ds("s-2"))
            }
        }
        .toolbar(.hidden, for: .navigationBar)
        .task { await pdf.load(piece: piece.id, text: text) }
        .onDisappear { save() }
        .onChange(of: scenePhase) { _, phase in
            if phase == .background { save() }
        }
    }

    private var bar: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button {
                dismiss()
            } label: {
                Icon(name: "back", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["nav.library"])
            Button {
                contentsOpen.toggle()
            } label: {
                Text(piece.title)
                    .font(DesignTokens.font(display: true, weight: 500, size: 17, relativeTo: .headline))
                    .foregroundStyle(contentsOpen ? Color.ds("accent") : Color.ds("ink"))
                    .lineLimit(1)
                    .padding(.horizontal, CGFloat.ds("s-2"))
                    .frame(height: 40)
            }
            .buttonStyle(BarActionStyle(on: contentsOpen))
            .accessibilityLabel(text.format("contents.open", piece.title))
            .accessibilityAddTraits(.isHeader)
            .popover(isPresented: $contentsOpen) {
                PdfContents(pdf: pdf, text: text) { page in
                    contentsOpen = false
                    pdf.go(to: page)
                }
            }
            if let pageText {
                Text(pageText)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
                    .lineLimit(1)
                    .padding(.leading, CGFloat.ds("s-2"))
            }
            Tag(text: text["pdf.title"], kind: .assumed)
            Spacer(minLength: CGFloat.ds("s-2"))
            Button {
                Task { await pdf.toggleBookmark() }
            } label: {
                Icon(name: "bookmark", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: pdf.currentBookmark != nil))
            .disabled(pdf.page == 0)
            .accessibilityLabel(text["pdf.bookmarkPage"])
            .accessibilityAddTraits(pdf.currentBookmark != nil ? .isSelected : [])
            Button {
                more.toggle()
            } label: {
                Icon(name: "more", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: more))
            .accessibilityLabel(text["bar.more"])
            .popover(isPresented: $more) { PdfMore(pdf: pdf, text: text) }
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
    }

    /// "p. 2 of 5", or "pp. 2–3 of 5" with two pages to a row.
    private var pageText: String? {
        guard pdf.page > 0 else { return nil }
        return pdf.page == pdf.lastPage
            ? text.format("bar.page", String(pdf.page), String(pdf.pageCount))
            : text.format("bar.pages", String(pdf.page), String(pdf.lastPage), String(pdf.pageCount))
    }

    private func save() {
        guard let file = piece.file else { return }
        let id = piece.id
        Task { try? await ProjectFiles.save(piece: id, to: file) }
    }
}

/// The PDF's Contents (the web app's `.pdf-contents`): the bookmarked pages, to go to, name or remove.
struct PdfContents: View {
    @ObservedObject var pdf: PdfController
    let text: UIText
    let go: (Int) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["pdf.bookmarks"]).overline().accessibilityAddTraits(.isHeader)
            if pdf.bookmarks.isEmpty {
                Text(text["pdf.noBookmarks"])
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
            }
            ForEach(pdf.bookmarks) { bookmark in
                PdfBookmarkRow(bookmark: bookmark, text: text) {
                    go(bookmark.page)
                } rename: { name in
                    Task { await pdf.rename(bookmark, name) }
                } remove: {
                    Task { await pdf.remove(bookmark) }
                }
            }
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 360, alignment: .leading)
        .background(Color.ds("surface"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["bar.contents"])
    }
}

struct PdfBookmarkRow: View {
    let bookmark: PageBookmarkEntry
    let text: UIText
    let go: () -> Void
    let rename: (String) -> Void
    let remove: () -> Void
    @State private var name = ""

    var body: some View {
        let place = text.format("contents.page", String(bookmark.page))
        HStack(spacing: CGFloat.ds("s-2")) {
            Button(action: go) {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: "bookmark").foregroundStyle(Color.ds("ink-carmine"))
                    Text(place)
                        .dsType(.label)
                        .monospacedDigit()
                        .foregroundStyle(Color.ds("ink"))
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            .accessibilityLabel(text.format("pdf.goTo", bookmark.name ?? place))
            TextField(text["pdf.bookmarkName"], text: $name)
                .dsType(.body)
                .textFieldStyle(.roundedBorder)
                .submitLabel(.done)
                .onSubmit { rename(String(name.prefix(80))) }
                .accessibilityLabel(text.format("pdf.bookmarkNameFor", place))
            Button(action: remove) {
                Icon(name: "trash").frame(width: 32, height: 32)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text.format("pdf.removeBookmark", place))
            .accessibilityHint(text["pdf.removeBookmarkHint"])
        }
        .onAppear { name = bookmark.name ?? "" }
        .onDisappear { rename(String(name.prefix(80))) }
    }
}

/// The PDF's overflow: Download the PDF (the bytes as added, through the share sheet), what the pages are and where they
/// are kept. Read the notes comes with scanning on the iPad.
struct PdfMore: View {
    @ObservedObject var pdf: PdfController
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            if let url = pdf.shareable {
                ShareLink(item: url) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "export")
                        Text(text["pdf.download"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
            Text(text["pdf.asIs"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
            Text(text["pdf.onDevice"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 320, alignment: .leading)
        .background(Color.ds("surface"))
    }
}
