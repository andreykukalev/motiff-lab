import SwiftUI

/// The screen's covers, sheets and reactions, as modifiers the layers are wrapped in (`ScoreScreenContent.page`, in the
/// order `following(sourcing(checking(watching(wired(…)))))`): each reaction calls the workspace in one line, and their
/// order is the one docs/code-map/ipad/workspace.md pins («The order the view must keep»).
extension ScoreScreenContent {
    /// What the screen hands its controllers when it appears, and the project file written when it goes.
    func wired(_ content: some View) -> some View {
        content
            .toolbar(.hidden, for: .navigationBar)
            .task { await workspace.appeared() }
            // A new interface language saves the piece and opens it again where it is; not a view shown over the piece.
            .handsOver(piece: viewing == nil ? piece.id : nil) { await workspace.handOverForLanguage() }
            .onDisappear { workspace.disappeared() }
            .overlay {
                // The pencil palette, opened by the Pencil's double tap or squeeze (#479 item 8), over everything else.
                if let call = penModel.pencilPalette {
                    PencilPalette(holder: penModel, call: call, text: text, modes: score.ringModes)
                }
            }
            .animation(.easeOut(duration: 0.16), value: penModel.pencilPalette)
            .onChange(of: scenePhase) { _, phase in workspace.scenePhaseChanged(phase) }
    }

    /// The drawers and sheets: what covers the page, and one of them at a time.
    func watching(_ content: some View, covered: CGFloat, contentsSide: Bool) -> some View {
        content
            .onChange(of: player.hearingAlone) { _, alone in workspace.hearingAloneChanged(alone) }
            .onChange(of: covered) { _, height in workspace.coveredChanged(height) }
            .onChange(of: contentsSide) { _, side in workspace.contentsSideChanged(side) }
            .onChange(of: panels[.contents]) { _, open in workspace.contentsChanged(open) }
            .onChange(of: focusModel.focused) { _, focused in workspace.focusChanged(focused) }
            .onChange(of: score.practicing, initial: true) { _, on in workspace.practicingChanged(on) }
            .onChange(of: score.selectionControlsShown) { _, shown in workspace.selectionControlsChanged(shown) }
            .onChange(of: focusModel.focused && focusModel.asRoll) { _, shown in workspace.focusRollChanged(shown, dark: colorScheme == .dark) }
            .onChange(of: panels[.roll]) { _, open in workspace.rollDrawerChanged(open, dark: colorScheme == .dark) }
            .onChange(of: checkModel.check) { _, _ in workspace.checkChanged() }
            .onChange(of: panels[.insights]) { _, open in workspace.insightsChanged(open) }
    }

    /// Check and Other versions take the place of the other drawers and sheets, and go when one of them opens. The Settings sheet comes
    /// over the score, and the score and the roll follow each change at once.
    func checking(_ content: some View) -> some View {
        content
            .fullScreenCover(isPresented: Binding(get: { dna != nil }, set: { if !$0 { dna = nil } })) {
                if let map = dna {
                    DnaScreen(
                        map: map, title: piece.title, player: player, text: text,
                        back: dnaFromMore ? (label: text["bar.more"], action: { dna = nil; backToMore(nil) }) : nil,
                        close: { dna = nil },
                        showInScore: { range, openInsights in
                            dna = nil
                            Task {
                                await score.select(range.lowerBound, range.upperBound)
                                if openInsights { change(.open(.insights)) }
                            }
                        },
                        addBookmarks: { await contents.addForm() },
                        ask: {
                            // The map gives way to the score with the assistant beside it, on the whole piece.
                            dna = nil
                            assistant.open(wholePiece: true, selection: selectionModel.selection)
                        }
                    )
                }
            }
            // The PDF linked where it lies in Files (version 0.3): read from there, shown, never written into the piece.
            .fullScreenCover(item: $originals.linkedPdf, onDismiss: { originals.closeLinkedPdf() }) { pdf in
                PdfScreen(piece: pdf, linked: true).heardOver(sources).environmentObject(account)
            }
            .task(id: piece.id) { await workspace.loadOriginals() }
            .overlay(alignment: .center) {
                // Opening the linked PDF from the bar: iOS may first download it from iCloud Drive or Google Drive.
                if originals.loading != nil && !panels[.versions] {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        ProgressView()
                        Text(text["originals.loading"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                    }
                    .padding(CGFloat.ds("s-4"))
                    .modifier(FloatingSurface())
                }
            }
            .sheet(isPresented: panel(.reference)) {
                ReferenceSheet(text: text, settings: settings, piece: piece.id)
            }
            .sheet(isPresented: panel(.settings)) {
                SettingsSheet(settings: settings, account: account, text: text, pieceOverrides: marksModel.pieceOverrides, clearPiece: { score.clearPieceOverrides() },
                              back: panels.fromMore.contains(.settings) ? (label: text["bar.more"], action: { backToMore(.settings) }) : nil)
            }
            .onChange(of: settings.values) { _, _ in workspace.settingsChanged() }
            .onChange(of: panels.placeFromMoreOpen) { _, open in workspace.placeFromMoreChanged(open) }
            .onChange(of: panels[.check]) { _, open in workspace.checkDrawerChanged(open) }
            .onChange(of: panels[.versions]) { _, open in workspace.sourcesChanged(open) }
            .onChange(of: panels.otherPlaceOpen(focused: focusModel.focused)) { _, open in workspace.otherPlaceChanged(open) }
    }

    /// What follows the selection, the player and the fact chosen.
    func following(_ content: some View) -> some View {
        content
            .onChange(of: focusModel.version) { _, version in workspace.focusVersionChanged(version) }
            .onChange(of: insights.chosen) { _, chosen in workspace.factChosen(chosen) }
            .onChange(of: colorScheme) { _, scheme in workspace.appearanceChanged(scheme) }
            .onChange(of: workspace.rollPlace) { _, place in workspace.rollPlaceChanged(place) }
            .onChange(of: player.tick) { _, tick in workspace.playerMoved(tick) }
            .onChange(of: player.state) { old, state in workspace.playerStateChanged(from: old, to: state) }
            .onChange(of: selectionModel.selection) { _, selection in workspace.selectionChanged(selection) }
            .onChange(of: player.loop) { _, loop in workspace.loopChanged(loop) }
            .onChange(of: checkModel.noteEditor) { _, editor in workspace.noteEditorChanged(editor) }
            .onChange(of: focusModel.playRange) { old, new in workspace.whatPlaysChanged(from: old, to: new) }
    }

    /// Piece DNA from More: the map music-core gives for the piece as it is now, the selection as its first idea.
    func openDna(fromMore: Bool) {
        let id = piece.id
        // «Идеи по смыслу» (#582 item 15): the ideas by what they are in the music.
        let request = #"{"ideas":"musical"}"#
        Task {
            let answer = await MusicCore.run { PieceCore(piece: id).send(.dna, request) }
            guard let map = DnaMap(answer: answer) else { return }
            dnaFromMore = fromMore
            dna = map
        }
    }
}
