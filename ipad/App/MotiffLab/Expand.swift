import Foundation

/// Expand as a focused practice view (#521 17, 19; #533 2, 3): the selection lifted over the page, as notes or as a roll,
/// bound to the exact passage it was opened on. Only with a selection; nothing in it creates or changes the selection or
/// the Loop, and no arrow steps to another phrase; the roll moves only within the passage (Scripts/roll/piano-roll.ts
/// setPassage); Play plays the passage round and round, with no Loop to turn off; the playhead on the roll is taken and
/// dragged inside the passage, the notes it passes sounding, and let go playback goes on from there. The way back is the
/// way in: from the score «К странице», from the roll «Свернуть», and either brings back the workspace as it was. The
/// rules, apart from the views so that they are tested.
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

    /// The playback model as Expand holds a drag (#533 2): the passage as a selection that always loops, so the playhead
    /// taken is the blue one, kept inside the passage, and the steps are never the loop's.
    static func held(_ passage: Range<Double>, tick: Double, transport: TransportState) -> PlaybackModel.State {
        PlaybackModel.State(selection: passage, loop: true, tick: tick, transport: transport)
    }

    /// The playhead on Expand's roll taken, dragged or let go (#533 2, the model's T14–T16 over the passage): taken, playback
    /// stands; dragged, the position goes there inside the passage and the notes it passes sound; let go, they fade and it
    /// plays on when it played. `drag` is the one held before, `tick` and `transport` the player's now; gives the one held
    /// after and the engine's steps. A new one taken while another is held lets the old one go first.
    static func gesture(_ gesture: PlayheadGesture, in passage: Range<Double>, drag: PlayheadDrag?, tick: Double, transport: TransportState)
        -> (drag: PlayheadDrag?, steps: [TransportStep])
    {
        switch gesture {
        case .grab:
            var steps: [TransportStep] = []
            var standing = transport
            if drag != nil {
                steps = Self.gesture(.release, in: passage, drag: drag, tick: tick, transport: transport).steps
                standing = steps.last == .play ? .playing : transport
            }
            let taken = PlayheadDrag(handle: .blue, resume: PlaybackModel.playing(standing), last: tick)
            return (taken, steps + PlaybackModel.next(held(passage, tick: tick, transport: standing), .grab).steps)
        case .move(let at):
            guard var taken = drag else { return (nil, []) }
            let decided = PlaybackModel.next(held(passage, tick: taken.last, transport: .stopped), .drag(.blue, to: at))
            taken.last = decided.state.tick
            return (taken, decided.steps)
        case .release:
            guard let taken = drag else { return (nil, []) }
            return (nil, PlaybackModel.next(held(passage, tick: taken.last, transport: .stopped), .release(.blue, resume: taken.resume)).steps)
        }
    }
}

extension Player {
    /// Play in Expand (#533 2): the passage round and round, from the playhead inside it or from its start, back at its
    /// start each time it ends; while it plays, a pause. Nothing plays past the passage.
    func playPassage(_ passage: Range<Double>) {
        guard state == .stopped else {
            toggle()
            return
        }
        transportKept = true
        setLoop(passage, from: ExpandRules.playStart(at: tick, in: passage))
    }

    /// Expand opened while the music plays: it plays on round the passage, from its start when it was elsewhere; still, the
    /// passage waits for Play.
    func keepWithin(_ passage: Range<Double>) {
        if state == .stopped {
            setLoop(passage, from: ExpandRules.playStart(at: tick, in: passage), play: false)
        } else if passage.contains(tick) {
            setLoop(passage, from: tick, play: false)
        } else {
            setLoop(passage)
        }
    }

    /// The playhead moved on the roll in Expand by a nudge or a tap: inside the passage; a tap plays the passage round from
    /// there.
    func move(to tick: Double, play: Bool, within passage: Range<Double>) {
        let at = ExpandRules.place(tick, in: passage)
        if play && state == .stopped {
            transportKept = true
            setLoop(passage, from: at)
        } else {
            seek(to: at)
        }
    }

    /// The playhead dragged on Expand's roll (#533 2, ExpandRules.gesture): it stands while held, scrubs inside the passage,
    /// and plays on from where it is let go; the passage keeps looping, and the model outside Expand is left as it is (T18).
    func handle(_ gesture: PlayheadGesture, within passage: Range<Double>) {
        let decided = ExpandRules.gesture(gesture, in: passage, drag: drag, tick: tick, transport: state)
        drag = decided.drag
        perform(decided.steps)
    }
}
