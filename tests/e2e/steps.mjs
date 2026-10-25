// Shared steps for the browser tests.
import { expect } from "@playwright/test";

// Where the roll draws bar numbers (Scripts/roll/piano-roll.ts): above the notes, right of the pitch names.
const GUTTER = 40;
const HEADER = 24;
const PIXELS_PER_QUARTER = 48;

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
    await expect(page.getByText("Für Elise (Beethoven, opening)")).toBeVisible();
    return errors;
}

/**
 * Clicks a bar number on the roll. By default the bars are Für Elise's: a pickup of one eighth, then
 * bars of 3/8; other pieces give their pickup and bar length in quarters.
 */
export async function selectBar(page, bar, { pickup = 0.5, barQuarters = 1.5 } = {}) {
    const roll = page.locator(".roll");
    await expect(roll).toBeVisible();
    const box = await roll.boundingBox();
    const quarters = pickup + (bar - 1) * barQuarters + 0.4;
    await page.mouse.click(box.x + GUTTER + quarters * PIXELS_PER_QUARTER, box.y + HEADER / 2);
    await expect(page.locator(".inspector .selection-text")).toContainText("bar " + bar);
}
