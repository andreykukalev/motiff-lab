import SwiftUI

/// The pen's three thicknesses in staff spaces, thin to thick, as the web app's (Scripts/score/pencil-marks.ts
/// PEN_SIZES and HIGHLIGHTER_SIZES; tests/web/pencil-marks.test.mjs checks the two agree). The middle one is the
/// default; the highlighter is a wide translucent band.
enum PenSizes {
    static let pen: [Double] = [0.1, 0.16, 0.3]
    static let highlighter: [Double] = [1.4, 2.2, 3.2]
    static let defaultWidth = 1

    /// How much of the highlighter's amber a highlighted place shows, however many strokes cover it: the alpha of the
    /// `amber-tint` token. Highlighter strokes are drawn opaque as one layer and the layer is laid on the page once at this
    /// alpha (#479 item 9: overlapping strokes showed darker blotches), live (InkInput) and on the page after.
    static var highlighterAlpha: CGFloat { DesignTokens.color("amber-tint").cgColor.alpha }

    /// Each thickness as a share of the tool's thickest (#533 9), so the palette's samples keep the true proportions of what
    /// the tool draws, the thickest at the sample's full height.
    static func shares(_ sizes: [Double]) -> [Double] {
        guard let thickest = sizes.max(), thickest > 0 else { return sizes.map { _ in 0 } }
        return sizes.map { $0 / thickest }
    }
}

/// What the pen palettes change, on the score and on a PDF's pages: the pen's state (one tool and each tool's settings,
/// PenState, #495 UX-06), the Pen itself, and the pencil palette the Pencil's double tap or squeeze opens (#479 item 8),
/// with Undo and Redo (#533 9).
@MainActor
protocol PenHolder: ObservableObject {
    var pen: PenState { get set }
    var penOn: Bool { get set }
    var pencilPalette: PencilPaletteCall? { get set }
    var canUndo: Bool { get }
    var canRedo: Bool { get }
    func undo()
    func redo()
}

extension PenHolder {
    /// What a stroke written now is, read from the one active tool and its settings.
    var tool: ScoreController.Tool { pen.stroke }
}

/// The tools (ds.css `.seg`, #533 9): the pencil, the marker and the eraser, each its own kit icon at a fingertip's size,
/// exactly one on the accent's strong tint; the word for VoiceOver and the pointer's hint. Choosing one keeps every tool's
/// settings.
struct PenToolChoice<Holder: PenHolder>: View {
    @ObservedObject var holder: Holder
    let text: UIText

    var body: some View {
        HStack(spacing: 0) {
            ForEach(ActiveTool.allCases, id: \.self) { tool in
                let on = holder.pen.active == tool
                Button {
                    holder.pen.choose(tool)
                } label: {
                    Icon(name: tool.icon, size: CGFloat.ds("icon-m"))
                        .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                        .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                        .background(on ? Color.ds("accent-tint-strong") : .clear, in: Capsule())
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(text[tool.labelKey])
                .accessibilityAddTraits(on ? .isSelected : [])
                .help(text[tool.labelKey])
            }
        }
        .padding(2)
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["pencil.tool"])
    }
}

/// Undo and Redo of the pen's own history, grey in place when there is nothing to take back or do again (#533 9).
struct PenHistory<Holder: PenHolder>: View {
    @ObservedObject var holder: Holder
    let text: UIText

    var body: some View {
        Button { holder.undo() } label: { Icon(name: "undo", size: CGFloat.ds("icon-m")) }
            .buttonStyle(DSIconItemStyle())
            .disabled(!holder.canUndo)
            .accessibilityLabel(text["pencil.undo"])
            .help(text["pencil.undo"])
        Button { holder.redo() } label: { Icon(name: "undo", size: CGFloat.ds("icon-m")).scaleEffect(x: -1, y: 1) }
            .buttonStyle(DSIconItemStyle())
            .disabled(!holder.canRedo)
            .accessibilityLabel(text["pencil.redo"])
            .help(text["pencil.redo"])
    }
}

/// The chosen tool's settings (#533 9), grouped on the palette's second row: its colour (the pencil's four inks, the chosen
/// one ringed; the marker's one amber) and its thickness (lines as thick as it draws, in proportion, in its colour); for the
/// eraser, one line on what it does. The pencil's row, unseen, keeps the room of the widest, so the palette never changes
/// size under the finger. A setting changes only its tool and never switches tools.
struct PenToolSettings<Holder: PenHolder>: View {
    @ObservedObject var holder: Holder
    let text: UIText

    var body: some View {
        let pen = holder.pen
        ZStack(alignment: .leading) {
            row(PenState()).hidden().accessibilityHidden(true)
            if pen.active == .eraser {
                Text(text["pencil.eraserHint"])
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .padding(.leading, CGFloat.ds("s-2"))
                    .frame(minHeight: CGFloat.ds("h-touch"))
            } else {
                row(pen)
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("pencil.settings", text[pen.active.labelKey]))
    }

    private func row(_ pen: PenState) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            HStack(spacing: 0) {
                if pen.active == .marker {
                    InkSwatch(colour: Color.ds("amber"), chosen: true)
                        .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                        .accessibilityElement()
                        .accessibilityLabel(text["pencil.markerColour"])
                } else {
                    ForEach(ScoreController.Ink.allCases, id: \.self) { ink in
                        let chosen = pen.isChosen(ink: ink)
                        Button {
                            holder.pen.setInk(ink)
                        } label: {
                            InkSwatch(ink: ink, chosen: chosen)
                                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                                .contentShape(Circle())
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel(text[ink.labelKey])
                        .accessibilityAddTraits(chosen ? .isSelected : [])
                    }
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text["pencil.ink"])
            PaletteDivider()
            HStack(spacing: 0) {
                ForEach(PenSizes.pen.indices, id: \.self) { width in
                    PenSettingButton(chosen: pen.isChosen(width: width), label: text[LineSample.labelKeys[width]]) {
                        holder.pen.setWidth(width)
                    } content: {
                        LineSample(width: width, pen: pen)
                    }
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text["pencil.width"])
        }
    }
}

/// One setting of a tool at a fingertip's size: a ring in the accent while it is the tool's.
struct PenSettingButton<Content: View>: View {
    private let chosen: Bool
    private let label: String
    private let action: () -> Void
    private let content: () -> Content

    init(chosen: Bool, label: String, action: @escaping () -> Void, @ViewBuilder content: @escaping () -> Content) {
        self.chosen = chosen
        self.label = label
        self.action = action
        self.content = content
    }

    var body: some View {
        Button(action: action) {
            content()
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                .background(chosen ? Color.ds("accent-tint") : .clear, in: Circle())
                .overlay(Circle().strokeBorder(chosen ? Color.ds("accent") : .clear, lineWidth: 2))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
        .accessibilityAddTraits(chosen ? .isSelected : [])
        .help(label)
    }
}

/// A colour of a tool as a disc of it (#533 9): the chosen one ringed in the accent, a little apart from it, the others with a
/// hairline.
struct InkSwatch: View {
    let colour: Color
    let chosen: Bool

    init(colour: Color, chosen: Bool = false) {
        self.colour = colour
        self.chosen = chosen
    }

    init(ink: ScoreController.Ink, chosen: Bool = false) {
        self.init(colour: Color(uiColor: ink.color), chosen: chosen)
    }

    var body: some View {
        let side = CGFloat.ds("icon-l")
        Circle()
            .fill(colour)
            .overlay(Circle().strokeBorder(Color.ds("hairline-strong")))
            .frame(width: side, height: side)
            .padding(chosen ? 4 : 0)
            .overlay(Circle().strokeBorder(chosen ? Color.ds("accent") : .clear, lineWidth: 2))
    }
}

/// A thickness as a line as thick as the tool draws it (#533 9), in proportion to its thickest (PenSizes.shares): the
/// pencil's lines in its ink, a few points at most, the marker's wider amber bands.
struct LineSample: View {
    let width: Int
    let pen: PenState
    @Environment(\.colorScheme) private var scheme

    static let labelKeys = ["pencil.widthThin", "pencil.widthMedium", "pencil.widthThick"]

    var body: some View {
        let marker = pen.active == .marker
        let shares = PenSizes.shares(marker ? PenSizes.highlighter : PenSizes.pen)
        let thickest = CGFloat.ds(marker ? "s-4" : "s-1")
        Capsule()
            .fill(marker ? Color.ds("amber") : Color(uiColor: pen.ink.color))
            // The inks keep their colour in the dark (pigment on paper): a hairline keeps the graphite in sight.
            .overlay(Capsule().stroke(scheme == .dark ? Color.ds("hairline-strong") : .clear, lineWidth: 1))
            .frame(width: CGFloat.ds("icon-l"), height: max(1, thickest * CGFloat(shares[width])))
    }
}

/// The surface of the pen palette's rows (ds.css `.pen-palette`): a capsule on the raised surface with a hairline.
struct PaletteSurface: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(.horizontal, CGFloat.ds("s-2"))
            .padding(.vertical, CGFloat.ds("s-1"))
            .background(Color.ds("surface-2"), in: Capsule())
            .overlay(Capsule().strokeBorder(Color.ds("hairline")))
            .dsShadow("shadow-menu")
    }
}

/// The cross at the end of the pen palette: it puts the tools away, as the bar's Pen does.
struct PenClose<Holder: PenHolder>: View {
    @ObservedObject var holder: Holder
    let text: UIText

    var body: some View {
        Button {
            holder.penOn = false
        } label: {
            Icon(name: "close", size: CGFloat.ds("icon-m"))
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(Color.ds("ink-2"))
        .accessibilityLabel(text["pencil.close"])
        .help(text["pencil.close"])
    }
}

/// The thin rule between the palette's groups.
struct PaletteDivider: View {
    var body: some View {
        Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
    }
}
