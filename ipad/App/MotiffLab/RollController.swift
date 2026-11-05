import SwiftUI
import WebKit

/// The piano roll in the drawer (0018 section 4): the web app's own roll (Scripts/roll/piano-roll.ts) on canvas in a web
/// view of its own (Web/roll.html), as the score is. music-core gives its data (`roll`: the notes and bars packed as the
/// roll reads them, and their names spelled as the score spells them). Swift tells it where the player is and what is
/// selected; what the player does on it comes back here: bars dragged across or a bar number tapped select those bars,
/// a tap on an empty place clears the selection, Space plays or pauses.
@MainActor
final class RollController: NSObject, ObservableObject {
    /// Whether each note carries its name (the web app's Note names chip).
    @Published var noteNames = true {
        didSet { if loaded { Task { _ = try? await webView.callAsyncJavaScript("return roll.noteNames(on)", arguments: ["on": noteNames], contentWorld: .page) } } }
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

    let webView: WKWebView
    private let text = UIText.current
    private var pageReady = false
    private var loaded = false
    private var piece: OpenPiece?
    private var dark = false
    private var shownSelection: [String: Any]?

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
    }

    private func load() async {
        guard let piece else { return }
        let id = piece.id
        let answer = await MusicCore.run { MusicCore.call(piece: id, "roll") }
        guard let roll = facadeObject(answer), roll["error"] == nil else {
            problem = facadeObject(answer)?["message"] as? String ?? "music-core gave no piano roll."
            return
        }
        let request: [String: Any] = ["roll": roll, "labels": labels, "noteNames": noteNames, "dark": dark]
        do {
            let shown = try await webView.callAsyncJavaScript("return roll.load(data)", arguments: ["data": request], contentWorld: .page) as? [String: Any]
            colourBy = shown?["colourBy"] as? String ?? "hand"
            canChangeColour = ((shown?["modes"] as? [Any])?.count ?? 0) > 1
            loaded = true
            if let shownSelection {
                _ = try? await webView.callAsyncJavaScript("return roll.selection(data)", arguments: ["data": shownSelection], contentWorld: .page)
            }
        } catch {
            problem = error.localizedDescription
        }
    }

    /// The roll's own words, from the web app's string files (`roll.js.*`), as the web app passes them.
    private var labels: [String: String] {
        let names = [
            "track", "trackVoice", "noVoice", "rightHand", "leftHand", "noHand", "rightLetter", "leftLetter", "rollAria", "scrollAria", "keysAria",
            "legendHint", "legendVariation", "legendHighlight", "legendInferred", "legendHands", "legendHandsInferred", "legendCheck",
        ]
        return Dictionary(uniqueKeysWithValues: names.map { ($0, text["roll.js.\($0)"]) })
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
    let close: () -> Void

    var body: some View {
        VStack(spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(heading).overline()
                Spacer(minLength: CGFloat.ds("s-2"))
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
                Button(action: close) {
                    Icon(name: "close").frame(width: 32, height: 32)
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["bar.close"])
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

    /// "Piano roll · bars 9–16", or "Piano roll" with nothing selected.
    private var heading: String {
        guard let selection = score.selection else { return text["roll.heading"] }
        let bars = selection.firstBar == selection.lastBar
            ? text.format("bars.one", String(selection.firstBar))
            : text.format("bars.range", String(selection.firstBar), String(selection.lastBar))
        return text.format("roll.headingBars", bars)
    }
}

struct ScoreRollView: UIViewRepresentable {
    @ObservedObject var roll: RollController

    func makeUIView(context: Context) -> WKWebView {
        roll.webView
    }

    func updateUIView(_ view: WKWebView, context: Context) {}
}
