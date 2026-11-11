import SwiftUI
import WebKit

/// The piano roll in the drawer (0018 section 4): the web app's own roll (Scripts/roll/piano-roll.ts) on canvas in a web
/// view of its own (Web/roll.html), as the score is. music-core gives its data (`roll`: the notes and bars packed as the
/// roll reads them, and their names spelled as the score spells them). Swift tells it where the player is and what is
/// selected, and lights the places of the fact chosen in Insights; what the player does on it comes back here: bars dragged across or a bar number tapped select those bars,
/// Escape clears the selection (a tap never does, #338), Space plays or pauses; the playhead dragged or nudged by a beat moves
/// the player there, and a tap inside the loop or the selection plays from there (v0.2); with the note editor on (testing item
/// 15), a note moved, added or taken out goes to music-core as a correction on the draft.
@MainActor
final class RollController: NSObject, ObservableObject {
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

    /// Bars chosen on the roll: the first, the last, and whether a tap extends the selection to them.
    var barsPicked: (@MainActor (_ first: Int, _ last: Int, _ extend: Bool) -> Void)?
    var cleared: (@MainActor () -> Void)?
    var spacePressed: (@MainActor () -> Void)?
    /// The playhead moved on the roll: to a tick, and with `play` a tap that plays from there.
    var playheadMoved: (@MainActor (_ tick: Double, _ play: Bool) -> Void)?
    /// A note moved, added or taken out with the note editor on.
    var noteEdited: (@MainActor (RollNoteEdit) -> Void)?

    let webView: WKWebView
    private let text = UIText.current
    private var pageReady = false
    private var loaded = false
    private var piece: OpenPiece?
    private var dark = false
    private var shownSelection: [String: Any]?
    private var shownHighlight: [[String: Any]] = []
    private var fits = false
    /// The note editor as last shown: on or off, its tool and the note picked.
    private var editor: [String: Any] = ["on": false]
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

    /// Where the player is: the roll marks the keys sounding and keeps the playhead in view.
    func position(_ tick: Double, playing: Bool) {
        guard loaded else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return roll.position(data)", arguments: ["data": ["tick": tick, "playing": playing]], contentWorld: .page) }
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

    /// The note editor on, with its tool and the note picked, or off (nil): a press on the notes then edits them.
    func showEditor(_ state: NoteEditorState?) {
        var data: [String: Any] = ["on": state != nil, "tool": state?.tool ?? "move"]
        if let picked = state?.picked { data["picked"] = picked }
        editor = data
        guard loaded else { return }
        Task { _ = try? await webView.callAsyncJavaScript("return roll.edit(data)", arguments: ["data": data], contentWorld: .page) }
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
        func number(_ value: Any?) -> Int? { (value as? NSNumber)?.intValue }
        if let range = message["range"] as? [String: Any], let first = number(range["first"]), let last = number(range["last"]) {
            barsPicked?(first, last, (range["extend"] as? Bool) ?? false)
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
        let request: [String: Any] = ["roll": roll, "labels": Self.labels(text), "noteNames": noteNames, "dark": dark, "colourBy": settings["roll.colour"]]
        do {
            let shown = try await webView.callAsyncJavaScript("return roll.load(data)", arguments: ["data": request], contentWorld: .page) as? [String: Any]
            colourBy = shown?["colourBy"] as? String ?? "hand"
            canChangeColour = ((shown?["modes"] as? [Any])?.count ?? 0) > 1
            loaded = true
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
/// landscape and 30% in portrait, with its head (where it is, Note names, the colour scheme, Close) over the roll.
struct RollDrawer: View {
    @ObservedObject var roll: RollController
    @ObservedObject var score: ScoreController
    let text: UIText
    /// Expand: the phrase in focus as a roll (testing item 12).
    let expand: () -> Void
    let close: () -> Void

    var body: some View {
        VStack(spacing: CGFloat.ds("s-2")) {
            if let editor = score.noteEditor {
                editing(editor)
            } else {
                head
            }
            ScoreRollView(roll: roll)
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

    /// Where it is with Clear selection, Edit notes, Note names, the colour scheme, Expand and Close.
    private var head: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Text(heading).overline()
            // The selection menu gives way to the drawer, so the selection is let go here (#338).
            if score.selection != nil {
                ClearSelectionButton(score: score, text: text)
            }
            Spacer(minLength: CGFloat.ds("s-2"))
            Button {
                score.beginNoteEdit()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "pen", size: CGFloat.ds("icon-s"))
                    Text(text["edit.begin"])
                }
            }
            .buttonStyle(DSItemStyle())
            .help(text["edit.beginHint"])
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
            Button(action: close) {
                Icon(name: "close").frame(width: 32, height: 32)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["bar.close"])
        }
    }

    /// The note editor's head (testing item 15): the tools, Undo, Reset, Cancel and Save, and what the tool does.
    private func editing(_ editor: NoteEditorState) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                // The overline in the accent: the roll is being edited.
                Text(text["edit.title"]).dsType(.overline).textCase(.uppercase).tracking(0.66).foregroundStyle(Color.ds("accent"))
                SegmentedChoice(options: ["move", "add", "delete"].map { ($0, text["edit.\($0)"]) }, chosen: editor.tool, label: text["edit.tool"]) {
                    score.chooseNoteEditTool($0)
                }
                Button {
                    score.undoNoteEdit()
                } label: {
                    Icon(name: "undo", size: CGFloat.ds("icon-s")).frame(width: 32, height: 32)
                }
                .buttonStyle(DSItemStyle())
                .disabled(!editor.canUndo)
                .accessibilityLabel(text["edit.undo"])
                Button(text["edit.reset"]) {
                    score.resetNotes()
                }
                .buttonStyle(DSItemStyle())
                .disabled(!editor.canReset)
                .help(text["edit.resetHint"])
                Spacer(minLength: CGFloat.ds("s-2"))
                Button(text["edit.cancel"]) {
                    score.cancelNoteEdit()
                }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                Button {
                    score.saveNoteEdit()
                } label: {
                    HStack(spacing: 6) {
                        Icon(name: "check", size: CGFloat.ds("icon-s"))
                        Text(text["edit.save"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                .disabled(!editor.changed)
            }
            Text("\(text["edit.\(editor.tool)Hint"]) \(text["edit.keptOnSave"])")
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .lineLimit(2)
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["edit.toolbar"])
    }

    /// "Piano roll · bars 9–16", or "Piano roll" with nothing selected.
    private var heading: String {
        guard let selection = score.selection else { return text["roll.heading"] }
        let bars = selection.firstBar == selection.lastBar
            ? text.format("bars.one", String(selection.firstBar))
            : text.format("bars.range", String(selection.firstBar), String(selection.lastBar))
        return text.format("roll.headingBars", bars)
    }
}

/// The roll's one web view, in the drawer or over the card in focus. Each place gets a holder of its own that the web view
/// fills at every layout, so it takes the place's size whenever the iPad turns (#338). Expand hands the web view from the
/// drawer to the card while the drawer is still sliding away; the holder shown last takes it back, rather than the web view
/// keeping the size of a holder that is going.
struct ScoreRollView: UIViewRepresentable {
    @ObservedObject var roll: RollController

    func makeUIView(context: Context) -> RollHolder {
        let holder = RollHolder(webView: roll.webView)
        holder.hold()
        return holder
    }

    func updateUIView(_ holder: RollHolder, context: Context) {
        holder.hold()
    }

    /// All the room it is offered, never the size the web view kept from its last place.
    func sizeThatFits(_ proposal: ProposedViewSize, uiView: RollHolder, context: Context) -> CGSize? {
        guard let width = proposal.width, let height = proposal.height, width.isFinite, height.isFinite else { return nil }
        return CGSize(width: width, height: height)
    }
}

/// A place the roll's web view fills: its bounds, on every layout. A holder leaving the screen with the web view hands it
/// to a holder still on screen.
final class RollHolder: UIView {
    private static let all = NSHashTable<RollHolder>.weakObjects()
    private let webView: WKWebView

    init(webView: WKWebView) {
        self.webView = webView
        super.init(frame: .zero)
        Self.all.add(self)
    }

    required init?(coder: NSCoder) {
        return nil
    }

    func hold() {
        if webView.superview !== self {
            addSubview(webView)
        }
        setNeedsLayout()
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        if webView.superview === self {
            webView.frame = bounds
        }
    }

    override func didMoveToWindow() {
        super.didMoveToWindow()
        guard window == nil, webView.superview === self else { return }
        Self.all.allObjects.first { $0 !== self && $0.window != nil && $0.webView === webView }?.hold()
    }
}
