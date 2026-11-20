import XCTest

@testable import MotiffLab

/// The workspace's scenarios (the v0.7 blueprint's section 5.3 step 3): sessions on the score's screen, taken through
/// `ScoreWorkspace` as the view takes it (`WorkspaceSession`), each compared with its kept transcript
/// (`fixtures/workspace-*.txt`, read where they lie in the repository). They protect what docs/ux-baseline.md says of
/// the reactions between features, as far as the workspace decides it: PLAY-03 and PLAY-04 (Expand's passage, the loop
/// rule), ROLL-01 (nothing the app sends changes the roll's zoom), VIEW-05 and VIEW-06 (the panels through the
/// workspace and the assistant), VIEW-02 (nothing keeps a visited screen's workspace), of PLAY-02 the pages' playhead
/// gestures reaching the player, of PLAY-01 a tap on Expand's card leaving the selection and Цикл as they were, and of
/// PLAY-02 and PLAY-04 what the player's model decides while it plays. The screen's life and the views of the switch are
/// no entry of the baseline: their transcripts are kept here as the workspace's own. What a page, a gesture or the sound
/// decides stays with the page scenarios, the walk and the device. A transcript is what the app does today: a line in it
/// is not thereby approved.
@MainActor
final class WorkspaceScenarioTests: XCTestCase {
    /// The pieces opened in music-core for a test, and what it left on the device.
    private var opened: [Int32] = []
    private var files: [URL] = []
    private var views: [String] = []

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
        for file in files {
            try? FileManager.default.removeItem(at: file)
            SourcesHandover.drop(file)
        }
        files = []
        for id in views { UserDefaults.standard.removeObject(forKey: ViewStates.notesKey(id)) }
        views = []
    }

    /// The kit's piece, open in music-core; `fit` sets what the library would have said of it.
    private func kit(_ fit: (inout OpenPiece) -> Void = { _ in }) async throws -> OpenPiece {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        var piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        fit(&piece)
        return piece
    }

    /// A project file's place for a test, with nothing there yet.
    private func projectFile() -> URL {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("scenario-\(UUID().uuidString).motiff.json")
        files.append(file)
        return file
    }

    private static func bars(_ first: Int, _ last: Int) -> String {
        #"{"bars": {"first": \#(first), "last": \#(last)}}"#
    }

    /// The transcript against its kept file; the whole transcript is printed when they differ.
    private func compare(_ transcript: String, with name: String, file: StaticString = #filePath, line: UInt = #line) throws {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("fixtures/\(name).txt")
        let kept = try String(contentsOf: url, encoding: .utf8)
        if transcript != kept { print("The transcript of \(name):\n\(transcript)") }
        XCTAssertEqual(transcript.components(separatedBy: "\n"), kept.components(separatedBy: "\n"), "\(name).txt", file: file, line: line)
    }

    // MARK: - A session on the notes and the roll

    /// PLAY-04, PLAY-03, ROLL-01: a selection made where there was none turns Цикл on and a moved edge leaves it as the
    /// player set it; Expand shows the passage it was opened on, in the view it was in last, and Play there loops it
    /// whatever Цикл says; and through selecting, Цикл, a tap, the notes drawn again and Expand the roll is sent one
    /// zoom, the player's own, and after its first load only loads that keep the zoom and the view. PLAY-02, the
    /// workspace's part, nothing playing: a playhead taken on the score's page or the roll's reaches the player, and in
    /// Expand it is held inside the passage. PLAY-06: a tap on the score outside the selection leaves Цикл on.
    func testPLAY03PLAY04ROLL01ASessionOnTheNotesAndTheRoll() async throws {
        let session = await WorkspaceSession(piece: try await kit())
        let workspace = session.workspace
        let score = workspace.score
        await session.step("the screen opens, its sound not loaded") {
            workspace.wire()
            session.appear()
            workspace.applyModel()
        }
        await session.step("bars 1–2 are selected where nothing was") { await score.select(request: Self.bars(1, 2), step: false) }
        XCTAssertTrue(workspace.player.cycle)
        await session.step("Цикл is turned off") { workspace.player.apply(.cycle) }
        await session.step("the selection's end moves to bar 3") { await score.select(request: Self.bars(1, 3), step: false) }
        XCTAssertFalse(workspace.player.cycle, "a moved edge leaves Цикл as the player set it")
        await session.step("the selection is cleared") { score.clearSelection(step: false) }
        await session.step("bars 5–6 are selected where nothing was") { await score.select(request: Self.bars(5, 6), step: false) }
        XCTAssertTrue(workspace.player.cycle, "a selection made where there was none turns Цикл on")
        XCTAssertEqual(workspace.player.loop, 3120..<4560)
        await session.step("the roll's drawer opens") { workspace.change(.toggle(.roll)) }
        await session.step("the player zooms the roll in") { workspace.roll.zoomStep(.time, 1) }
        await session.step("the roll picks bars 7–8") { workspace.roll.picked?(.bars(first: 7, last: 8, extend: false), nil) }
        await session.step("a beat outside the selection is tapped on the score") { score.placeTapped?(240..<480) }
        XCTAssertEqual(workspace.player.loop, 4560..<6000, "PLAY-06: a tap outside the selection leaves Цикл on, the selection looping")
        await session.step("Цикл is turned off") { workspace.player.apply(.cycle) }
        await session.step("the piece's notes change") {
            let loads = session.rollLoads
            score.notesChanged?()
            await session.until("the roll loads the notes again") { session.rollLoads > loads }
        }
        await session.step("Expand on the roll's drawer") {
            workspace.change(.expandFromDrawer)
            score.expandRoll()
        }
        XCTAssertNil(workspace.player.loop, "Цикл is off as Expand opens")
        await session.step("Space in Expand") { workspace.roll.spacePressed?() }
        XCTAssertEqual(workspace.player.loop, 4560..<6000, "Play in Expand loops the passage, whatever Цикл says")
        XCTAssertEqual(workspace.player.selection, 4560..<6000)
        await session.step("Collapse") { score.leaveFocus() }
        XCTAssertNil(workspace.player.loop, "out of Expand, Цикл is as the player left it")
        await session.step("the roll's drawer closes") { workspace.change(.toggle(.roll)) }
        await session.step("Expand from the selection's menu") { score.enterFocus() }
        XCTAssertEqual(workspace.rollPlace, .focus, "Expand opens in the view it was in last")
        let held = { session.say("playhead held: \(Self.words(workspace.player.drag))") }
        await session.step("Expand's roll: the playhead is taken and dragged past the passage's end") {
            workspace.roll.playheadGestured?(.grab(.black))
            workspace.roll.playheadGestured?(.move(7000))
            held()
        }
        XCTAssertEqual(workspace.player.drag?.handle, .blue, "PLAY-02: on Expand's roll the playhead held is the passage's")
        XCTAssertEqual(workspace.player.drag?.last, 5999, "PLAY-02: held inside the passage, a tick before its end")
        await session.step("Expand's roll: the playhead is let go") {
            workspace.roll.playheadGestured?(.release)
            held()
        }
        await session.step("the score's page in Expand: its playhead is taken and dragged before the passage's start") {
            score.playheadGestured?(.grab(.black))
            score.playheadGestured?(.move(1000))
            held()
            score.playheadGestured?(.release)
        }
        await session.step("Back to page") { score.leaveFocus() }
        await session.step("the score: the playhead is taken and dragged") {
            score.playheadGestured?(.grab(.black))
            score.playheadGestured?(.move(5040))
            held()
        }
        XCTAssertEqual(workspace.player.drag, PlayheadDrag(handle: .black, resume: false, last: 5040), "PLAY-02: the score's handle reaches the player")
        await session.step("the score: the playhead is let go") {
            score.playheadGestured?(.release)
            held()
        }
        await session.step("the roll's drawer: the playhead is taken, dragged and let go") {
            workspace.roll.playheadGestured?(.grab(.black))
            workspace.roll.playheadGestured?(.move(4800))
            held()
            workspace.roll.playheadGestured?(.release)
        }

        try compare(session.transcript, with: "workspace-session")
        let roll = session.lines.filter { $0.hasPrefix("  roll ") }
        XCTAssertEqual(roll.filter { $0.hasPrefix("  roll zoom") }, ["  roll zoom: time 1"], "the one zoom is the player's")
        XCTAssertEqual(roll.filter { $0.hasPrefix("  roll load") }.dropFirst().filter { !$0.contains("kept") }, [], "a load after the first keeps the zoom")
    }

    /// A playhead held, as a transcript writes it.
    private static func words(_ drag: PlayheadDrag?) -> String {
        drag.map { "\($0.handle.rawValue) at \(Int($0.last)), \($0.resume ? "to play on" : "standing")" } ?? "none"
    }

    /// PLAY-02, what the player's model decides (the handle is the pages', the sound the device's): taken while it
    /// plays, playback stands; dragged either way, the notes passed are scrubbed; let go, it plays on from there.
    func testPLAY02APlayheadTakenWhilePlaying() {
        let playing = PlaybackModel.State(selection: nil, loop: true, tick: 600, transport: .playing)
        let taken = PlaybackModel.next(playing, .grab)
        XCTAssertEqual(taken.steps, [.pause], "playback stands while the playhead is held")
        let dragged = PlaybackModel.next(taken.state, .drag(.black, to: 1500))
        XCTAssertEqual(dragged.steps, [.scrub(from: 600, to: 1500)])
        let back = PlaybackModel.next(dragged.state, .drag(.black, to: 300))
        XCTAssertEqual(back.steps, [.scrub(from: 1500, to: 300)], "either way")
        let released = PlaybackModel.next(back.state, .release(.black, resume: true))
        XCTAssertEqual(released.steps, [.scrubEnd, .play], "on from where it is let go, without a restart")
        XCTAssertEqual(released.state, PlaybackModel.State(selection: nil, loop: true, tick: 300, transport: .playing))
    }

    /// PLAY-04, the part no workspace without sound reaches: playing with Цикл on, a new selection that does not hold
    /// the playing position takes playback to its start and loops it, Цикл staying on; one that holds it plays on.
    func testPLAY04ANewSelectionWhilePlayingWithCycleOn() {
        let playing = PlaybackModel.State(selection: 240..<1680, loop: true, tick: 600, transport: .playing)
        let away = PlaybackModel.next(playing, .select(3120..<4560))
        XCTAssertEqual(away.steps, [.loop(3120..<4560), .seek(3120)])
        XCTAssertEqual(away.state, PlaybackModel.State(selection: 3120..<4560, loop: true, tick: 3120, transport: .playing))
        let holding = PlaybackModel.next(playing, .select(240..<2400))
        XCTAssertEqual(holding.steps, [.loop(240..<2400)])
        XCTAssertEqual(holding.state, PlaybackModel.State(selection: 240..<2400, loop: true, tick: 600, transport: .playing))
    }

    /// PLAY-01, the workspace's part (the page's is score-expand-tap.json): in Expand a tap on a beat of the card, as the
    /// page posts it (`place`), also one past the passage, makes no lead-in out of the passage, and the page's selection,
    /// Цикл and Expand's passage stay as they were. Where the playhead goes and that nothing starts playing are the
    /// engine's, heard on the device (no sound is loaded here).
    func testPLAY01ATapOnExpandsCardLeavesTheSelectionAndCycleAsTheyWere() async throws {
        let session = await WorkspaceSession(piece: try await kit())
        let workspace = session.workspace
        let score = workspace.score
        await session.step("the screen opens, its sound not loaded") {
            workspace.wire()
            session.appear()
            workspace.applyModel()
        }
        await session.step("bars 5–6 are selected where nothing was") { await score.select(request: Self.bars(5, 6), step: false) }
        await session.step("Expand from the selection's menu") { score.enterFocus() }
        let passage = try XCTUnwrap(score.expandPassage, "Expand opens on the selection")
        XCTAssertEqual(passage, 3120..<4560)
        await session.step("a beat of the card is tapped") { score.placeTapped?(3840..<4080) }
        await session.step("a beat past the passage is tapped") { score.placeTapped?(4800..<5040) }
        XCTAssertFalse(workspace.player.leadIn, "no lead-in out of the passage")
        XCTAssertTrue(workspace.player.cycle, "Цикл as it was")
        XCTAssertEqual(workspace.player.selection, passage, "the page's selection as it was")
        XCTAssertEqual(score.expandPassage, passage, "Expand still on its passage")
    }

    // MARK: - The panels

    /// VIEW-05 and VIEW-06 through the workspace: More → «Спросить о пьесе» over Sources over Insights leaves the
    /// assistant's panel open and no sheet; a place opened beside one that More opened has no way back to More.
    func testVIEW05VIEW06ThePanelsThroughTheWorkspace() async throws {
        let session = await WorkspaceSession(piece: try await kit())
        let workspace = session.workspace
        await session.step("the screen opens, its sound not loaded") {
            workspace.wire()
            session.appear()
        }
        await session.step("Insights opens") { workspace.change(.toggle(.insights)) }
        await session.step("Sources opens over Insights") { workspace.change(.open(.versions)) }
        await session.step("More opens") { workspace.change(.toggle(.more)) }
        await session.step("«Спросить о пьесе» in More") {
            workspace.change(.close(.more))
            workspace.assistant.open(wholePiece: true, selection: workspace.score.selectionModel.selection)
        }
        XCTAssertTrue(workspace.assistant.panelOpen, "VIEW-05: the assistant's panel stays")
        XCTAssertFalse(workspace.panels.sheetOpen, "VIEW-05: Sources closes and Insights does not come back")

        await session.step("the assistant's panel closes") { workspace.assistant.close() }
        await session.step("More opens") { workspace.change(.toggle(.more)) }
        await session.step("Check in More") { workspace.change(.openFromMore(.check)) }
        await session.step("a version's marker on the score opens Sources") { workspace.score.versionPicked?(1) }
        XCTAssertTrue(workspace.panels[.versions])
        XCTAssertFalse(workspace.panels.fromMore.contains(.versions), "VIEW-06: Sources opened by a marker shows no «‹ Ещё»")
        await session.step("Sources closes") { workspace.change(.close(.versions)) }
        await session.step("More opens") { workspace.change(.toggle(.more)) }
        await session.step("Sources in More") { workspace.change(.openFromMore(.versions)) }
        await session.step("a flag on the score opens Check") { workspace.score.checkPicked?(3) }
        XCTAssertTrue(workspace.panels[.check])
        XCTAssertFalse(workspace.panels.fromMore.contains(.check), "VIEW-06: Check opened by a flag shows no «‹ Ещё»")

        try compare(session.transcript, with: "workspace-panels")
    }

    // MARK: - The screen's life

    /// What a landing is called in a transcript.
    private static func words(_ landing: Landing) -> String {
        switch landing {
        case .page(let page): "page \(page)"
        case .tick(let tick): "tick \(Int(tick))"
        case .kept: "the view kept"
        }
    }

    /// What the view's three tasks do as the screen appears, one after another: `appeared`, `loadOriginals`,
    /// `loadSources`; `reopen` is the library's way to open the piece again in the screen's place.
    private func appear(_ session: WorkspaceSession) async {
        session.appear()
        await session.workspace.appeared()
        await session.workspace.loadOriginals()
        await session.workspace.loadSources(reopen: { session.say("the library opens the piece again at \(Self.words($0))") }, pick: { _ in })
    }

    /// What is on the device and in the workspace after a step: the project file, the resume point, the "Notes" view.
    private func kept(_ session: WorkspaceSession, _ file: URL, _ view: String) {
        session.say("project file: \(FileManager.default.fileExists(atPath: file.path) ? "written" : "not written")")
        session.say("resume point: \(session.workspace.resumeId == nil ? "none" : "kept")")
        guard let notes = ViewStates.notes(view) else { return session.say("view kept: none") }
        let selection = notes.selection.map { "\(Int($0.onset))–\(Int($0.end))" } ?? "none"
        let roll = "roll \(notes.rollOpen ? "open" : "closed"), its frame \(notes.roll == nil ? "none" : "kept")"
        session.say("view kept: selection \(selection), cycle \(notes.loop ? "on" : "off"), \(roll)")
    }

    /// When the project file was last written: a step that writes it waits for that (`WorkspaceSession.until`).
    private static func written(_ file: URL) -> Date? {
        (try? FileManager.default.attributesOfItem(atPath: file.path))?[.modificationDate] as? Date
    }

    /// Three visits to one piece, as the library makes them: opened and left (the resume point, the "Notes" view and
    /// the project file written as the app and the screen go), opened again where it was left and handed to its PDF,
    /// and back from the PDF with the view kept, then handed over for a new interface language.
    func testTheScreensLifeOverThreeVisits() async throws {
        let file = projectFile()
        let first = try await kit { $0.file = file }
        let view = try XCTUnwrap(first.own?.id, "music-core says the project's id as it opens the piece")
        views.append(view)

        var session = await WorkspaceSession(piece: first)
        var workspace = session.workspace
        await session.step("the screen appears") { await self.appear(session) }
        XCTAssertEqual(workspace.score.beatLines, workspace.player.beatLines, "the sound is read before the model is applied")
        await session.step("the piece's first notes are selected") {
            await workspace.score.select(request: #"{"span": {"onset": 0, "end": 1200}}"#, step: false)
        }
        await session.step("the app is no longer in front") {
            workspace.scenePhaseChanged(.inactive)
            await session.quiet()
            self.kept(session, file, view)
        }
        await session.step("the app goes to the background") {
            workspace.scenePhaseChanged(.background)
            await session.until("the project file is written") { Self.written(file) != nil }
            await session.quiet()
            self.kept(session, file, view)
        }
        await session.step("the app comes back") { workspace.scenePhaseChanged(.active) }
        await session.step("the piece changes in its originals") { workspace.originals.changed?() }
        await session.step("the Sources sheet opens the piece again in its place") { await workspace.sources.reopen?() }
        await session.step("the screen goes") {
            let before = Self.written(file)
            workspace.disappeared()
            session.say("the Sources sheet's way back: \(workspace.sources.reopen == nil ? "let go" : "held")")
            await session.until("the project file is written as the screen goes") { Self.written(file) != before }
        }
        var transcript = "# The first visit\n" + session.transcript

        var second = first
        second.other = OtherView(file: projectFile(), pages: PageMap(starts: [0, 2400]))
        session = await WorkspaceSession(piece: second)
        workspace = session.workspace
        await session.step("the screen appears") { await self.appear(session) }
        await session.step("bars 5–6 are selected") { await workspace.score.select(request: Self.bars(5, 6), step: false) }
        await session.step("the roll's drawer opens") { workspace.change(.toggle(.roll)) }
        var atPdf = false
        await session.step("PDF in the bar") {
            let toPdf = workspace.toPdf { landing in
                session.say("the library opens the PDF at \(Self.words(landing))")
                await ClosedPieces.close(second.id)
                atPdf = true
            }
            XCTAssertNotNil(toPdf, "the piece has a PDF to go to")
            let before = Self.written(file)
            toPdf?()
            await session.until("the piece is saved and handed to its PDF") { atPdf }
            session.say("project file: \(Self.written(file) != before ? "written again" : "not written again")")
        }
        await session.step("the screen goes") { workspace.disappeared() }
        kept(session, file, view)
        transcript += "\n# The second visit, where the first was left\n" + session.transcript

        var third = try await OpenPiece.open(fileName: file.lastPathComponent, data: try Data(contentsOf: file), language: "en", title: first.title).get()
        opened.append(third.id)
        third.file = file
        third.landing = .kept
        let reopened = third.id
        XCTAssertEqual(third.own?.id, view, "the project file keeps the project's id")
        session = await WorkspaceSession(piece: third)
        workspace = session.workspace
        await session.step("the screen appears, back from the PDF") {
            let point = ResumePlace(answer: await MusicCore.run { MusicCore.call(piece: reopened, "resume") })?.passage
            session.say("the file's resume point: \(point.map { "\(Int($0.lowerBound))–\(Int($0.upperBound))" } ?? "none")")
            await self.appear(session)
        }
        XCTAssertTrue(workspace.player.cycle, "PLAY-06: back from the PDF, Цикл as it was, though the position kept is outside the selection")
        XCTAssertEqual(workspace.player.loop, 3120..<4560, "PLAY-06: the selection loops again")
        await session.step("the selection is cleared") { workspace.score.clearSelection(step: false) }
        await session.step("the interface's language changes") {
            let reopen = await workspace.handOverForLanguage()
            session.say("the piece is to open again at \(reopen.map { Self.words($0.landing) } ?? "nothing")")
        }
        await session.step("the screen goes") { workspace.disappeared() }
        transcript += "\n# The third visit, back from the PDF\n" + session.transcript

        try compare(transcript, with: "workspace-life")
    }

    /// The views of the switch: a version picked opens Sources and is the one compared on the score (its page in Sources
    /// went with the bar-by-bar merge in v1.1, PR #738), a recording is heard from under the bar, and the working notes are
    /// this screen, the recording still heard over them (v0.10, Andrei's finding 24).
    func testAViewPickedInTheSwitch() async throws {
        let example = try XCTUnwrap(KitPiece.example)
        let session = await WorkspaceSession(piece: try await kit())
        let workspace = session.workspace
        let pick: (SourceLayers.ViewEntry) -> Void = { view in
            workspace.pickView(view, switchView: nil, text: UIText.current) { _ in session.say("a view is shown over the piece") }
            session.say("the recording heard: \(workspace.sources.performance ?? "none")")
        }
        await session.step("the screen opens, its sound not loaded") {
            workspace.wire()
            session.appear()
        }
        await session.step("the piece itself is added as another version, and the sources are read") {
            await workspace.score.addVersion(fileName: example.fileName, data: example.data)
            await workspace.loadSources(reopen: nil, pick: pick)
        }
        let version = try XCTUnwrap(workspace.sources.layers.views.first { $0.kind == "notes" && !$0.working }, "the version is a view of the switch")
        await session.step("the version is picked in the switch") { pick(version) }
        XCTAssertTrue(workspace.panels[.versions])
        await session.step("a recording is picked") {
            pick(SourceLayers.ViewEntry(id: "r1", kind: "performance", group: nil, working: false, hidden: false, op: "recording"))
        }
        let working = try XCTUnwrap(workspace.sources.layers.views.first { $0.working })
        await session.step("the working notes are picked") { pick(working) }

        try compare(session.transcript, with: "workspace-views")
    }

    // MARK: - Nothing keeps a visited screen

    /// VIEW-02, the shape of its cause (every screen visited stayed alive, v0.6.4 spec section 11): once the screen
    /// lets its workspace go, nothing holds it. Not the controllers' callbacks (`wire`), not the originals' word that
    /// the piece changed (`loadOriginals`), not the Sources sheet's way back (`loadSources`).
    func testVIEW02NothingKeepsAVisitedScreensWorkspace() async throws {
        let piece = try await kit()
        weak var held: ScoreWorkspace?
        do {
            let workspace = ScoreWorkspace(piece: piece)
            workspace.wire()
            await workspace.loadOriginals()
            await workspace.loadSources(reopen: { _ in }, pick: { _ in })
            XCTAssertNotNil(workspace.originals.changed)
            XCTAssertNotNil(workspace.sources.reopen)
            held = workspace
        }
        for _ in 0..<50 where held != nil {
            try await Task.sleep(for: .milliseconds(100))
        }
        XCTAssertNil(held)
    }
}
