# 0012. The workspace: projects, sessions and ideas; the score first; Focus mode; localization

**Status:** accepted · **Date:** 2026-10-27 · **Scope:** `src/MotiffLab.Web` (pages, layout, localization), `MotiffLab.MusicCore.Projects` (sessions), project file version 8

## Context

A UI/UX review of Stage 1 found the lab usable but shaped by how it was built rather than by how a musician works: the piano roll was the primary view and the engraved score a secondary panel; the inspector carried five tabs of mechanisms (Selection, Edit, Compare, the second file, the tracks) beside the three ways of working; a project held one open piece with no record of what the player sat down to do; what the player kept was a flat list; and nothing let the player stand at the instrument with only the phrase, the loop and the transport on the screen. The review also asked for levels of abstraction above the phrase, for a layering of observations, musical facts and interpretations, for Russian with a proper structure, and for an idea library with branching experiments.

`docs/08-central-product-model.md` already names the frame: the loaded piece is the context for a work session and the phrase is the unit of work. This record decides the structure the UI takes to match it, in the order the review set: the structure of the workspace first, then Focus mode and navigation, with the facts layering, the sentence level, Russian and the library planned behind them.

We looked at how comparable tools shape the same work. Soundslice puts the notation first, keeps the loop and the speed one gesture away and scrolls every view with playback; MuseScore keeps the score as the document and opens the piano roll as a panel on it; Hooktheory colours by harmonic function so the ear and the eye learn together; flowkey, Simply Piano and Yousician give a practice mode that is nothing but the music, the hands, slow and loop, with controls large enough for a tablet on a music stand in landscape. We take the score-first canvas with synchronised views, the loop and speed on the transport, the stripped practice screen with large targets, and the function-level colouring as a later batch, and we leave aside what they do that Stage 1 does not: scoring the player, streaming content, and producing notes.

## Decision

### Projects → sessions → ideas

- A **project** is the piece saved on the device, as before (0009). It now also holds its **sessions** and marks the observations kept during one with the session's id. `Session` (`MotiffLab.MusicCore.Projects`) is an id, an intent (Open, Practise, Understand, Explore), the task in the player's words, the passage last worked on, and when it started and was last active; it is built from values passed in, never a clock, so it stays pure. Ending a session keeps its observations as the project's own. The project file is version 8: a `sessions` array and `sessionId` on observations; a version 7 file opens with no sessions.
- The **project page** (`/project/{id}`) replaces opening a piece straight into the workspace: the piece's bars, sessions and ideas; *What do you want to do?* with the four intents and an optional task; recent sessions to continue or end; and the saved ideas to open. Starting a session opens the workspace on the tab the intent asks for.
- An **idea** is an observation (0009) seen from the library's side: a kept variation, the player's steps, or a note on a phrase, with the session it was kept in. Opening one from the project page brings the workspace back to that passage, with the variation when there is one. The library stays the project's; a session only groups.
- The workspace address names the project and the session (`playback?project=p_…&session=s_…`), so a reload or a link comes back to the same work; `&idea=o_…` opens an idea on arrival and is then dropped from the address.

### The score as the primary workspace

- The canvas shows the score first and the piano roll under it. The header offers **Score**, **Piano roll** or **Both**; with both, a **divider** between them is dragged, or moved with the arrow keys, Home and End (a `role="separator"` with a value), between a fifth and four fifths of the canvas. On a wide screen (from 64rem) and in Focus mode at any width, the canvas fills the window between the header and the transport, measured by `Scripts/layout/workspace-layout.ts`, which also tells the roll to fill its panel (`data-fill`). Below that the canvas stacks and the page scrolls, as before.
- Both views show one selection and one playback position: the roll keeps the selection in view when it moves, and the score scrolls to the first note of the range and follows playback. A click on a bar in the score away from its notes selects the bar, and Shift-click extends to it, as on the roll's bar numbers; a click on a note moves playback there. Verovio's timemap gives the bar onsets (`includeMeasures`).
- The view and the divider are device preferences (`Scripts/storage/preferences.ts`, `localStorage` under `motiff-lab:`), not project data: how a player lays out their screen is theirs, and a project opened on another device starts with the defaults.
- The inspector has four tabs: **Practise**, **Understand** and **Explore** work on the same selection, tempo and loop, and **Tools** holds the mechanisms (the scan check, Keep and the operations, editing and correcting notes, a second file and the tracks). Understand hands off to Explore on the same passage with one button. The phrase strip is one row of phrases to pick, with the boundary and transition tools folded under *Adjust phrases and hear transitions*.

### Focus mode

- **Focus** takes the header, the inspector, the phrase tools and the footer away and leaves the phrase (its chips and the selection text), previous and next phrase, Loop, Original and Variation while a variation exists, Score, Piano roll or Both, the transport, and *Back to exploring*. It asks the browser for the whole screen where it can, and works the same where it cannot (an iPad in Safari). Every control on the Focus bar and the transport is at least 44px, for a tablet on a music stand; on a narrow screen the transport keeps play, stop, the position and the tempo.
- Focus is a state of the page, not a route or a preference: a reload comes back to the full workspace, so nobody opens a project page into a bare screen by surprise.

### Localization

- Interface strings live in `wwwroot/i18n/<language>.json` and are read through `UiText` (`this[key]`, `Format`), which falls back from the chosen language to English to the key, so a missing translation shows English, never a blank. The language is a device preference set on Settings and applied to `document.documentElement.lang`. Russian covers the navigation, the project page, the workspace chrome and Focus in this batch; the musical layer (fact names, operation names, explanations) stays in English for now and is planned as its own batch with the review's form, "Синкопирование (Syncopate)", so the English term stays learnable. The musical layer is written by music-core itself (#134): each sentence a fact, a basis or a place label holds is built in English and in Russian where it is built, chosen by a `Words` value the caller passes (English by default, so the tests and the AI digest read English), so numbers, cases and plurals agree; the UI passes the interface language and shows the text as it comes, never rewriting it. Note names stay letters with the octave ("E5"), as the score and the roll write them; key names use the Russian syllables ("ля минор").
- Strings that come from music-core (fact text, notes, bar numbers) are not translated by the UI; translating them is a music-core concern, so the UI never rewrites analysis output.

## Consequences

- The project file grows by sessions; readers of version 7 files still open. Every existing observation has no session, which the project page shows as kept "with the piece".
- The roll fills whatever its panel leaves it on a filled canvas; given less room than its notes need, it scrolls within itself rather than collapsing, so the keyboard is reachable at any split.
- The e2e specs select bars through the roll as before; a new spec covers the views, the divider, bar selection in the score and Focus. The tabs changed names (Selection and Edit became Tools), so specs that reached them were updated.
- Later batches, as issues: observations, musical facts and interpretations as three layers with *Show in score*; the sentence level above the phrases; Russian for the musical layer; the idea library with experiment branches; comparing the musical function (what changed, what stayed, what was intended, what is perceived).
