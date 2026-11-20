# Golden project files

What today's reader and writer (`src/backend/MotiffLab.MusicCore/Projects/ProjectFile.cs` and `Projects/File/`, project file version 17) do with
files of earlier, current and later versions (task #293; basis rules S6 and S7 and pre-build checks 1 and 2 of the
project-continuity council, 2026-11-08), with the tolerant reader for enum values (#313). The tests are
`ProjectFileGoldenTests.cs` and `AnalysisParityTests.cs` in this folder.

## The files

| File | Version | Where it came from |
| --- | --- | --- |
| `v12-paterlini-all-my-joy.motiff.json` | 12 | A project Andrei made in the app from a transcription (transkun 2.0.1): one motif over bar 1, two resume points, no bars worked out. |
| `v12-einaudi-santiago.motiff.json` | 12 | A project Andrei made from a transcription, with the bars and the beat-by-beat tempo worked out for it (0016), one resume point, no motifs. |
| `v12-*.written.motiff.json` | 17 | What today's writer makes of each version 12 file. |
| `v15-worked.motiff.json` | 15 | Written by today's writer from a project built in code on the minuet fixture (`Import/MusicXml/Fixtures/importer-grand-staff.musicxml`), so that it holds every kind of entity a note piece can keep, see below. |
| `v15-originals.motiff.json`, `v15-originals-online.motiff.json` | 15 | Written by version 0.3 (`Projects/PieceOriginalTests.cs`): a PDF read by the scan server with a recording version, with the originals' descriptions, fingerprints, the web's holding marks and the `from`/`at` links, as kept on the device and as sent online (no PDF). Version 0.2's reader, built from the tag `v0.2`, opened both, opened their notes and wrote them back (2026-11-12, see "Version 0.2 reads version 0.3's files"). |
| `v15-photos-parts.motiff.json` | 15 | Written by version 0.4 (`Projects/PhotosAndPartsFormatTests.cs`): a waltz made from pages 6–7 of a photographed songbook, its two photos as a PDF (`kind: images`), read page by page with the scan server's report on each (`scan.pages`), the dim second page's bars flagged `PageQuality`, `partOf` naming the songbook's original, and a stroke in the left margin (`margin`). Versions 0.2 and 0.3, built from their tags, opened it, opened its notes and wrote it back (2026-11-12, see "Versions 0.2 and 0.3 read version 0.4's files"). |
| `v15-edits-meters.motiff.json` | 15 | Written by version 0.5 (`Projects/EditsAndMetersFormatTests.cs`): a synthetic recording heard (four bars of 4/4, then four of 7/8, generated in code) with its bars changing signature (`transcription.bars.meters`), where the stretch heard started in the recording file (`transcription.sentFromMs`), one correction and the corrections turned off (`editsOff`). |
| `v15-linked-pdf.motiff.json`, `v15-folder-notes.motiff.json` | 15 | Written by version 0.5 (`Projects/LinkedOriginalsFormatTests.cs`, «Импорт папки»): a PDF piece whose PDF stays in the player's folder (`kept: link`, `folder`, held by the iPad, no notes and no PDF in the file, with a title, composer, tags and a playlist), and a MusicXML score imported from a folder (`folder` on `src_file`) with its PDF linked beside it. See "Versions 0.2 to 0.4 read version 0.5's linked originals". |
| `v15-pdf-online.motiff.json` | 15 | Written by version 0.5 (`Projects/PdfPiecesOnlineTests.cs`): the online copy of a piece that is only a PDF (0026 C3 amended 2026-11-12): no notes and no PDF, its title, composer, tags and playlist, the PDF's description with its fingerprint held by the iPad, and a bookmark on its page 2. Versions 0.2 to 0.4 refuse it as damaged (see below). |
| `v16-future.motiff.json` | 16 | Made while version 15 was today's: the version 15 file with the version raised to 16, one unknown top-level field (`future`), one unknown field in an entity (`motifs[0].future`) and the value `FutureValue` in one place for each of the 24 enum types the reader names (council fact V28). A test checks that it is exactly that. Now that version 16 is today's, it is a file of this version in the layout of version 15, and damaged. |
| `v16-worked.motiff.json` | 16 | What versions 0.6 to 1.0 made of `v15-worked.motiff.json`, an input since version 17: the same project in the three layers of 0030. Laid out at the top level again (`Projects/LegacyFile.cs`), it is the version 15 file, field for field and in its order, but for the originals' descriptions version 16 writes for every original. |
| `v16-edits-meters`, `v16-photos-parts`, `v16-linked-pdf`, `v16-folder-notes`, `v16-originals`, `v16-originals-online`, `v16-pdf-online` (`.motiff.json`) | 16 | What versions 0.6 to 1.0 made of the project of the `v15-` file of the same name; inputs since version 17. |
| `v16-sources.motiff.json` | 16 | Written by version 0.6 (`Projects/OriginalOperationsTests.cs`, until v1.1 `SourceOperationsTests.cs`): a PDF read by the scan server, a second PDF added beside it with its bytes (`src_pdf_2`), read again into other Notes that were made the working ones (`n_2`), the notes until then kept as other Notes with their correction and `fileName` (`n_1`), hidden from the switch, and the first PDF removed: its tombstone with its page bookmark. |
| `v16-variants.motiff.json` | 16 | Written by version 0.6 (`Assistant/VariantRecordTests.cs`): the first twelve bars of Für Elise as a MIDI piece, two variants of bars 5–8 kept as ideas with their records (`provenance`, "op": "variation": the chip, the seed, the parent, one bar added, the notes under each as they were), the second taken into work as a carry from `variant:o_0002` and made notes, with the notes before the assembly. |
| `v17-future.motiff.json` | 17 | Made while version 16 was today's: the version 16 file with the version raised to 17, unknown fields at the top level (`future`), in the workspace (`workspace.future`) and in an entity (`workspace.motifs[0].future`), and the value `FutureValue` in the same 24 places in the layout of version 16. A test checks that it is exactly that. Now that version 17 is today's, it is a file of this version, damaged by its values on the notes that sound. |
| `v17-worked.motiff.json`, `v17-edits-meters`, `v17-photos-parts`, `v17-linked-pdf`, `v17-folder-notes`, `v17-originals`, `v17-originals-online`, `v17-pdf-online`, `v17-sources`, `v17-variants` (`.motiff.json`) | 17 | What today's writer makes of the project of the `v16-` file of the same name (and of its `v15-` file, where there is one): the `v16-` file with the version raised to 17 and nothing else. |
| `v17-recording.motiff.json` | 17 | Written by version 1.1 (`Projects/RecordingPieceFormatTests.cs`, finding 29): a piece that is only a recording added as it is, kept on the iPad, with no notes. Raised to version 16 it is damaged. |
| `v17-drafts.motiff.json` | 17 | Written by version 1.1 (`Projects/DraftsFormatTests.cs`, ADR 0034): a MIDI piece built in code with two drafts: places of the notes with a variant heard in a recording (its stretch, an anchor), of another piece's melody and a bridge with a pencil note, a left hand on the shelf, and a step of each kind. A draft with an unknown value is kept whole as unknown. |
| `v18-future.motiff.json` | 18 | The version 17 file with the version raised to 18 and the same unknown fields and values as `v17-future.motiff.json`. A test checks that it is exactly that. |

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

- **Version 12**: both files read, open (the motif still matches its notes) and are written as version 17; laid out at
  the top level again, only the version, an empty `performances` and the originals' descriptions differ.
- **Version 15**: the file reads and is written as `v17-worked.motiff.json`, which laid out at the top level again is
  the version 15 file. **Version 16**: `v16-worked.motiff.json` reads and is written as the version 17 file, with only
  the version changed. **Version 17**: that file reads, opens (3 motifs, 11 variations) and is written back unchanged.
- **The future file is refused** today, before any field is read: "This project was saved by a newer version of
  Motiff Lab." (council fact V3). Read-only opening, a later task, is meant to flip it.
- **One unknown enum value** in a version 16 or 17 file (#313, flipping council fact V3): for 21 of the 24 enum types the file
  opens, the entity holding the value is kept as unknown and the file is written back unchanged. For PerformanceSource,
  MergeChoice and Provenance, and for Hand and KeyMode on a note correction, a compared Notes or the assembly, the file
  is still "damaged": those change the notes that sound (Andrei's decision on #313, option A). A version 16 file is
  written back as the same file with version 17. In a version 15 file any unknown value is damage: the builds that
  wrote one knew no value this one does not.
- **The future file with this version**: with version 17 and the three values on the notes that sound put back, it
  opens and is written back whole, its unknown fields included.
- **An unknown field** in a version 17 file is kept at the top level, in the workspace, in a Notes, an original and a
  provenance, and written back; one inside a known entity (a motif) is still lost (0026 S6). One in a version 15 file
  is lost, as before.
- **Phrases, sentences, form and Piece DNA**: the facade's answers for the design kit's piece (`design/kit/elise.musicxml`)
  and the example piece of both apps (`src/shared/Playback/FurElise.musicxml`), in English and Russian, equal
  the golden in `tests/ipad/MotiffLabTests/Golden`. The iPad tests (`AnalysisParityTests.swift`) assert the same golden
  from music-core built with NativeAOT.

## Version 0.2 reads version 0.3's files

The originals of version 0.3 are kept within version 15 because a build refuses any file of a later version, so a
version 16 file would lock version 0.2 of both apps out while they sync with a version 0.3 build. To check it again,
take the music-core of the tag and read the files with it:

```bash
mkdir -p /tmp/v02 && git archive v0.2 src/backend/MotiffLab.MusicCore Directory.Build.props global.json | tar -x -C /tmp/v02
# a console project in /tmp/v02/reader referencing ../src/MotiffLab.MusicCore that, for each file, calls
# ProjectFile.Read, OpenedProject.Open, ProjectFile.Write, ProjectFile.Read again and ForOnline
dotnet run --project /tmp/v02/reader -- tests/backend/Unit/MotiffLab.MusicCore.Tests/Golden/v15-originals*.motiff.json
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

## Versions 0.2 and 0.4 read version 0.5's files

The additions of version 0.5 (`editsOff`, `transcription.bars.meters`, `transcription.sentFromMs`) are kept within version 15 for the same reason.
Checked the same way on 2026-11-12, with music-core of `v0.2` and of `v0.4` reading `v15-edits-meters.motiff.json`:
both read it, open its notes with the correction on (75 of the 76 heard), lay 8 bars of 4/4 from the same starts, write
it and read it again with the same notes. Both drop `editsOff`, `transcription.bars.meters` and `transcription.sentFromMs` on writing.

## Versions 0.2 to 0.4 read version 0.5's linked originals

Checked the same way on 2026-11-12, with music-core of `v0.2`, `v0.3` and `v0.4` reading `v15-linked-pdf.motiff.json` and
`v15-folder-notes.motiff.json` (a reader that calls `ProjectFile.Read`, `OpenedProject.Open`, `ProjectFile.Write`,
`ProjectFile.Read` again and `ForOnline`):

| Build | `v15-folder-notes` | `v15-linked-pdf` |
| --- | --- | --- |
| `v0.2` | reads it, opens its 53 notes, writes it and reads it again with the same notes; drops `originals` | refused as damaged ("its source cannot be read"): no notes and no PDF |
| `v0.3`, `v0.4` | the same, keeping the linked PDF's description whole as unknown (its `kept` is unknown to them) and dropping `folder` from `src_file` | refused as damaged, the same |

The same builds refuse `v15-pdf-online.motiff.json` as damaged ("its source cannot be read"), for the same reason: no
notes and no PDF.

## Changing a golden

The input files (`v12-*.motiff.json` without `.written`, every `v15-*.motiff.json` and `v16-*.motiff.json`,
`v17-future.motiff.json`, `v18-future.motiff.json`) are never rewritten: they are what the builds of their time wrote, or the future as it was
pictured then. When the format changes on purpose, run the tests with `MOTIFF_UPDATE_SNAPSHOTS=1` to write the
`*.written.motiff.json` files, the `v17-*` goldens and the analysis goldens again, and review the difference; when the
writer moves past version 17, the `v17-` files become inputs and the writer's answers get files of their own, as the
`v16-` files did when it moved to version 17.
