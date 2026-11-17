// The page scenarios on Linux (tests/contracts/page-scenarios/README.md): the iPad's real pages, src/ipad/App/Web/score.html
// and roll.html over the compiled shared modules, served as the app's bundle lays them out, in Chromium. Every scenario
// runs twice: pressed with the browser's own input (Chrome DevTools Protocol: a pen, a finger or a mouse as a device
// would give them) and with the pointer events the replayer makes, as the WKWebView replay presses
// (PageScenarioTests.swift). Fails with each difference. After `dotnet build src/shared`:
//   node tests/pages/run.mjs [--show] [scenario-id ...]
// --show prints what each expectation saw (all the messages, the marks it names), to write a scenario from what the page does.
// The browser is Playwright's Chromium, or with PAGES_CHANNEL=chrome the Chrome installed on the machine (CI). Meanwhile
// `fixture.mjs --check` makes the fixtures again and fails the run when a committed one is not what the code makes now.
import { execFile } from "node:child_process";
import { readFileSync, readdirSync } from "node:fs";
import { createServer } from "node:http";
import { basename, extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import { chromium } from "playwright-core";

const repo = fileURLToPath(new URL("../../", import.meta.url));
const scenarios = join(repo, "tests/contracts/page-scenarios");
const app = join(repo, "src/ipad/App");
const TYPES = { ".html": "text/html", ".js": "text/javascript", ".css": "text/css", ".png": "image/png", ".woff2": "font/woff2", ".json": "application/json" };

/** What the app bundles at the root of its resources, as project.yml lists the app's sources: each by its own name. */
function bundle() {
    const yml = readFileSync(join(app, "project.yml"), "utf8");
    const target = yml.slice(yml.indexOf("\n  MotiffLab:\n"), yml.indexOf("\n  MotiffLabTests:\n"));
    return new Map([...target.matchAll(/^\s+- path: (\S+)\s*$/gm)].map((match) => [basename(match[1]), join(app, match[1])]));
}

/** Serves the bundle as the app's scheme handler does (BundleSchemeHandler): a path under the resources' root. */
function serve(roots) {
    const server = createServer((request, response) => {
        const [top, ...rest] = decodeURIComponent(new URL(request.url, "http://app").pathname).split("/").filter(Boolean);
        const root = roots.get(top);
        const file = root === undefined ? null : normalize(join(root, ...rest));
        try {
            const body = readFileSync(file);
            response.writeHead(200, { "content-type": TYPES[extname(file)] ?? "application/octet-stream" });
            response.end(body);
        } catch {
            response.writeHead(404);
            response.end();
        }
    });
    return new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(server)));
}

const MOUSE = { pen: "pen", mouse: "mouse" };

/**
 * A pointer step pressed with the browser's own input as the replayer's `stroke` says: the pen and the mouse as mouse
 * events of that kind, a finger as touches. Every wait is the page's, as the replayer's are: a hold on the page's clock,
 * the first moves with the press until the pointer has left the page's hold, then a frame between moves and after the
 * lifting.
 */
async function press(page, cdp, step, { down, moves, atOnce, up }) {
    const frame = () => page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => resolve())));
    const touch = (type, point) => cdp.send("Input.dispatchTouchEvent", { type, touchPoints: point === null ? [] : [{ x: point.x, y: point.y, radiusX: 10, radiusY: 10, force: 0.5 }] });
    const mouse = (type, point, buttons) => cdp.send("Input.dispatchMouseEvent", { type, x: point.x, y: point.y, button: "left", buttons, clickCount: 1, pointerType: MOUSE[step.pointer] });
    const finger = step.pointer === "touch";
    await (finger ? touch("touchStart", down) : mouse("mousePressed", down, 1));
    if (step.hold) {
        await page.evaluate((ms) => new Promise((resolve) => setTimeout(resolve, ms)), step.hold);
    }
    for (const [index, move] of moves.entries()) {
        await (finger ? touch("touchMove", move) : mouse("mouseMoved", move, 1));
        if (index + 1 >= atOnce) {
            await frame();
        }
    }
    await (finger ? touch("touchEnd", null) : mouse("mouseReleased", up, 0));
    await frame();
}

/** Opens a scenario's page in a page of the browser's, once its script is ready, with the replayer in it. */
async function open(page, base, scenario) {
    await page.goto(`${base}/Web/${scenario.page}.html`);
    // A command its script defines, as PageScenarioTests waits for: roll.html's <div id="roll"> is window.roll before roll.js runs.
    await page.waitForFunction((roll) => typeof (roll ? window.roll?.load : window.motiff?.show) === "function", scenario.page === "roll");
    await page.addScriptTag({ content: readFileSync(join(repo, "tests/pages/replay.js"), "utf8") });
}

/** One scenario in one way of pressing: the differences found, the page's own errors among them. */
async function play(browser, base, scenario, fixture, input) {
    const page = await browser.newPage({ viewport: scenario.viewport ?? { width: 1024, height: 1366 }, deviceScaleFactor: 2, hasTouch: true });
    const errors = [];
    page.on("pageerror", (error) => errors.push(`page error: ${error.message}`));
    try {
        await open(page, base, scenario);
        if (input === "synthetic") {
            return [...(await page.evaluate(([s, f]) => window.pageScenario.play(s, f), [scenario, fixture])), ...errors];
        }
        const cdp = await page.context().newCDPSession(page);
        const count = await page.evaluate(([s, f]) => window.pageScenario.begin(s, f), [scenario, fixture]);
        const found = [];
        for (let index = 0; index < count; index++) {
            const step = scenario.steps[index];
            if ("pointer" in step) {
                await press(page, cdp, step, await page.evaluate((at) => window.pageScenario.stroke(at), index));
                const fault = await page.evaluate((at) => window.pageScenario.timing(at), index);
                if (fault !== null) {
                    return [`stopped: ${fault}`, ...errors];
                }
            } else {
                found.push(...(await page.evaluate((at) => window.pageScenario.step(at), index)));
                if (show && "expect" in step) {
                    console.log(JSON.stringify(await page.evaluate(() => window.pageScenario.seen())));
                }
            }
        }
        return [...found, ...errors];
    } catch (error) {
        return [`stopped: ${error.message.split("\n")[0]}`, ...errors];
    } finally {
        await page.close();
    }
}

function load(only) {
    return readdirSync(scenarios).filter((name) => name.endsWith(".json")).sort().map((name) => {
        const scenario = JSON.parse(readFileSync(join(scenarios, name), "utf8"));
        return { name, scenario, fixture: JSON.parse(readFileSync(join(scenarios, "fixtures", scenario.fixture), "utf8")) };
    }).filter(({ scenario }) => only.length === 0 || only.includes(scenario.id));
}

/** The fixtures made again beside the run (fixture.mjs --check): what differs from the committed ones, or nothing. */
async function freshness() {
    try {
        await promisify(execFile)(process.execPath, [join(repo, "tests/pages/fixture.mjs"), "--check"], { maxBuffer: 1 << 24 });
        return [];
    } catch (error) {
        return `${error.stdout ?? ""}${error.stderr ?? ""}`.trim().split("\n").filter(Boolean).slice(0, 8);
    }
}

const args = process.argv.slice(2);
const show = args.includes("--show");
const started = Date.now();
const fresh = freshness();
const server = await serve(bundle());
const base = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ channel: process.env.PAGES_CHANNEL || undefined });
// Each run in a page of its own, a few at a time; reported in order.
const runs = load(args.filter((arg) => arg !== "--show")).flatMap((entry) => (entry.scenario.chromiumOnly || show ? ["browser"] : ["browser", "synthetic"]).map((input) => ({ ...entry, input })));
const parallel = show ? 1 : Number(process.env.PAGES_PARALLEL) || 4;
let failed = 0;
try {
    const results = new Array(runs.length);
    let next = 0;
    const worker = async () => {
        while (next < runs.length) {
            const at = next++;
            const { scenario, fixture, input } = runs[at];
            results[at] = await play(browser, base, scenario, fixture, input);
        }
    };
    await Promise.all(Array.from({ length: parallel }, worker));
    runs.forEach(({ name, scenario, input }, at) => {
        const found = results[at];
        failed += found.length > 0 ? 1 : 0;
        console.log(`${found.length === 0 ? "ok  " : "FAIL"} ${scenario.id} (${scenario.protects.join(", ")}) · ${input} input · ${name}`);
        for (const line of found) {
            console.log(`       ${line}`);
        }
    });
    const stale = await fresh;
    failed += stale.length > 0 ? 1 : 0;
    console.log(`${stale.length === 0 ? "ok  " : "FAIL"} the fixtures are what fixture.mjs makes from the code now · fixture.mjs --check`);
    for (const line of stale) {
        console.log(`       ${line}`);
    }
    // What a scenario records but Andrei has not settled (README.md, "Questions"): shown on every run until answered.
    for (const { scenario } of runs.filter((run) => run.input === "browser")) {
        scenario.steps.forEach((step, index) => step.question && console.log(`?    ${scenario.id} step ${index + 1}: ${step.question}`));
    }
} finally {
    await browser.close();
    server.close();
}
console.log(`${failed === 0 ? "All page scenarios hold" : `${failed} page scenario runs failed`} (${((Date.now() - started) / 1000).toFixed(1)} s)`);
process.exitCode = failed === 0 ? 0 : 1;
