import SwiftUI
import UIKit

/// A line's actions under it, as in Messages (version 0.5, #479): a swipe to the left uncovers «Изменить», which opens the
/// piece's properties, and «Удалить», which asks once more, at the line's right end; a swipe back, a tap on the line or on
/// an action covers them again. One line is open at a time (`open`). The long press keeps its menu. On iPadOS 17 the line
/// has no swipe: SwiftUI's own drag inside a scroll view would hold up the scrolling there, so its long press stays the way.
struct LineSwipe: ViewModifier {
    let text: UIText
    @Binding var open: Bool
    let edit: () -> Void
    let delete: () -> Void
    /// Where the finger has taken the line since the swipe began, nil while it is not swiped.
    @State private var moved: CGFloat?

    /// The width of one action: two touch targets, room for «Изменить».
    static var actionWidth: CGFloat { CGFloat.ds("h-touch") * 2 }
    /// Both actions.
    static var revealed: CGFloat { actionWidth * 2 }

    /// The tokens of the actions (ds.css has none; the kit's danger and second ink, the text on the field surface, which
    /// reads on both in either stand).
    static let editFill = "ink-2"
    static let deleteFill = "danger"
    static let label = "surface-2"

    /// Where the line rests: open, the actions uncovered, or closed.
    private var rest: CGFloat { open ? -Self.revealed : 0 }

    /// Where the line is drawn: where it rests, moved by the finger, never to the right and only a little past the actions.
    private var offset: CGFloat {
        Self.offset(rest: rest, moved: moved ?? 0)
    }

    func body(content: Content) -> some View {
        if #available(iOS 18.0, *) {
            swiped(content)
                .gesture(HorizontalPan { moved = $0 } ended: { distance, speed in settle(distance, speed) })
        } else {
            content
        }
    }

    private func swiped(_ content: Content) -> some View {
        content
            // The page under the line while it moves, so the actions do not show through its text.
            .background(offset < 0 ? Color.ds("desk") : Color.clear)
            .offset(x: offset)
            .background(alignment: .trailing) {
                if offset < 0 { actions }
            }
            // While open, a tap on the line covers the actions instead of opening the piece.
            .overlay(alignment: .leading) {
                if open {
                    Color.clear
                        .contentShape(Rectangle())
                        .padding(.trailing, Self.revealed)
                        .onTapGesture { close() }
                        .accessibilityHidden(true)
                }
            }
            .clipped()
            .animation(.ds("t-base"), value: open)
            .accessibilityAction(named: text["home.delete"], delete)
    }

    private var actions: some View {
        HStack(spacing: 0) {
            action(text["library.edit"], fill: Self.editFill) {
                close()
                edit()
            }
            action(text["home.delete"], fill: Self.deleteFill) {
                close()
                delete()
            }
        }
        .frame(maxHeight: .infinity)
    }

    private func action(_ name: String, fill: String, _ act: @escaping () -> Void) -> some View {
        Button(action: act) {
            Text(name)
                .dsType(.label)
                .foregroundStyle(Color.ds(Self.label))
                .lineLimit(1)
                .minimumScaleFactor(0.8)
                .padding(.horizontal, CGFloat.ds("s-2"))
                .frame(width: Self.actionWidth)
                .frame(maxHeight: .infinity)
                .background(Color.ds(fill))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private func close() {
        moved = nil
        open = false
    }

    /// The finger lifted: the line opens when it was taken past half the actions or flicked to the left, else it closes.
    private func settle(_ distance: CGFloat, _ speed: CGFloat) {
        let opens = Self.opens(rest: rest, moved: distance, speed: speed)
        withAnimation(.ds("t-base")) {
            moved = nil
            open = opens
        }
    }

    /// Where the line is drawn for where it rests and how far the finger took it: never right of its place, at most a
    /// space past the actions.
    static func offset(rest: CGFloat, moved: CGFloat) -> CGFloat {
        min(0, max(-revealed - CGFloat.ds("s-6"), rest + moved))
    }

    /// Whether the line stays open when the finger lifts: past half the actions, or flicked by more than a touch target's
    /// height a tenth of a second to the left; a flick to the right closes it.
    static func opens(rest: CGFloat, moved: CGFloat, speed: CGFloat) -> Bool {
        let flick = CGFloat.ds("h-touch") * 10
        if speed < -flick { return true }
        if speed > flick { return false }
        return offset(rest: rest, moved: moved) < -revealed / 2
    }
}

/// A pan that begins only when the finger moves more across than up or down, so the library still scrolls under a
/// vertical drag, and that cancels the line's tap once it begins (iPadOS 18, UIKit's pan in SwiftUI).
@available(iOS 18.0, *)
private struct HorizontalPan: UIGestureRecognizerRepresentable {
    let changed: (CGFloat) -> Void
    let ended: (CGFloat, CGFloat) -> Void

    func makeCoordinator(converter: CoordinateSpaceConverter) -> Coordinator {
        Coordinator()
    }

    func makeUIGestureRecognizer(context: Context) -> UIPanGestureRecognizer {
        let pan = UIPanGestureRecognizer()
        pan.delegate = context.coordinator
        return pan
    }

    func handleUIGestureRecognizerAction(_ recognizer: UIPanGestureRecognizer, context: Context) {
        let distance = recognizer.translation(in: recognizer.view).x
        switch recognizer.state {
        case .began, .changed:
            changed(distance)
        case .ended, .cancelled, .failed:
            ended(distance, recognizer.velocity(in: recognizer.view).x)
        default:
            break
        }
    }

    final class Coordinator: NSObject, UIGestureRecognizerDelegate {
        func gestureRecognizerShouldBegin(_ recognizer: UIGestureRecognizer) -> Bool {
            guard let pan = recognizer as? UIPanGestureRecognizer else { return false }
            let velocity = pan.velocity(in: pan.view)
            return abs(velocity.x) > abs(velocity.y)
        }

        /// The scroll view keeps its own pan beside this one.
        func gestureRecognizer(_ recognizer: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer) -> Bool {
            true
        }
    }
}
