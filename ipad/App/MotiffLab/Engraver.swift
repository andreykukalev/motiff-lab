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

    /// The web app's A4 page (Scripts/score/score-page.ts, decision record 0017) in Leland, the redesign's music font.
    static let a4Leland = """
    {"scale": 100, "pageWidth": 2100, "pageHeight": 2970, "pageMarginLeft": 150, "pageMarginRight": 150,
     "pageMarginTop": 120, "pageMarginBottom": 150, "adjustPageHeight": false, "justifyVertically": true,
     "breaks": "auto", "header": "auto", "footer": "none", "svgViewBox": true, "font": "Leland"}
    """

    /// The pages of a piece's notation as SVG with Verovio's timemap: music-core writes the MEI (`notation`), Verovio
    /// lays it out on A4 pages. Call it through `MusicCore.run`.
    static func engrave(piece: Int32, title: String) -> Result<Engraving, EngraveError> {
        layOut(piece: piece, request: "{\"title\": \(jsonString(title))}", options: a4Leland)
    }

    /// The bars of a phrase in focus engraved on their own (0018 section 4): music-core writes the excerpt
    /// (`notation` with `bars`, 0021) and Verovio lays it out with the card's options, which the score page gives as
    /// the web app's phrase card has them (Scripts/score/phrase-card.ts, `cardLayout`). Call it through `MusicCore.run`.
    static func engraveCard(piece: Int32, firstBar: Int, lastBar: Int, options: String) -> Result<Engraving, EngraveError> {
        layOut(piece: piece, request: "{\"bars\": {\"first\": \(firstBar), \"last\": \(lastBar)}}", options: options)
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
        return .success(Engraving(pages: pages, timemap: engraver.timemap()))
    }

    private static func jsonString(_ text: String) -> String {
        let data = (try? JSONSerialization.data(withJSONObject: [text])) ?? Data("[\"\"]".utf8)
        return String(decoding: data, as: UTF8.self).dropFirst().dropLast().description
    }
}

/// A piece laid out: its pages as SVG and Verovio's timemap as JSON.
struct Engraving: Sendable {
    let pages: [String]
    let timemap: String
}

struct EngraveError: Error, Sendable {
    let message: String
}
