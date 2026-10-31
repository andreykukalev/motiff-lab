import SwiftUI

@main
struct CoreSpikeApp: App {
    var body: some Scene {
        WindowGroup {
            BenchmarkView()
        }
    }
}

/// A piece bundled with the app for the benchmark.
struct SamplePiece: Identifiable, Hashable, Sendable {
    let file: String
    let title: String
    var id: String { file }

    static let all = [
        SamplePiece(file: "fur-elise-97-bars.musicxml", title: "Für Elise, 97 bars (MusicXML)"),
        SamplePiece(file: "large-5000-notes.mid", title: "5000 notes, 201 bars (MIDI)"),
    ]

    func data() throws -> Data {
        let parts = file.split(separator: ".", maxSplits: 1).map(String.init)
        guard let url = Bundle.main.url(forResource: parts[0], withExtension: parts[1]) else {
            throw CocoaError(.fileNoSuchFile)
        }
        return try Data(contentsOf: url)
    }
}

/// One stage of the report: its first run (cold) and the median of the others (warm).
struct StageTime: Identifiable {
    let name: String
    let cold: Double
    let median: Double
    var id: String { name }
}

struct BenchmarkView: View {
    @State private var piece = SamplePiece.all[0]
    @State private var iterations = 20
    @State private var russian = true
    @State private var running = false
    @State private var startMs: Double?
    @State private var version = ""
    @State private var report: [String: Any] = [:]
    @State private var raw = ""

    private static let stageOrder = ["import", "phrases", "analysis", "experiments", "notation", "projectFile"]

    var body: some View {
        NavigationStack {
            Form {
                Section("Run") {
                    Picker("Piece", selection: $piece) {
                        ForEach(SamplePiece.all) { Text($0.title).tag($0) }
                    }
                    Stepper("Runs: \(iterations)", value: $iterations, in: 1...100)
                    Toggle("Russian facts", isOn: $russian)
                    Button(running ? "Running…" : "Run music-core") { Task { await run() } }
                        .disabled(running)
                }
                if !version.isEmpty {
                    Section("Runtime") {
                        LabeledContent("Library", value: version)
                        if let startMs {
                            LabeledContent("First call (runtime start)", value: Self.ms(startMs))
                        }
                    }
                }
                if !report.isEmpty {
                    Section("Piece") {
                        ForEach(["notes", "bars", "phrases", "facts", "experiments", "meiBytes"], id: \.self) { key in
                            LabeledContent(key, value: "\(report[key] ?? "–")")
                        }
                    }
                    Section("Milliseconds per step: first run / median") {
                        ForEach(stages) { stage in
                            LabeledContent(stage.name, value: "\(Self.ms(stage.cold)) / \(Self.ms(stage.median))")
                        }
                        LabeledContent("Total", value: "\(Self.ms(report["coldTotalMs"])) / \(Self.ms(report["warmTotalMs"]))")
                            .bold()
                    }
                }
                if !raw.isEmpty {
                    Section("JSON") {
                        Text(raw).font(.system(.caption, design: .monospaced)).textSelection(.enabled)
                    }
                }
            }
            .navigationTitle("music-core on iPad")
        }
    }

    private var stages: [StageTime] {
        guard let times = report["stagesMs"] as? [String: [String: Double]] else { return [] }
        return Self.stageOrder.compactMap { name in
            times[name].map { StageTime(name: name, cold: $0["cold"] ?? 0, median: $0["median"] ?? 0) }
        }
    }

    private func run() async {
        running = true
        defer { running = false }
        let piece = piece, iterations = iterations, russian = russian
        let firstCall = version.isEmpty
        let result: (String, Double, String) = await MusicCore.onCoreThread {
            let clock = ContinuousClock()
            var version = ""
            let start = clock.measure { version = MusicCore.version() }
            let text: String
            do {
                text = MusicCore.benchmark(fileName: piece.file, data: try piece.data(), iterations: iterations, russian: russian)
            } catch {
                text = #"{"error":"\#(error.localizedDescription)"}"#
            }
            return (version, Double(start.components.attoseconds) / 1e15 + Double(start.components.seconds) * 1000, text)
        }
        version = result.0
        if firstCall { startMs = result.1 }
        raw = result.2
        report = (try? JSONSerialization.jsonObject(with: Data(result.2.utf8)) as? [String: Any]) ?? [:]
    }

    private static func ms(_ value: Any?) -> String {
        guard let number = value as? Double ?? (value as? Int).map(Double.init) else { return "–" }
        return String(format: "%.1f ms", number)
    }
}
