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

## Later steps

1. Marks and bookmarks on the PDF's pages, kept in page coordinates and separate from the Notes view's marks; the metronome.
2. Recognise: the scan reads the kept PDF, the notes become the piece's `source`, and the bar switches between the "PDF" and "Notes" views, with a rough page-to-bar map from the scan's pages.
3. The iPad app's PDF view (PDFKit) over the same project file.
