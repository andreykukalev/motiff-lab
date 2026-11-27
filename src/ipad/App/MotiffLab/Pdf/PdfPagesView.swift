import PDFKit
import SwiftUI
import UIKit

/// Where a PDF's pages rest under the floating bar (#495 UX-12), as the notes' pages do (Web/score.css `--pad-top`,
/// `showPage` of the score page, ipad-page/page-navigation.ts): the first page at the top of the document, and a page
/// brought to the top, start a gap of `s-4` below the bar's bottom edge. The rules are plain numbers, so the tests check
/// them without a PDF view.
enum PageRest {
    /// The gap between the bar and a page at the top of the view, as on the notes.
    static var gap: CGFloat { CGFloat.ds("s-4") }

    /// Where a page's top rests, from the top of the view.
    static func pageTop(barBottom: CGFloat, gap: CGFloat = PageRest.gap) -> CGFloat {
        barBottom + gap
    }

    /// The room the scroll view keeps above its content, so that scrolled to the top the first page, whose top is at
    /// `firstPage` in the content (below the page break PDFKit draws above it), rests at `pageTop`.
    static func room(pageTop: CGFloat, firstPage: CGFloat) -> CGFloat {
        max(0, pageTop - firstPage)
    }

    /// The scroll offset that brings a page whose top is at `page` in the content to `pageTop`, within what the scroll
    /// view can reach: from `lowest` (its top, with the room) to `highest` (its end).
    static func offset(page: CGFloat, pageTop: CGFloat, lowest: CGFloat, highest: CGFloat) -> CGFloat {
        min(max(page - pageTop, lowest), max(lowest, highest))
    }
}

/// PDFKit's view of the pages, under the bar that floats over them (#495 UX-12). PDFKit starts at the top of its content
/// and scrolls a page it goes to up to the top of its view, both under the bar, which hid the top of the page after PDF |
/// Notes: so this view keeps room above the first page for the bar, and brings a page to rest just below it itself, at
/// the place the notes bring theirs to.
final class BarPdfView: PDFView {
    /// The bar's bottom edge, from the top of the view, as the screen lays it out.
    var barBottom: CGFloat = 0 {
        didSet {
            if barBottom != oldValue { setNeedsLayout() }
        }
    }

    /// The page to bring under the bar once the pages are laid out, and how far down it the bar is to rest (0 at its top).
    private var waiting: (page: PDFPage, fraction: CGFloat)?
    /// The page last brought under the bar, how far down it, and the view's size then.
    private var placed: (page: PDFPage, fraction: CGFloat, size: CGSize)?
    /// The zoom and the place across the pages to come back to with the page (PDF | Notes, #511 section 10); nil for none.
    private var returning: (zoom: Double?, left: Double)?

    private var scroll: UIScrollView? { subviews.compactMap { $0 as? UIScrollView }.first }

    private var pageTop: CGFloat { PageRest.pageTop(barBottom: barBottom) }

    /// Brings the page's top to rest under the bar, once the pages are laid out: on opening, after PDF | Notes, after a
    /// turn of the iPad and from the Contents.
    func rest(on page: PDFPage) {
        holdUntilPlaced()
        waiting = (page, 0)
        setNeedsLayout()
    }

    /// Comes back to where the player left the pages (PDF | Notes, #511 section 10): the zoom they chose, then the page and the
    /// place on it under the bar, once the pages are laid out.
    func comeBack(to state: PdfViewState) {
        guard let page = document?.page(at: state.page - 1) else { return }
        holdUntilPlaced()
        returning = (state.zoom, state.left)
        waiting = (page, CGFloat(state.place.fraction))
        setNeedsLayout()
    }

    /// Where the pages are now: the page under the bar and how far down it, how far across, and the zoom as a share of the
    /// zoom that fits them (nil while they fit); nil before they are laid out.
    func viewState() -> PdfViewState? {
        guard let scroll, let document, scroll.contentSize.height > 0 else { return nil }
        let spans = (0..<document.pageCount).compactMap { index -> AnchorSpan? in
            guard let page = document.page(at: index) else { return nil }
            let box = pageFrame(page, in: scroll)
            return AnchorSpan(key: Double(index + 1), top: Double(box.minY), bottom: Double(box.maxY))
        }
        guard let place = ViewAnchor.at(Double(scroll.contentOffset.y + pageTop), in: spans) else { return nil }
        let fit = scaleFactorForSizeToFit
        let zoom: Double? = autoScales || fit <= 0 ? nil : Double(scaleFactor / fit)
        return PdfViewState(place: place, left: Double(scroll.contentOffset.x / max(scroll.contentSize.width, 1)), zoom: zoom)
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        guard let scroll, bounds.height > 0, scroll.contentSize.height > 0 else { return }
        keepRoom(scroll)
        // A new size (PDFKit fitting the pages as the screen settles, a turn of the iPad) keeps the page brought under the
        // bar there while it is still the page in view.
        if waiting == nil, let placed, placed.size != bounds.size, currentPage == placed.page {
            waiting = (placed.page, placed.fraction)
        }
        guard let next = waiting else { return }
        waiting = nil
        let (page, fraction) = (next.page, next.fraction)
        // The zoom the player left first, so the page is placed at that zoom on the next pass.
        if let zoom = returning?.zoom {
            returning?.zoom = nil
            let fit = scaleFactorForSizeToFit
            if fit > 0 {
                autoScales = false
                scaleFactor = min(max(fit * CGFloat(zoom), minScaleFactor), maxScaleFactor)
            }
            waiting = (page, fraction)
            setNeedsLayout()
            return
        }
        // After this pass: PDFKit scrolls to its current page as it lays out.
        DispatchQueue.main.async { [weak self] in self?.place(page, fraction: fraction) }
    }

    /// A page's frame in the scroll view's content.
    private func pageFrame(_ page: PDFPage, in scroll: UIScrollView) -> CGRect {
        scroll.convert(convert(page.bounds(for: displayBox), from: page), from: self)
    }

    /// A page's top in the scroll view's content.
    private func top(of page: PDFPage, in scroll: UIScrollView) -> CGFloat {
        pageFrame(page, in: scroll).minY
    }

    /// The room above the first page, as the zoom and the page break above it are now; a view resting at the top stays there.
    private func keepRoom(_ scroll: UIScrollView) {
        guard let first = document?.page(at: 0) else { return }
        let room = PageRest.room(pageTop: pageTop, firstPage: top(of: first, in: scroll))
        guard abs(scroll.contentInset.top - room) > 0.5 else { return }
        let resting = scroll.contentOffset.y <= -scroll.adjustedContentInset.top + 0.5
        scroll.contentInset.top = room
        if resting {
            scroll.contentOffset.y = -scroll.adjustedContentInset.top
        }
    }

    /// The first place is reached a pass after PDFKit lays the pages out, at their first page: until then they stay out of
    /// sight (#533 6), so PDF | Notes never shows page 1 before the page it opens at.
    private func holdUntilPlaced() {
        if placed == nil { alpha = 0 }
    }

    private func place(_ page: PDFPage, fraction: CGFloat) {
        defer { alpha = 1 }
        guard let scroll else {
            go(to: page)
            return
        }
        let lowest = -scroll.adjustedContentInset.top
        let highest = scroll.contentSize.height + scroll.adjustedContentInset.bottom - scroll.bounds.height
        let box = pageFrame(page, in: scroll)
        let y = PageRest.offset(page: box.minY + fraction * box.height, pageTop: pageTop, lowest: lowest, highest: highest)
        var x = scroll.contentOffset.x
        if let left = returning?.left {
            returning = nil
            x = min(max(CGFloat(left) * scroll.contentSize.width, 0), max(0, scroll.contentSize.width - scroll.bounds.width))
        }
        scroll.setContentOffset(CGPoint(x: x, y: y), animated: false)
        placed = (page, fraction, bounds.size)
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
