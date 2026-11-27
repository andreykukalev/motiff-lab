import Foundation

/// Where one page of a scan's notes starts and ends, as `check` answers "pageStarts" (v1.5 i-7): its first and last bar,
/// each with the tick it starts at. Only what music-core answers; which PDF page is missing is the recognition's.
struct CheckPageStart: Equatable, Sendable {
    var page: Int
    var bar: Int
    var tick: Double
    var lastBar: Int
    var lastTick: Double

    /// The entries of "pageStarts", in order; one without its numbers is left out.
    static func all(_ value: Any?) -> [CheckPageStart] {
        (value as? [[String: Any]] ?? []).compactMap { entry in
            func number(_ key: String) -> Double? { (entry[key] as? NSNumber)?.doubleValue }
            guard let page = number("page"), let bar = number("bar"), let tick = number("tick"), let lastBar = number("lastBar"),
                  let lastTick = number("lastTick") else { return nil }
            return CheckPageStart(page: Int(page), bar: Int(bar), tick: tick, lastBar: Int(lastBar), lastTick: lastTick)
        }
    }
}
