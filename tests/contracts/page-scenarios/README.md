# Page scenarios

What the iPad's two pages, `src/ipad/App/Web/score.html` and `roll.html`, do when they are pressed. Each scenario is a
file of data, kept apart from the programs that replay it. It names the entries of `docs/ux-baseline.md` it protects.
Two runners replay the same files:

- **Linux, in CI** (`build-test`): `tests/pages/run.mjs`, Chromium through Playwright, on the real pages over the compiled
  shared modules, served as the app's bundle lays them out.
- **The Mac, in the `iPad` workflow**: `tests/ipad/MotiffLabTests/PageScenarioTests.swift`, in the app's own WKWebView,
  served by its scheme handler.

Both load `tests/pages/replay.js` into the page. The replayer stands in for the app's message handlers
(`window.webkit.messageHandlers`), sends the app's commands, presses, and compares. The pages need no hook of their own.

```bash
dotnet build src/shared                  # the compiled modules the pages import
npm ci --prefix tests/pages              # playwright-core alone; the browser is the machine's
node tests/pages/run.mjs [--show] [id …] # every scenario, or those named
```

`--show` prints what each expectation saw: every message posted since the expectation before, and the marks it names.
Use it to write a scenario from what the page does. `PAGES_CHANNEL=chrome` uses the installed Chrome (as CI does), and
`PAGES_PARALLEL` sets how many pages run at once (4 by default, 2 in CI). Beside the scenarios, every run checks that the
fixtures are fresh (see "The fixtures").

## A scenario

```json
{
  "id": "score-sel-grid-drag",
  "protects": ["SEL-01"],
  "says": "One sentence: the rule, as the player sees it.",
  "page": "score",
  "fixture": "fur-elise-score.json",
  "viewport": { "width": 1024, "height": 1366 },
  "chromiumOnly": false,
  "steps": [ … ]
}
```

- `id` is the file's name without `.json`.
- `page` is `score` or `roll`.
- `viewport` is the page's size in CSS pixels. It is optional, and 1024 × 1366 (an 11-inch iPad, upright) by default.
- `chromiumOnly: true` leaves the scenario to Chromium (see below). No scenario sets it yet.

Every step may carry a `note` (where in the app it comes from, or why). Differences name the step by its number and its
note.

A step may also carry a `question`. It marks what the step records but Andrei has not settled: the specs, the notes and
the code disagree, or the behaviour looks doubtful. The step still records what the page does now, and the question says
what it does, what each source said, and what to decide. `run.mjs` prints the questions after every run. Once Andrei
answers, the question goes, and the expectation is changed if he chose otherwise, with his label.

A step is one of four kinds:

| Step | What it does |
|---|---|
| `{"call": "select", "with": {…}}` | An app command: `window.motiff.<call>(with)` or `window.roll.<call>(with)`. Without `with`, the command is called with no argument. A string `"$fixture.<path>"` anywhere in `with` is replaced by that value of the fixture. |
| `{"pointer": "pen", "path": [p, …], "hold": 900, "click": true}` | A press: `pen`, `touch` or `mouse`, down at the first point, through the others a frame apart, up at the last. A press with one point and no `hold` is a tap: down and up back to back. `hold` waits that many ms on the page's clock before moving (a long press). `click` follows the lifting with the click WebKit sends after a stylus touch. |
| `{"wait": 100}` | Waits that many ms. |
| `{"expect": {…}}` | Compares what the page posted since the previous `expect`, and what it draws now. |

**Time.** A scenario never races a timer of the page. Where the page gives a condition, a step waits for that: the
replayer waits a frame after every call and every press, and a step sends the app's answer as a `call` (the position the
player reports after a drag, the selection after a span) instead of waiting for a timeout. Where a real duration is the
rule (a long press, a view that must stay put), the `hold` or `wait` is at least twice the page's timer, and its `note`
says which timer: a hold is 900 ms against the pages' hold (`HOLD_MS` of `selection-gesture.ts`, 450 ms), a wait to see
that the roll's view stays is 640 ms against its glide (the kit's slow time, 320 ms).

Both runners time every press on the page's own clock (`performance.now()` when the page takes each pointer event). A
press with no `hold` that the page felt held still for its `HOLD_MS` or more (or a hold it felt shorter) is reported as a
timing fault, `stopped: timing fault: step n …`, instead of the differences it would cause: the machine was too slow
for the scenario, and the page's rules were not tested. Two taps on the same place need a `wait` of twice the double
tap's 320 ms between them (`DOUBLE_TAP_MS`); no scenario has them yet.

**Points.**

- On the score:
  - `{"bar": n, "x": 0.3, "y": 0.4}` is a point in bar *n* (1-based, in the order of `motiff.frames()`). `x` and `y` are
    fractions of the bar's box, from its left to its right and from its top line to its bottom one.
  - `{"card": n, "x": 0.5, "y": 0.5}` is a point in bar *n* of Expand's card (1-based, in time order), boxed as a bar of
    the pages is, from the staff lines the card draws.
  - `{"edge": "start" | "end"}` is the centre of a drawn selection edge's handle.
  - `{"grip": "black" | "blue"}` is the centre of a drawn playhead handle.
  - `{"marker": "place", "bar": 6}` is the centre of the first drawn marker over the bars of that kind (`version`,
    `place`, `technique` or `seam`, as the mark `markers` reads them), of that bar when `bar` is given.
  - Each point may add `dx` and `dy` in pixels.
- On the roll: `{"tick": 1920, "y": 0.7}` is a tick across the notes, and a fraction of the notes' height down. The x of a
  tick comes from `roll.frame()` (zoom and scroll) and where the roll draws its playhead's control. `{"grip": "black"}`
  is the centre of the playhead's handle in the strip under the bar numbers, where its control sits. It is read only
  where the control stands at the tick the app last reported (`position`): after a drag the roll shows the place let go
  until that report or its 800 ms timer, so a scenario sends the app's answer to a drag before it presses the grip, and a
  grip pressed earlier stops the scenario.

**Expectations.**

```json
{"expect": {
  "posted": [{"kinds": ["chosen", "place"], "messages": [{"chosen": {"start": 1080, "end": 2160}}]},
             {"kinds": ["drag"], "includes": [{"drag": {"phase": "grab"}}, {"drag": {"phase": "release"}}]}],
  "marks": {"band": [{"left": 355, "top": 227, "right": 534, "bottom": 358}]},
  "returned": {"zoom": 68.93},
  "tolerance": 1
}}
```

- **`posted`** is one check, or a list of them. A message's kind is its first key (`{"chosen": …}` is a `chosen`). A check
  looks only at the messages of its `kinds`, in the order they came:
  - `messages` must match them all, one for one. `[]` says that none of those kinds was posted.
  - `includes` must appear among them in that order, with others allowed between. Use it for moves, whose number depends
    on timing.
- **`marks`** is what the page draws now, read from the page by name. All geometry is in document pixels, rounded:
  - score:
    - `band`: the selection's runs `{left, top, right, bottom}`;
    - `edges`: `{edge, x, top, bottom, handle: {x, y}}`, where the handle is the centre of the drawn `::after` circle;
    - `handles`: `{x, y}`;
    - `grips`: `{handle: black|blue, x, y}`;
    - `paper`: `{pages: [{left, top, right, bottom, inkTop, inkBottom}], above, below}`, where `inkTop` and `inkBottom`
      are the room between the paper's edge and its first and last staff;
    - `card`: Expand's card, `{left, top, right, bottom, inkTop, inkBottom, scroll}`: the box its notes scroll in, the
      room in it above its first bar's top line and below its last bar's bottom line, and how far its notes are
      scrolled; null when no card is open;
    - `markers`: the labels over the bars that `performance` gives, in the order the page draws them, `{kind, bar,
      text, state, left, top, right, bottom}`: `kind` is `version` (another version's label), or a draft's `place`
      label, `technique` number or `seam` diamond, `bar` the bar it names, `state` its `state-…` class or null;
    - `scroll`: `{x, y}`.
  - roll:
    - `frame`: `{zoom, rowScale, scroll, pitchTop}`, with zoom and rowScale to 0.01;
    - `playhead`: the tick its control shows.
- **`returned`** is what the last `call` returned.
- **The match is partial.** Only the keys an expectation names are compared, so it says no more than the rule needs. A
  list must have as many items as expected. Numbers match within `tolerance`, which is 1 by default. That absorbs the
  rounding of pixels; a tick one off is far below a sixteenth (120).

## The fixtures

`fixtures/` holds the input the app gives each page for *Für Elise* (`src/shared/Playback/FurElise.musicxml`):

- `fur-elise-score.json`: the first A4 page as Verovio engraves it with the app's options (`A4_LAYOUT` and
  `NOTE_SPACING` of `score-page.ts`), its timemap, and the piece's beat lines; and under `card`, bars 5–20 as the app
  engraves them for Expand's card (`Engraver.engraveCard`, with `cardLayout` of `phrase-card.ts` at the card's width on
  a page of 1024 × 1366, 964 px), their pages and timemap;
- `fur-elise-roll.json`: music-core's roll payload.

They are made by music-core and Verovio. Remake them after a change to either, or to the engraving options, and review
what moved:

```bash
dotnet build src/shared && node tests/pages/fixture.mjs
```

`tests/pages/fixture.cs` asks music-core's facade as the app does. The output is the same on every run, so every run of
`run.mjs` makes them again beside the scenarios (`fixture.mjs --check`, a few seconds) and fails when a committed fixture
is not what the code makes now, naming the first difference. A change to `A4_LAYOUT` or `NOTE_SPACING` (VIEW-03's
margins), to music-core's notation or roll, or to Verovio thus fails the check until the fixtures are remade, and then the
scenarios see it.

## What only Chromium checks

The WebKit replay presses with pointer events the replayer makes, because a test cannot give WebKit real touches.
Chromium replays every scenario twice: once with those same events, and once with the browser's own input (Chrome
DevTools Protocol: a pen and a mouse as mouse events of that kind, a finger as touch events). So these are Chromium's
alone:

- **The browser's own input.** That means the pointer events, touch events, and click Chromium makes itself from a
  press, the hit testing behind them, and real pointer capture. In WebKit the made-up pointers' capture is kept by the
  replayer (`setPointerCapture` throws for a pointer the browser does not know). WebKit also gets no `TouchEvent`, and
  its `click` is the replayer's (`"click": true`).
- **A scenario with `chromiumOnly: true`** runs only with the browser's own input. Set it when a rule can be pressed only
  that way, and say why in `says`.

Every expectation is otherwise read the same way in both browsers. A difference in WebKit fails the `iPad` workflow with
the scenario's id and the entries it protects.

## What is not seen at page level

These entries, or parts of them, are checked elsewhere or not at all:

- **SEL-04 on the roll.** The roll draws its selection's handles on its canvas, so they are no element to read. The roll
  scenarios check what it posts (`span`, `drag`), not the handles.
- **VIEW-03's engraving options in Swift.** The scenario checks the paper and the margins the page draws, on a fixture
  engraved with `score-page.ts`'s options and kept fresh by the check above. The options Swift gives Verovio
  (`Engraver.swift`) are tied to `score-page.ts` by `tests/shared/score-pdf.test.mjs`; the iPad's Verovio is built
  from the same version's source, not the JS module the fixture uses.
- **PLAY-02's sound.** What is heard as the playhead is dragged is the app's player, not the page. The scenario checks
  that the page posts the drag's grab, its moves and its release while playing.
- **Anything the app's Swift decides.** How the app answers a message (the selection it sends back, the menu, saving)
  is Swift's. A scenario sends the app's answer as a `call`, as the app would.
