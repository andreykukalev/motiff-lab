import SwiftUI

/// The score's workspace (the architecture blueprint of v0.7, section 5.3 step 2): the controllers of the score's screen
/// and its panels, owned in one place, and what happens between them as named methods. `ScoreScreen` holds it as a
/// `@StateObject`, as it held each controller before: it is made once, as the screen first appears, with its controllers
/// in the order they were declared, and goes with the screen. The screen's content observes each controller itself, as it
/// did, and the panels through this object.
///
/// `wire()` hands the controllers their callbacks, each one a method here. The callbacks hold the workspace weakly and the
/// workspace holds the controllers, so nothing is kept alive in a ring. The reactions of the view (`.onChange`) call their
/// methods (ScoreWorkspaceReactions.swift, ScoreWorkspaceFollowing.swift) where their bodies were:
/// `docs/code-map/ipad/workspace.md`, "The order the view must keep". The screen's life (it appears, the app leaves it,
/// it goes, its piece is handed to another screen) is in ScoreWorkspaceLife.swift, the piece's sources and views in
/// ScoreWorkspaceSources.swift.
@MainActor
final class ScoreWorkspace: ObservableObject {
    let piece: OpenPiece
    let player = Player()
    let score = ScoreController()
    let roll = RollController()
    let insights = InsightsController()
    /// The model-backed assistant (#582 item 17): its side panel and the piece's conversations; on a draft's view, the
    /// draft's screen's, handed to each screen its view opens again on (v1.4).
    let assistant: AssistantController
    let contents = ContentsController()
    /// The piece's originals on this iPad (version 0.3): where each one is, and the PDF linked where it lies in Files; known
    /// from the piece's opening, so PDF | Notes is whole from the first frame (#495 BUG-02).
    let originals: OriginalsController
    /// The piece's sources by layers and the switch of all its views (version 0.6, 0030).
    let sources: SourcesController
    /// Which panels are open, and what they give back (PanelState): written only by `change` and `apply`.
    @Published private(set) var panels: PanelState
    /// The resume point this visit brings up to date (#327), so the piece keeps one for it rather than one for each save.
    var resumeId: String?
    /// The piece is being handed to its other view (PDF | Notes): a second press on the switch meanwhile does nothing
    /// (#582 item 2), as the first is already saving, closing and opening the piece.
    var handingOver = false

    /// `viewing` is the tag of a view shown over the piece (ScoreScreen.viewing): it has no Sources. `assistant` is one
    /// that outlasts this workspace (a draft's screen's); nil, it makes its own.
    init(piece: OpenPiece, viewing: String? = nil, assistant: AssistantController? = nil) {
        self.piece = piece
        self.assistant = assistant ?? AssistantController()
        originals = OriginalsController(opened: piece.own)
        // The switch of all views as the screen before had it, until this one has read its own (SourcesHandover).
        sources = SourcesController(layers: SourcesHandover.layers(for: piece.file))
        // Nothing is kept under Sources on a view shown over the piece, which has none.
        panels = PanelState(sourcesOn: viewing == nil)
    }

    /// What an action asks of the panels (PanelState.asked): only what it writes. The rules follow in the reactions.
    func change(_ intent: PanelState.Intent) {
        set(panels.asked(intent))
    }

    /// One rule (PanelState.following), run by the reaction that observes its value.
    func apply(_ rule: PanelState.Rule) {
        set(panels.following(rule))
    }

    /// ‹ More: the place opened from More goes, and More opens again once it has (after a sheet's dismissal, so the popover can show).
    func backToMore(_ panel: PanelState.Panel?) {
        change(.backToMore(panel))
        Task {
            try? await Task.sleep(for: .milliseconds(350))
            change(.open(.more))
        }
    }

    /// The panels as `next`, written only when they change.
    private func set(_ next: PanelState) {
        if next != panels { panels = next }
    }

    /// What the controllers are handed as the screen appears: each callback one method here.
    func wire() {
        #if DEBUG
        ScreensProbe.wired(player: player, roll: roll, assistant: assistant)
        #endif
        score.blankTapped = { [weak self] in self?.blankTapped() }
        score.placeTapped = { [weak self] beat in self?.placeTapped(beat) }
        score.checkPicked = { [weak self] _ in self?.checkPicked() }
        score.versionPicked = { [weak self] _ in self?.versionPicked() }
        score.hearNotes = { [weak self] notes, range in self?.hearNotes(notes, over: range) }
        score.notesChanged = { [weak self] in self?.notesChanged() }
        insights.placeChosen = { [weak self] tick in self?.placeChosen(tick) }
        insights.scaleReady = { [weak self] notes, range in self?.scaleReady(notes, over: range) }
        insights.scaleStop = { [weak self] in self?.scaleStop() }
        insights.variationReady = { [weak self] notes, end in self?.variationReady(notes, end: end) }
        insights.variationLeft = { [weak self] in self?.variationLeft() }
        insights.ideaKept = { [weak self] in self?.ideaKept() }
        insights.keepsIdeas = !piece.shownOnly
        wireRollSelection()
        wireRollPlayback()
        roll.noteEdited = { [weak self] edit in self?.noteEdited(edit) }
        roll.notesMoveBegan = { [weak self] in self?.notesMoveBegan() }
        roll.notesMoved = { [weak self] edits in self?.notesMoved(edits) }
        // The file is written whether or not the screen is still there: the callback holds the piece, not the workspace.
        score.checkModel.editsKept = { [piece] in ScoreWorkspace.editsKept(of: piece) }
        contents.changed = { [weak self] shelf in self?.bookmarksChanged(shelf) }
    }

    // MARK: - The score's page

    /// A tap on the page plays nothing (#338): the music starts only with Play or Loop. With the sheet open, a tap that
    /// selects nothing puts it away; the selection stays as it is (0018 section 6).
    func blankTapped() {
        change(.close(.insights))
        // In practice view the tap shows Play/Pause and Leave for a few seconds too (#533 11).
        score.practise(.tap)
    }

    /// The modes (version 0.5): a tap on a beat moves the black playhead there (the playback model, v0.5.2, T12), playing
    /// on from there when it plays, and Цикл stays as it is (`place`, answer 16). On Expand's card it goes there inside the
    /// passage, as on Expand's roll, and starts nothing (finding 2 of the 0.8 test, Andrei's answer 4 of 2026-11-18), the
    /// model outside Expand left as it is (T18). Notes are edited only in the roll (#479 item 23).
    func placeTapped(_ beat: Range<Double>) {
        if let passage = score.expandPassage {
            player.move(to: beat.lowerBound, play: false, within: passage)
        } else {
            player.apply(.place(beat.lowerBound))
        }
        score.practise(.tap)
    }

    /// The pill's assistant button (Andrei's 0.8 test, finding 8, and his card of 2026-11-18 21:49 UTC): the panel opens
    /// with the conversation it had when it was closed (`AssistantController.close` keeps it), or closes; only an Ask
    /// opens it empty (`AssistantController.open`). The panel opened puts the sheet away (`assistantPanelChanged`).
    func assistantPressed() {
        if assistant.panelOpen {
            assistant.close()
        } else {
            assistant.show()
        }
    }

    /// A flag tapped on the score opens Check on its bar.
    func checkPicked() {
        change(.open(.check))
    }

    /// «Источники» on a version's marker popover opens Sources.
    func versionPicked() {
        change(.open(.versions))
    }

    /// A version's bar heard on its own (a marker's «Как в версии»).
    func hearNotes(_ notes: [PlaybackNote], over range: Range<Double>) {
        player.hearAlone(notes, over: range)
    }

    /// The piece's notes changed (a correction, the tempo set): the roll, what plays and the facts follow.
    func notesChanged() {
        roll.notesChanged()
        let id = piece.id
        Task { [weak player] in await player?.reload(piece: id) }
        // A fixed note may be one a fact rested on while it was still to check (#352).
        Task { [weak insights] in await insights?.reread() }
    }

    /// The note edits saved, «Мои правки» switched and a correction taken out are written to the project file at once.
    static func editsKept(of piece: OpenPiece) {
        guard let file = piece.file else { return }
        let id = piece.id
        Task { try? await ProjectFiles.save(piece: id, to: file) }
    }

    /// The bookmarks are drawn as brackets on the score, and again whenever they change.
    func bookmarksChanged(_ shelf: BookmarkShelf?) {
        score.marksModel.showBookmarks(shelf)
    }

    // MARK: - Insights

    func placeChosen(_ tick: Double) {
        player.seek(to: tick)
    }

    func scaleReady(_ notes: [PlaybackNote], over range: Range<Double>) {
        player.hearAlone(notes, over: range)
    }

    func scaleStop() {
        player.stopAlone()
    }

    /// A way tried sounds at once, looping the selection to the variation's end, and the card in focus shows it as the
    /// ossia (0018 section 6).
    func variationReady(_ notes: [PlaybackNote], end: Double) {
        player.setVariation(notes)
        score.focusModel.showOssia(insights.trial?.way.label)
        if let range = score.focusModel.playRange?.range {
            player.hear(.variation, over: range.lowerBound..<max(range.upperBound, end))
        }
    }

    /// An idea kept is attached to its phrase's anchor, minted for it when the phrase had none (#318): the phrases are read
    /// again so the stream shows it on that phrase; their marks do not change.
    func ideaKept() {
        let id = piece.id
        Task { [weak score] in await score?.selectionModel.loadPhrases(of: id) }
    }

    /// Left, the original sounds again.
    func variationLeft() {
        player.setVariation(nil)
        score.focusModel.showOssia(nil)
        // In focus, another version takes the variation's place again.
        if let version = score.focusModel.version, version.sounds { player.setVariation(version.notes, version: true) }
    }

    // MARK: - The roll's notes

    func noteEdited(_ edit: RollNoteEdit) {
        score.checkModel.editNote(edit)
    }

    /// Picked notes dragged in Select (#601): the first step opens the note editor's draft, and the notes let go are its
    /// edits, kept until Cancel or Save in the roll's head.
    func notesMoveBegan() {
        if score.checkModel.noteEditor == nil { score.checkModel.beginNoteEdit() }
    }

    func notesMoved(_ edits: [RollNoteEdit]) {
        edits.forEach { score.checkModel.editNote($0) }
    }
}

/// One selection for the score, the roll, the pill and the loop strip (#479 item 23): what the roll picks is the score's
/// selection, by beats where the score selects by beats, and the roll shows it back with the score. Each change comes with
/// its gesture, for the selection's history (#511 11); the roll's hold and its hint are the score's too.
extension ScoreWorkspace {
    private func wireRollSelection() {
        roll.byBeats = { [weak self] in self?.rollByBeats ?? false }
        roll.picked = { [weak self] pick, gesture in self?.rollPicked(pick, gesture: gesture) }
        roll.cleared = { [weak self] gesture in self?.rollCleared(gesture: gesture) }
        roll.notesPicked = { [weak self] pick in self?.rollNotesPicked(pick) }
        // A span drawn there, notes or none (v0.6.4), and the score's tuplets for the roll's grid: one selection for both.
        roll.sharePlaces(with: score)
        roll.gestureCancelled = { [weak self] gesture in self?.rollGestureCancelled(gesture) }
        roll.holdHinted = { [weak self] in self?.rollHoldHinted() }
        score.penChanged = { [weak self] on in self?.penChanged(on) }
        score.rollEditing = { [weak self] in self?.rollEditing ?? false }
        score.toolChanged = { [weak self] mode, penOn in self?.toolChanged(mode, penOn: penOn) }
        roll.barTool = RollInput.barTool(mode: score.mode, penOn: score.penModel.penOn)
    }

    var rollByBeats: Bool { !score.beatLines.isEmpty }

    func rollPicked(_ pick: RollPick, gesture: String?) {
        switch pick {
        case let .bars(first, last, extend): score.selectBars(first, last, extend: extend, gesture: gesture)
        case let .beats(span): score.selectBeats(span.lowerBound, span.upperBound, gesture: gesture)
        }
    }

    func rollCleared(gesture: String?) {
        score.clearSelection(gesture: gesture)
    }

    /// Notes a lasso or a tap picked in the roll's Select (#601): the one selection, lit on the score too.
    func rollNotesPicked(_ pick: RollNotesPick) {
        score.selectNotes(pick.ids, add: pick.add, gesture: pick.gesture, replaces: pick.replaces)
    }

    func rollGestureCancelled(_ gesture: String) {
        score.revertGesture(gesture)
    }

    func rollHoldHinted() {
        score.handle(.holdHint)
    }

    /// The Pen's stylus changes nothing on the roll (#511 4.1, #521 10), and the Pen takes the roll out of Edit notes:
    /// one tool at a time.
    func penChanged(_ on: Bool) {
        roll.penOn = on
        if RollInput.rollNavigates(penOn: on, mode: roll.mode) { roll.choose(.navigate, score: score) }
    }

    var rollEditing: Bool { roll.shownMode == .edit }

    /// The bar's tool decides on the roll too (#543 2–4): only Select changes the selection there.
    func toolChanged(_ mode: InteractionMode, penOn: Bool) {
        roll.barTool = RollInput.barTool(mode: mode, penOn: penOn)
    }
}

/// Space and the playhead on the roll. The playhead moved there (v0.2) stays inside the loop, and a tap in a selection that
/// does not loop yet loops it; under the one playback position (v0.5.2) it goes anywhere, and so do taps on the loop strip
/// and the whole piece.
extension ScoreWorkspace {
    private func wireRollPlayback() {
        roll.spacePressed = { [weak self] in self?.spacePressed() }
        roll.playheadMoved = { [weak self] tick, play in self?.rollPlayheadMoved(to: tick, play: play) }
        score.playheadGestured = { [weak self] gesture in self?.scorePlayheadGestured(gesture) }
        roll.playheadGestured = { [weak self] gesture in self?.rollPlayheadGestured(gesture) }
    }

    func spacePressed() {
        if score.focusModel.focused, let passage = score.focusModel.playRange?.range {
            player.playPassage(passage)
        } else {
            player.apply(.press)
        }
    }

    /// The playhead moved on the roll (v0.2): it stays inside the loop, and a tap in a selection that does not loop yet loops
    /// it. Under the playback model (v0.5.2) it goes anywhere, as a tap on a beat does (T12): the loop strip and the whole piece
    /// move it too, and Цикл turns off when it leaves the selection (the loop strip only moves it inside, T13).
    func rollPlayheadMoved(to tick: Double, play: Bool) {
        if score.focusModel.focused, let passage = score.focusModel.playRange?.range {
            // In Expand within the passage, playing it round from there (#533 2).
            player.move(to: tick, play: play, within: passage)
        } else {
            player.go(to: tick, play: play)
        }
    }

    /// A playhead dragged by its handle on the score or the roll (#495 UX-08, T14–T16): the player runs the model. The pages
    /// send it only under the playback model, and a gesture that ends after the model went is still let go. On Expand's
    /// roll it is held inside the passage, which goes on looping (#533 2).
    /// On Expand's card the black playhead is held inside the passage too (v0.6.4, spec 8), as on its roll.
    func scorePlayheadGestured(_ gesture: PlayheadGesture) {
        if let passage = score.expandPassage {
            player.handle(gesture, within: passage)
        } else {
            player.handle(gesture)
        }
    }

    func rollPlayheadGestured(_ gesture: PlayheadGesture) {
        if score.focusModel.focused, let passage = score.focusModel.playRange?.range {
            player.handle(gesture, within: passage)
        } else {
            player.handle(gesture)
        }
    }
}
