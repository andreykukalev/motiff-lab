import CoreGraphics
import WebKit

// The score's web page (`Web/score.html`, `Scripts/score/ipad-page`) as the app sees it, through one port (decision
// record 0031, step 2): the functions the page offers (`window.motiff`) and the messages it posts. The two lists are the
// contract, kept in `tests/contracts/score-page.json` and checked against the page and its types
// (`Scripts/score/page-contract.ts`) by a node test and against this file by `ScorePageContractTests`, so a function or a
// message renamed on one side fails both suites, not the page on the iPad.

/// The functions of `window.motiff`; the only place that names them.
enum PageCommand: String, CaseIterable {
    case show, layout, anchor, erase, eraseAlong, rub, rubEnd, input, draw, frames, inView, select, menuSize, menuMoved
    case menuClosed, pen, beats, focus, card, inset, highlight, bookmarks, clearLeft, outline, showPage, showTick
    case firstTickOn, placeAt, lineAt, reveal, notes, checks, performance, phraseMarks, layers, playing, returnToPlayhead
    case mode, playheads, writing, markForgotten
}

/// Calls the page's functions. `run` waits for the answer; `post` does not (the playhead, many times a second).
@MainActor
protocol ScorePage: AnyObject {
    func run(_ command: PageCommand, _ argument: Any?) async throws -> Any?
    func post(_ command: PageCommand, _ argument: Any)
}

extension ScorePage {
    @discardableResult
    func call(_ command: PageCommand, _ argument: Any) async throws -> Any? {
        try await run(command, argument)
    }

    @discardableResult
    func call(_ command: PageCommand) async throws -> Any? {
        try await run(command, nil)
    }

    /// A call whose answer and failure do not matter: the page draws what it was sent, or it is not there yet.
    func send(_ command: PageCommand, _ argument: Any) async {
        _ = try? await run(command, argument)
    }

    func send(_ command: PageCommand) async {
        _ = try? await run(command, nil)
    }
}

/// The page in the score's web view.
@MainActor
final class WebScorePage: ScorePage {
    private weak var webView: WKWebView?

    init(_ webView: WKWebView) {
        self.webView = webView
    }

    func run(_ command: PageCommand, _ argument: Any?) async throws -> Any? {
        guard let webView else { return nil }
        let (script, arguments) = Self.script(command, argument)
        return try await webView.callAsyncJavaScript(script, arguments: arguments, contentWorld: .page)
    }

    func post(_ command: PageCommand, _ argument: Any) {
        let (script, arguments) = Self.script(command, argument)
        webView?.callAsyncJavaScript(script, arguments: arguments, in: nil, in: .page, completionHandler: nil)
    }

    nonisolated static func script(_ command: PageCommand, _ argument: Any?) -> (String, [String: Any]) {
        pageScript("motiff", command.rawValue, argument)
    }
}

/// A call of a page's function (`object.function`) as a function's body for `callAsyncJavaScript`, with its one argument,
/// if any, passed as `argument`: the score's port and the roll's (`RollPage`) build their calls so.
func pageScript(_ object: String, _ function: String, _ argument: Any?) -> (String, [String: Any]) {
    guard let argument else { return ("return \(object).\(function)()", [:]) }
    return ("return \(object).\(function)(argument)", ["argument": argument])
}

/// What the page posts, each kind decoded in one place. A message carries one kind, `performanceBar` with its place (and a
/// приём's number or a seam's diamond its key).
enum PageEvent {
    /// The keys of the page's messages; the contract's list.
    enum Kind: String, CaseIterable {
        case pages, menu, chosen, cleared, picked, zoomSelection, tuplets, bar, bookmark
        case noteMarker, handles, playheadAway, checkFlag, performanceBar, phraseMark, sectionMark, phraseHeld, phraseMoved, motifMark
        case place, heads, drag, blank
        case leave, cardLayout, coloured, markMoved, markAway
    }

    case pages(first: Int, last: Int, count: Int)
    case menu(CGPoint?)
    /// A span the selection rule chose: by bars, by beats (`beats`) or by places on the notes (`places`).
    case chosen([String: Any], start: Double, end: Double)
    case cleared(gesture: String?)
    case picked(id: String, [String: Any])
    case zoomSelection([String: Any]?)
    case tuplets([[String: Any]])
    case bar(start: Double, end: Double)
    case bookmark(String)
    case noteMarker(String)
    case handles([CGPoint])
    case playheadAway(Double)
    case checkFlag(Int)
    /// A label over a bar tapped: its bar, and where it is: its bottom left and its top (`top`, v1.4 a-1, for a card opened
    /// above it; nil where the page did not tell it), as for each mark below.
    case performanceBar(Int, left: Double, bottom: Double, top: Double? = nil)
    /// A draft's приём number tapped (v1.1): a `performanceBar` message with the number's key, and where the number is.
    case techniqueNumber(String, left: Double, bottom: Double, top: Double? = nil)
    /// A draft's seam diamond tapped (v1.1): a `performanceBar` message with the seam's key, and where the diamond is.
    case seamDiamond(String, left: Double, bottom: Double, top: Double? = nil)
    /// A draft's anchor mark tapped (v1.2): a `performanceBar` message with the anchor's key, and where the mark is.
    case anchorMark(String, left: Double, bottom: Double, top: Double? = nil)
    /// A phrase's number tapped on the score (the phrases spec, decision 1): which phrase, from 1.
    case phraseMark(Int)
    /// A section's boxed letter tapped on the score: the section's id.
    case sectionMark(String)
    /// A phrase's number held still and let go (the phrases spec, «Polish of option A» 2): which phrase, and where its
    /// number's bottom left is on the page, for its menu.
    case phraseHeld(Int, left: Double, bottom: Double, top: Double? = nil)
    /// A phrase's mark dragged and let go on a note: the tick its phrase starts at and that note's onset.
    case phraseMoved(from: Double, to: Double)
    /// A motif's comma tapped on the card in focus (the phrases spec, decision 2): its phrase's number and its place in it,
    /// both from 1.
    case motifMark(phrase: Int, motif: Int)
    case place(start: Double, end: Double)
    case heads([CGPoint])
    case drag(PlayheadGesture)
    case blank
    case leave
    case cardLayout([String: Any])
    /// A passage the pen's colour bars coloured (finding 31), as Select would have selected it; the selection stays.
    case coloured(start: Double, end: Double)
    /// The mark a card shows at (a `performanceBar` or a `phraseHeld`) moved on the page, a scroll or a new layout: where
    /// its bottom left is now (r-1), and its top (v1.4 a-1).
    case markMoved(CGPoint, top: Double? = nil)
    /// That mark left the visible part of the page (playback turned the page, a scroll): its card closes (v1.4 a-1,
    /// answer 14); the page no longer follows it.
    case markAway

    /// How high the mark is whose place the event tells (a mark tapped for its card, or moved), in the web view's points,
    /// for a card opened above it (v1.4 a-1); nil for any other event, or a place told without its top.
    var markHeight: Double? {
        switch self {
        case let .performanceBar(_, _, bottom, top), let .techniqueNumber(_, _, bottom, top), let .seamDiamond(_, _, bottom, top),
             let .anchorMark(_, _, bottom, top), let .phraseHeld(_, _, bottom, top):
            return top.map { max(0, bottom - $0) }
        case let .markMoved(point, top):
            return top.map { max(0, Double(point.y) - $0) }
        default:
            return nil
        }
    }

    /// The events of one message, in the order of `Kind`; what does not decode is left out.
    static func events(in message: [String: Any]) -> [PageEvent] {
        Kind.allCases.compactMap { kind in message.keys.contains(kind.rawValue) ? event(kind, message) : nil }
    }

    private static func event(_ kind: Kind, _ message: [String: Any]) -> PageEvent? {
        selectionEvent(kind, message[kind.rawValue]) ?? tapEvent(kind, message[kind.rawValue]) ?? markEvent(kind, message[kind.rawValue], message)
    }

    /// The selection and its menu.
    private static func selectionEvent(_ kind: Kind, _ value: Any?) -> PageEvent? {
        let fields = value as? [String: Any]
        switch kind {
        case .menu:
            return .menu(fields.flatMap { place in number(place["left"]).map { CGPoint(x: $0, y: number(place["top"]) ?? 0) } })
        case .chosen:
            return span(fields).map { .chosen(fields ?? [:], start: $0.start, end: $0.end) }
        case .cleared:
            return fields.map { .cleared(gesture: $0["gesture"] as? String) }
        case .picked:
            return fields.flatMap { picked in (picked["id"] as? String).map { .picked(id: $0, picked) } }
        case .zoomSelection:
            return .zoomSelection(fields)
        case .tuplets:
            return (value as? [[String: Any]]).map { .tuplets($0) }
        case .bar:
            return span(fields).map { .bar(start: $0.start, end: $0.end) }
        case .handles:
            return (value as? [[String: Any]]).map { .handles(points($0)) }
        default:
            return nil
        }
    }

    /// A marker tapped over the bars, at its bottom left: a draft's приём number, a seam's diamond, an anchor's mark, else a
    /// version's or a place's label at its bar.
    private static func markerTap(_ value: Any?, _ message: [String: Any]) -> PageEvent? {
        let left = number(message["left"]) ?? 0
        let bottom = number(message["bottom"]) ?? 0
        let top = number(message["top"])
        if let key = message["technique"] as? String { return .techniqueNumber(key, left: left, bottom: bottom, top: top) }
        if let key = message["seam"] as? String { return .seamDiamond(key, left: left, bottom: bottom, top: top) }
        if let key = message["anchor"] as? String { return .anchorMark(key, left: left, bottom: bottom, top: top) }
        return number(value).map { .performanceBar(Int($0), left: left, bottom: bottom, top: top) }
    }

    /// A mark on the score tapped that names what it marks: a bookmark, a margin note, a Check flag, a phrase's number,
    /// a section's letter or a motif's comma in focus.
    private static func tapEvent(_ kind: Kind, _ value: Any?) -> PageEvent? {
        switch kind {
        case .bookmark:
            return (value as? String).map { .bookmark($0) }
        case .noteMarker:
            return (value as? String).map { .noteMarker($0) }
        case .checkFlag:
            return number(value).map { .checkFlag(Int($0)) }
        case .phraseMark:
            return number(value).map { .phraseMark(Int($0)) }
        case .sectionMark:
            return (value as? String).map { .sectionMark($0) }
        case .motifMark:
            guard let fields = value as? [String: Any], let phrase = number(fields["phrase"]), let motif = number(fields["motif"]) else { return nil }
            return .motifMark(phrase: Int(phrase), motif: Int(motif))
        default:
            return nil
        }
    }

    /// The pages, the version's markers on them, the playheads and Expand's card.
    private static func markEvent(_ kind: Kind, _ value: Any?, _ message: [String: Any]) -> PageEvent? {
        let fields = value as? [String: Any]
        switch kind {
        case .pages:
            return fields.map { .pages(first: int($0["first"]), last: int($0["last"]), count: int($0["count"])) }
        case .performanceBar:
            return markerTap(value, message)
        case .phraseHeld:
            return number(value).map { .phraseHeld(Int($0), left: number(message["left"]) ?? 0, bottom: number(message["bottom"]) ?? 0, top: number(message["top"])) }
        case .phraseMoved:
            return fields.flatMap { moved in number(moved["from"]).flatMap { from in number(moved["to"]).map { .phraseMoved(from: from, to: $0) } } }
        case .playheadAway:
            return number(value).map { .playheadAway($0) }
        case .place:
            return span(fields).map { .place(start: $0.start, end: $0.end) }
        case .heads:
            return (value as? [[String: Any]]).map { .heads(points($0)) }
        case .drag:
            return PlayheadGesture(message: value).map { .drag($0) }
        case .blank:
            return .blank
        case .leave:
            return .leave
        case .cardLayout:
            return fields.map { .cardLayout($0) }
        case .coloured:
            return span(fields).map { .coloured(start: $0.start, end: $0.end) }
        case .markMoved:
            return fields.flatMap { place in number(place["left"]).map { PageEvent.markMoved(CGPoint(x: $0, y: number(place["bottom"]) ?? 0), top: number(place["top"])) } }
        case .markAway:
            return .markAway
        default:
            return nil
        }
    }

    private static func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }

    private static func int(_ value: Any?) -> Int { Int(number(value) ?? 0) }

    private static func span(_ fields: [String: Any]?) -> (start: Double, end: Double)? {
        guard let fields, let start = number(fields["start"]), let end = number(fields["end"]) else { return nil }
        return (start, end)
    }

    private static func points(_ spots: [[String: Any]]) -> [CGPoint] {
        spots.compactMap { spot in number(spot["x"]).map { CGPoint(x: $0, y: number(spot["y"]) ?? 0) } }
    }
}
