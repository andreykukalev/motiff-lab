import XCTest

@testable import MotiffLab

/// The phrases at their three levels, the form and the Piece DNA from music-core built with NativeAOT, against the
/// answers the same calls give on Linux (task #293; basis rule S7 and pre-build check 2 of the project-continuity
/// council). The goldens in Golden/ are written and asserted on Linux by
/// tests/MotiffLab.MusicCore.Tests/Golden/AnalysisParityTests.cs; a difference here is a parity difference between the
/// two builds, to be recorded rather than smoothed over.
final class AnalysisParityTests: XCTestCase {
    private final class Marker {}

    func testTheKitPieceGivesTheAnswersOfTheLinuxBuild() throws {
        try assertGolden("elise", piece: KitPiece.example)
    }

    func testTheExamplePieceGivesTheAnswersOfTheLinuxBuild() throws {
        try assertGolden("FurElise", piece: OpenPiece.example)
    }

    private func assertGolden(_ name: String, piece: (fileName: String, data: Data, title: String)?) throws {
        let piece = try XCTUnwrap(piece, "\(name) is bundled")
        let url = try XCTUnwrap(Bundle(for: Marker.self).url(forResource: "\(name).analysis", withExtension: "json"), "the golden is bundled with the tests")
        let golden = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        XCTAssertEqual(golden["fixture"] as? String, piece.fileName)
        let answers = try XCTUnwrap(golden["answers"] as? [[String: Any]])
        XCTAssertEqual(answers.count, 8, "four calls in each of two languages")

        var opened: [String: Int32] = [:]
        defer { opened.values.forEach { _ = MusicCore.close(piece: $0) } }
        for entry in answers {
            let language = try XCTUnwrap(entry["language"] as? String)
            let call = try XCTUnwrap(entry["call"] as? String)
            let expected = try XCTUnwrap(entry["answer"] as? NSDictionary)
            var request: String?
            if let parameters = entry["request"] as? [String: Any] {
                request = try String(decoding: JSONSerialization.data(withJSONObject: parameters), as: UTF8.self)
            }

            if opened[language] == nil {
                let answer = try XCTUnwrap(facadeObject(MusicCore.open(fileName: piece.fileName, data: piece.data, request: #"{"language": "\#(language)", "id": "p_golden"}"#)))
                XCTAssertNil(answer["error"], "\(answer)")
                opened[language] = Int32(answer["piece"] as? Int ?? 0)
            }

            let text = MusicCore.call(piece: opened[language] ?? 0, call, request)
            let actual = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? NSDictionary)
            let label = "\(name) \(language) \(call) \(request ?? "")"
            if !actual.isEqual(expected) {
                let attachment = XCTAttachment(string: text)
                attachment.name = "\(label) (NativeAOT)"
                attachment.lifetime = .keepAlways
                add(attachment)
            }

            XCTAssertTrue(actual.isEqual(expected), "\(label) differs from the answer on Linux; the NativeAOT answer is attached")
        }
    }
}
