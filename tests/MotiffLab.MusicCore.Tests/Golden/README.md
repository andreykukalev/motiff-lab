# Golden project files

What today's reader and writer (`src/MotiffLab.MusicCore/Projects/ProjectFile*.cs`, project file version 15) do with
files of earlier, current and later versions (task #293; basis rules S6 and S7 and pre-build checks 1 and 2 of the
project-continuity council, 2026-11-08), with the tolerant reader for enum values (#313). The tests are
`ProjectFileGoldenTests.cs` and `AnalysisParityTests.cs` in this folder.

## The files

| File | Version | Where it came from |
| --- | --- | --- |
| `v12-paterlini-all-my-joy.motiff.json` | 12 | A project Andrei made in the app from a transcription (transkun 2.0.1): one motif over bar 1, two resume points, no bars worked out. |
| `v12-einaudi-santiago.motiff.json` | 12 | A project Andrei made from a transcription, with the bars and the beat-by-beat tempo worked out for it (0016), one resume point, no motifs. |
| `v12-*.written.motiff.json` | 15 | What today's writer makes of each version 12 file. |
| `v15-worked.motiff.json` | 15 | Written by today's writer from a project built in code on the minuet fixture (`Import/MusicXml/Fixtures/importer-grand-staff.musicxml`), so that it holds every kind of entity a note piece can keep, see below. |
| `v15-originals.motiff.json`, `v15-originals-online.motiff.json` | 15 | Written by version 0.3 (`Projects/PieceOriginalTests.cs`): a PDF read by the scan server with a recording version, with the originals' descriptions, fingerprints, the web's holding marks and the `from`/`at` links, as kept on the device and as sent online (no PDF). Version 0.2's reader, built from the tag `v0.2`, opened both, opened their notes and wrote them back (2026-11-12, see "Version 0.2 reads version 0.3's files"). |
| `v15-photos-parts.motiff.json` | 15 | Written by version 0.4 (`Projects/PhotosAndPartsFormatTests.cs`): a waltz made from pages 6–7 of a photographed songbook, its two photos as a PDF (`kind: images`), read page by page with the scan server's report on each (`scan.pages`), the dim second page's bars flagged `PageQuality`, `partOf` naming the songbook's original, and a stroke in the left margin (`margin`). Versions 0.2 and 0.3, built from their tags, opened it, opened its notes and wrote it back (2026-11-12, see "Versions 0.2 and 0.3 read version 0.4's files"). |
| `v16-future.motiff.json` | 16 | The version 15 file with the version raised to 16, one unknown top-level field (`future`), one unknown field in an entity (`motifs[0].future`) and the value `FutureValue` in one place for each of the 24 enum types the reader names (council fact V28). A test checks that it is exactly that. |

**Missing: versions 13 and 14.** No project files of those versions were supplied (Andrei has only version 12 files),
so there is no golden for them. A file of either version can be added here with the same test as the version 12 ones.

### The MIDI in the version 12 files is synthetic

Both projects were transcribed from recordings of copyrighted pieces, and this repository is public. Andrei decided
(2026-11-08) to keep every other field and replace the base64 MIDI in `source` with a synthetic one: a I–V–vi–IV
pattern in C major at 120 BPM in 4/4, three notes every four beats, in the original's ticks per quarter, from the
original's first note to its end, so that the motif over bar 1 and the transcription's bars still fit. Nothing of the
original's notes is kept. `synthetic_midi.py` made it and documents it; run on a golden file it changes nothing.

The file names (`fileName`) still name the pieces the projects were made from, as the app wrote them.

### What the version 15 file holds

Three motifs (a range in one hand, a range in both hands, picked notes; the three motif modes); eleven variations, one
for each of rhythm reshape (with Keep choices and a pending step), articulation, ornament (with a key in a minor
mode), accent shift, passing notes (with intervals), accompaniment pattern, density and rests, bass pattern, voicing
(with a scope), reharmonise and a hand edit (pending); an idea with the fact it came from; a note the player added;
ink in both tools, a margin note, a highlight, a fingering; a bookmark and a category of the player's own; a resume
point with a passage, overlays, hands and a loop; two Check flags; a settings override; a performance with dismissed
and taken bars and a merge record; an assembly journal with a carry, a note step and a transpose step, applied, with
the notes before it.

Not in it: a PDF (`document`, `documentMarks`, `documentPages`, `readFrom`), a scan and a transcription, which are other
kinds of piece; the Einaudi file holds a transcription.

## What the tests record

- **Version 12**: both files read, open (the motif still matches its notes) and are written as version 15 with only the
  version and an empty `performances` changed.
- **Version 15**: the file reads, opens (3 motifs, 11 variations) and is written back unchanged.
- **The future file is refused** today, before any field is read: "This project was saved by a newer version of
  Motiff Lab." (council fact V3). Read-only opening, a later task, is meant to flip it.
- **One unknown enum value** in a version 15 file (#313, flipping council fact V3): for 21 of the 24 enum types the file
  opens, the entity holding the value is kept as unknown and the file is written back unchanged. For PerformanceSource,
  MergeChoice and Provenance, and for Hand and KeyMode on a note correction, a performance or the assembly, the file is
  still "damaged": those change the notes that sound (Andrei's decision on #313, option A).
- **The enum part of the future file**: with version 15 and the three values on the notes that sound put back, the
  future file opens and is written back without only its unknown top-level field.
- **An unknown field** in a version 15 file is read and then lost: the file written back is the version 15 file
  without it (council fact V2, confirmed by running).
- **Phrases, sentences, form and Piece DNA**: the facade's answers for the design kit's piece (`design/kit/elise.musicxml`)
  and the example piece of both apps (`src/MotiffLab.Web/Playback/FurElise.musicxml`), in English and Russian, equal
  the golden in `ipad/App/MotiffLabTests/Golden`. The iPad tests (`AnalysisParityTests.swift`) assert the same golden
  from music-core built with NativeAOT.

## Version 0.2 reads version 0.3's files

The originals of version 0.3 are kept within version 15 because a build refuses any file of a later version, so a
version 16 file would lock version 0.2 of both apps out while they sync with a version 0.3 build. To check it again,
take the music-core of the tag and read the files with it:

```bash
mkdir -p /tmp/v02 && git archive v0.2 src/MotiffLab.MusicCore Directory.Build.props global.json | tar -x -C /tmp/v02
# a console project in /tmp/v02/reader referencing ../src/MotiffLab.MusicCore that, for each file, calls
# ProjectFile.Read, OpenedProject.Open, ProjectFile.Write, ProjectFile.Read again and ForOnline
dotnet run --project /tmp/v02/reader -- tests/MotiffLab.MusicCore.Tests/Golden/v15-originals*.motiff.json
```

## Versions 0.2 and 0.3 read version 0.4's files

The additions of version 0.4 (`partOf`, `scan.pages`, the Check reason `PageQuality`, `margin` on a stroke) are kept
within version 15 for the same reason. Checked the same way on 2026-11-12, with music-core of `v0.2` and of `v0.3`
reading `v15-photos-parts.motiff.json`: both read it, open its 248 notes, write it and read it again with the same
notes, and make its online copy. The 16 `PageQuality` flags are kept whole as unknown flags and written back (the
`check` list keeps its 30 flags). What each drops on writing:

| Build | Dropped |
| --- | --- |
| `v0.2` | `partOf`, `marks.ink[0].margin`, `scan.pages`, and, as for every 0.3 file, `originals`, `scan.from`, `scan.at` |
| `v0.3` | `partOf`, `marks.ink[0].margin`, `scan.pages` |

## Changing a golden

The input files (`v12-*.motiff.json` without `.written`, `v15-worked.motiff.json`, `v16-future.motiff.json`) are never
rewritten: they are what the builds of their time wrote. When the format changes on purpose, run the tests with
`MOTIFF_UPDATE_SNAPSHOTS=1` to write the `*.written.motiff.json` files and the analysis goldens again, and review the
difference; when the writer moves past version 15, give the version 15 file a `.written` golden of its own.
