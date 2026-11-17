import Foundation

/// Which of the score screen's panels are open (the architecture blueprint of v0.7, section 5.3 step 1): the roll's drawer,
/// Insights, the Contents, Check, Sources (Other versions), Settings, the Reference and More, and what the screen remembers
/// to give back: that a place was opened from More (its "‹ More", testing item 20), the roll's drawer and the Contents as
/// Expand found them (#521 17), and what was open under Sources (#582 item 4).
///
/// `after(_:expand:)` is the one way to change it. It applies what was asked and then the screen's rules, which until
/// v0.7 were `.onChange` reactions of `ScoreScreen` on one flag each: a rule runs when what it watches has changed since
/// the last round, in the order the reactions were declared, and its changes are seen in the next round, as SwiftUI ran
/// them render by render. So every case keeps the answer it had, the odd ones too (`PanelStateTests` lists them).
struct PanelState: Equatable, Sendable {
    enum Panel: CaseIterable, Sendable {
        case roll, insights, contents, check, versions, settings, reference, more
    }

    /// What is asked of the panels.
    enum Intent: Equatable, Sendable {
        case open(Panel)
        case close(Panel)
        case toggle(Panel)
        /// Check, Sources or Settings from More: More goes, and the place shows "‹ More".
        case openFromMore(Panel)
        /// "‹ More": the place opened from More goes (nil for Piece DNA, which is none of these), and its way back with it.
        case backToMore(Panel?)
        /// Expand on the roll's drawer (#521 19.1): the drawer goes, to come back with Collapse.
        case expandFromDrawer
        /// The assistant's panel opened: one thing at a time on the right.
        case assistantOpened
        /// Nothing asked: only Expand changed.
        case follow
    }

    /// Expand as the score shows it: the passage in focus, and shown as a roll on its card.
    struct Expand: Equatable, Sendable {
        var focused = false
        var roll = false
    }

    /// What was open as Sources opened over it, given back as Sources closes (#582 item 4).
    struct Shelved: Equatable, Sendable {
        var roll = false
        var insights = false
        var contents = false
    }

    /// Whether Sources is there: never on a view shown over the piece (0030), where nothing is kept under it.
    let sourcesOn: Bool
    private(set) var opened: Set<Panel> = []
    /// Whether Check, Sources or Settings was opened from More, so it shows "‹ More".
    private(set) var fromMore = false
    /// The roll's drawer, open as the passage went into Expand as a roll: Collapse opens it again.
    private(set) var rollBeforeFocus = false
    /// The Contents, open as Expand opened: Back to page opens it again.
    private(set) var contentsBeforeFocus = false
    /// What was open under Sources; nil while Sources is closed.
    private(set) var beforeSources: Shelved?
    /// Whether the roll last left Expand's card for no place (its drawer neither open nor coming back): its unsaved note
    /// edits go then, as they go with the drawer.
    private(set) var rollLeftFocusForGood = false
    /// Expand as these rules last followed it, so that a change of it is followed once.
    private(set) var expand = Expand()

    init(sourcesOn: Bool = true) {
        self.sourcesOn = sourcesOn
    }

    subscript(panel: Panel) -> Bool {
        opened.contains(panel)
    }

    /// Insights, Check or Sources: the sheet on the right, where the assistant's panel would be.
    var sheetOpen: Bool { self[.insights] || self[.check] || self[.versions] }

    /// The panels after `intent`, with Expand as the score shows it now.
    func after(_ intent: Intent, expand now: Expand) -> PanelState {
        var next = self
        next.apply(intent)
        var seen = Watched(self, expand)
        // The rules settle within four rounds; eight is the bound of a loop that must end.
        for _ in 0..<8 {
            let watched = Watched(next, now)
            guard watched != seen else { break }
            next.react(was: seen, now: watched, focused: now.focused)
            seen = watched
        }
        next.expand = now
        return next
    }

    private mutating func set(_ panel: Panel, _ on: Bool) {
        if on { opened.insert(panel) } else { opened.remove(panel) }
    }

    private mutating func apply(_ intent: Intent) {
        switch intent {
        case .open(let panel): set(panel, true)
        case .close(let panel): set(panel, false)
        case .toggle(let panel): set(panel, !self[panel])
        case .openFromMore(let panel):
            set(.more, false)
            set(panel, true)
            fromMore = true
        case .backToMore(let panel):
            if let panel { set(panel, false) }
            fromMore = false
        case .expandFromDrawer:
            rollBeforeFocus = true
            set(.roll, false)
        case .assistantOpened:
            close(.insights, .check, .versions)
        case .follow:
            break
        }
    }

    /// What the rules watch, each the value one reaction of the screen observed.
    private struct Watched: Equatable {
        let contents, focused, rollShown, roll, insights, fromMoreKept, check, versions, other: Bool

        init(_ state: PanelState, _ expand: Expand) {
            contents = state[.contents]
            focused = expand.focused
            rollShown = expand.roll
            roll = state[.roll]
            insights = state[.insights]
            fromMoreKept = state[.check] || state[.versions] || state[.settings]
            check = state[.check]
            versions = state[.versions]
            other = state[.insights] || state[.roll] || state[.contents] || expand.focused
        }
    }

    /// One round: each rule whose value changed since the last round, in their order.
    private mutating func react(was: Watched, now: Watched, focused: Bool) {
        // Opening the roll, Insights, the Contents or Check puts the others of them away.
        if now.contents != was.contents, now.contents { close(.roll, .insights) }
        if now.focused != was.focused { focusChanged(now.focused) }
        if now.rollShown != was.rollShown { rollFocusChanged(now.rollShown) }
        if now.roll != was.roll, now.roll { close(.contents, .insights) }
        if now.insights != was.insights, now.insights { close(.roll, .contents) }
        // The way back to More belongs to the place opened from it, and goes with it.
        if now.fromMoreKept != was.fromMoreKept, !now.fromMoreKept { fromMore = false }
        if now.check != was.check, now.check { close(.insights, .roll, .contents, .versions) }
        if now.versions != was.versions { versionsChanged(now.versions, focused: focused) }
        // Check and Sources go when another place opens, or Expand.
        if now.other != was.other, now.other { close(.check, .versions) }
    }

    private mutating func close(_ panels: Panel...) {
        for panel in panels { set(panel, false) }
    }

    /// Expand covers the page (#521 17, 19): the Contents goes, and comes back with the way back as it was, and so does the
    /// roll's drawer.
    private mutating func focusChanged(_ focused: Bool) {
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

    /// The phrase shown as a roll on Expand's card takes the drawer's roll, which comes back with Collapse.
    private mutating func rollFocusChanged(_ shown: Bool) {
        guard shown else {
            rollLeftFocusForGood = !self[.roll] && !rollBeforeFocus
            return
        }
        if self[.roll] {
            rollBeforeFocus = true
            set(.roll, false)
        }
    }

    /// Sources opened puts the other places away, keeping what was open under it; closed by its ✕ or the bar's button, not
    /// by another place opening, it gives the view back as it was (#582 item 4).
    private mutating func versionsChanged(_ open: Bool, focused: Bool) {
        if open {
            if sourcesOn { beforeSources = Shelved(roll: self[.roll], insights: self[.insights], contents: self[.contents]) }
            close(.insights, .roll, .contents, .check)
        } else if let before = beforeSources {
            beforeSources = nil
            if !(self[.insights] || self[.roll] || self[.contents] || self[.check] || focused) {
                set(.roll, before.roll)
                set(.insights, before.insights)
                set(.contents, before.contents)
            }
        }
    }
}
