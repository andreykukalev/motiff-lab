import SwiftUI

/// What follows a value the score's screen observes (ScoreWorkspace): the bodies of the view's `.onChange` reactions. Each
/// is called in one line from the modifier that observes its value, where its body was, so it runs in the render and the
/// order SwiftUI gave it (docs/code-map/ipad/workspace.md, "The order the view must keep"); they stand here in the order
/// `ScoreScreenContent` declares them. What only the view knows (its appearance, its geometry) comes in as a parameter,
/// read by the view at the call.
extension ScoreWorkspace {
    // MARK: - layers

    /// Put away, the menu forgets where the player dragged it (#582 item 7). The reaction on `selectionMenuOpen`.
    func selectionMenuChanged(_ open: Bool) {
        score.menuShown(open)
    }

    // MARK: - watching: the drawers and sheets, what covers the page, and one of them at a time

    /// Whether the circle's scale sounds now, which its button in Insights shows (the reaction on `player.hearingAlone`).
    func hearingAloneChanged(_ alone: Bool) {
        insights.scaleHeard = alone
    }

    /// Room under the last page for whatever covers the bottom of the score; `height` is what the view measured.
    func coveredChanged(_ height: CGFloat) {
        score.insetBottom(height)
    }

    /// The selection menu keeps to the right of the drawer over the left of the page; `side` is whether the Contents is
    /// there, as the view lays it out.
    func contentsSideChanged(_ side: Bool) {
        score.clearLeft(side ? ScoreScreen.contentsWidth + CGFloat.ds("s-3") : 0)
    }

    /// The Contents opened or closed (the reaction on `panels[.contents]`).
    func contentsChanged(_ open: Bool) {
        apply(.contents(open))
        // A bookmark being changed in the drawer goes with it; one being made over the selection stays.
        if !open, contents.draft?.id != nil { contents.cancel() }
    }

    /// Expand covers the page (#521 17, 19): the drawers go, and come back with the way back as they were (PanelState), and
    /// so does the loop. Opened while the music plays, it plays on within the passage. Out of it the playback model takes the
    /// selection and the loop back (v0.5.2). The reaction on `score.focused`.
    func focusChanged(_ focused: Bool) {
        if focused {
            apply(.expand(true))
        } else if player.loop != nil {
            // Expand's passage looped: the loop it left goes, so the one before comes back as it was.
            player.setLoop(nil)
        }
        syncPlayheads()
        if focused {
            // Expand plays on round its passage; the model out of it is left as it is (T18).
            if player.state != .stopped, let passage = score.playRange?.range { player.keepWithin(passage) }
        } else {
            player.hold(selection: score.playRange?.range)
            // Collapse opens the roll drawer again, as Expand found it; Back to page the Contents.
            apply(.expand(false))
        }
    }

    /// Practice view (#533 11): the page and the roll hide the blue playhead, the roll its loop strip too. The reaction on
    /// `score.practicing`, which also runs as the screen first appears.
    func practicingChanged(_ on: Bool) {
        score.sendPractice()
        roll.practice(on)
    }

    /// The selection controls (#582 item 10): the page's handles go with the menu. The reaction on
    /// `score.selectionControlsShown`.
    func selectionControlsChanged(_ shown: Bool) {
        score.sendSelectionControls(shown)
    }

    /// The roll in focus is the drawer's roll laid over the card, at the player's zoom (#511 section 5), bound to the passage
    /// Expand was opened on (#521 19): only it, laid across the card; the drawer gives it up and gets it back (PanelState).
    /// Back, or Notes chosen, lets the passage go: its zoom and view are as before Expand (#521 17). The reaction on
    /// `score.focused && score.focusRoll`; `dark` is the screen's appearance.
    func focusRollChanged(_ shown: Bool, dark: Bool) {
        apply(.rollOnCard(shown))
        if shown {
            roll.show(piece, dark: dark)
            roll.showSelection(score.selection, loop: player.loop)
            roll.bind(score.selection.map { $0.onset..<max($0.end, $0.onset + 1) })
            return
        }
        roll.bind(nil)
        if panels.rollEditsGoWithCard {
            // The roll in focus put away, not back to the drawer: its unsaved edits go, as the drawer's do.
            score.cancelNoteEdit()
        }
    }

    /// The roll's drawer opened or closed (the reaction on `panels[.roll]`); `dark` is the screen's appearance.
    func rollDrawerChanged(_ open: Bool, dark: Bool) {
        apply(.roll(open))
        if open {
            roll.show(piece, dark: dark)
            roll.showSelection(score.selection, loop: player.loop)
        } else if panels.rollEditsGoWithDrawer {
            // Edits are kept only by Save: the drawer put away with the editor open leaves them, as Cancel does.
            // Expand takes the editor into the roll in focus, with its draft (#479 item 23).
            score.cancelNoteEdit()
        }
    }

    /// A bar checked, or to check again: the facts on its notes say so, or no longer do (#352). The reaction on
    /// `score.check`.
    func checkChanged() {
        Task { await insights.reread() }
    }

    /// Insights opened or closed (the reaction on `panels[.insights]`).
    func insightsChanged(_ open: Bool) {
        apply(.insights(open))
        if open {
            Task { await insights.load(piece: piece.id, selection: score.selection) }
        } else {
            // The rings go with the sheet.
            insights.clearChoice()
        }
    }

    // MARK: - checking: Check and Sources take the other places' place, and go when one of them opens

    /// The device's settings changed in the sheet: the score and the roll follow at once. The reaction on
    /// `settings.values`.
    func settingsChanged() {
        score.settingsChanged()
        roll.settingsChanged()
    }

    /// The way back to More goes with the place opened from it (the reaction on `panels.placeFromMoreOpen`).
    func placeFromMoreChanged(_ open: Bool) {
        apply(.placeFromMore(open))
    }

    /// Check opened or closed (the reaction on `panels[.check]`): its flags show while it is open.
    func checkDrawerChanged(_ open: Bool) {
        score.checkDrawerOpen = open
        apply(.check(open))
    }

    /// Sources opened or closed (the reaction on `panels[.versions]` among Check's), with Expand as it is now.
    func sourcesChanged(_ open: Bool) {
        apply(.versions(open, focused: score.focused))
    }

    /// Check and Sources go when another place opens, or Expand (the reaction on `panels.otherPlaceOpen(focused:)`).
    func otherPlaceChanged(_ open: Bool) {
        apply(.otherPlace(open))
    }

    // MARK: - sourcing

    /// A variation only tried is left before the Sources sheet writes the project file (the second reaction on
    /// `panels[.versions]`, after Check's).
    func sourcesOpened(_ open: Bool) {
        if open && panels.sourcesOn { Task { await insights.leave() } }
    }

    // MARK: - body: one thing at a time on the right, the assistant's panel or the Insights, Check and Sources sheet

    /// The assistant's panel opened puts the sheet away (the reaction on `assistant.panelOpen`).
    func assistantPanelChanged(_ open: Bool) {
        if open { change(.assistantOpened) }
    }

    /// A sheet opened puts the assistant's panel away (the reaction on `panels.sheetOpen`).
    func sheetChanged(_ open: Bool) {
        if open { assistant.close() }
    }
}

/// The playback model as the page and the roll draw it, and where the roll is: what the reactions on Expand, the loop,
/// the selection and the player share.
extension ScoreWorkspace {
    /// Where the roll is on screen: in its drawer, or over the card in focus; the one place its web view is in (#495 BUG-01).
    var rollPlace: RollPlace? { RollPlace.shown(drawerOpen: panels[.roll], focused: score.focused, focusRoll: score.focusRoll) }

    /// Whether the roll is on screen.
    var rollShown: Bool { rollPlace != nil }

    /// The playback model as the page and the roll draw it (v0.5.2, #495 UX-10): the selection the blue playhead keeps to and
    /// Цикл, and the roll's lanes. In Expand the page draws the black playhead on the card inside the selection, its passage
    /// (v0.6.4, spec 8); the roll there keeps its own playhead, without lanes.
    func syncPlayheads() {
        // The hand the selection is narrowed to (v0.6.2): its band covers that staff, and the blue playhead with it (#608).
        let selection: Any = score.playRange.map { PlayheadsShown.selection($0.range, hand: $0.selection.hand) as Any } ?? NSNull()
        score.showPlayheads(["selection": selection, "loop": player.cycle])
        roll.lanes(score.holdsSelection)
        if rollShown { showRollPosition() }
    }

    /// Where the player is, for the roll: with the lanes, also the place as bar.beat and whether the selection loops.
    func showRollPosition() {
        let on = score.holdsSelection
        roll.position(player.tick, playing: player.state == .playing, label: on ? player.placeLabel : "", looping: on && player.loop != nil)
    }
}
