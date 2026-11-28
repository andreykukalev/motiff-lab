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
    /// places, across and up and down, and the two strips of «Пюпитр» (v1.7 e-2) where they are on the notes; while the
    /// notes open no old control (the bar's Play or Pause, the old Loop) appears, and the pill's Play is there once they
    /// have. The bar is looked at a few times a second: a flash shorter than a look is not seen (VIEW-01's
    /// «not even for a moment» is the device's to see).
    func pdfAndNotes() throws {
        let onNotes = try barPlaces("on the notes")
        let stripsOnNotes = try strips("on the notes")
        try press(pdfSwitch, "PDF in the switch")
        try until("the PDF shown", timeout: 30) { $0.screen == "pdf" && $0.ready }
        shot("44-switch-pdf", settle: 1)
        let onPdf = try barPlaces("on the PDF")
        let moved: [(String, CGRect, CGRect)] = [("Back", onNotes.back, onPdf.back), ("Contents", onNotes.contents, onPdf.contents),
                                                 ("the title", onNotes.titleFrame, onPdf.titleFrame)]
            + (onNotes.title == onPdf.title ? [("PDF", onNotes.pdf, onPdf.pdf), ("Notes", onNotes.notes, onPdf.notes)] : [])
        for (name, notes, pdf) in moved where abs(notes.minX - pdf.minX) > 2 || abs(notes.minY - pdf.minY) > 2 {
            throw miss("\(name) moved in the bar with the view: at \(Int(notes.minX)), \(Int(notes.minY)) on the notes, \(Int(pdf.minX)), \(Int(pdf.minY)) on the PDF (VIEW-04)")
        }
        let stripsOnPdf = try strips("on the PDF")
        guard abs(stripsOnNotes.top - stripsOnPdf.top) <= 1, abs(stripsOnNotes.shelf - stripsOnPdf.shelf) <= 1 else {
            throw miss("the strips moved with the view: the top strip to \(Int(stripsOnNotes.top)), the shelf from \(Int(stripsOnNotes.shelf)) on the notes, "
                + "\(Int(stripsOnPdf.top)) and \(Int(stripsOnPdf.shelf)) on the PDF (VIEW-04)")
        }
        try press(notesSwitch, "Notes in the switch")
        try watchNotesOpen()
        try need(button(beginning: "Играть с"), "the pill's Play on the notes")
        shot("45-switch-notes", settle: 1)
    }

    /// Where the top strip ends and the bottom shelf starts («Пюпитр», spec score-reading §3.1): the page's room between them.
    private func strips(_ view: String) throws -> (top: CGFloat, shelf: CGFloat) {
        let top = try need(app.otherElements["ml-top-strip"], "the top strip \(view)").frame
        let shelf = try need(app.otherElements["ml-bottom-shelf"], "the bottom shelf \(view)").frame
        print("SCREEN-STRIPS \(tag) \(view): top strip to \(Int(top.maxY)), shelf from \(Int(shelf.minY))")
        return (top.maxY, shelf.minY)
    }

    /// The bar's start, the switch, Sources and More in that order on one line (VIEW-04), each measured once it stands still.
    private func barPlaces(_ view: String) throws -> BarPlaces {
        let title = self.title
        let row: [(String, CGRect)] = try [
            ("Back", settled(button("Библиотека"), "Back in the bar \(view)")),
            ("Contents", settled(button("Оглавление"), "the Contents in the bar \(view)")),
            ("the title", settled(title, "the title in the bar \(view)")),
            ("PDF", settled(pdfSwitch, "PDF in the switch \(view)")),
            ("Notes", settled(notesSwitch, "Notes in the switch \(view)")),
            ("Sources", settled(button("Источники"), "Sources in the bar \(view)")),
            ("More", settled(more, "More in the bar \(view)")),
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
                guard old.count == 0 else { throw WalkMiss(what: "an old control showed on the notes once they were ready (VIEW-01)", probe: probe.line) }
                print("SCREEN-VIEW01 \(tag): \(looks) looks while the notes opened, no old control")
                return
            }
        }
        throw miss("the notes did not open in 30 s after Notes in the switch")
    }

    /// Sources from the bar lists the piece's originals, each row with no «Открыть» (v0.10, Andrei's finding 25: a view is
    /// picked in the switch); the sheet closes and the notes stay.
    func sourcesSheet() throws {
        try press(button("Источники"), "Sources in the bar")
        try need(button("Закрыть другие версии"), "the Sources sheet")
        try need(app.staticTexts.matching(NSPredicate(format: "label BEGINSWITH 'Оригиналы ·'")).firstMatch, "the originals in Sources")
        shot("51-sources-pdf", settle: 1)
        if button("Открыть").exists { throw miss("a row in Sources still has «Открыть»") }
        try press(button("Закрыть другие версии"), "Close Sources")
        try until("still on the notes") { $0.screen == "score" && $0.ready }
    }

    /// A PDF piece (0023): its pages, the pen, More and the metronome.
    func pdfPiece() throws {
        try need(button("Метроном"), "the metronome on the PDF's shelf")
        shot("40-pdf-piece", settle: 2)
        try press(button("Перо"), "the Pen on the PDF")
        try settled(button("Убрать карандаш"), "the PDF's pen palette")
        stroke(from: CGVector(dx: 0.3, dy: 0.4), to: CGVector(dx: 0.5, dy: 0.42))
        shot("41-pdf-pen")
        try press(button("Убрать карандаш"), "Put the pen away on the PDF")
        try press(more, "More on the PDF")
        try need(app.otherElements["PopoverDismissRegion"], "the PDF's More")
        shot("43-pdf-more")
        try dismissPopover()
        // A known issue, not a pass: strict (the day the pill shows, the walk fails saying the expected failure did not
        // come) and narrow (only the pill's own assertion is expected to fail).
        try press(button("Метроном"), "the metronome on the PDF's shelf")
        let pill = button("Убрать метроном")
        let options = XCTExpectedFailure.Options()
        options.isStrict = true
        options.issueMatcher = { $0.compactDescription.contains("the metronome's pill is not there") }
        XCTExpectFailure("the simulator does not start the metronome's sound, so Metronome.start stops it; to be checked on an iPad",
                         options: options) {
            XCTAssertTrue(pill.waitForExistence(timeout: 3), "the metronome's pill is not there")
        }
        if pill.exists {
            shot("42-pdf-metronome", settle: 1)
            try press(pill, "Close the metronome")
        } else {
            print("SCREEN-QUESTION metronome \(tag): no pill after the PDF shelf's Метроном in the simulator; to be checked on an iPad")
        }
    }
}
