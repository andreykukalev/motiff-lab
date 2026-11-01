import XCTest

@testable import CoreSpike

/// The facade's contract (docs/decisions/0020-music-core-facade.md) called through the C
/// functions on the simulator or the iPad: the same calls the xUnit tests make in C#.
final class FacadeTests: XCTestCase {
    private var piece: Int32 = 0

    override func setUpWithError() throws {
        let sample = SamplePiece.all[0]
        let opened = try object(MusicCore.open(fileName: sample.file, data: sample.data(), request: #"{"language": "ru", "id": "p_test"}"#))
        XCTAssertNil(opened["error"], "\(opened)")
        piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0)
    }

    override func tearDown() {
        MusicCore.close(piece: piece)
    }

    func testVersionNamesTheContract() throws {
        let version = try object(MusicCore.call(piece: 0, "version"))
        XCTAssertEqual(version["contract"] as? Int, 1)
        XCTAssertEqual(version["projectFile"] as? Int, 11)
        XCTAssertTrue((version["runtime"] as? String ?? "").contains("NativeAOT"), "\(version)")
    }

    func testOpenGivesTheProjectAndTheSummary() throws {
        let sample = SamplePiece.all[0]
        let opened = try object(MusicCore.open(fileName: sample.file, data: sample.data()))
        defer { MusicCore.close(piece: Int32(opened["piece"] as? Int ?? 0)) }
        let project = try XCTUnwrap(opened["project"] as? [String: Any])
        XCTAssertEqual(project["origin"] as? String, "imported")
        let summary = try XCTUnwrap(opened["summary"] as? [String: Any])
        XCTAssertEqual(summary["noteCount"] as? Int, 636)
        XCTAssertEqual(summary["format"] as? String, "musicXml")
        XCTAssertEqual(opened["language"] as? String, "en")
    }

    func testPhrasesAndTheirLevels() throws {
        let phrases = try call("phrases")
        XCTAssertEqual((phrases["phrases"] as? [Any])?.count, 10)
        let levels = try XCTUnwrap(phrases["levels"] as? [[String: Any]])
        XCTAssertEqual(levels.map { $0["id"] as? String }, ["phrase", "sentence", "period"])
        XCTAssertEqual(levels[0]["available"] as? Bool, true)
    }

    func testSelectUnderstandAndExperimentsInRussian() throws {
        let selected = try call("select", #"{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}"#)
        let selection = try XCTUnwrap(selected["selection"] as? [String: Any])
        XCTAssertEqual(selection["firstBar"] as? Int, 1)
        XCTAssertFalse((selection["noteIds"] as? [Any] ?? []).isEmpty)

        let understood = try call("understand")
        let motif = try XCTUnwrap(understood["motif"] as? [String: Any])
        let facts = try XCTUnwrap(motif["facts"] as? [[String: Any]])
        XCTAssertFalse(facts.isEmpty)
        XCTAssertTrue(facts.contains { ($0["text"] as? String ?? "").unicodeScalars.contains { (0x0410...0x044F).contains($0.value) } }, "facts are in Russian")

        let experiments = try XCTUnwrap(try call("experiments")["experiments"] as? [[String: Any]])
        XCTAssertFalse(experiments.isEmpty)
        let started = try call("experiments.start", #"{"id": "\#(experiments[0]["id"] as? String ?? "")", "alternative": 0}"#)
        XCTAssertNotNil(started["result"] as? [String: Any])
    }

    func testVariationNotationPlaybackAndSave() throws {
        _ = try call("select", #"{"bars": {"first": 1, "last": 2}}"#)
        let proposed = try call("variation.propose", #"{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}"#)
        let pending = try XCTUnwrap(proposed["pending"] as? [String: Any])
        XCTAssertEqual(pending["operation"] as? String, "V1")
        let accepted = try call("variation.accept")
        XCTAssertEqual((accepted["steps"] as? [Any])?.count, 1)

        let notation = try call("notation", #"{"of": "variation", "title": "Für Elise"}"#)
        XCTAssertTrue((notation["mei"] as? String ?? "").contains("<mei"))
        let playback = try call("playback", #"{"of": "inPlace"}"#)
        XCTAssertFalse((playback["notes"] as? [Any] ?? []).isEmpty)

        let saved = MusicCore.call(piece: piece, "project.save", #"{"now": "2026-11-01T12:00:00Z"}"#)
        let file = try object(saved)
        XCTAssertEqual(file["format"] as? String, "motiff-lab-project")
        XCTAssertEqual(file["version"] as? Int, 11)
        XCTAssertEqual(((file["variations"] as? [[String: Any]])?.first)?["id"] as? String, "v_0001")

        // The saved file opens as a second piece with the work restored.
        let reopened = try object(MusicCore.open(fileName: "fur-elise.motiff.json", data: Data(saved.utf8)))
        let second = Int32(reopened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: second) }
        XCTAssertEqual(reopened["hasVariation"] as? Bool, true)
        XCTAssertEqual((try object(MusicCore.call(piece: second, "variation"))["steps"] as? [Any])?.count, 1)
    }

    func testErrorsComeBackAsCodes() throws {
        XCTAssertEqual(try call("understand", expectError: true)["error"] as? String, "noSelection")
        XCTAssertEqual(try call("dance", expectError: true)["error"] as? String, "unknownCall")
        XCTAssertEqual(try object(MusicCore.call(piece: 99, "piece"))["error"] as? String, "noPiece")
        XCTAssertEqual(try call("select", "not json", expectError: true)["error"] as? String, "badRequest")
        XCTAssertFalse(MusicCore.close(piece: 99))
    }

    private func call(_ name: String, _ request: String? = nil, expectError: Bool = false) throws -> [String: Any] {
        let answer = try object(MusicCore.call(piece: piece, name, request))
        if !expectError {
            XCTAssertNil(answer["error"], "\(name): \(answer)")
        }
        return answer
    }

    private func object(_ text: String) throws -> [String: Any] {
        try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any], text)
    }
}