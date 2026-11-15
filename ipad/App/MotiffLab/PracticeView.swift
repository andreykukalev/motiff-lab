import SwiftUI
import WebKit

/// The workspace's two visibilities (#582 item 10) over practice view (#533 11), the web app's rule
/// (Scripts/layout/practice-view.ts) on the iPad. Two states, each changed without the other:
/// - the selection controls (`selectionHidden`): the selection menu and the hints of the selection (cleared · Undo, how to
///   hold); the selection's band stays;
/// - the playback controls (`on`, practice view as it was): the pill, the hint back to the playing, the blue playhead of the
///   selection and the roll's loop strip; the black playhead of the piece stays, still dragged, and a tap on the page shows
///   Play/Pause and «Выйти из практики» (the playback controls back) for a few seconds, as a video player does.
/// The bar's «Практика» hides both in one press and shows both again; its list sets each one, and the selection menu's Hide
/// hides the selection controls only. Nothing else changes: playback, the loop, the selection and the position go on, so
/// showing them again shows them as they are. View state only: not in the project file, not a step of the selection's
/// history.
struct PracticeView: Equatable {
    /// The playback controls shown (the normal workspace), hidden (practice view), or hidden with practice view's own
    /// controls shown for a while.
    enum Shown: String {
        case off, on, reveal
    }

    /// Both hidden or both shown (the bar's «Практика»: `toggle` hides both from the normal workspace, else shows both),
    /// one group shown or hidden on its own (#582 item 10), a tap on the page (it shows the controls), a touch on the shown
    /// controls (they stay a while longer), and their time over (they hide, whatever the clock says, as the timer may wake a
    /// moment early).
    enum Event {
        case enter, leave, toggle, tap, touch, hide
        case selection(shown: Bool)
        case playback(shown: Bool)
    }

    /// Which of the elements over the music the page shows.
    struct Chrome: Equatable {
        var menu: Bool
        var transport: Bool
        var selectionHead: Bool
        var pieceHead: Bool
        var loopStrip: Bool
        var hints: Bool
        var controls: Bool
    }

    /// How long a tap shows the controls, as the web app's REVEAL_MS.
    static let revealFor: TimeInterval = 3

    /// The playback controls hidden: practice view.
    private(set) var on: Bool
    /// Until when practice view's own controls show; nil while they do not.
    private(set) var revealUntil: Date?
    /// The selection controls hidden (#582 item 10): the selection menu and its hints. Track A's selection popup reads it
    /// through `ScoreController.selectionControlsShown`.
    private(set) var selectionHidden: Bool

    init(on: Bool = false, revealUntil: Date? = nil, selectionHidden: Bool = false) {
        self.on = on
        self.revealUntil = on ? revealUntil : nil
        self.selectionHidden = selectionHidden
    }

    /// Both groups shown: the normal workspace.
    var normal: Bool { !on && !selectionHidden }

    /// The view after `event` at `now`; each event but the two of both groups leaves the selection controls as they are.
    func after(_ event: Event, at now: Date) -> PracticeView {
        switch event {
        case .enter:
            return PracticeView(on: true, selectionHidden: true)
        case .leave:
            return PracticeView()
        case .toggle:
            return normal ? PracticeView(on: true, selectionHidden: true) : PracticeView()
        case .tap:
            return on ? PracticeView(on: true, revealUntil: now.addingTimeInterval(Self.revealFor), selectionHidden: selectionHidden) : self
        case .touch:
            guard on, let until = revealUntil, now < until else { return self }
            return PracticeView(on: true, revealUntil: now.addingTimeInterval(Self.revealFor), selectionHidden: selectionHidden)
        case .hide:
            return on ? PracticeView(on: true, selectionHidden: selectionHidden) : self
        case let .selection(shown):
            return PracticeView(on: on, revealUntil: revealUntil, selectionHidden: !shown)
        case let .playback(shown):
            if shown { return PracticeView(selectionHidden: selectionHidden) }
            return on ? self : PracticeView(on: true, selectionHidden: selectionHidden)
        }
    }

    func shown(at now: Date) -> Shown {
        guard on else { return .off }
        guard let until = revealUntil, now < until else { return .on }
        return .reveal
    }

    /// What the score's page shows at `now`, in Expand or not: Expand has its own bar and pill, so in it the page is the
    /// normal workspace and leaving it shows practice view again if it was on.
    func shown(at now: Date, focused: Bool) -> Shown {
        focused ? .off : shown(at: now)
    }

    /// Whether the selection controls show at all, in Expand or not: Expand has its own bar, so in it they are as normal.
    func selectionShown(focused: Bool) -> Bool {
        focused || !selectionHidden
    }

    /// What the page shows at `now`, in Expand or not.
    func chrome(at now: Date, focused: Bool) -> Chrome {
        Self.chrome(shown(at: now, focused: focused), selectionShown: selectionShown(focused: focused))
    }

    /// Whether the bar's «Практика» is lit, the kit's active state (#543 7): while both groups show, the normal workspace;
    /// dark while either is hidden, when its press shows both. It is read from the same state as what the page shows, so
    /// every way in and out (the button and its list, the menu's Hide, the controls' Show, Expand left) moves them together.
    static func lit(_ shown: Shown, selectionShown: Bool = true) -> Bool {
        let shows = Self.chrome(shown, selectionShown: selectionShown)
        return shows.menu && shows.transport
    }

    func lit(at now: Date, focused: Bool) -> Bool {
        Self.lit(shown(at: now, focused: focused), selectionShown: selectionShown(focused: focused))
    }

    /// How long from `now` until the controls hide again; 0 when they are not shown.
    func revealLeft(at now: Date) -> TimeInterval {
        guard on, let until = revealUntil else { return 0 }
        return max(0, until.timeIntervalSince(now))
    }

    /// What shows in each state of the playback controls and of the selection controls, each group by its own state only
    /// (#582 item 10); the selection's band, the notes and the black playhead always do. `hints` are the selection's (cleared
    /// · Undo, how to hold); the hint back to the playing goes with the pill (`transport`).
    static func chrome(_ shown: Shown, selectionShown: Bool = true) -> Chrome {
        let playback = shown == .off
        return Chrome(
            menu: selectionShown, transport: playback, selectionHead: playback, pieceHead: true, loopStrip: playback,
            hints: selectionShown, controls: shown == .reveal
        )
    }
}

extension ScoreController {
    /// Practice view in force: not in Expand, which has its own bar and pill.
    var practicing: Bool { practice.on && !focused }

    /// What the page shows now.
    var practiceShown: PracticeView.Shown { practice.shown(at: Date(), focused: focused) }

    var practiceChrome: PracticeView.Chrome { practice.chrome(at: Date(), focused: focused) }

    /// The selection controls visible (#582 item 10): the selection menu and its hints. The state track A's selection popup
    /// and gestures read; set only by the bar's «Практика», its list and the menu's Hide, never by the playback controls.
    var selectionControlsShown: Bool { practice.selectionShown(focused: focused) }

    /// The playback controls visible (#582 item 10): the pill, the blue playhead, the loop strip, the hint back to playing.
    var playbackControlsShown: Bool { !practicing }

    /// The bar's «Практика» and its list, the menu's Hide, the controls' Show, a tap on the page or a touch on the controls.
    func practise(_ event: PracticeView.Event) {
        let now = Date()
        practice = practice.after(event, at: now)
        practiceTimer?.cancel()
        let left = practice.revealLeft(at: now)
        if left > 0 {
            practiceTimer = Task { [weak self] in
                try? await Task.sleep(for: .seconds(left))
                guard !Task.isCancelled, let self else { return }
                self.practice = self.practice.after(.hide, at: Date())
            }
        }
    }

    /// Tells the page whether practice view is on: it hides the blue playhead and stops offering it to the Pencil (score.js).
    /// ScoreScreen calls it whenever `practicing` changes, Expand entered or left included.
    func sendPractice() {
        let shown = practicing ? "on" : "off"
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.practice(shown)", arguments: ["shown": shown], contentWorld: .page) }
    }

    /// Tells the page whether the selection controls show (#582 item 10): hidden, the selection's handles go with the menu
    /// and the Pencil writes where they were (score.js). ScoreScreen calls it whenever `selectionControlsShown` changes.
    func sendSelectionControls(_ shown: Bool) {
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.selectionControls(shown)", arguments: ["shown": shown], contentWorld: .page) }
    }
}

/// Practice view's own controls (#533 11): Play/Pause and «Выйти из практики», where the pill sits, shown for a few seconds
/// after a tap on the page; a touch on them keeps them a while longer. Leaving shows the playback controls again and leaves
/// the selection controls as they are (#582 item 10).
struct PracticeControls: View {
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        let playing = player.state != .stopped
        HStack(spacing: CGFloat.ds("s-1")) {
            Button {
                score.practise(.touch)
                // The pill's own Play: the playback model's press under one position, else Play and Pause as before.
                if score.onePosition { player.apply(.press) } else { player.toggle() }
            } label: {
                Icon(name: playing ? "pause" : "playf")
                    .foregroundStyle(Color.ds("on-accent"))
                    .frame(width: 40, height: 40)
                    .background(Color.ds("accent"), in: Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(text[playing ? "transport.pause" : "transport.play"])
            Button {
                score.practise(.playback(shown: true))
            } label: {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "practice")
                    Text(text["practice.leave"]).dsType(.label)
                }
                .padding(.horizontal, CGFloat.ds("s-3"))
                .frame(height: 36)
            }
            .buttonStyle(DSItemStyle())
        }
        .padding(.leading, 6)
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: 52)
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["practice.controls"])
    }
}

/// The bar's «Практика» (#533 11) and its list (#582 item 10): one press hides the selection controls and the playback
/// controls together, and shows both again while either is hidden; the chevron lists the two, each shown or hidden on its
/// own. Lit while both show, dark while either is hidden (#543 7, PracticeView.lit); VoiceOver names it by what it shows,
/// selected while both show.
struct PracticeButton: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    @State private var listOpen = false

    var body: some View {
        let lit = score.practice.lit(at: Date(), focused: score.focused)
        HStack(spacing: 0) {
            Button {
                score.practise(.toggle)
            } label: {
                Icon(name: "practice", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: lit))
            // With a keyboard, Escape shows both again before it would clear the selection.
            .keyboardShortcut(score.practicing ? KeyboardShortcut(.escape, modifiers: []) : nil)
            .accessibilityLabel(text["bar.practice"])
            .accessibilityHint(text["practice.hint"])
            .accessibilityAddTraits(lit ? .isSelected : [])
            Button {
                listOpen.toggle()
            } label: {
                Icon(name: "chevd", size: CGFloat.ds("icon-s")).frame(width: 20, height: 40)
            }
            .buttonStyle(BarActionStyle(on: listOpen))
            .accessibilityLabel(text["practice.list"])
            .popover(isPresented: $listOpen) {
                WorkspaceControlsList(score: score, text: text)
                    .padding(CGFloat.ds("s-4"))
                    .frame(width: 300, alignment: .leading)
                    .presentationCompactAdaptation(.popover)
            }
        }
    }
}

/// The two visibilities of the workspace (#582 item 10), each its own switch: the selection controls (the menu and its
/// hints) and the playback controls (the pill, the blue playhead, the loop strip); turning one never moves the other.
struct WorkspaceControlsList: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["practice.listTitle"]).overline()
            toggle(text["practice.selectionControls"], isOn: !score.practice.selectionHidden) { score.practise(.selection(shown: $0)) }
            toggle(text["practice.playbackControls"], isOn: !score.practice.on) { score.practise(.playback(shown: $0)) }
            Text(text["practice.listHint"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func toggle(_ label: String, isOn: Bool, set: @escaping (Bool) -> Void) -> some View {
        Toggle(label, isOn: Binding(get: { isOn }, set: set))
            .dsType(.label)
            .foregroundStyle(Color.ds("ink"))
            .tint(Color.ds("accent"))
    }
}
