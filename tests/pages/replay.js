// The page scenarios' replayer, inside the page (tests/contracts/page-scenarios/README.md): run by tests/pages/run.mjs in
// Chromium and by tests/ipad/MotiffLabTests/PageScenarioTests.swift in the app's WKWebView, so both read a scenario alike.
// It stands in for Swift's message handlers (window.webkit.messageHandlers), carries out the steps the app would (its
// commands through window.motiff or window.roll), presses where a step says with pointer events made here (or hands the
// points to the runner, which presses with the browser's own input), and compares what the page posted and drew with the
// step's expectations, as a list of readable differences. Plain script, no module: WKWebView evaluates it as it is.
(() => {
    const PAGE = "#pages > .page";
    const FIRST_POINTER = 1000;
    const STEPS_PER_SEGMENT = 8;
    const DEFAULT_TOLERANCE = 1;
    /** How far, in pixels, the roll's playhead control may stand from the reported tick's x: its left is rounded. */
    const GRIP_SLACK = 2;

    const run = { scenario: null, fixture: null, posts: [], read: 0, seen: null, page: "score", pointer: FIRST_POINTER, felt: [], hold: null };

    // ---- The page's clock for a press ------------------------------------------------------------------------------
    // Every pointer event as the page takes it (the window's capture comes before the page's own handlers), on the page's
    // clock: the time a press lasted is the time the page's hold timer saw, also under load, whoever made the events.
    for (const type of ["pointerdown", "pointermove", "pointerup", "pointercancel"]) {
        window.addEventListener(type, (event) => run.felt.push({ type, x: event.clientX, y: event.clientY, at: performance.now() }), { capture: true, passive: true });
    }

    /**
     * Swift's message handlers: what the page posts is kept, for the expectations, in the order it came. A WKWebView may
     * have its own `webkit` object already: then its `messageHandlers` are stood in for.
     */
    function standIn() {
        const handler = { postMessage: (message) => run.posts.push(JSON.parse(JSON.stringify(message))) };
        const handlers = { score: handler, roll: handler };
        for (const [owner, name, value] of [[window, "webkit", { messageHandlers: handlers }], [window.webkit, "messageHandlers", handlers]]) {
            try {
                Object.defineProperty(owner, name, { configurable: true, writable: true, value });
            } catch {
                // That object keeps its own; the next way, or the check below.
            }
            if (window.webkit?.messageHandlers?.[run.page] === handler) {
                return;
            }
        }
        if (window.webkit?.messageHandlers?.[run.page] !== handler) {
            throw new Error("The replayer could not stand in for window.webkit.messageHandlers: the page's messages cannot be read.");
        }
    }

    // ---- Pointer capture for pointers made here --------------------------------------------------------------------
    // A pointer event made by a script has no pointer the browser knows, so setPointerCapture throws for it (piano-roll.ts
    // calls it bare). For those pointers alone the capture is kept here, and their moves and lifting go to the element that
    // took them, as the browser would send them.
    const captured = new Map();
    const native = {
        set: Element.prototype.setPointerCapture,
        release: Element.prototype.releasePointerCapture,
        has: Element.prototype.hasPointerCapture,
    };
    Element.prototype.setPointerCapture = function setPointerCapture(id) {
        if (id >= FIRST_POINTER) {
            captured.set(id, this);
            return undefined;
        }
        return native.set.call(this, id);
    };
    Element.prototype.releasePointerCapture = function releasePointerCapture(id) {
        if (id >= FIRST_POINTER) {
            captured.delete(id);
            return undefined;
        }
        return native.release.call(this, id);
    };
    Element.prototype.hasPointerCapture = function hasPointerCapture(id) {
        return id >= FIRST_POINTER ? captured.get(id) === this : native.has.call(this, id);
    };

    const frame = () => new Promise((resolve) => requestAnimationFrame(() => resolve()));
    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    function pointerEvent(type, kind, id, point, buttons) {
        return new PointerEvent(type, {
            bubbles: true, cancelable: true, composed: true, pointerId: id, pointerType: kind, isPrimary: true,
            clientX: point.x, clientY: point.y, button: type === "pointermove" ? -1 : 0, buttons, pressure: buttons ? 0.5 : 0,
            width: kind === "touch" ? 20 : 1, height: kind === "touch" ? 20 : 1,
        });
    }

    function send(type, kind, id, point, buttons) {
        const target = captured.get(id) ?? document.elementFromPoint(point.x, point.y) ?? document.body;
        target.dispatchEvent(pointerEvent(type, kind, id, point, buttons));
        return target;
    }

    /** The points between two, the last included: a stylus or a finger moves on in small steps. */
    function between(from, to, steps) {
        return Array.from({ length: steps }, (_, index) => ({ x: from.x + ((to.x - from.x) * (index + 1)) / steps, y: from.y + ((to.y - from.y) * (index + 1)) / steps }));
    }

    /**
     * How a pointer step presses, for both runners: down at its first point, then the moves (a stylus or a finger moves on
     * in small steps), up at the last. With no `hold`, the first `atOnce` moves go with the press, back to back, until the
     * pointer has left the page's hold (HOLD_SLOP): no frame comes between, so no slow frame turns the press into a hold;
     * a tap (one point) is pressed and lifted back to back. The other moves go a frame apart. With a `hold`, the pointer
     * waits that long on the page's clock first, so the page's own hold timer, started at the press, runs out before it.
     */
    function stroke(step) {
        const [down, ...corners] = points(step);
        const moves = corners.flatMap((corner, index) => between(index === 0 ? down : corners[index - 1], corner, STEPS_PER_SEGMENT));
        const leaves = moves.findIndex((move) => Math.hypot(move.x - down.x, move.y - down.y) > run.hold.slop);
        return { down, moves, atOnce: step.hold ? 0 : leaves < 0 ? moves.length : leaves + 1, up: moves.at(-1) ?? down };
    }

    /** A press, its moves and its lifting as `stroke` says, made here (WKWebView, or Chromium's synthetic run); `click` sends WebKit's click after it. */
    async function press(step) {
        const kind = step.pointer;
        const id = run.pointer++;
        const { down, moves, atOnce, up: last } = stroke(step);
        const pressed = send("pointerdown", kind, id, down, 1);
        if (step.hold) {
            await sleep(step.hold);
        }
        for (const [index, move] of moves.entries()) {
            send("pointermove", kind, id, move, 1);
            if (index + 1 >= atOnce) {
                await frame();
            }
        }
        const target = captured.get(id);
        send("pointerup", kind, id, last, 0);
        if (target !== undefined) {
            captured.delete(id);
            target.dispatchEvent(pointerEvent("lostpointercapture", kind, id, last, 0));
        }
        if (step.click) {
            await frame();
            const under = document.elementFromPoint(last.x, last.y) ?? pressed;
            under.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, composed: true, clientX: last.x, clientY: last.y, detail: 1 }));
        }
        await frame();
    }

    /**
     * Whether the step's last press was what the step says on the page's clock (run.felt): a press with no `hold` left the
     * page's hold (HOLD_MS and HOLD_SLOP of selection-gesture.ts, read from the page's own module) before it ran out, and a
     * press with a `hold` waited it out. Null when it was; otherwise the timing fault, which says the machine was too slow
     * for the scenario rather than that the page changed.
     */
    function timing(index) {
        const step = run.scenario.steps[index];
        const down = run.felt.findLastIndex((event) => event.type === "pointerdown");
        if (down < 0) {
            return `timing fault: ${label(index)} pressed, but the page felt no pointerdown.`;
        }
        const start = run.felt[down];
        const left = run.felt.slice(down + 1).find((event) => event.type !== "pointermove" || Math.hypot(event.x - start.x, event.y - start.y) > run.hold.slop);
        const lasted = Math.round((left?.at ?? performance.now()) - start.at);
        if (!step.hold && lasted >= run.hold.ms) {
            return `timing fault: ${label(index)} is a press with no hold, but the page felt it held still for ${lasted} ms, past its hold (${run.hold.ms} ms): the machine was too slow for the scenario, so the page took a hold and its rules were not tested. Run it again on a less loaded machine.`;
        }
        if (step.hold && lasted < run.hold.ms) {
            return `timing fault: ${label(index)} holds for ${step.hold} ms, but the page felt it held still for ${lasted} ms, short of its hold (${run.hold.ms} ms).`;
        }
        return null;
    }

    // ---- Where a step presses ----------------------------------------------------------------------------------------

    /**
     * The bars of Expand's card, in window pixels and in time order: each bar's staff lines as the card draws them, from
     * its left to its right and from its top line to its bottom one, as `motiff.frames()` boxes the bars of the pages.
     */
    function cardBars() {
        return [...document.querySelectorAll("#focus .card-score g.measure")].map((measure) => {
            const lines = [...measure.querySelectorAll(":scope > g.staff > path")].map((line) => line.getBoundingClientRect());
            return {
                left: Math.min(...lines.map((line) => line.left)), top: Math.min(...lines.map((line) => line.top)),
                right: Math.max(...lines.map((line) => line.right)), bottom: Math.max(...lines.map((line) => line.bottom)),
            };
        });
    }

    /**
     * A point on the score, in window pixels: the nth bar on the pages (from 1), or with `card` the nth bar of Expand's
     * card, `x` across it and `y` down its ink, from 0 to 1 (below 0 or past 1 is beside it); or the handle of the
     * selection's `edge` ("start" or "end"), or of a playhead (`grip`: "black" or "blue"), or a marker over the bars
     * (`marker`, `markerPoint`), or a phrase's number or a section's letter (`phrase`, `section`, `phrasePoint`), as the page
     * draws it, moved by `dx` and `dy` pixels.
     */
    function scorePoint(point) {
        if ("marker" in point) {
            return markerPoint(point);
        }
        if ("phrase" in point || "section" in point) {
            return phrasePoint(point);
        }
        if ("card" in point) {
            const bar = cardBars()[point.card - 1];
            if (bar === undefined) {
                throw new Error(`There is no bar ${point.card} on Expand's card.`);
            }
            return { x: bar.left + (bar.right - bar.left) * point.x + (point.dx ?? 0), y: bar.top + (bar.bottom - bar.top) * point.y + (point.dy ?? 0) };
        }
        if ("edge" in point || "grip" in point) {
            const drawn = "edge" in point
                ? scoreMarks.edges().filter((each) => each.edge === point.edge).map((each) => each.handle)
                : scoreMarks.grips().filter((each) => each.handle === point.grip);
            if (drawn.length === 0) {
                throw new Error(`The page draws no ${"edge" in point ? `${point.edge} edge` : `${point.grip} playhead's handle`}.`);
            }
            return { x: drawn[0].x + (point.dx ?? 0) - window.scrollX, y: drawn[0].y + (point.dy ?? 0) - window.scrollY };
        }
        const bar = window.motiff.frames()[point.bar - 1];
        if (bar === undefined) {
            throw new Error(`There is no bar ${point.bar} on the pages.`);
        }
        return { x: bar.left + (bar.right - bar.left) * point.x - window.scrollX, y: bar.top + (bar.bottom - bar.top) * point.y - window.scrollY };
    }

    /**
     * The centre of the first marker over the bars of a kind (`marker`: "version", "place", "technique" or "seam", as
     * `scoreMarks.markers` reads them), of the bar `bar` when given, moved by `dx` and `dy` pixels; window pixels.
     */
    function markerPoint(point) {
        const drawn = scoreMarks.markers().filter((each) => each.kind === point.marker && (point.bar === undefined || each.bar === point.bar));
        if (drawn.length === 0) {
            throw new Error(`The page draws no ${point.marker} marker${point.bar === undefined ? "" : ` of bar ${point.bar}`}.`);
        }
        const box = drawn[0];
        return { x: (box.left + box.right) / 2 + (point.dx ?? 0) - window.scrollX, y: (box.top + box.bottom) / 2 + (point.dy ?? 0) - window.scrollY };
    }

    /**
     * The centre of the touch target of a phrase's number (`phrase`, its number) or of a section's letter (`section`, its id)
     * as the page draws it (v1.2), moved by `dx` and `dy` pixels; window pixels.
     */
    function phrasePoint(point) {
        const drawn = scoreMarks.phraseTargets().filter((each) => ("phrase" in point ? each.phrase === point.phrase : each.section === point.section));
        if (drawn.length === 0) {
            throw new Error(`The page draws no ${"phrase" in point ? `number of phrase ${point.phrase}` : `letter of section ${point.section}`}.`);
        }
        const box = drawn[0];
        return { x: (box.left + box.right) / 2 + (point.dx ?? 0) - window.scrollX, y: (box.top + box.bottom) / 2 + (point.dy ?? 0) - window.scrollY };
    }

    /**
     * The roll's notes canvas and the x of a tick on it, from its zoom and the left edge of its view (`roll.frame()`) and
     * where it draws its playhead's control: the room left of the notes (the gutter) is read once, the first time the
     * playhead stands in view (as it does when a roll opens), and kept for the scenario. Read later it could be wrong: the
     * control's value is the player's tick, but it is drawn where the line is, which shows a place let go until the player
     * reports it (playhead-drag.ts, PlayheadDrag.heard).
     */
    function rollAxis() {
        const notes = document.querySelector("#roll canvas.roll-notes").getBoundingClientRect();
        const view = window.roll.frame();
        const perTick = view.zoom / 480;
        if (run.gutter === undefined) {
            const head = document.querySelector("#roll .roll-playhead");
            const box = head.getBoundingClientRect();
            const at = box.left + box.width / 2 - notes.left;
            // Out of view the control is held at the view's edge: then it says nothing of where ticks are.
            if (Number(head.value) >= view.scroll && at < notes.width - 1) {
                run.gutter = at - (Number(head.value) - view.scroll) * perTick;
            }
        }
        if (run.gutter === undefined) {
            throw new Error("The roll's playhead was never in view, so a tick has no place on it yet.");
        }
        return { notes, x: (tick) => notes.left + run.gutter + (tick - view.scroll) * perTick };
    }

    /**
     * A point on the roll: a tick across it and `y` down its notes, from 0 to 1; or the playhead's handle in the strip
     * (`grip`), the centre of its control, which the roll keeps over the handle (roll-playhead.ts PlayheadControl.place);
     * window pixels.
     */
    function rollPoint(point) {
        if ("grip" in point) {
            return gripPoint();
        }
        const axis = rollAxis();
        return { x: axis.x(point.tick), y: axis.notes.top + axis.notes.height * point.y };
    }

    /**
     * The centre of the roll's playhead control, only where it stands at the tick the app last reported (`position`). After
     * a drag the roll shows the place let go until the player reports it or 800 ms pass (playhead-drag.ts PlayheadDrag.heard):
     * a grip read there races that timer, as the browser's own input presses a moment after the point is read. So a scenario
     * sends the app's answer to a drag, and a grip read before it stops the scenario instead.
     */
    function gripPoint() {
        const box = document.querySelector("#roll .roll-playhead").getBoundingClientRect();
        const centre = { x: box.left + box.width / 2, y: box.top + box.height / 2 };
        const reported = run.reported === undefined ? undefined : rollAxis().x(run.reported);
        if (reported === undefined || Math.abs(centre.x - reported) > GRIP_SLACK) {
            throw new Error(`The roll's playhead control is not where the app last reported it (${run.reported ?? "no position yet"}): send the app's answer to the drag before pressing its grip.`);
        }
        return centre;
    }

    function points(step) {
        return step.path.map((point) => (run.page === "roll" ? rollPoint(point) : scorePoint(point)));
    }

    // ---- What the page drew ------------------------------------------------------------------------------------------

    const round = (value) => Math.round(value);
    function boxOf(element) {
        const box = element.getBoundingClientRect();
        return { left: round(box.left + window.scrollX), top: round(box.top + window.scrollY), right: round(box.right + window.scrollX), bottom: round(box.bottom + window.scrollY) };
    }
    const shown = (element) => !element.hidden && element.isConnected && getComputedStyle(element).visibility !== "hidden" && element.getClientRects().length > 0;
    // A phrase's mark as each part is drawn: its kind (comma, letter or number), its phrase (null for a section's letter
    // where no phrase starts), its text, whether it is the player's own, the selected phrase's number or the phrase playing,
    // and its box.
    const phraseInks = (selector) => [...document.querySelectorAll(selector)].filter(shown).map((ink) => ({
        kind: ["comma", "letter", "number"].find((kind) => ink.classList.contains(kind)),
        phrase: ink.dataset.phrase === undefined ? null : Number(ink.dataset.phrase),
        text: ink.textContent,
        own: ink.classList.contains("own"),
        selected: ink.classList.contains("selected"),
        current: ink.classList.contains("current"),
        ...boxOf(ink),
    }));

    /** The pages, each with the room above its first ink and below its last (the paper's margins), and the room above and below them. */
    function paper() {
        const pages = [...document.querySelectorAll(PAGE)].map(boxOf);
        const frames = window.motiff.frames();
        const sheets = pages.map((page) => {
            const on = frames.filter((bar) => bar.top >= page.top && bar.bottom <= page.bottom);
            return { ...page, inkTop: round(Math.min(...on.map((bar) => bar.top)) - page.top), inkBottom: round(page.bottom - Math.max(...on.map((bar) => bar.bottom))) };
        });
        const height = document.documentElement.scrollHeight;
        return { pages: sheets, above: pages[0]?.top ?? 0, below: height - (pages.at(-1)?.bottom ?? 0) };
    }

    /**
     * Expand's card as it shows the passage: the box its notes scroll in, the room in it above its first bar's top line
     * and below its last bar's bottom line, and how far its notes are scrolled; null when no card is open.
     */
    function card() {
        const score = document.querySelector("#focus .card-score");
        if (score === null || !shown(score)) {
            return null;
        }
        const box = boxOf(score);
        const bars = cardBars().map((bar) => ({ top: bar.top + window.scrollY, bottom: bar.bottom + window.scrollY }));
        return {
            ...box,
            inkTop: round(Math.min(...bars.map((bar) => bar.top)) - box.top),
            inkBottom: round(box.bottom - Math.max(...bars.map((bar) => bar.bottom))),
            scroll: round(score.scrollTop),
        };
    }

    /** The centre of an element's pseudo-element as drawn, in the document's pixels: its place, its size and its transform. */
    function drawnCentre(element, pseudo) {
        const style = getComputedStyle(element, pseudo);
        const box = element.getBoundingClientRect();
        const shift = new DOMMatrixReadOnly(style.transform === "none" ? undefined : style.transform);
        const px = (value) => Number.parseFloat(value) || 0;
        return {
            x: box.left + window.scrollX + px(style.left) + px(style.width) / 2 + shift.e,
            y: box.top + window.scrollY + px(style.top) + px(style.height) / 2 + shift.f,
        };
    }

    const scoreMarks = {
        band: () => [...document.querySelectorAll("body > .sel-band")].filter(shown).map(boxOf),
        // The selection's edges, each with the centre of its handle as drawn (score.css .sel-edge::after, the kit's hollow circle).
        edges: () => [...document.querySelectorAll("body > .sel-edge")].filter(shown).map((edge) => {
            const box = boxOf(edge);
            const handle = drawnCentre(edge, "::after");
            return { edge: edge.classList.contains("start") ? "start" : "end", x: box.left, top: box.top, bottom: box.bottom, handle: { x: round(handle.x), y: round(handle.y) } };
        }),
        handles: () => [...document.querySelectorAll("body > .sel-handle")].filter(shown).map((handle) => {
            const box = boxOf(handle);
            return { x: round((box.left + box.right) / 2), y: round((box.top + box.bottom) / 2) };
        }),
        // The playheads' handles: the black dot under its bar, the cobalt knob of the line inside the selection.
        grips: () => [["black", ".gtrack .dot"], ["blue", ".ph-sel .knob"]].flatMap(([handle, selector]) => [...document.querySelectorAll(selector)].filter(shown).map((grip) => {
            const box = boxOf(grip);
            return { handle, x: round((box.left + box.right) / 2), y: round((box.top + box.bottom) / 2) };
        })),
        // The labels over the bars (`performance`): another version's, and a draft's place labels, приём numbers and seam
        // diamonds (v1.1), in the order the page draws them, each with its bar, its words, its state and its box.
        markers: () => [...document.querySelectorAll("body > .perf-marker")].filter(shown).map((marker) => ({
            kind: ["place", "technique", "seam"].find((kind) => marker.classList.contains(kind)) ?? "version",
            bar: Number(marker.dataset.bar),
            text: marker.textContent,
            state: [...marker.classList].find((name) => name.startsWith("state-"))?.slice("state-".length) ?? null,
            ...boxOf(marker),
        })),
        // The phrases' marks (v1.2), each part drawn in the order the page draws them (`phraseInks`).
        phrases: () => phraseInks("body > .phrase-ink"),
        // The same marks over the card's bars in focus (v1.2 i-3b), only to read.
        cardPhrases: () => phraseInks("#focus .card-score > .phrase-ink"),
        // The touch targets of the phrases' numbers and the sections' letters: which each is, what it says, and its box.
        phraseTargets: () => [...document.querySelectorAll("body > .phrase-mark")].filter(shown).map((target) => ({
            phrase: target.dataset.phrase === undefined ? null : Number(target.dataset.phrase),
            section: target.dataset.section ?? null,
            label: target.getAttribute("aria-label"),
            ...boxOf(target),
        })),
        paper,
        card,
        scroll: () => ({ x: round(window.scrollX), y: round(window.scrollY) }),
    };

    const rollMarks = {
        // The zoom and the view: pixels a quarter, the rows' scale, the left edge in ticks, the highest row.
        frame: () => {
            const kept = window.roll.frame();
            return kept === null ? null : { zoom: Math.round(kept.zoom * 100) / 100, rowScale: Math.round(kept.rowScale * 100) / 100, scroll: round(kept.scroll), pitchTop: kept.pitchTop };
        },
        playhead: () => Number(document.querySelector("#roll .roll-playhead")?.value),
    };

    function marks(names) {
        const readers = run.page === "roll" ? rollMarks : scoreMarks;
        return Object.fromEntries(names.map((name) => {
            if (readers[name] === undefined) {
                throw new Error(`The ${run.page} page has no mark "${name}"; it has ${Object.keys(readers).join(", ")}.`);
            }
            return [name, readers[name]()];
        }));
    }

    // ---- Comparing -----------------------------------------------------------------------------------------------------

    const kindOf = (message) => (typeof message === "string" ? message : Object.keys(message)[0]);
    const text = (value) => JSON.stringify(value);

    /** Where `got` differs from what `want` names (keys it does not name are not compared; arrays must be as long). */
    function differences(want, got, path, tolerance, out) {
        if (typeof want === "number" && typeof got === "number") {
            if (Math.abs(want - got) > tolerance) {
                out.push(`${path}: expected ${want}, got ${got}`);
            }
        } else if (Array.isArray(want)) {
            if (!Array.isArray(got) || got.length !== want.length) {
                out.push(`${path}: expected ${want.length} items ${text(want)}, got ${text(got)}`);
            } else {
                want.forEach((item, index) => differences(item, got[index], `${path}[${index}]`, tolerance, out));
            }
        } else if (want !== null && typeof want === "object") {
            if (got === null || typeof got !== "object") {
                out.push(`${path}: expected ${text(want)}, got ${text(got)}`);
            } else {
                for (const key of Object.keys(want)) {
                    differences(want[key], got[key], `${path}.${key}`, tolerance, out);
                }
            }
        } else if (want !== got) {
            out.push(`${path}: expected ${text(want)}, got ${text(got)}`);
        }
        return out;
    }

    /** Whether the messages `want` were posted in this order, others between them allowed. */
    function included(want, got, where, tolerance, out) {
        let from = 0;
        for (const [index, message] of want.entries()) {
            const at = got.findIndex((each, position) => position >= from && differences(message, each, "", tolerance, []).length === 0);
            if (at < 0) {
                out.push(`${where}: expected ${text(message)} (${index + 1} of ${want.length} in order), got ${text(got.slice(from))}`);
                return;
            }
            from = at + 1;
        }
    }

    /** The step's expectations against what the page posted since the expectation before, and what it draws now. */
    function check(expect, label) {
        const tolerance = expect.tolerance ?? DEFAULT_TOLERANCE;
        const posted = run.posts.slice(run.read);
        run.read = run.posts.length;
        const out = [];
        run.seen = { label, posted };
        for (const want of [expect.posted ?? []].flat()) {
            const got = posted.filter((message) => want.kinds.includes(kindOf(message)));
            const where = `${label} posted (${want.kinds.join(", ")})`;
            if (want.includes !== undefined) {
                included(want.includes, got, where, tolerance, out);
            } else {
                differences(want.messages, got, where, tolerance, out);
            }
        }
        if (expect.marks !== undefined) {
            run.seen.marks = marks(Object.keys(expect.marks));
            differences(expect.marks, run.seen.marks, `${label} marks`, tolerance, out);
        }
        if (expect.returned !== undefined) {
            differences(expect.returned, run.returned, `${label} returned`, tolerance, out);
        }
        return out;
    }

    // ---- Steps ---------------------------------------------------------------------------------------------------------

    /** A step's argument with "$fixture.<path>" read from the fixture. */
    function filled(value) {
        if (typeof value === "string" && value.startsWith("$fixture.")) {
            return value.slice("$fixture.".length).split(".").reduce((at, key) => at?.[key], run.fixture);
        }
        if (Array.isArray(value)) {
            return value.map(filled);
        }
        if (value !== null && typeof value === "object") {
            return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, filled(item)]));
        }
        return value;
    }

    async function call(step) {
        const page = run.page === "roll" ? window.roll : window.motiff;
        if (typeof page?.[step.call] !== "function") {
            throw new Error(`The ${run.page} page has no command "${step.call}".`);
        }
        run.returned = await ("with" in step ? page[step.call](filled(step.with)) : page[step.call]());
        await frame();
        if (run.page === "roll") {
            if (step.call === "position") {
                run.reported = filled(step.with).tick;
            }
            noteGutter();
        }
    }

    /** Reads the roll's gutter once its playhead is in view, for the points of a later step. */
    function noteGutter() {
        try {
            rollAxis();
        } catch {
            // Not drawn yet, or the playhead not yet in view.
        }
    }

    const label = (index) => `step ${index + 1}${run.scenario.steps[index].note ? ` (${run.scenario.steps[index].note})` : ""}`;

    window.pageScenario = {
        /** Starts a scenario on this page: the messages are read from now on. */
        async begin(scenario, fixture) {
            // The page's own hold, from the module its pages import (Scripts/score/selection-gesture.ts).
            const { HOLD_MS, HOLD_SLOP } = await import(new URL("../score/selection-gesture.js", document.baseURI).href);
            run.hold = { ms: HOLD_MS, slop: HOLD_SLOP };
            run.felt = [];
            run.scenario = scenario;
            run.fixture = fixture;
            run.page = scenario.page;
            run.posts = [];
            run.read = 0;
            run.gutter = undefined;
            run.reported = undefined;
            standIn();
            return scenario.steps.length;
        },
        /** What the last expectation saw: everything posted since the one before, and the marks it named (run.mjs --show). */
        seen: () => run.seen,
        /** How a pointer step presses (`stroke`), in window pixels, for a runner that presses with the browser's own input. */
        stroke: (index) => stroke(run.scenario.steps[index]),
        /** After a pointer step pressed with the browser's own input: its timing fault, or null (timing). */
        timing,
        /** Carries out one step; for an expectation, the differences found. */
        async step(index) {
            const step = run.scenario.steps[index];
            if ("call" in step) {
                await call(step);
            } else if ("pointer" in step) {
                await press(step);
                const fault = timing(index);
                if (fault !== null) {
                    throw new Error(fault);
                }
            } else if ("wait" in step) {
                await sleep(step.wait);
            } else if ("expect" in step) {
                await document.fonts.ready;
                return check(step.expect, label(index));
            } else {
                throw new Error(`${label(index)} is no call, pointer, wait or expect.`);
            }
            return [];
        },
        /** The whole scenario with pointer events made here: every difference, or the error that stopped it. */
        async play(scenario, fixture) {
            const differences = [];
            try {
                const count = await this.begin(scenario, fixture);
                for (let index = 0; index < count; index++) {
                    differences.push(...(await this.step(index)));
                }
            } catch (error) {
                differences.push(`stopped: ${error?.message ?? error}`);
            }
            return differences;
        },
    };
})();
