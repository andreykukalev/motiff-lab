// Measures how the web app answers on the large-score benchmark (#419): starting the app, opening the piece (until its first
// page is on screen, and until its phrases are read and it is ready), scrolling its pages, the playhead moving while it
// plays, a bar selected, the piano roll opened and Expand. Run on a published app:
//   dotnet publish src/MotiffLab.Web -c Release -o publish
//   (cd tests/e2e && npm ci)
//   node tools/perf/measure.mjs [publish dir ...] [--runs 3] [--profile out-dir]
// Prints a table of medians over the runs for each build, in milliseconds; given two builds (before and after a change),
// their runs take turns, so a machine busy with other work weighs on both alike. Frames are timed with requestAnimationFrame: "frame p95"
// and "frame max" are the slowest frames while it ran, "long" the main thread's tasks over 50 ms added up. The numbers
// depend on the machine; compare builds on the same one. --profile writes a Chrome CPU profile of each step there.

import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const require = createRequire(join(root, "tests/e2e/package.json"));
const { chromium } = require("playwright");

const PIECE = join(root, "tests/e2e/fixtures/large-score.mid");
const FIRST_PORT = 5193;
const VIEWPORT = { width: 1280, height: 800 };

function options(argv) {
    const result = { publish: [], runs: 3, profile: null };
    for (let i = 0; i < argv.length; i++) {
        if (argv[i] === "--runs") {
            result.runs = Number(argv[++i]);
        } else if (argv[i] === "--profile") {
            result.profile = resolve(argv[++i]);
        } else {
            result.publish.push(resolve(argv[i]));
        }
    }
    if (result.publish.length === 0) {
        result.publish.push(resolve(root, "publish"));
    }
    return result;
}

/** Watches the main thread from the page's start: long tasks, and the frames while a step runs. */
function watcher() {
    const state = { long: [], frames: null };
    window.__perf = state;
    new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) {
            state.long.push({ start: entry.startTime, duration: entry.duration });
        }
    }).observe({ type: "longtask", buffered: true });
    state.startFrames = () => {
        state.frames = [];
        let last = performance.now();
        const step = (now) => {
            if (state.frames === null) {
                return;
            }
            state.frames.push(now - last);
            last = now;
            requestAnimationFrame(step);
        };
        requestAnimationFrame(step);
    };
    state.stopFrames = () => {
        const frames = state.frames ?? [];
        state.frames = null;
        return frames;
    };
}

/** Resolves once the main thread has had no long task for a while: the step has settled. */
async function settled(page, quietMs = 1000, timeoutMs = 180_000) {
    const start = Date.now();
    for (;;) {
        const last = await page.evaluate(() => {
            const long = window.__perf.long;
            const end = long.length === 0 ? 0 : Math.max(...long.map((task) => task.start + task.duration));
            return performance.now() - end;
        });
        if (last >= quietMs || Date.now() - start > timeoutMs) {
            return;
        }
        await page.waitForTimeout(Math.max(50, quietMs - last));
    }
}

/** The long tasks between two of the page's times, added up. */
function longBetween(page, from, to) {
    return page.evaluate(([a, b]) => window.__perf.long.filter((task) => task.start >= a && task.start < b).reduce((sum, task) => sum + task.duration, 0), [from, to]);
}

function percentile(values, share) {
    if (values.length === 0) {
        return 0;
    }
    const sorted = [...values].sort((a, b) => a - b);
    return sorted[Math.min(sorted.length - 1, Math.floor(share * sorted.length))];
}

/** Runs a step: its wall time to `done`, the long tasks and the frames in it, and a CPU profile when asked. */
async function step(page, session, name, profileDir, action) {
    if (profileDir !== null) {
        await session.send("Profiler.enable");
        await session.send("Profiler.setSamplingInterval", { interval: 200 });
        await session.send("Profiler.start");
    }
    const from = await page.evaluate(() => {
        window.__perf.startFrames();
        return performance.now();
    });
    const wall = Date.now();
    await action();
    const elapsed = Date.now() - wall;
    await settled(page, 500);
    const to = await page.evaluate(() => performance.now());
    const frames = await page.evaluate(() => window.__perf.stopFrames());
    if (profileDir !== null) {
        const { profile } = await session.send("Profiler.stop");
        writeFileSync(join(profileDir, `${name}.cpuprofile`), JSON.stringify(profile));
    }
    return { time: elapsed, long: await longBetween(page, from, to), p95: percentile(frames, 0.95), max: percentile(frames, 1) };
}

/** Waits two frames, so what the step changed has been painted. */
function painted(page) {
    return page.evaluate(() => new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(() => done()))));
}

// The steps find and wait for elements with the page's own querySelector: Playwright's locators match in script over the
// whole document, which on a score of tens of thousands of elements takes longer than the app does, and is not its time.

/** Waits, frame by frame, for an element matching a selector. */
function appears(page, selector, timeout = 120_000) {
    return page.waitForFunction((css) => document.querySelector(css) !== null, selector, { polling: "raf", timeout });
}

/**
 * Waits until the piece's phrases are read: the Phrases popover counts them then. The score draws no phrase brackets to
 * wait for (#479 item 14), so the popover is opened, read and put away again.
 */
async function phrasesRead(page, timeout = 120_000) {
    await click(page, '.bar button[aria-label="Phrases"]');
    await page.waitForFunction(() => (document.querySelector(".phrases-popover .caption")?.textContent.trim() ?? "") !== "", null, { polling: "raf", timeout });
    // A tap beside the popover, on its scrim, puts it away (tests/e2e/steps.mjs closePopover).
    await page.mouse.click(4, 300);
    await page.waitForFunction(() => document.querySelector(".phrases-popover") === null, null, { polling: "raf" });
}

/** Clicks the middle of the first element matching a selector (and holding the text, when given) with the mouse. */
async function click(page, selector, text = null) {
    const point = await page.evaluate(([css, words]) => {
        const element = [...document.querySelectorAll(css)].find((candidate) => words === null || candidate.textContent.includes(words));
        const box = element?.getBoundingClientRect();
        return box === undefined ? null : { x: box.left + box.width / 2, y: box.top + box.height / 2 };
    }, [selector, text]);
    if (point === null) {
        throw new Error(`Nothing to click: ${selector}`);
    }
    await page.mouse.click(point.x, point.y);
}

/** Starts the app on the library and opens Add piece. */
async function boot(page, base) {
    await page.goto(base);
    await page.getByRole("button", { name: "Add piece" }).click();
}

/**
 * Adds the piece and waits until its phrases are read: the piece is ready to work on. When its first page was
 * on screen, and the main thread's long tasks until then, go in `shown`.
 */
async function openPiece(page, shown) {
    const picked = Date.now();
    const from = await page.evaluate(() => performance.now());
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(PIECE);
    await page.waitForURL(/\/playback\?project=/, { timeout: 180_000 });
    // Found by a frame's poll, so it is on screen: two more frames would wait behind the work that follows it.
    await appears(page, ".score-page svg g.measure", 300_000);
    shown.time = Date.now() - picked;
    shown.long = await longBetween(page, from, await page.evaluate(() => performance.now()));
    await phrasesRead(page, 300_000);
    await painted(page);
    shown.picked = picked;
}

/** Waits until the pages away from the view are drawn too, which they are after the piece is ready (#419). */
async function allDrawn(page) {
    await page.waitForFunction(() => document.querySelector(".score > .score-page:not(:has(> svg))") === null, null, { polling: 100, timeout: 300_000 });
}

/** Scrolls the score a screen at a time, a frame apart, to the end of the pages and back a third of the way. */
async function scrollPages(page) {
    await page.evaluate(async () => {
        const host = document.querySelector(".score.paged") ?? document.querySelector(".score");
        const frame = () => new Promise((done) => requestAnimationFrame(() => done()));
        const end = host.scrollHeight - host.clientHeight;
        for (let top = host.scrollTop; top < end; top += 300) {
            host.scrollTop = top;
            await frame();
        }
        for (let i = 0; i < 60; i++) {
            host.scrollTop -= 300;
            await frame();
        }
    });
}

/** Taps a bar in the middle of the page in view, on its top staff line, and waits for the selection's band. */
async function selectBar(page) {
    await page.evaluate(() => {
        const host = document.querySelector(".score");
        const view = host.getBoundingClientRect();
        const middle = (view.top + view.bottom) / 2;
        const sheet = [...host.querySelectorAll(":scope > .score-page")].find((candidate) => {
            const box = candidate.getBoundingClientRect();
            return box.top <= middle && box.bottom > middle;
        });
        const measures = [...sheet.querySelectorAll("svg g.measure")];
        const line = measures[Math.floor(measures.length / 2)].querySelector("g.staff > path");
        line.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    });
    await appears(page, ".score .sel-band");
    await painted(page);
}

/** Plays for a few seconds while the playhead moves (over the pages, or in focus over the roll), then pauses. */
async function play(page, playhead = ".score .playhead:not([hidden])") {
    await click(page, ".pill button.play");
    if (playhead !== null) {
        await appears(page, playhead, 30_000);
    }
    await page.waitForTimeout(5000);
    await click(page, ".pill button.play");
}

/**
 * Plays from the last bar of a system in view until the score has turned to the next system and a second more (#433), then
 * pauses: the frames at the turn's start are where it hitched.
 */
async function turn(page) {
    const top = await page.evaluate(() => {
        const host = document.querySelector(".score");
        const view = host.getBoundingClientRect();
        // The last system wholly in view: the next runs off the bottom, so playing into it turns the score.
        const system = [...host.querySelectorAll("svg g.system")].filter((candidate) => {
            const box = candidate.getBoundingClientRect();
            return box.top >= view.top + 120 && box.bottom <= view.bottom;
        }).at(-1);
        const measures = system.querySelectorAll("g.measure");
        const note = measures[measures.length - 1].querySelector("g.note");
        note.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
        return host.scrollTop;
    });
    await page.waitForTimeout(300);
    await click(page, '.bar button[aria-label="Play"]');
    await page.waitForFunction((from) => Math.abs(document.querySelector(".score").scrollTop - from) > 20, top, { timeout: 30_000 });
    await page.waitForTimeout(1500);
    await click(page, '.bar button[aria-label="Pause"]');
}

/**
 * Für Elise in phrase focus with its notes, playing from bar 6: the card turns to phrase 3 at bar 9 (#433). The step is
 * the turn and a second after it.
 */
async function focusOnPlaying(page, base) {
    await page.goto(`${base}/playback`);
    await page.getByRole("button", { name: "Für Elise" }).click();
    await phrasesRead(page);
    await page.waitForTimeout(1000);
    await page.evaluate(() => {
        const measure = document.querySelectorAll(".score svg g.measure")[5];
        measure.querySelector("g.note").dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    });
    await page.waitForTimeout(300);
    await click(page, '.bar button[aria-label="Play"]');
    await click(page, 'header.bar button[aria-label="Piano roll"]');
    await appears(page, ".roll-drawer canvas");
    await click(page, ".roll-drawer .chip", "Expand");
    await appears(page, ".focus-card");
    await click(page, '.bar [role="radiogroup"] button', "Notes");
    await appears(page, ".focus-card .phrase-card-score svg");
    return page.evaluate(() => document.querySelector(".bar .crumb").textContent);
}

async function focusTurn(page, shown) {
    await page.waitForFunction((was) => document.querySelector(".bar .crumb").textContent !== was, shown, { timeout: 30_000 });
    await page.waitForTimeout(1000);
    await click(page, '.bar button[aria-label="Pause"]');
}

async function openRoll(page) {
    await click(page, 'header.bar button[aria-label="Piano roll"]');
    await appears(page, ".roll-drawer canvas");
    await painted(page);
}

async function expand(page) {
    await click(page, ".roll-drawer .chip", "Expand");
    await appears(page, ".focus-roll canvas");
    await painted(page);
}

async function run(browser, base, profileDir) {
    const context = await browser.newContext({
        viewport: VIEWPORT,
        serviceWorkers: "block",
        storageState: { cookies: [], origins: [{ origin: base, localStorage: [{ name: "motiff-lab:settings.presetChosen", value: "on" }] }] },
    });
    await context.addInitScript(watcher);
    const page = await context.newPage();
    const session = await context.newCDPSession(page);
    const dir = profileDir;
    if (dir !== null) {
        mkdirSync(dir, { recursive: true });
    }
    const result = {};
    result.boot = await step(page, session, "boot", dir, () => boot(page, base));
    const shown = {};
    result.ready = await step(page, session, "open", dir, () => openPiece(page, shown));
    result.open = { time: shown.time, long: shown.long, p95: 0, max: 0 };
    result.drawn = await step(page, session, "drawn", dir, () => allDrawn(page));
    // From the file picked, like open and ready.
    result.drawn.time = Date.now() - shown.picked;
    result.dom = await page.evaluate(() => document.getElementsByTagName("*").length);
    result.pages = await page.locator(".score-page").count();
    result.scroll = await step(page, session, "scroll", dir, () => scrollPages(page));
    result.turn = await step(page, session, "turn", dir, () => turn(page));
    result.select = await step(page, session, "select", dir, () => selectBar(page));
    result.play = await step(page, session, "play", dir, () => play(page));
    result.roll = await step(page, session, "roll", dir, () => openRoll(page));
    result.expand = await step(page, session, "expand", dir, () => expand(page));
    result.focusPlay = await step(page, session, "focus-play", dir, () => play(page, null));
    result.heap = Math.round((await page.evaluate(() => performance.memory?.usedJSHeapSize ?? 0)) / 1e6);
    const phrase = await focusOnPlaying(page, base);
    result.focusTurn = await step(page, session, "focus-turn", dir, () => focusTurn(page, phrase));
    await context.close();
    return result;
}

function median(values) {
    return percentile(values, 0.5);
}

function table(results) {
    const rows = ["boot", "open", "ready", "drawn", "scroll", "turn", "select", "play", "roll", "expand", "focusPlay", "focusTurn"].map((name) => {
        const pick = (key) => Math.round(median(results.map((result) => result[name][key])));
        return `| ${name} | ${pick("time")} | ${pick("long")} | ${pick("p95")} | ${pick("max")} |`;
    });
    return [
        `Pages ${median(results.map((r) => r.pages))}, DOM elements after opening ${median(results.map((r) => r.dom))}, JS heap at the end ${median(results.map((r) => r.heap))} MB`,
        "",
        "| step | wall ms | long tasks ms | frame p95 ms | frame max ms |",
        "| --- | ---: | ---: | ---: | ---: |",
        ...rows,
    ].join("\n");
}

const settings = options(process.argv.slice(2));
const builds = settings.publish.map((publish, index) => ({ publish, base: `http://localhost:${FIRST_PORT + index}`, results: [] }));
const servers = builds.map((build, index) =>
    spawn(process.execPath, [join(root, "tests/e2e/serve.mjs"), join(build.publish, "wwwroot"), String(FIRST_PORT + index)], { stdio: "ignore" }));
try {
    await new Promise((done) => setTimeout(done, 800));
    const browser = await chromium.launch();
    for (let i = 0; i < settings.runs; i++) {
        for (const [index, build] of builds.entries()) {
            const profile = settings.profile === null ? null : join(settings.profile, `build-${index + 1}`, `run-${i + 1}`);
            build.results.push(await run(browser, build.base, profile));
            console.error(`${build.publish} run ${i + 1}: ${JSON.stringify(build.results.at(-1))}`);
        }
    }
    await browser.close();
    for (const build of builds) {
        console.log(`\n${build.publish}\n`);
        console.log(table(build.results));
    }
} finally {
    for (const server of servers) {
        server.kill();
    }
}
