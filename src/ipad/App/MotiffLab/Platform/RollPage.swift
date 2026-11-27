import WebKit

// The roll's web page (`Web/roll.js` over `Scripts/roll`) as the app sees it, through one port (decision record 0031,
// step 12; the v0.7 blueprint's B7), as `ScorePage` is the score's: the functions the page offers (`window.roll`), what
// they answer, and the messages it posts. The lists and an example of each are the contract, kept in
// `tests/contracts/roll-page.json` and checked against `roll.js` by `tests/shared/roll-page-contract.test.mjs` and
// against this file by `RollPageContractTests`, so a function, an argument's field (at any depth roll.js reads it) or a
// message renamed on one side fails both suites, not the roll on the iPad. What roll.js passes on whole to piano-roll.ts
// unread (the labels, the frame, the tuplets, the arrays of music-core's payload) is checked there by key no further.

/// The functions of `window.roll`; the only place that names them.
enum RollCommand: String, CaseIterable {
    case load, nextColour, colour, theme, position, selection, noteNames, highlight, edit, lanes, practice, input, shape
    case zoom, frame, passage, tuplets
}

/// Calls the roll page's functions, one typed method each: the one place that builds the roll's JavaScript and reads
/// what a function answers. A call whose failure does not matter (the page draws what it was sent, or it is not loaded
/// yet) swallows it, as the roll always has; only `load` throws.
@MainActor
final class RollPage {
    /// Runs a function's body in the page and answers what it returns.
    typealias Run = @MainActor (_ script: String, _ arguments: [String: Any]) async throws -> Any?

    private let run: Run

    init(run: @escaping Run) {
        self.run = run
    }

    /// The page in the roll's web view, in the page's world.
    convenience init(_ webView: WKWebView) {
        self.init { script, arguments in
            try await webView.callAsyncJavaScript(script, arguments: arguments, contentWorld: .page)
        }
    }

    private func call(_ command: RollCommand, _ argument: Any? = nil) async throws -> Any? {
        let (script, arguments) = pageScript("roll", command.rawValue, argument)
        return try await run(script, arguments)
    }

    private func send(_ command: RollCommand, _ argument: Any) async {
        _ = try? await call(command, argument)
    }

    /// Shows a piece (`roll`, music-core's answer passed on with every field it writes) and answers the colouring shown and
    /// whether another one tells the notes apart; `frame` the zoom and view to start from, nil for none.
    func load(roll: RollAnswer, labels: [String: String], noteNames: Bool, dark: Bool, colourBy: String, keep: Bool, frame: [String: Double]?) async throws -> RollShown {
        let data: [String: Any] = [
            "roll": roll.argument, "labels": labels, "noteNames": noteNames, "dark": dark, "colourBy": colourBy, "keep": keep, "frame": frame.map { $0 as Any } ?? NSNull(),
        ]
        return RollShown(try await call(.load, data))
    }

    /// The next colouring that tells the notes apart; nil when the page did not answer.
    func nextColour() async -> String? {
        let answer = try? await call(.nextColour)
        return colouring(answer, of: .nextColour)
    }

    /// The colouring chosen in Settings, when the piece allows it; answers the colouring shown, nil when the page did not answer.
    func colour(_ by: String) async -> String? {
        let answer = try? await call(.colour, by)
        return colouring(answer, of: .colour)
    }

    private func colouring(_ answer: Any?, of command: RollCommand) -> String? {
        guard let answer else { return nil }
        guard let by = answer as? String else {
            RollEvent.broken("the roll page's \(command.rawValue) answered no colouring")
            return nil
        }
        return by
    }

    /// The roll's zoom and view now, for the "Notes" view; nil before the roll first drew, or for a value that is no number.
    func frame() async -> [String: Double]? {
        let answer = try? await call(.frame)
        guard let object = answer as? [String: Any] else { return nil }
        var frame: [String: Double] = [:]
        for key in ["zoom", "rowScale", "scroll", "pitchTop"] {
            guard let value = (object[key] as? NSNumber)?.doubleValue, value.isFinite else { return nil }
            frame[key] = value
        }
        return frame
    }

    func theme(dark: Bool) async {
        await send(.theme, dark)
    }

    /// Where the player is; with the lanes also the place as bar.beat and whether the selection loops.
    func position(_ tick: Double, playing: Bool, label: String, looping: Bool) async {
        await send(.position, ["tick": tick, "playing": playing, "label": label, "looping": looping] as [String: Any])
    }

    func selection(_ shown: RollShownSelection) async {
        await send(.selection, shown.argument)
    }

    func noteNames(_ on: Bool) async {
        await send(.noteNames, on)
    }

    /// The places of a fact lit on the roll; none clears them.
    func highlight(_ places: [RollLitPlace]) async {
        await send(.highlight, ["places": places.map { ["onset": $0.onset, "end": $0.end, "noteIds": $0.noteIds] as [String: Any] }])
    }

    func edit(_ editor: RollEditorShown) async {
        await send(.edit, editor.argument)
    }

    func lanes(_ on: Bool) async {
        await send(.lanes, on)
    }

    func practice(_ on: Bool) async {
        await send(.practice, on)
    }

    /// Who edits and who moves the roll: RollInput.page with RollPlaceInput's fields, as they build it.
    func input(_ fields: [String: Any]) async {
        await send(.input, fields)
    }

    /// How Select picks notes: "box" or "lasso".
    func shape(_ by: String) async {
        await send(.shape, by)
    }

    /// A step of the zoom: `axis` "time", "pitch", "fit" or "overview"; `direction` +1 in, −1 out.
    func zoom(_ axis: String, _ direction: Int) async {
        await send(.zoom, ["axis": axis, "direction": direction] as [String: Any])
    }

    /// Binds the roll to the passage Expand shows, or lets it go (nil).
    func passage(_ span: Range<Double>?) async {
        await send(.passage, span.map { ["start": $0.lowerBound, "end": $0.upperBound] as Any } ?? NSNull())
    }

    /// The tuplet groups as blocks ({start, end} in ticks).
    func tuplets(_ spans: [[String: Double]]) async {
        await send(.tuplets, spans)
    }
}

extension RollAnswer {
    /// The answer as `load` passes it to the page: music-core's fields with their names, as `roll-model.ts` reads them.
    var argument: [String: Any] {
        let spelled: [String: Any] = [
            "notes": names.notes, "keyTicks": names.keyTicks, "rows": names.rows, "short": names.short, "shortRows": names.shortRows,
            "octaves": names.octaves, "voices": names.voices.map { ["track": $0.track, "voice": $0.voice, "name": $0.name] as [String: Any] },
        ]
        return ["ids": ids, "notes": notes, "bars": bars, "end": end, "names": spelled]
    }
}

/// What `load` answers: the colouring shown ("hand" when it says none) and whether the piece allows another.
struct RollShown: Equatable {
    var colourBy: String
    var canChangeColour: Bool

    init(_ answer: Any?) {
        let shown = answer as? [String: Any]
        if shown == nil { RollEvent.broken("the roll page's load answered no object") }
        colourBy = shown?["colourBy"] as? String ?? "hand"
        canChangeColour = ((shown?["modes"] as? [Any])?.count ?? 0) > 1
    }
}

/// What `selection` shows: the selection's span shaded with its notes lit (and ringed when it was chosen by notes,
/// `picked`), or none; and the span that loops, or none.
struct RollShownSelection: Equatable {
    struct Selected: Equatable {
        var start: Double
        var end: Double
        var ids: [String]
        var picked: [String]?
    }

    var selected: Selected?
    var loop: Range<Double>?

    var argument: [String: Any] {
        var data: [String: Any] = [:]
        if let selected {
            var fields: [String: Any] = ["start": selected.start, "end": selected.end, "ids": selected.ids]
            if let picked = selected.picked { fields["picked"] = picked }
            data["selected"] = fields
        }
        if let loop {
            data["loop"] = ["start": loop.lowerBound, "end": loop.upperBound]
        }
        return data
    }
}

/// A place of a fact lit on the roll (`highlight`): its onset, end and notes.
struct RollLitPlace: Equatable {
    var onset: Double
    var end: Double
    var noteIds: [String]
}

/// What `edit` shows: the note editor on or off, its tool, and the note picked (sent only while it is on).
struct RollEditorShown: Equatable {
    var on = false
    var tool = "move"
    var picked: String?

    var argument: [String: Any] {
        var data: [String: Any] = ["on": on, "tool": tool]
        if on, let picked { data["picked"] = picked }
        return data
    }
}

/// What the roll page posts (`post({ <kind>: … })` in roll.js), each kind decoded in one place. A message carries one
/// kind; the page's first message, the string "ready", is no event.
enum RollEvent {
    /// Where a broken contract of the page goes (a message or an answer that is not what the page sends): a log line, and an
    /// assertion in Debug builds (FacadeAnswer.loud); a test that breaks one on purpose puts its own here and back.
    static var broken: (String) -> Void = FacadeAnswer.loud

    /// The keys of the page's messages, in the order the app acts on them; the contract's list.
    enum Kind: String, CaseIterable {
        case range, zoom, clear, gestureCancelled, holdHint, space, seek, drag, edit, span, notes, moveBegan, moved
    }

    /// Bars dragged across or a bar number tapped, on the bar numbers or the selection's handles.
    case range(RollRange)
    case zoom(RollZoom)
    case cleared(gesture: String?)
    case gestureCancelled(String)
    case holdHint
    case space
    /// The playhead moved to a tick, and with `play` a tap that plays from there.
    case seek(tick: Double, play: Bool)
    /// A lane's playhead taken, dragged or let go: `{ phase, handle, tick }`, read by Playback's PlayheadGesture as the
    /// score's is.
    case drag([String: Any])
    case edit(RollNoteEdit)
    case span(RollSpan)
    case notes(RollNotesPick)
    case moveBegan
    /// Picked notes let go moved, one change each; never empty.
    case moved([RollNoteEdit])

    /// The events of one message, in the order of `Kind`; what does not decode is left out, and a kind whose value is not
    /// the object the page posts, or a message with no kind of the contract's, is reported.
    static func events(in message: [String: Any]) -> [RollEvent] {
        let kinds = Kind.allCases.filter { message[$0.rawValue] != nil }
        if kinds.isEmpty { RollEvent.broken("the roll page posted a message of no kind the app knows: \(message.keys.sorted())") }
        return kinds.compactMap { event($0, message[$0.rawValue]) }
    }

    private static func event(_ kind: Kind, _ value: Any?) -> RollEvent? {
        switch kind {
        case .clear:
            return .cleared(gesture: (value as? [String: Any])?["gesture"] as? String)
        case .gestureCancelled:
            return (value as? String).map { .gestureCancelled($0) }
        case .holdHint:
            return .holdHint
        case .space:
            return .space
        case .moveBegan:
            return .moveBegan
        default:
            guard let fields = value as? [String: Any] else {
                RollEvent.broken("the roll page's \(kind.rawValue) is no object")
                return nil
            }
            return objectEvent(kind, fields)
        }
    }

    /// The kinds whose value is an object.
    private static func objectEvent(_ kind: Kind, _ fields: [String: Any]) -> RollEvent? {
        switch kind {
        case .range:
            return RollRange(fields).map { .range($0) }
        case .zoom:
            return RollZoom(fields).map { .zoom($0) }
        case .seek:
            return (fields["tick"] as? NSNumber).map { .seek(tick: $0.doubleValue, play: (fields["play"] as? Bool) ?? false) }
        case .drag:
            return .drag(fields)
        case .edit:
            return RollNoteEdit(fields).map { .edit($0) }
        case .span:
            return RollSpan(fields).map { .span($0) }
        case .notes:
            return RollNotesPick(fields).map { .notes($0) }
        case .moved:
            let edits = RollNoteEdit.moved(fields["notes"])
            return edits.isEmpty ? nil : .moved(edits)
        default:
            return nil
        }
    }
}

/// The roll's `range` message: `{ first, last, extend, click, from, to, how, gesture }`, the bars chosen and the ticks
/// from where the drag began to where it ended, either way round (`how` is not read).
struct RollRange: Equatable, Sendable {
    var first: Double
    var last: Double
    var extend: Bool
    var click: Bool
    var from: Double?
    var to: Double?
    var gesture: String?

    /// Nil without the bars.
    init?(_ message: [String: Any]) {
        func number(_ key: String) -> Double? { (message[key] as? NSNumber)?.doubleValue }
        guard let first = number("first"), let last = number("last") else { return nil }
        self.first = first
        self.last = last
        extend = message["extend"] as? Bool ?? false
        click = message["click"] as? Bool ?? false
        from = number("from")
        to = number("to")
        gesture = message["gesture"] as? String
    }
}

/// The roll's zoom as it reports it (`roll-zoom.ts`): the bars and the octaves in view, which steps are left, and whether
/// Zoom Out shows (#521 2.2: after Fit laid a selection across the roll, until Zoom Out or the selection goes).
struct RollZoom: Equatable, Sendable {
    var bars: Int
    var octaves: Int
    var timeIn = true
    var timeOut = true
    var pitchIn = true
    var pitchOut = true
    var zoomOut = false

    init(bars: Int, octaves: Int, timeIn: Bool = true, timeOut: Bool = true, pitchIn: Bool = true, pitchOut: Bool = true, zoomOut: Bool = false) {
        self.bars = bars
        self.octaves = octaves
        self.timeIn = timeIn
        self.timeOut = timeOut
        self.pitchIn = pitchIn
        self.pitchOut = pitchOut
        self.zoomOut = zoomOut
    }

    /// From the roll's message: `{ bars, octaves, timeIn, timeOut, pitchIn, pitchOut, zoomOut }`.
    init?(_ message: [String: Any]) {
        guard let bars = (message["bars"] as? NSNumber)?.intValue, let octaves = (message["octaves"] as? NSNumber)?.intValue else { return nil }
        self.bars = bars
        self.octaves = octaves
        timeIn = message["timeIn"] as? Bool ?? true
        timeOut = message["timeOut"] as? Bool ?? true
        pitchIn = message["pitchIn"] as? Bool ?? true
        pitchOut = message["pitchOut"] as? Bool ?? true
        zoomOut = message["zoomOut"] as? Bool ?? false
    }
}

/// A note moved (its new onset and length in ticks, and pitch), added where the player pressed, or taken out, with the note
/// editor on (testing item 15).
enum RollNoteEdit: Equatable, Sendable {
    case change(id: String, onset: Int, pitch: Int, length: Int)
    case add(onset: Int, pitch: Int, length: Int)
    case remove(id: String)

    init?(_ message: [String: Any]) {
        func number(_ key: String) -> Int? { (message[key] as? NSNumber)?.intValue }
        switch message["kind"] as? String {
        case "change":
            guard let id = message["id"] as? String, let onset = number("onset"), let pitch = number("pitch"), let length = number("length") else { return nil }
            self = .change(id: id, onset: onset, pitch: pitch, length: length)
        case "add":
            guard let onset = number("onset"), let pitch = number("pitch"), let length = number("length") else { return nil }
            self = .add(onset: onset, pitch: pitch, length: length)
        case "remove":
            guard let id = message["id"] as? String else { return nil }
            self = .remove(id: id)
        default:
            return nil
        }
    }

    /// Picked notes let go moved (`moved.notes`: `[{ id, onset, pitch, length }]`), each a change on the draft, as a note
    /// moved in Edit notes is; a malformed entry is left out.
    static func moved(_ notes: Any?) -> [RollNoteEdit] {
        guard let notes = notes as? [[String: Any]] else { return [] }
        return notes.compactMap { note in
            var change = note
            change["kind"] = "change"
            return RollNoteEdit(change)
        }
    }
}

/// Notes the roll picked in Select (`notes` message): their ids, how they change the selection, and the gesture. A box
/// from outside replaces the selection (the toast «Выделение заменено · Вернуть»), one from inside adds to it, an edge moved
/// narrows the picked notes (an edit, no toast).
struct RollNotesPick: Equatable, Sendable {
    enum Change: String, Sendable {
        case create, replace, add, edit
    }

    var ids: [String]
    var change: Change
    var gesture: String?

    init(ids: [String], change: Change, gesture: String? = nil) {
        self.ids = ids
        self.change = change
        self.gesture = gesture
    }

    /// From `{ ids, change, gesture }`; nil without notes. An unknown change is a new selection.
    init?(_ message: Any?) {
        guard let message = message as? [String: Any], let ids = message["ids"] as? [String], !ids.isEmpty else { return nil }
        self.init(ids: ids, change: (message["change"] as? String).flatMap(Change.init(rawValue:)) ?? .create, gesture: message["gesture"] as? String)
    }

    /// Whether the notes add to those picked (ScoreController.selectNotes `add`).
    var add: Bool { change == .add }

    /// Whether they replace a selection there was, so the old one is a step back with its toast (`replaces`).
    var replaces: Bool { change == .replace }
}

/// A span of the one selection the roll's place rule drew on the grid (v0.6.4, Web/roll.js `span`: a rectangle, an
/// extension, an edge moved, a bar tapped), selected exactly as it is, notes in it or none; `how` is "create", "replace" or
/// "edit".
struct RollSpan: Equatable, Sendable {
    var start: Double
    var end: Double
    var how: String
    var gesture: String?

    /// From `{ start, end, how, gesture }`; nil without a span of time.
    init?(_ message: Any?) {
        guard let message = message as? [String: Any], let start = (message["start"] as? NSNumber)?.doubleValue,
              let end = (message["end"] as? NSNumber)?.doubleValue, end > start else { return nil }
        self.start = start
        self.end = end
        how = message["how"] as? String ?? "create"
        gesture = message["gesture"] as? String
    }
}
