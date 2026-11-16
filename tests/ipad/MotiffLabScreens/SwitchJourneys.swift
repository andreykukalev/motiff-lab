import XCTest

/// The journeys between a piece's views (architecture blueprint 6.1, layer T4): PDF | Notes and back, Sources, and a PDF
/// piece of its own. The switch's speed and memory over 40 switches are SwitchUITests' (VIEW-02), run by hand.
extension Walk {
    /// Where the bar's start and the switch are on one screen (VIEW-04).
    struct BarPlaces {
        let title: String
        let titleFrame: CGRect
        let back: CGRect
        let contents: CGRect
        let pdf: CGRect
        let notes: CGRect
    }

    /// The switch in the bar: PDF, and Notes («Ноты, Рабочие» for the working notes).
    var pdfSwitch: XCUIElement { button("PDF") }
    var notesSwitch: XCUIElement { button(beginning: "Ноты") }

    /// VIEW-01, VIEW-04: from the notes to the PDF and back. The bar reads Back, the Contents, the title, PDF | Notes,
    /// Sources, then the view's own actions with More last, in both views, Back, the Contents and the title in the same
    /// places; while the notes open no old control (the bar's Play or Pause, the old Loop) appears, and the pill's Play is
    /// there once they have. The bar is looked at a few times a second: a flash shorter than a look is not seen (VIEW-01's
    /// «not even for a moment» is the device's to see).
    func pdfAndNotes() throws {
        let onNotes = try barPlaces("on the notes")
        try press(pdfSwitch, "PDF in the switch")
        try until("the PDF shown", timeout: 30) { $0.screen == "pdf" && $0.ready }
        shot("44-switch-pdf", settle: 1)
        let onPdf = try barPlaces("on the PDF")
        let moved: [(String, CGRect, CGRect)] = [("Back", onNotes.back, onPdf.back), ("Contents", onNotes.contents, onPdf.contents),
                                                 ("the title", onNotes.titleFrame, onPdf.titleFrame)]
            + (onNotes.title == onPdf.title ? [("PDF", onNotes.pdf, onPdf.pdf), ("Notes", onNotes.notes, onPdf.notes)] : [])
        for (name, notes, pdf) in moved where abs(notes.minX - pdf.minX) > 2 {
            throw miss("\(name) moved in the bar with the view: \(Int(notes.minX)) on the notes, \(Int(pdf.minX)) on the PDF (VIEW-04)")
        }
        try press(notesSwitch, "Notes in the switch")
        try watchNotesOpen()
        try need(button(beginning: "Играть с"), "the pill's Play on the notes")
        shot("45-switch-notes", settle: 1)
    }

    /// The bar's start, the switch, Sources and More in that order on one line (VIEW-04).
    private func barPlaces(_ view: String) throws -> BarPlaces {
        let title = try need(self.title, "the title in the bar \(view)")
        let row: [(String, CGRect)] = try [
            ("Back", need(button("Библиотека"), "Back in the bar \(view)").frame),
            ("Contents", need(button("Оглавление"), "the Contents in the bar \(view)").frame),
            ("the title", title.frame),
            ("PDF", need(pdfSwitch, "PDF in the switch \(view)").frame),
            ("Notes", need(notesSwitch, "Notes in the switch \(view)").frame),
            ("Sources", need(button("Источники"), "Sources in the bar \(view)").frame),
            ("More", need(more, "More in the bar \(view)").frame),
        ]
        let line = row.map { "\($0.0) \(Int($0.1.minX))" }.joined(separator: ", ")
        guard row.allSatisfy({ abs($0.1.midY - row[0].1.midY) < 8 }) else { throw miss("the bar is not one line \(view) (VIEW-04): \(line)") }
        for (left, right) in zip(row, row.dropFirst()) where left.1.minX >= right.1.minX {
            throw miss("\(left.0) is not before \(right.0) in the bar \(view) (VIEW-04): \(line)")
        }
        print("SCREEN-BAR \(tag) \(view): \(line)")
        return BarPlaces(title: title.label, titleFrame: row[2].1, back: row[0].1, contents: row[1].1, pdf: row[3].1, notes: row[4].1)
    }

    /// While the notes open after PDF, the bar is looked at again and again: none of the old model's controls (the bar's
    /// Play, its Pause, the old Loop, as v0.6.4 spec 13 saw them flash) may show, until the probe says the notes are ready.
    private func watchNotesOpen() throws {
        let old = app.buttons.matching(NSPredicate(format: "label IN %@", ["Играть", "Пауза", "Цикл"]))
        let start = Date()
        var looks = 0
        while Date().timeIntervalSince(start) < 30 {
            looks += 1
            if old.count > 0 { throw miss("an old control, «\(old.firstMatch.label)», showed while the notes opened (VIEW-01)") }
            if let probe = probe(), probe.screen == "score", probe.ready {
                guard probe["oldbar"] == "0", old.count == 0 else { throw WalkMiss(what: "the old Play was drawn on the notes (VIEW-01)", probe: probe.line) }
                print("SCREEN-VIEW01 \(tag): \(looks) looks while the notes opened, no old control")
                return
            }
        }
        throw miss("the notes did not open in 30 s after Notes in the switch")
    }

    /// Sources from the bar lists the piece's originals; Open on the PDF's row shows the PDF, and Notes comes back.
    func sourcesAndAView() throws {
        try press(button("Источники"), "Sources in the bar")
        try need(button("Закрыть другие версии"), "the Sources sheet")
        try need(app.staticTexts.matching(NSPredicate(format: "label BEGINSWITH 'Оригиналы ·'")).firstMatch, "the originals in Sources")
        shot("51-sources-pdf", settle: 1)
        try press(button("Открыть"), "Open on the PDF's row in Sources")
        try until("the PDF opened from Sources", timeout: 30) { $0.screen == "pdf" && $0.ready }
        try press(notesSwitch, "Notes in the switch")
        try until("back on the notes", timeout: 30) { $0.screen == "score" && $0.ready }
        let close = button("Закрыть другие версии")
        if close.exists && close.isHittable { close.tap() }
    }

    /// A PDF piece (0023): its pages, the pen, the metronome and More.
    func pdfPiece() throws {
        try need(button("Метроном"), "the metronome in the PDF's bar")
        shot("40-pdf-piece", settle: 2)
        try press(button("Перо"), "the Pen on the PDF")
        try need(button("Убрать карандаш"), "the PDF's pen palette")
        stroke(from: CGVector(dx: 0.3, dy: 0.4), to: CGVector(dx: 0.5, dy: 0.42))
        shot("41-pdf-pen")
        try press(button("Убрать карандаш"), "Put the pen away on the PDF")
        try press(more, "More on the PDF")
        try need(app.otherElements["PopoverDismissRegion"], "the PDF's More")
        shot("43-pdf-more")
        dismissPopover()
        // Last: in the runs so far the metronome's pill did not show in the simulator (its sound may not start there).
        try press(button("Метроном"), "the metronome in the PDF's bar")
        try need(button("Убрать метроном"), "the metronome's pill")
        shot("42-pdf-metronome", settle: 1)
        try press(button("Убрать метроном"), "Close the metronome")
    }
}
