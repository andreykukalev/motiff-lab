import SwiftUI
import UIKit

/// How a chat with the assistant behaves on the iPad (#582 item 16), whichever surface holds it: the Ask popover, the
/// assistant's side panel or the small one at the selection. One sequence, the same with the Pencil and with a finger:
///
/// 1. Open: the chat shows with all its own controls (close, the field, Send); the field waits, so no keyboard comes up
///    on its own and nothing on the screen moves.
/// 2. Focus: a press on the field, by a finger or by the Pencil (Scribble is declined there, so the Pencil's press is a
///    finger's), a hardware keyboard or VoiceOver, focuses it. The iPad's keyboard comes with its own bar (dictation's
///    microphone, the keyboard's switch), which the app never hides.
/// 3. Keyboard: the chat stays where it is with all its controls, raised above the keyboard where it would cover them;
///    the score, the pill and the bars under it do not move.
/// 4. Type and Send (the button or the keyboard's Send): the question goes, the field empties and keeps the focus.
/// 5. Hide the keyboard (its own key, or a tap anywhere outside the field and its row: the chat's ground, the panel, the
///    score, which then does nothing else, TypingFocus): the field lets go, its text stays, the chat stays open with all
///    its controls, and so does everything around it. Only the player's press focuses the field again: the app never
///    gives it the focus by itself (v0.6.4: a focus given back from the chat's state left the field focused without the
///    keyboard, and iPadOS's bar of the hidden keyboard, «RU» and Undo, lay over the pill).
/// 6. Close: a press outside never closes the chat while its field has the focus (it puts the keyboard away). The panel
///    closes only by its close button; the small chat at the selection also closes from outside once the field has let go.
struct AskFlow: Equatable {
    /// Where the chat is: the Ask popover, the full assistant's side panel, or the contextual one at the selection.
    enum Surface: Equatable {
        case popover, panel, contextual
    }

    enum Event: Equatable {
        case open
        /// The field took the focus.
        case focus
        /// The field let it go: the keyboard's hide key, a press on the chat's ground, or another field taking the focus.
        case blur
        /// The iPad's keyboard (or the shortcuts bar of a hardware keyboard) came (true) or went (false).
        case keyboard(Bool)
        case type(String)
        case send
        /// A press outside the chat, on the score or the screen around it.
        case pressOutside
        case close
    }

    let surface: Surface
    private(set) var isOpen = false
    private(set) var focused = false
    private(set) var keyboardShown = false
    private(set) var draft = ""

    init(surface: Surface) {
        self.surface = surface
    }

    /// The chat's own controls (close, the field, Send) show whenever the chat is open, whatever the keyboard does.
    var controlsShown: Bool { isOpen }

    /// What Send would ask: the draft without its spaces; empty when there is nothing to send.
    var question: String { draft.trimmingCharacters(in: .whitespacesAndNewlines) }

    var canSend: Bool { isOpen && !question.isEmpty }

    /// Whether a press outside closes the chat now: never the panel, and never while the player types.
    var closesOnOutsidePress: Bool { isOpen && !focused && surface != .panel }

    /// The chat after `event`, and the question asked when the event sent one.
    @discardableResult
    mutating func apply(_ event: Event) -> String? {
        switch event {
        case .open:
            isOpen = true
            focused = false
        case .focus:
            if isOpen { focused = true }
        case .blur:
            focused = false
        case let .keyboard(shown):
            // The keyboard coming or going changes nothing else: the chat, its controls and its text stay.
            keyboardShown = shown
        case let .type(text):
            if isOpen { draft = text }
        case .send:
            guard canSend else { return nil }
            let asked = question
            draft = ""
            return asked
        case .pressOutside:
            if focused {
                focused = false
            } else if closesOnOutsidePress {
                self = AskFlow(surface: surface)
            }
        case .close:
            self = AskFlow(surface: surface)
        }
        return nil
    }
}

/// The text field of a chat with the assistant (#582 item 16): a UIKit field, so the Pencil focuses it as a finger does.
/// iPadOS gives a Pencil's press on a text field to Scribble, which takes the focus without the keyboard (and shows its own
/// small palette instead); the field declines Scribble, so the press opens the keyboard. The keyboard's own bar (dictation,
/// the keyboard switch) is the system's and stays: the field sets no input assistant or accessory of its own. Its return key
/// is Send.
struct AskField: UIViewRepresentable {
    @Binding var text: String
    @Binding var focused: Bool
    let placeholder: String
    var enabled = true
    let submit: () -> Void

    func makeCoordinator() -> Coordinator {
        Coordinator(self)
    }

    func makeUIView(context: Context) -> UITextField {
        let field = UITextField()
        field.delegate = context.coordinator
        field.font = Self.font
        field.adjustsFontForContentSizeCategory = true
        field.textColor = DesignTokens.dynamicColor("ink")
        field.tintColor = DesignTokens.dynamicColor("accent")
        field.returnKeyType = .send
        field.enablesReturnKeyAutomatically = true
        field.setContentHuggingPriority(.defaultLow, for: .horizontal)
        field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        field.addTarget(context.coordinator, action: #selector(Coordinator.changed(_:)), for: .editingChanged)
        field.addInteraction(UIScribbleInteraction(delegate: context.coordinator))
        return field
    }

    func updateUIView(_ field: UITextField, context: Context) {
        context.coordinator.parent = self
        if field.text != text { field.text = text }
        field.isEnabled = enabled
        field.attributedPlaceholder = NSAttributedString(
            string: placeholder, attributes: [.foregroundColor: DesignTokens.dynamicColor("ink-3"), .font: Self.font]
        )
        field.accessibilityLabel = placeholder
        // The chat lets the field go (a press on its ground); only the player's press gives it the focus (AskFlow 5).
        if !focused && field.isFirstResponder {
            DispatchQueue.main.async { field.resignFirstResponder() }
        }
    }

    /// The kit's body face (t-body), growing with the reader's text size.
    static var font: UIFont {
        let token = DSType.body.token
        let face = DesignTokens.faces["sans-\(token.weight < 450 ? 400 : token.weight < 550 ? 500 : 600)"]
        let base = face.flatMap { UIFont(name: $0, size: token.size) } ?? UIFont.systemFont(ofSize: token.size)
        return UIFontMetrics(forTextStyle: .body).scaledFont(for: base)
    }

    final class Coordinator: NSObject, UITextFieldDelegate, UIScribbleInteractionDelegate {
        var parent: AskField

        init(_ parent: AskField) {
            self.parent = parent
        }

        @objc func changed(_ field: UITextField) {
            parent.text = field.text ?? ""
        }

        func textFieldDidBeginEditing(_ textField: UITextField) {
            if !parent.focused { parent.focused = true }
        }

        func textFieldDidEndEditing(_ textField: UITextField) {
            if parent.focused { parent.focused = false }
        }

        /// The keyboard's Send: the question goes, and the field keeps the focus for the next one.
        func textFieldShouldReturn(_ textField: UITextField) -> Bool {
            parent.submit()
            return false
        }

        /// The Pencil's press is a press: no Scribble in this field, so the keyboard comes as it does for a finger.
        func scribbleInteraction(_ interaction: UIScribbleInteraction, shouldBeginAt location: CGPoint) -> Bool {
            false
        }
    }
}

/// The chat's field and Send in one row, the same on every surface: the kit's text field (DSFieldLook) at a fingertip's
/// height with its placeholder, the microphone inside it with «Диктовка» on (Dictation), and Send beside it, enabled once
/// there is something to ask. Presses on the row keep the keyboard up (Typing); a press anywhere else puts it away.
struct AskInputRow: View {
    @Binding var flow: AskFlow
    let text: UIText
    var enabled = true
    let send: (String) -> Void
    @ObservedObject private var settings = DeviceSettings.shared
    @StateObject private var dictation: Dictation

    init(flow: Binding<AskFlow>, text: UIText, enabled: Bool = true, send: @escaping (String) -> Void) {
        _flow = flow
        self.text = text
        self.enabled = enabled
        self.send = send
        _dictation = StateObject(wrappedValue: Dictation(language: text.language))
    }

    /// Dictation (v0.6.4): the microphone beside the field, always since v0.6.5.
    private let dictating = true

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                HStack(spacing: 0) {
                    AskField(
                        text: Binding(get: { flow.draft }, set: { flow.apply(.type($0)) }),
                        focused: Binding(get: { flow.focused }, set: { flow.apply($0 ? .focus : .blur) }),
                        placeholder: text["padAsk.placeholder"],
                        enabled: enabled,
                        submit: submit
                    )
                    .frame(maxWidth: .infinity, minHeight: CGFloat.ds("h-touch"))
                    if dictating {
                        DictationButton(dictation: dictation, text: text, draft: flow.draft).disabled(!enabled)
                    }
                }
                .padding(.leading, CGFloat.ds("s-3"))
                .padding(.trailing, dictating ? 0 : CGFloat.ds("s-3"))
                .modifier(DSFieldLook(focused: flow.focused || dictation.flow.listening))
                Button(text["mentor.send"], action: submit)
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    .disabled(!enabled || !flow.canSend)
            }
            .keepsTyping()
            if dictating, let line = DictationButton.line(dictation.flow, text) {
                Text(line).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
        }
        .onAppear {
            let flow = $flow
            dictation.write = { flow.wrappedValue.apply(.type($0)) }
        }
        // Typing, sending, a busy assistant or the chat going: the dictation stops, so it never writes over the player.
        .onChange(of: flow.focused) { _, focused in if focused { dictation.halt() } }
        .onChange(of: enabled) { _, now in if !now { dictation.halt() } }
        .onDisappear { dictation.halt() }
        .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillShowNotification)) { _ in
            flow.apply(.keyboard(true))
        }
        .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillHideNotification)) { _ in
            flow.apply(.keyboard(false))
        }
    }

    private func submit() {
        guard enabled, let asked = flow.apply(.send) else { return }
        dictation.halt()
        send(asked)
    }
}

/// The kit's text field (ds.css `.field`, docs/02-design-system.md «Fields»): the fields' ground (`surface-2`), the strong
/// hairline and the 8 px radius (`r-s`), so it reads as a place to type rather than a capsule (v0.6.4); in focus the border
/// turns cobalt with the accent's tint as a ring around it.
struct DSFieldLook: ViewModifier {
    let focused: Bool
    /// ds.css `.field.focus`: `box-shadow: 0 0 0 3px var(--accent-tint)`.
    private static let ring: CGFloat = 3

    func body(content: Content) -> some View {
        let shape = RoundedRectangle(cornerRadius: CGFloat.ds("r-s"))
        content
            .background(Color.ds("surface-2"), in: shape)
            .overlay(shape.strokeBorder(Color.ds(focused ? "accent" : "hairline-strong")))
            // Drawn under the field's ground: only its outer half shows, the ring outside the border.
            .background { shape.stroke(Color.ds("accent-tint"), lineWidth: Self.ring * 2).opacity(focused ? 1 : 0) }
            .animation(.ds("t-fast"), value: focused)
    }
}

/// Room for the iPad's keyboard under a surface with a chat in it (#582 item 16): the surface's content keeps clear of
/// the keyboard where the keyboard covers it, so its field and controls stay in sight, while nothing else on the screen
/// moves (the score's screen ignores the keyboard).
struct KeyboardRoom: ViewModifier {
    @State private var keyboard: CGRect?
    @State private var frame: CGRect = .zero

    func body(content: Content) -> some View {
        content
            .safeAreaInset(edge: .bottom, spacing: 0) {
                Color.clear.frame(height: Self.overlap(of: frame, keyboard: keyboard))
            }
            .background {
                GeometryReader { proxy in
                    Color.clear
                        .onAppear { frame = proxy.frame(in: .global) }
                        .onChange(of: proxy.frame(in: .global)) { _, now in frame = now }
                }
            }
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillChangeFrameNotification)) { note in
                keyboard = Self.frame(of: note)
            }
            .onReceive(NotificationCenter.default.publisher(for: UIResponder.keyboardWillHideNotification)) { _ in
                keyboard = nil
            }
    }

    /// How much of `view`'s height the keyboard covers from below, both in the window's coordinates; 0 when it covers
    /// none of it (no keyboard, a floating one beside it, or one below it).
    static func overlap(of view: CGRect, keyboard: CGRect?) -> CGFloat {
        guard let keyboard, !keyboard.isEmpty, !view.isEmpty,
              keyboard.minX < view.maxX, keyboard.maxX > view.minX,
              keyboard.minY < view.maxY, keyboard.maxY >= view.maxY
        else { return 0 }
        return min(view.height, view.maxY - keyboard.minY)
    }

    /// The keyboard's frame at the end of its move, in the key window's coordinates.
    static func frame(of note: Notification) -> CGRect? {
        guard let end = note.userInfo?[UIResponder.keyboardFrameEndUserInfoKey] as? CGRect else { return nil }
        let window = UIApplication.shared.connectedScenes.compactMap { ($0 as? UIWindowScene)?.keyWindow }.first
        guard let window else { return end }
        return window.convert(end, from: window.screen.coordinateSpace)
    }
}

extension View {
    /// Keeps the content clear of the iPad's keyboard where it covers it (KeyboardRoom).
    func keyboardRoom() -> some View {
        modifier(KeyboardRoom())
    }
}
