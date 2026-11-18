import SwiftUI
import UIKit

/// Who has the keyboard, and what a press outside the field does (v0.6.4, Andrei 2026-11-15): wherever the iPad's keyboard
/// comes up in the app (the assistant's panel, the chat at the selection, a search, a name, any text field or text view),
/// a tap outside the field puts it away, as iPadOS apps do, and a press on the field brings it back. Before, nothing heard
/// such a press: iPadOS never hides the keyboard by itself, the popover that did went with version 0.6.3, and the score,
/// which ignores the keyboard, took the press as its own. The rule is pure; `Typing` applies it to the window.
struct TypingFocus: Equatable {
    /// Where a press lands while a field has the keyboard.
    enum Place: Equatable {
        /// The field being typed in, or its own text's handles and loupe.
        case editor
        /// Another text field or text view: it takes the keyboard itself.
        case otherField
        /// The field's own row (Send, the microphone): typing goes on.
        case keeper
        /// A menu the system shows over the field (Cut, Copy, Paste).
        case systemMenu
        /// The score or the roll: the press only puts the keyboard away, the music does not hear it (TypingShield).
        case music
        /// Anywhere else: the chat's ground, a panel, a button, a list. The press goes on to what it landed on.
        case elsewhere
    }

    /// The field or text view that has the keyboard's focus, by identity; nil when nothing is typed in.
    private(set) var editor: ObjectIdentifier?

    var editing: Bool { editor != nil }

    /// While something is typed in, the score and the roll take no press: their first press only puts the keyboard away,
    /// so it never moves the playhead or changes the selection.
    var shieldsMusic: Bool { editing }

    /// A field took the focus.
    mutating func began(_ field: ObjectIdentifier) {
        editor = field
    }

    /// A field let it go; one that is not the focused one (the old field, when another took the focus first) changes nothing.
    mutating func ended(_ field: ObjectIdentifier) {
        if editor == field { editor = nil }
    }

    /// Whether a press there puts the keyboard away: only while something is typed in, and never on the field, another
    /// field, the field's own row or the system's menu over it.
    func hides(_ place: Place) -> Bool {
        guard editing else { return false }
        switch place {
        case .editor, .otherField, .keeper, .systemMenu: return false
        case .music, .elsewhere: return true
        }
    }
}

/// The app's one place for the keyboard's focus (TypingFocus): it follows every text field and text view as it takes and
/// lets go of the focus, and, while one has it, watches the window's taps beside every other gesture (it never takes a
/// touch from anyone) and puts the keyboard away on a tap outside the field. A drag (a scroll of the chat or a list) leaves
/// the keyboard up; on the score and the roll any press puts it away and goes no further (TypingShield).
@MainActor
final class Typing: NSObject, ObservableObject, UIGestureRecognizerDelegate {
    static let shared = Typing()

    @Published private(set) var focus = TypingFocus()
    private weak var editorView: UIView?
    private weak var pressed: UIView?
    private let tap = UITapGestureRecognizer()
    /// The rows that keep the keyboard (KeepsTyping): the field's row with Send and the microphone.
    private let keepers = NSHashTable<UIView>.weakObjects()

    private override init() {
        super.init()
        tap.addTarget(self, action: #selector(tapped(_:)))
        tap.cancelsTouchesInView = false
        tap.delaysTouchesBegan = false
        tap.delaysTouchesEnded = false
        tap.delegate = self
        let center = NotificationCenter.default
        for (name, starts) in [
            (UITextField.textDidBeginEditingNotification, true), (UITextField.textDidEndEditingNotification, false),
            (UITextView.textDidBeginEditingNotification, true), (UITextView.textDidEndEditingNotification, false),
        ] {
            center.addObserver(self, selector: starts ? #selector(began(_:)) : #selector(ended(_:)), name: name, object: nil)
        }
    }

    /// Puts the keyboard away, whichever field has it; nothing when nothing is typed in.
    func hide() {
        guard focus.editing else { return }
        if let editorView {
            editorView.resignFirstResponder()
        } else {
            UIApplication.shared.sendAction(#selector(UIResponder.resignFirstResponder), to: nil, from: nil, for: nil)
        }
    }

    func keep(_ view: UIView) {
        keepers.add(view)
    }

    @objc private func began(_ note: Notification) {
        guard let view = note.object as? UIView else { return }
        editorView = view
        focus.began(ObjectIdentifier(view))
        // The window the field is in is the one watched, from the first field typed in.
        if let window = view.window, tap.view !== window {
            tap.view?.removeGestureRecognizer(tap)
            window.addGestureRecognizer(tap)
        }
    }

    @objc private func ended(_ note: Notification) {
        guard let view = note.object as? UIView else { return }
        focus.ended(ObjectIdentifier(view))
        if !focus.editing { editorView = nil }
    }

    @objc private func tapped(_ recognizer: UITapGestureRecognizer) {
        guard recognizer.state == .ended, let window = recognizer.view else { return }
        if focus.hides(place(of: pressed, at: recognizer.location(in: window))) { hide() }
    }

    /// What a press at a point of the window landed on (TypingFocus.Place).
    private func place(of view: UIView?, at point: CGPoint) -> TypingFocus.Place {
        guard let view else { return .elsewhere }
        let chain = Array(sequence(first: view, next: { $0.superview }))
        if let editorView, chain.contains(where: { $0 === editorView }) { return .editor }
        if chain.contains(where: { $0 is UITextField || $0 is UITextView }) { return .otherField }
        if chain.contains(where: { Self.isSystemMenu($0) }) { return .systemMenu }
        if keepers.allObjects.contains(where: { $0.window != nil && $0.convert($0.bounds, to: nil).contains(point) }) { return .keeper }
        return .elsewhere
    }

    /// The system's edit menu or context menu over a field (Paste must reach the field).
    private static func isSystemMenu(_ view: UIView) -> Bool {
        let name = NSStringFromClass(type(of: view))
        return ["EditMenu", "Callout", "ContextMenu"].contains { name.contains($0) }
    }

    // MARK: - UIGestureRecognizerDelegate

    /// Only while something is typed in: otherwise the window's touches are not even looked at.
    func gestureRecognizer(_ gestureRecognizer: UIGestureRecognizer, shouldReceive touch: UITouch) -> Bool {
        guard focus.editing else { return false }
        pressed = touch.view
        return true
    }

    func gestureRecognizer(_ gestureRecognizer: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer) -> Bool {
        true
    }
}

/// Over the score's and the roll's web views while something is typed in (TypingFocus.shieldsMusic): the first press there,
/// a tap or the start of a scroll, only puts the keyboard away; the shield then goes, so the next press is the music's.
struct TypingShield: View {
    @ObservedObject private var typing = Typing.shared

    var body: some View {
        if typing.focus.shieldsMusic {
            Color.clear
                .contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance: 0).onChanged { _ in typing.hide() })
                .accessibilityHidden(true)
        }
    }
}

/// Marks a row whose presses keep the keyboard up (Typing): a chat's field with Send and the microphone.
private struct KeepsTypingMark: UIViewRepresentable {
    func makeUIView(context: Context) -> UIView {
        let view = UIView()
        view.isUserInteractionEnabled = false
        view.backgroundColor = .clear
        Typing.shared.keep(view)
        return view
    }

    func updateUIView(_ view: UIView, context: Context) {}
}

extension View {
    /// Presses on this view keep the keyboard up (Typing): the field's own row.
    func keepsTyping() -> some View {
        background(KeepsTypingMark().accessibilityHidden(true))
    }
}
