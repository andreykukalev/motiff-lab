import SwiftUI
import WebKit

/// Where the score's page is scrolled to, as a place in the notes (the page's `placeAt`, `lineAt`): the scroll view's top
/// in the document's points (its offset over its zoom) and back; and back where the player stopped (#327), once the
/// pages are drawn.
extension ScoreController {
    /// Waits for the pages to be drawn; false when they were not within ten seconds.
    func whenRendered() async -> Bool {
        var waited = 0
        while !rendered && waited < 100 {
            try? await Task.sleep(nanoseconds: 100_000_000)
            waited += 1
        }
        return rendered
    }

    /// Back where the player stopped (#327): the passage selected and brought into view, and in focus when it was. It
    /// waits for the page to be drawn, so the selection is drawn on it with its phrase.
    func arrive(at place: ResumePlace) async {
        guard await whenRendered(), piece != nil else { return }
        guard let passage = place.passage else {
            await reveal(place.at)
            return
        }
        await select(passage.lowerBound, passage.upperBound, step: false)
        guard let selection = selectionModel.selection else { return }
        await reveal(selection.onset)
        if place.focused { enterFocus() }
    }

    /// The tick at the top of the pages in view: where the player was when nothing is selected.
    func tickInView() async -> Double? {
        await firstTick(onPage: pages.first)
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
