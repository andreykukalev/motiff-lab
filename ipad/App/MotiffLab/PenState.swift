import Foundation

/// The pen's one active tool (v0.5.2, #495 UX-06): the pencil, the marker (the highlighter) or the eraser, never two
/// at once. A thickness or an ink is a setting of the tool, never a tool.
enum ActiveTool: String, CaseIterable, Equatable {
    case pencil, marker, eraser

    var labelKey: String {
        switch self {
        case .pencil: "pencil.pen"
        case .marker: "pencil.highlighter"
        case .eraser: "pencil.eraser"
        }
    }

    /// The kit's icon (design/kit/icons.js): three drawings of their own (#533 9), the pencil with its cone and lead, the
    /// marker with its chisel and its band, the eraser as a block; never the bar's Pen or the selection's Highlight.
    var icon: String {
        switch self {
        case .pencil: "pencil"
        case .marker: "marker"
        case .eraser: "eraser"
        }
    }
}

/// What a tool keeps while another one is chosen: its thickness (an index into PenSizes) and, for the pencil, its ink.
/// The marker draws in amber alone, so it has no ink.
struct ToolSettings: Equatable {
    var width: Int
    var ink: ScoreController.Ink?
}

/// The pen's whole state (#495 UX-06): the active tool, the one before it (for the eraser's way back and the Pencil's
/// «switch to previous») and each tool's own settings. Every highlight in the pen palettes is read from here, so what
/// shows as chosen cannot disagree: one tool, and the settings of that tool. Pure: no view, no device.
struct PenState: Equatable {
    private(set) var active: ActiveTool
    private(set) var previous: ActiveTool
    private(set) var pencil: ToolSettings
    private(set) var marker: ToolSettings

    init(
        active: ActiveTool = .pencil, previous: ActiveTool = .eraser,
        pencil: ToolSettings = ToolSettings(width: PenSizes.defaultWidth, ink: .graphite),
        marker: ToolSettings = ToolSettings(width: PenSizes.defaultWidth, ink: nil)
    ) {
        self.active = active
        self.previous = previous
        self.pencil = ToolSettings(width: Self.valid(pencil.width), ink: pencil.ink ?? .graphite)
        self.marker = ToolSettings(width: Self.valid(marker.width), ink: nil)
    }

    /// The settings of a tool; the eraser has none.
    func settings(of tool: ActiveTool) -> ToolSettings? {
        switch tool {
        case .pencil: pencil
        case .marker: marker
        case .eraser: nil
        }
    }

    /// The settings the palette shows now: the active tool's.
    var current: ToolSettings? { settings(of: active) }

    /// The pencil's ink, graphite unless another was chosen.
    var ink: ScoreController.Ink { pencil.ink ?? .graphite }

    /// What a stroke written now is: the pencil in its ink, the highlighter or the eraser.
    var stroke: ScoreController.Tool {
        switch active {
        case .pencil: .pen(ink)
        case .marker: .highlighter
        case .eraser: .eraser
        }
    }

    /// Whether a thickness shows as chosen: only the active tool's, so the eraser shows none.
    func isChosen(width: Int) -> Bool { current?.width == width }

    /// Whether an ink shows as chosen: only while the pencil is the tool.
    func isChosen(ink: ScoreController.Ink) -> Bool { active == .pencil && self.ink == ink }

    /// A tool chosen: it becomes the one tool, and the one it replaces is the way back. Each keeps its settings.
    mutating func choose(_ tool: ActiveTool) {
        guard tool != active else { return }
        previous = active
        active = tool
    }

    /// The Pencil's «switch to eraser»: to the eraser, or from it back to the tool before.
    mutating func toggleEraser() {
        if active == .eraser {
            choose(previous == .eraser ? .pencil : previous)
        } else {
            choose(.eraser)
        }
    }

    /// The Pencil's «switch to previous»: the tool before, and the one now becomes the way back.
    mutating func choosePrevious() {
        choose(previous)
    }

    /// A thickness for the active tool. It never changes the tool; the eraser has none, so there it does nothing.
    mutating func setWidth(_ width: Int) {
        guard PenSizes.pen.indices.contains(width) else { return }
        switch active {
        case .pencil: pencil.width = width
        case .marker: marker.width = width
        case .eraser: break
        }
    }

    /// An ink for the pencil. It never changes the tool: the palettes offer the inks only while the pencil is the tool.
    mutating func setInk(_ ink: ScoreController.Ink) {
        pencil.ink = ink
    }

    private static func valid(_ width: Int) -> Int {
        PenSizes.pen.indices.contains(width) ? width : PenSizes.defaultWidth
    }
}

/// The pen's state kept on the device and shared by the score and a PDF's pages (#479 item 11): PDF | Notes and opening
/// a piece again keep the tool and the settings the player chose instead of going back to graphite. The tool is kept
/// as before UX-06 ("pen.graphite" and the other inks, "highlighter" or "eraser"), so an iPad updated keeps its pen.
enum PenMemory {
    static let toolKey = "motiff.pen.tool"
    static let previousKey = "motiff.pen.previous"
    static let inkKey = "motiff.pen.ink"
    static let penWidthKey = "motiff.pen.width.pen"
    static let highlighterWidthKey = "motiff.pen.width.highlighter"

    /// How a tool is kept: the pencil with its ink, the highlighter or the eraser.
    static func code(_ tool: ActiveTool, ink: ScoreController.Ink) -> String {
        switch tool {
        case .pencil: "pen.\(ink.rawValue)"
        case .marker: "highlighter"
        case .eraser: "eraser"
        }
    }

    /// The tool a kept code names, with the pencil's ink when it names one; nil for anything else.
    static func tool(_ code: String?) -> (tool: ActiveTool, ink: ScoreController.Ink?)? {
        guard let code else { return nil }
        if code == "highlighter" { return (.marker, nil) }
        if code == "eraser" { return (.eraser, nil) }
        guard code.hasPrefix("pen."), let ink = ScoreController.Ink(rawValue: String(code.dropFirst(4))) else { return nil }
        return (.pencil, ink)
    }

    /// The state last kept on this device; the graphite pencil, medium, the first time.
    static func saved(in defaults: UserDefaults = .standard) -> PenState {
        let active = tool(defaults.string(forKey: toolKey))
        let previous = tool(defaults.string(forKey: previousKey))
        let ink = defaults.string(forKey: inkKey).flatMap(ScoreController.Ink.init(rawValue:)) ?? active?.ink ?? previous?.ink
        return PenState(
            active: active?.tool ?? .pencil, previous: previous?.tool ?? .eraser,
            pencil: ToolSettings(width: savedWidth(highlighter: false, in: defaults), ink: ink),
            marker: ToolSettings(width: savedWidth(highlighter: true, in: defaults), ink: nil)
        )
    }

    /// The thickness last kept for the pencil or, with `highlighter`, the marker; the middle one the first time.
    static func savedWidth(highlighter: Bool, in defaults: UserDefaults = .standard) -> Int {
        let key = highlighter ? highlighterWidthKey : penWidthKey
        guard defaults.object(forKey: key) != nil else { return PenSizes.defaultWidth }
        let width = defaults.integer(forKey: key)
        return PenSizes.pen.indices.contains(width) ? width : PenSizes.defaultWidth
    }

    static func keep(_ state: PenState, in defaults: UserDefaults = .standard) {
        defaults.set(code(state.active, ink: state.ink), forKey: toolKey)
        defaults.set(code(state.previous, ink: state.ink), forKey: previousKey)
        defaults.set(state.ink.rawValue, forKey: inkKey)
        defaults.set(state.pencil.width, forKey: penWidthKey)
        defaults.set(state.marker.width, forKey: highlighterWidthKey)
    }
}
