import UIKit

/// ScoreController's part of the page as the screen around it lays it out: room for a drawer at the left or the bottom
/// and the selection menu's size and place, the pages' outline for the Contents, where a page's notes start, a page or a
/// tick brought into view, and the places of the fact chosen in Insights ringed.
extension ScoreController {
    /// A drawer over the left of the page this wide: the selection menu keeps to its right.
    func clearLeft(_ width: CGFloat) {
        guard shown else { return }
        Task { await scorePage.send(.clearLeft, ["width": Double(width)]) }
    }

    /// The pages with their stretches of the piece and their pictures, `width` pixels wide, for the Pages tab.
    func outline(width: CGFloat) async -> [PageOutline] {
        guard shown,
              let answer = try? await scorePage.call(.outline, ["width": Double(width)]) as? [Any]
        else { return [] }
        return answer.compactMap { entry in
            guard let page = entry as? [String: Any], let number = (page["page"] as? NSNumber)?.intValue else { return nil }
            let image = (page["image"] as? String)
                .flatMap { $0.split(separator: ",", maxSplits: 1).last }
                .flatMap { Data(base64Encoded: String($0)) }
                .flatMap { UIImage(data: $0) }
            return PageOutline(
                page: number,
                start: (page["start"] as? NSNumber)?.doubleValue ?? 0,
                end: (page["end"] as? NSNumber)?.doubleValue ?? .infinity,
                image: image
            )
        }
    }

    /// Where the notes on a page (from 1) start, for PDF | Notes (0023); nil before the score is shown.
    func firstTick(onPage page: Int) async -> Double? {
        guard shown, page > 0 else { return nil }
        let tick = try? await scorePage.call(.firstTickOn, ["page": page])
        return (tick as? NSNumber)?.doubleValue
    }

    /// Brings a page to the top of the view, under the bar.
    func showPage(_ page: Int) {
        guard shown else { return }
        Task { await scorePage.send(.showPage, ["page": page]) }
    }

    /// Scrolls the page smoothly to where a tick is, without a jump of page (also for the assistant's scope chip).
    func reveal(_ tick: Double) async {
        await scorePage.send(.reveal, ["start": tick])
    }

    /// Rings the places of the fact chosen in Insights (0018 section 6); none clears them. With `scroll` the first comes
    /// into view.
    func highlight(_ places: [Fact.Place], scroll: Bool) {
        ringed = places
        guard shown else { return }
        Task { await showRings(scroll: scroll) }
    }

    func showRings(scroll: Bool) async {
        let data: [String: Any] = ["places": ringed.map { ["onset": $0.onset, "end": $0.end, "noteIds": $0.noteIds] as [String: Any] }, "scroll": scroll]
        await scorePage.send(.highlight, data)
    }

    /// Room under the last page for the drawer over the bottom of the score, so the end of the piece can still be read.
    func insetBottom(_ height: CGFloat) {
        guard shown else { return }
        Task { await scorePage.send(.inset, ["bottom": Double(height)]) }
    }

    /// The size of the selection menu, for the page to place it over the band.
    func menuSize(_ size: CGSize) {
        guard shown, size.width > 0 else { return }
        Task {
            await scorePage.send(.menuSize, ["width": size.width, "height": size.height])
        }
    }

    /// The selection menu dragged aside by the player and let go (#582 item 7): it stays where it was let go at once, and the
    /// page keeps that place inside the window until the menu closes (Scripts/score/menu-drag.ts), reporting it back.
    func moveMenu(by translation: CGSize) {
        guard shown, let place = selectionModel.menuPlace else { return }
        selectionModel.placeMenu(CGPoint(x: place.x + translation.width, y: place.y + translation.height))
        let data: [String: Any] = ["dx": Double(translation.width), "dy": Double(translation.height)]
        Task { await scorePage.send(.menuMoved, data) }
    }

    /// Whether the selection menu is shown; put away (the selection gone, Hide, the roll's drawer, Expand), the next one
    /// opens over its band.
    func menuShown(_ open: Bool) {
        guard shown, !open else { return }
        Task { await scorePage.send(.menuClosed) }
    }
}
