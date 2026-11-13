import SwiftUI
import WebKit

/// The Reference's music engraved (v0.5, «Справочник»): music-core's MEI for a chord, a scale or a progression, laid out by
/// Verovio with the web's options (Scripts/reference/reference-score.ts, `referenceLayout`): a chord or a scale large and as
/// narrow as the music, a progression smaller and stretched across the width.
struct ReferenceEngraving: Equatable, Sendable {
    var svg: String
    /// The music's size in Verovio's pixels, from the SVG's view box.
    var width: Double
    var height: Double

    /// The web's scales: a chord or a scale 64, a progression of up to eight bars 48.
    static func options(width: Double, stretch: Bool) -> String {
        let scale = stretch ? 48 : 64
        let pageWidth = max(900, Int((width * 100 / Double(scale)).rounded()))
        return """
        {"font": "Leland", "scale": \(scale), "pageWidth": \(pageWidth), "pageHeight": 60000, "adjustPageHeight": true,
         "adjustPageWidth": \(stretch ? "false" : "true"), "pageMarginLeft": 20, "pageMarginRight": 20, "pageMarginTop": 40,
         "pageMarginBottom": 20, "breaks": "auto", "minLastJustification": \(stretch ? "0" : "0.8"), "header": "none",
         "footer": "none", "svgViewBox": true}
        """
    }

    /// Lays the MEI out at a width in points; nil when Verovio cannot. Call it through `MusicCore.run`.
    static func engrave(_ mei: String, width: Double, stretch: Bool) -> ReferenceEngraving? {
        guard !mei.isEmpty, width > 0 else { return nil }
        let engraver = Engraver()
        engraver.setOptions(options(width: width, stretch: stretch))
        guard engraver.load(mei), engraver.pageCount > 0 else { return nil }
        let svg = engraver.svg(page: 1)
        guard let size = viewBox(svg) else { return nil }
        return ReferenceEngraving(svg: svg, width: size.width, height: size.height)
    }

    /// The width and height of the first view box in an SVG: Verovio gives the music's size only so.
    static func viewBox(_ svg: String) -> (width: Double, height: Double)? {
        guard let start = svg.range(of: "viewBox=\"") else { return nil }
        let rest = svg[start.upperBound...]
        guard let end = rest.firstIndex(of: "\"") else { return nil }
        let numbers = rest[..<end].split(whereSeparator: { $0 == " " || $0 == "," }).compactMap { Double($0) }
        guard numbers.count == 4, numbers[2] > 0, numbers[3] > 0 else { return nil }
        return (numbers[2], numbers[3])
    }

    /// The height the music takes at a width: a progression stretched to it, a chord or scale at most its own size.
    func height(at available: Double, stretch: Bool) -> Double {
        let shown = stretch ? available : min(available, width)
        return shown * height / width
    }
}

/// The notation on the paper of the score, engraved again when the music or the width changes; read out by its label.
struct ReferenceNotation: View {
    let mei: String
    var stretch = false
    let label: String
    @State private var width: Double = 0
    @State private var engraving: ReferenceEngraving?

    var body: some View {
        ReferenceNotationPage(engraving: engraving, stretch: stretch)
            .frame(height: max(CGFloat.ds("s-8"), engraving.map { CGFloat($0.height(at: width, stretch: stretch)) } ?? CGFloat.ds("s-8")))
            .frame(maxWidth: .infinity)
            .padding(CGFloat.ds("s-3"))
            .background(Color.ds("paper"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
            .background(GeometryReader { box in
                Color.clear
                    .onAppear { width = Double(box.size.width) - 2 * Double(CGFloat.ds("s-3")) }
                    .onChange(of: box.size.width) { _, next in width = Double(next) - 2 * Double(CGFloat.ds("s-3")) }
            })
            .task(id: "\(mei.hashValue).\(Int(width))") {
                let mei = self.mei, width = self.width, stretch = self.stretch
                engraving = await MusicCore.run { ReferenceEngraving.engrave(mei, width: width, stretch: stretch) }
            }
            .accessibilityElement()
            .accessibilityAddTraits(.isImage)
            .accessibilityLabel(label)
    }
}

/// The engraved SVG in a small web view, the notation in the paper's ink.
private struct ReferenceNotationPage: UIViewRepresentable {
    let engraving: ReferenceEngraving?
    let stretch: Bool

    /// The page last loaded, so an update that changes nothing does not load it again.
    final class Shown {
        var page = ""
    }

    func makeCoordinator() -> Shown { Shown() }

    func makeUIView(context: Context) -> WKWebView {
        let view = WKWebView()
        view.isOpaque = false
        view.scrollView.isScrollEnabled = false
        ([view, view.scrollView] as [UIView]).forEach { $0.backgroundColor = .clear }
        return view
    }

    func updateUIView(_ view: WKWebView, context: Context) {
        let page = html
        if context.coordinator.page != page {
            context.coordinator.page = page
            view.loadHTMLString(page, baseURL: nil)
        }
    }

    private var html: String {
        let tokens = DesignTokens.stands.light
        let paper = tokens["paper"] ?? ""
        let ink = tokens["notation"] ?? ""
        let width = stretch ? "100%" : "min(100%, \(Int((engraving?.width ?? 0).rounded()))px)"
        return """
        <!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">
        <style>
        html, body { margin: 0; background: \(paper); color: \(ink); }
        body > svg { display: block; width: \(width); height: auto; margin: 0 auto; fill: \(ink); color: \(ink); }
        svg [color] { color: \(ink) !important; }
        </style></head><body>
        \(engraving?.svg ?? "")
        </body></html>
        """
    }
}
