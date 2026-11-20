import Foundation

/// A recording picked on Add piece made a new piece (v1.1, finding 29; ADR 0030, «Add an original without recognising it»):
/// free and on the device. music-core makes a piece that is only that recording, with no notes (`open` with "recording",
/// project file version 17); its sound is copied where Sources keeps a recording's (`OriginalSounds`), and the piece is
/// saved to a new project file titled by the recording's name. Nothing leaves the iPad here: the notes are recognised
/// later with «Распознать · Pro» on its row in Sources, which says where the sound goes.
enum RecordingPiece {
    /// The new piece's project file, or why there is none. The player's file is only read.
    static func make(_ picked: URL, text: UIText) async -> Result<URL, ProjectError> {
        let sound = await Task.detached(priority: .userInitiated) { OriginalSounds.picked(picked) }.value
        let request = facadeRequest(request(sound, language: text.language, now: Date()))
        let opened = await MusicCore.run { MusicCore.open(fileName: sound.fileName, data: sound.data ?? Data(), request: request) }
        guard let object = facadeObject(opened), let piece = (object["piece"] as? NSNumber)?.int32Value, piece > 0 else {
            return .failure(ProjectError(message: facadeObject(opened)?["message"] as? String ?? text["originals.unreadable"]))
        }
        guard let project = (object["project"] as? [String: Any])?["id"] as? String,
              let original = (object["recording"] as? [String: Any])?["id"] as? String,
              OriginalSounds.keep(picked, project: project, original: original) else {
            _ = await MusicCore.run { MusicCore.close(piece: piece) }
            return .failure(ProjectError(message: text["sources.sheet.soundNotKept"]))
        }
        let saved = await ProjectFiles.saveNew(piece: piece, title: ProjectFiles.title(of: sound.fileName))
        // A piece not saved keeps no sound on this iPad.
        if case .failure = saved { try? FileManager.default.removeItem(at: OriginalSounds.folder(project: project)) }
        return saved
    }

    /// The fields of `open` for the recording: the language, the time, this iPad as the device that keeps the sound, and
    /// "recording" with its length when the iPad can read it; its bytes, sent as the file's data, give its fingerprint.
    static func request(_ sound: OriginalSounds.Picked, language: String, now: Date) -> [String: Any] {
        var recording: [String: Any] = [:]
        if let milliseconds = sound.milliseconds { recording["durationMs"] = milliseconds }
        return ["language": language, "now": ISO8601DateFormatter().string(from: now), "device": "ipad", "recording": recording]
    }
}
