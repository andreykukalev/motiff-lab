import SwiftUI

/// The screen's life (ScoreWorkspace): what happens as it appears, as the app leaves it and comes back, and as it goes.
/// Each is called in one line from the modifier of `ScoreScreenContent.wired` that had its body (`.task`,
/// `.onDisappear`, `.onChange(of: scenePhase)`).
extension ScoreWorkspace {
    /// The controllers get their callbacks (`wire`), then the piece's bookmarks, conversations and sound are read and the
    /// player is back where the piece was left. The screen's `.task`.
    func appeared() async {
        wire()
        await contents.load(piece: piece.id)
        assistant.load(piece: piece.own?.id ?? piece.file?.lastPathComponent ?? piece.title, score: score)
        await player.load(piece: piece.id, key: piece.own?.id); ProbeCounts.shared.mark("sound")
        applyModel()
        await resume()
        await returnToNotesView()
    }

    /// The screen goes (its `.onDisappear`): playback stops, and the "Notes" view, the resume point and the project file
    /// are written, unless the piece was handed to its other view.
    func disappeared() {
        // The Sources sheet's way back held this screen, and with it every controller and web view of it (v0.6.4
        // spec section 11: each visit with the sources on stayed in memory).
        sources.reopen = nil
        let now = resumeNow()
        let view = notesViewNow()
        // Handed to its other view, the piece was saved and closed already (#511 section 10); the metronome goes on.
        let handed = ClosedPieces.take(piece.id)
        if !handed { Metronome.shared.stop() }
        player.shutdown()
        guard !handed else { return }
        // Not handed over: the recording heard goes with the screen (v0.10 s-2 review).
        sources.leave(piece.file)
        // A variation only tried is not kept with the piece: it is left before the project file is written.
        let file = piece.file
        let id = piece.id
        let tried = insights
        Task {
            await keepNotesView(view)
            await keepResume(now)
            await tried.leave()
            if let file { try? await ProjectFiles.save(piece: id, to: file) }
        }
    }

    /// The app left the screen or came back to it (the reaction on the view's `scenePhase`, its new value).
    func scenePhaseChanged(_ phase: ScenePhase) {
        // The project file is written whenever the app leaves the screen, so nothing waits for a Save button.
        if phase == .background { save() }
        // The iPad's setting «Only draw with Apple Pencil» may have changed meanwhile.
        if phase == .active {
            score.applyInput()
            roll.applyInput()
        }
    }

    /// The project file written whenever the app leaves the screen, with the resume point and the "Notes" view.
    func save() {
        guard let file = piece.file else { return }
        let id = piece.id
        let now = resumeNow()
        let view = notesViewNow()
        // The piece's file first, with background time (review 3); the view kept on the device after it, as it waits for
        // the score's page.
        BackgroundTime.run { [self] in
            await keepResume(now)
            try? await ProjectFiles.save(piece: id, to: file)
            await keepNotesView(view)
        }
    }
}

/// At the instrument, on the score: resume where the player stopped (#327).
extension ScoreWorkspace {
    /// Back where the player stopped (#327): the project's latest resume point, the web app's too, with its passage, focus,
    /// tempo, loop and hands. Nothing starts playing.
    private func resume() async {
        // From the PDF in PDF | Notes the notes open at the page the player came from instead.
        guard piece.landing == nil else { return }
        let id = piece.id
        let answer = await MusicCore.run { MusicCore.call(piece: id, "resume") }
        guard let place = ResumePlace(answer: answer) else { return }
        resumeId = place.id
        player.tempoPercent = place.tempoPercent
        if player.handsHeld.canSeparate { player.hands = place.hands }
        await score.arrive(at: place)
        if place.loop, let range = score.focusModel.playRange?.range ?? place.passage {
            player.setLoop(range, play: false)
        }
    }

    /// Where the player is now, taken before the player stops.
    func resumeNow() -> ResumePlace.Now? {
        ResumePlace.Now(
            passage: score.selectionModel.selection.map { $0.onset..<$0.end },
            tick: player.tick,
            focused: score.focusModel.focused,
            tempoPercent: player.tempoPercent,
            hands: player.hands,
            loop: player.loop != nil
        )
    }

    /// Keeps where the player is as the project's resume point (`resume.save`), before the project file is written.
    func keepResume(_ now: ResumePlace.Now?) async {
        guard let now else { return }
        let id = piece.id
        var at = now.tick
        if now.passage == nil, let inView = await score.tickInView() { at = inView }
        let request = ResumePlace.saveRequest(
            id: resumeId, passage: now.passage, at: at, focused: now.focused, tempoPercent: now.tempoPercent, hands: now.hands, loop: now.loop, now: Date()
        )
        let answer = await MusicCore.run { MusicCore.call(piece: id, "resume.save", request) }
        if let saved = facadeObject(answer)?["saved"] as? [String: Any], let point = ResumePlace(point: saved) {
            resumeId = point.id
        }
    }
}

/// PDF | Notes on the score's screen (#511 section 10): the "Notes" view kept on the device as it goes (to the PDF, the
/// library or the background) and brought back when the player comes back to it from the PDF (`Landing.kept`): the
/// selection, also a bar with no notes, where the score was scrolled to and its zoom, where the music stood and whether
/// Цикл looped it, and the roll's drawer with its zoom and view (ViewStates.swift). Playback stops as the PDF opens, which
/// has its own metronome; nothing starts playing on the way back.
extension ScoreWorkspace {
    /// The piece's id, by which its views are kept on the device.
    private var viewKey: String? { piece.own?.id }

    /// The "Notes" view as it is now, read before the player stops; where the score and the roll are is read as it is kept.
    func notesViewNow() -> NotesViewState {
        var state = NotesViewState()
        state.selection = score.selectionModel.selection.map { SelectionSpanKept($0) }
        state.position = player.tick
        state.loop = player.cycle && score.selectionModel.selection != nil
        state.rollOpen = panels[.roll]
        return state
    }

    /// Keeps the "Notes" view on the device, with where the score is scrolled to and the roll's frame; what cannot be read
    /// now (the score's page already gone, the roll never opened) stays as it was kept before.
    func keepNotesView(_ now: NotesViewState) async {
        guard let id = viewKey else { return }
        let before = ViewStates.notes(id)
        var state = now
        if let shown = await score.viewPlace(), let place = shown.place {
            state.place = place
            state.zoom = shown.zoom
            state.left = shown.left
        } else if let before {
            state.place = before.place
            state.zoom = before.zoom
            state.left = before.left
        }
        state.roll = await roll.frame() ?? RollFrameHandoff.waiting(for: piece.id) ?? before?.roll
        ViewStates.keep(state, id)
    }

    /// Back from the PDF: the "Notes" view as the player left it, in place of the page the PDF was on. Nothing plays.
    func returnToNotesView() async {
        guard piece.landing == .kept else { return }
        // The pages were held out of sight for this (#533 6): they show once they stand where they were left, or now.
        defer { score.land() }
        guard let id = viewKey, let kept = ViewStates.notes(id) else { return }
        // The roll takes the frame as it first loads, whenever the drawer opens.
        RollFrameHandoff.give(kept.roll, to: piece.id)
        guard await score.whenRendered() else { return }
        if let span = kept.selection, span.end > span.onset {
            await score.select(request: span.span.request, step: false, picked: span.picked != nil)
        }
        // The model takes the selection now (T9), then Цикл as it was and the position, which leaves Цикл as it is (T7,
        // `place`, answer 14).
        player.hold(selection: score.focusModel.playRange?.range)
        if player.cycle != kept.loop { player.apply(.cycle) }
        player.apply(.place(kept.position))
        change(kept.rollOpen ? .open(.roll) : .close(.roll))
        await score.showPlace(kept.place, zoom: kept.zoom, left: kept.left)
    }

    /// The PDF where the player left it, or the first time at the page the notes at the top of the view were read from (the
    /// web app's `ShowPdfAsync`), once the "Notes" view, the resume point and the piece are saved: the library closes the
    /// piece as the PDF opens (ClosedPieces). `switchView` is the screen's way to the piece's other view.
    func showPdf(_ switchView: ((Landing) async -> Void)?) async {
        guard let switchView, !handingOver else { return }
        handingOver = true
        defer { handingOver = false }
        let pages = piece.other?.pages ?? PageMap(starts: score.sourcesModel.info.document?.starts ?? [])
        let tick = await score.firstTick(onPage: score.pages.first) ?? 0
        await keepForHandOver()
        await switchView(.page(pages.page(of: tick)))
    }

    /// A new interface language (InterfaceLanguage): the piece saved as for the PDF, closed, and opened again in the
    /// "Notes" view kept now, or where the view is in the music; nil without a project file or while handed over already.
    func handOverForLanguage() async -> InterfaceLanguage.Reopen? {
        guard let file = piece.file, !handingOver else { return nil }
        handingOver = true
        let at = await score.tickInView() ?? 0
        await keepForHandOver()
        Metronome.shared.stop()
        await ClosedPieces.close(piece.id)
        return InterfaceLanguage.Reopen(file: file, landing: .tick(at))
    }

    /// The "Notes" view, the resume point and the project file, a variation only tried left first, as the piece goes to
    /// another screen; playback stops.
    private func keepForHandOver() async {
        let now = resumeNow()
        let view = notesViewNow()
        player.shutdown()
        await keepNotesView(view)
        await keepResume(now)
        await insights.leave()
        if let file = piece.file { try? await ProjectFiles.save(piece: piece.id, to: file) }
    }
}
