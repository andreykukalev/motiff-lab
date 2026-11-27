import Foundation
import SwiftUI
import WebKit

#if DEBUG
/// The way in for the screenshot UI test (`MotiffLabScreens`, the `Screens` scheme), in Debug builds only:
/// `-ml-screens-reset` removes the projects in Documents before the library first reads them, and
/// `-ml-screens-open <path>` then opens that file as if it was picked in Files. Read once per launch. With
/// `-ml-screens-both`, the PDF given is made into a piece with notes and that PDF inside it (Für Elise read into it, as
/// Read the notes would), so PDF | Notes can be switched (SwitchUITests, v0.6.4 spec section 11).
@MainActor
enum ScreensHook {
    private static var used = false

    static let active = ProcessInfo.processInfo.arguments.contains("-ml-screens")
    static let bothViews = ProcessInfo.processInfo.arguments.contains("-ml-screens-both")

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

/// What the UI tests read from the app while they run (Debug builds, `-ml-screens`): the screen shown last and whether
/// it is ready to read, the controllers alive, the score's renders, music-core's longest call and the app's memory (the
/// switch test), and on the score's screen what the walk's journeys assert (ScreensUITests): the selection, Expand, the
/// Pen and its ink, the roll's place, the playing.
@MainActor
enum ScreensProbe {
    /// The screen that showed a piece last: "score" or "pdf", as its controller took the piece.
    static var latest = "library"
    static weak var score: ScoreController? { didSet { latest = "score" } }
    static weak var pdf: PdfController? { didSet { latest = "pdf" } }
    /// The score's screen's player, roll and assistant, as the screen wires them (ScoreWorkspace.wire).
    static weak var player: Player?
    static weak var roll: RollController?
    static weak var assistant: AssistantController?

    /// The score's screen hands over what the probe reads beside its page.
    static func wired(player: Player, roll: RollController, assistant: AssistantController) {
        self.player = player
        self.roll = roll
        self.assistant = assistant
    }

    /// One line for the test: `screen=score ready=1 piece=3 live=score:1,roll:1,pdf:0,player:1 renders=2 core=120/0 mem=210`,
    /// then the score's state (`workspace`) and last the phases of the last switch.
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
        return "screen=\(screen) ready=\(ready ? 1 : 0) piece=\(piece) live=\(live) renders=\(counts["renders"] ?? 0) core=\(core) mem=\(footprint())\(workspace()) phases=\(ProbeCounts.shared.phaseLine())"
    }

    /// The score's screen as the walk's journeys assert it, empty on any other screen: ` sel=480-2400 range=480-2400
    /// focus=0 as=notes mode=navigate pen=0 ink=0 scroll=0 roll=drawer zoom=8x4 play=stopped tick=0 cycle=1 sound=1
    /// variation=0 assistant=0`. The selection and what Play takes for it in ticks (`none` without one), Expand and whether
    /// it shows the passage as a roll, the bar's mode, the Pen and the ink's strokes, the page's scroll in points, where
    /// the roll is and its zoom (bars x octaves, `none` before it has drawn), the transport, the playing tick, Цикл, the
    /// sound loaded, a variation heard, the assistant's panel.
    static func workspace() -> String {
        guard latest == "score", let score = Self.score else { return "" }
        func span(_ from: Double?, _ to: Double?) -> String {
            guard let from, let to else { return "none" }
            return "\(Int(from))-\(Int(to))"
        }
        let range = score.focusModel.playRange?.range
        var line = " sel=\(span(score.selectionModel.selection?.onset, score.selectionModel.selection?.end)) range=\(span(range?.lowerBound, range?.upperBound))"
        line += " focus=\(score.focusModel.focused ? 1 : 0) as=\(score.focusModel.asRoll ? "roll" : "notes") mode=\(score.mode.rawValue) pen=\(score.penModel.penOn ? 1 : 0)"
        line += " ink=\(score.marksModel.markCount) scroll=\(Int(score.webView.scrollView.contentOffset.y))"
        let zoom = roll?.zoom.map { "\($0.bars)x\($0.octaves)" } ?? "none"
        line += " roll=\(roll?.place.map { $0 == .drawer ? "drawer" : "focus" } ?? "none") zoom=\(zoom)"
        if let player = Self.player {
            line += " play=\(player.state.rawValue) tick=\(Int(player.tick)) cycle=\(player.cycle ? 1 : 0) sound=\(player.ready ? 1 : 0) variation=\(player.hasVariation ? 1 : 0)"
        }
        return line + " assistant=\(assistant?.panelOpen == true ? 1 : 0)"
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

    /// A music-core call took this long, in milliseconds, on the core's thread.
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
