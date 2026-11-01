import Foundation
import Verovio

/// Verovio, built from its unmodified source as Verovio.xcframework (../build-verovio.sh), called
/// through its own C functions (tools/c_wrapper.h in Verovio). The strings Verovio returns belong
/// to the toolkit, so each one is copied before the next call.
final class Engraver {
    private let toolkit: UnsafeMutableRawPointer

    /// - Parameter resourcePath: the folder with Verovio's font data (verovio-data in the app).
    init(resourcePath: String) {
        toolkit = resourcePath.withCString { vrvToolkit_constructorResourcePath($0) }
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

    /// Loads MEI or MusicXML and lays it out into pages.
    func load(_ data: String) -> Bool {
        data.withCString { vrvToolkit_loadData(toolkit, $0) }
    }

    var pageCount: Int { Int(vrvToolkit_getPageCount(toolkit)) }

    /// One page as SVG; pages count from 1.
    func svg(page: Int) -> String {
        String(cString: vrvToolkit_renderToSVG(toolkit, Int32(page), false))
    }

    /// For each moment, the notes that start and stop there (the web app syncs playback with it).
    func timemap() -> String {
        String(cString: vrvToolkit_renderToTimemap(toolkit, "{}"))
    }

    var log: String { String(cString: vrvToolkit_getLog(toolkit)) }

    /// The web app's A4 page (Scripts/score/score-page.ts, decision record 0017) in Leland, the
    /// music font of the redesign.
    static let a4Leland = """
    {"scale": 100, "pageWidth": 2100, "pageHeight": 2970, "pageMarginLeft": 150, "pageMarginRight": 150,
     "pageMarginTop": 120, "pageMarginBottom": 150, "adjustPageHeight": false, "justifyVertically": true,
     "breaks": "auto", "header": "auto", "footer": "none", "svgViewBox": true, "font": "Leland"}
    """
}

/// What engraving one piece took: music-core's MEI, Verovio's layout, and the SVG of its pages.
struct ScoreReport: Sendable {
    var version = ""
    var notes = 0
    var meiMs = 0.0
    var layoutMs = 0.0
    var firstPageMs = 0.0
    var allPagesMs = 0.0
    var timemapMs = 0.0
    var pages = 0
    var svgBytes = 0
    var svgPages: [String] = []
    var error: String?

    var totalMs: Double { meiMs + layoutMs + allPagesMs }

    /// Runs the score view's pipeline on a piece: music-core writes MEI, Verovio lays it out on A4
    /// pages and draws every page as SVG. Call it on a core thread (MusicCore.onCoreThread).
    static func engrave(_ piece: SamplePiece) -> ScoreReport {
        var report = ScoreReport()
        let clock = ContinuousClock()
        func ms(_ duration: Duration) -> Double {
            Double(duration.components.seconds) * 1000 + Double(duration.components.attoseconds) / 1e15
        }

        let data: Data
        do { data = try piece.data() } catch {
            report.error = error.localizedDescription
            return report
        }

        var mei = ""
        report.meiMs = ms(clock.measure {
            let json = MusicCore.mei(fileName: piece.file, data: data)
            let object = (try? JSONSerialization.jsonObject(with: Data(json.utf8))) as? [String: Any]
            mei = object?["mei"] as? String ?? ""
            report.notes = object?["notes"] as? Int ?? 0
            if mei.isEmpty { report.error = json }
        })
        guard report.error == nil else { return report }

        let engraver = Engraver(resourcePath: Engraver.bundledResourcePath)
        report.version = engraver.version
        engraver.setOptions(Engraver.a4Leland)
        var loaded = false
        report.layoutMs = ms(clock.measure { loaded = engraver.load(mei) })
        guard loaded else {
            report.error = "Verovio could not load the MEI: \(engraver.log)"
            return report
        }

        report.pages = engraver.pageCount
        report.firstPageMs = ms(clock.measure { _ = engraver.svg(page: 1) })
        var pages: [String] = []
        report.allPagesMs = ms(clock.measure {
            pages = (1...max(report.pages, 1)).map { engraver.svg(page: $0) }
        })
        report.svgPages = pages
        report.svgBytes = pages.reduce(0) { $0 + $1.utf8.count }
        report.timemapMs = ms(clock.measure { _ = engraver.timemap() })
        return report
    }
}
