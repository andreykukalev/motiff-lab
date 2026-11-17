import XCTest

@testable import MotiffLab

/// The score screen's panels (PanelState, the v0.7 blueprint's section 5.3 step 1): what opening and closing one does to the
/// others, what is remembered and given back, and Expand, exactly as the screen's reactions did it before (pull request B2,
/// "The present table"). The tables are written as that table is: R the roll's drawer, I Insights, C the Contents, K Check,
/// S Sources, G Settings, F the Reference, M More, "-" nothing; then m: opened from More ("‹ More"); ^…: what Sources keeps
/// under it (^0 nothing); <R, <C: the roll's drawer or the Contents kept for after Expand.
final class PanelStateTests: XCTestCase {
    private static let letters: [(PanelState.Panel, String)] = [
        (.roll, "R"), (.insights, "I"), (.contents, "C"), (.check, "K"), (.versions, "S"), (.settings, "G"), (.reference, "F"), (.more, "M"),
    ]
    private let none = PanelState.Expand()
    private let notes = PanelState.Expand(focused: true, roll: false)
    private let asRoll = PanelState.Expand(focused: true, roll: true)

    /// The panels as the tables write them.
    private func code(_ state: PanelState) -> String {
        var code = Self.letters.filter { state[$0.0] }.map { $0.1 }.joined()
        if code.isEmpty { code = "-" }
        if state.fromMore { code += "m" }
        if let kept = state.beforeSources {
            let under = [(kept.roll, "R"), (kept.insights, "I"), (kept.contents, "C")].filter { $0.0 }.map { $0.1 }.joined()
            code += "^" + (under.isEmpty ? "0" : under)
        }
        if state.rollBeforeFocus { code += "<R" }
        if state.contentsBeforeFocus { code += "<C" }
        return code
    }

    private func panel(_ letter: String) -> PanelState.Panel {
        Self.letters.first { $0.1 == letter }!.0
    }

    /// Each step's panels, from a fresh screen; outside Expand unless a step says.
    private func codes(_ steps: [(PanelState.Intent, PanelState.Expand)], sourcesOn: Bool = true) -> [String] {
        var state = PanelState(sourcesOn: sourcesOn)
        return steps.map { intent, expand in
            state = state.after(intent, expand: expand)
            return code(state)
        }
    }

    private func codes(_ intents: PanelState.Intent...) -> [String] {
        codes(intents.map { ($0, none) })
    }

    func testAFreshScreenHasNothingOpen() {
        XCTAssertEqual(code(PanelState()), "-")
        XCTAssertFalse(PanelState().sheetOpen)
    }

    /// Every pair «X open, open Y», and X closed: the present table's first part, row by row.
    func testOpeningEachPanelOverEachPanel() {
        let table: [(String, [String], String)] = [
            // X open    open R  I    C    K    S       G       F       M       close X
            ("-", ["R", "I", "C", "K", "S^0", "G", "F", "M"], "-"),
            ("R", ["R", "I", "C", "K", "S^R", "RG", "RF", "RM"], "-"),
            ("I", ["R", "I", "C", "K", "S^I", "IG", "IF", "IM"], "-"),
            ("C", ["R", "I", "C", "K", "S^C", "CG", "CF", "CM"], "-"),
            ("K", ["R", "I", "C", "K", "S^0", "KG", "KF", "KM"], "-"),
            ("S", ["R", "I", "C", "K", "S^0", "SG^0", "SF^0", "SM^0"], "-"),
            ("G", ["RG", "IG", "CG", "KG", "SG^0", "G", "GF", "GM"], "-"),
            ("F", ["RF", "IF", "CF", "KF", "SF^0", "GF", "F", "FM"], "-"),
            ("M", ["RM", "IM", "CM", "KM", "SM^0", "GM", "FM", "M"], "-"),
        ]
        for (open, row, closed) in table {
            let start = open == "-" ? PanelState() : PanelState().after(.open(panel(open)), expand: none)
            for ((panel, letter), expected) in zip(Self.letters, row) {
                XCTAssertEqual(code(start.after(.open(panel), expand: none)), expected, "\(open) open, open \(letter)")
            }
            if open != "-" {
                XCTAssertEqual(code(start.after(.close(panel(open)), expand: none)), closed, "\(open) open, close it")
            }
        }
    }

    /// A toggle opens a closed panel and closes an open one, as the bar's buttons do; asked twice it changes nothing.
    func testAToggleOpensOrCloses() {
        XCTAssertEqual(codes(.toggle(.roll), .toggle(.roll)), ["R", "-"])
        XCTAssertEqual(codes(.open(.insights), .toggle(.roll)), ["I", "R"])
        let open = PanelState().after(.open(.check), expand: none)
        XCTAssertEqual(open.after(.open(.check), expand: none), open, "opening what is open changes nothing")
        XCTAssertEqual(open.after(.follow, expand: none), open)
    }

    /// Check, Sources and Settings from More: More goes, the place shows "‹ More" while one of the three is open.
    func testMoreOpensItsPlacesWithTheWayBack() {
        XCTAssertEqual(codes(.open(.more), .openFromMore(.check)), ["M", "Km"])
        XCTAssertEqual(codes(.open(.roll), .openFromMore(.versions)), ["R", "Sm^R"])
        XCTAssertEqual(codes(.open(.roll), .openFromMore(.settings)), ["R", "RGm"], "Settings, a sheet over the score, closes nothing")
        XCTAssertEqual(codes(.openFromMore(.check), .close(.check)), ["Km", "-"])
        XCTAssertEqual(codes(.openFromMore(.check), .open(.roll)), ["Km", "R"], "another place: Check goes, and its way back")
        // Question for Andrei (B2): the way back is one for the three, so a place opened otherwise while another opened from
        // More is open shows "‹ More" too: a version's marker over Check, a Check flag over Sources or Settings.
        XCTAssertEqual(codes(.openFromMore(.check), .open(.versions), .close(.versions)), ["Km", "Sm^0", "-"])
        XCTAssertEqual(codes(.openFromMore(.versions), .open(.check)), ["Sm^0", "Km"])
        XCTAssertEqual(codes(.openFromMore(.settings), .open(.check), .close(.settings), .close(.check)), ["Gm", "KGm", "Km", "-"])
    }

    /// "‹ More" closes the place and forgets the way back (the screen opens More again after it); Piece DNA's only forgets.
    func testBackToMore() {
        XCTAssertEqual(codes(.openFromMore(.check), .backToMore(.check)), ["Km", "-"])
        XCTAssertEqual(codes(.open(.insights), .openFromMore(.versions), .backToMore(.versions)), ["I", "Sm^I", "I"])
        XCTAssertEqual(codes(.openFromMore(.check), .openFromMore(.settings), .backToMore(.settings)), ["Km", "KGm", "K"])
        XCTAssertEqual(codes(.openFromMore(.check), .backToMore(nil)), ["Km", "K"], "Piece DNA's way back: Check stays, without ‹ More")
        XCTAssertEqual(codes(.open(.more), .backToMore(nil), .open(.more)), ["M", "M", "M"])
    }

    /// Sources keeps what was open under it and gives it back when it closes by itself (✕, the bar's button, ‹ More), not
    /// when another place opens (#582 item 4); on a view shown over the piece nothing is kept.
    func testSourcesGivesBackWhatWasUnderIt() {
        XCTAssertEqual(codes(.open(.roll), .open(.versions), .close(.versions)), ["R", "S^R", "R"])
        XCTAssertEqual(codes(.open(.insights), .open(.versions), .close(.versions)), ["I", "S^I", "I"])
        XCTAssertEqual(codes(.open(.contents), .open(.versions), .toggle(.versions)), ["C", "S^C", "C"])
        XCTAssertEqual(codes(.open(.contents), .open(.versions), .open(.roll)), ["C", "S^C", "R"])
        XCTAssertEqual(codes(.open(.contents), .open(.versions), .open(.check)), ["C", "S^C", "K"])
        XCTAssertEqual(codes(.open(.contents), .open(.versions), .open(.settings), .close(.versions)), ["C", "S^C", "SG^C", "CG"])
        XCTAssertEqual(codes(.open(.check), .open(.versions), .close(.versions)), ["K", "S^0", "-"], "Check is not given back")
        XCTAssertEqual(codes([(.open(.roll), none), (.open(.versions), none), (.close(.versions), none)], sourcesOn: false), ["R", "S", "-"])
    }

    /// The assistant's panel opened: Insights, Check and Sources go; what Sources kept comes back, and the screen then puts the
    /// assistant away again (`sheetOpen`).
    func testTheAssistantTakesTheRight() {
        XCTAssertEqual(codes(.open(.insights), .assistantOpened), ["I", "-"])
        XCTAssertEqual(codes(.open(.check), .assistantOpened), ["K", "-"])
        XCTAssertEqual(codes(.open(.roll), .assistantOpened), ["R", "R"], "the roll and the Contents stay")
        XCTAssertEqual(codes(.open(.roll), .open(.versions), .assistantOpened), ["R", "S^R", "R"])
        // Question for Andrei (B2): More's «Спросить о пьесе» with Sources over Insights gives Insights back, and the assistant
        // goes again at once.
        let back = PanelState().after(.open(.insights), expand: none).after(.open(.versions), expand: none).after(.assistantOpened, expand: none)
        XCTAssertEqual(code(back), "I")
        XCTAssertTrue(back.sheetOpen)
        XCTAssertFalse(PanelState().after(.open(.roll), expand: none).after(.assistantOpened, expand: none).sheetOpen)
    }

    /// Expand covers the page (#521 17, 19): the Contents goes and comes back with Back to page; the drawer opened into
    /// Expand comes back with Collapse; Check and Sources go and do not come back; Insights stays.
    func testExpandPutsAwayAndGivesBack() {
        XCTAssertEqual(codes([(.open(.roll), none), (.expandFromDrawer, asRoll), (.follow, none)]), ["R", "-<R", "R"])
        XCTAssertEqual(codes([(.open(.contents), none), (.follow, notes), (.follow, none)]), ["C", "-<C", "C"])
        XCTAssertEqual(codes([(.open(.insights), none), (.follow, notes), (.follow, none)]), ["I", "I", "I"])
        XCTAssertEqual(codes([(.open(.check), none), (.follow, notes), (.follow, none)]), ["K", "-", "-"])
        XCTAssertEqual(codes([(.open(.roll), none), (.open(.versions), none), (.follow, notes), (.follow, none)]), ["R", "S^R", "-", "-"])
        XCTAssertEqual(codes([(.open(.settings), none), (.follow, notes), (.follow, none)]), ["G", "G", "G"])
        // The drawer open over Expand as notes (a bookmark gone to): the card as a roll takes it, Notes keeps it waiting.
        XCTAssertEqual(codes([(.open(.roll), none), (.follow, notes), (.follow, asRoll), (.follow, notes), (.follow, none)]), ["R", "R", "-<R", "-<R", "R"])
        XCTAssertEqual(codes([(.open(.roll), none), (.follow, notes), (.expandFromDrawer, asRoll), (.follow, none)]), ["R", "R", "-<R", "R"])
        // Check opened in Expand goes when the drawer comes back; over Expand as notes it stays.
        XCTAssertEqual(codes([(.open(.roll), none), (.expandFromDrawer, asRoll), (.open(.check), asRoll), (.follow, none)]), ["R", "-<R", "K<R", "R"])
        XCTAssertEqual(codes([(.open(.roll), none), (.follow, notes), (.open(.check), notes), (.follow, none)]), ["R", "R", "K", "K"])
        // The Contents' bookmark into Expand puts the drawer away itself first, so Back to page does not give it back.
        XCTAssertEqual(codes([(.open(.contents), none), (.close(.contents), notes), (.follow, none)]), ["C", "-", "-"])
    }

    /// «Открыть в черновике» on Expand's bar leaves Expand and opens Sources in one step: what Expand had put away comes
    /// back under Sources (question for Andrei, B2: which of the two happened depended on the order SwiftUI ran two
    /// reactions in; this is the order they were declared in).
    func testTheDraftFromExpandKeepsWhatExpandPutAway() {
        XCTAssertEqual(codes([(.open(.roll), none), (.expandFromDrawer, asRoll), (.open(.versions), none), (.close(.versions), none)]), ["R", "-<R", "S^R", "R"])
        XCTAssertEqual(codes([(.open(.contents), none), (.follow, notes), (.open(.versions), none), (.close(.versions), none)]), ["C", "-<C", "S^C", "C"])
        XCTAssertEqual(codes([(.open(.roll), none), (.expandFromDrawer, asRoll), (.open(.versions), none)], sourcesOn: false), ["R", "-<R", "S"])
    }

    /// The roll on Expand's card put away: its unsaved note edits go, unless the drawer is open or comes back.
    func testTheRollLeavesExpandsCardForGoodOnlyWithoutTheDrawer() {
        let card = PanelState().after(.follow, expand: asRoll)
        XCTAssertTrue(card.after(.follow, expand: notes).rollLeftFocusForGood, "Notes chosen, no drawer")
        XCTAssertTrue(card.after(.follow, expand: none).rollLeftFocusForGood, "Back, no drawer")
        let fromDrawer = PanelState().after(.open(.roll), expand: none).after(.expandFromDrawer, expand: asRoll)
        XCTAssertFalse(fromDrawer.after(.follow, expand: none).rollLeftFocusForGood, "Collapse: back to the drawer")
        XCTAssertFalse(fromDrawer.after(.follow, expand: notes).rollLeftFocusForGood, "Notes chosen: the drawer waits")
        XCTAssertFalse(fromDrawer.after(.open(.versions), expand: none).rollLeftFocusForGood, "the draft: the drawer under Sources")
        let shownOver = PanelState(sourcesOn: false).after(.open(.roll), expand: none).after(.expandFromDrawer, expand: asRoll)
        XCTAssertFalse(shownOver.after(.open(.versions), expand: none).rollLeftFocusForGood, "the drawer came back, then went with Sources")
    }

    /// Insights, Check or Sources: the sheet on the right.
    func testTheSheetOnTheRight() {
        for (panel, letter) in Self.letters {
            let open = PanelState().after(.open(panel), expand: none)
            XCTAssertEqual(open.sheetOpen, ["I", "K", "S"].contains(letter), letter)
        }
    }
}
