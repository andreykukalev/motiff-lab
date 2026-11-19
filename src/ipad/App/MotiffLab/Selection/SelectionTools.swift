import SwiftUI

/// What the stylus menu offers with the modes (#521 11): the tools on its inner circle, and above them, together, only the
/// chosen tool's own controls (StylusRules.ringControls), which act on that tool's history; the tools' settings (the
/// Pen's inks and thicknesses, Select's bars or beats) stay in the palettes under the bar.
struct RingModes {
    var chosen: InteractionMode
    var choose: (InteractionMode) -> Void
    var controls: [StylusRules.RingControl]
    var canUndo: Bool
    var canRedo: Bool
    var canClear: Bool
    var undo: () -> Void
    var redo: () -> Void
    var clear: () -> Void
}

/// The stylus menu's rules (#511 4.2), apart from the views so that they are tested.
enum StylusRules {
    /// The Pencil's «switch to previous» with selection by the place of the touch (v0.6.2, #601 H 5, the lead's decision 9):
    /// Select and the tool before it take turns, from any tool; back from Select with no tool before it, the Pen, as the
    /// turn of the Pen and Select was before. What the next turn goes back to comes with it.
    static func selectTurn(current: InteractionMode, before: InteractionMode?) -> (chosen: InteractionMode, before: InteractionMode?) {
        current == .select ? (before ?? .pen, nil) : (.select, current)
    }

    /// Whose history Undo and Redo act on (#521 9): each tool that edits has its own, and Navigate, which only moves the
    /// music, has none.
    enum UndoOwner: Equatable { case ink, selection, noteEdits, nothing }

    /// The history of the tool in force: the Pen's ink while the Pen is on; the roll's note edits while the roll is in Edit
    /// notes (the two never at once, RollInput.penOff); else the bar's mode: Select's selection, nothing in Navigate.
    static func undoOwner(penOn: Bool, mode: InteractionMode, rollEditing: Bool) -> UndoOwner {
        if penOn { return .ink }
        if rollEditing { return .noteEdits }
        switch mode {
        case .navigate: return .nothing
        case .select: return .selection
        case .edit: return .noteEdits
        case .pen: return .ink
        }
    }

    /// A control of the stylus menu, by the tool it belongs to (#521 8, 11).
    enum RingControl: Equatable {
        case undo, redo, clearSelection

        /// The kit's icon (Redo draws Undo's mirrored). Clear selection has its own (#533 5), the selection's frame with a
        /// cross inside it, wherever the action is (the stylus menu, the selection menu, the roll's head, the web's
        /// selection menu): the bare cross read as closing the menu.
        var icon: String {
            switch self {
            case .undo, .redo: "undo"
            case .clearSelection: "deselect"
            }
        }
    }

    /// The chosen tool's controls in the stylus menu: Select's Undo, Redo and Clear selection; the Pen's Undo and Redo;
    /// nothing for Navigate, which has no history and no settings.
    static func ringControls(_ tool: InteractionMode) -> [RingControl] {
        switch tool {
        case .select: [.undo, .redo, .clearSelection]
        case .pen: [.undo, .redo]
        case .navigate, .edit: []
        }
    }
}

/// Select's «По тактам / По долям» of before v0.6.4, kept on this device under `key`: no longer read (Andrei, 19:53Z, one
/// selection on the sixteenth grid), and taken off the device once, so an iPad that chose bars keeps nothing of it.
enum SelectBy {
    static let key = "selection.byBeats"

    static func forget(_ defaults: UserDefaults = .standard) {
        defaults.removeObject(forKey: key)
    }
}

extension ScoreController {
    /// The Pencil's double tap or squeeze on the score: «switch to previous» as StylusRules says; the rest as on a PDF.
    func scorePencilGesture(_ action: PencilGesture.Action, at point: CGPoint?) {
        guard action == .previous else {
            penModel.pencilGesture(action, at: point)
            return
        }
        let turn = StylusRules.selectTurn(current: penModel.penOn ? .pen : mode, before: toolBeforeSelect)
        toolBeforeSelect = turn.before
        chooseMode(turn.chosen)
    }

    /// A mode chosen in the ring, as in the bar: the Pen is the Pen being on; another mode leaves it.
    func chooseMode(_ chosen: InteractionMode) {
        if chosen == .pen {
            if !penModel.penOn { penModel.pen.writeOnChoosing() }
            penModel.penOn = true
        } else {
            mode = chosen
            penModel.penOn = false
        }
    }

    /// The stylus menu's tools and the chosen one's controls: the Pen's on its ink, Select's on the selection; Navigate's none.
    var ringModes: RingModes {
        let chosen = penModel.penOn ? InteractionMode.pen : mode
        let ink = chosen == .pen
        return RingModes(
            chosen: chosen,
            choose: { [weak self] tool in self?.chooseMode(tool) },
            controls: ExpandRules.ringControls(focused: focusModel.focused, otherwise: StylusRules.ringControls(chosen)),
            canUndo: ink ? penModel.canUndo : selectionModel.steps.canUndo,
            canRedo: ink ? penModel.canRedo : selectionModel.steps.canRedo,
            canClear: selectionModel.selection != nil && !focusModel.focused,
            undo: { [weak self] in if ink { self?.penModel.undo() } else { self?.undoSelection() } },
            redo: { [weak self] in if ink { self?.penModel.redo() } else { self?.redoSelection() } },
            clear: { [weak self] in self?.clearSelection() }
        )
    }

    /// The history Undo and Redo act on now (#521 9): the tool's in force; none in Navigate, nor in Expand (#521 19.3).
    var undoOwner: StylusRules.UndoOwner {
        ExpandRules.undoOwner(focused: focusModel.focused, otherwise: StylusRules.undoOwner(penOn: penModel.penOn, mode: mode, rollEditing: rollEditing?() ?? false))
    }

    /// ⌘Z: Undo of the tool in force; nothing in Navigate.
    func undoKey() {
        switch undoOwner {
        case .ink: penModel.undo()
        case .selection: undoSelection()
        case .noteEdits: if noteEditor?.canUndo == true { undoNoteEdit() }
        case .nothing: break
        }
    }

    /// ⇧⌘Z: Redo of the tool in force; the note edits have none, nor has Navigate.
    func redoKey() {
        switch undoOwner {
        case .ink: penModel.redo()
        case .selection: redoSelection()
        case .noteEdits, .nothing: break
        }
    }

    /// The piece's beat lines, as the screen reads them: the score is always sent them, as the one selection's edges stop
    /// on the sixteenth grid (v0.6.4, Andrei 19:53Z: no «По тактам / По долям»; the page draws the band and its beat ticks
    /// with the lines).
    func setBeatLines(_ lines: [Double]) {
        SelectBy.forget()
        pieceBeatLines = lines
        beatLines = lines
    }
}

extension RollController {
    /// A mode chosen on the roll's switch, one tool at a time (#521 10): Edit notes turns the bar's Pen off first.
    func chooseTool(_ mode: RollMode, score: ScoreController) {
        if RollInput.penOff(choosing: mode) { score.penModel.penOn = false }
        choose(mode, score: score)
    }
}

/// The options of the mode in force under the bar (#511 4.2, option B): the pen palette while the Pen is on, Select's row
/// in Select; Navigate has none.
struct ModeOptions: View {
    @ObservedObject var score: ScoreController
    /// Handed on to Select's row, which shows the selection's Undo and Redo.
    let selectionModel: SelectionModel
    let text: UIText
    /// Whether the piece has other versions, from the screen that observes them: the layers list offers their marks.
    let hasVersions: Bool
    /// Whether the bar's Pen is on, and whether a layer of the player's marks is hidden, from the screen that observes the
    /// pen and the marks (Selection may read neither).
    let penOn: Bool
    let marksHidden: Bool

    var body: some View {
        if penOn {
            PenPalette(score: score, text: text, hasVersions: hasVersions, marksHidden: marksHidden)
                .transition(.opacity.combined(with: .move(edge: .top)))
        } else if score.mode == .select {
            SelectionOptionsRow(score: score, selectionModel: selectionModel, text: text).transition(.opacity)
        }
    }
}

/// Select's options under the bar (#511 4.2, option B): the selection's Undo and Redo, grey in place when there is
/// nothing to take back. Navigate has none. «По тактам / По долям» went in v0.6.4 (Andrei, 19:53Z: one selection).
struct SelectionOptionsRow: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var selectionModel: SelectionModel
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Button { score.undoSelection() } label: { Icon(name: "undo", size: CGFloat.ds("icon-s")) }
                .buttonStyle(DSIconItemStyle())
                .disabled(!selectionModel.steps.canUndo)
                .accessibilityLabel(text["selection.undo"])
            Button { score.redoSelection() } label: { Icon(name: "undo", size: CGFloat.ds("icon-s")).scaleEffect(x: -1, y: 1) }
                .buttonStyle(DSIconItemStyle())
                .disabled(!selectionModel.steps.canRedo)
                .accessibilityLabel(text["selection.redo"])
        }
        .modifier(PaletteSurface())
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["selection.options"])
    }
}

/// The selection menu moved aside (#582 item 7): pressed and dragged with a finger or the Pencil it follows, and let go it
/// stays there (ScoreController.moveMenu; the page keeps the place inside the window, Scripts/score/menu-drag.ts) until the
/// menu closes, when the next one opens over its band again. A tap still reaches the menu's buttons: the drag begins only
/// once the touch has moved as far as a selection drag does (DRAG_DISTANCE in Scripts/score/selection-gesture.ts). A card
/// in the menu with a handle (`menuDragHandle()`, the assistant's chat at the selection) is dragged only by it, so its
/// answer scrolls (Andrei, 2026-11-18: the whole card's drag took the scroll's pan, by finger and by the Pencil).
struct MenuDrag: ViewModifier {
    let score: ScoreController
    @State private var moved = CGSize.zero
    /// Whether a handle in the menu takes the drag (MenuHandleKey).
    @State private var handled = false

    /// How far a touch on the menu moves before it drags the menu, in points.
    static let distance: CGFloat = 6

    func body(content: Content) -> some View {
        let moves = MenuDragMoves(changed: { moved = $0 }, ended: { translation in
            score.moveMenu(by: translation)
            moved = .zero
        })
        content
            .environment(\.menuDragMoves, moves)
            .onPreferenceChange(MenuHandleKey.self) { handled = $0 }
            .offset(moved)
            .highPriorityGesture(Self.drag(moves), including: handled ? .subviews : .all)
    }

    /// The drag that moves the menu, on the whole menu or on its handle.
    static func drag(_ moves: MenuDragMoves) -> some Gesture {
        DragGesture(minimumDistance: distance, coordinateSpace: .global)
            .onChanged { value in moves.changed(value.translation) }
            .onEnded { value in moves.ended(value.translation) }
    }
}

/// Where a drag of the menu goes (MenuDrag): the menu follows it, and let go it stays there.
struct MenuDragMoves {
    var changed: (CGSize) -> Void = { _ in }
    var ended: (CGSize) -> Void = { _ in }
}

private struct MenuDragMovesKey: EnvironmentKey {
    static let defaultValue = MenuDragMoves()
}

/// Whether the menu shows a card with a handle, which alone drags it.
struct MenuHandleKey: PreferenceKey {
    static let defaultValue = false

    static func reduce(value: inout Bool, nextValue: () -> Bool) {
        value = value || nextValue()
    }
}

extension EnvironmentValues {
    var menuDragMoves: MenuDragMoves {
        get { self[MenuDragMovesKey.self] }
        set { self[MenuDragMovesKey.self] = newValue }
    }
}

/// The part of a card in the menu that drags the menu (MenuDrag); the rest of the card keeps its own gestures (a scroll).
private struct MenuDragHandle: ViewModifier {
    @Environment(\.menuDragMoves) private var moves

    func body(content: Content) -> some View {
        content
            .contentShape(Rectangle())
            .highPriorityGesture(MenuDrag.drag(moves))
            .preference(key: MenuHandleKey.self, value: true)
    }
}

extension View {
    /// Makes this the handle that alone drags the selection menu it is in (MenuDrag).
    func menuDragHandle() -> some View {
        modifier(MenuDragHandle())
    }
}

/// After a clear or a replace, «Выделение снято · Вернуть» for six seconds (#511 11): one tap brings the selection back,
/// with any pointer; no Redo here. While a pointer that cannot scroll moves off the selection before the hold, the hint
/// to hold. Nothing when there is nothing to take back. It holds ⌘Z and ⇧⌘Z too, which are always there.
struct SelectionToastView: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var selectionModel: SelectionModel
    /// Whether Expand is open, from the screen that observes it (Selection may not name FocusModel): no toast then.
    let focused: Bool
    let text: UIText

    var body: some View {
        ZStack {
            UndoKeys(score: score)
            if let notice = score.notice {
                // A word of guidance, such as why Expand waits for a selection (#521 19.1).
                Text(text[notice.key]).dsType(.label).foregroundStyle(Color.ds("ink")).modifier(ToastSurface())
            } else if selectionModel.toast != nil && selectionModel.steps.canUndo && !focused {
                HStack(spacing: CGFloat.ds("s-3")) {
                    Text(text[selectionModel.toastKey]).dsType(.label).foregroundStyle(Color.ds("ink"))
                    Button(text["selection.bringBack"]) { score.undoSelection() }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
                .modifier(ToastSurface())
            } else if selectionModel.holdHint != nil {
                Text(text["selection.holdHint"]).dsType(.label).foregroundStyle(Color.ds("ink")).modifier(ToastSurface())
            }
        }
        .animation(.easeOut(duration: 0.16), value: selectionModel.toast)
        .animation(.easeOut(duration: 0.16), value: selectionModel.holdHint)
        .animation(.easeOut(duration: 0.16), value: score.notice)
    }
}

/// ⌘Z and ⇧⌘Z on a keyboard: Undo and Redo of the tool in force (#521 9, ScoreController.undoOwner), nothing in Navigate.
struct UndoKeys: View {
    @ObservedObject var score: ScoreController

    var body: some View {
        Group {
            Button("") { score.undoKey() }.keyboardShortcut("z", modifiers: .command)
            Button("") { score.redoKey() }.keyboardShortcut("z", modifiers: [.command, .shift])
        }
        .frame(width: 0, height: 0)
        .opacity(0)
        .accessibilityHidden(true)
    }
}

private struct ToastSurface: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(.horizontal, CGFloat.ds("s-4"))
            .padding(.vertical, CGFloat.ds("s-2"))
            .background(Color.ds("surface"), in: Capsule())
            .overlay(Capsule().strokeBorder(Color.ds("hairline")))
            .dsShadow("shadow-menu")
            .accessibilityElement(children: .contain)
    }
}

/// Where the selection's toast («Выделение заменено · Вернуть», «Выделение снято · Вернуть», a notice) stands over the bottom
/// of the score (v0.6.4, Andrei's spec 7): above everything that floats there with the transport (the pill with Loop, the
/// tempo and the metronome, the practice controls), as tall as that is now, and never under it; over the score's bottom
/// edge or a drawer when nothing floats.
enum ToastPlace {
    /// The toast's bottom padding: the transport's own (`pillBottom`), then the transport's measured height and a gap.
    static func bottom(pillBottom: CGFloat, transport: CGFloat, gap: CGFloat) -> CGFloat {
        transport > 0 ? pillBottom + transport + gap : pillBottom
    }

    /// The toast's padding: `bottom` above the transport, and beside a sheet or a drawer at the side as the pill is.
    static func insets(pillBottom: CGFloat, transport: CGFloat, gap: CGFloat, leading: CGFloat, trailing: CGFloat) -> EdgeInsets {
        EdgeInsets(top: 0, leading: leading, bottom: bottom(pillBottom: pillBottom, transport: transport, gap: gap), trailing: trailing)
    }
}

