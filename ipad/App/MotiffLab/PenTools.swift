import SwiftUI

/// The pen's three thicknesses in staff spaces, thin to thick, as the web app's (Scripts/score/pencil-marks.ts
/// PEN_SIZES and HIGHLIGHTER_SIZES; tests/web/pencil-marks.test.mjs checks the two agree). The middle one is the
/// default; the highlighter is a wide translucent band.
enum PenSizes {
    static let pen: [Double] = [0.1, 0.16, 0.3]
    static let highlighter: [Double] = [1.4, 2.2, 3.2]
    static let defaultWidth = 1
}

/// What the pen palette changes, on the score and on a PDF's pages: the tool, its thickness and the Pen itself.
@MainActor
protocol PenHolder: ObservableObject {
    var tool: ScoreController.Tool { get set }
    var previousTool: ScoreController.Tool { get }
    var penOn: Bool { get set }
    var penWidth: Int { get set }
    var highlighterWidth: Int { get set }
}

extension PenHolder {
    /// The thickness of the tool that draws now; the eraser has none.
    var width: Int? {
        switch tool {
        case .pen: penWidth
        case .highlighter: highlighterWidth
        case .eraser: nil
        }
    }

    /// The size a new mark of the tool now is kept at, in staff spaces.
    var drawingSize: Double {
        tool == .highlighter ? PenSizes.highlighter[highlighterWidth] : PenSizes.pen[penWidth]
    }

    /// A thickness chosen: for the highlighter or the pen, back from the eraser to the tool before it.
    func setWidth(_ width: Int) {
        if tool == .eraser {
            tool = previousTool == .eraser ? .pen(.graphite) : previousTool
        }
        if tool == .highlighter {
            highlighterWidth = width
        } else {
            penWidth = width
        }
    }

    /// The pencil in the ink it last had.
    func choosePencil() {
        if case .pen = tool { return }
        if case .pen = previousTool {
            tool = previousTool
        } else {
            tool = .pen(.graphite)
        }
    }
}

/// The front of the pen palette (ds.css `.pen-palette`), shared by the score and a PDF's pages: the pencil and its four
/// inks, the highlighter, the eraser and the thickness. Testing (2026-11-06): the inks alone did not read as the way back
/// from the highlighter to the pencil, and there was no thickness.
struct PenTools<Holder: PenHolder>: View {
    @ObservedObject var holder: Holder
    let text: UIText
    /// Icons without their words, where the palette would not fit across the screen (an iPad in portrait).
    var compact = false

    private static var widthKeys: [String] { ["pencil.widthThin", "pencil.widthMedium", "pencil.widthThick"] }
    /// The dots that show the three thicknesses.
    private static var dots: [CGFloat] { [4, 8, 13] }

    var body: some View {
        let pencil: Bool = { if case .pen = holder.tool { return true } else { return false } }()
        Button {
            holder.choosePencil()
        } label: {
            HStack(spacing: 6) {
                Icon(name: "pen", size: CGFloat.ds("icon-s"))
                if !compact { Text(text["pencil.pen"]) }
            }
        }
        .buttonStyle(DSItemStyle(on: pencil))
        .accessibilityLabel(text["pencil.pen"])
        .accessibilityAddTraits(pencil ? .isSelected : [])
        ForEach(ScoreController.Ink.allCases, id: \.self) { ink in
            let chosen = holder.tool == .pen(ink)
            Button {
                holder.tool = .pen(ink)
            } label: {
                Circle()
                    .fill(Color(uiColor: ink.color))
                    .frame(width: 20, height: 20)
                    .padding(2)
                    .overlay(Circle().strokeBorder(chosen ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: chosen ? 2 : 1))
                    .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(text[ink.labelKey])
            .accessibilityAddTraits(chosen ? .isSelected : [])
        }
        PaletteDivider()
        Button {
            holder.tool = .highlighter
        } label: {
            HStack(spacing: 6) {
                Icon(name: "highlight", size: CGFloat.ds("icon-s"))
                if !compact { Text(text["pencil.highlighter"]) }
            }
        }
        .buttonStyle(DSItemStyle(on: holder.tool == .highlighter))
        .accessibilityLabel(text["pencil.highlighter"])
        .accessibilityAddTraits(holder.tool == .highlighter ? .isSelected : [])
        Button(text["pencil.eraser"]) {
            holder.tool = .eraser
        }
        .buttonStyle(DSItemStyle(on: holder.tool == .eraser))
        .accessibilityAddTraits(holder.tool == .eraser ? .isSelected : [])
        PaletteDivider()
        HStack(spacing: 0) {
            ForEach(0..<PenSizes.pen.count, id: \.self) { width in
                let chosen = holder.width == width
                Button {
                    holder.setWidth(width)
                } label: {
                    // A dot as thick as the line it draws: thin, medium, thick.
                    Circle()
                        .fill(Color.ds("ink"))
                        .frame(width: Self.dots[width], height: Self.dots[width])
                        .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                        .background(chosen ? Color.ds("accent-tint") : .clear, in: Circle())
                        .overlay(Circle().strokeBorder(chosen ? Color.ds("accent") : .clear, lineWidth: 2))
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(text[Self.widthKeys[width]])
                .accessibilityAddTraits(chosen ? .isSelected : [])
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["pencil.width"])
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
