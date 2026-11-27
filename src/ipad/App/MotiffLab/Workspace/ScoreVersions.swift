import Foundation

/// ScoreController's part of the versions on the score (`SourcesScore`, v1.6 i-10; SourcesModel draws and acts on them):
/// whether extra information of a kind shows, what follows the versions drawn (the version in focus, the page engraved
/// again when even bars were turned on or off), Check read again after a version is added, a version's bar heard and the
/// way from a marker to Sources.
extension ScoreController {
    func extraShown(_ kind: String) -> Bool {
        marksModel.extraShown(kind)
    }

    func versionsDrawn() async {
        await updateFocusVersion()
        await engraveIfEvenBarsChanged()
    }

    func loadCheck(in piece: Int32) async {
        await checkModel.load(in: piece)
    }

    func hear(version id: String, over span: Range<Double>) {
        guard let piece else { return }
        Task {
            guard let notes = await focusModel.versionNotes(of: id, in: piece.id) else { return }
            hearNotes?(notes, span)
        }
    }

    func openSources(atBar bar: Int) {
        versionPicked?(bar)
    }

    /// The page engraved again when even bars were turned on or off.
    private func engraveIfEvenBarsChanged() async {
        guard sourcesModel.engraved.even != DeviceSettings.shared.isOn("score.evenBars") else { return }
        await engraveScore()
    }
}
