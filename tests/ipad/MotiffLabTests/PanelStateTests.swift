import XCTest

@testable import MotiffLab

/// The score screen's panels (PanelState, the v0.7 blueprint's section 5.3 step 1): what opening and closing one does to the
/// others, what is remembered and given back, and Expand, exactly as the screen's reactions did it before PanelState.
/// Golden/panel-states.txt is the table of it: start states, every action the screen offers in each, and the panels render
/// by render until they settle; its states hold every kind of change the screen's reachable states have. The test takes
/// PanelState through each line as the screen does: the action writes what it asks (`asked`), then each render runs the
/// rule (`following`) of every value that changed since the render before, in the order ScoreScreen declares its
/// reactions (docs/code-map/ipad/workspace.md). The file is read where it lies in the repository (`#filePath`).
final class PanelStateTests: XCTestCase {
    private static let names: [(panel: PanelState.Panel, name: String)] = [
        (.roll, "roll"), (.insights, "insights"), (.contents, "contents"), (.check, "check"), (.versions, "versions"), (.settings, "settings"),
        (.reference, "reference"), (.more, "more"),
    ]

    /// An action of the table the test does not know.
    private struct Unread: Error {
        let action: String
    }

    /// The screen as far as its panels go: PanelState, Expand as ScoreController holds it, and the assistant's panel.
    private struct Screen {
        var panels: PanelState
        var focused = false
        var focusRoll = false
        var assistant = false

        /// A value a reaction of ScoreScreen observes, and what the reaction does to the panels with it.
        struct Reaction {
            let value: (Screen) -> Bool
            let act: (inout Screen, Bool) -> Void
        }

        /// ScoreScreen's reactions on the panels, in the order it declares them: `watching`, `checking`, then `body`.
        static let reactions = [
            Reaction(value: { $0.panels[.contents] }, act: { $0.follow(.contents($1)) }),
            Reaction(value: { $0.focused }, act: { $0.follow(.expand($1)) }),
            Reaction(value: { $0.focused && $0.focusRoll }, act: { $0.follow(.rollOnCard($1)) }),
            Reaction(value: { $0.panels[.roll] }, act: { $0.follow(.roll($1)) }),
            Reaction(value: { $0.panels[.insights] }, act: { $0.follow(.insights($1)) }),
            Reaction(value: { $0.panels.placeFromMoreOpen }, act: { $0.follow(.placeFromMore($1)) }),
            Reaction(value: { $0.panels[.check] }, act: { $0.follow(.check($1)) }),
            Reaction(value: { $0.panels[.versions] }, act: { $0.follow(.versions($1, focused: $0.focused)) }),
            Reaction(value: { $0.panels.otherPlaceOpen(focused: $0.focused) }, act: { $0.follow(.otherPlace($1)) }),
            // One thing at a time on the right: the assistant's panel opened, then a sheet opened.
            Reaction(value: { $0.assistant }, act: { $0.assistantChanged($1) }),
            Reaction(value: { $0.panels.sheetOpen }, act: { $0.sheetChanged($1) }),
        ]

        mutating func follow(_ rule: PanelState.Rule) {
            panels = panels.following(rule)
        }

        mutating func assistantChanged(_ open: Bool) {
            if open { panels = panels.asked(.assistantOpened) }
        }

        mutating func sheetChanged(_ open: Bool) {
            if open { assistant = false }
        }

        /// The state as the table writes it.
        var description: String {
            var text = PanelStateTests.words(panels)
            if focused { text += focusRoll ? " [Expand roll]" : " [Expand]" }
            if assistant { text += " (assistant open)" }
            return text
        }

        /// What an action writes, as the code writes it; the table offers each only where the screen does.
        mutating func act(_ action: String) throws {
            switch action {
            case "expandFromDrawer":
                // ScoreScreen.expandFromDrawer: the panels, then ScoreController.expandRoll().
                panels = panels.asked(.expandFromDrawer)
                focusRoll = true
                focused = true
            case "enterFocus notes":
                focused = true
            case "enterFocus (focusRoll kept on)":
                focusRoll = true
                focused = true
            case "bookmark to Expand":
                // The Contents' entry pressed again: goToBookmark enters Expand, then the drawer closes itself.
                focused = true
                panels = panels.asked(.close(.contents))
            case "leaveFocus":
                focused = false
            case "focusRoll on":
                focusRoll = true
            case "focusRoll off":
                focusRoll = false
            case "openDraftFromFocus":
                // ScoreController.openDraftFromFocus: leaveFocus(), then openDraft(at:), which asks for Sources.
                focused = false
                panels = panels.asked(.open(.versions))
            case "assistant open":
                assistant = true
            case "assistant close":
                assistant = false
            default:
                panels = try panels.asked(PanelStateTests.intent(action))
            }
        }

        /// One action and the renders after it, as SwiftUI runs them: a reaction fires when its value differs from the
        /// render before, with the value as the render showed it, on the panels as the reactions before it left them.
        /// Returns the state as the action wrote it, then after each render that changed it.
        mutating func run(_ action: String) throws -> [String] {
            var seen = Screen.reactions.map { $0.value(self) }
            try act(action)
            var shown = [description]
            // The longest change on develop takes four renders; eight is the bound of a loop that must end.
            for _ in 0..<8 {
                let now = Screen.reactions.map { $0.value(self) }
                if now == seen { return shown }
                for (index, reaction) in Screen.reactions.enumerated() where now[index] != seen[index] {
                    reaction.act(&self, now[index])
                }
                seen = now
                if description != shown.last { shown.append(description) }
            }
            XCTFail("the panels did not settle after \(action)")
            return shown
        }
    }

    /// The panels as the table writes them: those open, joined by + (nothing for none), then {what is remembered}. The
    /// workspace scenarios (tests/ipad/Baseline/WorkspaceSession.swift) write them so too.
    static func words(_ panels: PanelState) -> String {
        var text = names.filter { panels[$0.panel] }.map { $0.name }.joined(separator: "+")
        if text.isEmpty { text = "nothing" }
        var kept: [String] = []
        if !panels.fromMore.isEmpty {
            // "fromMore" while every place open shows "‹ More"; else the places opened from More (answer 10).
            let places: [PanelState.Panel] = [.check, .versions, .settings]
            let all = places.filter { panels[$0] }.allSatisfy { panels.fromMore.contains($0) }
            let from = names.filter { panels.fromMore.contains($0.panel) }.map { $0.name }.joined(separator: "+")
            kept.append(all ? "fromMore" : "fromMore=" + from)
        }
        if panels.rollBeforeFocus { kept.append("rollBeforeFocus") }
        if panels.contentsBeforeFocus { kept.append("contentsBeforeFocus") }
        if let under = panels.beforeSources {
            let places = [(under.roll, "roll"), (under.insights, "insights"), (under.contents, "contents")].filter { $0.0 }.map { $0.1 }
            kept.append("beforeSources=" + (places.isEmpty ? "nothing" : places.joined(separator: "+")))
        }
        if !kept.isEmpty { text += " {" + kept.joined(separator: ", ") + "}" }
        return text
    }

    /// "open roll", "close roll", "fromMore check", "backToMore check", and Piece DNA's "backToMore dna".
    private static func intent(_ action: String) throws -> PanelState.Intent {
        let words = action.components(separatedBy: " ")
        guard words.count == 2 else { throw Unread(action: action) }
        let panel = names.first { $0.name == words[1] }?.panel
        if words[0] == "backToMore", panel != nil || words[1] == "dna" { return .backToMore(panel) }
        guard let panel else { throw Unread(action: action) }
        switch words[0] {
        case "open": return .open(panel)
        case "close": return .close(panel)
        case "fromMore": return .openFromMore(panel)
        default: throw Unread(action: action)
        }
    }

    private static func table() throws -> [String] {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("Golden/panel-states.txt")
        return try String(contentsOf: url, encoding: .utf8).components(separatedBy: "\n")
    }

    /// Every line of the table: from the section's start state, the action, and the panels render by render.
    func testThePanelsDoWhatTheTableSays() throws {
        var screen = Screen(panels: PanelState())
        var section = ""
        var lines = 0
        for (index, line) in try Self.table().enumerated() {
            let text = line.trimmingCharacters(in: .whitespaces)
            let place = "line \(index + 1) of panel-states.txt (\(section))"
            if text.hasPrefix("### ") {
                // A section: its name, then the actions that reach its start state from a fresh screen.
                section = String(text.dropFirst(4))
                let parts = section.components(separatedBy: ": ")
                guard parts.count == 2 else {
                    XCTFail(place)
                    continue
                }
                screen = Screen(panels: PanelState(sourcesOn: !parts[0].hasSuffix("(no Sources)")))
                for action in parts[1].components(separatedBy: ", ") where action != "a fresh screen" {
                    _ = try screen.run(action)
                }
            } else if text.hasPrefix("= ") {
                XCTAssertEqual(screen.description, String(text.dropFirst(2)), place)
            } else if !text.isEmpty, !text.hasPrefix("#") {
                let parts = text.components(separatedBy: "→").map { $0.trimmingCharacters(in: .whitespaces) }
                var tried = screen
                let shown = try tried.run(parts[0])
                XCTAssertEqual(shown, Array(parts.dropFirst()), "\(place): \(parts[0])")
                lines += 1
            }
        }
        XCTAssertEqual(lines, 914, "every line of the table is run")
    }

    func testAFreshScreenHasNothingOpen() {
        let fresh = PanelState()
        XCTAssertTrue(PanelState.Panel.allCases.allSatisfy { !fresh[$0] })
        XCTAssertFalse(fresh.sheetOpen)
        XCTAssertFalse(fresh.placeFromMoreOpen)
        XCTAssertFalse(fresh.otherPlaceOpen(focused: false))
        XCTAssertTrue(fresh.otherPlaceOpen(focused: true), "Expand counts as another place")
    }

    /// A toggle opens a closed panel and closes an open one, as the bar's buttons do; asking for what is so changes nothing.
    func testAToggleOpensOrCloses() {
        let open = PanelState().asked(.toggle(.roll))
        XCTAssertTrue(open[.roll])
        XCTAssertEqual(open.asked(.toggle(.roll)), PanelState())
        XCTAssertEqual(open.asked(.open(.roll)), open)
    }

    /// An action writes only what it asks: the others give way in the renders after it, by their rules.
    func testAnActionWritesOnlyWhatItAsks() {
        let both = PanelState().asked(.open(.roll)).asked(.open(.insights))
        XCTAssertTrue(both[.roll] && both[.insights])
        XCTAssertFalse(both.following(.insights(true))[.roll])
        XCTAssertEqual(both.following(.insights(false)), both, "a rule follows the value the render showed")
    }

    /// The roll's unsaved note edits go when the roll is put away, not when it changes places between its drawer and
    /// Expand's card (ScoreWorkspace.rollDrawerChanged, focusRollChanged).
    func testTheRollsEditsGoUnlessTheRollChangesPlaces() {
        let drawer = PanelState().asked(.open(.roll)).following(.roll(true))
        XCTAssertTrue(drawer.asked(.close(.roll)).rollEditsGoWithDrawer, "the drawer's ✕")
        XCTAssertTrue(drawer.asked(.open(.insights)).following(.insights(true)).rollEditsGoWithDrawer, "another place opened")
        XCTAssertFalse(drawer.asked(.expandFromDrawer).rollEditsGoWithDrawer, "Expand on the drawer takes the editor to the card")
        XCTAssertFalse(drawer.following(.expand(true)).following(.rollOnCard(true)).rollEditsGoWithDrawer, "Expand with the card as a roll")

        let card = drawer.asked(.expandFromDrawer)
        XCTAssertFalse(card.rollEditsGoWithCard, "Notes chosen on the card: the drawer comes back with Collapse")
        XCTAssertFalse(card.following(.expand(false)).rollEditsGoWithCard, "Collapse: the roll is back in its drawer")
        let cardOnly = PanelState().following(.expand(true)).following(.rollOnCard(true))
        XCTAssertTrue(cardOnly.rollEditsGoWithCard, "a roll shown only on the card is put away")
        XCTAssertTrue(cardOnly.following(.expand(false)).rollEditsGoWithCard, "and so it is with Back to page")
    }

    /// Insights, Check or Sources: the sheet on the right.
    func testTheSheetOnTheRight() {
        for (panel, name) in Self.names {
            XCTAssertEqual(PanelState().asked(.open(panel)).sheetOpen, ["insights", "check", "versions"].contains(name), name)
        }
    }
}
