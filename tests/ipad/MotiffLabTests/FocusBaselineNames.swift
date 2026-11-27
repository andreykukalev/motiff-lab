import Foundation
@testable import MotiffLab

/// Expand as the protected baseline drives it (`tests/ipad/Baseline`: `WorkspaceScenarioTests` expands from the roll's
/// drawer and goes back), by the names it had on `ScoreController` before FocusModel opened and left it on the page (ADR
/// 0031 section 3, v1.6 i-10b). Each only calls `focusModel`, so no assertion of the baseline changes and the app keeps no
/// member for the tests. They go when the baseline calls `score.focusModel.expandRoll()` and `leave()`, a change of
/// `tests/ipad/Baseline` Andrei merges. `enterFocus` is the root's own (`MarksScore`), so it needs none.
@MainActor
extension ScoreController {
    func expandRoll() {
        focusModel.expandRoll()
    }

    func leaveFocus() {
        focusModel.leave()
    }
}
