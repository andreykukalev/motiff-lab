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
}

/// What the pen palettes change, on the score and on a PDF's pages: the pen's state (one tool and each tool's settings,
/// PenState, #495 UX-06), the Pen itself, and the pencil palette the Pencil's double tap or squeeze opens (#479 item 8),
/// with Undo.
@MainActor
protocol PenHolder: ObservableObject {
    var pen: PenState { get set }
    var penOn: Bool { get set }
    var pencilPalette: PencilPaletteCall? { get set }
    var canUndo: Bool { get }
    func undo()
}

extension PenHolder {
    /// What a stroke written now is, read from the one active tool and its settings.
    var tool: ScoreController.Tool { pen.stroke }
}

/// The tools (ds.css `.seg`, at a fingertip's height, #495 UX-06): the pencil, the marker and the eraser, exactly one on
/// the accent's strong tint. Choosing one keeps every tool's settings; each option keeps its width either way, so the
/// tools do not move under the finger.
struct PenToolChoice<Holder: PenHolder>: View {
    @ObservedObject var holder: Holder
    let text: UIText
    /// Icons without their words, where the palette would not fit across the screen (an iPad in portrait).
    var compact = false

    var body: some View {
        HStack(spacing: 0) {
            ForEach(ActiveTool.allCases, id: \.self) { tool in
                let on = holder.pen.active == tool
                Button {
                    holder.pen.choose(tool)
                } label: {
                    ZStack {
                        // The option in the chosen weight, unseen, holds its width.
                        label(tool, weight: 600).hidden().accessibilityHidden(true)
                        label(tool, weight: on ? 600 : 500)
                    }
                    .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                    .padding(.horizontal, CGFloat.ds("s-3"))
                    .frame(minWidth: CGFloat.ds("h-touch"), minHeight: CGFloat.ds("h-touch"))
                    .background(on ? Color.ds("accent-tint-strong") : .clear, in: Capsule())
                    .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(text[tool.labelKey])
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .padding(2)
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["pencil.tool"])
    }

    /// The tool's kit icon and its word; the word alone for the eraser, which has no icon in the kit.
    private func label(_ tool: ActiveTool, weight: Int) -> some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            if let icon = tool.icon { Icon(name: icon, size: CGFloat.ds("icon-s")) }
            if !compact || tool.icon == nil {
                Text(text[tool.labelKey]).lineLimit(1)
            }
        }
        .font(DesignTokens.font(weight: weight, size: 13, relativeTo: .callout))
    }
}

/// The active tool's settings (#495 UX-06), in a capsule of their own under the tools: the pencil's four inks and its
/// three thicknesses, the marker's three thicknesses, each led by its tool's icon. The eraser has none, so the row goes.
/// A setting chosen changes only the tool it belongs to and never switches tools.
struct PenToolSettings<Holder: PenHolder>: View {
    @ObservedObject var holder: Holder
    let text: UIText

    var body: some View {
        let pen = holder.pen
        if pen.current != nil {
            HStack(spacing: CGFloat.ds("s-1")) {
                if let icon = pen.active.icon {
                    Icon(name: icon, size: CGFloat.ds("icon-s"))
                        .foregroundStyle(Color.ds("ink-3"))
                        .padding(.horizontal, CGFloat.ds("s-2"))
                }
                if pen.active == .pencil {
                    HStack(spacing: 0) {
                        ForEach(ScoreController.Ink.allCases, id: \.self) { ink in
                            PenSettingButton(chosen: pen.isChosen(ink: ink), label: text[ink.labelKey]) {
                                holder.pen.setInk(ink)
                            } content: {
                                InkSwatch(ink: ink)
                            }
                        }
                    }
                    .accessibilityElement(children: .contain)
                    .accessibilityLabel(text["pencil.ink"])
                    PaletteDivider()
                }
                HStack(spacing: 0) {
                    ForEach(PenSizes.pen.indices, id: \.self) { width in
                        PenSettingButton(chosen: pen.isChosen(width: width), label: text[WidthDot.labelKeys[width]]) {
                            holder.pen.setWidth(width)
                        } content: {
                            WidthDot(width: width, pen: pen)
                        }
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["pencil.width"])
            }
            .modifier(PaletteSurface())
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text.format("pencil.settings", text[pen.active.labelKey]))
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
    }
}

/// An ink of the pencil, as a disc of its colour.
struct InkSwatch: View {
    let ink: ScoreController.Ink

    var body: some View {
        Circle()
            .fill(Color(uiColor: ink.color))
            .overlay(Circle().strokeBorder(Color.ds("hairline-strong")))
            .frame(width: CGFloat.ds("icon-m"), height: CGFloat.ds("icon-m"))
    }
}

/// A thickness as a dot as thick as the line it draws, in the colour the tool draws in: the pencil's ink, the marker's
/// amber (a little larger, as its band is wide).
struct WidthDot: View {
    let width: Int
    let pen: PenState

    static let labelKeys = ["pencil.widthThin", "pencil.widthMedium", "pencil.widthThick"]
    private static let pencilDots: [CGFloat] = [4, 8, 13]
    private static let markerDots: [CGFloat] = [8, 12, 16]

    var body: some View {
        let marker = pen.active == .marker
        let side = (marker ? Self.markerDots : Self.pencilDots)[width]
        Circle()
            .fill(marker ? Color.ds("amber") : Color(uiColor: pen.ink.color))
            .frame(width: side, height: side)
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
            Icon(name: "close", size: CGFloat.ds("icon-s"))
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(Color.ds("ink-2"))
        .accessibilityLabel(text["pencil.close"])
    }
}

/// The thin rule between the palette's groups.
struct PaletteDivider: View {
    var body: some View {
        Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
    }
}
