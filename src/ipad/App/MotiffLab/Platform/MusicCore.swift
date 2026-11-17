import Foundation
import MotiffLabCore
import os

/// The native music-core (src/ipad/Core): the facade of decision record 0020 through its five C functions. Everything
/// musical the app shows comes from here (0019): import, phrases, facts, Try chips, variations, notation, playback
/// and the project file. Every answer is the facade's JSON text.
///
/// The facade keeps its open pieces in memory and is not shared between threads, so every call runs through
/// `MusicCore.run`, one at a time, on a thread with the 8 MB stack the .NET runtime expects, off the main thread.
enum MusicCore {
    /// {"contract": 5, "projectFile": 14, "runtime": "…"}
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
            ProbeCounts.shared.add("pending")
            let thread = Thread {
                lock.lock()
                let began = DispatchTime.now().uptimeNanoseconds
                let result = work()
                ProbeCounts.shared.called(Int((DispatchTime.now().uptimeNanoseconds - began) / 1_000_000))
                lock.unlock()
                ProbeCounts.shared.add("pending", -1)
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

/// A facade answer read loudly (step A7; now only the roll's payload, which track R types, B7; every other hot answer is
/// typed and read by `decode`, step B5):
/// the facade's refusal {"error": code, "message": text} is never read as a success, and a field music-core always writes
/// (its writer in src/backend/MotiffLab.MusicCore/Facade/Answer*.cs) that is missing or of another type is a broken
/// contract, told to `broken` instead of read as a default. A field music-core leaves out by design is read from `fields`
/// as it is.
struct FacadeAnswer {
    /// What the answer is to, for the problem's words: the call, then the path to the object in it ("playback.notes[]").
    let call: String
    let fields: [String: Any]

    /// The answer of `call`; nil for a refusal, and nil, reported, for text that is no JSON object.
    init?(_ text: String, of call: String) {
        guard let object = facadeObject(text) else {
            FacadeAnswer.broken("music-core's answer to \(call) is not a JSON object")
            return nil
        }
        guard object["error"] == nil else { return nil }
        self.init(call: call, fields: object)
    }

    init(call: String, fields: [String: Any]) {
        self.call = call
        self.fields = fields
    }

    /// A field music-core always writes; nil, reported, when it is missing or of another type.
    func required<T>(_ key: String, _ type: T.Type = T.self) -> T? {
        guard let value = fields[key] as? T else {
            Self.broken("music-core's answer to \(call) has no \"\(key)\" of type \(T.self)")
            return nil
        }
        return value
    }

    /// An object music-core always writes.
    func entry(_ key: String) -> FacadeAnswer? {
        required(key, [String: Any].self).map { FacadeAnswer(call: "\(call).\(key)", fields: $0) }
    }

    /// Fields music-core always writes that the app passes on unread (the roll's payload, read by roll.js); false, each
    /// missing one reported, when any is missing or null.
    func has(_ keys: String...) -> Bool {
        let missing = keys.filter { fields[$0] == nil || fields[$0] is NSNull }
        missing.forEach { Self.broken("music-core's answer to \(call) has no \"\($0)\"") }
        return missing.isEmpty
    }

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

    /// What a decoding error says of the answer, in `required`'s words: the field missing or of another type, and where.
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
