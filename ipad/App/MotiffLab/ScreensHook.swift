import Foundation
import SwiftUI
import WebKit

#if DEBUG
/// The way in for the screenshot UI test (`MotiffLabScreens`, the `Screens` scheme), in Debug builds only:
/// `-ml-screens-reset` removes the projects in Documents before the library first reads them, and
/// `-ml-screens-open <path>` then opens that file as if it was picked in Files. Read once per launch. With `-ml-screens`
/// the score also has a hidden button that chooses the next phrase, as a tap on its bracket would. With
/// `-ml-screens-both`, the PDF given is made into a piece with notes and that PDF inside it (Für Elise read into it, as
/// Read the notes would), so PDF | Notes can be switched (SwitchUITests, v0.6.4 spec section 11).
@MainActor
enum ScreensHook {
    private static var used = false
    private static var phrase = 1

    static let active = ProcessInfo.processInfo.arguments.contains("-ml-screens")
    static let bothViews = ProcessInfo.processInfo.arguments.contains("-ml-screens-both")

    /// Phrase 2, then 3, and so on: each press chooses another phrase, so it never lifts the chosen one into focus.
    static func nextPhrase() -> Int {
        phrase += 1
        return phrase
    }

    static func start(arguments: [String] = ProcessInfo.processInfo.arguments) -> URL? {
        guard !used else { return nil }
        used = true
        if arguments.contains("-ml-screens-reset") {
            for entry in ProjectFiles.list() { try? FileManager.default.removeItem(at: entry.url) }
        }
        guard let index = arguments.firstIndex(of: "-ml-screens-open"), arguments.indices.contains(index + 1) else { return nil }
        return URL(fileURLWithPath: arguments[index + 1])
    }

    /// The PDF at `pdf` with the example piece's notes read into it, saved as a project file in Documents: a piece with
    /// notes and a PDF kept inside it, which opens on its notes with PDF | Notes in the bar.
    static func withNotes(_ pdf: URL) async -> URL? {
        guard let data = try? Data(contentsOf: pdf), let example = OpenPiece.example,
              case .success(let opened) = await OpenPiece.open(fileName: pdf.lastPathComponent, data: data, language: UIText.current.language) else { return nil }
        let id = opened.id
        let request = DocumentRead.request(musicXml: example.data, engine: "homr", pages: 3, reports: nil, language: UIText.current.language, now: Date())
        let answer = await DocumentRead.call(piece: id, request)
        _ = await MusicCore.run { MusicCore.close(piece: id) }
        guard let file = facadeObject(answer)?["file"] as? String else { return nil }
        let url = ProjectFiles.newURL(for: example.title)
        guard (try? Data(file.utf8).write(to: url, options: .atomic)) != nil else { return nil }
        // `-ml-screens-copies <n>`: a library of n more such projects, each a file of its own as a player's would be.
        let arguments = ProcessInfo.processInfo.arguments
        if let index = arguments.firstIndex(of: "-ml-screens-copies"), arguments.indices.contains(index + 1), let copies = Int(arguments[index + 1]) {
            for number in 0..<copies { try? Data(file.utf8).write(to: ProjectFiles.newURL(for: "\(example.title) \(number + 1)"), options: .atomic) }
        }
        return url
    }
}

/// What the switch test reads from the app while it runs (Debug builds, `-ml-screens`): the screen shown last and whether
/// it is ready to read, the controllers alive, the score's renders, music-core's longest call and the app's memory.
@MainActor
enum ScreensProbe {
    /// The screen that showed a piece last: "score" or "pdf", as its controller took the piece.
    static var latest = "library"
    static weak var score: ScoreController? { didSet { latest = "score" } }
    static weak var pdf: PdfController? { didSet { latest = "pdf" } }
    /// The bar's old Play drawn on a score screen whose modes are on (the flash of v0.6.4 spec section 13).
    static var oldBarPlay = 0

    /// Counts the old Play when it is drawn where it should not be; asked from the bar's body.
    @discardableResult
    static func drawn(oldBarPlay old: Bool) -> Bool {
        if old { oldBarPlay += 1 }
        return old
    }

    /// One line for the test: `screen=score ready=1 piece=3 live=score:1,roll:1,pdf:0,player:1 renders=2 core=120/0 mem=210 oldbar=0`.
    static func line() -> String {
        let screen = latest
        var ready = false
        var piece: Int32 = 0
        if screen == "score", let score = Self.score {
            piece = score.piece?.id ?? 0
            ready = score.rendered && score.webView.alpha > 0.99
        } else if screen == "pdf", let pdf = Self.pdf {
            piece = pdf.piece
            ready = pdf.document != nil
        }
        let counts = ProbeCounts.shared.snapshot()
        let live = ["score", "roll", "pdf", "player"].map { "\($0):\(counts[$0] ?? 0)" }.joined(separator: ",")
        let core = "\(ProbeCounts.shared.takeLongestCall())/\(counts["pending"] ?? 0)"
        return "screen=\(screen) ready=\(ready ? 1 : 0) piece=\(piece) live=\(live) renders=\(counts["renders"] ?? 0) core=\(core) mem=\(footprint()) oldbar=\(oldBarPlay) phases=\(ProbeCounts.shared.phaseLine())"
    }

    /// The app's own memory in MB, as the system counts it against the app (WebKit's processes not included).
    static func footprint() -> Int {
        var info = task_vm_info_data_t()
        var count = mach_msg_type_number_t(MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<natural_t>.size)
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &count) }
        }
        return result == KERN_SUCCESS ? Int(info.phys_footprint / 1_048_576) : -1
    }
}

/// The probe line, hidden over the whole window and read again four times a second.
struct ScreensProbeView: View {
    var body: some View {
        TimelineView(.periodic(from: .now, by: 0.25)) { _ in
            Color.clear
                .frame(width: 2, height: 2)
                .accessibilityElement()
                .accessibilityLabel(ScreensProbe.line())
                .accessibilityIdentifier("ml-screens-probe")
        }
        .allowsHitTesting(false)
    }
}
#endif

/// Counts kept from any thread for the probe (Debug builds only count): controllers made and gone, music-core's calls.
final class ProbeCounts: @unchecked Sendable {
    static let shared = ProbeCounts()
    private let lock = NSLock()
    private var counts: [String: Int] = [:]
    private var longest = 0
    private var began: UInt64 = 0
    private var phases: [String] = []

    func add(_ key: String, _ by: Int = 1) {
        #if DEBUG
        lock.lock()
        counts[key, default: 0] += by
        lock.unlock()
        #endif
    }

    /// A music-core call took this long, in milliseconds, inside the core's lock.
    func called(_ milliseconds: Int) {
        #if DEBUG
        lock.lock()
        longest = max(longest, milliseconds)
        lock.unlock()
        #endif
    }

    /// A switch starts now: the phases after it are counted from here.
    func begin() {
        #if DEBUG
        lock.lock()
        began = DispatchTime.now().uptimeNanoseconds
        phases = []
        lock.unlock()
        #endif
    }

    /// The switch reached `name` (opened in music-core, engraved, drawn, landed, ...): written as `name@ms` since `begin`.
    func mark(_ name: String) {
        #if DEBUG
        lock.lock()
        if began > 0, phases.count < 24 { phases.append("\(name)@\((DispatchTime.now().uptimeNanoseconds - began) / 1_000_000)") }
        lock.unlock()
        #endif
    }

    /// The phases of the last switch, in the order they came.
    func phaseLine() -> String {
        lock.lock()
        defer { lock.unlock() }
        return phases.joined(separator: ",")
    }

    func snapshot() -> [String: Int] {
        lock.lock()
        defer { lock.unlock() }
        return counts
    }

    /// The longest call since the last time it was asked.
    func takeLongestCall() -> Int {
        lock.lock()
        defer { lock.unlock() }
        let value = longest
        longest = 0
        return value
    }
}
