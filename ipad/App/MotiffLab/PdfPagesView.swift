import PDFKit
import UIKit

/// Where a PDF's pages rest under the floating bar (#495 UX-12), as the notes' pages do (Web/score.css `--pad-top`,
/// Web/score.js `showPage`): the first page at the top of the document, and a page brought to the top, start a gap of
/// `s-4` below the bar's bottom edge. The rules are plain numbers, so the tests check them without a PDF view.
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

    /// The page to bring under the bar once the pages are laid out.
    private var waiting: PDFPage?
    /// The page last brought under the bar, and the view's size then.
    private var placed: (page: PDFPage, size: CGSize)?

    private var scroll: UIScrollView? { subviews.compactMap { $0 as? UIScrollView }.first }

    private var pageTop: CGFloat { PageRest.pageTop(barBottom: barBottom) }

    /// Brings the page's top to rest under the bar, once the pages are laid out: on opening, after PDF | Notes, after a
    /// turn of the iPad and from the Contents.
    func rest(on page: PDFPage) {
        waiting = page
        setNeedsLayout()
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        guard let scroll, bounds.height > 0, scroll.contentSize.height > 0 else { return }
        keepRoom(scroll)
        // A new size (PDFKit fitting the pages as the screen settles, a turn of the iPad) keeps the page brought under the
        // bar there while it is still the page in view.
        if waiting == nil, let placed, placed.size != bounds.size, currentPage == placed.page {
            waiting = placed.page
        }
        guard let page = waiting else { return }
        waiting = nil
        // After this pass: PDFKit scrolls to its current page as it lays out.
        DispatchQueue.main.async { [weak self] in self?.place(page) }
    }

    /// A page's top in the scroll view's content.
    private func top(of page: PDFPage, in scroll: UIScrollView) -> CGFloat {
        scroll.convert(convert(page.bounds(for: displayBox), from: page), from: self).minY
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

    private func place(_ page: PDFPage) {
        guard let scroll else {
            go(to: page)
            return
        }
        let lowest = -scroll.adjustedContentInset.top
        let highest = scroll.contentSize.height + scroll.adjustedContentInset.bottom - scroll.bounds.height
        let y = PageRest.offset(page: top(of: page, in: scroll), pageTop: pageTop, lowest: lowest, highest: highest)
        scroll.setContentOffset(CGPoint(x: scroll.contentOffset.x, y: y), animated: false)
        placed = (page, bounds.size)
    }
}
