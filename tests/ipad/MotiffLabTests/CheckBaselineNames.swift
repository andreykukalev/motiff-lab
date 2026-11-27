import UIKit
@testable import MotiffLab

/// Check as the protected baseline reads it (`tests/ipad/Baseline`: `ScanCheckBaselineTests`, the reactions of
/// `WorkspaceSession`), by the names it had on `ScoreController` before Check became its own model (ADR 0031 section 3, S6,
/// v1.6 i-8). Each only reads or calls `checkModel`, so no assertion of the baseline changes and the app keeps no member
/// for the tests. They go when the baseline reads `score.checkModel`, a change of `tests/ipad/Baseline` Andrei merges.
@MainActor
extension ScoreController {
    var check: CheckShelf { checkModel.check }
    var scanReview: ScanReview? { checkModel.scanReview }
    var scanCrops: [Int: UIImage] { checkModel.scanCrops }
    var variantChoice: ScanVariantChoice { checkModel.variantChoice }
    var corrections: NoteCorrections? { checkModel.corrections }
    var editsOn: Bool { checkModel.editsOn }
    var noteEditor: NoteEditorState? { checkModel.noteEditor }

    func chooseVariant(_ variant: ScanVariant, bar: Int) {
        checkModel.chooseVariant(variant, bar: bar)
    }

    func undoVariant() {
        checkModel.undoVariant()
    }
}
