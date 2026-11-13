import SwiftUI
import WebKit

/// The piano roll in the drawer (0018 section 4): the web app's own roll (Scripts/roll/piano-roll.ts) on canvas in a web
/// view of its own (Web/roll.html), as the score is. music-core gives its data (`roll`: the notes and bars packed as the
/// roll reads them, and their names spelled as the score spells them). Swift tells it where the player is and what is
/// selected, and lights the places of the fact chosen in Insights; what the player does on it comes back here: bars dragged across or a bar number tapped select those bars,
/// Escape clears the selection (a tap never does, #338), Space plays or pauses; the playhead dragged or nudged by a beat moves
/// the player there, and a tap inside the loop or the selection plays from there (v0.2); with the note editor on (testing item
/// 15), a note moved, added or taken out goes to music-core as a correction on the draft. Since v0.5.2 (#479 item 23) the
/// roll is where notes are edited: its own modes, Navigate and Edit notes (RollMode, who edits is RollInput's), the zoom on
/// each axis it reports and Swift draws (RollTools), and a drag across its bar numbers selecting by beats as the score does.
@MainActor
final class RollController: NSObject, ObservableObject {
    /// The mode chosen on the roll's switch (#479 item 23); Edit notes holds while the note editor is open.
    @Published var mode: RollMode = .navigate {
        didSet {
            guard mode != oldValue else { return }
            sendEditor()
        }
    }
    /// The bars and the octaves in view, as the roll last reported them; nil until it has.
    @Published private(set) var zoom: RollZoom?
    /// The mode the roll is in: Edit notes only while the editor is open.
    var shownMode: RollMode { RollMode.shown(chosen: mode, editorOpen: editorState != nil) }
    /// Whether each note carries its name (the web app's Note names chip, the setting "roll.noteNames").
    @Published var noteNames = true {
        didSet {
            if loaded { Task { _ = try? await webView.callAsyncJavaScript("return roll.noteNames(on)", arguments: ["on": noteNames], contentWorld: .page) } }
            if !DeviceSettings.shared.values.isEmpty { DeviceSettings.shared.set("roll.noteNames", noteNames ? "on" : "off") }
        }
    }
    /// What the notes are coloured by: "hand", "track" or "voice".
    @Published private(set) var colourBy = "hand"
    /// Whether another colour scheme tells this piece's notes apart.
    @Published private(set) var canChangeColour = false
    @Published private(set) var problem: String?

    /// A bar tapped or a stretch dragged on the roll's bar numbers or the selection's handles; `byBeats` says how a drag
    /// selects (by beats where the score does).
    var picked: (@MainActor (RollPick) -> Void)?
    var byBeats: (@MainActor () -> Bool)?
    var cleared: (@MainActor () -> Void)?
    var spacePressed: (@MainActor () -> Void)?
    /// The playhead moved on the roll: to a tick, and with `play` a tap that plays from there.
    var playheadMoved: (@MainActor (_ tick: Double, _ play: Bool) -> Void)?
    /// A note moved, added or taken out with the note editor on.
    var noteEdited: (@MainActor (RollNoteEdit) -> Void)?

    let webView: WKWebView
    /// Where the roll is on screen, as the score screen has it (RollPlace.shown): the one place whose holder has the web view
    /// (#495 BUG-01). Not published, so setting it renders nothing again; the new place's holder takes the web view at once.
    var place: RollPlace? {
        didSet {
            if place != oldValue { RollHolder.placeChanged(self) }
        }
    }
    private let text = UIText.current
    private var pageReady = false
    private var loaded = false
    private var piece: OpenPiece?
    /// The piece whose notes the roll shows: its notes loaded again keep the roll's zoom and view (#495 BUG-04).
    private var shownPiece: Int32?
    private var dark = false
    private var shownSelection: [String: Any]?
    private var shownHighlight: [[String: Any]] = []
    private var fits = false
    /// The lanes of version 0.5.2 (#479 item 25): one position, the loop strip and the whole piece; under the modes, not in focus.
    private var lanesOn = false
    /// The note editor as last shown: on or off, its tool and the note picked.
    private var editor: [String: Any] = ["on": false]
    /// The note editor's state as the score has it; nil while it is closed.
    private var editorState: NoteEditorState?
    /// Who edits and who moves the roll, as last told (RollInput).
    private var input: [String: Any] = [:]
    /// How the notes were named when the roll was loaded: "auto", "letters" or "syllables" (the setting "notes.naming").
    private var naming = "auto"

    override init() {
        let configuration = WKWebViewConfiguration()
        configuration.setURLSchemeHandler(BundleSchemeHandler(), forURLScheme: BundleSchemeHandler.scheme)
        webView = WKWebView(frame: .zero, configuration: configuration)
        super.init()
        webView.configuration.userContentController.add(RollMessageRelay(self), name: "roll")
        webView.isOpaque = false
        webView.backgroundColor = .clear
        webView.scrollView.isScrollEnabled = false
        webView.load(URLRequest(url: BundleSchemeHandler.url("Web/roll.html")))
    }

    /// Shows a piece's notes; again when the appearance changes.
    func show(_ piece: OpenPiece, dark: Bool) {
        if self.piece?.id == piece.id && loaded {
            if self.dark != dark {
                self.dark = dark
                Task { _ = try? await webView.callAsyncJavaScript("return roll.theme(dark)", arguments: ["dark": dark], contentWorld: .page) }
            }
            return
        }
        self.piece = piece
        self.dark = dark
        if pageReady { Task { await load() } }
    }

    /// The piece's notes changed (bars of another version taken into the score or restored, 0022): the roll loads them again.
    func notesChanged() {
        guard loaded else { return }
        Task { await load() }
    }

    /// Where the player is: the roll marks the keys sounding and keeps the playhead in view. With the lanes, also the place as
    /// bar.beat for the whole-piece strip and whether the selection loops (Цикл).
    func position(_ tick: Double, playing: Bool, label: String = "", looping: Bool = false) {
        guard loaded else { return }
        let data: [String: Any] = ["tick": tick, "playing": playing, "label": label, "looping": looping]
        Task { _ = try? await webView.callAsyncJavaScript("return roll.position(data)", arguments: ["data": data], contentWorld: .page) }
    }

    /// The lanes of version 0.5.2 on or off (#479 item 25): on under the modes outside phrase focus.
    func lanes(_ on: Bool) {
        guard on != lanesOn else { return }
        lanesOn = on
        guard loaded else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return roll.lanes(on)", arguments: ["on": on], contentWorld: .page) }
    }

    /// The selection, shaded with its notes lit, and the span that loops; nil for none.
    func showSelection(_ selection: ScoreSelection?, loop: Range<Double>?) {
        var data: [String: Any] = [:]
        if let selection {
            data["selected"] = ["start": selection.onset, "end": selection.end, "ids": selection.noteIds] as [String: Any]
        }
        if let loop {
            data["loop"] = ["start": loop.lowerBound, "end": loop.upperBound]
        }
        shownSelection = data
        guard loaded else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return roll.selection(data)", arguments: ["data": data], contentWorld: .page) }
    }

    /// Whether every selection is laid across the roll, as its Fit does: the roll in phrase focus (testing item 12).
    func fitsSelection(_ on: Bool) {
        fits = on
        guard loaded else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return roll.fits(on)", arguments: ["on": on], contentWorld: .page) }
    }

    /// The note editor open, with its tool and the note picked, or closed (nil), when the roll goes back to Navigate. A press
    /// on the notes edits them in Edit notes only (#479 item 23).
    func showEditor(_ state: NoteEditorState?) {
        editorState = state
        if state == nil { mode = .navigate }
        sendEditor()
    }

    private func sendEditor() {
        let editing = shownMode == .edit
        var data: [String: Any] = ["on": editing, "tool": editorState?.tool ?? "move"]
        if editing, let picked = editorState?.picked { data["picked"] = picked }
        editor = data
        applyInput()
        guard loaded else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return roll.edit(data)", arguments: ["data": data], contentWorld: .page) }
    }

    /// Who edits and who moves the roll (RollInput, #479 item 23): again when the mode changes and when the app comes back,
    /// as the iPad's setting «Only draw with Apple Pencil» may have changed.
    func applyInput() {
        input = RollInput(stylus: InkInput.stylusPreferred, mode: shownMode).page
        guard loaded else { return }
        let data = input
        Task { _ = try? await webView.callAsyncJavaScript("return roll.input(data)", arguments: ["data": data], contentWorld: .page) }
    }

    /// The zoom controls' steps (#479 item 23): the time or the pitch, +1 in and −1 out, or Fit.
    enum ZoomAxis: String {
        case time, pitch, fit
    }

    func zoomStep(_ axis: ZoomAxis, _ direction: Int) {
        guard loaded else { return }
        let data: [String: Any] = ["axis": axis.rawValue, "direction": direction]
        Task { _ = try? await webView.callAsyncJavaScript("return roll.zoom(data)", arguments: ["data": data], contentWorld: .page) }
    }

    /// The places of the fact chosen in Insights, lit on the roll as on the score; none clears them.
    func highlight(_ places: [Fact.Place]) {
        shownHighlight = places.map { ["onset": $0.onset, "end": $0.end, "noteIds": $0.noteIds] }
        guard loaded else { return }
        let data: [String: Any] = ["places": shownHighlight]
        Task { _ = try? await webView.callAsyncJavaScript("return roll.highlight(data)", arguments: ["data": data], contentWorld: .page) }
    }

    /// The settings changed: the names and the colouring follow, and the roll is loaded again for another naming.
    func settingsChanged() {
        let settings = DeviceSettings.shared
        let names = settings.isOn("roll.noteNames")
        if noteNames != names { noteNames = names }
        guard loaded else { return }
        if settings["notes.naming"] != naming {
            Task { await load() }
        } else if settings["roll.colour"] != colourBy {
            Task {
                let chosen = try? await webView.callAsyncJavaScript("return roll.colour(by)", arguments: ["by": settings["roll.colour"]], contentWorld: .page) as? String
                if let chosen { colourBy = chosen }
            }
        }
    }

    /// The next colour scheme that tells the notes apart.
    func nextColour() {
        guard loaded else { return }
        Task {
            if let by = try? await webView.callAsyncJavaScript("return roll.nextColour()", contentWorld: .page) as? String { colourBy = by }
        }
    }

    fileprivate func pageDidLoad() {
        pageReady = true
        if piece != nil { Task { await load() } }
    }

    fileprivate func received(_ message: [String: Any]) {
        if let range = message["range"] as? [String: Any], let pick = RollPick(range, byBeats: byBeats?() ?? false) {
            picked?(pick)
        }
        if let report = message["zoom"] as? [String: Any], let read = RollZoom(report), read != zoom {
            zoom = read
        }
        if message["clear"] != nil {
            cleared?()
        }
        if message["space"] != nil {
            spacePressed?()
        }
        if let seek = message["seek"] as? [String: Any], let tick = (seek["tick"] as? NSNumber)?.doubleValue {
            playheadMoved?(tick, (seek["play"] as? Bool) ?? false)
        }
        if let edit = message["edit"] as? [String: Any], let made = RollNoteEdit(edit) {
            noteEdited?(made)
        }
    }

    private func load() async {
        guard let piece else { return }
        let id = piece.id
        let settings = DeviceSettings.shared
        naming = settings["notes.naming"]
        if !settings.values.isEmpty, noteNames != settings.isOn("roll.noteNames") { noteNames = settings.isOn("roll.noteNames") }
        let asked = naming == "letters" || naming == "syllables" ? "{\"naming\": \"\(naming)\"}" : nil
        let answer = await MusicCore.run { MusicCore.call(piece: id, "roll", asked) }
        guard let roll = facadeObject(answer), roll["error"] == nil else {
            problem = facadeObject(answer)?["message"] as? String ?? "music-core gave no piano roll."
            return
        }
        // The same piece's notes again (a note edited, a version taken in, another naming) keep the zoom and the view (BUG-04).
        let request: [String: Any] = [
            "roll": roll, "labels": Self.labels(text), "noteNames": noteNames, "dark": dark, "colourBy": settings["roll.colour"], "keep": shownPiece == id,
        ]
        do {
            let shown = try await webView.callAsyncJavaScript("return roll.load(data)", arguments: ["data": request], contentWorld: .page) as? [String: Any]
            colourBy = shown?["colourBy"] as? String ?? "hand"
            canChangeColour = ((shown?["modes"] as? [Any])?.count ?? 0) > 1
            loaded = true
            shownPiece = id
            if lanesOn {
                _ = try? await webView.callAsyncJavaScript("return roll.lanes(on)", arguments: ["on": true], contentWorld: .page)
            }
            if fits {
                _ = try? await webView.callAsyncJavaScript("return roll.fits(on)", arguments: ["on": true], contentWorld: .page)
            }
            if let shownSelection {
                _ = try? await webView.callAsyncJavaScript("return roll.selection(data)", arguments: ["data": shownSelection], contentWorld: .page)
            }
            if !shownHighlight.isEmpty {
                let data: [String: Any] = ["places": shownHighlight]
                _ = try? await webView.callAsyncJavaScript("return roll.highlight(data)", arguments: ["data": data], contentWorld: .page)
            }
            if editor["on"] as? Bool == true {
                _ = try? await webView.callAsyncJavaScript("return roll.edit(data)", arguments: ["data": editor], contentWorld: .page)
            }
            input = RollInput(stylus: InkInput.stylusPreferred, mode: shownMode).page
            _ = try? await webView.callAsyncJavaScript("return roll.input(data)", arguments: ["data": input], contentWorld: .page)
        } catch {
            problem = error.localizedDescription
        }
    }

    /// The roll's own words, from the web app's string files (`roll.js.*`), as the web app passes them.
    static func labels(_ text: UIText) -> [String: String] {
        let names = [
            "track", "trackVoice", "noVoice", "rightHand", "leftHand", "noHand", "rollAria", "scrollAria", "keysAria",
            "legendVariation", "legendHighlight", "legendInferred", "legendCheck", "zoomGroup", "zoomIn", "zoomOut", "zoomLevel", "fit", "fitHint",
            "playhead", "playheadAt",
        ]
        var labels = Dictionary(uniqueKeysWithValues: names.map { ($0, text["roll.js.\($0)"]) })
        // The iPad's roll is read by touch (#338): a screen reader hears how, not the web roll's mouse and keyboard.
        labels["rollAria"] = text["roll.ipad.rollAria"]
        return labels
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
}

/// Passes the roll's messages on without the web view keeping the controller alive.
private final class RollMessageRelay: NSObject, WKScriptMessageHandler {
    weak var controller: RollController?

    init(_ controller: RollController) {
        self.controller = controller
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        if message.body as? String == "ready" {
            MainActor.assumeIsolated { controller?.pageDidLoad() }
        } else if let body = message.body as? [String: Any] {
            MainActor.assumeIsolated { controller?.received(body) }
        }
    }
}

/// The piano roll's drawer (ds.css `.drawer`, the web app's roll drawer): up from the bottom, 40% of the height in
/// landscape and 30% in portrait, with its head (where it is, its mode switch and its settings, or the note editor's tools)
/// over the roll and the zoom on each axis over the notes (#479 item 23).
struct RollDrawer: View {
    @ObservedObject var roll: RollController
    @ObservedObject var score: ScoreController
    let text: UIText
    /// The selection's span as bar.beat, "9.1 – 12.3", under the one playback position (v0.5.2); nil for its bars.
    var span: String?
    /// Expand: the phrase in focus as a roll (testing item 12).
    let expand: () -> Void
    let close: () -> Void

    var body: some View {
        VStack(spacing: CGFloat.ds("s-2")) {
            // The head on one line where it fits, else with the roll's settings in More (portrait, the mockup's «⋯»).
            ViewThatFits(in: .horizontal) {
                head(compact: false)
                head(compact: true)
            }
            RollSurface(roll: roll, place: .drawer, text: text)
        }
        .padding(.top, CGFloat.ds("s-5"))
        .padding(.horizontal, CGFloat.ds("s-3"))
        .padding(.bottom, CGFloat.ds("s-2"))
        .background(Color.ds("surface"), in: UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
        .overlay(alignment: .top) {
            Capsule().fill(Color.ds("hairline-strong")).frame(width: 36, height: 4).padding(.top, CGFloat.ds("s-2"))
        }
        .dsShadow("shadow-sheet")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["bar.roll"])
    }

    /// The head (the approved mockup v0.5.2, #479 item 23): where it is, the roll's mode switch, and then in Navigate Clear
    /// selection, Note names, the colour scheme, Expand and Close, or in Edit notes the editor's tools, Cancel and Save.
    @ViewBuilder private func head(compact: Bool) -> some View {
        let mode = roll.shownMode
        HStack(spacing: CGFloat.ds("s-2")) {
            if !(compact && mode == .edit) {
                Text(heading).dsType(.bodyStrong).foregroundStyle(Color.ds("ink")).lineLimit(1).fixedSize()
            }
            // The selection menu gives way to the drawer, so the selection is let go here (#338).
            if !compact, mode == .navigate, score.selection != nil {
                ClearSelectionButton(score: score, text: text)
            }
            RollModeSwitch(chosen: mode, text: text) { roll.choose($0, score: score) }
            if mode == .edit, let editor = score.noteEditor {
                RollEditTools(score: score, editor: editor, text: text, compact: compact)
                Spacer(minLength: CGFloat.ds("s-2"))
                RollEditFinish(score: score, editor: editor, text: text)
            } else {
                Spacer(minLength: CGFloat.ds("s-2"))
                if compact {
                    more
                } else {
                    settings
                }
                // A draft with changes waits in Navigate for Cancel or Save.
                if let editor = score.noteEditor {
                    RollEditFinish(score: score, editor: editor, text: text)
                }
                Button(action: close) {
                    Icon(name: "close").frame(width: CGFloat.ds("h-compact"), height: CGFloat.ds("h-compact"))
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["bar.close"])
            }
        }
    }

    /// Note names, the colour scheme and Expand, on the head's line.
    @ViewBuilder private var settings: some View {
        Button {
            roll.noteNames.toggle()
        } label: {
            HStack(spacing: 6) {
                Circle().fill(roll.noteNames ? Color.ds("accent") : Color.ds("hairline-strong")).frame(width: 6, height: 6)
                Text(text["roll.noteNames"])
            }
        }
        .buttonStyle(DSItemStyle(on: roll.noteNames))
        .accessibilityAddTraits(roll.noteNames ? .isSelected : [])
        Button(text["roll.colour.\(roll.colourBy)"]) {
            roll.nextColour()
        }
        .buttonStyle(DSItemStyle())
        .disabled(!roll.canChangeColour)
        .help(text["roll.colourHint"])
        Button(action: expand) {
            HStack(spacing: 6) {
                Icon(name: "search", size: CGFloat.ds("icon-s"))
                Text(text["roll.expand"])
            }
        }
        .buttonStyle(DSItemStyle())
        .help(text["roll.expandHint"])
    }

    /// The same in More where the head is narrow, with Clear selection.
    private var more: some View {
        Menu {
            if score.selection != nil {
                Button(text["menu.clearSelection"]) { score.clearSelection() }
            }
            Toggle(text["roll.noteNames"], isOn: $roll.noteNames)
            Button(text["roll.colour.\(roll.colourBy)"]) { roll.nextColour() }
                .disabled(!roll.canChangeColour)
            Button(text["roll.expand"], action: expand)
        } label: {
            Icon(name: "more").frame(width: CGFloat.ds("h-control"), height: CGFloat.ds("h-control"))
        }
        .menuStyle(.button)
        .buttonStyle(BarActionStyle())
        .accessibilityLabel(text["bar.more"])
    }

    /// "Piano roll · bars 9–16", or "Piano roll" with nothing selected.
    private var heading: String {
        guard let selection = score.selection else { return text["roll.heading"] }
        if let span { return text.format("roll.headingBars", span) }
        let bars = selection.firstBar == selection.lastBar
            ? text.format("bars.one", String(selection.firstBar))
            : text.format("bars.range", String(selection.firstBar), String(selection.lastBar))
        return text.format("roll.headingBars", bars)
    }
}

/// The roll's one web view, in the drawer or over the card in focus. Each place gets a holder of its own that the web view
/// fills at every layout, so it takes the place's size whenever the iPad turns (#338). Only the holder of the place shown
/// has the web view (RollPlace.holder, #495 BUG-01): Expand hands it from the drawer to the card once, while the drawer is
/// still sliding away, and no SwiftUI update moves it again. Before, each update of each holder took it: while the music
/// played (an update about thirty times a second) the drawer going and the card coming passed it back and forth on every
/// update, each move resizing the roll, refitting it and sending a new zoom, which updated both holders again.
struct ScoreRollView: UIViewRepresentable {
    @ObservedObject var roll: RollController
    let place: RollPlace

    func makeUIView(context: Context) -> RollHolder {
        let holder = RollHolder(roll: roll, place: place)
        holder.claim()
        return holder
    }

    func updateUIView(_ holder: RollHolder, context: Context) {
        holder.claim()
    }

    /// All the room it is offered, never the size the web view kept from its last place.
    func sizeThatFits(_ proposal: ProposedViewSize, uiView: RollHolder, context: Context) -> CGSize? {
        guard let width = proposal.width, let height = proposal.height, width.isFinite, height.isFinite else { return nil }
        return CGSize(width: width, height: height)
    }
}

/// A place the roll's web view fills: its bounds, on every layout. Of the holders, the newest of the place shown has the web
/// view; a holder that is going, or of another place, never takes it.
final class RollHolder: UIView {
    private static let all = NSHashTable<RollHolder>.weakObjects()
    /// How many holders were made: each one's number, so the newest of a place is known.
    private static var made = 0
    private weak var roll: RollController?
    private let webView: WKWebView
    private let place: RollPlace
    private let number: Int

    init(roll: RollController, place: RollPlace) {
        self.roll = roll
        webView = roll.webView
        self.place = place
        Self.made += 1
        number = Self.made
        super.init(frame: .zero)
        Self.all.add(self)
    }

    required init?(coder: NSCoder) {
        return nil
    }

    /// Takes the web view when this is the holder of the place shown and does not have it yet; else changes nothing.
    func claim() {
        guard webView.superview !== self, let roll, Self.holder(of: roll) === self else { return }
        addSubview(webView)
        setNeedsLayout()
    }

    /// The place shown changed: its holder takes the web view, when it is made already; else it takes it as it is made.
    static func placeChanged(_ roll: RollController) {
        holder(of: roll)?.claim()
    }

    private static func holder(of roll: RollController) -> RollHolder? {
        let holders = all.allObjects.filter { $0.roll === roll }
        let newest = RollPlace.holder(for: roll.place, among: holders.map { (place: $0.place, made: $0.number) })
        return holders.first { $0.number == newest }
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        if webView.superview === self {
            webView.frame = bounds
        }
    }
}
