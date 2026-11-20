import XCTest

@testable import MotiffLab

/// The score page's contract (decision record 0031, step 2): the app names the functions and decodes the messages listed
/// in tests/contracts/score-page.json, the lists tests/shared/score-page-contract.test.mjs checks the page
/// (Scripts/score/ipad-page) and its types against.
final class ScorePageContractTests: XCTestCase {
    private final class Marker {}

    private func contract() throws -> [String: Any] {
        let url = try XCTUnwrap(Bundle(for: Marker.self).url(forResource: "score-page", withExtension: "json"), "the contract is bundled with the tests")
        return try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
    }

    func testTheAppNamesExactlyThePagesFunctions() throws {
        let commands = try XCTUnwrap(contract()["commands"] as? [String])
        XCTAssertEqual(PageCommand.allCases.map(\.rawValue).sorted(), commands.sorted())
    }

    func testTheAppDecodesExactlyThePagesMessages() throws {
        let events = try XCTUnwrap(contract()["events"] as? [String])
        XCTAssertEqual(PageEvent.Kind.allCases.map(\.rawValue).sorted(), events.sorted())
    }

    func testACallNamesItsFunctionAndPassesItsArgument() {
        let (script, arguments) = WebScorePage.script(.reveal, ["start": 960])
        XCTAssertEqual(script, "return motiff.reveal(argument)")
        XCTAssertEqual((arguments["argument"] as? [String: Int])?["start"], 960)
        XCTAssertEqual(WebScorePage.script(.menuClosed, nil).0, "return motiff.menuClosed()")
    }

    func testABarTappedIsItsSpan() {
        let events = PageEvent.events(in: ["bar": ["start": 1920, "end": 3840]])
        guard case let .bar(start, end)? = events.first else { return XCTFail("a bar: \(events)") }
        XCTAssertEqual([start, end], [1920, 3840])
    }

    func testAPassageColouredIsItsSpan() {
        let events = PageEvent.events(in: ["coloured": ["start": 480, "end": 1440]])
        guard case let .coloured(start, end)? = events.first else { return XCTFail("a passage coloured: \(events)") }
        XCTAssertEqual([start, end], [480, 1440])
    }

    func testAVersionMarkerCarriesItsPlace() {
        let events = PageEvent.events(in: ["performanceBar": 12, "left": 40.5, "bottom": 300])
        XCTAssertEqual(events.count, 1, "left and bottom belong to the marker")
        guard case let .performanceBar(bar, left, bottom)? = events.first else { return XCTFail("a marker: \(events)") }
        XCTAssertEqual(bar, 12)
        XCTAssertEqual([left, bottom], [40.5, 300])
    }

    /// A draft's приём number (v1.1) is a marker that says which it is: the card opens for that occurrence, at the number.
    func testAPriyomNumberCarriesItsKey() {
        let events = PageEvent.events(in: ["performanceBar": 4, "left": 10, "bottom": 20, "technique": "pl_0001/1/4"])
        XCTAssertEqual(events.count, 1)
        guard case let .techniqueNumber(key, left, bottom)? = events.first else { return XCTFail("a number: \(events)") }
        XCTAssertEqual(key, "pl_0001/1/4")
        XCTAssertEqual([left, bottom], [10, 20])
    }

    /// A draft's seam diamond (v1.1) is a marker that says which seam it is: its card opens there.
    func testASeamDiamondCarriesItsKey() {
        let events = PageEvent.events(in: ["performanceBar": 5, "left": 30, "bottom": 40, "seam": "pl_0002"])
        XCTAssertEqual(events.count, 1)
        guard case let .seamDiamond(key, left, bottom)? = events.first else { return XCTFail("a seam: \(events)") }
        XCTAssertEqual(key, "pl_0002")
        XCTAssertEqual([left, bottom], [30, 40])
    }

    /// A phrase's number and a section's letter tapped on the score (the phrases spec, v1.2) name what they mark.
    func testAPhraseMarkNamesItsPhraseAndALetterItsSection() {
        guard case .phraseMark(let number)? = PageEvent.events(in: ["phraseMark": 2]).first else { return XCTFail("a phrase's number") }
        XCTAssertEqual(number, 2)
        guard case .sectionMark(let id)? = PageEvent.events(in: ["sectionMark": "section_2"]).first else { return XCTFail("a section's letter") }
        XCTAssertEqual(id, "section_2")
    }

    /// A motif's comma tapped on the card in focus (the phrases spec, decision 2) names its phrase and its place in it.
    func testAMotifMarkNamesItsPhraseAndItsPlace() {
        guard case let .motifMark(phrase, motif)? = PageEvent.events(in: ["motifMark": ["phrase": 3, "motif": 2]]).first else { return XCTFail("a motif's comma") }
        XCTAssertEqual([phrase, motif], [3, 2])
        XCTAssertTrue(PageEvent.events(in: ["motifMark": ["phrase": 3]]).isEmpty, "a motif needs its place")
    }

    /// A phrase's number held names its phrase with where it is, for its menu; a mark dragged and let go, the start it
    /// moves from and the note it goes to (the phrases spec, v1.2 i-3a).
    func testAPhraseMarkHeldCarriesItsPlaceAndOneDraggedItsMove() {
        let held = PageEvent.events(in: ["phraseHeld": 3, "left": 12.5, "bottom": 80])
        XCTAssertEqual(held.count, 1, "left and bottom belong to the mark")
        guard case let .phraseHeld(number, left, bottom)? = held.first else { return XCTFail("a mark held: \(held)") }
        XCTAssertEqual(number, 3)
        XCTAssertEqual([left, bottom], [12.5, 80])
        guard case let .phraseMoved(from, to)? = PageEvent.events(in: ["phraseMoved": ["from": 2880, "to": 2520]]).first else { return XCTFail("a mark moved") }
        XCTAssertEqual([from, to], [2880, 2520])
        XCTAssertTrue(PageEvent.events(in: ["phraseMoved": ["from": 2880]]).isEmpty, "a move needs both ticks")
    }

    func testTheMenuPutAwayIsNoPlace() {
        guard case .menu(let place)? = PageEvent.events(in: ["menu": NSNull()]).first else { return XCTFail("the menu") }
        XCTAssertNil(place)
    }

    func testWhatDoesNotDecodeIsLeftOut() {
        XCTAssertTrue(PageEvent.events(in: ["chosen": ["start": 0]]).isEmpty, "a span needs both ends")
        XCTAssertTrue(PageEvent.events(in: ["drag": ["phase": "spin"]]).isEmpty, "an unknown phase")
        XCTAssertTrue(PageEvent.events(in: ["somethingNew": true]).isEmpty, "a kind the contract does not list")
    }
}
