import Foundation
import MotiffLabCore

/// The native music-core (ipad/Core): the facade of decision record 0020 through its five C functions. Everything
/// musical the app shows comes from here (0019): import, phrases, facts, Try chips, variations, notation, playback
/// and the project file. Every answer is the facade's JSON text.
///
/// The facade keeps its open pieces in memory and is not shared between threads, so every call runs through
/// `MusicCore.run`, one at a time, on a thread with the 8 MB stack the .NET runtime expects, off the main thread.
enum MusicCore {
    /// {"contract": 3, "projectFile": 12, "runtime": "…"}
    static func version() -> String {
        take(ml_version())
    }

    /// Opens a MIDI or MusicXML file as a new project, or a project file, and returns the piece's number with its
    /// summary: {"piece": 1, "project": {…}, "summary": {…}}.
    static func open(fileName: String, data: Data, request: String? = nil) -> String {
        withOptionalCString(request) { requestPointer in
            fileName.withCString { name in
                data.withUnsafeBytes { raw in
                    take(ml_open(name, raw.bindMemory(to: UInt8.self).baseAddress, Int32(raw.count), requestPointer))
                }
            }
        }
    }

    /// Runs one call on an open piece; piece 0 is for "version", "catalogue", "settings" and "settings.state".
    static func call(piece: Int32, _ name: String, _ request: String? = nil) -> String {
        withOptionalCString(request) { requestPointer in
            name.withCString { namePointer in
                take(ml_call(piece, namePointer, requestPointer))
            }
        }
    }

    /// Forgets an open piece; false when it was not open.
    @discardableResult
    static func close(piece: Int32) -> Bool {
        ml_close(piece) != 0
    }

    /// Runs `work` on the core's thread, after any call before it, and hands its result back.
    static func run<T: Sendable>(_ work: @escaping @Sendable () -> T) async -> T {
        await withCheckedContinuation { continuation in
            let thread = Thread {
                lock.lock()
                let result = work()
                lock.unlock()
                continuation.resume(returning: result)
            }
            thread.stackSize = 8 << 20
            thread.qualityOfService = .userInitiated
            thread.start()
        }
    }

    private static let lock = NSLock()

    private static func withOptionalCString<T>(_ text: String?, _ body: (UnsafePointer<CChar>?) -> T) -> T {
        guard let text else { return body(nil) }
        return text.withCString { body($0) }
    }

    private static func take(_ text: UnsafeMutablePointer<CChar>?) -> String {
        guard let text else { return #"{"error":"internal","message":"The library returned nothing."}"# }
        defer { ml_free(text) }
        return String(cString: text)
    }
}

/// A facade answer as a dictionary, or nil when it is not a JSON object.
func facadeObject(_ text: String) -> [String: Any]? {
    (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any]
}
