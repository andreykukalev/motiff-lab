import SwiftUI

/// The roll on the score's screen: its drawer (#511 section 7), the drawer's height (section 7.1), the span in its heading
/// and the drawer's Expand.
extension ScoreScreenContent {
    /// The roll's drawer from the bottom shelf while it is open, `height` tall (`drawerShare` of the `room` between the
    /// strips, or following its handle).
    @ViewBuilder func rollDrawer(height: CGFloat, room: CGSize, landscape: Bool) -> some View {
        if panels[.roll] {
            RollDrawer(roll: roll, score: score, selectionModel: selectionModel, text: text, span: rollSpan, expand: expandFromDrawer, resize: { dy, ended in
                resizeDrawer(by: dy, ended: ended, height: room.height, landscape: landscape)
            }, step: { direction in
                stepDrawer(direction, landscape: landscape)
            }, penOn: penModel.penOn, noteEditor: checkModel.noteEditor) { change(.close(.roll)) }
                .frame(height: height)
                .padding(.horizontal, CGFloat.ds("s-3"))
                .transition(.move(edge: .bottom))
        }
    }

    /// The drawer's Expand: only with a selection (#521 19.1), without one the dimmed button says why. Expand inspects the
    /// passage, so the Pen, which would write nothing there, is put away first.
    private func expandFromDrawer() {
        guard selectionModel.selection != nil else {
            score.showNotice("roll.expandNeedsSelection")
            return
        }
        if penModel.penOn { penModel.penOn = false }
        change(.expandFromDrawer)
        focusModel.expandRoll()
    }

    /// The drawer's share of the room's height between the strips: as the player left it in this orientation, 40% in
    /// landscape and 30% in portrait until then.
    func drawerShare(_ landscape: Bool) -> CGFloat {
        drawerShares[landscape] ?? RollDrawerHeight.standard(landscape: landscape)
    }

    /// The drawer's handle dragged by `dy` points (#511 section 7.1): the drawer follows it, and when let go keeps that
    /// height on the device, or goes away when dragged nearly off. The roll keeps its zoom, view and selection, and the
    /// music plays on.
    private func resizeDrawer(by dy: CGFloat, ended: Bool, height: CGFloat, landscape: Bool) {
        let share = RollDrawerHeight.share(from: drawerShare(landscape), dragged: dy, height: height)
        guard ended else {
            drawerDrag = share ?? RollDrawerHeight.minShare
            return
        }
        withAnimation(.ds("t-fast")) {
            drawerDrag = nil
            if let share {
                drawerShares[landscape] = share
                RollDrawerHeight.keep(share, landscape: landscape)
            } else {
                change(.close(.roll))
            }
        }
    }

    /// A step of the drawer's height from VoiceOver's adjust (the web's arrow keys on the handle).
    private func stepDrawer(_ direction: CGFloat, landscape: Bool) {
        let share = RollDrawerHeight.clamped(drawerShare(landscape) + direction * RollDrawerHeight.step)
        withAnimation(.ds("t-fast")) { drawerShares[landscape] = share }
        RollDrawerHeight.keep(share, landscape: landscape)
    }

    /// The selection's span as bar.beat for the roll's heading, under the one playback position (v0.5.2); nil for its bars.
    private var rollSpan: String? {
        guard let range = focusModel.playRange?.range, let span = player.barBeats.span(range) else { return nil }
        return text.format("pill.span", span.first, span.last)
    }
}
