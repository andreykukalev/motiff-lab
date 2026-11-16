# UX baseline: the behaviour Andrei approved

Status: current, 2026-11-16. The first edition: it replaces nothing, and brings back in a smaller form item 18 of
Andrei's v0.6.1 spec (#582), which he cancelled then and approved in this form on 2026-11-16 (decision 2 of the
architecture blueprint for v0.7, #641).

One entry per behaviour Andrei approved, in the product's words: what the player does and what the app does in answer,
not the visual detail (his item 18.1). Before a change, an agent looks up the entries of its area here; a change that
would alter an entry stops and goes to Andrei with the entry quoted (item 18.3). Only Andrei adds an entry or changes
one (item 18.2).

The list starts with the fourteen behaviours that Andrei had approved, that broke between v0.5 and v0.6.5 and were
restored (the blueprint's R1–R14, here 17 entries). Each says the behaviour as approved now: where his instructions
differ, the newest wins. The restorations of 0.6.4 and 0.6.5 wait for his test of those versions.

## Fields

| Field | What it holds |
| --- | --- |
| Id | Area and number: `SEL` selection, `PLAY` playback, `VIEW` the piece's screen and its views, `ROLL` the piano roll, `SET` Settings, `SCAN` reading a scan, `INK` the stylus and the pen. An id is never reused. |
| Behaviour | What holds, in the product's words. |
| Approved | Where Andrei's word is: the version, the spec item or his message (`/mnt/project-files/product/versions/`, the version's GitHub issue); then the version that restored it. |
| Status | `approved`: Andrei's word stands. `provisional`: built, not yet confirmed by Andrei, or an open question to him. `known defect`: approved, and the app does not do it now (the bug's issue named). |
| Protected by | One or more, separated by `;`. A test: the path of a file under `tests/backend/Acceptance/`, `tests/ipad/Baseline/` or `tests/contracts/page-scenarios/` in which the entry's id is written (a test's name, a comment or the scenario's data), so `git grep -n SEL-01 -- tests` finds it. `device: <checklist line>`: what only a real iPad shows. `pending: <step>`: the step that writes the test (the blueprint's A4 the UI walk on macOS, A5 a page scenario, B4 a workspace scenario test), or `baseline 0.<n>` for that version's baseline pull request. |

`governance/tests/baseline.test.mjs` (in `build-test`) checks every entry's id, status and «Protected by», that each
test it names exists and names the entry, and that every file under `tests/ipad/Baseline/` and
`tests/contracts/page-scenarios/` (a `README.md` and fixture folders aside) names an entry that is in the list.

## Entries

| Id | Behaviour | Approved | Status | Protected by |
| --- | --- | --- | --- | --- |
| SEL-01 | A selection is a span of musical time on a grid of sixteenths: it starts and ends at any sixteenth inside a bar, covers part of a bar or runs across bars, and its edges move by the same step. It never snaps to whole bars, and no «По тактам / По долям» choice exists. A tuplet group is selected whole, never split. The same span shows on the score and in the roll; in the roll a frame selects time, so a stretch with no notes is a selection too. | v0.6.1 spec item 6; v0.6.4 spec items 2 and 4 (#614) and Andrei's change of 2026-11-15 19:53–19:55 UTC (the sixteenth, tuplets whole, one pattern on the score and the roll); restored in 0.6.4 (PR #618, #620) | approved | pending: A5 |
| SEL-02 | Selecting works as on the approved mockup, with no switch: in Select the stylus draws a thin dashed frame from where it pressed and the selection grows under it; what the frame covers is selected (part of a bar or one beat too; within one staff, that hand only; across systems, like text). Where the press lands decides: on an edge it moves that edge, inside it extends the selection (the original never shrinks), outside it starts a new one, also from the empty space between systems. A tap on a bar selects the bar, on a note head the note, on the page's margin clears the selection with «Вернуть». The same frame and taps in the roll. | Andrei's feedback on 0.6.2, 2026-11-15 17:01 UTC, items 2 and 4 (#608: as the approved mockup, the mockup authoritative) and his answer «Да, рисует»; v0.6.4 spec (#614), 19:53–19:55 UTC; restored in 0.6.3 (PR #611), its switch removed in 0.6.5 (PR #625) | approved | pending: A5 |
| SEL-03 | A new selection can always be started over an existing one, on the score and in the roll, standing or playing: in Select a frame drawn from outside the selection, or a tap on another bar or note, replaces it in one gesture. The old one shows dashed while the new one is drawn; «Выделение заменено · Вернуть» brings it back. A gesture that was cancelled or lost never blocks the next one. | v0.5.7 spec item 9 (#543); v0.6.1 spec item 8 (#582, one continuous gesture); restored in 0.5.7 and by the place rule of 0.6.3 (PR #611) | approved | pending: A5 |
| SEL-04 | A selection has two handles, hollow circles at the middle of its left and right edges, the same on the score and in the roll: the left one moves the start and the right one the end, outwards and inwards, smoothly, and the edge stays where it is released, also when the selection's size changes. No handle sits in a corner. | v0.6.4 spec items 5 and 6 (#614: a restoration, not a redesign) and Andrei, 2026-11-15 19:55 UTC (the same handles in the roll); restored in 0.6.4 (PR #618, #621) | approved | pending: A5 |
| PLAY-01 | In «Развернуть» the playhead is the same control as on the score: a black strip under the notes with a round handle. With the stylus or a finger the player grabs the handle and drags it either way inside the passage; the notes follow and are heard, and playback goes on from there. | v0.5.6 spec item 2 (#533); v0.6.4 spec item 8 (#614: the same control, no reduced model); restored in 0.6.4 (PR #616) | approved | pending: A5; device: in «Развернуть», drag the handle with the Pencil and with a finger, standing and playing: the notes follow and are heard |
| PLAY-02 | While playing, the player can grab the playhead (stylus or finger, on the score and in the roll at any zoom) and drag it either way: playback pauses while it is held, the notes passed are heard, and on release playback goes on from the new place, without a restart. | v0.5.4 spec item 3.1 (#511, «KEEP AS IS»); v0.5.6 spec item 4 (#533, the roll at every zoom); restored in 0.5.6 | approved | pending: B4; device: playing, drag the playhead on the score and in the roll: the notes passed are heard, playback goes on from where it is released |
| PLAY-03 | «Развернуть» shows the selected passage only; Play there plays the passage round and round, back to its start at the end. It has no «Цикл» toggle, and its edges are the selection's at entry, unchanged inside. | v0.5.6 spec item 2 (#533, Andrei, 2026-11-14), which reversed 0.5.5's «plays once» (an open point an agent had decided); kept in 0.6.4 | approved | pending: B4 |
| PLAY-04 | Now (since 0.6.3): a selection made where there was none turns «Цикл» on, and moving its edges leaves «Цикл» as the player set it. Playing with «Цикл» on, a new selection that does not hold the playing position takes playback to its start and loops it, «Цикл» staying on; one that holds the position plays on. | Open: v0.5.5 spec item 5 (#521, Andrei, 2026-11-14) keeps «a new selection outside the loop turns «Цикл» off» unchanged; 0.6.3 (#608) replaced that rule with the one above, the lead's reading of Andrei's item 3 («the blue playhead does not leave the loop»), listed under «Решено без вас». «Цикл» on with a new selection: Andrei, 2026-11-13 14:55 UTC | provisional | pending: B4 |
| PLAY-05 | A stylus tap does one thing: the click WebKit sends after it never moves the playhead as well, so the same tap gives the same result every time. | Andrei's feedback on 0.6.2, 2026-11-15 17:01 UTC (#608: taps as on the mockup, the same each time); restored in 0.6.3 (PR #611) | approved | pending: A5 |
| VIEW-01 | Switching from PDF to Notes (and back) shows only the controls of the view that opens, from its first frame: a control the app no longer has (the old Play, the bar's Pen button) never appears, not even for a moment, and the old and the new never show together. | v0.6.4 spec item 13 (#614); restored in 0.6.4 (PR #622), the old screen's code removed in 0.6.5 (PR #625) | approved | pending: A4 |
| VIEW-02 | Switching between PDF and Notes again and again, in a full library and with Sources, never freezes or slows down: each switch finishes (the switch test allows 10 s), memory does not grow with the switches, the app answers a tap («Ещё») after every one, and no restart is needed. | v0.6.4 spec item 11 (#614, a release blocker; earlier rounds in 0.5.2 and 0.6.1); restored in 0.6.4 (PR #622) | approved | pending: A4; device: switch PDF and Notes 20 times in the full library with Sources: no slow-down, «Ещё» opens every time |
| VIEW-03 | A page of the notes has balanced margins at the top and the bottom: no wide empty band above the first system, and no room left at the bottom for one more system. | v0.5 bugs item 12 (#479, Andrei, 2026-11-13); v0.5.5 spec item 14 (#521); restored in 0.5.5 | approved | pending: A5 |
| VIEW-04 | The switch of views in the top bar lists the PDF before the notes read from it. The top bar keeps the same layout in every view: the piece's title stays visible in its place, and switching causes no jump. | v0.5 bugs item 13 (#479); v0.6.1 spec item 2 (#582, Andrei, 2026-11-15 12:13 UTC: PDF → Notes); restored in 0.6.1 | approved | pending: A4 |
| ROLL-01 | The roll's zoom is the player's: only −/+, a pinch and Fit change it. Selecting, moving the selection's edges, playback, «Цикл», redrawing, resizing the roll and «Развернуть» leave it as it is; Fit fits the view to the selection once, and the zoom then stays. | v0.5.4 spec item 5 (#511); restored in 0.5.4 | approved | pending: A5 |
| SET-01 | Every control in the iPad's Settings changes what the app does: there is no «Экспериментальное» section and no setting the app does not read. | Andrei, 2026-11-16 09:21 UTC (every experiment becomes the product, no switches) and 09:31 UTC («Мои правки» and the live tempo made to work); restored in 0.6.5 (PR #625, #626) | approved | pending: B4 |
| SCAN-01 | A score written in 4/4 is read as 4/4 from a scan, whether the PDF is read whole or page by page: no extra beat in every bar; a real change of time signature is kept. | v0.5 bugs item 20 (#479); v0.5.2 bug 3 (#495); Andrei, 2026-11-14 19:00 UTC («в нотах снова 5/4»); fixes in 0.5.2 (PR #496) and 0.5.7 (PR #548), his case not reproduced; paused by Andrei at 19:37 UTC until the scan engines are compared | provisional | pending: baseline 0.7 |
| INK-01 | A stylus stroke belongs to one tool: with Pen it writes or erases, with Select it selects, and the page under it (the score or the PDF) does not scroll or zoom; a finger scrolls. | Andrei, 2026-11-13 (#479 item 18: the finger navigates, the stylus follows the tool); v0.5.5 spec item 10 (#521); 0.6.3 (#608, as the mockup: in Select the stylus never scrolls); restored in 0.5.5 and 0.6.3 (PR #611) | approved | pending: A5; device: with «Только Apple Pencil» off, write, erase and select with the Pencil across a page: the page stands, a finger scrolls |

## How the list changes

Once per version (the blueprint's section 6.2):

1. **At the tag**, the lead's version note lists what is new as `provisional` entries with ids: the «what to check»
   list.
2. **Andrei tests.** What he accepts becomes `approved`; what he rejects becomes a bug with the entry's id; what he
   changes his mind about replaces the entry, the old row kept with its id struck through (`~~SEL-03~~`) and dated.
   Only his word changes an `approved` entry.
3. **The next version's first pull request** («baseline of 0.x») adds a test for each newly approved entry at the
   cheapest layer that can see it, with his label. A bug he reports gets a test that fails first; if it broke an
   approved entry, that test goes into the protected folders.
4. Every task brief names the entries of its area; every pull request says `Baseline: unchanged` or lists the entries
   it changes, with Andrei's word for each.

This file, `tests/ipad/Baseline/` and `tests/contracts/page-scenarios/` change only with Andrei's label
`protected-scenarios-ok`, like `tests/backend/Acceptance/` (`governance/README.md`, "Guards").
