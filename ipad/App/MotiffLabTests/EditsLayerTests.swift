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

    func testAProjectFileOpensWithTheLayerItsFileSaysAndARecordingAsALiveTempoTranscription() throws {
        let file = OpenPiece.openRequest(language: "ru")
        XCTAssertEqual(file["edits"] as? String, "piece")
        XCTAssertEqual(file["language"] as? String, "ru")
        XCTAssertNil(file["transcription"])

        let heard = HeardRecording(name: "Concert", engine: "transkun", milliseconds: 120_000, parts: [(index: 0, midi: Data([1, 2, 3]))], levels: [4, 5])
        let recorded = OpenPiece.openRequest(language: "en", heard: heard)
        XCTAssertNil(recorded["edits"], "a new piece from a recording has no corrections to lay")
        let transcription = try XCTUnwrap(recorded["transcription"] as? [String: Any])
        XCTAssertEqual(transcription["tempo"] as? String, "live")
        XCTAssertNil(transcription["starts"], "the iPad does not know where the pieces begin")
        XCTAssertNil(transcription["hint"], "a new piece has no score to hint at its metre and tempo")
        XCTAssertEqual(transcription["milliseconds"] as? Int64, 120_000)
        XCTAssertEqual((transcription["parts"] as? [[String: Any]])?.first?["midi"] as? String, Data([1, 2, 3]).base64EncodedString())
        // A version from a recording is lined up with the score (`performance.add`): no bars are found there.
        XCTAssertNil(heard.request["tempo"])
    }

    func testTheLayerAndDropRequestsCarryTheBarCheckListsOnlyWhenThereIsOne() {
        XCTAssertEqual(EditsLayerState.layerRequest(on: false, bar: nil) as NSDictionary, ["on": false] as NSDictionary)
        XCTAssertEqual(EditsLayerState.layerRequest(on: true, bar: 3) as NSDictionary, ["on": true, "bar": 3] as NSDictionary)
        XCTAssertEqual(EditsLayerState.dropRequest(["n_0007", "n_0009"], bar: nil) as NSDictionary, ["noteIds": ["n_0007", "n_0009"]] as NSDictionary)
        XCTAssertEqual(EditsLayerState.dropRequest(["n_0007"], bar: 2) as NSDictionary, ["noteIds": ["n_0007"], "bar": 2] as NSDictionary)
    }

    // MARK: - Reading the answers

    func testAnAnswerWithTheLayerOnListsWhatNoLongerFitsOneRowPerNote() throws {
        let answer = #"""
        {"bar": 1, "notes": [], "removed": [], "count": 5, "layer": "on", "editsOff": false, "applied": 2,
         "moved": [{"noteId": "n_0003", "to": "n_0004", "bar": 2}],
         "unfit": [{"noteId": "n_0010", "bar": 4, "reason": "missing", "text": "The note is not there"},
                   {"noteId": "n_0010", "bar": 4, "reason": "changed", "text": "The note is different now"},
                   {"noteId": "n_0010", "bar": 4, "reason": "changed", "text": "The note is different now"},
                   {"noteId": "n_0020", "bar": 7, "reason": "idTaken", "text": "A note with this id is already there"}],
         "hidden": []}
        """#
        let state = try XCTUnwrap(EditsLayerState(answer: answer))
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
        let off = try XCTUnwrap(EditsLayerState(answer: #"{"bar": 1, "count": 0, "layer": "off", "moved": [], "unfit": [], "hidden": []}"#))
        XCTAssertFalse(off.on)
        XCTAssertTrue(off.shows, "off, the switch stays to turn them on again")
        XCTAssertEqual(off.stateLine(UIText(language: "ru")), UIText(language: "ru")["edits.off"])

        let strict = try XCTUnwrap(EditsLayerState(answer: #"{"bar": 1, "notes": [], "removed": [], "count": 2}"#))
        XCTAssertEqual(strict, EditsLayerState(count: 2))
        XCTAssertTrue(strict.on)
        XCTAssertFalse(EditsLayerState(count: 0).shows, "no corrections, no switch")

        XCTAssertNil(EditsLayerState(answer: #"{"error": "unavailable", "message": "Corrections are off."}"#))
        XCTAssertEqual(EditsLayerState.opened(["project": ["corrections": 3]]), EditsLayerState(count: 3))
        XCTAssertEqual(EditsLayerState.opened(["edits": ["count": 1, "layer": "off"] as [String: Any], "project": ["corrections": 1]]).on, false)
    }

    // MARK: - Through music-core

    func testTurnedOffTheLayerIsKeptInTheFileAndRefusesCorrectionsUntilTurnedOn() throws {
        let example = try XCTUnwrap(KitPiece.example)
        let imported = try open(example.fileName, example.data, #"{"language": "en", "id": "p_edits"}"#)
        XCTAssertNil(imported["edits"], "a new import opens strictly")
        let first = try piece(imported)
        let bar = try object(MusicCore.call(piece: first, "corrections", #"{"bar": 1}"#))
        let note = try XCTUnwrap((bar["notes"] as? [[String: Any]])?.first?["id"] as? String)
        let removed = try XCTUnwrap(EditsLayerState(answer: MusicCore.call(piece: first, "corrections.remove", #"{"bar": 1, "id": "\#(note)"}"#)))
        XCTAssertEqual(removed, EditsLayerState(count: 1))

        let worked = MusicCore.call(piece: first, "project.save")
        let reopened = try open("elise.motiff.json", Data(worked.utf8), json(OpenPiece.openRequest(language: "en")))
        XCTAssertEqual((reopened["edits"] as? [String: Any])?["layer"] as? String, "on")
        XCTAssertEqual(EditsLayerState.opened(reopened).count, 1)
        XCTAssertTrue(EditsLayerState.opened(reopened).unfitNotes.isEmpty)

        let id = try piece(reopened)
        let off = try XCTUnwrap(EditsLayerState(answer: MusicCore.call(piece: id, "corrections.layer", json(EditsLayerState.layerRequest(on: false, bar: nil)))))
        XCTAssertFalse(off.on)
        XCTAssertEqual(off.count, 1, "the corrections are kept while off")
        let refused = try object(MusicCore.call(piece: id, "corrections.remove", #"{"bar": 1, "id": "\#(note)"}"#))
        XCTAssertEqual(refused["error"] as? String, "unavailable")

        let saved = MusicCore.call(piece: id, "project.save")
        let again = try open("elise.motiff.json", Data(saved.utf8), json(OpenPiece.openRequest(language: "en")))
        XCTAssertFalse(EditsLayerState.opened(again).on, "the piece opens as its file says")
        let on = try XCTUnwrap(EditsLayerState(answer: MusicCore.call(piece: piece(again), "corrections.layer", json(EditsLayerState.layerRequest(on: true, bar: 1)))))
        XCTAssertTrue(on.on)
        XCTAssertEqual(on.count, 1)
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
