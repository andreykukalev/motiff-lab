import XCTest

@testable import MotiffLab

/// The facade's contract (decision record 0020) through the C functions in the simulator or on the iPad: the calls
/// the xUnit tests make in C# (tests/MotiffLab.MusicCore.Tests/Facade) and ipad/Core/check.py makes on Linux.
final class FacadeTests: XCTestCase {
    private var piece: Int32 = 0

    override func setUpWithError() throws {
        let example = try XCTUnwrap(OpenPiece.example, "the example piece is in the app")
        let opened = try object(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "ru", "id": "p_test"}"#))
        XCTAssertNil(opened["error"], "\(opened)")
        piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0)
    }

    override func tearDown() {
        MusicCore.close(piece: piece)
    }

    func testVersionNamesTheContract() throws {
        let version = try object(MusicCore.version())
        XCTAssertGreaterThanOrEqual(version["contract"] as? Int ?? 0, 3)
        XCTAssertGreaterThanOrEqual(version["projectFile"] as? Int ?? 0, 12)
        XCTAssertTrue((version["runtime"] as? String ?? "").contains("NativeAOT"), "\(version)")
    }

    func testOpenGivesTheSummaryInTheLanguageAskedFor() throws {
        let example = try XCTUnwrap(OpenPiece.example)
        let opened = try object(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "ru"}"#))
        defer { MusicCore.close(piece: Int32(opened["piece"] as? Int ?? 0)) }
        let summary = try XCTUnwrap(opened["summary"] as? [String: Any])
        XCTAssertEqual(summary["noteCount"] as? Int, 636)
        XCTAssertEqual(summary["format"] as? String, "musicXml")
        XCTAssertEqual(opened["language"] as? String, "ru")
    }

    func testPhrasesComeInThreeLevels() throws {
        let phrases = try call("phrases")
        let levels = try XCTUnwrap(phrases["levels"] as? [[String: Any]])
        XCTAssertEqual(levels.map { $0["id"] as? String }, ["short", "phrase", "sentence"])
        XCTAssertTrue(levels.allSatisfy { $0["available"] as? Bool == true })
    }

    func testSelectAndUnderstandInRussian() throws {
        let selected = try call("select", #"{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}"#)
        let selection = try XCTUnwrap(selected["selection"] as? [String: Any])
        XCTAssertEqual(selection["firstBar"] as? Int, 1)
        XCTAssertFalse((selection["noteIds"] as? [Any] ?? []).isEmpty)

        let understood = try call("understand")
        let motif = try XCTUnwrap(understood["motif"] as? [String: Any])
        let facts = try XCTUnwrap(motif["facts"] as? [[String: Any]])
        XCTAssertFalse(facts.isEmpty)
        XCTAssertTrue(facts.contains { ($0["text"] as? String ?? "").unicodeScalars.contains { (0x0410...0x044F).contains($0.value) } }, "facts are in Russian")
    }

    func testVariationNotationPlaybackAndSave() throws {
        _ = try call("select", #"{"bars": {"first": 1, "last": 2}}"#)
        let proposed = try call("variation.propose", #"{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}"#)
        XCTAssertEqual((proposed["pending"] as? [String: Any])?["operation"] as? String, "V1")
        _ = try call("variation.accept")

        let notation = try call("notation", #"{"bars": "selection", "ossia": "variation"}"#)
        XCTAssertTrue((notation["mei"] as? String ?? "").contains("<mei"))
        let playback = try call("playback")
        XCTAssertNil(playback["error"])

        let saved = MusicCore.call(piece: piece, "project.save", #"{"now": "2026-11-04T12:00:00Z"}"#)
        let file = try object(saved)
        XCTAssertEqual(file["format"] as? String, "motiff-lab-project")
        XCTAssertGreaterThanOrEqual(file["version"] as? Int ?? 0, 12)

        // The saved file opens as a second piece with the work restored.
        let reopened = try object(MusicCore.open(fileName: "elise.motiff.json", data: Data(saved.utf8)))
        let second = Int32(reopened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: second) }
        XCTAssertEqual(reopened["hasVariation"] as? Bool, true)
    }

    func testALibraryCardReadsAProjectFileWithoutOpeningIt() throws {
        let file = MusicCore.call(piece: piece, "project.save")
        let body = try JSONSerialization.data(withJSONObject: ["file": file])
        let summary = try XCTUnwrap(PieceSummary(answer: MusicCore.call(piece: 0, "project.summary", String(decoding: body, as: UTF8.self))))
        XCTAssertTrue(summary.hasNotes)
        XCTAssertEqual(summary.origin, "imported")
        XCTAssertEqual(summary.checkBars, 0)
    }

    func testErrorsComeBackAsCodes() throws {
        XCTAssertEqual(try call("dance", expectError: true)["error"] as? String, "unknownCall")
        XCTAssertEqual(try object(MusicCore.call(piece: 99, "piece"))["error"] as? String, "noPiece")
        XCTAssertEqual(try call("select", "not json", expectError: true)["error"] as? String, "badRequest")
        XCTAssertFalse(MusicCore.close(piece: 99))
    }

    func testCallsRunOneAtATimeOffTheMainThread() async throws {
        let piece = piece
        let answers = await withTaskGroup(of: String.self) { group in
            for _ in 0..<4 {
                group.addTask { await MusicCore.run { MusicCore.call(piece: piece, "phrases") } }
            }
            return await group.reduce(into: [String]()) { $0.append($1) }
        }
        XCTAssertEqual(Set(answers).count, 1, "the same calls give the same answer")
    }

    private func call(_ name: String, _ request: String? = nil, expectError: Bool = false) throws -> [String: Any] {
        let answer = try object(MusicCore.call(piece: piece, name, request))
        if !expectError {
            XCTAssertNil(answer["error"], "\(name): \(answer)")
        }
        return answer
    }

    private func object(_ text: String) throws -> [String: Any] {
        try XCTUnwrap(facadeObject(text), text)
    }
}
