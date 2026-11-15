import SwiftUI

/// The bar over the score and over a PDF alike (#479 item 13; 0.6.1 #582 item 2): the same start in both views (Back, the
/// Contents, the title and the switch of views with Sources), then the pages in view, then each view's own actions at the
/// right. The start never gives way: the title shows whole up to a share of the bar (BarFit), on the same width in both
/// views, so switching views moves nothing there. What gives way where the bar is short is the rest, in this order: the
/// pages in view go (whole, never cut to an ellipsis), then the actions take their compact form, then they scroll
/// sideways. Before 0.6.1 the score's longer row of actions squeezed the title to nothing and cut the pages to a lone «…».
struct TopBar<Leading: View, Actions: View, Compact: View>: View {
    /// The bar's width: the screen's, less the margins the bar floats in.
    let width: CGFloat
    /// "p. 2 of 4", or nil before the pages are laid out.
    let pages: String?
    @ViewBuilder let leading: () -> Leading
    @ViewBuilder let actions: () -> Actions
    @ViewBuilder let compact: () -> Compact

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) { leading() }
                .environment(\.barTitleCap, BarFit.titleCap(barWidth: width))
                .fixedSize()
            ViewThatFits(in: .horizontal) {
                rest(pages: pages) { actions() }
                rest(pages: nil) { actions() }
                rest(pages: nil) { compact() }
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: CGFloat.ds("s-2")) { compact() }
                }
                .defaultScrollAnchor(.trailing)
            }
            .frame(maxWidth: .infinity, alignment: .trailing)
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
    }

    private func rest(pages: String?, @ViewBuilder _ actions: () -> some View) -> some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            if let pages { BarPages(text: pages) }
            Spacer(minLength: CGFloat.ds("s-2"))
            actions()
        }
    }
}

/// The room the bar gives its start (#582 item 2), from the bar's width alone, so the score and the PDF lay it out alike.
enum BarFit {
    /// The share of the bar the piece's title may take at most; a longer title ends in an ellipsis there.
    static let titleShare: CGFloat = 0.25

    /// The widest the title shows on a bar this wide; unbounded before the bar has a width.
    static func titleCap(barWidth: CGFloat) -> CGFloat {
        barWidth > 0 ? (barWidth * titleShare).rounded(.down) : .infinity
    }
}

private struct BarTitleCapKey: EnvironmentKey {
    static let defaultValue: CGFloat = .infinity
}

extension EnvironmentValues {
    /// The widest the piece's title shows in the bar (BarFit).
    var barTitleCap: CGFloat {
        get { self[BarTitleCapKey.self] }
        set { self[BarTitleCapKey.self] = newValue }
    }
}

/// Lays its one view out at most `cap` wide, at its own width when narrower: a title asked for its ideal width ends in an
/// ellipsis at the cap instead of running on (`frame(maxWidth:)` would widen a short title to the cap).
struct WidthCap: Layout {
    let cap: CGFloat

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        guard let view = subviews.first else { return .zero }
        return view.sizeThatFits(ProposedViewSize(width: min(proposal.width ?? .infinity, cap), height: proposal.height))
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        subviews.first?.place(at: bounds.origin, proposal: ProposedViewSize(width: bounds.width, height: bounds.height))
    }
}

/// The start of the bar on the score and on a PDF alike (#479 item 13): Back, the Contents at the left by the way back, as
/// in a reader (testing item 21), and the title, which opens the Contents too and always shows (#582 item 2).
struct BarLeading: View {
    let title: String
    @Binding var contentsOpen: Bool
    let text: UIText
    let back: () -> Void
    @Environment(\.barTitleCap) private var titleCap

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button(action: back) {
                Icon(name: "back", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["nav.library"])
            Button {
                contentsOpen.toggle()
            } label: {
                Icon(name: "pages", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: contentsOpen))
            .accessibilityLabel(text["bar.contents"])
            .accessibilityAddTraits(contentsOpen ? .isSelected : [])
            Button {
                contentsOpen.toggle()
            } label: {
                WidthCap(cap: titleCap) {
                    Text(title)
                        .font(DesignTokens.font(display: true, weight: 500, size: 17, relativeTo: .headline))
                        .foregroundStyle(contentsOpen ? Color.ds("accent") : Color.ds("ink"))
                        .lineLimit(1)
                }
                .padding(.horizontal, CGFloat.ds("s-2"))
                .frame(height: 40)
            }
            .buttonStyle(BarActionStyle(on: contentsOpen))
            .accessibilityLabel(text.format("contents.open", title))
            .accessibilityAddTraits(contentsOpen ? [.isHeader, .isSelected] : .isHeader)
        }
    }
}

/// The pages in view, after the switch in the bar: shown whole or not at all, the first to give way where the bar is short
/// of room (#479 item 13, #582 item 2).
struct BarPages: View {
    let text: String

    var body: some View {
        Text(text)
            .dsType(.label)
            .foregroundStyle(Color.ds("ink-2"))
            .lineLimit(1)
            .fixedSize()
            .padding(.leading, CGFloat.ds("s-2"))
    }
}
