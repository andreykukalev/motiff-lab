import Foundation

/// Expand as a focused practice view (#521 17, 19): the selection lifted over the page, as notes or as a roll, bound to the
/// exact passage it was opened on. Only with a selection; nothing in it creates or changes the selection or a loop, and no
/// arrow steps to another phrase; the roll moves only within the passage (Scripts/roll/piano-roll.ts setPassage); Play
/// plays the passage once and stops at its end. The way back is the way in: from the score «К странице», from the roll
/// «Свернуть», and either brings back the workspace as it was. The rules, apart from the views so that they are tested.
enum ExpandEntry: Equatable, Sendable {
    /// From the score: the selection menu's Expand (or a bookmark or a phrase chosen again, or the resume point).
    case notes
    /// From the roll's drawer: its Expand.
    case roll

    /// The way back's word (#521 17): Back to page from the score, Collapse from the roll.
    var backKey: String {
        switch self {
        case .notes: "focus.back"
        case .roll: "focus.collapse"
        }
    }

    /// What the way back does, for a hint and VoiceOver.
    var backHintKey: String {
        switch self {
        case .notes: "focus.backHint"
        case .roll: "focus.collapseHint"
        }
    }

    /// The way back's icon: the page's back arrow, or the two arrows of Expand turned inwards (the kit's `collapse`).
    var backIcon: String {
        switch self {
        case .notes: "back"
        case .roll: "collapse"
        }
    }
}

enum ExpandRules {
    /// Whether Expand opens (#521 19.1): only on a selection, which it is then bound to; never while the Pen writes.
    static func opens(selected: Bool, penOn: Bool) -> Bool {
        selected && !penOn
    }

    /// Where Play in Expand starts (#521 19.4): at the playhead while it is inside the passage, else at the passage's start.
    static func playStart(at tick: Double, in passage: Range<Double>) -> Double {
        passage.contains(tick) ? tick : passage.lowerBound
    }

    /// Where the playhead moved on the roll in Expand goes: inside the passage, never past it (#521 19.2).
    static func place(_ tick: Double, in passage: Range<Double>) -> Double {
        Player.place(tick, inside: passage)
    }

    /// What Undo and Redo act on in Expand: nothing, as the selection is the passage and nothing there is edited (#521 19.3).
    static func undoOwner(focused: Bool, otherwise owner: StylusRules.UndoOwner) -> StylusRules.UndoOwner {
        focused ? .nothing : owner
    }

    /// The stylus menu's controls in Expand: none, as there is no selection to undo, redo or clear there (#521 19.3).
    static func ringControls(focused: Bool, otherwise controls: [StylusRules.RingControl]) -> [StylusRules.RingControl] {
        focused ? [] : controls
    }
}

extension Player {
    /// Play in Expand (#521 19.4): the passage once, from the playhead inside it or from its start, stopping at its end and
    /// waiting at its start; while it plays, a pause. Nothing loops round and nothing plays past the passage.
    func playPassage(_ passage: Range<Double>) {
        guard state == .stopped else {
            toggle()
            return
        }
        transportKept = true
        setLoop(passage, from: ExpandRules.playStart(at: tick, in: passage), once: true)
    }

    /// Expand opened, or its passage's span changed (Lead-in), while the music plays: it plays on within the passage, from
    /// its start when it was elsewhere, and stops at its end; still, the passage waits for Play.
    func keepWithin(_ passage: Range<Double>) {
        if state == .stopped {
            setLoop(passage, from: ExpandRules.playStart(at: tick, in: passage), play: false, once: true)
        } else if passage.contains(tick) {
            setLoop(passage, from: tick, play: false, once: true)
        } else {
            setLoop(passage, once: true)
        }
    }

    /// The playhead moved on the roll in Expand, by a drag, a nudge or a tap: inside the passage; a tap plays the passage on
    /// from there, once.
    func move(to tick: Double, play: Bool, within passage: Range<Double>) {
        let at = ExpandRules.place(tick, in: passage)
        if play && state == .stopped {
            transportKept = true
            setLoop(passage, from: at, once: true)
        } else {
            seek(to: at)
        }
    }
}
