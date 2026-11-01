import SwiftUI
import WebKit

/// Engraves a piece on the iPad: music-core writes MEI, Verovio lays it out on A4 pages in Leland,
/// and the pages are shown one under another. The spike shows the SVG in a web view; drawing it
/// natively is a later decision (README).
struct ScoreScreen: View {
    @State private var piece = SamplePiece.all[0]
    @State private var running = false
    @State private var report: ScoreReport?

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                controls
                if let report {
                    timings(report)
                }
                Divider()
                if let report, report.error == nil {
                    PagesView(pages: report.svgPages)
                } else if let error = report?.error {
                    ScrollView { Text(error).font(.system(.footnote, design: .monospaced)).padding() }
                } else {
                    ContentUnavailableView("No score yet", systemImage: "music.note", description: Text("Pick a piece and tap Engrave."))
                }
            }
            .navigationTitle("Verovio on iPad")
            .navigationBarTitleDisplayMode(.inline)
        }
    }

    private var controls: some View {
        HStack {
            Picker("Piece", selection: $piece) {
                ForEach(SamplePiece.all) { Text($0.title).tag($0) }
            }
            Spacer()
            Button(running ? "Engraving…" : "Engrave") { Task { await engrave() } }
                .buttonStyle(.borderedProminent)
                .disabled(running)
        }
        .padding(.horizontal)
        .padding(.vertical, 8)
    }

    private func timings(_ report: ScoreReport) -> some View {
        let items: [(String, String)] = [
            ("Verovio", report.version),
            ("pages", "\(report.pages)"),
            ("MEI (music-core)", Self.ms(report.meiMs)),
            ("layout", Self.ms(report.layoutMs)),
            ("page 1", Self.ms(report.firstPageMs)),
            ("all pages", Self.ms(report.allPagesMs)),
            ("timemap", Self.ms(report.timemapMs)),
            ("total", Self.ms(report.totalMs)),
            ("SVG", "\(report.svgBytes / 1024) KB"),
        ]
        return ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 16) {
                ForEach(items, id: \.0) { item in
                    VStack(alignment: .leading) {
                        Text(item.0).font(.caption).foregroundStyle(.secondary)
                        Text(item.1).font(.callout.monospacedDigit())
                    }
                }
            }
            .padding(.horizontal)
            .padding(.bottom, 8)
        }
    }

    private func engrave() async {
        running = true
        defer { running = false }
        let piece = piece
        report = await MusicCore.onCoreThread { ScoreReport.engrave(piece) }
    }

    private static func ms(_ value: Double) -> String { String(format: "%.0f ms", value) }
}

/// The pages as SVG, one under another on a grey desk, each as wide as the view.
struct PagesView: UIViewRepresentable {
    let pages: [String]

    func makeUIView(context: Context) -> WKWebView {
        let view = WKWebView()
        view.isOpaque = false
        view.backgroundColor = .systemGroupedBackground
        return view
    }

    func updateUIView(_ view: WKWebView, context: Context) {
        let body = pages.map { "<div class=\"page\">\($0)</div>" }.joined()
        let html = """
        <!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">
        <style>
          body { margin: 0; padding: 16px; background: #e9e6df; }
          .page { background: #fbf8f1; margin: 0 auto 16px; max-width: 900px; box-shadow: 0 1px 4px rgba(0,0,0,.2); }
          .page svg { display: block; width: 100%; height: auto; }
        </style></head><body>\(body)</body></html>
        """
        view.loadHTMLString(html, baseURL: nil)
    }
}
