import XCTest

@testable import MotiffLab

/// `MusicCore.run` (v1.6 i-17): music-core's calls run one at a time on the core's one thread, in the order they were
/// issued. Before, each call started a thread of its own that waited on a lock, so two calls issued one after another
/// (the note editor's begin, then an edit or Save; a change, then a read) could reach music-core in either order.
final class MusicCoreOrderTests: XCTestCase {
    /// Calls issued one after another, each in a task of its own on the main actor as the screens issue them, run in
    /// that order, off the main thread, on the 8 MB stack the core needs. The core is held while they are issued, so
    /// every one of them waits for it.
    @MainActor
    func testCallsRunInTheOrderIssued() async {
        let calls = 100
        let waitingBefore = ProbeCounts.shared.snapshot()["pending"] ?? 0
        let gate = DispatchSemaphore(value: 0)
        let holding = Task { await MusicCore.run { _ = gate.wait(timeout: .now() + 30) } }
        let seen = SeenOnCore()
        var issued: [Task<Void, Never>] = []
        for number in 0..<calls {
            issued.append(Task { await MusicCore.run { seen.ran(number) } })
        }
        // Debug builds count the calls waiting: let the core go once all of them wait (else after 10 s, which only
        // makes the test weaker, never wrong).
        await until(10) { (ProbeCounts.shared.snapshot()["pending"] ?? 0) >= waitingBefore + calls + 1 }
        gate.signal()
        for call in issued { await call.value }
        await holding.value

        XCTAssertEqual(seen.numbers, Array(0..<calls), "the calls reach music-core in the order issued")
        XCTAssertFalse(seen.onMain, "no call runs on the main thread")
        XCTAssertGreaterThanOrEqual(seen.smallestStack, 8 << 20, "every call runs on the core's 8 MB stack")
    }
}

/// What the calls saw on the core's thread, in the order they ran there.
private final class SeenOnCore: @unchecked Sendable {
    private let lock = NSLock()
    private var order: [Int] = []
    private var main = false
    private var stack = Int.max

    func ran(_ number: Int) {
        lock.lock()
        order.append(number)
        main = main || Thread.isMainThread
        stack = min(stack, Thread.current.stackSize)
        lock.unlock()
    }

    var numbers: [Int] { locked { order } }
    var onMain: Bool { locked { main } }
    var smallestStack: Int { locked { stack } }

    private func locked<T>(_ read: () -> T) -> T {
        lock.lock()
        defer { lock.unlock() }
        return read()
    }
}
