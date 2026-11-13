import XCTest

@testable import MotiffLab

/// The playback model of version 0.5.2 (#495 UX-10, PlaybackModel) on the case file the web's reference runs too
/// (Golden/playback-model.json, tests/web/playback-model.test.mjs on Scripts/playback/playback-model.ts): the steps to the
/// engine, the state after them and what the views draw, case by case; and bar.beat.
final class PlaybackPositionTests: XCTestCase {
    private final class Marker {}

    private struct Malformed: Error {
        let what: String
    }

    func testThePortAnswersEveryCaseAsTheReferenceDoes() throws {
        let cases = try Self.cases()
        XCTAssertGreaterThan(cases.count, 40, "the case file is read whole")
        let rules = Set(cases.compactMap { $0["rule"] as? String })
        for rule in ["view", "T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8", "T9", "T10", "T11", "T12", "T13", "T14", "T15", "T16"] {
            XCTAssertTrue(rules.contains(rule), "a case for \(rule)")
        }
        for one in cases {
            let label = "\(one["rule"] as? String ?? "?"): \(one["name"] as? String ?? "?")"
            var state = try Self.state(one["given"], label)
            if one["event"] != nil {
                let event = try Self.event(one["event"], label)
                let steps = try Self.steps(one["steps"], label)
                let then = try Self.state(one["then"], label)
                let decided = PlaybackModel.next(state, event)
                XCTAssertEqual(decided.steps, steps, "\(label): the steps to the engine")
                XCTAssertEqual(decided.state, then, "\(label): the state once the engine has them")
                state = decided.state
            }
            if one["view"] != nil {
                let view = try Self.view(one["view"], label)
                XCTAssertEqual(PlaybackModel.view(of: state), view, "\(label): what the views draw")
            }
        }
    }

    /// The score and the roll report a playhead's handle as `{ drag: { phase, handle, tick } }` (#495 UX-08).
    func testAPlayheadGestureIsReadFromThePagesMessage() {
        XCTAssertEqual(PlayheadGesture(message: ["phase": "grab", "handle": "blue"] as [String: Any]), .grab(.blue))
        XCTAssertEqual(PlayheadGesture(message: ["phase": "move", "handle": "black", "tick": 960] as [String: Any]), .move(960))
        XCTAssertEqual(PlayheadGesture(message: ["phase": "release", "handle": "black", "tick": NSNull()] as [String: Any]), .release)
        XCTAssertNil(PlayheadGesture(message: ["phase": "grab", "handle": "green"] as [String: Any]), "no such playhead")
        XCTAssertNil(PlayheadGesture(message: ["phase": "move", "handle": "black"] as [String: Any]), "a move without its tick")
        XCTAssertNil(PlayheadGesture(message: nil))
    }

    func testTheEngineLoopsTheSelectionOnlyWhileCycleIsOn() {
        let selection = 960.0..<1920.0
        XCTAssertEqual(PlaybackModel.engineLoop(.init(selection: selection, loop: true, tick: 0, transport: .stopped)), selection)
        XCTAssertNil(PlaybackModel.engineLoop(.init(selection: selection, loop: false, tick: 0, transport: .stopped)))
        XCTAssertNil(PlaybackModel.engineLoop(.init(selection: nil, loop: true, tick: 0, transport: .playing)))
    }

    // MARK: - Reading the case file

    private static func cases() throws -> [[String: Any]] {
        let url = try XCTUnwrap(Bundle(for: Marker.self).url(forResource: "playback-model", withExtension: "json"), "the case file is bundled with the tests")
        let file = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        return try XCTUnwrap(file["cases"] as? [[String: Any]])
    }

    private static func number(_ value: Any?) -> Double? {
        (value as? NSNumber)?.doubleValue
    }

    private static func transport(_ value: Any?) -> TransportState? {
        (value as? String).flatMap(TransportState.init(rawValue:))
    }

    /// A selection [start, end), or nil for JSON's null.
    private static func span(_ value: Any?, _ label: String) throws -> Range<Double>? {
        guard let value, !(value is NSNull) else { return nil }
        guard let object = value as? [String: Any], let start = number(object["start"]), let end = number(object["end"]), start < end else {
            throw Malformed(what: "\(label): a selection")
        }
        return start..<end
    }

    private static func state(_ value: Any?, _ label: String) throws -> PlaybackModel.State {
        guard let object = value as? [String: Any], let loop = object["loop"] as? Bool, let tick = number(object["tick"]),
              let transport = transport(object["transport"]) else {
            throw Malformed(what: "\(label): a state")
        }
        let selection = try span(object["selection"], label)
        return PlaybackModel.State(selection: selection, loop: loop, tick: tick, transport: transport)
    }

    private static func event(_ value: Any?, _ label: String) throws -> PlaybackModel.Event {
        let object = value as? [String: Any] ?? [:]
        switch object["type"] as? String {
        case "press":
            return .press
        case "cycle":
            return .cycle
        case "report":
            if let tick = number(object["tick"]), let transport = transport(object["transport"]) {
                return .report(tick: tick, transport: transport)
            }
        case "select":
            let selection = try span(object["selection"], label)
            return .select(selection)
        case "seek":
            if let at = number(object["at"]) { return .seek(at) }
        case "seekInLoop":
            if let at = number(object["at"]) { return .seekInLoop(at) }
        case "grab", "drag", "release":
            if let gesture = gesture(object) { return gesture }
        default:
            break
        }
        throw Malformed(what: "\(label): an event")
    }

    /// A playhead taken, dragged or let go (T14–T16); nil when a field is missing.
    private static func gesture(_ object: [String: Any]) -> PlaybackModel.Event? {
        let handle = (object["handle"] as? String).flatMap(PlaybackModel.Handle.init(rawValue:))
        switch object["type"] as? String {
        case "grab":
            return .grab
        case "drag":
            guard let handle, let at = number(object["at"]) else { return nil }
            return .drag(handle, to: at)
        default:
            guard let handle, let resume = object["resume"] as? Bool else { return nil }
            return .release(handle, resume: resume)
        }
    }

    private static func steps(_ value: Any?, _ label: String) throws -> [TransportStep] {
        guard let list = value as? [[String: Any]] else { throw Malformed(what: "\(label): the steps") }
        return try list.map { step -> TransportStep in
            switch step["type"] as? String {
            case "pause":
                return .pause
            case "play":
                return .play
            case "loopOff":
                return .loopOff
            case "scrubEnd":
                return .scrubEnd
            case "scrub":
                if let from = number(step["from"]), let to = number(step["to"]) { return .scrub(from: from, to: to) }
            case "seek":
                if let at = number(step["at"]) { return .seek(at) }
            case "loop":
                if let start = number(step["start"]), let end = number(step["end"]), start < end { return .loop(start..<end) }
            default:
                break
            }
            throw Malformed(what: "\(label): a step")
        }
    }

    private static func view(_ value: Any?, _ label: String) throws -> PlaybackModel.View {
        guard let object = value as? [String: Any], let inside = object["inside"] as? Bool, let black = number(object["black"]),
              let playing = object["playing"] as? Bool, let at = number(object["at"]),
              let context = (object["context"] as? String).flatMap(PlaybackModel.Context.init(rawValue:)) else {
            throw Malformed(what: "\(label): a view")
        }
        return PlaybackModel.View(inside: inside, black: black, blue: number(object["blue"]), playing: playing, context: context, at: at)
    }

    func testBarBeatIsTheBeatThePositionLiesInWrittenBarDotBeat() {
        let beats = BarBeats.read([
            ["at": 0, "bar": 0, "beat": 3],
            ["at": 240, "bar": 1, "beat": 1],
            ["at": 480, "bar": 1, "beat": 2],
            ["at": 720, "bar": 1, "beat": 3],
            ["at": 960, "bar": 2, "beat": 1],
        ])
        XCTAssertEqual(beats.at(0)?.text, "0.3", "the pickup's beat as a selection counts it")
        XCTAssertEqual(beats.at(500)?.text, "1.2")
        XCTAssertEqual(beats.at(960)?.text, "2.1")
        XCTAssertEqual(beats.at(-5)?.text, "0.3")
        XCTAssertEqual(beats.span(240..<960)?.first, "1.1")
        XCTAssertEqual(beats.span(240..<960)?.last, "1.3", "the last beat that starts inside")
        XCTAssertNil(BarBeats().at(100))
        XCTAssertTrue(BarBeats.read([["at": 0]]).marks.isEmpty, "an older answer without bar and beat")
    }
}
