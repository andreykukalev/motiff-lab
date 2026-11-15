// The assistant's conversation (an experiment, version 0.6; decision record 0028, section 4): with it on, Ask opens the
// mentor in the Insights sheet; before the first question it lists exactly what a question sends and asks for consent; it
// sends nothing unless the API says a model is on for the account (GET /assistant/status); then the device sends the turn,
// runs the model's read tools here and sends their results, and the checked answer's citations ring their places in the
// score. The build under test has no sign-in, so the test gives it sign-in settings, plays MSAL's script as a signed-in
// Pro player and plays the API, the model included. Runs in desktop Chromium only (playwright.config.mjs).
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar, violations } from "./steps.mjs";

const api = "https://api.example.test";
const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "*" };

test.use({ serviceWorkers: "block" });

/** A signed-in Pro player with the experiment on; the API answers the status and each turn as `turn` says. Returns the turns sent. */
async function signedIn(page, { configured, turn }) {
    const turns = [];
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.assistant", "on"));
    await page.route("**/appsettings.json", route => route.fulfill({
        json: {
            Account: {
                Authority: "https://motifflab.ciamlogin.com/00000000-0000-0000-0000-000000000001",
                ClientId: "00000000-0000-0000-0000-000000000003",
                ApiScope: "api://00000000-0000-0000-0000-000000000002/Projects.ReadWrite",
                Api: api,
            },
        },
    }));
    await page.route("**/AuthenticationService.js", route => route.fulfill({
        contentType: "text/javascript",
        body: `window.AuthenticationService = {
            init: async () => {},
            getUser: async () => ({ name: "Andrei" }),
            getAccessToken: async request => ({
                status: "success",
                token: { value: "token", grantedScopes: (request && request.scopes) || [], expires: new Date(Date.now() + 3600000).toISOString() },
            }),
            signIn: async () => ({ status: "success" }),
            signOut: async () => ({ status: "success" }),
            completeSignIn: async () => ({ status: "success" }),
            completeSignOut: async () => ({ status: "success" }),
        };`,
    }));
    await page.route(`${api}/**`, async route => {
        const request = route.request();
        const path = new URL(request.url()).pathname;
        if (request.method() === "OPTIONS") {
            return route.fulfill({ status: 204, headers: { ...cors, "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE" } });
        }
        if (path === "/account") {
            return route.fulfill({ headers: cors, json: { plan: "pro", tester: false, chosen: null, features: { scan: true, transcribe: true, saveOnline: true, assistant: true } } });
        }
        if (path === "/assistant/status") {
            return route.fulfill({ headers: cors, json: { configured, allowed: true, pro: true, left: 200 } });
        }
        if (path === "/assistant/turn") {
            const sent = request.postDataJSON();
            turns.push(sent);
            return route.fulfill({ headers: cors, json: turn(sent, turns.length) });
        }
        return route.fulfill({ headers: cors, json: [] });
    });
    return turns;
}

async function askFromMenu(page) {
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "More for the selection" }).click();
    await page.locator(".ctx .menu-more").getByRole("button", { name: "Ask", exact: true }).click();
    return page.locator(".sheet.insights .assistant.mentor");
}

test("with no model switched on, the mentor asks for consent, says so plainly and sends nothing", async ({ page }) => {
    const turns = await signedIn(page, { configured: false, turn: () => ({ kind: "notConfigured" }) });
    const errors = await openFurElise(page);

    const mentor = await askFromMenu(page);
    await expect(page.locator(".ask-pop")).toHaveCount(0);
    await expect(mentor.getByRole("radiogroup", { name: "What the assistant looks at" }).getByRole("radio", { checked: true })).toHaveText(/^This (selection|phrase) · bar 2$/);

    // The consent lists exactly what a question sends, and where it goes.
    const consent = mentor.getByRole("group", { name: "Before your first question" });
    await expect(consent).toContainText("the notes of the selected passage");
    await expect(consent).toContainText("the facts the app found there, each with its id");
    await expect(consent).toContainText("only to the Motiff Lab API");
    await expect(consent).toContainText("Never sent: the file and its name, the rest of the piece");
    await expect(mentor.getByRole("textbox")).toHaveCount(0);
    expect(await violations(page)).toEqual([]);

    await consent.getByRole("button", { name: "Agree and ask" }).click();
    await mentor.getByRole("textbox", { name: "Ask or say what you noticed…" }).fill("What is the harmony doing here?");
    await mentor.getByRole("button", { name: "Ask", exact: true }).click();
    await expect(mentor.locator(".mentor-question")).toHaveText("What is the harmony doing here?");
    await expect(mentor.locator(".mentor-note")).toHaveText("The assistant's AI model is not switched on yet, so nothing was sent.");
    expect(turns).toEqual([]);
    expect(await violations(page)).toEqual([]);

    // Consent can be withdrawn; the next question asks for it again.
    await mentor.getByRole("button", { name: "Withdraw consent" }).click();
    await expect(mentor.getByRole("group", { name: "Before your first question" })).toBeVisible();
    expect(errors).toEqual([]);
});

test("a question runs the model's read tools on the device and its citations ring their places in the score", async ({ page }) => {
    let cited = null;
    const turns = await signedIn(page, {
        configured: true,
        turn: (sent, count) => {
            if (count === 1) {
                return { kind: "toolCalls", entry: { kind: "toolCalls", calls: [{ id: "c1", name: "harmony.chords", arguments: { bars: { from: 2, to: 2 } } }] } };
            }
            const item = sent.context.digest.items.find(fact => fact.places);
            cited = item;
            return { kind: "answer", text: `Look at this first [[${item.id}]], then play it slower.`, fallback: false };
        },
    });
    const errors = await openFurElise(page);
    const mentor = await askFromMenu(page);
    await mentor.getByRole("button", { name: "Agree and ask" }).click();
    await mentor.locator(".suggestions").getByRole("button").first().click();

    const answer = mentor.locator(".mentor-answer");
    await expect(answer).toContainText("then play it slower.");
    await expect(answer.locator(".overline")).toHaveText("The assistant's reading · cites the app's facts");

    // The first turn carried the passage's digest and notes; the second, the tool's result run here.
    expect(turns).toHaveLength(2);
    expect(turns[0]).toMatchObject({ version: 1, role: "mentor", language: "en" });
    expect(turns[0].context.notes.notes.length).toBeGreaterThan(0);
    expect(turns[0].history.map(entry => entry.kind)).toEqual(["question"]);
    expect(turns[1].history.map(entry => entry.kind)).toEqual(["question", "toolCalls", "toolResults"]);
    expect(turns[1].history[2].results[0]).toMatchObject({ id: "c1", name: "harmony.chords" });
    expect(turns[1].history[2].results[0].content.ok).toBe(true);

    // The citation is the engine's own line, and it rings its notes in the score.
    const chip = answer.getByRole("button", { name: cited.text });
    await expect(chip).toBeVisible();
    await expect(page.locator(".score .highlight")).toHaveCount(0);
    await chip.click();
    await expect(page.locator(".score .highlight").first()).toBeVisible();
    expect(await violations(page)).toEqual([]);
    expect(errors).toEqual([]);
});

test("an answer that cites a fact the turn never gave is replaced by the engine's facts", async ({ page }) => {
    await signedIn(page, { configured: true, turn: () => ({ kind: "answer", text: "It modulates [[made.up]].", fallback: false }) });
    await openFurElise(page);
    const mentor = await askFromMenu(page);
    await mentor.getByRole("button", { name: "Agree and ask" }).click();
    await mentor.locator(".suggestions").getByRole("button").first().click();

    const answer = mentor.locator(".mentor-answer.fallback");
    await expect(answer.locator(".overline")).toHaveText("The app's facts · the answer could not be checked");
    await expect(answer).toContainText("The assistant's answer could not be checked against the engine's facts");
    await expect(answer).not.toContainText("made.up");
    await expect(answer.locator(".cite").first()).toBeVisible();
});

test("bars selected with the assistant already open are what the next question sends (#582 item 1)", async ({ page }) => {
    const turns = await signedIn(page, { configured: true, turn: sent => ({ kind: "answer", text: `Here [[${sent.context.digest.ids[0]}]].`, fallback: false }) });
    await openFurElise(page);
    // Opened with nothing selected: the whole piece.
    await page.keyboard.press("?");
    const mentor = page.locator(".sheet.insights .assistant.mentor");
    const scope = mentor.getByRole("radiogroup", { name: "What the assistant looks at" }).getByRole("radio", { checked: true });
    await expect(scope).toHaveText("Whole piece");
    await mentor.getByRole("button", { name: "Agree and ask" }).click();

    // Bars selected afterwards: the scope follows the selection, and the turn carries their digest and notes.
    await selectBar(page, 2);
    await expect(scope).toHaveText(/^This (selection|phrase) · bar 2$/);
    await mentor.getByRole("textbox", { name: "Ask or say what you noticed…" }).fill("What happens here?");
    await mentor.getByRole("button", { name: "Ask", exact: true }).click();
    await expect(mentor.locator(".mentor-answer")).toHaveCount(1);
    expect(turns).toHaveLength(1);
    expect(turns[0].context.digest).toMatchObject({ scope: "selection", bars: { from: 2, to: 2 } });
    expect(turns[0].context.notes.notes.length).toBeGreaterThan(0);
    expect(turns[0].context.notes.notes.every(note => note.address.startsWith("2."))).toBe(true);

    // The whole piece chosen for this selection holds while it stands.
    await mentor.getByRole("radio", { name: "Whole piece" }).click();
    await mentor.getByRole("textbox", { name: "Ask or say what you noticed…" }).fill("And the whole piece?");
    await mentor.getByRole("button", { name: "Ask", exact: true }).click();
    await expect(mentor.locator(".mentor-answer")).toHaveCount(2);
    expect(turns[1].context.digest.scope).toBe("piece");
});
