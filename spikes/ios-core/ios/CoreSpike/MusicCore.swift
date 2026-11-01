import Foundation
import MotiffLabCore

/// Swift calls into the native build of music-core (CoreBridge/include/motifflab_core.h).
/// Every call returns the library's JSON text; the library's string is freed here.
enum MusicCore {
    static func version() -> String {
        take(ml_version())
    }

    // MARK: The facade (docs/decisions/0020-music-core-facade.md)

    /// Opens a MIDI or MusicXML file as a new project, or a project file, and returns the
    /// piece's number with its summary: {"piece": 1, "project": {…}, "summary": {…}}.
    static func open(fileName: String, data: Data, request: String? = nil) -> String {
        withOptionalCString(request) { requestPointer in
            call(fileName, data) { name, bytes, count in
                ml_open(name, bytes, count, requestPointer)
            }
        }
    }

    /// Runs one call on an open piece; piece 0 is for "version" and "catalogue".
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

    // MARK: The spike's benchmarks

    static func benchmark(fileName: String, data: Data, iterations: Int, russian: Bool) -> String {
        call(fileName, data) { name, bytes, count in
            ml_benchmark(name, bytes, count, Int32(iterations), russian ? 1 : 0)
        }
    }

    static func phrases(fileName: String, data: Data) -> String {
        call(fileName, data) { name, bytes, count in
            ml_phrases(name, bytes, count)
        }
    }

    static func mei(fileName: String, data: Data) -> String {
        call(fileName, data) { name, bytes, count in
            ml_mei(name, bytes, count)
        }
    }

    /// Runs `work` on a thread with an 8 MB stack, as the .NET runtime expects, and hands the
    /// result back on the main actor.
    static func onCoreThread<T: Sendable>(_ work: @escaping @Sendable () -> T) async -> T {
        await withCheckedContinuation { continuation in
            let thread = Thread {
                continuation.resume(returning: work())
            }
            thread.stackSize = 8 << 20
            thread.qualityOfService = .userInitiated
            thread.start()
        }
    }

    private static func call(
        _ fileName: String,
        _ data: Data,
        _ body: (UnsafePointer<CChar>, UnsafePointer<UInt8>?, Int32) -> UnsafeMutablePointer<CChar>?
    ) -> String {
        fileName.withCString { name in
            data.withUnsafeBytes { raw in
                take(body(name, raw.bindMemory(to: UInt8.self).baseAddress, Int32(raw.count)))
            }
        }
    }

    private static func withOptionalCString<T>(_ text: String?, _ body: (UnsafePointer<CChar>?) -> T) -> T {
        guard let text else { return body(nil) }
        return text.withCString { body($0) }
    }

    private static func take(_ text: UnsafeMutablePointer<CChar>?) -> String {
        guard let text else { return #"{"error":"NullResult"}"# }
        defer { ml_free(text) }
        return String(cString: text)
    }
}