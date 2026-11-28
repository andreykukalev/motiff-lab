# 0036 · Reading the score: the working mode «Пюпитр» with two docked strips, a concert mode, and the «Вид страницы» menu

Status: accepted, 2026-11-28. Andrei decided the two modes on 2026-11-26 (23:11 UTC) and answered the proposal page's questions «1а–8а» (23:15 UTC, «Согласен со всеми вопросами»), thread «Предложить редизайн «как партитура» для 2.0». Built in version 1.7 «Пюпитр» (#871). Supersedes the parts of [0018](0018-redesign.md) named in the last section and amends [0017](0017-a4-pages-and-pdf.md) and [0023](0023-pdf-pieces.md); [0025](0025-two-studios.md) stands.

Sources (project folder): the spec `product/specs/score-reading.md` (every behaviour in full, and how to check it on the iPad), the research `product/research/score-reading-2026-11-27/` (`paper-practice.md`, paper scores at the piano; `pianist-voices.md`, score apps on the iPad), the proposal page with its mockups (https://claude.ai/artifact/A3QZc63tzXUm3JSC4ngf5K, version 3; where a mockup and the spec differ, the text is right) and the version plan `product/versions/v1.7-plan.md` (items and the lead's decisions; saved format: see the version plan).

## Context

0018 kept the page free by keeping everything else off it: the score is the whole screen, nothing is docked, the bar fades and the transport pill appears only when needed. On the iPad it went the other way. Under the playback model (v0.5.2) the pill always shows; it is rounded and translucent and lies on the bottom of the score, so some notes under it show and some do not. To get the notes back the app grew «Вид для практики» and «Управление на листе», two switches that hide the selection controls and the playback controls, and «Скрыть» in the selection menu. Andrei at the piano: the pill covers the bottom of the score, and the interface feels loaded and fragile. What Andrei asks for: the feeling of reading a score, not of working in a studio.

The research on paper practice and on pianists' use of score apps gave seven rules, against which every decision below is checked:

1. Nothing lies on the notes and nothing pops up by itself.
2. What comes next is visible: the end of a page and the start of the next are seen together.
3. Every bar keeps its place: the layout does not change by itself, the page does not move while the hands play, a turn is instant.
4. Notes as large as possible.
5. While playing, no more than one large touch target is needed.
6. The playing tools sit at an edge, near the hands, but off the page.
7. Quiet: no glass or translucency over the notes, nothing blinks.

Weighed by 0025's six questions: it serves the player reading and playing at the instrument (the Practice Studio's main purpose, 0025 section 3); it changes where the tools are, not what any of them does; the value is notes that are never covered and a page that turns like paper; the risk is the space the strips take, which paging and «Ноты крупнее» give back.

## Decision

This is a reorganisation of the score screen, not new functions: what each existing control does, and how, does not change.

### 1. Two modes

| | Working mode «Пюпитр» | Concert mode |
| --- | --- | --- |
| For | practice: everything the app can do | a concert or rehearsal that needs only a score manager |
| On the screen | two strips at the edges and the notes between them | only the notes and the player's marks |
| A touch on the notes | as now (the place, the selection, the pen as its mode says) | only turns pages |
| In and out | the normal state | in from «Вид страницы»; out by holding a finger in a corner |

Both modes work on the Notes and PDF views. The recording view (the waveform only) gets the strips of «Пюпитр» as well; it has no concert mode, since it has no notes.

### 2. The working mode «Пюпитр»

- **The top strip** runs the full width along the top edge: opaque (`--surface`), a hairline below (`--hairline`), no shadow, radius or blur, its height from the tokens (`--h-touch` and its padding). **The bottom strip** («полочка») is the same along the bottom edge, with the hairline above.
- **The page takes everything between the strips.** No strip, in any state, lies on the page; scrolled notes pass under a strip's edge as under the screen's, and do not show through it. In portrait one A4 page fits whole between the strips (about 94 % of today's width, the whole page in view); in landscape two pages side by side, whole in height.
- **What goes where** (taken from today's top bar and pill; no control's logic changes). Top strip, left to right: Back, Contents, the piece's title, the view switch (PDF | Notes) and Sources, the pages in view («с. 1–2 из 4»); on the right the modes (Navigate, Select, Pen), ⓘ and its list, the circle of fifths, Insights, **«Вид страницы»** (new), More. Bottom strip: on the left Play with its place, with a selection Цикл with its span and the phrase steps ‹ ›; in the centre tempo, hands, metronome; on the right the roll and the assistant. When space runs short each strip follows today's rules (`TopBar`: the view note goes first, then the pages, then the buttons turn compact; `Pill`: full, compact with «⋯», tight). Touch targets stay at least 44 pt.
- **What the player opens stays over the page, as now**: the menu over the selection, the cards (note, bookmark), the buttons' popovers, the panels on the right (Insights, the assistant, Check, Sources, Settings) and the «Развернуть» card. They sit between the strips: their top no higher than the top strip's lower edge, their bottom no lower than the bottom strip's upper edge. The roll's drawer opens above the bottom strip, and the page moves so the selection stays in view above it, as now.
- **Removed, not hidden:** «Управление на листе» with its two switches (the selection controls, the playback controls), «Вид для практики» with its temporary «Играть / Выйти из практики», «Скрыть» in the selection menu, and their saved settings (`CLAUDE.md` «Writing code», rule 2). In «Пюпитр» there is nothing to hide; clean notes are the concert mode (answer 5а). No «Прятать при игре» either: the strips are always there.

### 3. The «Вид страницы» menu

One button in the top strip (the kit's `scroll` icon, a page with arrows) opens a popover. Its choices are kept on this iPad for every piece, not in the project file and not online, except «Ноты крупнее», which is the piece's own and saved with it.

- **«По страницам»** is the default (answer 1а). The page stands still; a swipe left or a tap in the zone at the right edge shows the next page, a swipe right or a tap at the left edge the previous one (zones the full height between the strips, 44 pt wide from the screen's edge). A turn is instant, with no page-curl animation. Keyboards and page-turner pedals (which act as keyboards): the right and down arrows and Page Down go forward, the left and up arrows and Page Up go back.
- **«Половина страницы заранее»** (on by default). With one page on screen, the first «forward» replaces the top half of the screen with the start of the next page while the bottom half still shows the end of the current one; the second shows the next page whole. The halves part between systems, at the break nearest the middle of the page, marked by a thin line across the width (rule 2).
- **«Две рядом» turns one page at a time** (answer 3а): 1–2 → 2–3 → 3–4, with no half page ahead.
- **«Лентой»**, as now: one continuous document, scrolled freely.
- **While playing the app turns the pages** (answer 2а). In «По страницам» with one page on screen: when the playhead enters the bottom half, the top half already shows the start of the next page; when it reaches the next page, that page shows whole. With two side by side: when the playhead reaches the right page, the left one is replaced by the next (the pair moves by one). A turn by hand while playing wins until the next Play or a touch on the playhead, as a manual scroll wins over following today. In «Лентой», as now.
- **Going to a place** (the selection, the playhead, marks, bookmarks, the Contents, Insights, Piece DNA, Check) opens the page, or the pair, that holds it in «По страницам», and scrolls there in «Лентой» as now.
- **Size:** «Страница» (the page whole: one in portrait, one in height in landscape); «По ширине» (one page across the width between the strips, scrolled within itself; in «По страницам» a «forward» after its end shows the top of the next; while playing the playhead's system stays in the upper third); «Две рядом» (landscape only; in portrait «Страница» takes its place). A pinch still zooms; a double tap with two fingers, or the menu's button, returns to the chosen size. While playing a pinch does not change the layout.
- **«Ноты крупнее»** (answer 4а): steps − and + upwards from 100 % (the A4 layout, as now). Above 100 % the notes are laid out again by Verovio with the parameters of `A4_LAYOUT` except the page size, on pages of the screen's shape for the orientation: fewer systems to a page, more pages. Bar numbers stay; page numbers are the layout's own, and the strip says «с. 1 из 4 · крупно». The layout for a piece and an orientation is made once and does not change by itself (rule 3); only the step is saved, since the same parameters always give the same layout (the iPad caches it). The PDF view is never laid out again. Ink stays anchored as it is: a stroke is stored from its bar's start as a share of the bar's width and in staff spaces from the bar's top line, so in another layout it lands on its bar at its size, its shape not stretched across the width (the lead's decision 1).

### 4. Concert mode

- **In:** «Концертный режим» at the bottom of the «Вид страницы» popover. It stays on until the player turns it off: back in the library another piece opens in concert mode too (a concert is several pieces in a row), and it holds across a restart of the app. While it is on, the library's header carries a quiet «Концертный режим» label, which turns it off when touched.
- **On the screen:** only the pages and the player's marks (pen, colouring, bookmarks), shown and not changed (answer 8а). No strips, menus, popovers, playhead, selection, phrase marks or ⓘ facts, no roll, assistant, Insights, playback or metronome. The iPad's status bar is hidden; the screen does not dim while the mode is on and a piece is open.
- **Touches:** in «По страницам» a tap on the right half of the screen or a swipe left turns forward, the left half or a swipe right turns back, with half a page ahead and one at a time as in section 3; in «Лентой» a finger scrolls. Keyboards and pedals as in section 3. Nothing else does anything (a tap on a note, a long press, a pinch, two fingers). The size and turning are those chosen in «Вид страницы».
- **Out** (answer 7а): hold a finger in the top right corner (a 64 × 64 pt zone). After 0.3 s a circle appears and fills in 2 s; let go earlier and it goes, nothing happens; once full, «Пюпитр» returns on the same page. VoiceOver has the action «Выйти из концертного режима».
- Not offered on the recording view.

### 5. What does not change

Every function's logic and model (selection, playback, the pen and the marks, Expand, the roll); one accent; tokens only (a new value becomes a token); any iPad, no stylus needed; Russian and English for every new word. Playlists as records of the library (spec section 6, built in the same version) change neither the score screen nor concert mode; a playlist as a concert programme is later (spec section 9).

## Consequences

- `CLAUDE.md` «Non-negotiables» says this record's two rules in place of «the score is the whole screen, nothing docked» and «one continuously scrolling document of A4 pages (no page jumps)»; `design/kit/docs/01-ux-concept.md`, `02-design-system.md`, `HANDOFF.md` and `docs/ui-conventions.md` state it where they stated the old rules, in the same pull request. Their descriptions of code that still runs (the bar's fade, the pill, practice view, the Page Up / Page Down phrase steps, the code maps) change with that code in the version's items.
- The UX baseline entries on the pill, practice view or scrolling (the spec lists PLAY-01, PLAY-02, VIEW-01, VIEW-04, VIEW-08, SCAN-06, INK-01, DRAFT-03, DRAFT-04, DRAFT-05, DRAFT-08, PHR-02) are revised in the pull request that changes the behaviour; the entries for «Вид страницы» and concert mode are new.
- Saved data: only the «Ноты крупнее» step travels with the piece; the rest of «Вид страницы» and concert mode stay on this iPad. Saved format: see the version plan.
- In «По страницам» the arrows and Page Up / Page Down turn pages, where today Page Up / Page Down step phrases (`docs/ui-conventions.md`, #521 19); in «Лентой» they keep what they do now.
- A portrait page is about 6 % narrower than today's, and the strips take height; the page in view whole, «По ширине» and «Ноты крупнее» give the size back.
- The work is the version plan's items e-1 to e-7, r-1 and b-1; each checks itself against the seven rules and the spec's section 8.

## What this record changes in earlier records

| Record | Stands | Replaced by this record |
| --- | --- | --- |
| [0017](0017-a4-pages-and-pdf.md) A4 pages and PDF | A4 as the layout unit at 100 %, the bar-to-page map, the PDF writer | the 2026-11-01 note's «one continuously scrolling document» → pages turned one at a time by default or scrolled, as the player chooses; above 100 % «Ноты крупнее» lays out pages of the screen's shape with their own page numbers |
| [0018](0018-redesign.md) Redesign | the four layers and two places of section 1, phrases, marks, the roll as a drawer, Insights, Piece DNA, the visual system, the Verovio options of section 2 as the 100 % layout | the title's «the score as the whole screen» and section 1's «with nothing else on screen» → the page between two strips; section 2's one continuously scrolling document, edge taps that step a page within the scroll, about 97 % of the width in portrait, the bar that fades and returns and the pill on demand → sections 2 and 3 above; section 8's floating pill that fades while playing → the bottom strip, and following by smooth scrolling → the app turning pages in «По страницам»; section 10's switches that hide or fade the bar and the pill, and «Clean page» → no switches, concert mode |
| [0023](0023-pdf-pieces.md) PDF pieces | the PDF kept byte for byte and shown as printed, its marks, its metronome | the PDF view's one continuously scrolling document → «Вид страницы» as on the Notes; its controls in the strips, never over the page; never laid out again |
| [0025](0025-two-studios.md) Two studios | the whole record; this decision is weighed by its six questions (section 1) | nothing |
