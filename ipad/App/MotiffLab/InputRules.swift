import Foundation

/// Who does what on the score and on a PDF's pages (#479 item 18, the approved mockup v0.5.2, product spec
/// interaction-modes, «Стилус следует инструменту»). A finger moves around the music: it scrolls, pinches and taps. The
/// stylus does what the chosen tool does: in Navigate it moves the music as a finger does, in Select it selects by
/// beats, with the Pen it writes. A finger never writes while the iPad is used with a stylus. Without one (the iPad's
/// setting «Only draw with Apple Pencil» off) one finger takes the tool and two fingers scroll, so every tool works with
/// a finger alone. The selection's handles move with any finger or stylus in any tool, and while one is held nothing
/// writes. The same rules hold in Notes and on a PDF, where nothing can be selected.
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

    /// What the score page is told (`motiff.input`): for "touch" and "pen", how a press selects and whether a tap acts.
    var page: [String: Any] {
        var rules: [String: Any] = [:]
        for pointer in Pointer.allCases {
            rules[pointer.pageName] = ["select": selectStart(pointer).rawValue, "tap": taps(pointer)] as [String: Any]
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

    /// The mode the roll is in: Edit notes while the editor is open and chosen, else Navigate.
    static func shown(chosen: RollMode, editorOpen: Bool) -> RollMode {
        chosen == .edit && editorOpen ? .edit : .navigate
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
/// a finger alone. In Navigate every pointer moves the roll, with one finger.
struct RollInput: Equatable, Sendable {
    var stylus: Bool
    var mode: RollMode

    /// Whether a press of the pointer on the notes edits them; otherwise it moves the roll.
    func edits(_ pointer: InputRules.Pointer) -> Bool {
        guard mode == .edit else { return false }
        return stylus ? pointer == .stylus : true
    }

    /// How many fingers move the roll: two while one finger edits.
    var scrollFingers: Int {
        !stylus && mode == .edit ? 2 : 1
    }

    /// What the roll is told (`roll.input`).
    var page: [String: Any] {
        ["touch": edits(.finger), "pen": edits(.stylus), "scrollFingers": scrollFingers]
    }
}
