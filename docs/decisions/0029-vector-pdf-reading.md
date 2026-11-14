# 0029 · Reading a born-digital PDF on the device, and «Авто» choosing for each page

Status: accepted, 2026-11-14 (Andrei, thread «Добавить выбор движка распознавания сканов»: «заводи задачу для реализации движков распознавания на основе сравнения», then «да» to the plan of a vector reader and a choice made for each page). Shipped behind the experiment «Выбор движка распознавания» as its third choice, «Авто». Builds on [0011](0011-sheet-music-recognition.md) and [0014](0014-hosted-scan-server.md) (the scan server) and [0023](0023-pdf-pieces.md) (PDF pieces); the scan server and its engines (homr, copisteria) are unchanged.

Research behind it (project folder): `product/research/omr-alternatives-2026-11-14/report.md`.

## Context

Most sheet music players add is a PDF a notation program printed: MuseScore, LilyPond, Sibelius, Finale, Dorico. Such a PDF holds no picture of the music: every notehead, clef, accidental and digit of a time signature is a glyph of a music font, and every staff line, stem, bar line, beam and slur a vector path. The scan server renders it to an image and guesses it back with a model, losing what is printed exactly (homr reads no time signature at all, and both engines misread rhythms a page in ten). The comparison found that reading the glyphs and lines directly is exact on such pages and needs no server, no model and no network.

## Decision

### 1. A vector reader in music-core

`MusicCore/Import/Vector` reads each page of a PDF with PdfPig (Apache-2.0, pure .NET, no native code), so it runs in the browser's WebAssembly and in the iPad's NativeAOT framework alike. It finds the staves and systems from the lines, the symbols from the music font's glyphs (SMuFL fonts such as Bravura and Leland, MuseScore 2's MScore, LilyPond's Emmentaler by glyph name), and from them the chords, lengths, beams, flags, dots, ties, tuplets, grace notes, clefs and clef changes, key signatures, printed time signatures and octave lines. The bars are timed per staff and per voice and written as MusicXML in the scan server's shape (one part, a staff per printed staff, 480 divisions), so a page read here is joined, checked and imported exactly as a scanned page (`ScanJoin`, `ScanCheck`). It is pure and deterministic, and its name and version ("vector 1") are the engine named in the piece's provenance; its notes are marked detected, as any scan's.

### 2. Each page is checked, and a page that fails goes to the server

A page goes to the scan server instead when it is an image, prints its music in a font the reader does not know (Petrucci, Opus, Maestro and other legacy fonts), has systems of different staff counts, leaves out more than 2% of its noteheads, or has more than 10% of bars whose staves disagree or whose length differs from the meter (a pickup, its completing bar and an irregular bar every staff agrees on are allowed). A page with no staves and no music is left out. The page's report is written in the server's `X-Page-Report` shape (drawn, so nothing is turned or faint), so Check counts its doubtful bars as for a scan.

### 3. «Авто», the third engine choice

In the experiment's choice of engine, «Авто» reads the PDF on the device first (`scan.vector`, a facade call within contract 12): the pages it reads are kept, the others go to the scan server one a request with the server's own engine, and all are joined in page order; the engines are named together ("vector 1 + homr 0.7.1"). A PDF the reader cannot open goes to the server as before. «Авто» is never named to the server. Photos always go to the server. On the web the reader runs in the app (`Pdf/ScoreScan.cs`), on the iPad through the facade (`VectorPages.swift`).

### 4. Nothing more leaves the device

A page read on the device is not sent anywhere, which narrows what 0011 lets leave the device; the pages that do go, go only to the server the player set, as before.

## Consequences

- A born-digital score is read exactly and at once, with its meters, ties and tuplets, and needs no server for the pages read.
- Known limits of version 1: a key change of a single accidental inside a system is missed; a page left out as blank makes the page map share the bars evenly between pages; a 7-tuplet's notes are rounded to whole ticks; «Авто» still asks for a scan server to be set, even for a PDF read wholly on the device.
- Next: more music fonts (Petrucci, Opus), and «Авто» with no server and for free, as on-device work is (0025).
