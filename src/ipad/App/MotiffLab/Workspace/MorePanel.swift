import SwiftUI

/// More, from the bar: Check for a piece with flags, Other versions, the layers of marks, what the app owes the piano and the engraving (CC BY 3.0 asks for the piano's
/// credit wherever it plays), and what went wrong, if anything did. The layout, the PDF and the other tools come with later
/// steps.
struct MorePanel: View {
    @ObservedObject var score: ScoreController
    /// Check, for More's way to it and «Мои правки».
    @ObservedObject var checkModel: CheckModel
    /// Whether the piece has other versions: the layers list offers their marks (MarksModel.pieceExtra).
    let hasVersions: Bool
    let text: UIText
    let problem: String?
    let openCheck: () -> Void
    /// Sources; nil on a view shown over the piece, which has none.
    let openVersions: (() -> Void)?
    let openSettings: () -> Void
    let openDna: () -> Void
    let askAboutPiece: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            if let problem {
                Text(problem)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("danger"))
            }
            if checkModel.check.hasEntries {
                Button(action: openCheck) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "check")
                        Text(checkModel.check.open > 0 ? text.count("check.toReview", checkModel.check.openBars.count) : text["check.title"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
                Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            }
            // The piece's sources (0030): its originals and the notes read from them.
            if let openVersions {
                Button(action: openVersions) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "mic")
                        Text(text["sources.title"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
            // Piece DNA, the map of the whole piece, and the assistant on the whole piece (testing items 22, 23).
            Button(action: openDna) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "dial")
                    Text(text["dna.title"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Button(action: askAboutPiece) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "insight")
                    Text(text["ask.aboutPiece"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            // «Мои правки» (version 0.5): the player's corrections laid over the notes or left out, once there are some.
            if checkModel.editsLayer?.shows == true {
                EditsLayerSection(checkModel: checkModel, text: text)
                Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            }
            LayersList(score: score, marksModel: score.marksModel, checkModel: checkModel, text: text, hasVersions: hasVersions)
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            Button(action: openSettings) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "dial")
                    Text(text["settings.all"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            Text(credit)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .tint(Color.ds("accent"))
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: ScoreScreen.popoverWidth, alignment: .leading)
        .presentationCompactAdaptation(.popover)
    }

    private var credit: AttributedString {
        let markdown = "\(text["credit.piano"]) [Salamander Grand Piano V3](https://archive.org/details/SalamanderGrandPianoV3) \(text["credit.by"]) Alexander Holm, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/), \(text["credit.shortened"]) \(text["credit.engraving"]) [Verovio](https://www.verovio.org) 6.3.0, [LGPL 3.0](https://www.gnu.org/licenses/lgpl-3.0.html), [\(text["credit.source"])](https://github.com/rism-digital/verovio/tree/version-6.3.0)."
        return (try? AttributedString(markdown: markdown)) ?? AttributedString(markdown)
    }
}
