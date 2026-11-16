import XCTest

@testable import MotiffLab

/// The one encoding of a facade request (decision record 0031, step 1a): what music-core reads back is what was sent.
final class FacadeRequestTests: XCTestCase {
    func testARequestReadsBackAsTheFieldsItWasMadeOf() throws {
        let fields: [String: Any] = ["id": "layers.ink", "value": NSNull(), "span": ["onset": 480, "end": 960], "on": true]
        let read = try XCTUnwrap(facadeObject(facadeRequest(fields)))
        XCTAssertEqual(read["id"] as? String, "layers.ink")
        XCTAssertTrue(read["value"] is NSNull)
        XCTAssertEqual((read["span"] as? [String: Any])?["end"] as? Int, 960)
        XCTAssertEqual(read["on"] as? Bool, true)
    }

    func testTheSameFieldsGiveTheSameText() {
        XCTAssertEqual(facadeRequest(["span": ["onset": 0, "end": 480], "empty": true]), #"{"empty":true,"span":{"end":480,"onset":0}}"#)
    }

    func testNoFieldsIsAnEmptyObject() {
        XCTAssertEqual(facadeRequest([:]), "{}")
    }
}
