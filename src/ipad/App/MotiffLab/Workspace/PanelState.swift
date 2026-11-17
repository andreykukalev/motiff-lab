import Foundation

/// Which of the score screen's panels are open (the architecture blueprint of v0.7, section 5.3 step 1): the roll's drawer,
/// Insights, the Contents, Check, Sources (Other versions), Settings, the Reference and More, and what the screen remembers
/// to give back: which places were opened from More (each its "‹ More", testing item 20), the roll's drawer and the
/// Contents as Expand found them (#521 17), and what was open under Sources (#582 item 4).
///
/// It changes in two ways, both pure. `asked` is what an action writes: the panel it opens, closes or toggles, and nothing
/// more. `following` is one rule of the screen: what the change of one value does to the other panels. Until v0.7 each
/// rule was the body of an `.onChange` reaction of `ScoreScreen`; the reaction still observes the value and runs the rule
/// where those lines were, so a change settles render by render as before, and every other reaction (the roll, Insights,
/// the page's inset, the assistant) fires in the render and at the place it did. `PanelStateTests` runs the rules as the
/// renders do, over the table of every case.
struct PanelState: Equatable, Sendable {
    enum Panel: CaseIterable, Sendable {
        case roll, insights, contents, check, versions, settings, reference, more
    }

    /// What an action asks of the panels.
    enum Intent: Equatable, Sendable {
        case open(Panel)
        case close(Panel)
        case toggle(Panel)
        /// Check, Sources or Settings from More: More goes, and the place shows "‹ More".
        case openFromMore(Panel)
        /// "‹ More": the place opened from More goes (nil for Piece DNA, which is none of these), and the way back with it.
        case backToMore(Panel?)
        /// Expand on the roll's drawer (#521 19.1): the drawer goes, to come back with Collapse.
        case expandFromDrawer
        /// The assistant's panel opened: one thing at a time on the right, so the sheet goes and Insights does not come back.
        case assistantOpened
    }

    /// A rule of the screen, named by the value whose change it follows, with the value as the render showed it.
    enum Rule: Equatable, Sendable {
        /// The Contents opened or closed.
        case contents(Bool)
        /// Expand entered or left (`ScoreController.focused`).
        case expand(Bool)
        /// The passage shown as a roll on Expand's card, or no longer.
        case rollOnCard(Bool)
        /// The roll's drawer opened or closed.
        case roll(Bool)
        /// Insights opened or closed.
        case insights(Bool)
        /// `placeFromMoreOpen` changed.
        case placeFromMore(Bool)
        /// Check opened or closed.
        case check(Bool)
        /// Sources opened or closed; `focused` is Expand as it is now.
        case versions(Bool, focused: Bool)
        /// `otherPlaceOpen` changed.
        case otherPlace(Bool)
    }

    /// What was open as Sources opened over it, given back as Sources closes (#582 item 4).
    struct Shelved: Equatable, Sendable {
        var roll = false
        var insights = false
        var contents = false
    }

    /// Whether Sources is there: never on a view shown over the piece (0030), where nothing is kept under it.
    let sourcesOn: Bool
    private var opened: Set<Panel> = []
    /// Check, Sources or Settings, each one opened from More, so it shows "‹ More": only the place opened from More
    /// (Andrei's answer 10, 2026-11-17), never one opened otherwise beside it.
    private(set) var fromMore: Set<Panel> = []
    /// The roll's drawer, open as the passage went into Expand as a roll: Collapse opens it again.
    private(set) var rollBeforeFocus = false
    /// The Contents, open as Expand opened: Back to page opens it again.
    private(set) var contentsBeforeFocus = false
    /// What was open under Sources; nil while Sources is closed.
    private(set) var beforeSources: Shelved?

    init(sourcesOn: Bool = true) {
        self.sourcesOn = sourcesOn
    }

    subscript(panel: Panel) -> Bool {
        opened.contains(panel)
    }

    /// Insights, Check or Sources: the sheet on the right, where the assistant's panel would be.
    var sheetOpen: Bool { self[.insights] || self[.check] || self[.versions] }

    /// Check, Sources or Settings, the places More opens: the way back to More is kept while one of them is open.
    var placeFromMoreOpen: Bool { self[.check] || self[.versions] || self[.settings] }

    /// Insights, the roll's drawer, the Contents or Expand: what Check and Sources give way to.
    func otherPlaceOpen(focused: Bool) -> Bool { self[.insights] || self[.roll] || self[.contents] || focused }

    /// Whether the roll's unsaved note edits go as its drawer closes. They do, as with Cancel: edits are kept only by Save.
    /// Not when Expand takes the roll, for the editor goes into the roll on the card with its draft (#479 item 23).
    var rollEditsGoWithDrawer: Bool { !rollBeforeFocus }

    /// Whether they go as the roll leaves Expand's card: when it is put away, not when it goes back to the drawer.
    var rollEditsGoWithCard: Bool { !self[.roll] && !rollBeforeFocus }

    /// The panels with `intent` asked: what the action writes, before any rule follows it.
    func asked(_ intent: Intent) -> PanelState {
        var next = self
        switch intent {
        case .open(let panel): next.set(panel, true)
        case .close(let panel): next.set(panel, false)
        case .toggle(let panel): next.set(panel, !self[panel])
        case .openFromMore(let panel):
            next.set(.more, false)
            next.set(panel, true)
            next.fromMore.insert(panel)
        case .backToMore(let panel):
            if let panel { next.set(panel, false) }
            next.fromMore = []
        case .expandFromDrawer:
            next.rollBeforeFocus = true
            next.set(.roll, false)
        case .assistantOpened:
            next.close(.insights, .check, .versions, when: true)
            // Sources closed under the assistant's panel would give Insights back, and Insights would close the panel
            // again (Andrei's answer 9, 2026-11-17): the panel stays, and what else was under Sources comes back.
            next.beforeSources?.insights = false
        }
        return next
    }

    /// The panels after one rule.
    func following(_ rule: Rule) -> PanelState {
        var next = self
        switch rule {
        // Opening the roll, Insights, the Contents or Check puts the others of them away.
        case .contents(let open): next.close(.roll, .insights, when: open)
        case .expand(let focused): next.expandChanged(focused)
        // The phrase shown as a roll on Expand's card takes the drawer's roll, which comes back with Collapse.
        case .rollOnCard(let shown):
            if shown, self[.roll] {
                next.rollBeforeFocus = true
                next.set(.roll, false)
            }
        case .roll(let open): next.close(.contents, .insights, when: open)
        case .insights(let open): next.close(.roll, .contents, when: open)
        // The way back to More belongs to the place opened from it, and goes with it.
        case .placeFromMore(let open): if !open { next.fromMore = [] }
        case .check(let open): next.close(.insights, .roll, .contents, .versions, when: open)
        case let .versions(open, focused): next.versionsChanged(open, focused: focused)
        // Check and Sources go when another place opens, or Expand.
        case .otherPlace(let open): next.close(.check, .versions, when: open)
        }
        return next
    }

    /// A place opened anew has no way back to More until More opens it (`openFromMore` adds it after).
    private mutating func set(_ panel: Panel, _ on: Bool) {
        if on, !self[panel] { fromMore.remove(panel) }
        if on { opened.insert(panel) } else { opened.remove(panel) }
    }

    /// A place opened puts `panels` away; closed, it changes nothing.
    private mutating func close(_ panels: Panel..., when open: Bool) {
        if open { opened.subtract(panels) }
    }

    /// Expand covers the page (#521 17, 19): the Contents goes, and comes back with the way back as it was, and so does the
    /// roll's drawer.
    private mutating func expandChanged(_ focused: Bool) {
        if focused {
            contentsBeforeFocus = self[.contents]
            set(.contents, false)
            return
        }
        if rollBeforeFocus {
            rollBeforeFocus = false
            set(.roll, true)
        }
        if contentsBeforeFocus {
            contentsBeforeFocus = false
            set(.contents, true)
        }
    }

    /// Sources opened puts the other places away, keeping what was open under it; closed by its ✕ or the bar's button, not
    /// by another place opening, it gives the view back as it was (#582 item 4).
    private mutating func versionsChanged(_ open: Bool, focused: Bool) {
        if open {
            if sourcesOn { beforeSources = Shelved(roll: self[.roll], insights: self[.insights], contents: self[.contents]) }
            close(.insights, .roll, .contents, .check, when: true)
        } else if let before = beforeSources {
            beforeSources = nil
            if !(otherPlaceOpen(focused: focused) || self[.check]) {
                set(.roll, before.roll)
                set(.insights, before.insights)
                set(.contents, before.contents)
            }
        }
    }
}
