import SwiftUI

/// A one-line field in the kit's look (ds.css `.field`): body type in the ink on the fields' surface, a strong hairline
/// round it, the accent's ring while it is typed in. Both colours follow the stand, so what is typed reads in Daylight and
/// at Night (#479: on the paper, which stays cream at Night, the Night ink was light on light). The app's one-line
/// fields: the properties, «Пьесы в этом файле», a performance's name and the player's name.
struct FieldLook: ViewModifier {
    var on = false

    /// The tokens of a field (ds.css `.field`): its surface and its text.
    static let background = "surface-2"
    static let ink = "ink"

    func body(content: Content) -> some View {
        content
            .dsType(.body)
            .foregroundStyle(Color.ds(Self.ink))
            .tint(Color.ds("accent"))
            .padding(.horizontal, CGFloat.ds("s-4"))
            .frame(height: CGFloat.ds("h-touch"))
            .background(Color.ds(Self.background), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(on ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: on ? 2 : 1))
    }
}
