import SwiftUI

/// The pill (ds.css `.pill`): play, the tempo and the metronome, floating at the bottom of the score, then the piano roll
/// and the assistant, where the hand looks for them (Andrei's 0.8 test, findings 7 and 8). Its controls show their icons
/// alone, their words for VoiceOver and the pointer's tip, so it fits beside the assistant (v0.10, finding 22); what shows a
/// value keeps it: the place, Цикл's span, the tempo and the hands, which say the mode. Where even so it has no room, the
/// tempo, the metronome and the roll go into «⋯» (Andrei's answer A). It shows what it is handed and calls the actions it
/// is handed; where it floats is the screen's (`ScoreScreenContent.transportLayer`).
struct Pill: View {
    @ObservedObject var player: Player
    @ObservedObject var selectionModel: SelectionModel
    @ObservedObject var focusModel: FocusModel
    /// A draft's приёмы (v1.1): while their pill steps through them, the phrase's steps are not offered here.
    @ObservedObject var draftModel: DraftTechniquesModel
    @ObservedObject private var metronome = Metronome.shared
    let text: UIText
    /// Expand's passage while it is open, which Play plays round and round (`ScoreController.expandPassage`); nil out of it.
    let passage: Range<Double>?
    /// The phrase before (-1) or after (1), from the pill's ‹ › (`ScoreController.step`).
    let step: (Int) -> Void
    /// The piece tempo the player typed, kept with the piece (`ScoreController.setPieceTempo`).
    let setPieceTempo: (Int?) -> Void
    /// The roll's drawer, which its button opens and puts away.
    @Binding var rollOpen: Bool
    /// Whether the assistant's panel is open: its button is lit.
    let assistantOpen: Bool
    /// The assistant's button: its panel with the conversation it had, or closed (ScoreWorkspace.assistantPressed).
    let assistantPressed: () -> Void

    /// The tempo's popover opened from «⋯» while the pill has no room for the tempo (finding 22).
    @State private var tempoFromMore = false

    /// How much of the pill has room on it (finding 22): all of it; all but the tempo, the metronome and the roll, which go
    /// into «⋯»; and with less still, the phrase's steps go there too.
    private enum Room { case full, compact, tight }

    var body: some View {
        // As built while it fits; with no room (portrait beside the assistant, say), the tempo, the metronome and the roll go
        // into «⋯» and play, the place, Цикл and the hands stay (finding 22, Andrei's answer A of 2026-11-19 09:55 UTC), then
        // the phrase's steps too. SwiftUI measures: the first that fits the width the pill is given, else the last.
        ViewThatFits(in: .horizontal) {
            items(.full)
            items(.compact)
            items(.tight)
        }
        .padding(.leading, 6)
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: 52)
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["transport.label"])
    }

    private func items(_ room: Room) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            playback(room).disabled(!player.ready)
            ItemDivider()
            if room == .full {
                // The roll in its drawer (finding 7); in Expand the bar's Notes | Piano roll shows the passage as a roll.
                if !focusModel.focused {
                    iconItem("roll", text["bar.roll"], on: rollOpen) { rollOpen.toggle() }
                }
            } else {
                more(steps: room == .tight)
            }
            // The assistant (finding 8, Andrei's card of 2026-11-18 21:49 UTC): its panel with the last conversation, or
            // closed; an Ask elsewhere opens it empty.
            iconItem("note", text["pill.assistant"], on: assistantOpen, action: assistantPressed)
                .accessibilityHint(text["pill.assistantHint"])
        }
    }

    /// Play, the tempo, the hands, the phrase's steps and the metronome: they wait for the player. Without room, play, the
    /// hands and the steps; with less, play and the hands alone.
    private func playback(_ room: Room) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            // The playback model (v0.5.2, #495 UX-10): one Play, cobalt or ink by the place, and Цикл with the span; in
            // Expand the same Play over its passage, which always loops (v0.6.4, spec 8).
            PositionTransport(player: player, passage: passage, text: text)
            if room == .full {
                // The tempo observes only itself (#511 section 17), so it opens and changes while playing.
                TempoControl(tempo: player.tempo, text: text, setPiece: setPieceTempo)
                    .equatable()
            }
            handsControls
            if room != .tight {
                stepButtons
            }
            if room == .full {
                ItemDivider()
                // The metronome clicks at once, with or without Play; held, a slide sets its tempo (#521 section 15). Its icon
                // alone, as the roll's and the assistant's, so the pill fits beside the assistant (finding 22).
                iconItem("metronome", text["transport.metronome"], on: metronome.running) { metronome.toggle() }
                    .modifier(MetronomeHold(metronome: metronome, text: text) { metronome.toggle() })
            }
        }
    }

    /// «⋯» with what the pill had no room for, by their words, in the pill's order: the tempo, whose item opens its popover
    /// here; with `steps`, the phrase before and after; the metronome on or off; the roll's drawer, not in Expand. All but
    /// the roll wait for the player.
    private func more(steps: Bool) -> some View {
        Menu {
            TempoMenuItem(tempo: player.tempo, text: text) { tempoFromMore = true }
                .disabled(!player.ready)
            if steps && stepsShown {
                Button {
                    step(-1)
                } label: {
                    Label(text["focus.previous"], image: "ic-back")
                }
                .disabled(!player.ready || !focusModel.canStepBack)
                Button {
                    step(1)
                } label: {
                    Label(text["focus.next"], image: "ic-fwd")
                }
                .disabled(!player.ready || !focusModel.canStepOn)
            }
            Toggle(isOn: Binding(get: { metronome.running }, set: { _ in metronome.toggle() })) {
                Label(text["transport.metronome"], image: "ic-metronome")
            }
            .disabled(!player.ready)
            if !focusModel.focused {
                Toggle(isOn: $rollOpen) {
                    Label(text["bar.roll"], image: "ic-roll")
                }
            }
        } label: {
            Icon(name: "more")
        }
        .menuStyle(.button)
        .buttonStyle(DSIconItemStyle(on: tempoFromMore))
        .accessibilityLabel(text["bar.more"])
        // «Ещё» as the bar's More is: the walk tells them apart by this.
        .accessibilityIdentifier("ml-pill-more")
        .help(text["bar.more"])
        .tempoPopover(isPresented: $tempoFromMore, tempo: player.tempo, text: text, setPiece: setPieceTempo)
        // With room again «⋯» goes, and its popover with it, not to come back when the room goes again.
        .onDisappear { tempoFromMore = false }
    }

    /// Hands separately at the instrument (#328): both, right or left, two touches away. The hands are the ones music-core
    /// gives the notes, and the menu says when they were worked out rather than read from the file.
    @ViewBuilder private var handsControls: some View {
        if player.handsHeld.canSeparate {
            let label = text.format("pill.handsLabel", text[player.hands.labelKey])
            Menu {
                Picker(label, selection: $player.hands) {
                    ForEach(PlayerHands.allCases, id: \.self) { Text(text[$0.labelKey]).tag($0) }
                }
                if player.handsHeld.inferred {
                    Text(text["import.handsInferredWhy"])
                }
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "hands")
                    Text(text[player.hands.labelKey])
                }
            }
            .menuStyle(.button)
            .buttonStyle(DSItemStyle(on: player.hands != .both))
            .accessibilityLabel(label)
            .accessibilityHint(player.handsHeld.inferred ? text["import.handsInferredWhy"] : "")
        }
    }

    /// The phrase before or after without lifting it into focus (#328), with a selection out of Expand; not while a draft's
    /// приёмы are stepped through (`DraftTechniquesModel.stepping`), so one pair of ‹ › is on the screen.
    private var stepsShown: Bool {
        selectionModel.selection != nil && !focusModel.focused && !draftModel.stepping
    }

    @ViewBuilder private var stepButtons: some View {
        if stepsShown {
            stepItem("back", text["focus.previous"], enabled: focusModel.canStepBack) { step(-1) }
            stepItem("fwd", text["focus.next"], enabled: focusModel.canStepOn) { step(1) }
        }
    }
}
