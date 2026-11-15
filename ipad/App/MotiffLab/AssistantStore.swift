import Foundation

/// The full assistant's conversations, kept on this iPad by piece (the lead's decision 4): one file per piece in
/// Application Support/Assistant, never in the project file, not synchronised with the web in 0.6.1. The newest
/// conversation first; the oldest go beyond `kept`.
struct AssistantStore {
    /// The most conversations kept for one piece.
    static let kept = 30

    let folder: URL

    static var standard: AssistantStore {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first ?? FileManager.default.temporaryDirectory
        return AssistantStore(folder: support.appendingPathComponent("Assistant", isDirectory: true))
    }

    /// The piece's conversations, newest first; none when there are none or the file cannot be read.
    func chats(of piece: String) -> [AssistantChat] {
        guard let data = try? Data(contentsOf: file(of: piece)),
              let chats = try? JSONDecoder().decode([AssistantChat].self, from: data) else { return [] }
        return Self.ordered(chats)
    }

    /// Keeps the piece's conversations, newest first, at most `kept`; empty ones are not kept.
    func keep(_ chats: [AssistantChat], of piece: String) {
        let kept = Array(Self.ordered(chats.filter { !$0.exchanges.isEmpty }).prefix(Self.kept))
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        guard let data = try? JSONEncoder().encode(kept) else { return }
        try? data.write(to: file(of: piece), options: [.atomic, .completeFileProtection])
    }

    static func ordered(_ chats: [AssistantChat]) -> [AssistantChat] {
        chats.sorted { $0.updated > $1.updated }
    }

    /// The piece's file: its key with nothing but letters, digits, dot, dash and underscore.
    func file(of piece: String) -> URL {
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-_."))
        let name = String(piece.unicodeScalars.map { allowed.contains($0) ? Character($0) : "_" })
        return folder.appendingPathComponent((name.isEmpty ? "piece" : name) + ".json")
    }
}
