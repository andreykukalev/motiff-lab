import Foundation

/// The piece these tests were written against: the design kit's 97 generated bars of Für Elise
/// (design/kit/elise.musicxml), bundled with the tests. The app's own example is the whole piece
/// (OpenPiece.example, ScoreTests.testTheAppsExampleIsTheWholePiece).
enum KitPiece {
    private final class Marker {}

    static var example: (fileName: String, data: Data, title: String)? {
        guard let url = Bundle(for: Marker.self).url(forResource: "elise", withExtension: "musicxml"),
              let data = try? Data(contentsOf: url) else { return nil }
        return ("elise.musicxml", data, "Für Elise")
    }
}
