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

    /// The bookmark on the page under the bar, if any.
    var currentBookmark: PageBookmarkEntry? { bookmarks.first { $0.page == page } }

    /// The page the player was last on in this PDF, kept on the device as the web app keeps it (`pdf.page.<id>`).
    static func pageKey(_ id: String) -> String { ViewStates.pdfPageKey(id) }

    /// Loads the PDF piece's pages and marks; it opens at `page` when given (from PDF | Notes), else where the player left it.
    func load(piece: Int32, text: UIText, page: Int? = nil) async {
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

    /// A change through music-core; the pages are drawn from its answer unless `draws` is false, for the first of two calls
    /// that make one change, so they never show it half done.
    func change(_ call: String, _ fields: [String: Any], draws: Bool = true) async {
        guard let body = try? JSONSerialization.data(withJSONObject: fields), let request = String(data: body, encoding: .utf8) else { return }
        let piece = piece
        let answer = await MusicCore.run { MusicCore.call(piece: piece, call, request) }
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
    /// Where the floating bar ends, from the top of the pages' view, as it is laid out: the pages rest below it (#495 UX-12).
    let barBottom: CGFloat
    let controller: PdfController

    func makeCoordinator() -> Coordinator { Coordinator(controller: controller) }

    func makeUIView(context: Context) -> BarPdfView {
        let view = BarPdfView()
        view.barBottom = barBottom
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
        controller.attach(to: view)
        // The place the player left the pages at, or the page PDF | Notes landed on: under the bar once the pages are laid out.
        if let state = controller.startState { view.comeBack(to: state) } else if let start = document.page(at: controller.startPage - 1) { view.rest(on: start) }
        return view
    }

    func updateUIView(_ view: BarPdfView, context: Context) {
        let mode: PDFDisplayMode = twoUp ? .twoUpContinuous : .singlePageContinuous
        if view.displayMode != mode {
            let at = view.currentPage
            view.displayMode = mode
            view.autoScales = true
            if let at { view.rest(on: at) }
        }
        view.barBottom = barBottom
        // The tabs of pages bookmarked since are drawn with the pages again.
        view.setNeedsDisplay()
        view.documentView?.setNeedsDisplay()
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

/// The PDF screen's space, from the top of the pages' view, in which the floating bar is measured (#495 UX-12).
private let pdfScreenSpace = "pdf.screen"

/// A PDF piece's screen (0023, the web app's `/pdf`): the score's paper with the printed pages on it, the bar over the top
/// with the way back, the title, which opens the bookmarked pages, the page under the bar, the PDF tag, the bookmark for
/// the page, the metronome, the pen and the overflow with Download the PDF. The pen's palette comes under the bar as on
/// the score, and the metronome's pill floats at the bottom where the score's transport does. No notes play here.
struct PdfScreen: View {
    let piece: OpenPiece
    /// Opens the piece's other view in its place, for PDF | Notes (0023); nil where the library has none.
    var switchView: ((Landing) async -> Void)? = nil
    /// Opens a project file just written, at a place, in this piece's place: the notes read from the PDF.
    var openRead: ((URL, Landing) async -> Void)? = nil
    /// The PDF linked where it lies in Files (version 0.3), shown as it is: marks and bookmarks on its pages are kept with
    /// a copy in the piece, so the pen and the bookmark are not offered here.
    var linked = false
    @EnvironmentObject private var account: Account
    @ObservedObject private var scan = ScanServer.shared
    @ObservedObject private var settings = DeviceSettings.shared
    @State private var readingOpen = false
    @State private var settingsOpen = false
    @State private var settingsNext = false
    private let text = UIText.current
    @Environment(\.dismiss) private var dismiss
    @Environment(\.scenePhase) private var scenePhase
    @StateObject private var pdf = PdfController()
    @StateObject private var photos = PhotoReader()
    @ObservedObject private var metronome = Metronome.shared
    @State private var contentsOpen = false
    /// «Пьесы в этом файле» (v0.4, experiment «Пьесы в файле»), opened from the Contents.
    @State private var partsOpen = false
    @State private var more = false
    @State private var metronomeOpen = false
    @State private var referenceOpen = false
    /// The floating bar's bottom edge as laid out, in the screen's space; nil before it is.
    @State private var measuredBarBottom: CGFloat?

    var body: some View {
        GeometryReader { screen in
            ZStack(alignment: .top) {
                Color.ds("desk").ignoresSafeArea()
                if let document = pdf.document {
                    PdfPages(document: document, twoUp: screen.size.width > screen.size.height, barBottom: barBottom, controller: pdf)
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
                VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
                    bar
                    if pdf.penOn {
                        PdfPenPalette(pdf: pdf, text: text)
                            .transition(.opacity.combined(with: .move(edge: .top)))
                    }
                }
                // Where the score's bar floats (ds.css `.bar`), so PDF | Notes leaves it in place (#479 item 13).
                .padding(.horizontal, CGFloat.ds("s-4"))
                .padding(.top, CGFloat.ds("s-3"))
                .animation(.easeOut(duration: 0.16), value: pdf.penOn)
                if let call = pdf.pencilPalette {
                    // The pencil palette, opened by the Pencil's double tap or squeeze (#479 item 8).
                    PencilPalette(holder: pdf, call: call, text: text)
                }
            }
            .animation(.easeOut(duration: 0.16), value: pdf.pencilPalette)
            .coordinateSpace(.named(pdfScreenSpace))
            .overlay(alignment: .bottom) {
                if metronomeOpen {
                    MetronomePill(metronome: metronome, text: text) {
                        metronome.stop()
                        metronomeOpen = false
                    }
                    .padding(.bottom, CGFloat.ds("s-6"))
                    .transition(.opacity)
                }
            }
            .animation(.easeOut(duration: 0.2), value: metronomeOpen)
        }
        .toolbar(.hidden, for: .navigationBar)
        .sheet(isPresented: $readingOpen, onDismiss: {
            if settingsNext {
                settingsNext = false
                settingsOpen = true
            }
        }) {
            if pdf.isPhotos && DeviceSettings.shared.isOn("experimental.photoPages") {
                PhotoReadingPanel(reader: photos, pdf: pdf, address: scanAddress, text: text) { read in
                    readPanel(read, heading: text["photos.readHeading"], keeps: text["photos.readKeeps"], line: "photos.readLine")
                } open: { musicXml, engine, pages, reports in
                    await fill(musicXml, engine: engine, pages: pages, reports: reports)
                }
                .presentationDetents([.large])
            } else {
                readPanel(readNotes).fittedSheet()
            }
        }
        .sheet(isPresented: $referenceOpen) {
            ReferenceSheet(text: text, settings: settings, piece: piece.id)
        }
        .sheet(isPresented: $settingsOpen) {
            SettingsSheet(settings: DeviceSettings.shared, account: account, text: text, section: .account)
        }
        .sheet(isPresented: $partsOpen) {
            PiecePartsSheet(piece: piece, pdf: pdf, text: text, reopen: reopenParts).presentationDetents([.large])
        }
        .task {
            pdf.inkable = !linked
            pdf.family = piece.family
            await pdf.load(piece: piece.id, text: text, page: piece.landing?.page)
            metronome.load(piece: pdf.projectId)
            // Handed from the notes, the metronome may click already (#521 section 15): its pill shows with it.
            if metronome.running { metronomeOpen = true }
        }
        .onDisappear {
            // Handed to the notes, the piece was saved and closed already (#511 section 10); the metronome goes on there.
            guard !ClosedPieces.take(piece.id) else { return }
            metronome.stop()
            pdf.keepView()
            save()
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .background {
                pdf.keepView()
                save()
            }
            // The iPad's setting «Only draw with Apple Pencil» may have changed meanwhile.
            if phase == .active { pdf.applyInput() }
        }
    }

    private var bar: some View {
        // Recognise notes with its word where the whole bar fits, else its icon alone.
        ViewThatFits(in: .horizontal) {
            barContent(compact: false)
            barContent(compact: true)
        }
        // Where it ends, for the pages to rest below it (#495 UX-12).
        .onGeometryChange(for: CGFloat.self) { $0.frame(in: .named(pdfScreenSpace)).maxY } action: { measuredBarBottom = $0 }
    }

    /// Where the floating bar ends, from the top of the pages: as laid out, and until then its top margin and its height.
    private var barBottom: CGFloat { measuredBarBottom ?? CGFloat.ds("s-3") + CGFloat.ds("h-touch") }

    /// Notes in PDF | Notes: the notes read from this PDF, in this piece or the one they were read into; for the PDF linked
    /// in Files, which is only ever shown over its notes, those notes again (#495 BUG-02: it showed a tag in the switch's
    /// place, so «Ноты» was missing though the notes were right under it). Nil for a PDF with no notes yet.
    private var toNotes: (() -> Void)? {
        if linked { return { dismiss() } }
        guard switchView != nil, piece.other != nil else { return nil }
        return { Task { await showNotes() } }
    }

    /// Laid out as the score's bar (ScoreBar, #479 item 13): Back, the Contents, the title and PDF | Notes in the same places
    /// in both views, then the pages in view, and this view's own actions at the right.
    private func barContent(compact: Bool) -> some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            barLeading
            if let toNotes {
                PdfNotesSwitch(onPdf: true, text: text, other: toNotes)
            } else {
                Tag(text: text["pdf.title"], kind: .assumed)
            }
            if let pageText { BarPages(text: pageText) }
            // Why this PDF takes no pen or bookmark: it is read where it lies, after the places it shares with the notes.
            if linked { Tag(text: text["originals.linkFiles"], kind: .assumed) }
            Spacer(minLength: CGFloat.ds("s-2"))
            // Recognise notes, in the bar while the PDF has pages and no notes yet (Andrei, 2026-11-11: in More it was not found).
            if let readNotesAction, pdf.pageCount > 0 {
                Button(action: readNotesAction) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "roll", size: CGFloat.ds("icon-l"))
                        if !compact {
                            Text(text["pdf.readNotes"]).dsType(.label).fontWeight(.semibold).lineLimit(1)
                        }
                        if hosted { Tag(text: "Pro", kind: .saved) }
                    }
                    .padding(.horizontal, CGFloat.ds("s-2"))
                    .frame(height: CGFloat.ds("h-control"))
                }
                .buttonStyle(BarActionStyle(on: readingOpen))
                .disabled(pdf.original == nil)
                .accessibilityLabel(text["pdf.readNotes"])
                .accessibilityValue(hosted ? "Pro" : "")
            }
            barActions
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
    }

    /// Back, the Contents and the title, which opens them too, as on the score.
    private var barLeading: some View {
        BarLeading(title: piece.title, contentsOpen: $contentsOpen, text: text) { dismiss() }
            .popover(isPresented: $contentsOpen) {
                PdfContents(pdf: pdf, text: text, parts: openParts) { page in
                    contentsOpen = false
                    pdf.go(to: page)
                }
            }
    }

    private func toggleMetronome() {
        metronome.toggle()
        metronomeOpen = metronome.running
    }

    /// Bookmark the page and the metronome, then how the pages are used (the modes, or the Pen before them), then the
    /// Circle and More: the modes, the Circle and More where the score's bar has them (the approved mockup v0.5.2, #479
    /// item 21).
    @ViewBuilder private var barActions: some View {
        if !linked {
            Button {
                Task { await pdf.toggleBookmark() }
            } label: {
                Icon(name: "bookmark", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: pdf.currentBookmark != nil))
            .disabled(pdf.page == 0)
            .accessibilityLabel(text["pdf.bookmarkPage"])
            .accessibilityAddTraits(pdf.currentBookmark != nil ? .isSelected : [])
        }
        // The metronome starts at once with its pill for the beats and the tapped tempo, and stops with the pill put away;
        // held, a slide sets its tempo (#521 section 15).
        Button(action: toggleMetronome) {
            Icon(name: "metronome", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: metronome.running))
        .accessibilityLabel(text["metronome.label"])
        .accessibilityAddTraits(metronome.running ? .isSelected : [])
        .modifier(MetronomeHold(metronome: metronome, text: text, below: true, toggle: toggleMetronome))
        if !linked {
            PaletteDivider()
            if settings.isOn("experimental.playheads") {
                // The score's modes: nothing on a PDF's pages is selected or edited, so only Navigate and Pen choose.
                ModeCapsule(chosen: pdf.penOn ? .pen : .navigate, text: text, unavailable: [.select]) { mode in
                    pdf.penOn = mode == .pen
                }
            } else {
                Button {
                    pdf.penOn.toggle()
                } label: {
                    Icon(name: "pen", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
                }
                .buttonStyle(BarActionStyle(on: pdf.penOn))
                .accessibilityLabel(text["bar.pen"])
                .accessibilityHint(text["pencil.padHint"])
                .accessibilityAddTraits(pdf.penOn ? .isSelected : [])
            }
        }
        PaletteDivider()
        if settings.isOn("experimental.reference") {
            Button {
                referenceOpen = true
            } label: {
                Icon(name: "keysig", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: referenceOpen))
            .accessibilityLabel(text["reference.open"])
            .accessibilityHint(text["reference.openHint"])
        }
        Button {
            more.toggle()
        } label: {
            Icon(name: "more", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: more))
        .accessibilityLabel(text["bar.more"])
        .popover(isPresented: $more) {
            PdfMore(pdf: pdf, text: text)
        }
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

    /// «Пьесы в этом файле» from the Contents, with the experiment on, for a PDF kept in the piece.
    private var openParts: (() -> Void)? {
        guard !linked, DeviceSettings.shared.isOn("experimental.pieces") else { return nil }
        return {
            contentsOpen = false
            partsOpen = true
        }
    }

    /// Opens this piece's file again in the PDF view at the page under the bar, once «Подтвердить пьесы» has written the
    /// pieces layer into it (version 0.5); nil where the library cannot open it here.
    private var reopenParts: (() async -> Void)? {
        guard let switchView else { return nil }
        return { [pdf] in
            partsOpen = false
            pdf.keepView()
            await switchView(.page(max(1, pdf.page)))
        }
    }

    /// Recognise notes in the bar, while none have been read from this PDF on this device.
    private var readNotesAction: (() -> Void)? {
        guard openRead != nil, piece.other == nil else { return nil }
        return {
            more = false
            readingOpen = true
        }
    }

    /// Whether recognising the notes goes to Motiff Lab's server, which is Pro.
    private var hosted: Bool { scanAddress != nil && scanAddress == ScanServer.hosted(account) }

    /// The scan server the PDF would go to: the player's own, or Motiff Lab's in a build with sign-in.
    private var scanAddress: URL? { scan.address(hosted: ScanServer.hosted(account)) }

    /// Sends the PDF to the scan server and puts the notes it read into this same piece (testing item 16), which opens as
    /// notes where the page under the bar starts (the web app's `ReadNotesAsync`). Answers why not, or nil once they open.
    /// With «Фото страниц» on, the server's report on the pages goes with the notes (v0.4). A PDF longer than the server
    /// reads at once goes in parts, with the panel's bar (issue #479).
    private func readNotes() async -> String? {
        guard let address = scanAddress, let data = pdf.original else { return text["pdf.readNone"] }
        switch await ScanServer.recognise(fileName: pdf.fileName, data: data, address: address, account: account) {
        case .failure(let problem): return problem.text(text)
        case .success(let read):
            let reports = DeviceSettings.shared.isOn("experimental.photoPages") ? read.reports : nil
            return await fill(read.musicXml, engine: read.engine, pages: max(1, pdf.pageCount), reports: reports)
        }
    }

    /// Read the notes, as the panel offers it: what leaves the iPad and to where, and what to do on Send.
    private func readPanel(_ read: @escaping () async -> String?, heading: String? = nil, keeps: String? = nil, line: String? = nil) -> ReadNotesPanel {
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
            read: read
        )
    }

    /// The notes read from the pages into this same piece, which then opens as notes: answers why not, or nil. The page
    /// reports (v0.4), one per page of the notes, are kept with them, or left out when music-core refuses them.
    private func fill(_ musicXml: Data, engine: String, pages: Int, reports: [String]?) async -> String? {
        guard let openRead else { return text["pdf.readNone"] }
        let request = DocumentRead.request(musicXml: musicXml, engine: engine, pages: pages, reports: reports, language: text.language, now: Date())
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

    /// The notes read from the PDF as the player left them (#511 section 10), or the first time where the page under the bar
    /// starts in them, once the piece is saved and the PDF's place kept (the web app's `ShowNotesAsync`).
    private func showNotes() async {
        guard let other = piece.other, let switchView else { return }
        pdf.keepView()
        if let file = piece.file { try? await ProjectFiles.save(piece: piece.id, to: file) }
        await switchView(.tick(other.pages.start(of: max(1, pdf.page))))
    }
}

/// The PDF's Contents (the web app's `.pdf-contents`): the bookmarked pages, to go to, name or remove.
struct PdfContents: View {
    @ObservedObject var pdf: PdfController
    let text: UIText
    /// Opens «Пьесы в этом файле», with the experiment on.
    var parts: (() -> Void)?
    let go: (Int) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            if let parts { PiecePartsEntry(family: pdf.family, text: text, open: parts) }
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

/// The PDF's overflow: Download the PDF (the bytes as added, through the share sheet). Recognise notes is in the bar.
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
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 320, alignment: .leading)
        .background(Color.ds("surface"))
    }
}
