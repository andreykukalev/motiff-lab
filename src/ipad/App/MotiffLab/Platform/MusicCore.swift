import Foundation
import MotiffLabCore
import os

/// The native music-core (src/ipad/Core): the facade of decision record 0020 through its five C functions. Everything
/// musical the app shows comes from here (0019): import, phrases, facts, Try chips, variations, notation, playback
/// and the project file. Every answer is the facade's JSON text.
///
/// The facade keeps its open pieces in memory and is not shared between threads, so every call runs through
/// `MusicCore.run`, one at a time and in the order issued, on the core's one thread with the 8 MB stack the .NET runtime
/// expects, off the main thread.
enum MusicCore {
    /// {"contract": n, "projectFile": n, "runtime": "…"}: the facade contract's number (docs/facade-contract.md) and the
    /// project file's version.
    static func version() -> String {
        take(ml_version())
    }

    /// Opens a MIDI or MusicXML file as a new project, a project file, a PDF or a recording, and returns the piece's number
    /// with its project: {"piece": 1, "project": {…}, "summary": {…}}, or "document" or "recording" for a piece without notes.
    static func open(fileName: String, data: Data, request: String? = nil) -> String {
        withOptionalCString(request) { requestPointer in
            fileName.withCString { name in
                data.withUnsafeBytes { raw in
                    take(ml_open(name, raw.bindMemory(to: UInt8.self).baseAddress, Int32(raw.count), requestPointer))
                }
            }
        }
    }

    /// Runs one call by name on an open piece, or on piece 0 for the calls that need none. The calls and where each is
    /// answered: docs/facade-contract.md (music-core's call registry), checked against tests/contracts/facade-calls.json.
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

    /// Runs `work` on the core's thread after every call issued before it, and hands its result back. The call joins the
    /// core's queue in the caller's isolation (`isolation`, the caller's actor), before its first wait: calls issued one
    /// after another, each in a task of its own on the main actor (an edit, then Save), reach music-core in that order.
    /// A helper that wraps `run` in an async function of its own takes the same parameter to keep that (`PieceCore`,
    /// `ProjectFiles.save`).
    /// `work` never waits for another call: it would wait for itself.
    static func run<T: Sendable>(isolation: isolated (any Actor)? = #isolation, _ work: @escaping @Sendable () -> T) async -> T {
        await withCheckedContinuation { continuation in
            ProbeCounts.shared.add("pending")
            CoreThread.shared.enqueue {
                let began = DispatchTime.now().uptimeNanoseconds
                let result = autoreleasepool { work() }
                ProbeCounts.shared.called(Int((DispatchTime.now().uptimeNanoseconds - began) / 1_000_000))
                ProbeCounts.shared.add("pending", -1)
                continuation.resume(returning: result)
            }
        }
    }

    /// The core's one thread, started at the first call and kept: it runs the works queued in the order they came, one
    /// at a time, on the 8 MB stack.
    private final class CoreThread: @unchecked Sendable {
        static let shared = CoreThread()
        private let condition = NSCondition()
        /// Guarded by `condition`.
        private var queued: [@Sendable () -> Void] = []

        private init() {
            let thread = Thread { [self] in drain() }
            thread.name = "music-core"
            thread.stackSize = 8 << 20
            thread.qualityOfService = .userInitiated
            thread.start()
        }

        /// Queues `work` after every work queued before it.
        func enqueue(_ work: @escaping @Sendable () -> Void) {
            condition.lock()
            queued.append(work)
            condition.signal()
            condition.unlock()
        }

        private func drain() {
            while true {
                condition.lock()
                while queued.isEmpty { condition.wait() }
                let work = queued.removeFirst()
                condition.unlock()
                work()
            }
        }
    }

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

/// A facade request as JSON text, the one encoding every call uses; "{}" when the object is not JSON. Its keys are sorted,
/// so the same fields always give the same text (a selection's request is compared as text, SelectionHistory).
func facadeRequest(_ fields: [String: Any]) -> String {
    guard let data = try? JSONSerialization.data(withJSONObject: fields, options: [.sortedKeys]) else { return "{}" }
    return String(decoding: data, as: UTF8.self)
}

/// A facade answer as a dictionary, or nil when it is not a JSON object.
func facadeObject(_ text: String) -> [String: Any]? {
    (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any]
}

/// music-core's answers read loudly (step A7, typed in B5 and B7): `decode` reads an answer once into its typed answer
/// (PieceAnswers.swift); the facade's refusal {"error": code, "message": text} is never read as a success, and a field
/// music-core always writes (its writer in src/backend/MotiffLab.MusicCore/Facade/Answer*.cs) that is missing or of another
/// type is a broken contract, told to `broken` instead of read as a default.
enum FacadeAnswer {
    /// Why an answer was not read, for the screen's problem line: music-core's words for a refusal, else that it gave no
    /// answer to read.
    static func problem(_ text: String) -> String {
        message(text) ?? UIText.current["parts.pdf.failedNoWord"]
    }

    /// music-core's words in an answer ("message", a refusal's); nil without them.
    static func message(_ text: String) -> String? {
        facadeObject(text)?["message"] as? String
    }

    /// An answer decoded once into its typed answer (step B5, PieceAnswers.swift): nil for a refusal, not reported; nil,
    /// told to `broken` with the field and where, for text that is no JSON object or lacks a field of the type's.
    static func decode<T: Decodable>(_ type: T.Type, _ text: String, of call: String) -> T? {
        do {
            return try JSONDecoder().decode(Refusable<T>.self, from: Data(text.utf8)).value
        } catch {
            broken("music-core's answer to \(call) \(reason(error, call))")
            return nil
        }
    }

    /// The type's value, or nil when the answer is the facade's refusal: one reading of the text for both.
    private struct Refusable<T: Decodable>: Decodable {
        let value: T?

        private struct Key: CodingKey {
            let stringValue: String
            var intValue: Int? { nil }
            init(stringValue: String) { self.stringValue = stringValue }
            init?(intValue: Int) { return nil }
        }

        init(from decoder: Decoder) throws {
            let refused = try decoder.container(keyedBy: Key.self).contains(Key(stringValue: "error"))
            value = refused ? nil : try T(from: decoder)
        }
    }

    /// What a decoding error says of the answer: the field missing or of another type, and where.
    private static func reason(_ error: Error, _ call: String) -> String {
        func place(_ path: [CodingKey]) -> String {
            call + path.map { $0.intValue.map { "[\($0)]" } ?? ".\($0.stringValue)" }.joined()
        }
        switch error as? DecodingError {
        case .keyNotFound(let key, let context)?:
            return "has no \"\(key.stringValue)\" at \(place(context.codingPath))"
        case .typeMismatch(let type, let context)?, .valueNotFound(let type, let context)?:
            guard let field = context.codingPath.last else { return "is not a JSON object" }
            return "has no \"\(field.stringValue)\" of type \(type) at \(place(Array(context.codingPath.dropLast())))"
        default:
            return "is not a JSON object"
        }
    }

    /// Where a broken contract goes: `loud` (a log line, and an assertion in Debug builds, so a test or a run through the
    /// reader stops on it); a test that breaks an answer on purpose puts its own here and back.
    static var broken: (String) -> Void = FacadeAnswer.loud

    static let loud: (String) -> Void = { problem in
        FacadeAnswer.log.error("\(problem, privacy: .public)")
        assertionFailure(problem)
    }

    private static let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "MotiffLab", category: "facade")
}
