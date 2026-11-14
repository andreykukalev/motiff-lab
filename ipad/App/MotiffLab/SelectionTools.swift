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
    /// What the Pencil's «switch to previous» does on the score: with the modes the Pen and Select take turns (the Pen
    /// on, or off into Select); without them the Pen comes on, and while it is on the tool before comes back.
    static func previous(penOn: Bool, mode: InteractionMode?) -> (penOn: Bool, mode: InteractionMode?, previousTool: Bool) {
        guard mode != nil else { return (true, nil, penOn) }
        return penOn ? (false, .select, false) : (true, mode, false)
    }

    /// Whose history Undo and Redo act on (#521 9): each tool that edits has its own, and Navigate, which only moves the
    /// music, has none.
    enum UndoOwner: Equatable { case ink, selection, noteEdits, nothing }

    /// The history of the tool in force: the Pen's ink while the Pen is on; the roll's note edits while the roll is in Edit
    /// notes (the two never at once, RollInput.penOff); else the bar's mode: Select's selection, nothing in Navigate. Before
    /// the modes the score selects as it did, so the selection's.
    static func undoOwner(penOn: Bool, mode: InteractionMode?, rollEditing: Bool) -> UndoOwner {
        if penOn { return .ink }
        if rollEditing { return .noteEdits }
        switch mode {
        case .navigate: return .nothing
        case .select, nil: return .selection
        case .edit: return .noteEdits
        case .pen: return .ink
        }
    }

    /// A control of the stylus menu, by the tool it belongs to (#521 8, 11).
    enum RingControl: Equatable { case undo, redo, clearSelection }

    /// The chosen tool's controls in the stylus menu: Select's Undo, Redo and Clear selection; the Pen's Undo and Redo;
    /// nothing for Navigate, which has no history and no settings.
    static func ringControls(_ tool: InteractionMode) -> [RingControl] {
        switch tool {
        case .select: [.undo, .redo, .clearSelection]
        case .pen: [.undo, .redo]
        case .navigate, .edit: []
        }
    }

    /// Whether the selection under the modes goes by beats: as Select's options say; without the modes, as the
    /// experiment Select by beats says (#349).
    static func byBeats(modes: Bool, chosen: Bool, experiment: Bool) -> Bool {
        modes ? chosen : experiment
    }
}

/// Select's «По тактам / По долям», kept on this device; by beats until the player chooses bars.
enum SelectBy {
    static let key = "selection.byBeats"

    static func saved(_ defaults: UserDefaults = .standard) -> Bool {
        defaults.object(forKey: key) as? Bool ?? true
    }

    static func keep(_ byBeats: Bool, _ defaults: UserDefaults = .standard) {
        defaults.set(byBeats, forKey: key)
    }
}

extension ScoreController {
    /// The Pencil's double tap or squeeze on the score: «switch to previous» as StylusRules says; the rest as on a PDF.
    func scorePencilGesture(_ action: PencilGesture.Action, at point: CGPoint?) {
        guard action == .previous else {
            pencilGesture(action, at: point)
            return
        }
        let next = StylusRules.previous(penOn: penOn, mode: mode)
        if next.previousTool { pen.choosePrevious() }
        if let chosen = next.mode, chosen != mode { mode = chosen }
        penOn = next.penOn
    }

    /// A mode chosen in the ring, as in the bar: the Pen is the Pen being on; another mode leaves it.
    func chooseMode(_ chosen: InteractionMode) {
        if chosen == .pen {
            penOn = true
        } else {
            mode = chosen
            penOn = false
        }
    }

    /// The stylus menu's tools and the chosen one's controls, while the modes are on: the Pen's on its ink, Select's on the
    /// selection; Navigate's none.
    var ringModes: RingModes? {
        guard let mode else { return nil }
        let chosen = penOn ? InteractionMode.pen : mode
        let ink = chosen == .pen
        return RingModes(
            chosen: chosen,
            choose: { [weak self] tool in self?.chooseMode(tool) },
            controls: StylusRules.ringControls(chosen),
            canUndo: ink ? canUndo : selectionSteps.canUndo,
            canRedo: ink ? canRedo : selectionSteps.canRedo,
            canClear: selection != nil,
            undo: { [weak self] in if ink { self?.undo() } else { self?.undoSelection() } },
            redo: { [weak self] in if ink { self?.redo() } else { self?.redoSelection() } },
            clear: { [weak self] in self?.clearSelection() }
        )
    }

    /// The history Undo and Redo act on now (#521 9): the tool's in force; none in Navigate.
    var undoOwner: StylusRules.UndoOwner {
        StylusRules.undoOwner(penOn: penOn, mode: mode, rollEditing: rollEditing?() ?? false)
    }

    /// ⌘Z: Undo of the tool in force; nothing in Navigate.
    func undoKey() {
        switch undoOwner {
        case .ink: undo()
        case .selection: undoSelection()
        case .noteEdits: if noteEditor?.canUndo == true { undoNoteEdit() }
        case .nothing: break
        }
    }

    /// ⇧⌘Z: Redo of the tool in force; the note edits have none, nor has Navigate.
    func redoKey() {
        switch undoOwner {
        case .ink: redo()
        case .selection: redoSelection()
        case .noteEdits, .nothing: break
        }
    }

    /// «По тактам / По долям» chosen: kept on the device, and the score snaps to the beats or to the bars from now on.
    func chooseSelectBy(beats: Bool) {
        selectsByBeats = beats
        SelectBy.keep(beats)
        applyBeatLines()
    }

    /// The piece's beat lines and the experiment Select by beats, as the screen reads them; the lines the score snaps to follow.
    func setBeatLines(_ lines: [Double], experiment: Bool) {
        pieceBeatLines = lines
        beatsExperiment = experiment
        applyBeatLines()
    }

    private func applyBeatLines() {
        beatLines = StylusRules.byBeats(modes: mode != nil, chosen: selectsByBeats, experiment: beatsExperiment) ? pieceBeatLines : []
    }
}

extension RollController {
    /// A mode chosen on the roll's switch, one tool at a time (#521 10): Edit notes turns the bar's Pen off first.
    func chooseTool(_ mode: RollMode, score: ScoreController) {
        if RollInput.penOff(choosing: mode) { score.penOn = false }
        choose(mode, score: score)
    }
}

/// The options of the mode in force under the bar (#511 4.2, option B): the pen palette while the Pen is on, Select's row
/// in Select; Navigate has none.
struct ModeOptions: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        if score.penOn {
            PenPalette(score: score, text: text)
                .transition(.opacity.combined(with: .move(edge: .top)))
        } else if score.mode == .select {
            SelectionOptionsRow(score: score, text: text).transition(.opacity)
        }
    }
}

/// Select's options under the bar (#511 4.2, option B): the selection's Undo and Redo, grey in place when there is
/// nothing to take back, and whether a selection goes by bars or by beats. Navigate has none.
struct SelectionOptionsRow: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Button { score.undoSelection() } label: { Icon(name: "undo", size: CGFloat.ds("icon-s")) }
                .buttonStyle(DSIconItemStyle())
                .disabled(!score.selectionSteps.canUndo)
                .accessibilityLabel(text["selection.undo"])
            Button { score.redoSelection() } label: { Icon(name: "undo", size: CGFloat.ds("icon-s")).scaleEffect(x: -1, y: 1) }
                .buttonStyle(DSIconItemStyle())
                .disabled(!score.selectionSteps.canRedo)
                .accessibilityLabel(text["selection.redo"])
            PaletteDivider()
            ForEach([false, true], id: \.self) { beats in
                let on = score.selectsByBeats == beats
                Button(text[beats ? "selection.byBeats" : "selection.byBars"]) { score.chooseSelectBy(beats: beats) }
                    .buttonStyle(DSItemStyle(on: on))
                    .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .modifier(PaletteSurface())
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["selection.options"])
    }
}

/// After a clear or a replace, «Выделение снято · Вернуть» for six seconds (#511 11): one tap brings the selection back,
/// with any pointer; no Redo here. While a pointer that cannot scroll moves off the selection before the hold, the hint
/// to hold. Nothing when there is nothing to take back. It holds ⌘Z and ⇧⌘Z too, which are always there.
struct SelectionToastView: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        ZStack {
            UndoKeys(score: score)
            if score.selectionToast != nil && score.selectionSteps.canUndo {
                HStack(spacing: CGFloat.ds("s-3")) {
                    Text(text["selection.clearedToast"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                    Button(text["selection.bringBack"]) { score.undoSelection() }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
                .modifier(ToastSurface())
            } else if score.holdHint != nil {
                Text(text["selection.holdHint"]).dsType(.label).foregroundStyle(Color.ds("ink")).modifier(ToastSurface())
            }
        }
        .animation(.easeOut(duration: 0.16), value: score.selectionToast)
        .animation(.easeOut(duration: 0.16), value: score.holdHint)
    }
}

/// ⌘Z and ⇧⌘Z on a keyboard: Undo and Redo of the tool in force (#521 9, ScoreController.undoOwner), nothing in Navigate;
/// not while a merge has its own.
struct UndoKeys: View {
    @ObservedObject var score: ScoreController

    var body: some View {
        if score.merge == nil {
            Group {
                Button("") { score.undoKey() }.keyboardShortcut("z", modifiers: .command)
                Button("") { score.redoKey() }.keyboardShortcut("z", modifiers: [.command, .shift])
            }
            .frame(width: 0, height: 0)
            .opacity(0)
            .accessibilityHidden(true)
        }
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
