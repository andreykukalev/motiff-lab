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

test("the assistant that edits chooses an operation, the app shows its draft over the bars, and Accept adds it to the corrections (#582 item 17.3)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.assistantActions", "on"));
    const remove = { bars: { from: 2, to: 2 }, edits: [{ kind: "remove", address: "2.R.2", bar: null, hand: null, start: null, length: null, pitch: null, velocity: null }] };
    const turns = await signedIn(page, {
        configured: true,
        turn: (sent, count) => count % 2 === 1
            ? { kind: "toolCalls", entry: { kind: "toolCalls", calls: [{ id: `c${count}`, name: "notes.edit", arguments: remove }] } }
            : { kind: "answer", text: "I asked the app to remove the second note.", fallback: false },
    });
    const errors = await openFurElise(page);
    const mentor = await askFromMenu(page);
    await mentor.getByRole("button", { name: "Agree and ask" }).click();
    await mentor.getByRole("textbox", { name: "Ask or say what you noticed…" }).fill("Remove the stray note.");
    await mentor.getByRole("button", { name: "Ask", exact: true }).click();

    // The device ran the call in music-core and sent the draft it made back; the answer needs no citation beside a draft.
    const draft = mentor.locator(".assistant-draft");
    await expect(draft).toBeVisible();
    expect(turns[0].role).toBe("proposer");
    expect(turns[1].history[2].results[0].content.drafts[0].edits[0]).toMatchObject({ kind: "remove", address: "2.R.2" });
    await expect(mentor.locator(".mentor-answer")).toContainText("I asked the app to remove the second note.");
    await expect(draft.locator(".overline").first()).toHaveText("The assistant's draft · bar 2");
    await expect(draft.getByRole("figure")).toBeVisible();
    await expect(draft.locator(".repair-changes")).toContainText("removed");
    await expect(draft.getByRole("button", { name: "Cancel" })).toBeVisible();
    expect(await violations(page)).toEqual([]);

    // Accept: the draft goes into My corrections.
    await draft.getByRole("button", { name: "Accept" }).click();
    await expect(draft).toHaveCount(0);
    await expect(page.getByText("The draft is accepted (bar 2). The correction is in My corrections.")).toBeVisible();

    // A second request, cancelled: the draft goes and nothing changes.
    await mentor.getByRole("textbox", { name: "Ask or say what you noticed…" }).fill("And the next one?");
    await mentor.getByRole("button", { name: "Ask", exact: true }).click();
    await expect(mentor.locator(".mentor-answer")).toHaveCount(2);
    await expect(draft).toBeVisible();
    await draft.getByRole("button", { name: "Cancel" }).click();
    await expect(draft).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("Ask on a selection answers in a popup there that keeps nothing, and Add to context moves the exchange into the piece's kept conversation (#582 items 17.1, 17.2)", async ({ page }) => {
    await page.addInitScript(() => {
        localStorage.setItem("motiff-lab:experimental.assistantActions", "on");
        localStorage.setItem("motiff-lab:experimental.assistantContext", "on");
    });
    const remove = { bars: { from: 2, to: 2 }, edits: [{ kind: "remove", address: "2.R.2", bar: null, hand: null, start: null, length: null, pitch: null, velocity: null }] };
    const turns = await signedIn(page, {
        configured: true,
        turn: (sent) => sent.history.at(-1).kind === "question" && sent.history.at(-1).text === "Remove the stray note."
            ? { kind: "toolCalls", entry: { kind: "toolCalls", calls: [{ id: "c1", name: "notes.edit", arguments: remove }] } }
            : { kind: "answer", text: `Answered: ${sent.history.filter(entry => entry.kind === "question").at(-1).text} [[${sent.context.digest.ids[0]}]]`, fallback: false },
    });
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "More for the selection" }).click();
    await page.locator(".ctx .menu-more").getByRole("button", { name: "Ask", exact: true }).click();

    // The compact popup at the selection, not the full assistant.
    const popup = page.locator(".ask-pop .context-assistant");
    await expect(popup).toBeVisible();
    await expect(page.locator(".sheet.insights .assistant.mentor")).toHaveCount(0);
    await expect(popup.locator(".context-about")).toHaveText(/^About This (selection|phrase) · bar 2$/);
    await popup.getByRole("button", { name: "Agree and ask" }).click();
    const input = popup.getByRole("textbox", { name: "Ask about these bars or ask to change them" });

    // A request to change the bars: the draft is shown in the popup; Cancel drops it.
    await input.fill("Remove the stray note.");
    await popup.getByRole("button", { name: "Ask", exact: true }).click();
    const draft = popup.locator(".assistant-draft");
    await expect(draft).toBeVisible();
    expect(turns[0]).toMatchObject({ role: "proposer" });
    expect(turns[0].context.digest).toMatchObject({ scope: "selection", bars: { from: 2, to: 2 } });
    await draft.getByRole("button", { name: "Cancel" }).click();
    await expect(draft).toHaveCount(0);

    // A second question takes the place of the first: each is a conversation of its own.
    await input.fill("What happens here?");
    await popup.getByRole("button", { name: "Ask", exact: true }).click();
    await expect(popup.locator(".mentor-answer")).toContainText("Answered: What happens here?");
    await expect(popup.locator(".mentor-question")).toHaveCount(1);
    expect(turns.at(-1).history.map(entry => entry.kind)).toEqual(["question"]);
    expect(await violations(page)).toEqual([]);

    await popup.getByRole("button", { name: "Add to context" }).click();
    await expect(popup.getByRole("status").filter({ hasText: "Added to the assistant's conversation about this piece." })).toBeVisible();
    await popup.getByRole("button", { name: "Close" }).click();
    await expect(popup).toHaveCount(0);

    // The page loaded again: the full assistant opens on the piece's conversation, kept on the device, and goes on from it.
    await page.reload();
    const load = page.getByRole("button", { name: "Für Elise" });
    await expect(load).toBeEnabled({ timeout: 30_000 });
    await load.click();
    await expect(page.locator(".bar .ttl")).toHaveText("Für Elise");
    await page.keyboard.press("?");
    const mentor = page.locator(".sheet.insights .assistant.mentor");
    await expect(mentor.locator(".mentor-question")).toHaveText(["What happens here?"]);
    await expect(mentor.locator(".mentor-answer .cite").first()).toBeVisible();
    await mentor.getByRole("textbox", { name: "Ask or say what you noticed…" }).fill("And then?");
    await mentor.getByRole("button", { name: "Ask", exact: true }).click();
    await expect(mentor.locator(".mentor-answer")).toHaveCount(2);
    expect(turns.at(-1).history.map(entry => entry.kind)).toEqual(["question", "answer", "question"]);
    expect(turns.at(-1).history[0].text).toBe("What happens here?");

    // A new conversation, and back to the earlier one from the list.
    await mentor.getByRole("button", { name: "New conversation" }).click();
    await expect(mentor.locator(".mentor-question")).toHaveCount(0);
    await mentor.getByRole("button", { name: "1 conversation about this piece" }).click();
    await mentor.getByRole("list", { name: "Conversations about this piece" }).getByRole("button", { name: /What happens here\?/ }).click();
    await expect(mentor.locator(".mentor-question")).toHaveText(["What happens here?", "And then?"]);
    expect(await violations(page)).toEqual([]);
    expect(errors).toEqual([]);
});
