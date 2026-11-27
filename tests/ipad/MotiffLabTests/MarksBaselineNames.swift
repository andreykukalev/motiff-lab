import Foundation
@testable import MotiffLab

/// The marks as the protected baseline drives them (`tests/ipad/Baseline`: `PhrasesBaselineTests` hides and shows the
/// phrases' marks), by the name they had on `ScoreController` before MarksModel sent what the page hides (ADR 0031
/// section 3, v1.6 i-10b). It only calls `marksModel`, so no assertion of the baseline changes and the app keeps no member
/// for the tests. It goes when the baseline calls `score.marksModel.showPhraseMarks`, a change of `tests/ipad/Baseline`
/// Andrei merges.
@MainActor
extension ScoreController {
    func showPhraseMarks(_ on: Bool) {
        marksModel.showPhraseMarks(on)
    }
}
