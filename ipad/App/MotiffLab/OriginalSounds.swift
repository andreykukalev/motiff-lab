import AVFoundation
import Foundation

/// A recording's sound added to a piece as an original (version 0.6, decision record 0030; experiment «Новые источники»):
/// kept "device", beside the project file on this iPad only, never in the project file and never online. A copy of the
/// file the player picked is kept in Application Support, `OriginalSounds/<environment>/<project>/<original>.<extension>`
/// (not in Documents, so the Files app does not show it); the player's own file is only read, never changed or moved.
/// Its original is "here" for music-core when the copy is there (`sources.layers` with "here"), which makes it a view of
/// the switch («Исполнение»).
enum OriginalSounds {
    /// This environment's own place: each environment has its own library.
    static var root: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("OriginalSounds", isDirectory: true)
            .appendingPathComponent(EnvironmentSetup.current().rawValue, isDirectory: true)
    }

    static func folder(project: String, in root: URL = root) -> URL {
        root.appendingPathComponent(safe(project), isDirectory: true)
    }

    /// The copy kept for the original, if there is one.
    static func file(project: String, original: String, in root: URL = root) -> URL? {
        let files = (try? FileManager.default.contentsOfDirectory(at: folder(project: project, in: root), includingPropertiesForKeys: nil)) ?? []
        return files.first { $0.deletingPathExtension().lastPathComponent == safe(original) }
    }

    /// The ids of the originals whose sound is kept here for the project.
    static func ids(project: String, in root: URL = root) -> [String] {
        let files = (try? FileManager.default.contentsOfDirectory(at: folder(project: project, in: root), includingPropertiesForKeys: nil)) ?? []
        return files.map { $0.deletingPathExtension().lastPathComponent }.sorted()
    }

    /// Copies the player's file as the original's sound; false when it could not be read or written. The player's file is
    /// read once, inside its security scope, and left as it was.
    static func keep(_ picked: URL, project: String, original: String, in root: URL = root) -> Bool {
        let target = folder(project: project, in: root)
            .appendingPathComponent(safe(original))
            .appendingPathExtension(picked.pathExtension.isEmpty ? "m4a" : picked.pathExtension.lowercased())
        do {
            try FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
            if let earlier = file(project: project, original: original, in: root) { try FileManager.default.removeItem(at: earlier) }
            let lent = picked.startAccessingSecurityScopedResource()
            defer { if lent { picked.stopAccessingSecurityScopedResource() } }
            try FileManager.default.copyItem(at: picked, to: target)
            return true
        } catch {
            return false
        }
    }

    /// How long a recording is, in whole milliseconds; nil when the iPad cannot read it.
    static func milliseconds(of url: URL) -> Int? {
        let lent = url.startAccessingSecurityScopedResource()
        defer { if lent { url.stopAccessingSecurityScopedResource() } }
        guard let file = try? AVAudioFile(forReading: url), file.processingFormat.sampleRate > 0 else { return nil }
        return Int((Double(file.length) / file.processingFormat.sampleRate * 1000).rounded())
    }

    /// Forgets the sounds of originals the piece no longer has live (deleted, or deleted elsewhere): their copies go from
    /// this iPad, as a deleted original's bytes leave the device (0030, section 4). Run when the piece opens, so an Undo
    /// in the session still finds the sound.
    static func forget(project: String, keeping live: Set<String>, in root: URL = root) {
        let files = (try? FileManager.default.contentsOfDirectory(at: folder(project: project, in: root), includingPropertiesForKeys: nil)) ?? []
        for file in files where !live.contains(file.deletingPathExtension().lastPathComponent) {
            try? FileManager.default.removeItem(at: file)
        }
    }

    /// A name that is safe as one path component.
    static func safe(_ name: String) -> String {
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "_-"))
        let cleaned = String(name.unicodeScalars.map { allowed.contains($0) ? Character($0) : "_" })
        return cleaned.isEmpty ? "_" : cleaned
    }
}
