@testable import MotiffLab

/// The score as Check sees it (`CheckScore`), for `CheckModelTests` and `ReadingMarksTests`: what was asked of it, in
/// order, the passages and bars it was asked to select, and what it was asked to say.
@MainActor
final class FakeCheckScore: CheckScore {
    var pieceId: Int32?
    private(set) var asked: [String] = []
    private(set) var shown: [Range<Double>] = []
    private(set) var selected: [ClosedRange<Int>] = []
    private(set) var notices: [String] = []
    private(set) var problems: [String?] = []

    func showSpan(_ span: Range<Double>) {
        shown.append(span)
    }

    func selectBars(_ first: Int, _ last: Int, extend: Bool, gesture: String?) {
        selected.append(first...last)
    }

    func drawChecks() async {
        asked.append("drawChecks")
    }

    func applyLayers() async {
        asked.append("applyLayers")
    }

    func notesDidChange() async {
        asked.append("notesDidChange")
    }

    func showNotice(_ key: String) {
        notices.append(key)
    }

    func showProblem(_ message: String?) {
        problems.append(message)
    }

    func count(_ what: String) -> Int { asked.filter { $0 == what }.count }
}
