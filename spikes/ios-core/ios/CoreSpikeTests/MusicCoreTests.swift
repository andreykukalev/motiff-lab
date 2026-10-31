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

    private func object(_ text: String) throws -> [String: Any] {
        try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any], text)
    }
}
