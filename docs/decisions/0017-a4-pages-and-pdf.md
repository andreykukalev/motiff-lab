# 0017 · A4 pages and a PDF of the score

Status: accepted, 2026-10-31. Andrei: the app now makes scores (from MusicXML, MIDI, recordings and scans), so they should be laid out as A4 pages and exported as PDF.

> 2026-11-01: the redesign ([0018](0018-redesign.md)) keeps A4 pages as the default and the PDF, and lays the pages out in one continuously scrolling document, two per row in landscape, with the kit's margins and scale.

## Context

The score view (0008) lays the notation out as one strip the width of the view: no title, no pages, and systems that reflow whenever the window changes. That suits following playback in a short panel, but a score made from a recording or a scan is also something to print, put on the music stand or keep, and players expect it to look like a printed page. The export has to work in the iPad app (0015), a WKWebView that does nothing on `window.print()` and saves files only through downloads, and offline like the rest of the PWA.

## Options considered

| Option | Verdict |
| --- | --- |
| The browser's print dialog with an `@page { size: A4 }` style | Rejected as the export. Exact, but it is a dialog, not a file: "Save as PDF" is a step players must know, and the iPad app ignores `window.print()` unless the Swift shell adds a print bridge |
| jsPDF with svg2pdf.js (MIT) | Rejected. About 450 KB more to download, and its standard fonts cannot write Cyrillic titles or Verovio's music font in text (tempo marks): both would need font files embedded in the app |
| Every page as a 300 dpi image in a PDF | Rejected as the whole answer. Simple and exact, but soft when zoomed, and several times larger |
| **Our own small PDF writer: Verovio's SVG as vector paths, its text drawn by the browser over them** | **Chosen.** Verovio draws with a small part of SVG (paths, polygons, polylines, ellipses, glyphs placed with `<use>`, groups with transforms), which maps directly to PDF paths. The few texts on a page (title, page numbers, tempo words, bar and tuplet numbers) are drawn by the browser, which has the fonts for any script, at 300 dpi as black ink through a soft mask. No new library, and nothing to download first |

## Decision

1. **A4 pages are the score view's default layout.** Verovio lays the score out on a page of 210 × 297 mm (2100 × 2970 units at scale 100, which are tenths of a millimetre), margins of 15 mm at the sides, 12 mm at the top and 15 mm at the bottom, and staves 7.2 mm high, Verovio's default and the size of printed piano music. Systems break where they fill the width and spread to fill each page but the last. The first page is headed by the piece's title, the others by their page number (Verovio's automatic header; its footer and logo are left out). The view shows each page as a sheet scaled to the panel's width, up to about the size of a printed page, on a darker desk. "Strip" brings back the layout of 0008; the choice is a device preference (`ml-score-layout`), not part of a project.
2. **The title is the piece's name.** Generated notation already carries a title in its MEI; the web app now gives it the file name without the extensions of music files (`ScoreTitle`), so "Take 1 at the piano.mid" is headed "Take 1 at the piano". A MusicXML file shown as written keeps its own title or credits; one with neither gets the piece's name as its `movement-title`.
3. **Download PDF writes what the view shows, on A4 pages, whatever the layout on screen.** A second Verovio toolkit engraves the same document with the A4 options, so the PDF has the same page breaks as the A4 view and the view keeps its ids. For each page, `svg-to-pdf.ts` turns the SVG's shapes into PDF path operators in vector form, and the browser draws the same page with only its text (`textOnlySvg`) on a canvas at 300 dpi; the text's coverage, cut to the part of the page that has text, becomes a soft mask over black. `pdf-document.ts` writes the pages (PDF 1.4, A4 media box, streams compressed with `CompressionStream` where the platform has it) with the title in the document information. The file, named after the title, is handed over like the project and MIDI exports, so the iPad app saves it through Files.
4. **Only what is engraved is exported.** Playback marks, selection, phrase marks, highlights and pencil marks are the view's overlays and are not part of the PDF.

## Consequences

- A score from a recording or a scan reads as a printed page and prints as one, with the notation as vectors; a page of notation is about 30 to 40 KB.
- Text in the PDF is an image: it prints sharply but cannot be selected or searched.
- The A4 layout is the same in the view and the PDF; in a short panel the player scrolls through a page, and "Strip" fits more music across a wide window.
- Pencil marks could be added to the PDF later by placing them against the A4 bars, as the view does against its own.
- Node tests cover the SVG conversion, the PDF's structure and Verovio's A4 pages; a browser test downloads the PDF in Chromium, Firefox and WebKit, desktop and tablet.
