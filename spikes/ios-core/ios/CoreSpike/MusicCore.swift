import Foundation
import MotiffLabCore

/// Swift calls into the native build of music-core (CoreBridge/include/motifflab_core.h).
/// Every call returns the library's JSON text; the library's string is freed here.
enum MusicCore {
    static func version() -> String {
        take(ml_version())
    }

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

    private static func take(_ text: UnsafeMutablePointer<CChar>?) -> String {
        guard let text else { return #"{"error":"NullResult"}"# }
        defer { ml_free(text) }
        return String(cString: text)
    }
}
