import UniformTypeIdentifiers
@testable import MotiffLab

/// What the library opens from Files as the protected baseline reads it (`tests/ipad/Baseline`: `AddPieceBaselineTests`,
/// ADD-01), by the name it had on the library's screen before it moved to the piece's opening (`OpenPiece.openable`,
/// W2.12, v1.6 i-11b). It only reads `OpenPiece.openable`, so no assertion of the baseline changes and the app keeps no
/// member for the tests. It goes when the baseline reads `OpenPiece.openable`, a change of `tests/ipad/Baseline` Andrei
/// merges.
extension LibraryView {
    static var openable: [UTType] { OpenPiece.openable }
}
