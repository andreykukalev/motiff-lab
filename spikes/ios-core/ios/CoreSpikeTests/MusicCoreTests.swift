import XCTest

@testable import CoreSpike

/// Runs music-core inside the app on the simulator or the iPad. The macOS workflow reads the
/// benchmark reports between the BENCHMARK markers into its summary.
final class MusicCoreTests: XCTestCase {
    func testVersionIsNativeAot() {
        let version = MusicCore.version()
        XCTAssertTrue(version.contains("NativeAOT"), version)
        XCTAssertTrue(version.contains("ios"), version)
    }

    func testPhrasesOfFurElise() throws {
        let piece = SamplePiece.all[0]
        let json = try object(MusicCore.phrases(fileName: piece.file, data: piece.data()))
        XCTAssertEqual(json["notes"] as? Int, 636)
        XCTAssertEqual((json["phrases"] as? [Any])?.count, 10)
    }

    func testDamagedFileComesBackAsError() throws {
        let json = try object(MusicCore.phrases(fileName: "notes.mid", data: Data([1, 2, 3, 4])))
        XCTAssertNotNil(json["error"])
    }

    func testBenchmarkOfBothPieces() async throws {
        for piece in SamplePiece.all {
            let data = try piece.data()
            let text = await MusicCore.onCoreThread {
                MusicCore.benchmark(fileName: piece.file, data: data, iterations: 20, russian: true)
            }
            print("BENCHMARK-BEGIN\n\(text)\nBENCHMARK-END")
            let json = try object(text)
            XCTAssertNil(json["error"], text)
            XCTAssertGreaterThan(json["facts"] as? Int ?? 0, 0)
        }
    }

    func testEngravesFurEliseOnA4PagesInLeland() async throws {
        let piece = SamplePiece.all[0]
        let report = await MusicCore.onCoreThread { ScoreReport.engrave(piece) }
        XCTAssertNil(report.error, report.error ?? "")
        XCTAssertTrue(report.version.hasPrefix("6.3"), report.version)
        XCTAssertGreaterThan(report.pages, 1)
        XCTAssertTrue(report.svgPages[0].contains("<svg"))
        XCTAssertTrue(report.svgPages[0].contains("viewBox=\"0 0 2100 2970\""), String(report.svgPages[0].prefix(400)))
    }

    func testEngravingTimes() async throws {
        for piece in SamplePiece.all {
            let report = await MusicCore.onCoreThread { ScoreReport.engrave(piece) }
            XCTAssertNil(report.error, report.error ?? "")
            let json: [String: Any] = [
                "file": piece.file, "notes": report.notes, "pages": report.pages, "svgKB": report.svgBytes / 1024,
                "meiMs": report.meiMs.rounded(), "layoutMs": report.layoutMs.rounded(),
                "firstPageMs": report.firstPageMs.rounded(), "allPagesMs": report.allPagesMs.rounded(),
                "timemapMs": report.timemapMs.rounded(), "verovio": report.version,
            ]
            let text = String(decoding: try JSONSerialization.data(withJSONObject: json, options: [.sortedKeys]), as: UTF8.self)
            print("ENGRAVE-BEGIN\n\(text)\nENGRAVE-END")
        }
    }

    private func object(_ text: String) throws -> [String: Any] {
        try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any], text)
    }
}
