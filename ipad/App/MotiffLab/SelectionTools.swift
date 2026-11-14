import SwiftUI

/// What the Pencil's ring offers with the modes (#511 4.2, option B of the evaluation): the modes on its innermost circle,
/// and around them the chosen one's options, as the row under the bar has them.
struct RingModes {
    var chosen: InteractionMode
    var choose: (InteractionMode) -> Void
    var canUndo: Bool
    var canRedo: Bool
    var undo: () -> Void
    var redo: () -> Void
    var byBeats: Bool
    var setByBeats: (Bool) -> Void
}

/// The stylus menu's rules (#511 4.2), apart from the views so that they are tested.
enum StylusRules {
    /// What the Pencil's «switch to previous» does on the score: with the modes the Pen and Select take turns (the Pen
    /// on, or off into Select); without them the Pen comes on, and while it is on the tool before comes back.
    static func previous(penOn: Bool, mode: InteractionMode?) -> (penOn: Bool, mode: InteractionMode?, previousTool: Bool) {
        guard mode != nil else { return (true, nil, penOn) }
        return penOn ? (false, .select, false) : (true, mode, false)
    }

    /// Whose Undo ⌘Z and ⇧⌘Z are: the ink's while the Pen is on, else the selection's.
    enum UndoOwner: Equatable { case ink, selection }

    static func undoOwner(penOn: Bool) -> UndoOwner {
        penOn ? .ink : .selection
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

    /// The ring's modes and options, while the modes are on.
    var ringModes: RingModes? {
        guard let mode else { return nil }
        return RingModes(
            chosen: penOn ? .pen : mode,
            choose: { [weak self] chosen in self?.chooseMode(chosen) },
            canUndo: selectionSteps.canUndo,
            canRedo: selectionSteps.canRedo,
            undo: { [weak self] in self?.undoSelection() },
            redo: { [weak self] in self?.redoSelection() },
            byBeats: selectsByBeats,
            setByBeats: { [weak self] beats in self?.chooseSelectBy(beats: beats) }
        )
    }

    /// ⌘Z: the ink's Undo while the Pen is on, else the selection's.
    func undoKey() {
        switch StylusRules.undoOwner(penOn: penOn) {
        case .ink: undo()
        case .selection: undoSelection()
        }
    }

    /// ⇧⌘Z: the selection's Redo; the ink has none.
    func redoKey() {
        if StylusRules.undoOwner(penOn: penOn) == .selection { redoSelection() }
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

/// ⌘Z and ⇧⌘Z on a keyboard: the ink's Undo while the Pen is on, else the selection's (#511 11); not while a merge has its own.
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
