import XCTest

@testable import MotiffLab

/// «Мои правки: вкл / выкл» and «Темп живого исполнения» on the iPad (v0.6.5, facade contract 12): the requests the iPad
/// builds, the state it reads from music-core's answers, and the layer kept in the piece's file through music-core itself.
final class EditsLayerTests: XCTestCase {
    private var opened: [Int32] = []

    override func tearDown() {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    // MARK: - Requests

    func testAProjectFileOpensWithTheLayerItsFileSays() {
        let file = OpenPiece.openRequest(language: "ru")
        XCTAssertEqual(file["edits"] as? String, "piece")
        XCTAssertEqual(file["language"] as? String, "ru")
        XCTAssertNil(file["transcription"], "since v1.1 a recording is added as it is, never heard into a new piece")
    }

    func testTheLayerAndDropRequestsCarryTheBarCheckListsOnlyWhenThereIsOne() {
        XCTAssertEqual(EditsLayerState.layerRequest(on: false, bar: nil) as NSDictionary, ["on": false] as NSDictionary)
        XCTAssertEqual(EditsLayerState.layerRequest(on: true, bar: 3) as NSDictionary, ["on": true, "bar": 3] as NSDictionary)
        XCTAssertEqual(EditsLayerState.dropRequest(["n_0007", "n_0009"], bar: nil) as NSDictionary, ["noteIds": ["n_0007", "n_0009"]] as NSDictionary)
        XCTAssertEqual(EditsLayerState.dropRequest(["n_0007"], bar: 2) as NSDictionary, ["noteIds": ["n_0007"], "bar": 2] as NSDictionary)
    }

    func testTheNoteEditorSendsTheSelectionsBarAndNoneWithoutOneSoAPickupIsNotRefused() throws {
        XCTAssertEqual(EditsLayerState.correctionsRequest(["id": "n1"], bar: 3) as NSDictionary, ["id": "n1", "bar": 3] as NSDictionary)
        XCTAssertEqual(EditsLayerState.correctionsRequest([:], bar: nil) as NSDictionary, [:] as NSDictionary, "never a guessed bar 1")

        // music-core answers a call without a bar for the piece's first one: bar 0 of a piece that starts with a pickup.
        let pickup = #"<?xml version="1.0"?><score-partwise version="3.1"><part-list><score-part id="P1"><part-name>P</part-name></score-part></part-list><part id="P1"><measure number="0" implicit="yes"><attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes><note><pitch><step>E</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note></measure><measure number="1"><note><pitch><step>A</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note></measure></part></score-partwise>"#
        let number = try piece(open("pickup.musicxml", Data(pickup.utf8), #"{"language": "en"}"#))
        let begun = try object(MusicCore.call(piece: number, "corrections.begin", facadeRequest(EditsLayerState.correctionsRequest([:], bar: nil))))
        XCTAssertEqual(begun["bar"] as? Int, 0, "\(begun)")
    }

    // MARK: - Reading the answers

    func testAnAnswerWithTheLayerOnListsWhatNoLongerFitsOneRowPerNote() throws {
        let answer = #"""
        {"bar": 1, "notes": [], "removed": [], "canUndo": false, "changed": false, "count": 5, "layer": "on", "editsOff": false, "applied": 2,
         "moved": [{"noteId": "n_0003", "to": "n_0004", "bar": 2}],
         "unfit": [{"noteId": "n_0010", "bar": 4, "reason": "missing", "text": "The note is not there"},
                   {"noteId": "n_0010", "bar": 4, "reason": "changed", "text": "The note is different now"},
                   {"noteId": "n_0010", "bar": 4, "reason": "changed", "text": "The note is different now"},
                   {"noteId": "n_0020", "bar": 7, "reason": "idTaken", "text": "A note with this id is already there"}],
         "hidden": []}
        """#
        let state = try XCTUnwrap(layer(answer))
        XCTAssertTrue(state.on)
        XCTAssertEqual(state.count, 5)
        XCTAssertEqual(state.moved, [EditsLayerState.Moved(noteId: "n_0003", to: "n_0004", bar: 2)])
        XCTAssertEqual(state.unfit.count, 4)
        XCTAssertEqual(state.unfitNotes, [
            EditsLayerState.UnfitNote(id: "n_0010", bar: 4, reasons: ["The note is not there", "The note is different now"]),
            EditsLayerState.UnfitNote(id: "n_0020", bar: 7, reasons: ["A note with this id is already there"]),
        ])
        XCTAssertTrue(state.shows)

        let text = UIText(language: "en")
        XCTAssertEqual(state.stateLine(text), [text.count("edits.count", 5), text.count("edits.unfit", 4), text.format("edits.moved", "1")].joined(separator: " · "))
    }

    func testOffSaysTheNotesAreAsRecognisedAndAStrictPieceIsOnWithNothingUnfit() throws {
        let off = try XCTUnwrap(layer(#"{"bar": 1, "notes": [], "removed": [], "canUndo": false, "changed": false, "count": 0, "layer": "off", "moved": [], "unfit": [], "hidden": []}"#))
        XCTAssertFalse(off.on)
        XCTAssertTrue(off.shows, "off, the switch stays to turn them on again")
        XCTAssertEqual(off.stateLine(UIText(language: "ru")), UIText(language: "ru")["edits.off"])

        let strict = try XCTUnwrap(layer(#"{"bar": 1, "notes": [], "removed": [], "canUndo": false, "changed": false, "count": 2}"#))
        XCTAssertEqual(strict, EditsLayerState(count: 2))
        XCTAssertTrue(strict.on)
        XCTAssertFalse(EditsLayerState(count: 0).shows, "no corrections, no switch")

        XCTAssertNil(layer(#"{"error": "unavailable", "message": "Corrections are off."}"#))
        XCTAssertEqual(EditsLayerState.opened(#"{"project": {"corrections": 3}}"#), EditsLayerState(count: 3))
        XCTAssertEqual(EditsLayerState.opened(#"{"edits": {"count": 1, "layer": "off"}, "project": {"corrections": 1}}"#).on, false)
    }

    // MARK: - Through music-core

    func testTurnedOffTheLayerIsKeptInTheFileAndRefusesCorrectionsUntilTurnedOn() throws {
        let example = try XCTUnwrap(KitPiece.example)
        let imported = try open(example.fileName, example.data, #"{"language": "en", "id": "p_edits"}"#)
        XCTAssertNil(imported["edits"], "a new import opens strictly")
        let first = try piece(imported)
        let bar = try object(MusicCore.call(piece: first, "corrections", #"{"bar": 1}"#))
        let note = try XCTUnwrap((bar["notes"] as? [[String: Any]])?.first?["id"] as? String)
        let removed = try XCTUnwrap(layer(MusicCore.call(piece: first, "corrections.remove", #"{"bar": 1, "id": "\#(note)"}"#)))
        XCTAssertEqual(removed, EditsLayerState(count: 1))

        let worked = MusicCore.call(piece: first, "project.save")
        let reopened = try open("elise.motiff.json", Data(worked.utf8), json(OpenPiece.openRequest(language: "en")))
        XCTAssertEqual((reopened["edits"] as? [String: Any])?["layer"] as? String, "on")
        XCTAssertEqual(try EditsLayerState.opened(json(reopened)).count, 1)
        XCTAssertTrue(try EditsLayerState.opened(json(reopened)).unfitNotes.isEmpty)

        let id = try piece(reopened)
        let off = try XCTUnwrap(layer(MusicCore.call(piece: id, "corrections.layer", json(EditsLayerState.layerRequest(on: false, bar: nil)))))
        XCTAssertFalse(off.on)
        XCTAssertEqual(off.count, 1, "the corrections are kept while off")
        let refused = try object(MusicCore.call(piece: id, "corrections.remove", #"{"bar": 1, "id": "\#(note)"}"#))
        XCTAssertEqual(refused["error"] as? String, "unavailable")

        let saved = MusicCore.call(piece: id, "project.save")
        let again = try open("elise.motiff.json", Data(saved.utf8), json(OpenPiece.openRequest(language: "en")))
        XCTAssertFalse(try EditsLayerState.opened(json(again)).on, "the piece opens as its file says")
        let on = try XCTUnwrap(layer(MusicCore.call(piece: piece(again), "corrections.layer", json(EditsLayerState.layerRequest(on: true, bar: 1)))))
        XCTAssertTrue(on.on)
        XCTAssertEqual(on.count, 1)
    }

    /// A corrections answer read as `PieceCore.corrections` reads it (`CorrectionsAnswer`), then its layer's state; nil for
    /// a refusal.
    private func layer(_ answer: String) -> EditsLayerState? {
        FacadeAnswer.decode(CorrectionsAnswer.self, answer, of: "corrections").map { EditsLayerState($0.edits) }
    }

    private func open(_ fileName: String, _ data: Data, _ request: String) throws -> [String: Any] {
        let answer = try object(MusicCore.open(fileName: fileName, data: data, request: request))
        XCTAssertNil(answer["error"], "\(answer)")
        let number = try piece(answer)
        opened.append(number)
        return answer
    }

    private func piece(_ answer: [String: Any]) throws -> Int32 {
        try XCTUnwrap((answer["piece"] as? NSNumber)?.int32Value)
    }

    private func object(_ text: String) throws -> [String: Any] {
        try XCTUnwrap(facadeObject(text), text)
    }

    private func json(_ fields: [String: Any]) throws -> String {
        String(decoding: try JSONSerialization.data(withJSONObject: fields), as: UTF8.self)
    }
}
