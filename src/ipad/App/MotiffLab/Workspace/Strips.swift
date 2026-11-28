import SwiftUI

/// The column of «Пюпитр» on every view of a piece (spec score-reading §3.1, v1.7 e-1, e-2): the top strip round the view's
/// bar, the room between the strips, the bottom shelf, laid out once for the notes, the PDF and the recording, so
/// switching views moves nothing in either strip (VIEW-04). The strips lie over whatever the room holds, so nothing from
/// it ever lies on them; the read-only line hangs under the top strip (`ReadOnlyLine`). The keyboard never changes the
/// screen's size (#582 item 16): before, a chat's keyboard shrank it, portrait read as landscape and the sheets, the
/// drawer, the strips and the page's room moved, and back again as it went; a chat keeps itself clear of the keyboard
/// (AskFlow, KeyboardRoom).
struct Strips<Top: View, Room: View, Shelf: View>: View {
    /// The top strip's content, handed the bar's width: the screen's less the strip's margins (TopBar).
    @ViewBuilder let top: (_ barWidth: CGFloat) -> Top
    /// What the room between the strips holds, handed its size and whether the screen is landscape.
    @ViewBuilder let room: (_ size: CGSize, _ landscape: Bool) -> Room
    /// The bottom shelf's content.
    @ViewBuilder let shelf: () -> Shelf

    var body: some View {
        GeometryReader { screen in
            VStack(spacing: 0) {
                top(screen.size.width - 2 * DockedStrip.margin)
                    .modifier(DockedStrip(edge: .top))
                    .overlay(alignment: .bottomLeading) { ReadOnlyLine().padding(.leading, DockedStrip.margin) }
                    .zIndex(1)
                GeometryReader { space in
                    room(space.size, screen.size.width > screen.size.height)
                }
                // Above a hidden keyboard's bar (iPadOS's «RU» and Undo), never above the keyboard itself (v0.6.4,
                // E-score-5), which the screen ignores.
                shelf()
                    .modifier(DockedStrip(edge: .bottom))
                    .keyboardLift(barOnly: true)
                    .zIndex(1)
            }
        }
        .ignoresSafeArea(.keyboard)
    }
}

extension View {
    /// Where what the player opened under the top strip hangs over the page (E-score-3): the strip's margin at the sides,
    /// a gap below it; the same on every view.
    func hungUnderTopStrip() -> some View {
        padding(.horizontal, DockedStrip.margin).padding(.top, CGFloat.ds("s-2"))
    }
}

/// The piece's one line that it opened read-only (project file 18, `PieceScreen`), hung under the top strip of every view
/// of the piece without changing its height or taking a touch; nothing for a piece that saves.
private struct ReadOnlyLine: View {
    @Environment(\.readOnlyLine) private var readOnlyLine

    var body: some View {
        if let readOnlyLine {
            Text(readOnlyLine).dsType(.caption).foregroundStyle(Color.ds("warning"))
                .fixedSize(horizontal: false, vertical: true)
                .padding(.horizontal, CGFloat.ds("s-3"))
                .padding(.vertical, CGFloat.ds("s-1"))
                .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                .alignmentGuide(.bottom) { $0[.top] - CGFloat.ds("s-2") }
                .allowsHitTesting(false)
        }
    }
}

private struct ReadOnlyLineKey: EnvironmentKey {
    static let defaultValue: String? = nil
}

extension EnvironmentValues {
    /// The line that the piece shown opened read-only, for a project a newer version saved (`PieceSummary.readOnly`); nil
    /// for a piece that saves. Written once, by `PieceScreen`, for every view of the piece.
    var readOnlyLine: String? {
        get { self[ReadOnlyLineKey.self] }
        set { self[ReadOnlyLineKey.self] = newValue }
    }
}
