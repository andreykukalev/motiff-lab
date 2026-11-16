// The page scenarios on Linux (tests/contracts/page-scenarios/README.md): the iPad's real pages, src/ipad/App/Web/score.html
// and roll.html over the compiled shared modules, served as the app's bundle lays them out, in Chromium. Every scenario
// runs twice: pressed with the browser's own input (Chrome DevTools Protocol: a pen, a finger or a mouse as a device
// would give them) and with the pointer events the replayer makes, as the WKWebView replay presses
// (PageScenarioTests.swift). Fails with each difference. After `dotnet build src/shared`:
//   node tests/pages/run.mjs [--show] [scenario-id ...]
// --show prints what each expectation saw (all the messages, the marks it names), to write a scenario from what the page does.
// The browser is Playwright's Chromium, or with PAGES_CHANNEL=chrome the Chrome installed on the machine (CI).
import { readFileSync, readdirSync } from "node:fs";
import { createServer } from "node:http";
import { basename, extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";
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

/** A pointer step pressed with the browser's own input: the pen and the mouse as mouse events of that kind, a finger as touches. */
async function press(page, cdp, step, points) {
    const pause = () => page.waitForTimeout(16);
    const [first, ...rest] = points;
    const path = rest.flatMap((point, index) => {
        const from = index === 0 ? first : rest[index - 1];
        return Array.from({ length: 8 }, (_, at) => ({ x: from.x + ((point.x - from.x) * (at + 1)) / 8, y: from.y + ((point.y - from.y) * (at + 1)) / 8 }));
    });
    const last = path.at(-1) ?? first;
    if (step.pointer === "touch") {
        const touch = (type, point) => cdp.send("Input.dispatchTouchEvent", { type, touchPoints: point === null ? [] : [{ x: point.x, y: point.y, radiusX: 10, radiusY: 10, force: 0.5 }] });
        await touch("touchStart", first);
        await (step.hold ? page.waitForTimeout(step.hold) : pause());
        for (const point of path) {
            await touch("touchMove", point);
            await pause();
        }
        await touch("touchEnd", null);
    } else {
        const mouse = (type, point, buttons) => cdp.send("Input.dispatchMouseEvent", { type, x: point.x, y: point.y, button: "left", buttons, clickCount: 1, pointerType: MOUSE[step.pointer] });
        await mouse("mousePressed", first, 1);
        await (step.hold ? page.waitForTimeout(step.hold) : pause());
        for (const point of path) {
            await mouse("mouseMoved", point, 1);
            await pause();
        }
        await mouse("mouseReleased", last, 0);
    }
    await page.waitForTimeout(50);
}

async function open(browser, base, scenario) {
    const viewport = scenario.viewport ?? { width: 1024, height: 1366 };
    const page = await browser.newPage({ viewport, deviceScaleFactor: 2, hasTouch: true });
    const errors = [];
    page.on("pageerror", (error) => errors.push(`page error: ${error.message}`));
    await page.goto(`${base}/Web/${scenario.page}.html`);
    await page.waitForFunction((name) => typeof window[name] === "object", scenario.page === "roll" ? "roll" : "motiff");
    await page.addScriptTag({ content: readFileSync(join(repo, "tests/pages/replay.js"), "utf8") });
    return { page, errors };
}

/** One scenario in one way of pressing: the differences found, the page's own errors among them. */
async function play(browser, base, scenario, fixture, input) {
    const { page, errors } = await open(browser, base, scenario);
    try {
        if (input === "synthetic") {
            return [...(await page.evaluate(([s, f]) => window.pageScenario.play(s, f), [scenario, fixture])), ...errors];
        }
        const cdp = await page.context().newCDPSession(page);
        const count = await page.evaluate(([s, f]) => window.pageScenario.begin(s, f), [scenario, fixture]);
        const found = [];
        for (let index = 0; index < count; index++) {
            const step = scenario.steps[index];
            if ("pointer" in step) {
                await press(page, cdp, step, await page.evaluate((at) => window.pageScenario.points(at), index));
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

const args = process.argv.slice(2);
const show = args.includes("--show");
const started = Date.now();
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
} finally {
    await browser.close();
    server.close();
}
console.log(`${failed === 0 ? "All page scenarios hold" : `${failed} page scenario runs failed`} (${((Date.now() - started) / 1000).toFixed(1)} s)`);
process.exitCode = failed === 0 ? 0 : 1;
