# 0024 · Assembling the player's own version from a piece's sources

Status: accepted, 2026-11-07. Andrei approved the concept «Рабочая среда версий» (9.5/10, frozen) and the mockups «Сборка своей версии» (2026-11-07), and gave the go to build them. Supersedes the bar-by-bar merge draft of [0022](0022-score-and-performance.md), section 5.

## Context

The merge draft of 0022 decided bar by bar between the player's notes and one version lined up with them. Real covers and recordings differ in structure: extra or missing bars, another middle, half a bar more here and there. Bar numbers and forced synchronisation were the wrong start, and the draft's controls were hard to follow. Andrei's frame: a player has two or more versions of a piece and assembles a third, their own, by ear; when two versions nearly match, the app does it for them.

## Decision

### 1. Sources and the player's notes

Every source is independent: its own notes, bars and bar numbers, tempo, key, playhead and selection. The piece's notes are one source; each other version (`SavedPerformance`) is another, read in its own time from its MIDI (`SourceMusic.OfVersion`). The player's notes, called «Ноты» in the interface (the word «Итог» of the concept is never shown), are a source too, and the only one operations change. Before the first apply they start equal to the piece's notes; the notes before the assembly are «Как было» and, after the first apply, «Ноты до сборки».

### 2. The journal

The player's notes are rebuilt from a base by a journal of steps (`AssemblyStep`), deterministically (`Assembled.Build`): a carry (`CarryStep`: a fragment of a source put in place of a place in the notes, or inserted), a note change (`NoteStep`: removed, or moved by semitones) and a transposition (`TransposeStep`). Each has a number («перенос №3») and a time.

Steps hold on to notes, not times: a place is a note's onset plus an offset (`TimeAnchor`), so material put in or taken out earlier never moves what a later step meant. A carried note's id is its source note's id behind the step's number (`c3.n_0042`), the same on every rebuild, and the rebuild knows which step put each note in (`Assembled.Origins`).

A step whose note is gone (its carry was returned, its source removed) is not applied, and its outcome names what it lost. Returning one step («Вернуть что было», `AssemblyWork.PlanRevert` and `Revert`) takes with it every step that leans on it (a change of a note it put in, a carry held by its notes), lists them before, and leaves the independent ones. ⌘Z and ⇧⌘Z step through the journal's history in memory; after the piece is opened again, ⌘Z takes the last step off.

### 3. Bars at a seam

A carry splices bars (`AssemblyBars.Splice`): the bars before it stay, the source's bars come in as they are, and the bars after it move with the music by the difference in length, so their downbeats stay downbeats. Where the fragment starts or ends inside a bar, the cut bars are short; two short bars meeting at a seam become one bar, of the signature whose length they make, or of a signature of their own («2/8») when no bar is that long. A short bar is written with its own time signature (`AssemblyBars.ToMeter`). Seams go on the 32nd grid notation is written on.

### 4. Matching, in words

A source is compared with the player's notes as they are now (`AssemblyMatch`), bar by bar of the source: the alignment of 0022 between the two, then for each bar how many of its notes are found in place. A bar «совпадает» when at least 85 % of its notes are found, or all but one of at least half; «частично» from 50 %; «отличается» below; «не найдено» when nothing lines up with it. The card says it in bars: «Совпадает в тактах 1–29 и 42–64. В тактах 30–41 другой материал. На 8 тактов длиннее.» «Найти похожее» uses the same alignment.

«Принять совпавшее» carries, bar for bar, every bar that matches but is not identical and lands on a bar of the notes of the same length; bars that only partly match or differ are left to the player. A comparison is stamped with the journal it was made for, and is never acted on when the journal has changed since.

### 5. The carry sheet

«В ноты…» opens a sheet that says what the carry will do before it is done, in the same six lines every time (`CarrySheet`): where (replace the selected notes or insert at the playhead), length (and what moves), seam (short bars, notes cut at the seam), key (with a transposition offered when the key signatures differ), meter and tempo, and notes (how many go in and out, and where those that go out came from). The ghost is the notes as they will be.

### 6. In the interface

The score editor stays the main experience. With one source there is no trace of the assembly; «Добавить другую версию…» in More is the one way in. After a version is added, one quiet card offers «Сравнить», «Послушать версию» and «Позже», and «+1 версия» stays by the title. Comparing adds a chip per source to the playback pill («Ноты», each version, and «Как было» after the first carry), selection from note to note, a card at the selection, the carry sheet, «Рядом», «Происхождение» and «Сопоставление»; it folds away when the work is done.

### 7. One rule changes the piece

Only «Сделать нотами» changes the piece's notes (`Project.ApplyAssembly`). The notes the journal builds become the piece's notes (`PieceAssembly.Applied`, read over the imported piece by `PieceAssembly.Over`: its notes, bars, keys and tempo; the score is engraved from the notes from then on). The notes before the first apply are kept (`Before`) and stay the journal's base, so a carry can be returned after applying and the player can start again from them. Corrections made since the last apply stay where they still fit. Motifs whose notes the apply changed leave the list. Project file version 15 (`assembly`, [project-file.md](../project-file.md)).

### 8. Free and Pro

Everything here runs on the device and is free. Hearing a recording's notes (the source) stays Pro, as in 0022.

### 9. A recording as a source

A recording added as a version (0022) is a source like the others, read in its own time: the notes heard in it are written in the bars worked out from them, with a tempo that follows the playing and a key that fits them ([0016](0016-bars-for-transcribed-notes.md)), marked heard (`SourceMusic.OfVersion`). Each note's place in those bars is paired with the time it was heard at (`RecordingClock`), so a place between notes lies between their times.

While the recording is shown, its own sound plays in place of its notes, at its own speed whatever the tempo, from where the playhead is heard in it: the playback engine reads the recording's samples through the clock, so loops, seeks and the playhead stay in the notes' time. A strip over the pill shows the waveform around the selection or the playhead, the selection's stretch and the playhead on it; moving along it moves playing (mockups 06 and 13c). The card says when the selection is heard («0:41–0:45») and that the recording itself plays.

The sound is kept on the device only, as a mono WAV in a store of its own under the project and the version, never in the project file, so it never goes online with the project; deleting the project deletes it. On another device, or for a version added before, the strip says the sound is not here and offers to pick the file again; until then the notes play on the piano.

The carry sheet from a recording starts with «Как получить ноты» (mockup 07): keep the notes and add what the performer plays otherwise (`CarryStep.Keep`, the default when the carry replaces), or take the notes as heard (an ordinary carry). A carry that keeps adds only the fragment's notes no note of the place sounds, fitted into the place, which keeps its length and bars. «Запись» in the sheet's footer plays the fragment as the recording sounds; «Призрак» goes back to the notes.

## Consequences

- Built in steps: the engine and the project file; the web app (scenario 1, two different versions); «Рядом», «Сопоставление» and «Принять совпавшее» (scenario 3); a recording as a source (scenario 2); the iPad app through the facade.
- The merge draft of 0022, section 5 (the version's page with three starts, Compare versions, the Mine / Version / Result switch) leaves the interface once the web app's step is in. Bars taken by it in older files still load as part of the notes.
