import Combine
import WebKit
import XCTest

@testable import MotiffLab

/// A session on the score's screen without the screen (the v0.7 blueprint's section 5.3 step 3): a `ScoreWorkspace`
/// over the real music-core and the real controllers, with recording pages in place of the score's and the roll's web
/// pages, driven as `ScoreScreenContent` drives it. It writes a transcript: each step the test takes, and under it, in the
/// order they happen, what the controllers say to the two pages, what the player's model takes, and the panels.
/// `WorkspaceScenarioTests` compares the transcripts with the kept files (`fixtures/workspace-*.txt`), which protect the
/// entries of docs/ux-baseline.md that are reactions between features (PLAY-03, PLAY-04, ROLL-01, VIEW-02, VIEW-05,
/// VIEW-06).
///
/// The view is modelled as `PanelStateTests` models it: after a step's action, each render calls the reaction of every
/// value that differs from the render before, with the value as that render showed it, in the order
/// `ScoreScreenContent` declares its reactions (docs/code-map/ipad/workspace.md, "The order the view must keep"), until
/// nothing changes. A render comes only once everything the action and the reactions started is done, never while a
/// method of the workspace still waits: where SwiftUI puts its renders among them is the walk's to see. The reactions on
/// what only the view has (its geometry, its appearance, the settings, whether the selection's menu shows) are not run.
///
/// A transcript holds only what does not depend on the device or on its sound: of the score's page the playheads (when
/// they change), the bookmarks and the two questions of where the view is (`placeAt`, `firstTickOn`); of the roll its
/// load, selection, position, lanes, practice, passage, zoom and frame; of the player its selection, Цикл and what
/// loops, and its shutdown; the metronome's stop; the panels, Expand and the assistant's panel. Nothing here plays: a
/// playing position is the engine's, heard on the device.
@MainActor
final class WorkspaceSession {
    let workspace: ScoreWorkspace
    private(set) var lines: [String] = []
    /// The two web views on screen, as the pages' own tests have theirs: their pages load and say they are ready.
    private let window: UIWindow
    private var watched: [AnyCancellable] = []
    private var reactions: [Reaction] = []
    private var shown: [String: String] = [:]
    /// Whether the roll's page was sent a piece: before that it has no frame to answer.
    private var rollDrawn = false

    /// A reaction of the view: the value it observes, as the render before showed it, and what it calls.
    private final class Reaction {
        /// What to call when the value differs from the render before; nil when it does not. Reading it is the render.
        let fire: () -> (() -> Void)?

        init<Value: Equatable>(_ value: @escaping () -> Value, _ act: @escaping (_ old: Value, _ new: Value) -> Void) {
            var seen = value()
            fire = {
                let now = value()
                guard now != seen else { return nil }
                let old = seen
                seen = now
                return { act(old, now) }
            }
        }
    }

    /// The piece's screen as it opens: the workspace with recording pages, both visibilities shown (they are kept on the
    /// device), and the score's page drawn. Both pages have said they are ready before anything is asked of them, so
    /// the score is drawn as the piece is shown and the roll's first load comes as the roll is first shown.
    init(piece: OpenPiece, viewing: String? = nil) async {
        workspace = ScoreWorkspace(piece: piece, viewing: viewing)
        let frame = CGRect(x: 0, y: 0, width: 1024, height: 1366)
        window = UIWindow(frame: frame)
        for view in [workspace.score.webView, workspace.roll.webView] {
            view.frame = frame
            window.addSubview(view)
        }
        window.isHidden = false
        await Self.loaded(workspace.score.webView, "motiff")
        await Self.loaded(workspace.roll.webView, "roll")
        let page = RecordingScorePage()
        page.called = { [weak self] command, argument in self?.pageCalled(command, argument) }
        workspace.score.scorePage = page
        workspace.roll.page = RollPage { [weak self] script, arguments in self?.rollCalled(script, arguments) }
        workspace.score.practice = PracticeView()
        lines.append("## the score's page is drawn")
        workspace.score.show(piece, dark: false)
        let drawn = await workspace.score.whenRendered()
        XCTAssertTrue(drawn, "the score's page is drawn")
        await quiet()
        watch()
        observe()
    }

    /// Waits for a page of the app's to have loaded its modules (`window.motiff`, `window.roll`): it tells its
    /// controller it is ready as it does. The first web view of a run can take a while on a CI runner.
    private static func loaded(_ view: WKWebView, _ object: String) async {
        for _ in 0..<300 {
            let found = try? await view.callAsyncJavaScript("return typeof window.\(object)", arguments: [:], contentWorld: .page)
            if found as? String == "object" { return }
            try? await Task.sleep(for: .milliseconds(100))
        }
        XCTFail("the page did not load its modules: \(object)")
    }

    /// The transcript as the kept files hold it.
    var transcript: String {
        lines.joined(separator: "\n") + "\n"
    }

    /// One step of the session: its name, what it does, and the renders after it until the screen stands still. The
    /// first page is the one in view throughout, as the score's page would say.
    func step(_ name: String, _ action: () async -> Void) async {
        lines.append("")
        lines.append("## " + name)
        workspace.score.handle(.pages(first: 1, last: 1, count: 1))
        await action()
        await settle()
    }

    /// A line of the transcript the test itself writes: what a step's closure was handed, or what was kept.
    func say(_ line: String) {
        lines.append("  " + line)
    }

    /// The reaction that also runs as the screen first appears (`onChange(of: score.practicing, initial: true)`).
    func appear() {
        workspace.practicingChanged(workspace.score.practicing)
    }

    /// The renders after an action, with everything the action and its reactions started done before each.
    func settle() async {
        for _ in 0..<12 {
            await quiet()
            let fired = reactions.compactMap { $0.fire() }
            if fired.isEmpty { return }
            fired.forEach { $0() }
        }
        XCTFail("the screen did not settle")
    }

    /// Waits until nothing is on its way: no call in music-core, and nothing new said for a while. What is done off the
    /// main actor outside music-core (the project file written) is not seen here: a step waits for it with `until`.
    func quiet() async {
        var still = 0
        var said = lines.count
        for _ in 0..<2000 where still < 10 {
            for _ in 0..<10 { await Task.yield() }
            try? await Task.sleep(for: .milliseconds(20))
            let waiting = ProbeCounts.shared.snapshot()["pending"] ?? 0
            still = waiting == 0 && lines.count == said ? still + 1 : 0
            said = lines.count
        }
    }

    /// Waits, up to ten seconds, for what a step started that `quiet` does not see.
    func until(_ what: String, _ done: () -> Bool) async {
        for _ in 0..<1000 where !done() {
            try? await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertTrue(done(), what)
    }

    // MARK: - The view's reactions

    /// `ScoreScreenContent`'s reactions in the order it declares them: `watching`, `checking`, `sourcing`, `following`,
    /// then `body`.
    private func observe() {
        let workspace = workspace
        let score = workspace.score
        let player = workspace.player
        reactions = [
            Reaction({ player.hearingAlone }, { _, alone in workspace.hearingAloneChanged(alone) }),
            Reaction({ workspace.panels[.contents] }, { _, open in workspace.contentsChanged(open) }),
            Reaction({ score.focused }, { _, focused in workspace.focusChanged(focused) }),
            Reaction({ score.practicing }, { _, on in workspace.practicingChanged(on) }),
            Reaction({ score.selectionControlsShown }, { _, shown in workspace.selectionControlsChanged(shown) }),
            Reaction({ score.focused && score.focusRoll }, { _, shown in workspace.focusRollChanged(shown, dark: false) }),
            Reaction({ workspace.panels[.roll] }, { _, open in workspace.rollDrawerChanged(open, dark: false) }),
            Reaction({ score.check }, { _, _ in workspace.checkChanged() }),
            Reaction({ workspace.panels[.insights] }, { _, open in workspace.insightsChanged(open) }),
            Reaction({ workspace.panels.placeFromMoreOpen }, { _, open in workspace.placeFromMoreChanged(open) }),
            Reaction({ workspace.panels[.check] }, { _, open in workspace.checkDrawerChanged(open) }),
            Reaction({ workspace.panels[.versions] }, { _, open in workspace.sourcesChanged(open) }),
            Reaction({ workspace.panels.otherPlaceOpen(focused: score.focused) }, { _, open in workspace.otherPlaceChanged(open) }),
            Reaction({ workspace.panels[.versions] }, { _, open in workspace.sourcesOpened(open) }),
            Reaction({ score.focusVersion }, { _, version in workspace.focusVersionChanged(version) }),
            Reaction({ workspace.insights.chosen }, { _, chosen in workspace.factChosen(chosen) }),
            Reaction({ workspace.rollPlace }, { _, place in workspace.rollPlaceChanged(place) }),
            Reaction({ player.tick }, { _, tick in workspace.playerMoved(tick) }),
            Reaction({ player.state }, { old, state in workspace.playerStateChanged(from: old, to: state) }),
            Reaction({ score.selection }, { _, selection in workspace.selectionChanged(selection) }),
            Reaction({ player.loop }, { _, loop in workspace.loopChanged(loop) }),
            Reaction({ score.noteEditor }, { _, editor in workspace.noteEditorChanged(editor) }),
            Reaction({ score.playRange }, { old, new in workspace.whatPlaysChanged(from: old, to: new) }),
            Reaction({ workspace.assistant.panelOpen }, { _, open in workspace.assistantPanelChanged(open) }),
            Reaction({ workspace.panels.sheetOpen }, { _, open in workspace.sheetChanged(open) }),
        ]
    }

    // MARK: - What is written down

    /// What the controllers publish, each as it is written: the panels, Expand, the assistant's panel and the player's
    /// model when they change (what they are as the session starts is not written); every shutdown of the player,
    /// every stop of the metronome and every reading of the score's sources.
    private func watch() {
        let score = workspace.score
        let player = workspace.player
        workspace.$panels.sink { [weak self] in self?.changed("panels", PanelStateTests.words($0)) }.store(in: &watched)
        score.$focused.sink { [weak self] in self?.changed("Expand", $0 ? "open" : "closed") }.store(in: &watched)
        workspace.assistant.$panelOpen.sink { [weak self] in self?.changed("assistant's panel", $0 ? "open" : "closed") }.store(in: &watched)
        player.$selection.sink { [weak self] in self?.changed("player selection", Self.span($0)) }.store(in: &watched)
        player.$cycle.sink { [weak self] in self?.changed("player cycle", $0 ? "on" : "off") }.store(in: &watched)
        player.$loop.sink { [weak self] in self?.changed("player loops", Self.span($0)) }.store(in: &watched)
        player.$ready.dropFirst().sink { [weak self] ready in if !ready { self?.say("player shut down") } }.store(in: &watched)
        score.$sources.dropFirst().sink { [weak self] _ in self?.say("score reads its sources") }.store(in: &watched)
        Metronome.shared.$running.dropFirst().sink { [weak self] running in self?.say(running ? "metronome started" : "metronome stopped") }
            .store(in: &watched)
    }

    /// A value written when it is not what it was last. The first is only kept (what a published value is as the
    /// session starts), or written too with `first`.
    private func changed(_ name: String, _ value: String, first: Bool = false) {
        let before = shown.updateValue(value, forKey: name)
        guard before != value, before != nil || first else { return }
        say("\(name): \(value)")
    }

    private static func span(_ range: Range<Double>?) -> String {
        range.map { "\(Int($0.lowerBound))–\(Int($0.upperBound))" } ?? "none"
    }

    private static func number(_ value: Any?) -> Double? {
        (value as? NSNumber)?.doubleValue ?? (value as? Double) ?? (value as? Int).map(Double.init)
    }

    private static func span(_ fields: Any?) -> String {
        guard let fields = fields as? [String: Any], let start = number(fields["start"]), let end = number(fields["end"]) else { return "none" }
        return span(start..<end)
    }

    /// A call into the score's page: written when it is one of those a transcript holds.
    private func pageCalled(_ command: PageCommand, _ argument: Any?) {
        let fields = argument as? [String: Any] ?? [:]
        switch command {
        case .playheads:
            let hand = ((fields["selection"] as? [String: Any])?["hand"] as? String).map { " hand=\($0)" } ?? ""
            // Written when it changes: whether the controller sends a model again that it has sent already varies from
            // run to run (it compares the texts of two dictionaries, whose order is not fixed).
            let cycle = fields["loop"] as? Bool == true ? "on" : "off"
            changed("page playheads", "selection \(Self.span(fields["selection"]))\(hand), cycle \(cycle)", first: true)
        case .bookmarks:
            say("page bookmarks: \((fields["items"] as? [Any])?.count ?? 0)")
        case .placeAt:
            say("page placeAt")
        case .firstTickOn:
            say("page firstTickOn: page \(Int(Self.number(fields["page"]) ?? 0))")
        default:
            break
        }
    }

    /// A call into the roll's page (`return roll.<function>(argument)`): written when it is one of those a transcript
    /// holds, and answered as the page would: what a load shows, and the roll's frame.
    private func rollCalled(_ script: String, _ arguments: [String: Any]) -> Any? {
        let name = script.components(separatedBy: "roll.").last?.components(separatedBy: "(").first ?? ""
        let argument = arguments["argument"]
        let fields = argument as? [String: Any] ?? [:]
        switch RollCommand(rawValue: name) {
        case .load?:
            let kept = fields["keep"] as? Bool == true ? "the zoom and the view kept" : "a first load"
            say("roll load: \(kept), frame \(fields["frame"] is NSNull ? "none" : "given")")
            rollDrawn = true
            return ["colourBy": "hand", "modes": ["hand"]] as [String: Any]
        case .selection?:
            say("roll selection: \(Self.span(fields["selected"])), loops \(Self.span(fields["loop"]))")
        case .position?:
            let standing = fields["playing"] as? Bool == true ? "playing" : "standing"
            let looping = fields["looping"] as? Bool == true ? "yes" : "no"
            say("roll position: \(Int(Self.number(fields["tick"]) ?? -1)) \(standing), looping \(looping)")
        case .lanes?:
            say("roll lanes: \(argument as? Bool == true ? "on" : "off")")
        case .practice?:
            say("roll practice: \(argument as? Bool == true ? "on" : "off")")
        case .passage?:
            say("roll passage: \(Self.span(argument))")
        case .zoom?:
            say("roll zoom: \(fields["axis"] as? String ?? "") \(Int(Self.number(fields["direction"]) ?? 0))")
        case .frame?:
            say("roll frame: \(rollDrawn ? "given" : "none, the roll never drew")")
            guard rollDrawn else { return nil }
            return ["zoom": 2.0, "rowScale": 1.0, "scroll": 480.0, "pitchTop": 84.0].mapValues { NSNumber(value: $0) }
        default:
            break
        }
        return nil
    }
}

/// The score's page as a session has it: it draws nothing, answers nothing, and tells what it was asked.
@MainActor
final class RecordingScorePage: ScorePage {
    var called: ((PageCommand, Any?) -> Void)?

    func run(_ command: PageCommand, _ argument: Any?) async throws -> Any? {
        called?(command, argument)
        return nil
    }

    func post(_ command: PageCommand, _ argument: Any) {
        called?(command, argument)
    }
}
