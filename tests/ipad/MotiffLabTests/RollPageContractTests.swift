import XCTest

@testable import MotiffLab

/// The roll page's contract (decision record 0031, step 12; the v0.7 blueprint's B7): `RollPage` calls exactly the
/// functions listed in tests/contracts/roll-page.json, each with the example's argument, reads the example answers, and
/// `RollEvent` decodes every example message: the file tests/shared/roll-page-contract.test.mjs checks roll.js against.
/// The file is read where it lies in the repository (`#filePath`).
@MainActor
final class RollPageContractTests: XCTestCase {
    /// What was told to `RollEvent.broken`, and the calls the port made, kept outside the main actor.
    private final class Record {
        var reported: [String] = []
        var calls: [(name: String, script: String, argument: Any?)] = []
    }

    private let record = Record()
    private var reported: [String] { record.reported }

    override func setUp() async throws {
        let record = record
        RollEvent.broken = { record.reported.append($0) }
    }

    override func tearDown() async throws {
        RollEvent.broken = FacadeAnswer.loud
    }

    private static func contract() throws -> [String: Any] {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/roll-page.json")
        return try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any], url.path)
    }

    /// A value as JSON text with sorted keys, so a sent argument compares with the file's.
    private static func json(_ value: Any?) -> String {
        guard let value, !(value is NSNull) else { return "null" }
        let data = try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys, .fragmentsAllowed])
        return data.map { String(decoding: $0, as: UTF8.self) } ?? "unwritable"
    }

    func testTheAppNamesExactlyThePagesFunctionsAndMessages() throws {
        let contract = try Self.contract()
        XCTAssertEqual(RollCommand.allCases.map(\.rawValue).sorted(), try XCTUnwrap(contract["commands"] as? [String]).sorted())
        XCTAssertEqual(RollEvent.Kind.allCases.map(\.rawValue).sorted(), try XCTUnwrap(contract["events"] as? [String]).sorted())
    }

    /// Each typed method once, with the example's values: the calls are exactly the contract's functions, each with exactly
    /// the example's argument, and the example answers read as the app reads them.
    func testThePortCallsExactlyTheListedFunctionsWithTheirArguments() async throws {
        let contract = try Self.contract()
        let arguments = try XCTUnwrap(contract["arguments"] as? [String: Any])
        let answers = try XCTUnwrap(contract["answers"] as? [String: Any])
        let record = record
        let page = RollPage { script, given in
            let name = String(script.dropFirst("return roll.".count).prefix { $0 != "(" })
            record.calls.append((name, script, given["argument"]))
            return answers[name]
        }
        let load = try XCTUnwrap(arguments["load"] as? [String: Any])
        let roll = try JSONDecoder().decode(RollAnswer.self, from: JSONSerialization.data(withJSONObject: try XCTUnwrap(load["roll"])))
        let shown = try await page.load(
            roll: roll, labels: try XCTUnwrap(load["labels"] as? [String: String]), noteNames: true,
            dark: false, colourBy: "hand", keep: false, frame: ["zoom": 1.5, "rowScale": 1, "scroll": 960, "pitchTop": 84]
        )
        XCTAssertEqual(shown, RollShown(["colourBy": "hand", "modes": ["hand", "track"]] as [String: Any]))
        XCTAssertTrue(shown.canChangeColour, "two colourings tell the notes apart")
        let next = await page.nextColour()
        let chosen = await page.colour("track")
        XCTAssertEqual([next, chosen], ["track", "track"])
        await page.theme(dark: true)
        await page.position(960, playing: true, label: "2.1", looping: false)
        await page.selection(RollShownSelection(selected: .init(start: 0, end: 1440, ids: ["n_0001", "n_0002"], picked: ["n_0002"]), loop: 0..<1440))
        await page.noteNames(false)
        await page.highlight([RollLitPlace(onset: 0, end: 720, noteIds: ["n_0001"])])
        await page.edit(RollEditorShown(on: true, tool: "move", picked: "n_0001"))
        await page.lanes(true)
        await page.practice(false)
        await page.input(RollPlaceInput(stylus: true).added(to: RollInput(stylus: true, mode: .navigate, tool: .select).page))
        await page.shape(RollPickShape.lasso.rawValue)
        await page.zoom(RollController.ZoomAxis.time.rawValue, 1)
        let frame = await page.frame()
        XCTAssertEqual(frame, ["zoom": 1.5, "rowScale": 1, "scroll": 960, "pitchTop": 84])
        await page.passage(720..<2160)
        await page.tuplets([["start": 0, "end": 480]])

        XCTAssertEqual(record.calls.map(\.name).sorted(), RollCommand.allCases.map(\.rawValue).sorted(), "each function once")
        for call in record.calls {
            XCTAssertEqual(call.script, call.argument == nil ? "return roll.\(call.name)()" : "return roll.\(call.name)(argument)")
            XCTAssertEqual(Self.json(call.argument), Self.json(arguments[call.name]), "the argument of \(call.name)")
        }
        XCTAssertEqual(reported, [])
    }

    func testEveryExampleMessageDecodesIntoItsKind() throws {
        let messages = try XCTUnwrap(Self.contract()["messages"] as? [[String: Any]])
        for message in messages {
            let events = RollEvent.events(in: message)
            XCTAssertEqual(events.count, 1, "\(message)")
            XCTAssertEqual(events.first.map(Self.kind)?.rawValue, message.keys.first, "\(message)")
        }
        XCTAssertEqual(Set(messages.flatMap(RollEvent.events(in:)).map(Self.kind)), Set(RollEvent.Kind.allCases))
        XCTAssertEqual(reported, [])
    }

    func testTheExampleMessagesCarryWhatTheAppReads() throws {
        let messages = try XCTUnwrap(Self.contract()["messages"] as? [[String: Any]])
        let events = messages.flatMap(RollEvent.events(in:))
        guard case let .range(range)? = events.first(where: { Self.kind($0) == .range }) else { return XCTFail("a range") }
        XCTAssertEqual(RollPick(range, byBeats: true), .beats(5760..<6720))
        XCTAssertEqual(range.gesture, "g-1")
        guard case let .drag(fields)? = events.first(where: { Self.kind($0) == .drag }) else { return XCTFail("a drag") }
        XCTAssertEqual(PlayheadGesture(message: fields), .move(2400), "the playhead's drag is Playback's to read")
        guard case let .moved(edits)? = events.first(where: { Self.kind($0) == .moved }) else { return XCTFail("notes moved") }
        XCTAssertEqual(edits, [.change(id: "n_0001", onset: 600, pitch: 73, length: 480)])
        guard case let .seek(tick, play)? = events.first(where: { Self.kind($0) == .seek }) else { return XCTFail("a seek") }
        XCTAssertEqual(tick, 1920)
        XCTAssertTrue(play)
    }

    /// What the page sends and the app leaves out stays quiet (as before the port); what it never sends is reported.
    func testOnlyWhatThePageNeverSendsIsReported() {
        XCTAssertTrue(RollEvent.events(in: ["notes": ["ids": [String]()]]).isEmpty, "no notes, no pick")
        XCTAssertTrue(RollEvent.events(in: ["span": ["start": 600, "end": 600]]).isEmpty, "never an empty stretch")
        XCTAssertTrue(RollEvent.events(in: ["moved": ["notes": [[String: Any]]()]]).isEmpty, "nothing moved")
        XCTAssertTrue(RollEvent.events(in: ["drag": ["phase": "spin"]]).count == 1, "the phase is Playback's to read")
        XCTAssertEqual(reported, [])
        XCTAssertTrue(RollEvent.events(in: ["seek": 1920]).isEmpty)
        XCTAssertTrue(RollEvent.events(in: ["somethingNew": true]).isEmpty)
        XCTAssertEqual(reported.count, 2, "\(reported)")
        guard case .cleared(let gesture)? = RollEvent.events(in: ["clear": NSNull()]).first else { return XCTFail("a clear") }
        XCTAssertNil(gesture, "a clear without its gesture still clears")
        let shown = RollShown(nil)
        XCTAssertEqual([shown.colourBy], ["hand"], "the colouring by hand when load answers nothing")
        XCTAssertFalse(shown.canChangeColour)
        XCTAssertEqual(reported.count, 3, "\(reported)")
    }

    private static func kind(_ event: RollEvent) -> RollEvent.Kind {
        switch event {
        case .range: .range
        case .zoom: .zoom
        case .cleared: .clear
        case .gestureCancelled: .gestureCancelled
        case .holdHint: .holdHint
        case .space: .space
        case .seek: .seek
        case .drag: .drag
        case .edit: .edit
        case .span: .span
        case .notes: .notes
        case .moveBegan: .moveBegan
        case .moved: .moved
        }
    }
}
