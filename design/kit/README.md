# Motiff Lab design kit

The visual design system of the redesign (decision record [0018](../../docs/decisions/0018-redesign.md)): "blue pencil on cream paper", as delivered by Andrei on 2026-11-01 (rev 3 of the kit, 2026-10-31, with the fourth user ink `--ink-ochre` added). The kit is the reference the web app (Blazor) and the native iPad app (SwiftUI) are built from; `tokens.css` is the source of truth for every colour, type style, size, radius, shadow and motion value, and the two PDFs the kit came with (UX concept, Design system) are summarised in 0018.

- `tokens.css` — all design tokens: the Daylight theme on `:root`, Night stand on `[data-theme="dark"]`, the Slate page on `[data-theme="dark"][data-page="slate"]`. The four user inks (graphite, carmine, forest, ochre) are the same in both themes: they are pigments on paper.
- `ds.css` — components and score marks built on the tokens: the bar, the transport pill, the contextual menu, buttons, chips and tags, sheets and drawers, fact rows, cards, fields, dialogs, toasts, the pages rail, the ossia panel, bookmark brackets, the Contents drawer, assistant messages and action chips, settings rows and the Piece DNA map.
- `icons.js` — the icon set (20 px grid, 1.5 px stroke, round caps and joins) as inline SVG; `ic(name, size)`.
- `score.js` — overlay helpers that place brackets, bands, rings and the piano roll from Verovio's rendered measure boxes: the approach the score geometry module takes (prep list item 11).
- `render.mjs`, `render2.mjs`, `render3.mjs` — the Verovio options for the A4 pages, the phrase and ossia excerpts and the motif excerpts (Leland font, scale 50, page 2100 × 2970, top margin 500, side margins 150, `spacingSystem` 20, `svgViewBox`).
- `fonts/` — Source Sans 3 (400, 500, 600) and Source Serif 4 (400, 500, 600) as woff2, from Fontsource (SIL Open Font License). `tokens.css` points here instead of `node_modules`; that is the kit's only change.
- `s1`–`s11 *.html` — the eleven screens, open in a browser at 1180 × 820 or 820 × 1180: library, score page, selection, phrase focus (Daylight and Night stand with the Slate page), Insights, annotations in portrait, bookmarks, assistant, settings, Piece DNA. `b1`–`b4 *.html` — the palette, type, components and icons boards.
- `screens/`, `boards/` — the rendered PNGs of those pages, as they appear in the Design system document.
- `build.py`, `gen_xml.py`, `gen_motifs.py`, `elise.musicxml` — what produced the pages: Für Elise (public domain) engraved by Verovio and paginated to A4, with the brackets, bands and marks drawn from the measure positions.

Rebuild: `npm install verovio`, then `node render.mjs Leland && node render2.mjs && node render3.mjs && python3 gen_xml.py && python3 gen_motifs.py && python3 build.py`; screenshots with Playwright at 2×.

What the kit does not decide (Design system, last page): the final wordmark and app icon (a cream rounded square with one cobalt phrase bracket is proposed), the bar's fade timing after use testing, and the Russian rendering of every control, checked with the longest labels («Правая рука», «Фортепианный ролл», «Оригинал | Вариация | Поочерёдно»).
