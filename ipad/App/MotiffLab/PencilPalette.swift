import SwiftUI
import UIKit

/// What a double tap or a squeeze of the Apple Pencil does (#479 item 8): what the player chose for it in the iPad's
/// Settings › Apple Pencil. Switching to the eraser or to the tool before is done as asked; every palette the iPad offers
/// there (the contextual palette, the colours, the ink's attributes) opens the app's own pencil palette, the tools and
/// the inks in a half circle at the Pencil, since the iPad's own palettes hold PencilKit's tools and not the app's
/// inks. Off and a system shortcut leave the app alone.
enum PencilGesture {
    enum Action: Equatable {
        case palette, eraser, previous, nothing
    }

    static func action(for preferred: UIPencilPreferredAction) -> Action {
        switch preferred {
        case .switchEraser: .eraser
        case .switchPrevious: .previous
        case .ignore: .nothing
        case .showColorPalette, .showInkAttributes: .palette
        default: unlistedAction(preferred)
        }
    }

    /// The actions iPadOS 17.5 added: the contextual palette opens the palette; a system shortcut is the system's.
    private static func unlistedAction(_ preferred: UIPencilPreferredAction) -> Action {
        if #available(iOS 17.5, *), preferred == .runSystemShortcut {
            return .nothing
        }
        return .palette
    }
}

/// Hears the Pencil's double tap and, on iPadOS 17.5 and later, its squeeze (the Apple Pencil Pro), with where the Pencil
/// hovers when the iPad knows it, in the window's coordinates.
@MainActor
final class PencilGestures: NSObject, UIPencilInteractionDelegate {
    var perform: (PencilGesture.Action, CGPoint?) -> Void = { _, _ in }

    /// Before iPadOS 17.5: a double tap, without a place.
    nonisolated func pencilInteractionDidTap(_ interaction: UIPencilInteraction) {
        MainActor.assumeIsolated {
            perform(PencilGesture.action(for: UIPencilInteraction.preferredTapAction), nil)
        }
    }

    @available(iOS 17.5, *)
    nonisolated func pencilInteraction(_ interaction: UIPencilInteraction, didReceiveTap tap: UIPencilInteraction.Tap) {
        MainActor.assumeIsolated {
            perform(PencilGesture.action(for: UIPencilInteraction.preferredTapAction), Self.place(tap.hoverPose, interaction))
        }
    }

    /// A squeeze acts once it is let go.
    @available(iOS 17.5, *)
    nonisolated func pencilInteraction(_ interaction: UIPencilInteraction, didReceiveSqueeze squeeze: UIPencilInteraction.Squeeze) {
        MainActor.assumeIsolated {
            guard squeeze.phase == .ended else { return }
            perform(PencilGesture.action(for: UIPencilInteraction.preferredSqueezeAction), Self.place(squeeze.hoverPose, interaction))
        }
    }

    @available(iOS 17.5, *)
    private static func place(_ pose: UIPencilHoverPose?, _ interaction: UIPencilInteraction) -> CGPoint? {
        guard let pose, let view = interaction.view else { return nil }
        return view.convert(pose.location, to: nil)
    }
}

/// The pencil palette asked for by the Pencil: where it hovered, in the window's coordinates, or nil when the iPad does
/// not know (then the palette opens under the bar's right end).
struct PencilPaletteCall: Equatable {
    var at: CGPoint?
}

extension PenHolder {
    /// What a double tap or a squeeze does: the palette opens (or closes again), or the tool switches.
    func pencilGesture(_ action: PencilGesture.Action, at point: CGPoint?) {
        switch action {
        case .palette:
            pencilPalette = pencilPalette == nil ? PencilPaletteCall(at: point) : nil
        case .eraser:
            tool = tool == .eraser ? previousTool : .eraser
        case .previous:
            tool = previousTool
        case .nothing:
            break
        }
    }

    /// A tool chosen in the pencil palette: the Pen comes on with it, so the Pencil writes with it at once.
    func chooseFromPalette(_ tool: ScoreController.Tool) {
        self.tool = tool
        penOn = true
        pencilPalette = nil
    }
}

/// Where the pencil palette's buttons go: the tools and inks on an outer half circle, the thicknesses on an inner one,
/// Undo in the middle, opening upward from the Pencil, or downward where there is no room above.
enum PaletteLayout {
    /// The offsets from the middle of `count` buttons spread evenly over a half circle of the radius, left to right.
    static func arc(count: Int, radius: CGFloat, upward: Bool) -> [CGPoint] {
        guard count > 0 else { return [] }
        return (0..<count).map { index in
            let angle = count == 1 ? Double.pi / 2 : Double.pi - Double.pi * Double(index) / Double(count - 1)
            let y = CGFloat(sin(angle)) * radius
            return CGPoint(x: CGFloat(cos(angle)) * radius, y: upward ? -y : y)
        }
    }

    /// The palette's middle for the Pencil's place in an area of this size: kept inside by the reach of its buttons, and
    /// whether it opens upward (room above the Pencil) or downward.
    static func centre(_ at: CGPoint, in size: CGSize, reach: CGFloat) -> (point: CGPoint, upward: Bool) {
        let upward = at.y - reach >= 0
        let x = min(max(at.x, reach), max(reach, size.width - reach))
        let y = upward ? min(at.y, size.height - reach / 4) : max(at.y, reach / 4)
        return (CGPoint(x: x, y: y), upward)
    }
}

/// The pencil palette (#479 item 8): opened by a double tap or a squeeze of the Pencil, at the Pencil. The four inks of
/// the pencil, the highlighter and the eraser in a half circle, the three thicknesses inside it and Undo in the middle; a
/// tool chosen turns the Pen on with it. A tap beside it closes it.
struct PencilPalette<Holder: PenHolder>: View {
    @ObservedObject var holder: Holder
    let call: PencilPaletteCall
    let text: UIText

    private static var button: CGFloat { CGFloat.ds("h-touch") }
    private static var outer: CGFloat { button * 2.1 }
    private static var inner: CGFloat { button * 1.1 }
    private static var widthKeys: [String] { ["pencil.widthThin", "pencil.widthMedium", "pencil.widthThick"] }
    private static var dots: [CGFloat] { [4, 8, 13] }

    var body: some View {
        GeometryReader { proxy in
            let frame = proxy.frame(in: .global)
            let fallback = CGPoint(x: frame.maxX - Self.outer - CGFloat.ds("s-4"), y: frame.minY + Self.outer + CGFloat.ds("h-touch") * 2)
            let at = call.at ?? fallback
            let placed = PaletteLayout.centre(CGPoint(x: at.x - frame.minX, y: at.y - frame.minY), in: proxy.size, reach: Self.outer + Self.button / 2)
            ZStack {
                Color.clear
                    .contentShape(Rectangle())
                    .onTapGesture { holder.pencilPalette = nil }
                    .accessibilityHidden(true)
                ring(placed.upward)
                    .position(placed.point)
            }
        }
        .transition(.opacity)
    }

    private func ring(_ upward: Bool) -> some View {
        let tools: [ScoreController.Tool] = ScoreController.Ink.allCases.map { .pen($0) } + [.highlighter, .eraser]
        let outer = PaletteLayout.arc(count: tools.count, radius: Self.outer, upward: upward)
        let inner = PaletteLayout.arc(count: PenSizes.pen.count, radius: Self.inner, upward: upward)
        return ZStack {
            ForEach(tools.indices, id: \.self) { index in
                toolButton(tools[index]).offset(x: outer[index].x, y: outer[index].y)
            }
            ForEach(0..<PenSizes.pen.count, id: \.self) { width in
                widthButton(width).offset(x: inner[width].x, y: inner[width].y)
            }
            undoButton
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["pencil.palette"])
    }

    private func toolButton(_ tool: ScoreController.Tool) -> some View {
        let chosen = holder.penOn && holder.tool == tool
        return Button {
            holder.chooseFromPalette(tool)
        } label: {
            disc(chosen: chosen) {
                switch tool {
                case .pen(let ink):
                    Circle().fill(Color(uiColor: ink.color)).frame(width: 22, height: 22)
                case .highlighter:
                    Icon(name: "highlight", size: CGFloat.ds("icon-m"))
                case .eraser:
                    // The kit has no eraser icon: its word, as in the palette under the bar.
                    Text(text["pencil.eraser"]).dsType(.label).lineLimit(1).fixedSize()
                }
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text[Self.label(tool)])
        .accessibilityAddTraits(chosen ? .isSelected : [])
    }

    private func widthButton(_ width: Int) -> some View {
        let chosen = holder.width == width
        return Button {
            holder.setWidth(width)
            holder.penOn = true
            holder.pencilPalette = nil
        } label: {
            disc(chosen: chosen) {
                Circle().fill(Color.ds("ink")).frame(width: Self.dots[width], height: Self.dots[width])
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text[Self.widthKeys[width]])
        .accessibilityAddTraits(chosen ? .isSelected : [])
    }

    private var undoButton: some View {
        Button {
            holder.undo()
        } label: {
            disc(chosen: false) { Icon(name: "undo", size: CGFloat.ds("icon-m")) }
        }
        .buttonStyle(.plain)
        .disabled(!holder.canUndo)
        .accessibilityLabel(text["pencil.undo"])
    }

    private func disc<Content: View>(chosen: Bool, @ViewBuilder content: () -> Content) -> some View {
        content()
            .foregroundStyle(chosen ? Color.ds("accent") : Color.ds("ink"))
            .padding(.horizontal, CGFloat.ds("s-1"))
            .frame(minWidth: Self.button, minHeight: Self.button)
            .background(chosen ? Color.ds("accent-tint") : Color.ds("surface"), in: Capsule())
            .overlay(Capsule().strokeBorder(chosen ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: chosen ? 2 : 1))
            .dsShadow("shadow-menu")
            .contentShape(Capsule())
    }

    private static func label(_ tool: ScoreController.Tool) -> String {
        switch tool {
        case .pen(let ink): ink.labelKey
        case .highlighter: "pencil.highlighter"
        case .eraser: "pencil.eraser"
        }
    }
}
