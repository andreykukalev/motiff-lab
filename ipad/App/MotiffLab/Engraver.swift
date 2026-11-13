import Foundation
import Verovio

/// Verovio, built from its source with one patch as Verovio.xcframework (ipad/Verovio), called through its own C
/// functions (tools/c_wrapper.h). It is the same engraver and version as the web app's (0008), so a piece lays out on
/// the same pages. The strings Verovio returns belong to the toolkit, so each one is copied before the next call.
final class Engraver {
    private let toolkit: UnsafeMutableRawPointer

    /// - Parameter resourcePath: the folder with Verovio's font data (verovio-data in the app).
    init(resourcePath: String = Engraver.bundledResourcePath) {
        toolkit = resourcePath.withCString { vrvToolkit_constructorResourcePath($0)! }
    }

    deinit {
        vrvToolkit_destructor(toolkit)
    }

    static var bundledResourcePath: String {
        Bundle.main.path(forResource: "verovio-data", ofType: nil) ?? ""
    }

    var version: String { String(cString: vrvToolkit_getVersion(toolkit)) }

    @discardableResult
    func setOptions(_ json: String) -> Bool {
        json.withCString { vrvToolkit_setOptions(toolkit, $0) }
    }

    /// Loads MEI and lays it out into pages.
    func load(_ mei: String) -> Bool {
        mei.withCString { vrvToolkit_loadData(toolkit, $0) }
    }

    var pageCount: Int { Int(vrvToolkit_getPageCount(toolkit)) }

    /// One page as SVG; pages count from 1.
    func svg(page: Int) -> String {
        String(cString: vrvToolkit_renderToSVG(toolkit, Int32(page), false))
    }

    var log: String { String(cString: vrvToolkit_getLog(toolkit)) }

    /// When each engraved note and rest sounds and where each measure starts, in quarter notes: the timemap the web
    /// app's score timeline reads (Scripts/score/score-timeline.ts), as JSON.
    func timemap() -> String {
        #"{"includeRests": true, "includeMeasures": true}"#.withCString { String(cString: vrvToolkit_renderToTimemap(toolkit, $0)) }
    }

    /// The web app's A4 page in Leland, the redesign's music font, with its paper margins: the options of `A4_PDF_LAYOUT` in
    /// Scripts/score/score-page.ts (decision record 0017, design/kit/HANDOFF.md section 8). The page holds the same height of
    /// music as the web's screen page (`A4_LAYOUT`), so a piece breaks into the same systems and pages on both, and a resume
    /// point, a page bookmark or a page count means the same page. The iPad's pages start below the floating bar already
    /// (Web/score.css `--pad-top`), so the 50 mm top margin the web keeps for its bar left a band of empty paper above the
    /// first system (#479 item 12); the music starts 20 mm from the top, as on paper. tests/web/score-pdf.test.mjs keeps the
    /// two equal.
    static let a4Leland = """
    {"font": "Leland", "scale": 50, "pageWidth": 2100, "pageHeight": 2970, "pageMarginLeft": 150, "pageMarginRight": 150,
     "pageMarginTop": 200, "pageMarginBottom": 460, "spacingSystem": 20, "spacingStaff": 10, "adjustPageHeight": false,
     "justifyVertically": false, "breaks": "auto", "header": "auto", "footer": "none", "svgViewBox": true}
    """

    /// Even bars (Settings · Notes, `score.evenBars`; testing item 35): the bars take room by their length, so the playhead
    /// keeps one pace; the web app's `EVEN_SPACING` in Scripts/score/score-page.ts, which tests/web/score-pdf.test.mjs keeps equal.
    static let evenSpacing = """
    {"spacingLinear": 0.08, "spacingNonLinear": 0.9}
    """

    /// Verovio's own spacing, by the notes: given outright when even bars are off, as a toolkit keeps the options it was
    /// last set; the web app's `NOTE_SPACING`.
    static let noteSpacing = """
    {"spacingLinear": 0.25, "spacingNonLinear": 0.6}
    """

    /// The A4 page's options, with even bars or Verovio's own spacing.
    static func a4(evenBars: Bool) -> String {
        guard let options = (try? JSONSerialization.jsonObject(with: Data(a4Leland.utf8))) as? [String: Any],
              let spacing = (try? JSONSerialization.jsonObject(with: Data((evenBars ? evenSpacing : noteSpacing).utf8))) as? [String: Any],
              let merged = try? JSONSerialization.data(withJSONObject: options.merging(spacing) { _, new in new })
        else { return a4Leland }
        return String(decoding: merged, as: UTF8.self)
    }

    /// The pages of a piece's notation as SVG with Verovio's timemap: music-core writes the MEI (`notation`), Verovio
    /// lays it out on A4 pages. Call it through `MusicCore.run`.
    /// With `evenBars`, the bars take room by their length (Settings · Notes).
    static func engrave(piece: Int32, title: String, evenBars: Bool = false) -> Result<Engraving, EngraveError> {
        layOut(piece: piece, request: "{\"title\": \(jsonString(title))}", options: a4(evenBars: evenBars))
    }

    /// The whole piece as a merge draft's view shows it on the pages (testing items 32 and 34): "version" or "result",
    /// with the ids of the notes from the version and of the player's own (`performance.merge.notation`).
    static func engraveMerge(piece: Int32, title: String, view: String, evenBars: Bool = false) -> Result<Engraving, EngraveError> {
        layOut(piece: piece, call: "performance.merge.notation", request: "{\"title\": \(jsonString(title)), \"view\": \(jsonString(view))}", options: a4(evenBars: evenBars))
    }

    /// The bars of a merge draft's view engraved on their own for Compare versions, one system at the window's width,
    /// the notes larger or smaller with `zoom` (100 as the page has them).
    static func engraveMergeBars(piece: Int32, view: String, firstBar: Int, lastBar: Int, zoom: Int) -> Result<Engraving, EngraveError> {
        let scale = max(20, min(80, 45 * zoom / 100))
        let options = """
        {"font": "Leland", "scale": \(scale), "pageWidth": 2400, "pageHeight": 2970, "pageMarginLeft": 40, "pageMarginRight": 40,
         "pageMarginTop": 40, "pageMarginBottom": 40, "adjustPageHeight": true, "breaks": "none", "header": "none", "footer": "none",
         "svgViewBox": true}
        """
        return layOut(piece: piece, call: "performance.merge.notation",
                      request: "{\"view\": \(jsonString(view)), \"bars\": {\"first\": \(firstBar), \"last\": \(lastBar)}}", options: options)
    }

    /// The bars of a phrase in focus engraved on their own (0018 section 4): music-core writes the excerpt
    /// (`notation` with `bars`, 0021) and Verovio lays it out with the card's options, which the score page gives as
    /// the web app's phrase card has them (Scripts/score/phrase-card.ts, `cardLayout`). Call it through `MusicCore.run`.
    /// With `ossia` the variation being tried is engraved above the original, bar for bar (0021); with `version` instead,
    /// another version of the piece as the performer has it (0022, section 5).
    static func engraveCard(piece: Int32, firstBar: Int, lastBar: Int, options: String, ossia: Bool = false, version: String? = nil) -> Result<Engraving, EngraveError> {
        let above = ossia ? ", \"ossia\": \"variation\"" : version.map { ", \"ossia\": \(jsonString($0))" } ?? ""
        return layOut(piece: piece, request: "{\"bars\": {\"first\": \(firstBar), \"last\": \(lastBar)}\(above)}", options: options)
    }

    private static func layOut(piece: Int32, call: String = "notation", request: String, options: String) -> Result<Engraving, EngraveError> {
        guard let answer = facadeObject(MusicCore.call(piece: piece, call, request)) else {
            return .failure(EngraveError(message: "music-core gave no notation."))
        }
        guard let mei = answer["mei"] as? String, !mei.isEmpty else {
            return .failure(EngraveError(message: answer["message"] as? String ?? "music-core gave no notation."))
        }
        let engraver = Engraver()
        engraver.setOptions(options)
        guard engraver.load(mei) else {
            return .failure(EngraveError(message: "Verovio could not lay out the notation: \(engraver.log)"))
        }
        let pages = (1...max(engraver.pageCount, 1)).map { engraver.svg(page: $0) }
        let ossias = (answer["ossias"] as? [[String: Any]] ?? []).map { ossia in
            Engraving.Ossia(
                staves: (ossia["staves"] as? [[String: Any]] ?? []).compactMap { ($0["number"] as? NSNumber)?.intValue },
                prefix: ossia["idPrefix"] as? String ?? "",
                added: ossia["added"] as? [String] ?? [],
                artefacts: ossia["artefacts"] as? [String] ?? []
            )
        }
        return .success(Engraving(
            pages: pages, timemap: engraver.timemap(), ossias: ossias,
            version: answer["version"] as? [String] ?? [], own: answer["own"] as? [String] ?? []
        ))
    }

    private static func jsonString(_ text: String) -> String {
        let data = (try? JSONSerialization.data(withJSONObject: [text])) ?? Data("[\"\"]".utf8)
        return String(decoding: data, as: UTF8.self).dropFirst().dropLast().description
    }
}

/// A piece laid out: its pages as SVG, Verovio's timemap as JSON, and the ossias above an excerpt.
struct Engraving: Sendable {
    /// An ossia above the original on an excerpt: its staves' numbers in the document and its notes' id prefix.
    struct Ossia: Sendable, Equatable {
        let staves: [Int]
        let prefix: String
        /// Another version's ossia: the engraved ids of the notes it adds (plum) and of its likely artefacts (grey).
        var added: [String] = []
        var artefacts: [String] = []
    }

    let pages: [String]
    let timemap: String
    var ossias: [Ossia] = []
    /// On a merge draft's view, the engraved ids of the notes from the version (plum) and of the player's own (amber).
    var version: [String] = []
    var own: [String] = []
}

struct EngraveError: Error, Sendable {
    let message: String
}
