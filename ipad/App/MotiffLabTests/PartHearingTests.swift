import XCTest
@testable import MotiffLab

/// A long recording's parts sent several at once, a busy part again after a wait (0014 item 7), as the web app's
/// tests/web/long-recording.test.mjs checks its Pacer and hearParts.
@MainActor
final class PartHearingTests: XCTestCase {
    private func parts(_ count: Int) -> [RecordingParts.Part] {
        (0..<count).map { RecordingParts.Part(index: $0, sendFrom: Int64($0) * 120_000, sendTo: Int64($0) * 120_000 + 125_000) }
    }

    func testThePacerStartsAtTwoClimbsToFourAndDropsToOneOnABusyAnswer() {
        var pacer = PartPacer(most: 4)
        XCTAssertEqual(pacer.allowed, 2)
        pacer.heard(pacer.ticket, elapsed: 20, seconds: 125)
        pacer.heard(pacer.ticket, elapsed: 20, seconds: 125)
        XCTAssertEqual(pacer.allowed, 3, "two quick parts at two at once let a third go")
        let before = pacer.ticket
        for _ in 0..<3 { pacer.heard(pacer.ticket, elapsed: 20, seconds: 125) }
        XCTAssertEqual(pacer.allowed, 4)
        pacer.failed(pacer.ticket)
        XCTAssertEqual(pacer.allowed, 1, "a busy answer: one at a time")
        XCTAssertEqual(pacer.ceiling, 3, "and never again as many as were busy until parts come back in a row")
        pacer.failed(before)
        XCTAssertEqual(pacer.ceiling, 3, "an answer to a request sent before the change does not count again")
        XCTAssertEqual(PartPacer(most: 1).allowed, 1)
        XCTAssertGreaterThanOrEqual(PartPacer.backoff(1), 4)
        XCTAssertLessThan(PartPacer.backoff(10), 61, "the wait stops growing at a minute")
    }

    func testPartsGoSeveralAtOnceAndABusyPartIsSentAgain() async {
        let sent = Counter()
        var told: [(Int, Int, Bool)] = []
        let heard = await PartHearing.hear(parts(5), most: 4, send: { part in
            let tries = sent.add(part.index)
            if part.index == 2 && tries == 1 { return .busy("busy", after: nil, reached: true) }
            if part.index == 4 { return .nothing }
            return .notes(midi: Data([UInt8(part.index)]), engine: "transkun 2.0.1")
        }, wait: { _ in }, progress: { told.append(($0, $1, $2)) })
        XCTAssertNil(heard.failure)
        XCTAssertEqual(heard.parts.map { $0.index }, [0, 1, 2, 3], "in order, the quiet part left out")
        XCTAssertEqual(heard.quiet, 1)
        XCTAssertEqual(heard.engine, "transkun 2.0.1")
        XCTAssertEqual(sent.tries(2), 2, "the busy part went again")
        XCTAssertTrue(told.contains { $0.2 }, "the player was told it is asked again")
        XCTAssertEqual(told.last.map { [$0.0, $0.1] }, [5, 5])
    }

    func testAServerNeverReachedOrARefusalEndsTheHearing() async {
        let unreachable = await PartHearing.hear(parts(3), most: 4, send: { _ in .busy("lost", after: nil, reached: false) },
                                                 wait: { _ in }, progress: { _, _, _ in })
        XCTAssertEqual(unreachable.failure, "lost", "not asked again and again")
        let refused = await PartHearing.hear(parts(3), most: 4, send: { _ in .failed("Pro") }, wait: { _ in }, progress: { _, _, _ in })
        XCTAssertEqual(refused.failure, "Pro")
        let busy = await PartHearing.hear(parts(1), most: 4, send: { _ in .busy("busy", after: 1, reached: true) },
                                          wait: { _ in }, progress: { _, _, _ in })
        XCTAssertEqual(busy.failure, "busy", "six busy answers and the hearing stops")
    }
}

/// How often each part was sent, from the tasks that send them.
private final class Counter: @unchecked Sendable {
    private var counts: [Int: Int] = [:]
    private let lock = NSLock()

    func add(_ index: Int) -> Int {
        lock.lock()
        defer { lock.unlock() }
        counts[index, default: 0] += 1
        return counts[index] ?? 0
    }

    func tries(_ index: Int) -> Int {
        lock.lock()
        defer { lock.unlock() }
        return counts[index] ?? 0
    }
}
