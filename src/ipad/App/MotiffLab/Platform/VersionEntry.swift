import Foundation

/// Another version as `performances` lists it. In Platform, as Sources lists it and Expand (Playback) is handed the one
/// shown on the score (`FocusScore.versionShown`, v1.6 i-10b).
struct VersionEntry: Identifiable, Equatable, Sendable {
    var id: String
    var name: String

    /// A time music-core writes (.NET's round trip, seven digits of fraction).
    static func date(_ text: String?) -> Date? {
        guard let text else { return nil }
        let reader = ISO8601DateFormatter()
        return reader.date(from: text) ?? reader.date(from: text.replacingOccurrences(of: #"\.\d+"#, with: "", options: .regularExpression))
    }
}

extension VersionEntry {
    /// A version as music-core writes its head (`VersionHead`); nil without its id.
    init?(_ head: VersionHead) {
        guard let id = head.id else { return nil }
        self.init(id: id, name: head.name ?? "")
    }
}
