import Foundation

/// One page of a scan the server reported on, as `check` counts it (v0.4, within contract 12): its place in the notes from
/// 1, its quality ("good", "weak" or "poor"), how far the server turned it, its bars and how many of them are doubtful, and
/// its first and last bar (nil without bars). In Platform, as Check (Marks) and its part about a scan's pages (Import)
/// read it.
struct CheckPageEntry: Identifiable, Equatable, Sendable {
    var page: Int
    var quality: String
    var rotation: Int
    var bars: Int
    var doubtful: Int
    var firstBar: Int?
    var lastBar: Int?
    var id: Int { page }

    /// The bars to show for the page.
    var barRange: ClosedRange<Int>? {
        guard let firstBar, let lastBar, firstBar <= lastBar else { return nil }
        return firstBar...lastBar
    }
}
