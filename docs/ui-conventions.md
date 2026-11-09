# UI conventions

Rules for the web UI (`src/MotiffLab.Web`) as it is on main after the redesign (decision record 0018), so a fix in one corner reads like the rest. The native iPad app (0019) follows these rules in SwiftUI where it has the same function; which functions it has, and in which form, follows 0025 (two workspaces, no parity). Where this file and `design/kit` disagree, the kit's documents win (`design/kit/HANDOFF.md` says which source wins), and this file is corrected in the same PR.

## What the UI may do

- The UI only displays what music-core returns. It never changes analysis or variation code, or the shapes of their results, and it never works out a musical fact itself: no grouping, no key, no chord, no place in the score, no boundary. A short line for a fact is music-core's too (`MotifFact.Brief`, shown through `MotifFact.Claim`).
- Never build an experiment, a variation step, an observation or a session in the UI. Ask music-core (`ExperimentCatalogue`, `VariationChain`, `Project.Keep`, `NoteEditLog`) and draw what comes back.
- Anything inferred is marked inferred, by more than colour (a dashed line, a tag, words). Every fact keeps its certainty and its Evidence.
- Everything works with AI off. What the assistant adds is Pro; on the device is free; nothing names a server to a player except the privacy lines that say where a file goes.

## Design system

- Every colour, size, radius, shadow, font and timing is a token from `design/kit/tokens.css` (the web app serves a copy in `wwwroot/css`); a canvas reads them with `Scripts/layout/tokens.ts`. Components follow `design/kit/ds.css`. Do not port the mockup HTML (`s*.html`); port rules, tokens, icons and the Verovio options.
- One accent: cobalt (`--accent`) is pointing and selection only; plum is what the app composed and Pro; amber is what the player marked. The player's inks (carmine, graphite, ochre) do not change with the theme.
- Icons come from `wwwroot/img/icons.svg`, drawn in Razor with `<Icon Name="…" />`; `node tools/icons/build.mjs` writes the set for both apps.
- Switches use the kit's switch (`input[type=checkbox].switch` with `role="switch"`); overlines are uppercase; the piece's name is the serif title.
- A control a finger uses is at least 44px (`--h-touch`); a compact control is `--h-compact`. Text meets WCAG 2.2 AA contrast; the axe checks in `tests/e2e` run on every screen they reach.

## The score page

- The score is the whole screen (`Pages/PlaybackLab.razor`, `Score/ScoreView.razor`): one continuously scrolling document of A4 pages (0017; Verovio options in `Scripts/score/score-page.ts`, the same on the iPad) or, chosen in Settings · Score, a strip. Nothing is docked; there are no page jumps.
- The bar floats at the top: back to the library, the piece's title (it opens the Contents), the page count, and the actions Phrases, Pen, Piano roll, Insights, Play and More. It fades while the player reads and comes back on a tap (`Scripts/layout/bar-fade.ts`).
- More holds only what changes the whole score: which score shows (Reading | As played, with About this notation), a second file and what is heard from it, downloads. The layout (pages or strip) is in Settings · Score.
- The pill (the transport) appears with a selection: play, loop, tempo (the dial icon) and hands, each popover anchored to its button. A long press on the pill, or on an action of the bar, opens that element's own settings (`Scripts/layout/long-press.ts`); only a held press swallows the click its release makes.
- Drawers and sheets rise over the score: the piano roll from the bottom (resized by its handle, `Scripts/layout/drawer-resize.ts`), Insights, the Contents, the Check sheet and a version's sheet. An element over the score that the selection menu must keep clear of carries `data-menu-clear`.
- The piano roll (`Scripts/roll/piano-roll.ts`, testing items 11–13): rows of 22 px at 100 %, a note's name without its octave inside it where it fits, the octave named once in a band down the left, the hand told by colour and pattern alone. One zoom for the time and the rows floats over the corner of the notes (− the percentage + and Fit, no panel); a pinch and Ctrl and the wheel zoom too. Fit lays the selection, or the phrase in view, across the roll. Expand on its head takes the phrase into Focus as a roll; Focus switches Notes | Piano roll in its bar.
- Overlays on the score (brackets, bands, rings, bookmarks, pencil marks, check flags) are placed by `Scripts/score/score-geometry.ts` from the rendered `g.measure` and `g.staff` boxes, on pages and on the strip alike, and stored per bar, never per pixel.

## Phrases and the selection

- The score is the selection surface; there is no phrase list or strip. Phrases are brackets above the staves in three levels (short phrases, phrases, sentences; the level in the Phrases popover, for this piece or everywhere). An inferred bracket is dashed. A bracket across a system break draws a run on each system.
- A tap on a bracket selects the phrase; a second tap lifts it into Focus. A tap on a bar, or a drag on the piano roll, selects bars; Shift extends. A tap on the paper where there is no music puts the selection away. Escape does too.
- Page Up and Page Down step to the previous and next phrase anywhere on the page, except in a field (`Scripts/phrases/phrase-keys.ts`); page-turner pedals send these keys. Space plays and pauses.
- The selection menu (`Scripts/score/selection-layer.ts`) floats over the selection: Roll, Bookmark, Note, Highlight, Focus, and More for the selection (Ask first, then making and merging phrases). Under 700px it shows icons only. It sits under the drawers and sheets and moves aside for any element marked `data-menu-clear`.
- The headings say where the player is in one line: "Phrase 1 · bars 0–4", "Nothing selected". What was restored when a piece opens is said once, not kept in a heading.

## Insights

- One stream (`Insights/InsightsStream.razor`), no Understand / Explore / Build tabs. It reads the workspace state (`Workspace/WorkspaceState.cs`), which the page builds for each render and cascades. Put state the stream needs into the snapshot, never into a new parameter or into the component.
- From the top: the lens chips (All and one per lens, one row that scrolls sideways), the ideas kept for the passage as plum chips with Manage, the one thing to hear first (`FactRanking.KeyObservation`), then the facts by lens.
- A fact is a short statement that points at the notes: its claim, its certainty, how many places, Evidence (the full listing when there is a brief, then the basis), and one quiet Try button. The stream leads with Main (`FactRanking.Main`, testing item 14): the harmonic motion and what it gives (`harmony.motion`), then the texture (`character.texture`), then the strongest fact of another lens, three at most. Every other fact sits under Details, folded by lens with its count (harmony and character first, then structure, line, rhythm and key), a tap opening a lens in `FactRanking.InStream` order; filtering to a lens opens it; the lens holding the chosen fact or the trial opens by itself.
- A claim with places is a toggle: it rings its notes on the score and lists its places as chips; a chip rings only its place and moves playback to it.
- Try opens the two or three ways music-core offers for the fact; a way sounds at once. Its card (`Explore/ExploreCard.razor`) leads with the sound: the title, the passage, the ways, what changed, Try another and Leave it. The idea, its question, what it keeps, what to listen for and the facts it rests on are under About this idea. Keep and Adjust follow the Try button on the row that holds the trial.
- The assistant's input closes the stream; it asks about the selection or the whole piece.

## Compare and ideas

- While a variation exists the inspector head has the compare switch (`Compare/CompareSwitch.razor`): Original | Variation and One after the other. A running loop keeps looping with the new choice; the engine switches on the exact frame (0007) and the score follows the one sounding.
- Compare (`Compare/ComparePanel.razor`) says what changed (from `Variation.Changes`) and what was kept (from the Keep checks); nothing is computed again in the UI.
- Keep this variation keeps it as an idea (`Observation`), with a note in the player's own words that nothing reads, scores or sends. Ideas are listed, opened, renamed, exported and deleted through `Project`; Delete asks first. Für Elise has no project, so what is kept lasts while the page is open, and the card says so.

## Marks

- The pen writes on the score (`Scripts/score/pencil-layer.ts`): a pen or the mouse with the pen on, never a finger, which scrolls. Marks are kept against their bar and are the player's writing, never music: no analysis reads them.
- A bookmark is a bracket in its category's ink above the phrase brackets, its title in italic serif; across a system break each run keeps its hooks and repeats the title after "…", and a long title ends in an ellipsis. The Contents lists bookmarks by category, the detected form and the pages.
- Layers (under Pen) show or hide the player's marks by kind.

## The library and the screens around it

- The library (`Pages/Home.razor`) shows the pieces as cards, each resumed where it was left; Add a piece opens one sheet with the routes in (Open a file, Scan a page, From a recording, the demo). The Pro routes say what to do in the player's words ("Sign in or get Pro").
- A screen a link leads to outside the score (opening a file, scanning a page, notes from a recording, the account, a page not found) is a sheet over the library (`Design/PageSheet.razor`): its title, a close back to the library, the paper dimmed behind.
- Settings is a sheet (`Settings/SettingsSheet.razor`) with its sections as tabs: Guidance, Score, Layers, Piano roll, Appearance and Account (with the scan server's address only in a build without Motiff Lab's own). A popover's settings name where they apply: this piece or everywhere.
- A link to a place on another screen (`settings#account`) lands there: the layout scrolls to the fragment.

## Localization

- Interface strings live in `wwwroot/i18n/en.json` and `ru.json`, read through `UiText` (`Text["key"]`, `Text.Format`, `Text.Count` with `.one`, `.few`, `.many`). Add every new string to both files; `tests/web/i18n.test.mjs` checks that every key used exists in both.
- Musical text (facts, briefs, bases, place labels) comes from music-core in the language the page passes (`Words.For(Text.Language)`); the UI never translates or rewrites it. Terms shared across screens live under `term.*` (0013 section 7).

## General

- Say it in one line: an empty state is one line and one action; a heading is one line; explanations open on request (Evidence, About this idea, About this notation).
- Collapsible sections the app keeps open across renders use a `<button aria-expanded>`; a disclosure that only reads uses `<details>`.
- Saving online goes only through `ProjectStore`; pages never call the API themselves, and anything about accounts shows only when `CloudAccount.IsAvailable`.
- Device preferences (`Projects/Preferences.cs`, `Settings/DeviceSettings.cs`) are never project data, and project data is never a device preference.
