# 0011 · Sheet music recognition with homr on the player's computer

Status: accepted, 2026-10-25. Andrei chose homr (AGPL-3.0) and a scan server on the player's own computer; cloud hosting is not planned.

## Context

Players often have a piece only as a PDF or on paper. The Stage 1 spec lists PDF and photographed scores as later work, and a file exported by any scanning app already imports as MusicXML. The research of 2026-10-25 (project folder `omr/`, report "Sheet music recognition options") compared engines on a full page of Für Elise: homr 0.7.0 read 98.4% of pitches and 95.2% of pitch plus rhythm, from a clean render and from tilted, blurred photos alike, in 20 to 40 s per page on 4 CPU cores. oemer was slower and much weaker on rhythm; Audiveris could not be tested; general LLMs are far behind and would break the rule that AI never produces notes (0003).

homr is a Python program with about 190 MB of ONNX models, so it cannot run inside the browser app today, and its AGPL licence keeps it out of an App Store build.

## Decision

1. **homr runs unmodified in its own container, on the player's computer** (`services/omr`). A small HTTP service takes one PDF (up to 8 pages, rendered at 300 dpi) or photo, writes the pages into a temporary folder, runs the `homr` command on each, joins the pages into one MusicXML score and deletes the folder. It logs nothing about the music and keeps no copy. The models are baked into the image, so it works offline. The service is AGPL-3.0 like homr; the web app only exchanges files with it over HTTP.
2. **Scanning is off until the player gives the server's address in Settings.** The address is kept in the browser. A page goes only to that address; when it is not this computer, each scan asks first. MIDI and MusicXML files still never leave the browser.
3. **A scan is detected, not imported.** The returned MusicXML goes through the normal importer and is saved as the project's source; the picture is not kept. The project records the engine and the bars the player checked (`ScanOrigin`, project file version 7). On every open, `ScanCheck` marks the notes: detected at 0.4 in bars a check flags, 0.7 elsewhere (homr gives no per-note confidence), and confirmed by the player in checked bars. Hands read from the staves are marked the same way.
4. **Check the scan.** `ScanCheck` flags a measure that runs longer than its time signature, one that runs shorter (other than a pickup or the closing bar), a staff whose notes and rests stop short of the measure's end while another's reach it, notes outside the piano's range, and grace notes. On the Für Elise page these flag 7 of 32 bars, which are where homr's rhythm slips. The workspace opens a scanned piece on the Scan tab: Show selects a flagged bar, Correct notes opens the existing correction log (S1-I7), and Looks right confirms it. Any selected bars can be confirmed too.
5. **The score shows the generated view for a scan.** homr writes the sounding pitch but no printed accidentals or beams, so the file as written would show Für Elise without its sharps.

## Consequences

- Nothing about scanning needs Azure. If a hosted server is wanted later, the same container fits Azure Container Apps in an EU region, with the per-upload consent this decision already has for a server on another computer.
- Analysis and hints are not held back until bars are checked: unchecked notes show as inferred on the roll, and flagged bars sit below the 0.6 confidence hints need.
- Not built yet: a crop of the page next to each flagged bar (homr knows the staff positions but does not write them to MusicXML), and an on-device engine for iPad and iPhone, which waits for a permissively licensed model of homr's quality.
- The CI job runs the service's unit tests with the engine replaced by a stand-in; homr itself is not installed in CI.
