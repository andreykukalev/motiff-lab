import SwiftUI

/// The piano roll's drawer (ds.css `.drawer`, the web app's roll drawer): up from the bottom, 40% of the height in
/// landscape and 30% in portrait until the player drags its handle (RollDrawerHeight, #511 section 7.1), with its head
/// (its name, its mode switch and its settings, or the note editor's tools) over the roll and the zoom on each axis over
/// the notes (#479 item 23).
struct RollDrawer: View {
    @ObservedObject var roll: RollController
    @ObservedObject var score: ScoreController
    let text: UIText
    /// The selection's span as bar.beat, "9.1 – 12.3", under the one playback position (v0.5.2); nil for its bars.
    var span: String?
    /// Expand: the phrase in focus as a roll (testing item 12).
    let expand: () -> Void
    /// The handle dragged by a distance down (true when let go), and a step of its height up (+1) or down (−1).
    let resize: (_ dy: CGFloat, _ ended: Bool) -> Void
    let step: (_ direction: CGFloat) -> Void
    let close: () -> Void
    /// Whether the press on the handle has moved past a tap, so it resizes rather than puts the drawer away.
    @State private var handleMoved = false

    var body: some View {
        VStack(spacing: CGFloat.ds("s-2")) {
            // The head on one line where it fits, else with the roll's settings in More (portrait, the mockup's «⋯»). Which
            // of the two is decided by the width alone, never by the mode, so switching modes moves nothing (#521 18.3).
            ViewThatFits(in: .horizontal) {
                sized(compact: false)
                sized(compact: true)
            }
            RollSurface(roll: roll, place: .drawer, text: text)
        }
        .padding(.top, CGFloat.ds("s-5"))
        .padding(.horizontal, CGFloat.ds("s-3"))
        .padding(.bottom, CGFloat.ds("s-2"))
        .background(Color.ds("surface"), in: UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
        .overlay(alignment: .top) { handle }
        .dsShadow("shadow-sheet")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["bar.roll"])
    }

    /// The handle over the top of the drawer (#511 section 7.1, the web's `.drawer > .grab`): dragged up the drawer grows,
    /// down it shrinks, dragged nearly away it goes, and a tap puts it away. It is the drawer's own, over no score, so it
    /// never takes a stroke of the Pencil or a selection; it reads the drag on the screen, as the drawer moves under it.
    private var handle: some View {
        Capsule().fill(Color.ds("hairline-strong")).frame(width: 36, height: 4)
            .padding(.top, CGFloat.ds("s-2"))
            .frame(width: CGFloat.ds("h-touch") * 3, height: CGFloat.ds("s-6"), alignment: .top)
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 0, coordinateSpace: .global)
                    .onChanged { drag in
                        let dy = drag.translation.height
                        if handleMoved || abs(dy) >= RollDrawerHeight.tapDistance {
                            handleMoved = true
                            resize(dy, false)
                        }
                    }
                    .onEnded { drag in
                        let moved = handleMoved
                        handleMoved = false
                        if moved {
                            resize(drag.translation.height, true)
                        } else {
                            close()
                        }
                    }
            )
            .accessibilityElement()
            .accessibilityLabel(text["drawer.resize"])
            .accessibilityAddTraits(.isButton)
            .accessibilityAction { close() }
            .accessibilityAdjustableAction { direction in
                switch direction {
                case .increment: step(1)
                case .decrement: step(-1)
                @unknown default: break
                }
            }
    }

    /// One form of the head with the heads of both modes under it, unseen (#521 18.3): the wider of them decides whether the
    /// form fits, so the form, and with it every control's place, stays the same in Navigate and in Edit notes.
    private func sized(compact: Bool) -> some View {
        ZStack(alignment: .leading) {
            Group {
                head(compact: compact, mode: .navigate, editor: NoteEditorState(), live: false)
                head(compact: compact, mode: .edit, editor: NoteEditorState(), live: false)
            }
            .hidden()
            .allowsHitTesting(false)
            .accessibilityHidden(true)
            head(compact: compact, mode: roll.shownMode, editor: score.noteEditor, live: true)
        }
    }

    /// The head (the approved mockup v0.5.2, #479 item 23; #521 18): the name and the roll's mode switch first, where they
    /// always are; then in Navigate the selection's place and Clear selection, Note names, the colour scheme, Expand and
    /// Close, or in Edit notes the editor's tools, the selection's place, Cancel and Save. The selection's place takes the
    /// room left and never pushes a control (#521 18.4).
    @ViewBuilder private func head(compact: Bool, mode: RollMode, editor: NoteEditorState?, live: Bool) -> some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            if !compact {
                Text(text["roll.heading"]).dsType(.bodyStrong).foregroundStyle(Color.ds("ink")).lineLimit(1).fixedSize()
            }
            RollModeSwitch(chosen: mode, text: text, labels: !compact) { roll.chooseTool($0, score: score) }
            if mode == .edit, let editor {
                RollEditTools(score: score, editor: editor, text: text, compact: compact)
                place
                RollEditFinish(score: score, editor: editor, text: text)
            } else {
                place
                if !compact {
                    clearSelection(live: live)
                    settings
                } else {
                    more
                }
                // A draft with changes waits in Navigate for Cancel or Save.
                if let editor {
                    RollEditFinish(score: score, editor: editor, text: text)
                }
                Button(action: close) {
                    Icon(name: "close")
                }
                .buttonStyle(RollHeadStyle(round: true))
                .accessibilityLabel(text["bar.close"])
            }
        }
    }

    /// Where the selection is, "bars 9–16" or "9.1 – 12.3": in the room the controls leave, cut short rather than moving
    /// them (an ideal width of nothing, so it never decides the head's form).
    private var place: some View {
        Text(selectionPlace ?? "")
            .dsType(.label)
            .foregroundStyle(Color.ds("ink-2"))
            .lineLimit(1)
            .truncationMode(.tail)
            .frame(minWidth: 0, idealWidth: 0, maxWidth: .infinity, alignment: .trailing)
            .accessibilityHidden(selectionPlace == nil)
    }

    /// Clear selection in the head (the selection menu gives way to the drawer, #338): its room is kept without a selection,
    /// so it appearing moves nothing (#521 18.4). Under the modes of version 0.5, Escape removes the selection too, on the
    /// head shown (`live`) and not on the unseen ones that measure it.
    private func clearSelection(live: Bool) -> some View {
        let shown = score.selection != nil
        return Button {
            score.clearSelection()
        } label: {
            Icon(name: "close")
        }
        .buttonStyle(RollHeadStyle(round: true))
        .keyboardShortcut(live && shown && score.mode != nil ? KeyboardShortcut(.escape, modifiers: []) : nil)
        .disabled(!shown)
        .opacity(shown ? 1 : 0)
        .accessibilityHidden(!shown)
        .accessibilityLabel(text["menu.clearSelection"])
        .help(text["menu.clearSelection"])
    }

    /// Note names, the colour scheme and Expand, on the head's line: the two settings quiet, Expand a button (#521 18.1).
    @ViewBuilder private var settings: some View {
        Button {
            roll.noteNames.toggle()
        } label: {
            HStack(spacing: 6) {
                Circle().fill(roll.noteNames ? Color.ds("accent") : Color.ds("hairline-strong")).frame(width: 6, height: 6)
                Text(text["roll.noteNames"])
            }
        }
        .buttonStyle(RollHeadStyle(on: roll.noteNames))
        .accessibilityAddTraits(roll.noteNames ? .isSelected : [])
        Button(text["roll.colour.\(roll.colourBy)"]) {
            roll.nextColour()
        }
        .buttonStyle(RollHeadStyle())
        .disabled(!roll.canChangeColour)
        .help(text["roll.colourHint"])
        Button(action: expand) {
            HStack(spacing: 6) {
                Icon(name: "expand", size: CGFloat.ds("icon-s"))
                Text(text["roll.expand"])
            }
        }
        .buttonStyle(RollHeadStyle(outlined: true))
        .help(text["roll.expandHint"])
    }

    /// The same in More where the head is narrow, with Clear selection.
    private var more: some View {
        Menu {
            if score.selection != nil {
                Button(text["menu.clearSelection"]) { score.clearSelection() }
            }
            Toggle(text["roll.noteNames"], isOn: $roll.noteNames)
            Button(text["roll.colour.\(roll.colourBy)"]) { roll.nextColour() }
                .disabled(!roll.canChangeColour)
            Button(text["roll.expand"], action: expand)
        } label: {
            Icon(name: "more")
        }
        .menuStyle(.button)
        .buttonStyle(RollHeadStyle(round: true))
        .accessibilityLabel(text["bar.more"])
    }

    /// "bars 9–16", the span as bar.beat under the one playback position, or nil with nothing selected.
    private var selectionPlace: String? {
        guard let selection = score.selection else { return nil }
        if let span { return span }
        return selection.firstBar == selection.lastBar
            ? text.format("bars.one", String(selection.firstBar))
            : text.format("bars.range", String(selection.firstBar), String(selection.lastBar))
    }
}

/// A button of the roll's head (#521 18.2): one height (`--h-control`) and one shape, a capsule, for every control, in
/// every state. A setting is quiet ink, tinted while it is on; an action (Expand) has a rim, so it reads as a button and
/// not as a status; `round` is an icon alone in a circle of the same height.
struct RollHeadStyle: ButtonStyle {
    var on = false
    var outlined = false
    var round = false
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(DesignTokens.font(weight: on ? 600 : 500, size: 13, relativeTo: .callout))
            .foregroundStyle(on ? Color.ds("accent") : Color.ds(outlined ? "ink" : "ink-2"))
            .lineLimit(1)
            .padding(.horizontal, round ? 0 : CGFloat.ds("s-3"))
            .frame(minWidth: CGFloat.ds("h-control"), minHeight: CGFloat.ds("h-control"))
            .background(fill(pressed: configuration.isPressed), in: Capsule())
            .overlay {
                if outlined {
                    Capsule().strokeBorder(Color.ds("hairline-strong"))
                }
            }
            .opacity(isEnabled ? 1 : 0.38)
            .contentShape(Capsule())
    }

    private func fill(pressed: Bool) -> Color {
        if on || pressed { return .ds("accent-tint") }
        return outlined ? .ds("surface-2") : .clear
    }
}
