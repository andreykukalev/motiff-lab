import XCTest
@testable import MotiffLab

/// The tests' way to a sources sheet call by its name, as the facade's contract and the tests' steps write it (the app's
/// is `SourcesController.change(_:_:on:)` with the port's `PieceCore.Call`); a name the port does not make fails the test.
extension SourcesController {
    @discardableResult
    func change(_ name: String, _ fields: [String: Any], on shown: Int32? = nil) async -> [String: Any]? {
        guard let call = PieceCore.Call(rawValue: name) else {
            XCTFail("\(name) is no call PieceCore makes")
            return nil
        }
        return await change(call, fields, on: shown)
    }
}
