// Shared steps for the browser tests.
import AxeBuilder from "@axe-core/playwright";
import { expect } from "@playwright/test";

// Where the roll draws bar numbers (Scripts/roll/piano-roll.ts): above the notes, right of the pitch names.
const GUTTER = 62;
const HEADER = 24;
const PIXELS_PER_QUARTER = 48;

/**
 * The WCAG 2.2 AA rules axe finds broken on the page. It is read with reduced motion, so the workspace's bar fades at
 * once rather than half-way through the reading, and once the transitions running (a theme just switched) have ended:
 * the rules read the colours the page ends on.
 */
export async function violations(page) {
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.evaluate(() => Promise.all(document.getAnimations().filter(animation => animation instanceof CSSTransition).map(animation => animation.finished.catch(() => null))));
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
    return results.violations.map(v => `${v.id} (${v.impact}): ${v.help}\n  ${v.nodes.slice(0, 5).map(n => n.target.join(" ")).join("\n  ")}`);
}

/**
 * The page's uncaught script errors, collected from now on, to expect none. Playwright reports every JavaScript error
 * WebKit writes to its console as a page error, and WebKit writes one for a load the page gives up on: a spec that
 * leaves the workspace while the piano samples load gets "Fetch API cannot load …/audio/piano/piano.json due to
 * access control checks", though the app catches that failure (Playback/Player.cs). That log of the piano samples is
 * left out. Any other failed load still counts, and so does an uncaught rejection ("Unhandled Promise Rejection: …").
 */
export function pageErrors(page) {
    const errors = [];
    page.on("pageerror", error => {
        if (!(error.name.startsWith("Fetch API cannot load") && error.message.includes("/audio/piano/"))) {
            errors.push(String(error));
        }
    });
    return errors;
}

/** Opens the lab with Für Elise loaded, and fails on any script error. */
export async function openFurElise(page) {
    // Listens to the engine's reports alongside the app, for startTime, and keeps what audioDiagnostic tells.
    await page.addInitScript(() => {
        const trace = { clicks: [], resumes: [], posted: [], lastReport: null };
        window.__audioTrace = trace;
        const since = () => Math.round(performance.now());
        document.addEventListener("click", event => {
            const button = event.target instanceof Element ? event.target.closest("button") : null;
            if (button !== null) {
                trace.clicks.push({ at: since(), label: button.getAttribute("aria-label") ?? button.textContent.trim().slice(0, 40), engine: window.__audio !== undefined });
            }
        }, { capture: true });
        const resume = AudioContext.prototype.resume;
        AudioContext.prototype.resume = function (...args) {
            const call = { at: since(), before: this.state, settled: null, after: null };
            trace.resumes.push(call);
            return resume.apply(this, args).then(value => {
                Object.assign(call, { settled: since(), after: this.state });
                return value;
            }, error => {
                Object.assign(call, { settled: since(), after: `${this.state} (${error})` });
                throw error;
            });
        };
        const Node = window.AudioWorkletNode;
        window.AudioWorkletNode = class extends Node {
            constructor(...args) {
                super(...args);
                window.__audio = this.context;
                const port = this.port;
                const post = port.postMessage;
                port.postMessage = (message, ...rest) => {
                    if (typeof message?.type === "string" && !["timeline", "source", "samples", "recording"].includes(message.type)) {
                        trace.posted.push({ at: since(), type: message.type });
                    }
                    return post.call(port, message, ...rest);
                };
                let playing = false;
                port.addEventListener("message", event => {
                    if (event.data?.type === "position") {
                        trace.lastReport = { at: since(), state: event.data.state, tick: event.data.tick, frame: event.data.frame };
                    }
                    const now = event.data?.type === "position" && event.data.state === "playing";
                    if (now && !playing) {
                        window.__engineStarted?.(event.data.frame / this.context.sampleRate);
                    }
                    playing = now;
                });
            }
        };
    });
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback");
    const load = page.getByRole("button", { name: "Für Elise" });
    await expect(load).toBeEnabled({ timeout: 30_000 });
    await load.click();
    await expect(page.locator(".bar .ttl")).toHaveText("Für Elise");
    return errors;
}

/**
 * Runs the steps after a press of Play; if they fail, attaches to the test what the audio did before throwing again
 * (#401): the AudioContext's state and clock, every resume asked of it and how it ended, the buttons clicked (and
 * whether the engine existed then), the messages the app posted to the engine (a "play" is posted only once the Play
 * handler has run and the context has resumed) and the engine's last report.
 */
export async function withAudioDiagnostic(page, testInfo, steps) {
    try {
        await steps();
    } catch (error) {
        const diagnostic = await page.evaluate(() => ({
            context: window.__audio === undefined ? null : { state: window.__audio.state, currentTime: window.__audio.currentTime },
            ...window.__audioTrace,
            now: Math.round(performance.now()),
        })).catch(reason => ({ unavailable: String(reason) }));
        await testInfo.attach("audio diagnostic", { body: JSON.stringify(diagnostic, null, 2), contentType: "application/json" });
        console.log(`Audio diagnostic, ${testInfo.project.name}: ${JSON.stringify(diagnostic)}`);
        throw error;
    }
}

/**
 * Clicks a bar number on the roll. By default the bars are Für Elise's: a pickup of one eighth, then
 * bars of 3/8; other pieces give their pickup and bar length in quarters. A selection there is is let go first: a tap
 * outside it leaves it as it is (#511 section 2.1, scenarios B and C), so the press rests on the bar until the hold takes
 * it away, and the click then selects the bar. A bar inside the selection cannot be selected so; the spec lets it go first.
 */
export async function selectBar(page, bar, { pickup = 0.5, barQuarters = 1.5 } = {}) {
    await openRoll(page);
    const status = page.locator(".selection-status");
    const roll = page.locator(".roll");
    const box = await roll.boundingBox();
    const quarters = pickup + (bar - 1) * barQuarters + 0.4;
    const x = box.x + GUTTER + quarters * PIXELS_PER_QUARTER;
    const y = box.y + HEADER / 2;
    if (SELECTED.test(await status.textContent() ?? "")) {
        await page.mouse.move(x, y);
        await page.mouse.down();
        try {
            await expect(status, `bar ${bar} lies outside the selection, so a hold on it lets the selection go`).not.toContainText(SELECTED, { timeout: 5_000 });
        } finally {
            await page.mouse.up();
        }
    }
    await page.mouse.click(x, y);
    await expect(status).toContainText(new RegExp(`(bar|такт) ${bar}(?!\\d)`, "i"));
}

/** The selection status line once something is selected, in English or Russian (selection.phrase, .sentence, .selected). */
const SELECTED = /Selected|Выделен|Выбрано/;

/**
 * Selects a unit of the level shown with Page Down (phrase-keys.ts), as the score draws no phrase brackets to tap
 * (Andrei, 2026-11-13, #479 item 14): the first unit when nothing is selected, then `steps` units on from it, or from
 * the selection there is. Returns the selection's status line.
 */
export async function selectPhrase(page, steps = 0) {
    const status = page.locator(".selection-status");
    // The keys are listened to a moment after the title shows (PlaybackLab.Layout.cs), so a press that came before is pressed again.
    await expect(async () => {
        if (!SELECTED.test(await status.textContent() ?? "")) {
            await page.keyboard.press("PageDown");
        }
        await expect(status).toContainText(SELECTED, { timeout: 3_000 });
    }).toPass({ timeout: 20_000 });
    for (let step = 0; step < steps; step++) {
        const before = await status.textContent();
        await page.keyboard.press("PageDown");
        await expect(status).not.toHaveText(before ?? "");
    }
    return status;
}

/** Lifts the selection into focus (0018 section 4) with Focus in its menu. */
export async function focusSelection(page) {
    await page.locator(".ctx .menu").getByRole("button", { name: /^(Focus|Фокус)$/ }).click();
    await expect(page.locator(".focus-card")).toBeVisible();
}

/** Opens the piano roll drawer from the bar (0018 section 4), if it is not open. */
export async function openRoll(page) {
    const action = page.locator(".bar").getByRole("button", { name: /^(Piano roll|Фортепианный ролл)$/ });
    if ((await action.getAttribute("aria-pressed")) !== "true") {
        await action.click();
    }
    const roll = page.locator(".roll");
    await expect(roll).toBeVisible();
    // The drawer slides up; clicks land where it comes to rest.
    let last = null;
    await expect.poll(async () => {
        const box = await roll.boundingBox();
        const still = last !== null && box !== null && box.y === last.y && box.height === last.height;
        last = box;
        return still;
    }, { intervals: [100] }).toBe(true);
}

/** Opens the Insights sheet from the bar (0018 section 6), if it is not open, and shows one of its tabs. */
export async function openInsights(page) {
    const action = page.locator(".bar").getByRole("button", { name: /^(Insights|Инсайты)$/ });
    if ((await action.getAttribute("aria-pressed")) !== "true") {
        await action.click();
    }
    await expect(page.locator(".sheet.insights")).toBeVisible();
    return stream(page);
}

/** The Insights stream (0018 section 6): Main, then the facts by lens under Details, their Try chips, the experiment tried and Adjust. */
export function stream(page) {
    return page.locator(".sheet.insights .stream");
}

/** Opens every folded lens under Details (testing item 14), so each fact row is in view. */
export async function openLenses(page) {
    const closed = stream(page).locator('.lens-toggle[aria-expanded="false"]');
    while ((await closed.count()) > 0) {
        await closed.first().click();
    }
}

/**
 * Tries a way from a fact's Try button (Andrei, 2026-11-04): opens the Try buttons until the way shows, taps it, and
 * waits for the experiment's card with that way chosen.
 */
export async function tryWay(page, name) {
    const ways = stream(page).locator(".try-row").getByRole("button", { name, exact: true });
    const closed = stream(page).locator('.try-row .btn.try[aria-expanded="false"]');
    // Details folds each lens; a way may rest on a fact in one of them.
    const more = stream(page).locator('.lens-toggle[aria-expanded="false"]');
    while ((await ways.count()) === 0 && ((await closed.count()) > 0 || (await more.count()) > 0)) {
        await ((await closed.count()) > 0 ? closed : more).first().click();
    }
    await ways.first().click();
    const card = stream(page).locator(".explore");
    await expect(card).toBeVisible();
    const chosen = card.locator(".choices button[aria-pressed=true]");
    if ((await chosen.count()) > 0) {
        await expect(chosen).toHaveText(name);
    }
    return card;
}

/** Opens Adjust (keep / change / operation): on the experiment being tried, or Shape a variation yourself without one. */
export async function openAdjust(page) {
    const adjust = stream(page).locator(".try-row").getByRole("button", { name: /^(Adjust|Настроить)$/ });
    if (await adjust.count() > 0) {
        if ((await adjust.first().getAttribute("aria-expanded")) !== "true") {
            await adjust.first().click();
        }
    } else {
        const own = stream(page).getByRole("button", { name: /^(Shape a variation yourself|Собрать вариацию самому)$/ });
        if ((await own.getAttribute("aria-expanded")) !== "true") {
            await own.click();
        }
    }
    const tools = stream(page).locator(".adjust");
    await expect(tools).toBeVisible();
    return tools;
}

/** Opens More tools at the end of the sheet: hands, corrections and a second file. */
export async function openMoreTools(page) {
    const toggle = page.locator(".sheet.insights .more-tools > button.section-toggle");
    if ((await toggle.getAttribute("aria-expanded")) !== "true") {
        await toggle.click();
    }
    return page.locator(".sheet.insights .more-tools");
}

/** Opens the bar's overflow (0018 section 2), if it is not open, and gives it. */
export async function openMore(page) {
    const action = page.locator(".bar").getByRole("button", { name: /^(More|Ещё)$/ });
    if ((await action.getAttribute("aria-expanded")) !== "true") {
        await action.click();
    }
    const more = page.locator(".more-popover");
    await expect(more).toBeVisible();
    return more;
}

/** Puts an open popover away with a tap beside it. */
export async function closePopover(page) {
    const scrim = page.locator("button.scrim");
    if ((await scrim.count()) > 0) {
        await scrim.click({ position: { x: 4, y: 300 } });
    }
    await expect(scrim).toHaveCount(0);
}

/**
 * What is heard, as the stream's Original | Variation | One after the other switch shows it (0018: what is heard lives
 * with the variation, in the stream and in focus, not in the overflow).
 */
export async function heardChoice(page) {
    const compare = page.locator(".compare-switch").first();
    if ((await compare.getByRole("button", { name: "One after the other" }).getAttribute("aria-pressed")) === "true") {
        return "The original, then the variation";
    }
    if ((await compare.getByRole("button", { name: "Variation", exact: true }).getAttribute("aria-pressed")) === "true") {
        return "Variation";
    }
    return (await compare.getByRole("button", { name: "Original", exact: true }).getAttribute("aria-pressed")) === "true" ? "Original" : null;
}

/** Which score the overflow's Notation choice shows, read with the overflow open and put away again. */
export async function scoreChoice(page) {
    const more = await openMore(page);
    const text = await more.getByRole("radiogroup", { name: "Which score shows" }).getByRole("radio", { checked: true }).textContent();
    await closePopover(page);
    return text;
}

/** Clears the selection the way a player does: a tap on the page where there is no music (0018 section 3). */
export async function clearByPage(page) {
    const sheet = page.locator(".score .score-page").first();
    const box = await sheet.boundingBox();
    // The page's left margin, clear of the drawers and the pill.
    await page.mouse.click(box.x + 12, box.y + box.height * 0.3);
    await expect(page.locator(".selection-status")).toContainText(/Nothing selected|Ничего не выделено/);
}

/**
 * The library's Sessions tab (version 0.2, lib-3-sessions.png): the pieces with work to come back to, as cards, each resumed
 * with one tap. The library opens on its list of every piece; the tab picked is kept on the device.
 */
export async function librarySessions(page) {
    await page.getByRole("tab", { name: /^(Sessions|Сессии)$/ }).click();
}
