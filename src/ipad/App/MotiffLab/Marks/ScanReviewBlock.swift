import SwiftUI

/// A scan's bar to check where the engines disagree, in its Check entry (decision record 0035 section 6, v1.3 i-2): the
/// bar's crop of the original page, the engines' other readings of it, each put in with one tap as the player's
/// correction of that bar only, and Undo of the one chosen. Without what this iPad kept of the scan (an older piece, read
/// on another device), a short line says so; Fix notes and Looks right stay as for any bar.
struct ScanReviewBlock: View {
    /// What this iPad kept of the bar; nil when it kept nothing of it.
    let entry: ScanReview.Bar?
    /// The bar's crop of its page, read once with the review (`ScoreController.scanCrops`).
    let crop: UIImage?
    let bar: Int
    /// The engines of the variant chosen last in this bar, while one Undo takes it back whole; nil otherwise.
    let chosen: [String]?
    /// False while «Мои правки» is off: no variant is put in then.
    let editsOn: Bool
    let text: UIText
    let choose: (ScanVariant) -> Void
    let undo: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            if let entry {
                if let crop {
                    Text(text["scanReview.page"]).overline()
                    Image(uiImage: crop)
                        .resizable()
                        .scaledToFit()
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color.ds("paper"))
                        .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline")))
                        .accessibilityLabel(text.format("scanReview.cropLabel", String(bar)))
                }
                Text(text["scanReview.variants"]).overline()
                if entry.variants.isEmpty {
                    Text(text["scanReview.none"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
                ForEach(Array(entry.variants.enumerated()), id: \.offset) { _, variant in
                    Button { choose(variant) } label: {
                        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                            Text(variant.engines.joined(separator: ", ")).dsType(.label)
                            Text(variant.names(rest: text["scanReview.rest"])).dsType(.caption).fixedSize(horizontal: false, vertical: true)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                    .disabled(!editsOn)
                    .accessibilityHint(text["scanReview.variantHint"])
                }
                if let chosen {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Text(text.format("scanReview.chosen", chosen.joined(separator: ", ")))
                            .dsType(.caption)
                            .foregroundStyle(Color.ds("ink-2"))
                        Button(text["scanReview.undo"], action: undo)
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    }
                }
            } else {
                Text(text["scanReview.missing"])
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .accessibilityElement(children: .contain)
    }
}
