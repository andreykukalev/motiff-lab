# 0023 · PDF pieces, read as they are

Status: accepted, 2026-11-05. Andrei, 2026-11-04 ("new positioning: forScore + MuseScore with subtle AI"): a plain PDF is added to the library and shown as it is, with no interactivity, alongside a separate "Notes" view once it is recognised; each view keeps its own marks, and only a rough link from pages to bars is needed.

## Context

Until now a PDF could only be scanned (0011, 0014): the notes the scan server read became the piece, and the PDF itself was not kept. Players who read from printed scores want the page exactly as printed (fingerings, editor's marks, the engraver's layout), whether or not the notes are ever recognised, and they want it to cost nothing: reading a PDF on the device is free; recognition runs on the server and is Pro (0018).

## Decision

- **The PDF is kept with the piece, byte for byte.** `Project.Document` holds it and the project file writes it as `document` (base64, version 13, `docs/project-file.md`). A PDF piece has an empty `source`: `Project.HasNotes` is false and `OpenedProject.Open` refuses it, so nothing in the engine mistakes it for notes. `PdfDocument` (music-core) checks the `%PDF-` header and the size: up to 50 MB, since the file lives inside the project file in IndexedDB and passes through JS interop as one string.
- **It stays on the device.** A PDF piece is never queued for saving online: an online copy holds at most 2 MB, and the free tier has no cloud account. The PDF view says so in its overflow.
- **The web app reads it with pdf.js**, vendored in `wwwroot/lib/pdfjs` (Apache-2.0; `tools/pdfjs/README.md` has the version, checksums and update steps) and loaded only when a PDF opens, like Verovio. The service worker caches it on first use, so a PDF opens offline after that. Only the Foxit standard fonts ship (their own licence); the Liberation fonts (GPL) and the QuickJS sandbox are left out, and pdf.js runs with `isEvalSupported: false`.
- **The PDF view is the score's paper, not a new screen.** `/pdf?project=…` shows the pages as sheets on the desk in one continuously scrolling document (0018 section 2), at each page's own proportions, one column in portrait and two to a row in landscape. Pages are drawn as they come near the view and let go when far from it. The bar is the score's: back to the library, the title, "p. 2 of 5", a PDF tag, and the overflow, with Download the PDF (the bytes as added). There is no transport, roll, Insights or selection: nothing on the page is music the app knows.
- **Where the player was is kept on the device.** The page under the bar is remembered per piece and reopened; Page 1 in the library opens at the top. A PDF piece has no resume points, Piece DNA or ideas until it has notes.
- **The library treats it as a piece.** Open a file accepts `.pdf` next to MIDI and MusicXML; the card shows "PDF · 3 pages" with page 1 as its cover, drawn by pdf.js.

- **Marks on the pages are the PDF view's own.** The pen writes on the pages with the score's pencil layer and palette (four inks, the highlighter, the eraser, undo, clear): each page is one frame its strokes are kept against, by its number, in units of a hundred and twentieth of the page's width, so a stroke keeps its place and size however wide the page is drawn. The bookmark in the bar marks the page under it (a carmine tab on its edge); the title opens the Contents, which lists the bookmarked pages to go to, name or remove. Both are kept in the project as `documentMarks` (`DocumentMarks` in music-core), apart from the notes' `marks`.

- **A metronome of its own.** The PDF has no notes to play, so the view has a metronome in a pill at the bottom, like the score's transport: start and stop, slower and faster by 4, the tempo tapped in time on its number, 2, 3, 4 or 6 beats to the bar (the first clicks higher) and a dot for each beat. It uses the playback engine's click and schedules it on the audio clock (`Scripts/metronome/metronome.ts`). It runs on the device and is free; the tempo and beats are kept per piece on the device.

- **Read the notes from the kept PDF.** The PDF view's overflow offers Read the notes (Pro on Motiff Lab's server, free on the player's own, as scanning is, 0011, 0014): the panel says where the PDF goes, with the same switch as Scan printed music, and sends the kept PDF as it is. The MusicXML the scan server returns becomes the piece's `source` (`Project.Recognised`), its doubtful bars become Check flags, and the "Notes" view opens with the Check drawer. The PDF, its marks and bookmarks stay as they were.
- **Two views, one piece.** A recognised PDF has PDF | Notes in the bar of both views, the score's segmented control. The link between them is rough by design: the scan server marks the first measure of each page after the first with `<print new-page="yes"/>` (services/omr `merge.py`), music-core keeps where each page starts (`PageMap`, `documentPages` in the project file) and shares the bars evenly when there are no marks. From the PDF, the Notes view opens at the score page holding the bar the page under the bar starts with; from the notes, the PDF opens at the page the first bar of the score page under the bar was read from. The library opens a recognised piece in its Notes view, at its resume point.

## Later steps

1. The iPad app's PDF view (PDFKit) over the same project file.
