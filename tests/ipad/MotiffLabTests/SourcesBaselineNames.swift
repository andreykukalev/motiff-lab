import Foundation
@testable import MotiffLab

/// The versions as the protected baseline reads them (`tests/ipad/Baseline`: `WorkspaceScenarioTests` adds a version), by
/// the name they had on `ScoreController` before SourcesModel drew and acted on them (ADR 0031 section 3, v1.6 i-10). It
/// only calls `sourcesModel`, so no assertion of the baseline changes and the app keeps no member for the tests. It goes
/// when the baseline calls `score.sourcesModel.addVersion`, a change of `tests/ipad/Baseline` Andrei merges.
@MainActor
extension ScoreController {
    func addVersion(fileName: String, data: Data) async {
        await sourcesModel.addVersion(fileName: fileName, data: data)
    }
}
