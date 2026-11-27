import XCTest

@testable import MotiffLab

/// CheckModel (S6, ADR 0031 section 3, v1.6 i-8) against a fake score: the drawer opened or closed asks the page to hide
/// again once per change; with «Мои правки» off, Fix notes, the note editor and the assistant's drafts say why and open
/// nothing; with no piece nothing is asked of music-core; Check is read for a piece and its flags drawn once; the note
/// editor's draft opens, changes the notes, and goes on Cancel or is kept on Save. What ScoreController did before the move,
/// rule for rule.
@MainActor
final class CheckModelTests: XCTestCase {
    /// The score as Check sees it: what was asked of it, in order.
    @MainActor
    private final class FakeScore: CheckScore {
        var pieceId: Int32?
        private(set) var asked: [String] = []
        private(set) var shown: [Range<Double>] = []
        private(set) var notices: [String] = []
        private(set) var problems: [String?] = []

        func showSpan(_ span: Range<Double>) {
            shown.append(span)
        }

        func drawChecks() async {
            asked.append("drawChecks")
        }

        func applyLayers() async {
            asked.append("applyLayers")
        }

        func notesDidChange() async {
            asked.append("notesDidChange")
        }

        func showNotice(_ key: String) {
            notices.append(key)
        }

        func showProblem(_ message: String?) {
            problems.append(message)
        }

        func count(_ what: String) -> Int { asked.filter { $0 == what }.count }
    }

    private var opened: [Int32] = []

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    /// A model over a score `fake`, scan jobs off.
    private func made(_ fake: FakeScore) -> CheckModel {
        let model = CheckModel(selection: SelectionModel()) { false }
        model.score = fake
        return model
    }

    /// «Мои правки» off, as a piece's file can have it.
    private var editsOff: EditsLayerState {
        var edits = EditsLayerState(count: 1)
        edits.on = false
        return edits
    }

    /// The kit's piece open in music-core.
    private func kitPiece() async throws -> Int32 {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        return piece.id
    }

    /// Waits, up to five seconds, for what a call started in its own task.
    private func until(_ what: String, _ done: () -> Bool) async {
        for _ in 0..<500 where !done() {
            try? await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertTrue(done(), what)
    }

    func testTheDrawerOpenedOrClosedAsksThePageToHideAgainOncePerChange() async {
        let fake = FakeScore()
        let model = made(fake)
        model.drawerOpen = true
        await until("the page hides again") { fake.count("applyLayers") == 1 }
        model.drawerOpen = true
        model.drawerOpen = false
        await until("and again once closed") { fake.count("applyLayers") == 2 }
        try? await Task.sleep(for: .milliseconds(50))
        XCTAssertEqual(fake.count("applyLayers"), 2, "the same value asks nothing")
    }

    func testWithMyCorrectionsOffFixNotesTheNoteEditorAndTheAssistantsDraftsSayWhyAndOpenNothing() async {
        let fake = FakeScore()
        fake.pieceId = 7
        let model = made(fake)
        model.open(edits: editsOff)
        XCTAssertFalse(model.editsOn)
        model.fixCheckBar(2)
        model.beginNoteEdit()
        XCTAssertEqual(fake.notices, ["padEdits.offNotice", "padEdits.offNotice"])
        XCTAssertNil(model.corrections)
        XCTAssertNil(model.noteEditor)
        let accepted = await model.acceptAssistantDraft("{}")
        let undone = await model.undoAssistantDraft(firstBar: 2)
        XCTAssertEqual(accepted, UIText.current["edits.off"])
        XCTAssertEqual(undone, UIText.current["edits.off"])
        XCTAssertEqual(fake.asked, [], "neither the page nor the notes")
    }

    func testWithNoPieceNothingIsAskedOfMusicCore() async {
        let fake = FakeScore()
        let model = made(fake)
        XCTAssertTrue(model.editsOn, "on until a piece says otherwise")
        let accepted = await model.acceptAssistantDraft("{}")
        XCTAssertEqual(accepted, "", "no piece: no words of music-core")
        model.resolveCheck(1, true)
        model.beginNoteEdit()
        model.cancelNoteEdit()
        try? await Task.sleep(for: .milliseconds(50))
        XCTAssertEqual(fake.asked, [])
        XCTAssertEqual(fake.problems, [])
    }

    func testCheckIsReadForAPieceAndItsFlagsDrawnOnce() async throws {
        let fake = FakeScore()
        let model = made(fake)
        let piece = try await kitPiece()
        fake.pieceId = piece
        await model.load(in: piece)
        XCTAssertEqual(fake.asked, ["drawChecks"])
        XCTAssertFalse(model.check.hasFlags, "notes imported from MusicXML have no doubt")
        XCTAssertTrue(model.flags(UIText.current).isEmpty)
        XCTAssertNil(model.scanReview, "scan jobs off: nothing kept of a scan")
        model.showCheckBar(3)
        XCTAssertEqual(fake.shown, [], "a bar with no flag shows nothing")
    }

    func testTheNoteEditorsDraftChangesTheNotesAndGoesOnCancelOrIsKeptOnSave() async throws {
        let fake = FakeScore()
        let model = made(fake)
        var kept = 0
        model.editsKept = { kept += 1 }
        fake.pieceId = try await kitPiece()

        model.beginNoteEdit()
        XCTAssertEqual(model.noteEditor, NoteEditorState(), "open at once, before music-core answers")
        // Each music-core call runs on a thread of its own: the draft is begun before a note is added on it.
        try await Task.sleep(for: .milliseconds(300))
        model.editNote(.add(onset: 0, pitch: 60, length: 480))
        await until("the note added changed the notes") { fake.count("notesDidChange") == 1 }
        XCTAssertEqual(model.noteEditor?.changed, true)
        XCTAssertNotNil(model.noteEditor?.picked, "the note added is the one picked")
        model.cancelNoteEdit()
        XCTAssertNil(model.noteEditor)
        await until("the draft gone changed the notes back") { fake.count("notesDidChange") == 2 }
        XCTAssertEqual(kept, 0, "nothing written on Cancel")

        model.beginNoteEdit()
        try await Task.sleep(for: .milliseconds(300))
        model.saveNoteEdit()
        XCTAssertNil(model.noteEditor)
        await until("Save writes the project file") { kept == 1 }
        XCTAssertEqual(fake.count("notesDidChange"), 2, "a draft with no change keeps the notes as they are")
        XCTAssertEqual(fake.problems, [])
    }
}
