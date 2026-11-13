import SwiftUI

/// The piano roll as a place to edit notes (#479 item 23, the approved mockup v0.5.2): its own mode switch, the zoom on each
/// axis, and the selection made on it. The roll is the web app's (RollController); what is drawn over it lives here.

/// The roll's zoom as it reports it (`roll-zoom.ts`): the bars and the octaves in view, and which steps are left.
struct RollZoom: Equatable, Sendable {
    var bars: Int
    var octaves: Int
    var timeIn = true
    var timeOut = true
    var pitchIn = true
    var pitchOut = true

    init(bars: Int, octaves: Int, timeIn: Bool = true, timeOut: Bool = true, pitchIn: Bool = true, pitchOut: Bool = true) {
        self.bars = bars
        self.octaves = octaves
        self.timeIn = timeIn
        self.timeOut = timeOut
        self.pitchIn = pitchIn
        self.pitchOut = pitchOut
    }

    /// From the roll's message: `{ bars, octaves, timeIn, timeOut, pitchIn, pitchOut }`.
    init?(_ message: [String: Any]) {
        guard let bars = (message["bars"] as? NSNumber)?.intValue, let octaves = (message["octaves"] as? NSNumber)?.intValue else { return nil }
        self.bars = bars
        self.octaves = octaves
        timeIn = message["timeIn"] as? Bool ?? true
        timeOut = message["timeOut"] as? Bool ?? true
        pitchIn = message["pitchIn"] as? Bool ?? true
        pitchOut = message["pitchOut"] as? Bool ?? true
    }

    /// The time zoom's value in words, as Andrei chose over a percentage: "9 bars", «9 тактов».
    static func barsText(_ bars: Int, _ text: UIText) -> String {
        text.count("rollZoom.bars", bars)
    }

    /// The pitch zoom's value in words: "3 octaves", «3 октавы».
    static func octavesText(_ octaves: Int, _ text: UIText) -> String {
        text.count("rollZoom.octaves", octaves)
    }

    /// The word under the number on the pitch zoom, which stands in a column: "octaves", «октавы».
    static func octaveWord(_ octaves: Int, _ text: UIText) -> String {
        text.count("rollZoom.octaveWord", octaves)
    }
}

/// Where the roll is on screen (#495 BUG-01): in its drawer, or over the card in focus (Expand). The roll has one web view,
/// so one holder has it: of the place shown, the newest, never one that is going or of another place.
enum RollPlace: Equatable, Sendable {
    case drawer, focus

    /// The place shown: the drawer while it is open, else the card in focus showing the phrase as a roll; nil for neither.
    static func shown(drawerOpen: Bool, focused: Bool, focusRoll: Bool) -> RollPlace? {
        if drawerOpen { return .drawer }
        return focused && focusRoll ? .focus : nil
    }

    /// Of the holders there are (each one's place and the order it was made in), the one that has the web view: the newest
    /// of the place shown; nil while no place is shown, or none of its holders is made yet.
    static func holder(for place: RollPlace?, among holders: [(place: RollPlace, made: Int)]) -> Int? {
        guard let place else { return nil }
        return holders.filter { $0.place == place }.map { $0.made }.max()
    }
}

/// What the player chose on the roll's bar numbers or the selection's handles: a bar tapped, which selects that bar; or a
/// stretch dragged across, which selects by beats where the score does (#349, the modes) and by bars otherwise. It is the
/// one selection of the score, the pill and the loop strip, so whatever is chosen here shows on the score at once.
enum RollPick: Equatable, Sendable {
    case bars(first: Int, last: Int, extend: Bool)
    case beats(Range<Double>)

    /// The roll's `range` message: `{ first, last, extend, click, from, to }`, the ticks from where the drag began to where it
    /// ended, either way round.
    init?(_ message: [String: Any], byBeats: Bool) {
        func number(_ key: String) -> Double? { (message[key] as? NSNumber)?.doubleValue }
        guard let first = number("first"), let last = number("last") else { return nil }
        let click = message["click"] as? Bool ?? false
        if byBeats, !click, let from = number("from"), let to = number("to") {
            let start = min(from, to)
            self = .beats(start..<max(max(from, to), start + 1))
        } else {
            self = .bars(first: Int(first), last: Int(last), extend: message["extend"] as? Bool ?? false)
        }
    }
}

/// «Навигация | Правка нот» in the roll's head, as the bar's modes are drawn: the chosen mode with its icon and its name,
/// the other with its icon alone (Andrei, 2026-11-13 14:33).
struct RollModeSwitch: View {
    let chosen: RollMode
    let text: UIText
    let choose: (RollMode) -> Void

    var body: some View {
        HStack(spacing: 2) {
            ForEach(RollMode.allCases, id: \.self) { mode in
                button(mode)
            }
        }
        .padding(2)
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["rollMode.label"])
    }

    private func button(_ mode: RollMode) -> some View {
        let on = chosen == mode
        return Button {
            choose(mode)
        } label: {
            HStack(spacing: 6) {
                Icon(name: mode.icon, size: CGFloat.ds("icon-m"))
                if on {
                    Text(text[mode.label]).dsType(.label).lineLimit(1).fixedSize()
                }
            }
            .padding(.horizontal, on ? CGFloat.ds("s-3") : CGFloat.ds("s-2"))
            .frame(minWidth: CGFloat.ds("h-control"), minHeight: CGFloat.ds("h-control"))
        }
        .buttonStyle(BarActionStyle(on: on))
        .accessibilityLabel(text[mode.label])
        .accessibilityHint(text[mode.hint])
        .accessibilityAddTraits(on ? .isSelected : [])
        .help(text[mode.hint])
    }
}

/// The zoom on each axis over the roll (#479 item 23): along the bottom on the right «− 9 тактов +» and Fit, and in a
/// column over the keyboard on the left «+ 3 октавы −». Every button takes a finger or the Pencil (44 pt).
struct RollZoomControls: View {
    @ObservedObject var roll: RollController
    let text: UIText

    var body: some View {
        if let zoom = roll.zoom {
            HStack(alignment: .bottom) {
                pitch(zoom)
                Spacer(minLength: CGFloat.ds("s-2"))
                time(zoom)
            }
            .padding(.horizontal, CGFloat.ds("s-1"))
            .padding(.bottom, CGFloat.ds("s-2"))
        }
    }

    private func time(_ zoom: RollZoom) -> some View {
        HStack(spacing: 0) {
            step("−", text["rollZoom.timeOut"], enabled: zoom.timeOut) { roll.zoomStep(.time, -1) }
            Text(RollZoom.barsText(zoom.bars, text))
                .dsType(.label)
                .foregroundStyle(Color.ds("ink"))
                .monospacedDigit()
                .lineLimit(1)
                .fixedSize()
                .padding(.horizontal, CGFloat.ds("s-1"))
            step("+", text["rollZoom.timeIn"], enabled: zoom.timeIn) { roll.zoomStep(.time, 1) }
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: CGFloat.ds("s-6"))
            Button(text["roll.js.fit"]) { roll.zoomStep(.fit, 0) }
                .buttonStyle(DSItemStyle())
                .frame(minHeight: CGFloat.ds("h-touch"))
                .help(text["roll.js.fitHint"])
        }
        .padding(.horizontal, CGFloat.ds("s-1"))
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.94))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["rollZoom.time"])
        .accessibilityValue(RollZoom.barsText(zoom.bars, text))
    }

    private func pitch(_ zoom: RollZoom) -> some View {
        VStack(spacing: 0) {
            step("+", text["rollZoom.pitchIn"], enabled: zoom.pitchIn) { roll.zoomStep(.pitch, 1) }
            VStack(spacing: 0) {
                Text(String(zoom.octaves)).dsType(.bodyStrong).foregroundStyle(Color.ds("ink")).monospacedDigit()
                Text(RollZoom.octaveWord(zoom.octaves, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2")).lineLimit(1).fixedSize()
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(RollZoom.octavesText(zoom.octaves, text))
            step("−", text["rollZoom.pitchOut"], enabled: zoom.pitchOut) { roll.zoomStep(.pitch, -1) }
        }
        .padding(.vertical, CGFloat.ds("s-1"))
        .frame(minWidth: CGFloat.ds("s-12"))
        .background(Color.ds("surface").opacity(0.94), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-pill")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["rollZoom.pitch"])
    }

    /// − or +: a fingertip's size, for a finger or the Pencil.
    private func step(_ sign: String, _ label: String, enabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(sign)
                .dsType(.heading)
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                .contentShape(Rectangle())
        }
        .buttonStyle(BarActionStyle())
        .disabled(!enabled)
        .accessibilityLabel(label)
    }
}

/// The roll's web view with the zoom over it: the same in the drawer and in the expanded roll.
struct RollSurface: View {
    @ObservedObject var roll: RollController
    let place: RollPlace
    let text: UIText

    var body: some View {
        ScoreRollView(roll: roll, place: place)
            .overlay(alignment: .bottom) {
                RollZoomControls(roll: roll, text: text)
            }
    }
}

/// The note editor's tools in the roll's head (testing item 15, moved here from the bar by #479 item 23): Move, Add and
/// Remove, Undo and Reset, then Cancel and Save.
struct RollEditTools: View {
    @ObservedObject var score: ScoreController
    let editor: NoteEditorState
    let text: UIText
    /// Whether Reset goes into a menu, where the head is narrow (portrait).
    var compact = false

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            SegmentedChoice(options: ["move", "add", "delete"].map { ($0, text["edit.\($0)"]) }, chosen: editor.tool, label: text["edit.tool"]) {
                score.chooseNoteEditTool($0)
            }
            .help(text["edit.\(editor.tool)Hint"])
            Button {
                score.undoNoteEdit()
            } label: {
                Icon(name: "undo", size: CGFloat.ds("icon-s")).frame(width: CGFloat.ds("h-compact"), height: CGFloat.ds("h-compact"))
            }
            .buttonStyle(DSItemStyle())
            .disabled(!editor.canUndo)
            .accessibilityLabel(text["edit.undo"])
            if !compact {
                Button(text["edit.reset"]) {
                    score.resetNotes()
                }
                .buttonStyle(DSItemStyle())
                .disabled(!editor.canReset)
                .help(text["edit.resetHint"])
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["edit.toolbar"])
    }
}

/// Cancel and Save of the note editor: in Edit notes, and in Navigate while a draft with changes waits.
struct RollEditFinish: View {
    @ObservedObject var score: ScoreController
    let editor: NoteEditorState
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button(text["edit.cancel"]) {
                score.cancelNoteEdit()
            }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            Button {
                score.saveNoteEdit()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "check", size: CGFloat.ds("icon-s"))
                    Text(text["edit.save"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            .disabled(!editor.changed)
            .help(text["edit.keptOnSave"])
        }
    }
}

extension RollController {
    /// Chooses a mode on the roll's switch: Edit notes opens the note editor; Navigate closes it unless it holds changes,
    /// which wait for Cancel or Save (RollMode.choose).
    func choose(_ mode: RollMode, score: ScoreController) {
        let editor = score.noteEditor
        let action = RollMode.choose(mode, editorOpen: editor != nil, changed: editor?.changed ?? false)
        if action.openEditor { score.beginNoteEdit() }
        if action.closeEditor { score.cancelNoteEdit() }
        self.mode = mode
    }
}
