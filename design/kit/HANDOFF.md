# Motiff Lab redesign — handoff for the implementing project

Read this file first. It explains how to read the rest of the package, which source wins when two disagree, what was decided and why, and which parts of the pictures are illustration rather than specification. It was written by the model that produced the UX concept and the design system, from the full working conversation with the product owner (Andrei), so it carries context the documents themselves only imply.

Living document (the original, with comments and history): https://claude.ai/artifact/Q22treHDbqvwz4YUQE9FEp — two tabs, "UX concept" and "Design system". The markdown files in this package are exports of those tabs as of 2026-11-04.

---

## 1. What is in the package

| Path | What it is | How to treat it |
|---|---|---|
| `HANDOFF.md` | This file | Read first |
| `CLAUDE-snippet.md` | A short block to paste into the implementing repo's `CLAUDE.md` | Paste once |
| `docs/01-ux-concept.md` | UX concept: interaction model, IA, every overlay, feature mapping, decisions | **Specification of behaviour** |
| `docs/02-design-system.md` | Visual identity, colour, score marks, type, icons, tokens, components, states, responsive rules, the rendered screens, how to apply it to the Blazor app | **Specification of look** |
| `docs/wireframes/w01…w11.png` | The greyscale wireframes embedded in the UX tab (they appear as `[embedded content: …]` placeholders in the markdown) | Structure only; see §7 for stale details |
| `tokens.css` | Every design token, Daylight on `:root`, Night stand on `[data-theme="dark"]`, Slate page on `[data-theme="dark"][data-page="slate"]` | **Copy as is**; the single source for colour, type, space, radius, motion |
| `ds.css` | Every component and score mark, built only on tokens | Reference implementation; port the rules, keep the token usage |
| `icons.js` | The icon set (20 px grid, 1.5 px stroke) as inline SVG, `ic(name, cls)` | Copy the SVG paths; wrap them in whatever the app uses |
| `score.js` | How brackets, bands, rings and the piano roll are placed from Verovio's rendered `g.measure` / `g.staff` boxes | Reference for the overlay layer; `phraseRanges()` is fake (see §6) |
| `render*.mjs` | The exact Verovio options for the A4 pages, the phrase/ossia excerpts and the motif excerpts | Copy the options |
| `gen_xml.py`, `gen_motifs.py`, `elise.musicxml` | Generators for the demo score | Mock data only (see §6) |
| `build.py` | Generates the `s*.html` screens and `b*.html` boards | Tooling for the mockups; not product code |
| `s1…s11 *.html`, `screens/*.png` | High-fidelity renders of 11 screens at iPad 11″ (1180×820 / 820×1180) | Visual reference; behaviour comes from the docs |
| `b1…b4 *.html`, `boards/*.png` | Palette, type, components, icons boards | Visual reference |

---

## 2. Which source wins

When two sources disagree, the one higher in this list is right:

1. **The text of the two document tabs** (`docs/*.md`), and within them the most recent decisions (the "Open questions" list at the end of the UX tab, and §4 below, which restates them).
2. **`tokens.css`** for any value: colour, size, radius, shadow, timing, font.
3. **`ds.css`** for component construction and the form of every score mark.
4. **The high-fidelity renders** (`s*.html`, `screens/*.png`) for composition, density and feel.
5. **The wireframes** (`docs/wireframes/*.png`) for layout and structure only. They are the oldest artefact and several details in them were superseded (listed in §7).

The HTML mockups are pictures built to look right in a screenshot, with absolute positioning and inline styles. Do not port their markup or layout code; port the tokens, the component rules in `ds.css`, the icons and the Verovio options.

---

## 3. Product context the documents assume

- **Motiff Lab** — an app for pianists that is centred on the score: import a piece, read it, select phrases, practise them (loop, tempo, hands), understand them (facts with evidence), try variations, annotate. Tagline: *Hear. Understand. Transform.*
- **Current build** (when the kit was made; history now): a Blazor web app running in an iPad shell, branch `main` after PR #170, 32 screens, notation rendered by **Verovio**. Import routes: MIDI/MusicXML (on device), photo/PDF via **homr** OMR (server), audio/video via **Transkun** transcription (server). Phrase detection, facts, experiments, build-a-variation, sessions and saved ideas all exist today. The redesign moves and reshapes them; it removes no capability (the full mapping is the "Where every current feature goes" table in the UX tab). Since then the shell was replaced by a native iPad app (`docs/decisions/0019-native-ipad-app.md`), and the web app and the iPad app became two workspaces of one product with no parity, the web Music Studio and the iPad Practice Studio (`docs/decisions/0025-two-studios.md`).
- **Today's look** is a dark dashboard with a neon-lime accent. That accent and those surfaces disappear entirely.
- **Target device**: any iPad, **with or without a stylus**, both orientations, designed at 11″ (1180×820 landscape, 820×1180 portrait) and to be checked on 13″.
- **Languages**: Russian and English, for the interface **and all generated content** (facts, Evidence, Try chip names, assistant answers, Piece DNA idea cards, category names, Check reasons, import warnings). One language setting applies to everything. The current build mixes English into Russian; that is a defect to remove. Lay out every control for the longer Russian labels.
- **Free vs Pro** (a product rule, not a per-feature choice): everything that runs on the device is free; everything that needs cloud infrastructure or an AI model is Pro. So: AI Assistant, scan import and transcription import are Pro; sync between the workspaces is free with a quota (Andrei, 2026-11-08, `docs/decisions/0025-two-studios.md` section 8). Facts, Try chips, variations, Piece DNA, bookmarks, ink, playback and on-device file import are free. Free-tier Pro entry points are visible and open a one-line plum offer; nothing is sent anywhere unless the player signs in and turns sync on.

---

## 4. The model in twelve rules

If an implementation choice is unclear, check it against these. Each one came from an explicit decision by the product owner.

1. **The score is the whole screen.** The page is the only permanent thing. Nothing is docked: no side panel, no permanent piano roll, no tab bar. Everything else is pulled up from a selection or the bar and put away with a swipe, leaving the state underneath exactly as it was.
2. **One continuously scrolling document, laid out as A4 pages.** Vertical scroll, never page jumps. Pages are a layout unit (A4 proportions, page numbers, a stable bar-to-page map), two per row in landscape, one column in portrait. A phrase that crosses a page edge is followed without a break. Edge tap / arrow keys step exactly one page within the same scroll; a snap option exists in Settings.
3. **Two places to work, one to step back to.** The score page and phrase focus are the working places; Piece DNA is the step-back place outside the practice loop; the library is outside the piece. Overlays never open another place.
4. **The score is the selection surface.** Phrases are pencil brackets above the top staff; tap selects, second tap focuses; drag across bars makes a free selection. There is no phrase strip, no phrase list, no review step.
5. **Phrase granularity is the user's choice**: Short (1–4 bars) / Phrases (4–8, default) / Sentences (8–16+), from the Phrases action in the bar. Detection runs once as a hierarchy; changing level redraws in place. Never show a piece as dozens of equal brackets. Manual edits (drag handles, merge, split, make phrase) stick at that level until Reset to detected.
6. **One contextual menu** above the selection holds the actions a musician takes on a passage; it never shows items that do not apply.
7. **Focus is a reading lamp, not a screen.** The phrase lifts into a full-width card; the page stays dimmed behind it; Back, a tap on the dim page or a pinch-in returns. Variations appear as an **ossia** above the original; a phrase can hold **any number** of variations (a chip row picks which, two can be compared).
8. **Insights is one continuous stream**, not tabs. Understand / Explore / Build no longer exist as places: a fact carries its own **Try** chips (experiments derived from that fact); a tap sounds the variation as the ossia without stopping the loop; **Keep** saves it as an idea; **Adjust** opens the old Build controls inline. The sheet is called **Insights** (decided name).
9. **Bookmarks are the musician's own table of contents**: titled, categorised passages, shown as coloured brackets above the phrase brackets and listed in the Contents drawer. Four default categories, each with an ink colour: Practise (carmine), Performance (forest), Structure (graphite), Teacher (ochre).
10. **The AI Assistant is the last voice in the Insights stream**, not a chat window. Scoped to a phrase, a selection, a bookmark or the whole piece; shows openly what context it was given; answers carry action chips that drive the app (loop, tempo, hands, bookmark, show on score, try). Pro in full.
11. **Settings live at two levels**: one global Settings sheet (with three presets — Learning / Practising / Clean — and a switch per element), and small contextual popovers on each element by long-press, with a *This piece · Everywhere* scope. Both edit the same switches. The first run asks once for a preset, with a one-line description per card.
12. **Progressive disclosure everywhere.** The default state is a page of music and nothing else; the bar fades after ~2 s; the transport pill appears only with a selection or playback; the app does not explain itself in captions.

---

## 5. Decisions log, with the reason behind each

These are the product owner's decisions during the work, in his own terms where possible. They explain *why* the documents look the way they do; respect them when something has to be adapted.

| Decision | Reason given |
|---|---|
| UX concept before visual design; iPad first, both orientations | Get the model right before styling it |
| Drop the "What do you want to do?" intent step; Open goes straight to the score | Play / Understand / Explore are things done from a phrase, not a choice made before seeing the music |
| Keep variations, but behind phrase focus | Variations are contextual to a phrase |
| Continuous vertical scroll instead of page turns | "Phrase can be continued on next page. Better to have scrolling instant document, rather than page jump." |
| User-chosen phrase granularity, no review list | "Current detection is useless, just long enumeration of phrases (dozens). No one will explore this." Beginners want short practisable chunks, advanced players whole semantic sentences. "Review by the user is not needed; however the user can update phrases manually." |
| Bookmarks as a personal table of contents with categories | Explicit request for a phrase bookmarking and navigation system |
| Understand / Explore / Build merged into one stream | "Should be an instant and continuous process" |
| AI Assistant: context-aware, phrase or whole piece | Requested; Pro |
| Global Settings **plus** contextual small settings | "Not only a Settings tab; should be a global settings menu + contextual small settings" |
| Piece DNA as a composition-level map outside the practice flow | Requested explicitly as an overview, not part of practice |
| Any iPad, with or without stylus | "Target — any iPad. App should support iPads without stylus." → Pen mode for finger drawing |
| Multiple variations per phrase | "Yes, multiple variations should be possible" |
| Reading / As played quick switch for transcriptions | A person imports a performance from audio and learns from clean notation, but should be able to notice the performer's nuances and switch to As played instantly |
| MIDI keyboard input as a future idea | Gather context about actual playing for personalised, better advice; scope of v1 still open |
| Sheet named "Insights" | Chosen name |
| RU + EN for UI and content | Both languages ship |
| Pro = anything needing cloud/AI; on-device = free | "Pro is everything that I will pay for (infra, cloud, AI models). Everything run on user device is free." |
| Sync between the workspaces is free, with a quota (2026-11-08) | An exception to the Pro rule above; `docs/decisions/0025-two-studios.md` section 8 |
| First run asks for a preset, with short descriptions | "Ask, and give a short description of each preset" |
| Fourth ink token `--ink-ochre` #9C6A0C for Teacher | The concept had four categories but tokens had three inks; ochre chosen for 4.5:1 on paper |

---

## 6. What is invented in the mockups (do not ship it, do not treat it as data)

- **The score.** `elise.musicxml` is an *approximation* of Für Elise generated by `gen_xml.py` (97 bars, A/A′/B pattern) so Verovio had something real-looking to engrave. Notes are not the real piece. The excerpts (`phrase`, `ossia`, `motif_a/b/c`) are generated the same way.
- **Phrase ranges.** `score.js → phraseRanges()` returns fixed, hand-picked bar ranges for the pictures. Real ranges come from the existing detection engine at the chosen granularity.
- **Facts, counts and labels**: "fifteen places", fact texts, Try chip names on screen, "9 bookmarks", "2 ideas", Piece DNA sections / motifs / occurrence marks, assistant messages, Check flags on bar 50, resume point names. All illustrative. The *kinds* of content and where they sit are specified; the content itself is not.
- **Strings.** All renders are in English. Only a few Russian labels are given (Правая рука, Фортепианный ролл, Оригинал | Вариация | Поочерёдно). The Russian set has not been rendered; the current build's existing Russian strings are the starting point, and the i18n layer must cover generated content too.
- **Wordmark and app icon** are proposals only (Source Serif 4 Medium wordmark; cream rounded square with one cobalt phrase bracket).

---

## 7. Known gaps, stale spots and small contradictions

The documents were checked for consistency, but some pictures predate later decisions. **Where a picture disagrees with the text, the text wins.** Specifically:

**Wireframes (`docs/wireframes/`)**
- `w01-layers`: the page box says "page turns" — superseded by continuous scroll.
- `w03-score-page`: "pp. 2–3 of 7" / "p. 2 of 7" are placeholder numbers; callout 4 text is clipped at the right edge (full text: "Scrolling: the whole piece is one continuous document; tap the edge or press an arrow key to move exactly one page. Nothing is cut at a page edge.").
- `w05-phrase-focus`: "phrase 3 of 12" is a placeholder (at Phrases level Für Elise has about sixteen); "Experiments and Build results land here" uses retired terms (now: Try chips from the Insights stream); it lacks the variation chip row, the Reading | As played control, the Contents toggle and the Insights strip, all specified in the text.
- `w07-insights`: filter chips read "Rhythm, Melody, Key"; the text groups facts as Rhythm, Melody, Harmony and Form. The saved-ideas strip at the top of the stream and the Adjust chip are not drawn.
- `w08-assistant`: the Pro line is orange in the wireframe; in the design it is plum. "p. 1 of 4" vs other wireframes' "of 7" — placeholders.
- `w10-library`: callout 4 puts language behind the avatar; decided later: language lives in **Settings** (the account sheet keeps sign-in, online storage, privacy, terms). "7 pages · 12 phrases" are placeholders. The Check line is orange here; in the design it is the amber flag with `--warning` text. Add piece rows lack the Pro tags on Scan a page and From a recording.
- Wireframes are greyscale with a generic blue for callouts and selection; that blue is not the cobalt token, and any orange is not a token at all.

**High-fidelity renders (`screens/`, `s*.html`)**
- Phrase focus renders (`s4`, `s5`) predate multiple variations and the Insights strip: no chip row above the ossia, no strip of three facts under the card.
- Bookmarks render (`s8`) shows three categories; the fourth, Teacher (ochre), was added later and is specified in the text and tokens.
- Library render (`s1`) lacks the Piece DNA entry on the card and the Pro tags on the scan and transcribe rows of Add piece.
- **Not rendered at all** (spec is in the text only): first-run preset screen; Check drawer; Reading | As played control; Pen active state and finger-drawing mode; Notes and Pages tabs of the Contents drawer; Pages rail; Add piece sheet; account sheet; portrait versions of most overlays (the responsive table in both tabs is the spec); empty, loading and error states; Not Found page; the Russian language.

**Small text inconsistencies to resolve during implementation**
- *Contextual menu items.* The UX text says it holds "the six actions" (Loop, Focus, Note, Bookmark, Piano roll, Insights; overflow: Make phrase, Merge with next, Split here, Highlight), while the Assistant section also puts *Ask* in the contextual menu. Suggested reading: the six primary items as drawn, with Ask next to Insights in the same group (it opens the same sheet). Confirm with the product owner.
- *Bar actions.* UX: title, page count and **five actions** (Phrases, Pen, Piano roll, Insights, Play). DS responsive note mentions "six icons" — that is the five actions plus the overflow (⋯). Piece DNA and Settings live in the overflow / title menu, not in the five.
- *Retired words still in passing text*: "page turns" (Annotations intro), "Explore tries" (Piece DNA section) — read as "scrolling" and "Try chips".

**Still open (to confirm against the code, not to redesign)**
- That the current build renders fixed A4 pages with a stable bar-to-page map (the design assumes it; Verovio does it with the options in §8).
- That detection can produce the three granularity levels from one analysis, and manual edits override per level until Reset.
- MIDI keyboard input: first-version scope (listening only vs a kept take per resume point).
- Bar fade timing after use testing; final wordmark and icon.

---

## 8. Technical notes that cost time to discover

**Verovio** (tested with npm `verovio` 6.4.0, `verovio/wasm` + `verovio/esm`):
- Options: `font: "Leland"`, `pageHeight: 2970`, `pageWidth: 2100`, `scale: 50`, `header: "auto"`, `pageMarginTop: 500` (500 is the maximum Verovio accepts; 520 is rejected), `pageMarginLeft/Right: 150`, `spacingSystem: 20`, `svgViewBox: true`.
- Keep `header: "auto"` but hide the engraved header with CSS (`.page svg .pgHead { display: none }`): it reserves the top margin the bar floats over, and the title is shown by the app, not engraved. Without this the engraved title collides with the bar.
- **Theming notation**: Verovio emits glyphs as `<use>` with hard colours. Set `color: var(--notation)` on the container, `.page svg use { fill: currentColor }`, and override any element carrying a `color` attribute (`.page svg [color] { color: var(--notation) !important }`). Without this, dark theme and the Slate page keep black notation.
- All overlays (brackets, bands, rings, bookmark brackets, ink anchors, Check flags) are placed from the rendered `g.measure` and `g.staff` bounding boxes; see `score.js`. Store marks per bar, never per pixel, so they survive zoom and reflow.

**Colour and contrast**
- `--warning` text is #965D04 (4.95:1); the brighter amber failed contrast for text and is fill-only.
- `--ink-ochre` #9C6A0C is 4.46–4.5:1 on paper; acceptable for 1.25 px brackets and italic titles, borderline for small body text.
- User inks do not change with the theme (pigment on paper). On the Slate page they get a 1 px paper-coloured halo.

**Fonts**: Source Sans 3 and Source Serif 4, weights 400/500/600, self-hosted woff2 (`@fontsource`). Tabular numerals on globally.

---

## 9. Colour semantics — strict, this is the identity

| Colour | Means | Never use it for |
|---|---|---|
| **Cobalt** `--accent` #2F5BD6 | What the app is pointing at: selection, focus, playhead, active control, fact rings, links | Decoration, headings, category colours, illustrations |
| **Plum** `--plum` #7B3F9E | What the app composed: ossia, variations, saved ideas (the dot on a bracket) — and the Pro offer | User marks, selection |
| **Amber** `--amber` #F0B429 | What the user marked: highlighter, margin-note markers, Check flags (fill) | Text (use `--warning` for text) |
| **User inks** graphite / carmine / forest / ochre | The musician's pencil: ink strokes, fingering (graphite), bookmark brackets by category | App-generated marks |
| **Teal** `--teal` | Left-hand notes in the piano roll. Right-hand notes use `--accent`: the one documented exception to "cobalt only points", kept because it matches the current build's hand colours | Anything else |
| **Neutrals** desk < surface < paper | The paper is always the brightest surface; chrome is warm, matte, quiet | — |

One accent only. If a new element seems to need a second "interactive" colour, it is the wrong element.

---

## 10. Recommended order of work

This follows the DS tab's "Applying it to the existing app", adjusted with what is known now.

1. **Tokens and fonts.** Add `tokens.css`; set `data-theme` from the system with a manual override; replace every literal colour, radius and shadow. The lime accent and dashboard surfaces go in this step. Set up RU/EN string resources if they are not already complete, including for generated content.
2. **Score surface.** Verovio options and notation theming (§8); continuous vertical scroll of A4 pages (two per row landscape, one column portrait); the overlay layer driven by measure boxes; phrase brackets at the chosen granularity.
3. **Bar and transport pill**, with fade behaviour.
4. **Selection**: band, handles, contextual menu; sticky selection.
5. **Phrase focus**: lifted card, dim page, prev/next, ossia with variation chips, Original / Variation / Alternate, Reading | As played, lead-in.
6. **Piano roll drawer** (aligned bar for bar), then the **Insights sheet** (stream, Try / Keep / Adjust), then **Contents drawer** (bookmarks, Notes, Pages).
7. **Annotations**: the stylus follows the tool (since 2026-11-13, #479 item 18: it draws with Pen, selects with Select); Pen mode for finger; margin notes, highlights, fingering, bookmark brackets; layers in the Pen menu.
8. **Library and import** (resume points replace sessions; Check layer and drawer replace report pages).
9. **Settings** (global sheet with presets, first-run preset screen, contextual popovers).
10. **Piece DNA**, then **AI Assistant** (Pro).
11. **Verify**: contrast table against the shipped build, Slate page with all four inks, both orientations on 11″ and 13″, finger-only use (no stylus), Russian with the longest labels.

---

## 11. Review checklist — reject a screen if any of these is true

- Something is docked permanently beside or under the score.
- There are view tabs or mode switches in the header (Score / Piano roll / Both, Understand / Explore / Build).
- Phrases are presented as a list or strip to read through, or a piece shows dozens of brackets by default.
- Moving through the score jumps page to page instead of scrolling.
- A feature works only with a stylus.
- Cobalt is used for anything other than pointing / selection / active state; or a second accent appears.
- A white panel sits on top of the page (other than the contextual menu and dialogs), or anything is brighter than the paper.
- The app explains itself in captions under the score.
- A Pro feature sends anything to the cloud in the free tier (other than sync the player turned on), or an on-device feature is behind Pro.
- A string or generated text is in English while the UI is Russian (or the reverse).
- Opening or closing an overlay loses the selection, the position, the loop or the tempo.
