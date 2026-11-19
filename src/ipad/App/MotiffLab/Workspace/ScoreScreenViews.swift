import PDFKit
import SwiftUI
import WebKit

/// Where the score's page is scrolled to, as a place in the notes (Web/score.js `placeAt`, `lineAt`): the scroll view's top
/// in the document's points (its offset over its zoom) and back.
extension ScoreController {
    /// Waits for the pages to be drawn, as `arrive(at:)` does; false when they were not within ten seconds.
    func whenRendered() async -> Bool {
        var waited = 0
        while !rendered && waited < 100 {
            try? await Task.sleep(nanoseconds: 100_000_000)
            waited += 1
        }
        return rendered
    }

    /// The place at the top of the view, the page's zoom and how far across it is; nil before the pages are drawn.
    func viewPlace() async -> (place: ViewAnchor?, zoom: Double, left: Double)? {
        guard rendered else { return nil }
        let scroll = webView.scrollView
        let zoom = max(Double(scroll.zoomScale), 0.01)
        let y = Double(scroll.contentOffset.y) / zoom
        let answer = try? await scorePage.call(.placeAt, ["y": y])
        return (ViewAnchor(page: answer), zoom, Double(scroll.contentOffset.x) / max(Double(scroll.contentSize.width), 1))
    }

    /// Back at the zoom and the place kept, within what the scroll view reaches.
    func showPlace(_ place: ViewAnchor?, zoom: Double, left: Double) async {
        guard rendered else { return }
        let scroll = webView.scrollView
        let scale = min(max(CGFloat(zoom), scroll.minimumZoomScale), scroll.maximumZoomScale)
        if abs(scroll.zoomScale - scale) > 0.001 { scroll.setZoomScale(scale, animated: false) }
        guard let place,
              let line = try? await scorePage.call(.lineAt, place.page) as? NSNumber
        else { return }
        let lowest = -scroll.adjustedContentInset.top
        let highest = max(lowest, scroll.contentSize.height + scroll.adjustedContentInset.bottom - scroll.bounds.height)
        let y = min(max(CGFloat(line.doubleValue) * scroll.zoomScale, lowest), highest)
        let x = min(max(CGFloat(left) * scroll.contentSize.width, 0), max(0, scroll.contentSize.width - scroll.bounds.width))
        scroll.setContentOffset(CGPoint(x: x, y: y), animated: false)
    }
}

extension PdfController {
    /// Keeps where the pages are on the device (#511 section 10), with the page under the bar; nothing before they are laid out.
    func keepView() {
        guard let projectId, let pages = view as? BarPdfView, let state = pages.viewState() else { return }
        ViewStates.keep(state, projectId)
    }
}
