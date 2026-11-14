import Foundation

/// Who does what on the score and on a PDF's pages (#479 item 18, the approved mockup v0.5.2, product spec
/// interaction-modes, «Стилус следует инструменту»). A finger moves around the music: it scrolls, pinches and taps. The
/// stylus does what the chosen tool does: in Navigate it moves the music as a finger does, in Select it selects by
/// beats, with the Pen it writes. A finger never writes while the iPad is used with a stylus. Without one (the iPad's
/// setting «Only draw with Apple Pencil» off) one finger takes the tool and two fingers scroll, so every tool works with
/// a finger alone. The selection's handles and the playheads' handles move with a finger in any tool and with the stylus
/// wherever it does not write: with the Pen the stylus writes everywhere, at the handles too (#511 4.1), so a stroke never
/// moves a selection's edge. The same rules hold in Notes and on a PDF, where nothing can be selected.
struct InputRules: Equatable, Sendable {
    /// The tool chosen in the bar: none before the modes (the experiment off) with the Pen off.
    enum Tool: Equatable, Sendable {
        case none, navigate, select, edit, pen

        /// The bar's choice: the Pen while it is on, else the mode, or none before the modes.
        init(mode: InteractionMode?, penOn: Bool) {
            if penOn {
                self = .pen
                return
            }
            switch mode {
            case .navigate: self = .navigate
            case .select: self = .select
            case .edit: self = .edit
            case .pen: self = .pen
            case nil: self = .none
            }
        }
    }

    enum Pointer: String, CaseIterable, Sendable {
        case finger, stylus

        /// The pointer type the page's pointer events name it by.
        var pageName: String { self == .finger ? "touch" : "pen" }
    }

    enum Surface: Sendable {
        case notes, pdf
    }

    /// How a press starts a selection on the score: at once, after a long press, or not at all.
    enum SelectStart: String, Sendable {
        case now, longPress, none
    }

    /// Whether the iPad is used with a stylus: the iPad's own setting «Only draw with Apple Pencil»
    /// (`UIPencilInteraction.prefersPencilOnlyDrawing`), as the mockup decided.
    var stylus: Bool
    var tool: Tool
    var surface: Surface = .notes

    /// The tool as this surface has it: a PDF has no bars to select or notes to edit, so there they move the pages.
    var effectiveTool: Tool {
        surface == .pdf && (tool == .select || tool == .edit) ? .navigate : tool
    }

    /// Whether a pointer is a finger in the rules: without a stylus the Pencil, if there is one, goes as a finger does.
    private func actsAsFinger(_ pointer: Pointer) -> Bool {
        pointer == .finger || !stylus
    }

    /// Whether the pointer writes with the pen.
    func writes(_ pointer: Pointer) -> Bool {
        guard effectiveTool == .pen else { return false }
        return stylus ? pointer == .stylus : true
    }

    /// Whether the pointer scrolls the music (with as many fingers as `scrollFingers` asks).
    func scrolls(_ pointer: Pointer) -> Bool {
        if actsAsFinger(pointer) { return true }
        return effectiveTool == .none || effectiveTool == .navigate
    }

    /// How many fingers scroll: one, unless one finger takes the tool (no stylus, any tool but Navigate), then two.
    var scrollFingers: Int {
        if stylus { return 1 }
        return effectiveTool == .none || effectiveTool == .navigate ? 1 : 2
    }

    /// How a press of the pointer starts a selection: the stylus in Select and one finger in Select without a stylus at
    /// once; otherwise a long press and a drag, in any tool, except where the pointer writes. Never on a PDF.
    func selectStart(_ pointer: Pointer) -> SelectStart {
        guard surface == .notes, !writes(pointer) else { return .none }
        guard effectiveTool == .select else { return .longPress }
        return actsAsFinger(pointer) && stylus ? .longPress : .now
    }

    /// Whether a tap of the pointer acts on the page (places the playhead, opens a note, selects a bar before the modes):
    /// everywhere but where it writes.
    func taps(_ pointer: Pointer) -> Bool {
        !writes(pointer)
    }

    /// Whether a press of the pointer on a selection handle or a playhead's handle takes the handle (#511 4.1): a finger
    /// always does; the stylus only where it does not write, so with the Pen it writes over the handles as everywhere.
    func takesHandles(_ pointer: Pointer) -> Bool {
        pointer == .finger || !writes(pointer)
    }

    /// Whether the pen's gesture takes a touch of the pointer at a point `nearHandle` (a selection's or a playhead's
    /// handle, within `handleReach`) or not: only a pointer that writes reaches the gesture, and it leaves the handles to
    /// the pointers that take them.
    func inkTakes(_ pointer: Pointer, nearHandle: Bool) -> Bool {
        writes(pointer) && !(nearHandle && takesHandles(pointer))
    }

    /// What the score page is told (`motiff.input`): for "touch" and "pen", how a press selects, whether a tap acts and
    /// whether a press on a handle takes it.
    var page: [String: Any] {
        var rules: [String: Any] = [:]
        for pointer in Pointer.allCases {
            rules[pointer.pageName] = ["select": selectStart(pointer).rawValue, "tap": taps(pointer), "handles": takesHandles(pointer)] as [String: Any]
        }
        return rules
    }

    /// How far from a selection handle's centre a touch takes the handle rather than the pen: the 44 pt target.
    static let handleReach = 22.0
}

/// The piano roll's own two modes (#479 item 23, the approved mockup v0.5.2): Navigate, where the stylus and a finger move
/// the roll every way and a press on the notes neither selects nor edits, and Edit notes, where the roll's note editor is
/// open. Note editing lives only here, not in the bar's modes. The selection's handles and the loop strip work in both,
/// as on the score.
enum RollMode: String, CaseIterable, Sendable {
    case navigate, edit

    /// The kit's icon: the page with arrows, as the bar's Navigate, and the note with a pen.
    var icon: String {
        switch self {
        case .navigate: "scroll"
        case .edit: "editnote"
        }
    }

    var label: String { "rollMode.\(rawValue)" }
    var hint: String { "rollMode.\(rawValue)Hint" }

    /// The mode the roll is in: Edit notes while the editor is open and chosen, else Navigate. Expanded (`bound` to the
    /// passage of Expand) always Navigate: Expand inspects a passage and edits no notes (#521 19.3); a draft waits for the
    /// drawer, where Edit notes comes back as it was.
    static func shown(chosen: RollMode, editorOpen: Bool, bound: Bool = false) -> RollMode {
        chosen == .edit && editorOpen && !bound ? .edit : .navigate
    }

    /// What choosing a mode does to the note editor: Edit notes opens it when it is closed; Navigate closes it when nothing
    /// was changed, and keeps a draft with changes open (its Cancel and Save stay) so that no edit is lost by a switch.
    static func choose(_ mode: RollMode, editorOpen: Bool, changed: Bool) -> (openEditor: Bool, closeEditor: Bool) {
        switch mode {
        case .edit: (!editorOpen, false)
        case .navigate: (false, editorOpen && !changed)
        }
    }
}

/// Who edits and who moves the roll (#479 item 23), by InputRules' reading of the iPad: with a stylus, the stylus edits in
/// Edit notes and a finger always moves the roll; without one, one finger edits and two move it, so every edit works with
/// a finger alone. In Navigate every pointer moves the roll, with one finger. The bar's tool decides on the roll too
/// (#543 2–4): under the modes only Select changes the selection there; the roll's own Navigate and Edit notes are about
/// editing notes.
struct RollInput: Equatable, Sendable {
    var stylus: Bool
    var mode: RollMode
    /// Whether the bar's Pen is on (#511 4.1, #521 10): then the stylus is the Pen's, which draws nothing on the roll, so
    /// there it neither selects (bar numbers, loop strip, handles), nor moves the roll, nor edits; a finger still does all.
    var penOn = false
    /// The bar's tool under the modes (RollInput.barTool); none before them, when every tool selects on the roll as before.
    var tool: InputRules.Tool = .none

    /// The bar's tool as the roll reads it: the Pen while it is on, else the mode; none while the experiment is off, even
    /// with the Pen on (the Pen before the modes changes nothing else on the roll).
    static func barTool(mode: InteractionMode?, penOn: Bool) -> InputRules.Tool {
        mode == nil ? .none : InputRules.Tool(mode: mode, penOn: penOn)
    }

    /// Whether a press of the pointer on the notes edits them; otherwise it moves the roll.
    func edits(_ pointer: InputRules.Pointer) -> Bool {
        guard mode == .edit, acts(pointer) else { return false }
        return stylus ? pointer == .stylus : true
    }

    /// How many fingers move the roll: two while one finger edits.
    var scrollFingers: Int {
        !stylus && mode == .edit ? 2 : 1
    }

    /// Whether a press of the pointer acts on the roll at all: not the stylus while the Pen is on (#521 10).
    func acts(_ pointer: InputRules.Pointer) -> Bool {
        pointer == .finger || !penOn
    }

    /// Whether a press of the pointer on the bar numbers, the loop strip or a handle changes the selection: under the modes
    /// only in Select (#543 2, lead decision 3), before them in every tool; never the Pen's stylus.
    func selects(_ pointer: InputRules.Pointer) -> Bool {
        acts(pointer) && (tool == .none || tool == .select)
    }

    /// The bar's tool as the roll's page names it (roll-modes.ts, modeOf): empty before the modes. The iPad's bar has no
    /// Edit notes (notes are edited in the roll's own mode), so that one, never chosen here, goes as Navigate.
    var pageMode: String {
        switch tool {
        case .none: ""
        case .navigate, .edit: "navigate"
        case .select: "select"
        case .pen: "pen"
        }
    }

    /// What the roll is told (`roll.input`): `penSelects` false makes the stylus do nothing there (roll-modes.ts selectsOnRoll);
    /// `mode` is the bar's tool, which decides what a press on the roll's strips and handles does (selectsOnRoll, navigatesOnRoll).
    var page: [String: Any] {
        ["touch": edits(.finger), "pen": edits(.stylus), "scrollFingers": scrollFingers, "penSelects": acts(.stylus), "mode": pageMode]
    }

    /// One tool at a time (#521 10): Edit notes on the roll and the bar's Pen are both tools that write, so choosing one puts
    /// the other away: Edit notes turns the Pen off, the Pen takes the roll back to Navigate (a draft with changes waits
    /// there for Cancel or Save, RollMode.choose).
    static func penOff(choosing mode: RollMode) -> Bool {
        mode == .edit
    }

    static func rollNavigates(penOn: Bool, mode: RollMode) -> Bool {
        penOn && mode == .edit
    }
}
