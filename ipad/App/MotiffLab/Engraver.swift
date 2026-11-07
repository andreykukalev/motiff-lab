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

    /// The web app's A4 page in Leland, the redesign's music font: the same options as `A4_LAYOUT` in
    /// Scripts/score/score-page.ts (decision record 0017, design/kit/HANDOFF.md section 8), so a piece breaks into the same
    /// systems and pages on both, and a resume point, a page bookmark or a page count means the same page.
    /// tests/web/score-pdf.test.mjs keeps the two equal.
    static let a4Leland = """
    {"font": "Leland", "scale": 50, "pageWidth": 2100, "pageHeight": 2970, "pageMarginLeft": 150, "pageMarginRight": 150,
     "pageMarginTop": 500, "pageMarginBottom": 160, "spacingSystem": 20, "spacingStaff": 10, "spacingLinear": 0.08,
     "spacingNonLinear": 0.9, "adjustPageHeight": false,
     "justifyVertically": false, "breaks": "auto", "header": "auto", "footer": "none", "svgViewBox": true}
    """

    /// The pages of a piece's notation as SVG with Verovio's timemap: music-core writes the MEI (`notation`), Verovio
    /// lays it out on A4 pages. Call it through `MusicCore.run`.
    /// With `version`, another version's id, it stands on small staves above every system as the comparison layer (0022,
    /// section 4), its likely artefacts too with `artefacts`.
    static func engrave(piece: Int32, title: String, version: String? = nil, artefacts: Bool = false) -> Result<Engraving, EngraveError> {
        let above = version.map { ", \"ossia\": \(jsonString($0)), \"artefacts\": \(artefacts)" } ?? ""
        return layOut(piece: piece, request: "{\"title\": \(jsonString(title))\(above)}", options: a4Leland)
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

    private static func layOut(piece: Int32, request: String, options: String) -> Result<Engraving, EngraveError> {
        guard let answer = facadeObject(MusicCore.call(piece: piece, "notation", request)) else {
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
        return .success(Engraving(pages: pages, timemap: engraver.timemap(), ossias: ossias))
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
}

struct EngraveError: Error, Sendable {
    let message: String
}
