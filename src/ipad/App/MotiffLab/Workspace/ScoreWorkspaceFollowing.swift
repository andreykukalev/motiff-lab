import SwiftUI

/// What follows the selection, the player and the fact chosen (ScoreWorkspace): the bodies of the `.onChange` reactions of
/// `ScoreScreenContent.following`, in the order it declares them. Each is called in one line from the modifier that
/// observes its value, where its body was, so it runs in the render and the order SwiftUI gave it
/// (docs/code-map/ipad/workspace.md, "The order the view must keep"). The panels' and Expand's reactions, and what these
/// share with them (`syncPlayheads`, `showRollPosition`, `rollShown`), are in ScoreWorkspaceReactions.swift.
extension ScoreWorkspace {
    /// In focus with no way tried, the other version is there to hear in the variation's place (0022, section 5). The
    /// reaction on `score.focusModel.version`.
    func focusVersionChanged(_ version: FocusModel.Version?) {
        guard insights.trial == nil else { return }
        if let version, version.sounds {
            player.setVariation(version.notes, version: true)
        } else if player.secondIsVersion {
            player.setVariation(nil)
        }
    }

    /// The places of the fact chosen in Insights are ringed on the page and lit on the roll; none clears them. The
    /// reaction on `insights.chosen`.
    func factChosen(_ chosen: InsightsController.Choice?) {
        let places = chosen?.places ?? []
        score.highlight(places, scroll: player.state != .playing)
        roll.highlight(places)
    }

    /// The roll on screen follows the iPad's appearance (the reaction on the view's `colorScheme`, its new value).
    func appearanceChanged(_ scheme: ColorScheme) {
        if rollShown { roll.show(piece, dark: scheme == .dark) }
    }

    /// The one holder of the roll's web view follows the place shown, so Expand and Back move it once (#495 BUG-01). The
    /// reaction on `rollPlace`. Shown in its drawer or on Expand's card, the roll takes where the player is: it is told
    /// nothing while it is away (v1.4 b-1: its black playhead stood where it was left).
    func rollPlaceChanged(_ place: RollPlace?) {
        roll.place = place
        if place != nil { showRollPosition() }
    }

    /// Where the music is, on the page and on the roll while it is on screen (the reaction on `player.tick`).
    func playerMoved(_ tick: Double) {
        score.playhead(tick, playing: player.state == .playing)
        if rollShown { showRollPosition() }
    }

    /// A new Play follows the music again, even after the player scrolled away by hand (the reaction on `player.state`).
    func playerStateChanged(from old: TransportState, to state: TransportState) {
        score.playhead(player.tick, playing: state == .playing, follow: old != .playing && state == .playing)
        if rollShown { showRollPosition() }
    }

    /// The selection changed (the reaction on `score.selectionModel.selection`): the roll shows it, and what was started on the one
    /// before goes.
    func selectionChanged(_ selection: ScoreSelection?) {
        roll.showSelection(selection, loop: player.loop)
        // A bookmark being made belongs to the selection it was started on, and so does a note.
        if let draft = contents.draft, draft.id == nil { contents.cancel() }
        if let note = score.marksModel.noteDraft, note.onset != selection?.onset || note.end != selection?.end { score.marksModel.noteDraft = nil }
        // The stream follows the selection while it is open; a variation tried belongs to the passage it was
        // tried on, so it is left either way.
        if panels[.insights] {
            Task { await insights.load(piece: piece.id, selection: selection) }
        } else if insights.trial != nil {
            Task { await insights.leave() }
        }
    }

    /// Цикл turned on or off: the blue playhead waits at the selection's start or stands at its end (v0.5.2). The
    /// reaction on `player.loop`.
    func loopChanged(_ loop: Range<Double>?) {
        roll.showSelection(score.selectionModel.selection, loop: loop)
        syncPlayheads()
    }

    /// The note editor on the roll (testing item 15): a press on its notes edits them while it is open. The reaction on
    /// `score.noteEditor`.
    func noteEditorChanged(_ editor: NoteEditorState?) {
        roll.showEditor(editor)
    }

    /// What plays for the selection changed (the reaction on `score.focusModel.playRange`): the player takes it, then the page and
    /// the roll draw the model.
    func whatPlaysChanged(from old: FocusModel.PlayRange?, to new: FocusModel.PlayRange?) {
        playRangeChanged(from: old, to: new)
        syncPlayheads()
    }
}

/// The playback model on the score's screen (v0.5.2): what the player takes from the score.
extension ScoreWorkspace {
    /// What plays for the selection changed. A loop follows it, as on the web; a new selection stops the old loop. Under the
    /// playback model (v0.5.2) the player takes the selection, and Цикл decides (T9–T11).
    func playRangeChanged(from old: FocusModel.PlayRange?, to new: FocusModel.PlayRange?) {
        // Expand loops its passage (#533 2): what plays for it plays on round it.
        if score.focusModel.focused {
            if let new, player.state != .stopped { player.keepWithin(new.range) }
            return
        }
        player.apply(.select(new?.range))
    }

    /// Once the sound has loaded: the piece's beat lines for the one selection on the sixteenth grid (v0.6.4), and the
    /// playback model as the page and the roll draw it.
    func applyModel() {
        score.setBeatLines(player.beatLines)
        score.setBarBeats(player.barBeats)
        syncPlayheads()
        if score.holdsSelection { player.hold(selection: score.focusModel.playRange?.range) }
    }
}
