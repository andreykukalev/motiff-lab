import PDFKit
import SwiftUI
import WebKit

/// PDF | Notes on the score's screen (#511 section 10): the "Notes" view kept on the device as it goes (to the PDF, the
/// library or the background) and brought back when the player comes back to it from the PDF (`Landing.kept`): the
/// selection, also a bar with no notes, where the score was scrolled to and its zoom, where the music stood and whether
/// Цикл looped it, and the roll's drawer with its zoom and view (ViewStates.swift). Playback stops as the PDF opens, which
/// has its own metronome; nothing starts playing on the way back.
extension ScoreScreen {
    /// The piece's id, by which its views are kept on the device.
    private var viewKey: String? { piece.own?.id }

    /// The "Notes" view as it is now, read before the player stops; where the score and the roll are is read as it is kept.
    func notesViewNow() -> NotesViewState {
        var state = NotesViewState()
        state.selection = score.selection.map { SelectionSpanKept(onset: $0.onset, end: $0.end) }
        state.position = player.tick
        state.loop = score.onePosition ? player.cycle && score.selection != nil : player.loop != nil
        state.rollOpen = rollOpen
        return state
    }

    /// Keeps the "Notes" view on the device, with where the score is scrolled to and the roll's frame; what cannot be read
    /// now (the score's page already gone, the roll never opened) stays as it was kept before.
    func keepNotesView(_ now: NotesViewState) async {
        guard let id = viewKey else { return }
        let before = ViewStates.notes(id)
        var state = now
        if let shown = await score.viewPlace(), let place = shown.place {
            state.place = place
            state.zoom = shown.zoom
            state.left = shown.left
        } else if let before {
            state.place = before.place
            state.zoom = before.zoom
            state.left = before.left
        }
        state.roll = await roll.frame() ?? RollFrameHandoff.waiting(for: piece.id) ?? before?.roll
        ViewStates.keep(state, id)
    }

    /// Back from the PDF: the "Notes" view as the player left it, in place of the page the PDF was on. Nothing plays.
    func returnToNotesView() async {
        guard piece.landing == .kept, let id = viewKey, let kept = ViewStates.notes(id) else { return }
        // The roll takes the frame as it first loads, whenever the drawer opens.
        RollFrameHandoff.give(kept.roll, to: piece.id)
        guard await score.whenRendered() else { return }
        if let span = kept.selection, span.end > span.onset {
            await score.select(request: ScoreController.beatsRequest(span.onset, span.end), step: false)
        }
        if score.onePosition {
            // The model takes the selection now (T9), then Цикл as it was and the position (T7, T12).
            player.hold(selection: score.playRange?.range)
            if player.cycle != kept.loop { player.apply(.cycle) }
            player.go(to: kept.position, play: false)
        } else {
            if kept.loop, let range = score.playRange?.range { player.setLoop(range, play: false) }
            player.seek(to: Player.place(kept.position, inside: player.loop))
        }
        rollOpen = kept.rollOpen
        await score.showPlace(kept.place, zoom: kept.zoom, left: kept.left)
    }

    /// The project file written whenever the app leaves the screen, with the resume point and the "Notes" view.
    func save() {
        guard let file = piece.file else { return }
        let id = piece.id
        let now = resumeNow()
        let view = notesViewNow()
        Task {
            await keepNotesView(view)
            await keepResume(now)
            try? await ProjectFiles.save(piece: id, to: file)
        }
    }

    /// The PDF where the player left it, or the first time at the page the notes at the top of the view were read from (the
    /// web app's `ShowPdfAsync`), once the "Notes" view, the resume point and the piece are saved: the library closes the
    /// piece as the PDF opens (ClosedPieces).
    func showPdf() async {
        guard let switchView else { return }
        let pages = piece.other?.pages ?? PageMap(starts: score.sources.document?.starts ?? [])
        let tick = await score.firstTick(onPage: score.pages.first) ?? 0
        let now = resumeNow()
        let view = notesViewNow()
        player.shutdown()
        await keepNotesView(view)
        await keepResume(now)
        await insights.leave()
        if let file = piece.file { try? await ProjectFiles.save(piece: piece.id, to: file) }
        await switchView(.page(pages.page(of: tick)))
    }
}

/// Where the score's page is scrolled to, as a place in the notes (Web/score.js `placeAt`, `lineAt`): the scroll view's top
/// in the document's points (its offset over its zoom) and back.
extension ScoreController {
    /// Waits for the pages to be drawn, as `arrive(at:)` does; false when they were not within ten seconds.
    func whenRendered() async -> Bool {
        var waited = 0
        while !rendered && waited < 100 {
            try? await Task.sleep(nanoseconds: 100_000_000)
            waited += 1
        }
        return rendered
    }

    /// The place at the top of the view, the page's zoom and how far across it is; nil before the pages are drawn.
    func viewPlace() async -> (place: ViewAnchor?, zoom: Double, left: Double)? {
        guard rendered else { return nil }
        let scroll = webView.scrollView
        let zoom = max(Double(scroll.zoomScale), 0.01)
        let y = Double(scroll.contentOffset.y) / zoom
        let answer = try? await webView.callAsyncJavaScript("return motiff.placeAt(data)", arguments: ["data": ["y": y]], contentWorld: .page)
        return (ViewAnchor(page: answer), zoom, Double(scroll.contentOffset.x) / max(Double(scroll.contentSize.width), 1))
    }

    /// Back at the zoom and the place kept, within what the scroll view reaches.
    func showPlace(_ place: ViewAnchor?, zoom: Double, left: Double) async {
        guard rendered else { return }
        let scroll = webView.scrollView
        let scale = min(max(CGFloat(zoom), scroll.minimumZoomScale), scroll.maximumZoomScale)
        if abs(scroll.zoomScale - scale) > 0.001 { scroll.setZoomScale(scale, animated: false) }
        guard let place,
              let line = try? await webView.callAsyncJavaScript("return motiff.lineAt(data)", arguments: ["data": place.page], contentWorld: .page) as? NSNumber
        else { return }
        let lowest = -scroll.adjustedContentInset.top
        let highest = max(lowest, scroll.contentSize.height + scroll.adjustedContentInset.bottom - scroll.bounds.height)
        let y = min(max(CGFloat(line.doubleValue) * scroll.zoomScale, lowest), highest)
        let x = min(max(CGFloat(left) * scroll.contentSize.width, 0), max(0, scroll.contentSize.width - scroll.bounds.width))
        scroll.setContentOffset(CGPoint(x: x, y: y), animated: false)
    }
}

extension RollController {
    /// The roll's zoom and view now (Web/roll.js `frame`), for the "Notes" view; nil before the roll first drew.
    func frame() async -> [String: Double]? {
        let answer = try? await webView.callAsyncJavaScript("return roll.frame()", contentWorld: .page)
        guard let object = answer as? [String: Any] else { return nil }
        var frame: [String: Double] = [:]
        for key in ["zoom", "rowScale", "scroll", "pitchTop"] {
            guard let value = (object[key] as? NSNumber)?.doubleValue, value.isFinite else { return nil }
            frame[key] = value
        }
        return frame
    }
}

extension PdfController {
    /// Keeps where the pages are on the device (#511 section 10), with the page under the bar; nothing before they are laid out.
    func keepView() {
        guard let projectId, let pages = view as? BarPdfView, let state = pages.viewState() else { return }
        ViewStates.keep(state, projectId)
    }
}
