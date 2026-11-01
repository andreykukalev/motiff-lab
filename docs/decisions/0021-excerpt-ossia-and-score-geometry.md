# 0021 · Excerpts, the ossia and the score's geometry

Status: accepted, 2026-11-01. Redesign package 3, preparation items 10 and 11 of [0018](0018-redesign.md) (sections 2 and 4): the engine work the score-first screens need before they are drawn.

## Context

The redesign shows a phrase in focus with a variation as an **ossia** above it, bar for bar, two variations at once for comparison, and a transcription's **As played** notes above its Reading notation (0018, section 4). The Piece DNA cards show an engraved excerpt of a motif. Until now the engine wrote only whole scores: the "with variation" score replaced the notes in place (0008, 0010). The preparation list asked for a prototype first, because it was not known whether Verovio engraves an ossia from MEI or whether two SVGs would have to be lined up by hand.

Every overlay of the redesign (phrase brackets, the selection band, rings round the notes a fact names, Check flags, bookmarks, margin-note markers, the pencil) is placed from the boxes of the engraved bars, on A4 pages and on the strip alike (0018, section 2; Andrei kept the strip as a switchable view). The only geometry was the pencil layer's, read from the DOM inside the score view.

## Decision

### Excerpts and ossias in one MEI document

`NotationGenerator.Generate` takes `bars` (a `BarRange`) and `ossias` (`NotationOssia`: a label and notes, written for reading or as played).

- **The prototype.** Verovio 6 engraves an extra staff group with `staffDef/@scale="75%"` and a `label` above a braced grand staff, bar for bar, in one system: the ossia's bars line up with the original's because they are the same measures, and its staff space is three quarters of the original's (checked in Node with the vendored Verovio and in the geometry tests). MEI's own `<ossia>` element is not needed, and two SVGs lined up by hand would break whenever a system breaks differently. So: **one document, the ossia as staves of the same measures.**
- **An excerpt** writes only the bars asked for, keeping their numbers and the key and meter in force, but plans every note of the piece first, so voices, ties into and out of the excerpt and accidentals are exactly those of the whole score. A pedal or hairpin that runs past the last bar ends there; a slur to a note outside it is left out.
- **The ossia's staves come first** and show only what has notes in the bars written: a variation of the melody is one small staff above the grand staff. Outside the variation's passage the ossia rests. Its note ids carry the prefix `ossia-` (`ossia2-` for a second), so a note shown twice has two ids and the timeline, highlights and geometry tell them apart. The counts of `GeneratedNotation` stay the original's; `Ossias` names each ossia's label, staves and notes.
- **Sources in the facade** (`notation`, still contract 3 because only fields were added, [0020](0020-music-core-facade.md)): `bars` {first, last} or `"selection"`, and `ossia` as `variation` (the notes of the variation heard now, in the passage it changed), `asPlayed` (a transcription written as played above its Reading notation, with the playing's marks on the ossia's own staves) or an idea's id (its kept variation, labelled with the idea's name); a list of two compares them. Labels are in the player's language. The dashed plum frame and the label chip of the design kit are drawn by the app over the ossia's staves, from the geometry below.

### One geometry module for both layouts

`Scripts/score/score-geometry.ts` reads each engraved page from **Verovio's SVG text**, not from the DOM: systems (`g.system`), bars (`g.measure`, boxed by their staves' lines), staves with their line spacing, and noteheads (`g.note` > `g.notehead` > `use`, sized from the staff space). Everything is in page units, the page's view box. A `ScoreGeometry` then places the pages where the view shows them (an offset and a scale per page) and puts every bar in time through the timeline's measure onsets. From it come:

- the bar-to-page map (the same as the PDF's on A4 pages, 0017) and the page of any tick;
- `runs` of a passage, one per system it touches, with its first and last run marked: what a bracket (`bracket`, 14 px above the top staff), the selection band (`band`, 8 px round the staves, or only some staves, such as the original under an ossia), a bookmark or the focused phrase draws;
- `ring` round a notehead (8 px wider, as the kit's fact highlights), `flag` at a bar's top right for Check flags and markers;
- `barFrames` for the pencil layer, which the score view now takes from here.

A4 pages and the strip go through the same code: the strip is pages that run on without a gap. Because the module works on SVG text and plain numbers, Node tests it against real Verovio output, and the iPad app, which shows the same SVG in a web view (0018, preparation item 22), can place its overlays the same way.

## Consequences

- The redesign's phrase focus asks for `notation` with `bars: "selection"` and `ossia: "variation"` and draws the ossia frame and chips from `ScoreGeometry.runs` restricted to the ossia's staves; Compare asks for two ossias; Piece DNA cards ask for excerpts.
- The old UI keeps its own overlays (phrase marks, fact boxes) placed from the DOM; they move onto the geometry module with the redesign's score, screen by screen.
- Noteheads are sized from the staff space and SMuFL's usual widths, not measured glyph by glyph: close enough for rings and hit tests, not for typesetting.
- Reading the SVG text costs one parse per page per layout; the result is cached until the next layout.
