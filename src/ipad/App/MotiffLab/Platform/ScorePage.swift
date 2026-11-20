import CoreGraphics
import WebKit

// The score's web page (`Web/score.js` over `Scripts/score`) as the app sees it, through one port (decision record 0031,
// step 2): the functions the page offers (`window.motiff`) and the messages it posts. The two lists are the contract,
// kept in `tests/contracts/score-page.json` and checked against `score.js` by a node test and against this file by
// `ScorePageContractTests`, so a function or a message renamed on one side fails both suites, not the page on the iPad.

/// The functions of `window.motiff`; the only place that names them.
enum PageCommand: String, CaseIterable {
    case show, layout, anchor, erase, eraseAlong, rub, rubEnd, input, draw, frames, inView, select, menuSize, menuMoved
    case menuClosed, pen, beats, focus, card, inset, highlight, bookmarks, clearLeft, outline, showPage, showTick
    case firstTickOn, placeAt, lineAt, reveal, notes, checks, performance, layers, playing, returnToPlayhead, mode
    case playheads, practice, selectionControls, writing
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
        case pages, menu, chosen, cleared, gestureCancelled, holdHint, picked, zoomSelection, tuplets, bar, bookmark
        case noteMarker, handles, playheadAway, checkFlag, performanceBar, place, heads, drag, blank, leave, cardLayout
        case coloured
    }

    case pages(first: Int, last: Int, count: Int)
    case menu(CGPoint?)
    /// A span the selection rule chose: by bars, by beats (`beats`) or by places on the notes (`places`).
    case chosen([String: Any], start: Double, end: Double)
    case cleared(gesture: String?)
    case gestureCancelled(String)
    case holdHint
    case picked(id: String, [String: Any])
    case zoomSelection([String: Any]?)
    case tuplets([[String: Any]])
    case bar(start: Double, end: Double)
    case bookmark(String)
    case noteMarker(String)
    case handles([CGPoint])
    case playheadAway(Double)
    case checkFlag(Int)
    case performanceBar(Int, left: Double, bottom: Double)
    /// A draft's приём number tapped (v1.1): a `performanceBar` message with the number's key, and where the number is.
    case techniqueNumber(String, left: Double, bottom: Double)
    /// A draft's seam diamond tapped (v1.1): a `performanceBar` message with the seam's key, and where the diamond is.
    case seamDiamond(String, left: Double, bottom: Double)
    case place(start: Double, end: Double)
    case heads([CGPoint])
    case drag(PlayheadGesture)
    case blank
    case leave
    case cardLayout([String: Any])
    /// A passage the pen's colour bars coloured (finding 31), as Select would have selected it; the selection stays.
    case coloured(start: Double, end: Double)

    /// The events of one message, in the order of `Kind`; what does not decode is left out.
    static func events(in message: [String: Any]) -> [PageEvent] {
        Kind.allCases.compactMap { kind in message.keys.contains(kind.rawValue) ? event(kind, message) : nil }
    }

    private static func event(_ kind: Kind, _ message: [String: Any]) -> PageEvent? {
        selectionEvent(kind, message[kind.rawValue]) ?? markEvent(kind, message[kind.rawValue], message)
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
        case .gestureCancelled:
            return (value as? String).map { .gestureCancelled($0) }
        case .holdHint:
            return .holdHint
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

    /// A marker tapped over the bars, at its bottom left: a draft's приём number, a seam's diamond, else a version's or a
    /// place's label at its bar.
    private static func markerTap(_ value: Any?, _ message: [String: Any]) -> PageEvent? {
        let left = number(message["left"]) ?? 0
        let bottom = number(message["bottom"]) ?? 0
        if let key = message["technique"] as? String { return .techniqueNumber(key, left: left, bottom: bottom) }
        if let key = message["seam"] as? String { return .seamDiamond(key, left: left, bottom: bottom) }
        return number(value).map { .performanceBar(Int($0), left: left, bottom: bottom) }
    }

    /// The pages, the marks on them, the playheads and Expand's card.
    private static func markEvent(_ kind: Kind, _ value: Any?, _ message: [String: Any]) -> PageEvent? {
        let fields = value as? [String: Any]
        switch kind {
        case .pages:
            return fields.map { .pages(first: int($0["first"]), last: int($0["last"]), count: int($0["count"])) }
        case .bookmark:
            return (value as? String).map { .bookmark($0) }
        case .noteMarker:
            return (value as? String).map { .noteMarker($0) }
        case .checkFlag:
            return number(value).map { .checkFlag(Int($0)) }
        case .performanceBar:
            return markerTap(value, message)
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
