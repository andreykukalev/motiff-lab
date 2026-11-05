// Shared steps for the browser tests.
import AxeBuilder from "@axe-core/playwright";
import { expect } from "@playwright/test";

// Where the roll draws bar numbers (Scripts/roll/piano-roll.ts): above the notes, right of the pitch names.
const GUTTER = 52;
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

/** Opens the lab with Für Elise loaded, and fails on any script error. */
export async function openFurElise(page) {
    // Listens to the engine's reports alongside the app, for startTime.
    await page.addInitScript(() => {
        const Node = window.AudioWorkletNode;
        window.AudioWorkletNode = class extends Node {
            constructor(...args) {
                super(...args);
                window.__audio = this.context;
                let playing = false;
                this.port.addEventListener("message", event => {
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
    await expect(page.locator(".bar .ttl")).toHaveText("Für Elise (Beethoven, opening)");
    return errors;
}

/**
 * Clicks a bar number on the roll. By default the bars are Für Elise's: a pickup of one eighth, then
 * bars of 3/8; other pieces give their pickup and bar length in quarters.
 */
export async function selectBar(page, bar, { pickup = 0.5, barQuarters = 1.5 } = {}) {
    await openRoll(page);
    const roll = page.locator(".roll");
    const box = await roll.boundingBox();
    const quarters = pickup + (bar - 1) * barQuarters + 0.4;
    await page.mouse.click(box.x + GUTTER + quarters * PIXELS_PER_QUARTER, box.y + HEADER / 2);
    await expect(page.locator(".selection-status")).toContainText(new RegExp(`(bar|такт) ${bar}(?!\\d)`, "i"));
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

/** The Insights stream (0018 section 6): facts by lens, their Try chips, the experiment tried and Adjust. */
export function stream(page) {
    return page.locator(".sheet.insights .stream");
}

/**
 * Tries a way from a fact's Try button (Andrei, 2026-11-04): opens the Try buttons until the way shows, taps it, and
 * waits for the experiment's card with that way chosen.
 */
export async function tryWay(page, name) {
    const ways = stream(page).locator(".try-row").getByRole("button", { name, exact: true });
    const closed = stream(page).locator('.try-row .btn.try[aria-expanded="false"]');
    while ((await ways.count()) === 0 && (await closed.count()) > 0) {
        await closed.first().click();
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
