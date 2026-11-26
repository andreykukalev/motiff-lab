import XCTest

extension XCTestCase {
    /// Waits on the main actor for `done`, checked every 10 ms, at most `seconds`; the test then asserts what it waited
    /// for, so a wait that runs out fails there.
    @MainActor
    func until(_ seconds: Double = 5, _ done: @escaping () -> Bool) async {
        let end = Date().addingTimeInterval(seconds)
        while !done() && Date() < end {
            try? await Task.sleep(for: .milliseconds(10))
        }
    }
}
