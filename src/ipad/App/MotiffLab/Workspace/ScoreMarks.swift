import Foundation

/// ScoreController's side of the marks (`MarksScore`, v1.6 i-10b; MarksModel changes and draws them, the bookmarks, the
/// margin notes, the layers and what the page hides): what the marks may not read themselves (Check's drawer, the
/// device's appearance), the versions drawn again, and the pen's and Check's calls into the marks (`PenScore`,
/// `CheckScore`), which name no Marks model.
extension ScoreController {
    var checkDrawerOpen: Bool { checkModel.drawerOpen }

    var slate: Bool { DeviceSettings.shared["appearance.theme"] == "slate" }

    func drawVersions() async {
        await sourcesModel.drawVersions()
    }

    func applyLayers() async {
        await marksModel.applyLayers()
    }

    func changeMarks(_ piece: Int32, _ call: PieceCore.Call, _ request: [String: Any], draws: Bool) async {
        await marksModel.changeMarks(piece, call, request, draws: draws)
    }
}
