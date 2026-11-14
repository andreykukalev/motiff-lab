import Foundation

/// PDF | Notes keep each view as the player left it (#511 section 10). The two views are two screens, and the library opens
/// the other one from the project file, so each view is written to the device as it goes (to the other view, the library
/// or the background) and brought back in place of the landing on the same stretch of music when the player comes back to
/// it from the other one (`Landing.kept`). The places are the web app's (Scripts/score/view-anchor.ts): a system of the
/// notes, by the tick of its first bar, or a page of the PDF, and how far into it the view is, not pixels, so a new
/// layout (the notes engraved again, a turn of the iPad) comes back on the same bar and repeated switching does not drift.

/// A stretch of a view from top to bottom, in its content's points: a system of the notes, or a page of the PDF.
struct AnchorSpan: Equatable, Sendable {
    /// What names it across layouts: the tick its first bar starts at, or the page's number.
    var key: Double
    var top: Double
    var bottom: Double
}

/// A place in a view: the span named by `key`, and how far into its height the place lies (0 its top, 1 its bottom).
struct ViewAnchor: Codable, Equatable, Sendable {
    var key: Double
    var fraction: Double

    init(key: Double, fraction: Double) {
        self.key = key
        self.fraction = fraction
    }

    /// A place from the score's page (`motiff.placeAt`): both numbers, finite; nil for anything else.
    init?(page value: Any?) {
        guard let object = value as? [String: Any],
              let key = (object["key"] as? NSNumber)?.doubleValue, let fraction = (object["fraction"] as? NSNumber)?.doubleValue,
              key.isFinite, fraction.isFinite else { return nil }
        self.init(key: key, fraction: fraction)
    }

    /// The place as the score's page reads it (`motiff.lineAt`).
    var page: [String: Double] { ["key": key, "fraction": fraction] }

    /// The place a line of the view lies at: the span it crosses, or the next one below when it lies in the gap between two
    /// (a share below zero, so the gap is kept), or the last span when it lies below them all; nil without spans.
    static func at(_ y: Double, in spans: [AnchorSpan]) -> ViewAnchor? {
        let ordered = inOrder(spans)
        guard y.isFinite, let last = ordered.last else { return nil }
        let span = ordered.first { $0.bottom > y } ?? last
        return ViewAnchor(key: span.key, fraction: (y - span.top) / max(1, span.bottom - span.top))
    }

    /// Where the line of this place lies now: in the span of the same key, or, laid out anew, in the last span that starts at
    /// or before it, at the same share of its height; nil without spans.
    func line(in spans: [AnchorSpan]) -> Double? {
        let ordered = Self.inOrder(spans)
        guard key.isFinite, fraction.isFinite, let first = ordered.first else { return nil }
        let span = ordered.first { $0.key == key } ?? ordered.filter { $0.key <= key }.max { $0.key < $1.key } ?? first
        return span.top + fraction * max(1, span.bottom - span.top)
    }

    /// The spans from the top down; side by side (two pages to a row), the one named first.
    private static func inOrder(_ spans: [AnchorSpan]) -> [AnchorSpan] {
        spans.filter { $0.top.isFinite && $0.bottom.isFinite }.sorted { ($0.top, $0.key) < ($1.top, $1.key) }
    }
}

/// The "Notes" view as the player left it: the selection, also a bar with no notes, as its span in ticks; where the score
/// was scrolled to and its zoom; where the music stood and whether Цикл looped it; and the roll's drawer with its zoom and view.
struct NotesViewState: Codable, Equatable, Sendable {
    var selection: SelectionSpanKept?
    var place: ViewAnchor?
    /// The page's zoom (the score's scroll view), 1 unzoomed.
    var zoom: Double = 1
    /// How far across the zoomed pages the view is, as a share of their width.
    var left: Double = 0
    var position: Double = 0
    var loop = false
    var rollOpen = false
    /// The roll's zoom and view (Scripts/roll/roll-view.ts `ViewFrame`: zoom, rowScale, scroll, pitchTop).
    var roll: [String: Double]?
}

/// A selection's span as the device keeps it.
struct SelectionSpanKept: Codable, Equatable, Sendable {
    var onset: Double
    var end: Double
}

/// The PDF as the player left it: the page under the bar and how far down it, how far across the pages, and its zoom as a
/// share of the zoom that fits the pages (nil while it fits), so a turn of the iPad keeps the zoom the player chose.
struct PdfViewState: Codable, Equatable, Sendable {
    var place: ViewAnchor
    var left: Double = 0
    var zoom: Double?

    /// The page it was left on, from 1.
    var page: Int { max(1, Int(place.key)) }
}

/// The views kept on this device, by the piece's project id (`motiff.view.notes.<id>`, `motiff.view.pdf.<id>`).
enum ViewStates {
    static func notesKey(_ id: String) -> String { "motiff.view.notes.\(id)" }
    static func pdfKey(_ id: String) -> String { "motiff.view.pdf.\(id)" }
    /// The page last under the bar, as the PDF view always kept it, the web app's `pdf.page.<id>` too.
    static func pdfPageKey(_ id: String) -> String { "motiff.pdf.page.\(id)" }

    static func notes(_ id: String, in defaults: UserDefaults = .standard) -> NotesViewState? {
        read(defaults.data(forKey: notesKey(id)))
    }

    static func keep(_ state: NotesViewState, _ id: String, in defaults: UserDefaults = .standard) {
        defaults.set(try? JSONEncoder().encode(state), forKey: notesKey(id))
    }

    /// The PDF's place while its page is still the page last under the bar (`pdfPageKey`, kept as the pages
    /// scroll): a PDF scrolled on and left without its place kept opens at the top of its page instead.
    static func pdf(_ id: String, shownPage: Int, in defaults: UserDefaults = .standard) -> PdfViewState? {
        guard let state: PdfViewState = read(defaults.data(forKey: pdfKey(id))) else { return nil }
        return shownPage <= 0 || state.page == shownPage ? state : nil
    }

    static func keep(_ state: PdfViewState, _ id: String, in defaults: UserDefaults = .standard) {
        defaults.set(try? JSONEncoder().encode(state), forKey: pdfKey(id))
        defaults.set(state.page, forKey: pdfPageKey(id))
    }

    /// Where a piece opened from its other view lands: as the player left this view, when it was kept on the device, else at
    /// the landing on the same stretch of music (the first switch). Opened from the library, with no landing, it opens as
    /// before.
    static func landing(_ landing: Landing?, for piece: OpenPiece, in defaults: UserDefaults = .standard) -> Landing? {
        guard let landing, let id = piece.own?.id else { return landing }
        let kept = piece.isDocument
            ? defaults.data(forKey: pdfKey(id)) != nil
            : notes(id, in: defaults) != nil
        return kept ? .kept : landing
    }

    private static func read<T: Decodable>(_ data: Data?) -> T? {
        guard let data else { return nil }
        return try? JSONDecoder().decode(T.self, from: data)
    }
}

/// The pieces the library closed in music-core as another took their screen (PDF | Notes, the notes read from a PDF): it
/// closes them as the next screen opens, before the old screen's `onDisappear`, which then wrote the resume point and the
/// project file to a closed piece (#511 section 10). The screen saves before it hands the piece over, and, going, saves
/// nothing more for a piece closed. Music-core never gives a closed piece's number again.
@MainActor
enum ClosedPieces {
    private static var closed: Set<Int32> = []

    static func close(_ piece: Int32) async {
        closed.insert(piece)
        _ = await MusicCore.run { MusicCore.close(piece: piece) }
    }

    /// Whether the piece was closed; asked once, as its screen goes.
    static func take(_ piece: Int32) -> Bool {
        closed.remove(piece) != nil
    }
}

/// The roll's zoom and view kept for a piece until its roll first loads on this screen (`RollController.load`), which
/// may be never if the drawer stays closed: then it is still what the "Notes" view keeps as it goes.
@MainActor
enum RollFrameHandoff {
    private static var pending: (piece: Int32, frame: [String: Double])?

    static func give(_ frame: [String: Double]?, to piece: Int32) {
        pending = frame.map { (piece, $0) }
    }

    /// The frame for the piece's roll as it loads, once; NSNull for none, as the page reads it.
    static func take(for piece: Int32) -> Any {
        guard let pending, pending.piece == piece else { return NSNull() }
        self.pending = nil
        return pending.frame
    }

    /// The frame waiting for the piece's roll, without taking it.
    static func waiting(for piece: Int32) -> [String: Double]? {
        pending?.piece == piece ? pending?.frame : nil
    }
}
