import SwiftUI

/// Where an original's bytes are, in one quiet line (the web's `.source-card .held`): dashed when they are not on this
/// iPad, and in the warning ink when the linked file has changed.
struct OriginalPlaceLabel: View {
    let label: String
    let badge: OriginalBadge

    var body: some View {
        let away = !badge.isHere
        Text(label)
            .dsType(.caption)
            .foregroundStyle(badge == .changed ? Color.ds("warning") : Color.ds("ink-2"))
            .padding(.horizontal, away ? CGFloat.ds("s-2") : 0)
            .overlay {
                if away {
                    RoundedRectangle(cornerRadius: CGFloat.ds("r-xs"))
                        .strokeBorder(Color.ds("hairline-strong"), style: StrokeStyle(lineWidth: 1, dash: [CGFloat.ds("s-1"), CGFloat.ds("s-1")]))
                }
            }
    }
}
