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
            // Outside the Pen the eraser comes on with the Pen, so the switch is seen and the Pencil erases at once (#511 4.2).
            if penOn || pen.active != .eraser { pen.toggleEraser() }
            penOn = true
        case .previous:
            // The tool before while the Pen is on; outside it the Pen comes on, with the tool it had.
            if penOn { pen.choosePrevious() } else { penOn = true }
        case .nothing:
            break
        }
    }

    /// A tool chosen in the pencil palette: the Pen comes on with it, so the Pencil writes with it at once.
    func chooseFromPalette(_ tool: ActiveTool) {
        pen.choose(tool)
        closePaletteWritingOn()
    }

    /// A thickness of the active tool chosen in the pencil palette: the tool stays, the Pen comes on.
    func chooseFromPalette(width: Int) {
        pen.setWidth(width)
        closePaletteWritingOn()
    }

    /// An ink of the pencil chosen in the pencil palette: the tool stays, the Pen comes on.
    func chooseFromPalette(ink: ScoreController.Ink) {
        pen.setInk(ink)
        closePaletteWritingOn()
    }

    private func closePaletteWritingOn() {
        penOn = true
        pencilPalette = nil
    }
}

/// Where the pencil palette's buttons go: the tools on an inner half circle, the active tool's settings on an outer
/// one, Undo in the middle, opening upward from the Pencil, or downward where there is no room above.
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

    /// The outer half circle's places (#495 UX-06): the pencil's four inks on the left, a gap, the three thicknesses on
    /// the right. The thicknesses keep their places for the marker, which has no inks.
    static let settingSlots = 8

    /// The place on the outer half circle of an ink of the pencil.
    static func slot(ink: ScoreController.Ink) -> Int {
        ScoreController.Ink.allCases.firstIndex(of: ink) ?? 0
    }

    /// The place on the outer half circle of a thickness.
    static func slot(width: Int) -> Int {
        settingSlots - PenSizes.pen.count + width
    }
}

/// The pencil palette (#479 item 8): opened by a double tap or a squeeze of the Pencil, at the Pencil. The tools (the
/// pencil, the marker, the eraser) on an inner half circle, exactly one chosen; the active tool's settings on an outer
/// one (#495 UX-06): the pencil's inks and its thicknesses, the marker's thicknesses in the same places, nothing for the
/// eraser; Undo in the middle. Whatever is chosen turns the Pen on with it and closes the palette; a tap beside it closes
/// it. On the score under the modes (#511 4.2, option B) the modes lie innermost, and around them the chosen one's
/// options: the Pen's tools and settings with the ink's Undo in the middle, or Select's Redo and «По тактам / По долям»
/// with the selection's Undo in the middle; Navigate has the selection's Undo alone. A mode chosen there closes it too.
struct PencilPalette<Holder: PenHolder>: View {
    @ObservedObject var holder: Holder
    let call: PencilPaletteCall
    let text: UIText
    /// The modes on the innermost circle and the chosen one's options around them (#511 4.2, option B), where the view has
    /// modes (the score under the modes); nil keeps the tools alone (a PDF's pages).
    var modes: RingModes?

    private static var button: CGFloat { CGFloat.ds("h-touch") }
    private static var outer: CGFloat { button * 2.5 }
    private static var inner: CGFloat { button * 1.3 }
    private static var outermost: CGFloat { button * 3.7 }

    /// The circles' radii: the modes, the tools (or Select's options), the tool's settings.
    private var radii: (tools: CGFloat, settings: CGFloat) {
        modes == nil ? (Self.inner, Self.outer) : (Self.outer, Self.outermost)
    }

    var body: some View {
        GeometryReader { proxy in
            let frame = proxy.frame(in: .global)
            let fallback = CGPoint(x: frame.maxX - Self.outer - CGFloat.ds("s-4"), y: frame.minY + Self.outer + CGFloat.ds("h-touch") * 2)
            let at = call.at ?? fallback
            let placed = PaletteLayout.centre(CGPoint(x: at.x - frame.minX, y: at.y - frame.minY), in: proxy.size, reach: radii.settings + Self.button / 2)
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
        ZStack {
            if let modes {
                let places = PaletteLayout.arc(count: InteractionMode.inBar.count, radius: Self.inner, upward: upward)
                ForEach(InteractionMode.inBar.indices, id: \.self) { index in
                    modeButton(InteractionMode.inBar[index], modes).offset(x: places[index].x, y: places[index].y)
                }
            }
            switch modes?.chosen ?? .pen {
            case .pen:
                penRing(upward)
            case .select:
                if let modes { selectRing(modes, upward) }
            default:
                EmptyView()
            }
            if let modes, modes.chosen != .pen {
                selectionUndoButton(modes)
            } else {
                undoButton
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["pencil.palette"])
    }

    /// A mode on the innermost circle: chosen, it closes the ring, and its options show under the bar.
    private func modeButton(_ mode: InteractionMode, _ modes: RingModes) -> some View {
        let chosen = modes.chosen == mode
        return Button {
            modes.choose(mode)
            holder.pencilPalette = nil
        } label: {
            disc(chosen: chosen, tool: true) { Icon(name: mode.icon, size: CGFloat.ds("icon-m")) }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text[mode.label])
        .accessibilityAddTraits(chosen ? .isSelected : [])
    }

    /// Select's options around the modes: Redo, then by bars or by beats; Undo is in the middle.
    private func selectRing(_ modes: RingModes, _ upward: Bool) -> some View {
        let places = PaletteLayout.arc(count: 3, radius: radii.tools, upward: upward)
        return ZStack {
            Button {
                modes.redo()
            } label: {
                disc(chosen: false) { Icon(name: "undo", size: CGFloat.ds("icon-m")).scaleEffect(x: -1, y: 1) }
            }
            .buttonStyle(.plain)
            .disabled(!modes.canRedo)
            .accessibilityLabel(text["selection.redo"])
            .offset(x: places[0].x, y: places[0].y)
            byButton(beats: false, modes).offset(x: places[1].x, y: places[1].y)
            byButton(beats: true, modes).offset(x: places[2].x, y: places[2].y)
        }
    }

    private func byButton(beats: Bool, _ modes: RingModes) -> some View {
        let chosen = modes.byBeats == beats
        let key = beats ? "selection.byBeats" : "selection.byBars"
        return Button {
            modes.setByBeats(beats)
            holder.pencilPalette = nil
        } label: {
            disc(chosen: chosen) { Text(text[key]).dsType(.label).lineLimit(1).fixedSize() }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text[key])
        .accessibilityAddTraits(chosen ? .isSelected : [])
    }

    private func selectionUndoButton(_ modes: RingModes) -> some View {
        Button {
            modes.undo()
        } label: {
            disc(chosen: false) { Icon(name: "undo", size: CGFloat.ds("icon-m")) }
        }
        .buttonStyle(.plain)
        .disabled(!modes.canUndo)
        .accessibilityLabel(text["selection.undo"])
    }

    /// The Pen's tools and the active tool's settings.
    private func penRing(_ upward: Bool) -> some View {
        let pen = holder.pen
        let tools = PaletteLayout.arc(count: ActiveTool.allCases.count, radius: radii.tools, upward: upward)
        let slots = PaletteLayout.arc(count: PaletteLayout.settingSlots, radius: radii.settings, upward: upward)
        return ZStack {
            ForEach(ActiveTool.allCases.indices, id: \.self) { index in
                toolButton(ActiveTool.allCases[index]).offset(x: tools[index].x, y: tools[index].y)
            }
            if pen.active == .pencil {
                ForEach(ScoreController.Ink.allCases, id: \.self) { ink in
                    let slot = slots[PaletteLayout.slot(ink: ink)]
                    inkButton(ink).offset(x: slot.x, y: slot.y)
                }
            }
            if pen.current != nil {
                ForEach(PenSizes.pen.indices, id: \.self) { width in
                    let slot = slots[PaletteLayout.slot(width: width)]
                    widthButton(width).offset(x: slot.x, y: slot.y)
                }
            }
        }
    }

    private func toolButton(_ tool: ActiveTool) -> some View {
        let chosen = holder.pen.active == tool
        return Button {
            holder.chooseFromPalette(tool)
        } label: {
            disc(chosen: chosen, tool: true) {
                if let icon = tool.icon {
                    Icon(name: icon, size: CGFloat.ds("icon-m"))
                } else {
                    // The kit has no eraser icon: its word, as in the palette under the bar.
                    Text(text[tool.labelKey]).dsType(.label).lineLimit(1).fixedSize()
                }
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text[tool.labelKey])
        .accessibilityAddTraits(chosen ? .isSelected : [])
    }

    private func inkButton(_ ink: ScoreController.Ink) -> some View {
        let chosen = holder.pen.isChosen(ink: ink)
        return Button {
            holder.chooseFromPalette(ink: ink)
        } label: {
            disc(chosen: chosen) { InkSwatch(ink: ink) }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text[ink.labelKey])
        .accessibilityAddTraits(chosen ? .isSelected : [])
    }

    private func widthButton(_ width: Int) -> some View {
        let chosen = holder.pen.isChosen(width: width)
        return Button {
            holder.chooseFromPalette(width: width)
        } label: {
            disc(chosen: chosen) { WidthDot(width: width, pen: holder.pen) }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text[WidthDot.labelKeys[width]])
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

    /// A button of the palette, on the surface. The chosen tool lies on the accent's strong tint, as in the tools under
    /// the bar; a chosen setting has the accent's tint and ring, as there.
    private func disc<Content: View>(chosen: Bool, tool: Bool = false, @ViewBuilder content: () -> Content) -> some View {
        let tint = !chosen ? Color.clear : Color.ds(tool ? "accent-tint-strong" : "accent-tint")
        let ringed = chosen && !tool
        return content()
            .foregroundStyle(chosen ? Color.ds("ink") : Color.ds("ink-2"))
            .padding(.horizontal, CGFloat.ds("s-1"))
            .frame(minWidth: Self.button, minHeight: Self.button)
            .background(tint, in: Capsule())
            .background(Color.ds("surface"), in: Capsule())
            .overlay(Capsule().strokeBorder(ringed ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: ringed ? 2 : 1))
            .dsShadow("shadow-menu")
            .contentShape(Capsule())
    }
}
