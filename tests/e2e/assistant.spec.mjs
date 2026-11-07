// The assistant's entry points (decision record 0018, section 7; testing item 23): Ask in the contextual menu's overflow, ? on
// the keyboard, Ask about the piece in the overflow and in Piece DNA open it as a popover where it was pressed, over the place,
// which stays as it was; the input that closes the Insights stream and Open in Insights open it in the Insights sheet. Both show
// its scope chips, what it knows and three questions; the build under test has no sign-in, so it is the free tier, where the
// conversation's place holds the one-line Pro offer and nothing is sent anywhere.
import { expect, test } from "@playwright/test";
import { openFurElise, openMore, selectBar, stream, violations } from "./steps.mjs";

function assistant(page) {
    return page.locator(".sheet.insights .assistant");
}

function popover(page) {
    return page.locator(".ask-pop .assistant");
}

test("Ask from the menu's overflow looks at the selection, says what it knows, and offers Pro without sending anything", async ({ page }) => {
    const errors = await openFurElise(page);
    const requests = [];
    page.on("request", request => requests.push(request.url()));
    await selectBar(page, 2);

    await page.locator(".ctx").getByRole("button", { name: "More for the selection" }).click();
    await page.locator(".ctx .menu-more").getByRole("button", { name: "Ask", exact: true }).click();
    const ask = popover(page);
    await expect(ask.getByRole("heading", { name: "Ask" })).toBeVisible();
    await expect(page.locator(".sheet.insights")).toHaveCount(0);
    const scope = ask.getByRole("radiogroup", { name: "What the assistant looks at" });
    await expect(scope.getByRole("radio", { checked: true })).toHaveText(/^This (selection|phrase) · bar 2$/);
    await expect(scope.getByRole("radio", { name: "Whole piece" })).toHaveAttribute("aria-checked", "false");

    // What it knows: the piece, the passage with its facts, the practice, the level; any item can be taken out.
    const knows = ask.locator(".knows");
    await knows.locator("summary").click();
    await expect(knows.getByLabel("The piece: Für Elise")).toBeChecked();
    await expect(knows.getByLabel(/^The passage: bar 2, \d+ facts?$/)).toBeVisible();
    await expect(knows.getByLabel(/^Practising at 100%/)).toBeVisible();
    await expect(knows.getByLabel(/^Your settings: /)).toBeVisible();
    const count = Number((await knows.locator("summary").textContent()).match(/(\d+) items/)[1]);
    await knows.getByLabel(/^Practising at/).uncheck();
    await expect(knows.locator("summary")).toHaveText(`What it knows · ${count - 1} item${count - 1 === 1 ? "" : "s"}`);

    // The free tier: one plum line where the conversation would be, and the input does not send.
    await expect(ask.locator(".pro-offer")).toHaveText(/^Pro The assistant answers with an AI model in the cloud, so it is part of Pro\. Nothing has been sent\./);
    await expect(ask.getByRole("textbox", { name: "Ask or say what you noticed…" })).toBeDisabled();
    await expect(ask.locator(".suggestions").getByRole("button")).toHaveCount(3);
    await expect(ask.locator(".suggestions").getByRole("button").first()).toBeDisabled();
    await expect(ask.locator(".ask-foot")).toContainText("The conversation is kept in Insights");
    expect(await violations(page)).toEqual([]);

    // The whole piece: the passage leaves what it knows.
    await scope.getByRole("radio", { name: "Whole piece" }).click();
    await expect(scope.getByRole("radio", { name: "Whole piece" })).toHaveAttribute("aria-checked", "true");
    await expect(knows.getByLabel(/^The passage:/)).toHaveCount(0);

    // Open in Insights: the same conversation in the sheet; back to the stream, whose foot is the assistant's input.
    await ask.getByRole("button", { name: "Open in Insights ›" }).click();
    await expect(popover(page)).toHaveCount(0);
    await expect(assistant(page).getByRole("radio", { name: "Whole piece" })).toHaveAttribute("aria-checked", "true");
    await assistant(page).getByRole("button", { name: "Back to Insights" }).click();
    await expect(assistant(page)).toHaveCount(0);
    await stream(page).getByRole("button", { name: "Ask or say what you noticed…" }).click();
    await expect(assistant(page).getByRole("radio", { checked: true })).toHaveText(/ · bar 2$/);

    // Nothing went anywhere but the app itself.
    const origin = new URL(page.url()).origin;
    expect(requests.filter(url => !url.startsWith(origin) && !url.startsWith("data:") && !url.startsWith("blob:"))).toEqual([]);
    expect(errors).toEqual([]);
});

test("? asks about the selection, and Ask about the piece comes from the overflow and from Piece DNA, which stays open under it", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    await page.keyboard.press("Shift+?");
    await expect(popover(page).getByRole("radio", { checked: true })).toHaveText(/ · bar 1$/);
    await popover(page).getByRole("button", { name: "Close" }).click();
    await expect(popover(page)).toHaveCount(0);
    await expect(page.locator(".sheet.insights")).toHaveCount(0);

    const more = await openMore(page);
    await more.getByRole("button", { name: "Ask about the piece" }).click();
    await expect(popover(page).getByRole("radio", { checked: true })).toHaveText("Whole piece");
    await popover(page).getByRole("button", { name: "Close" }).click();

    await (await openMore(page)).getByRole("button", { name: "Piece DNA" }).click();
    const dna = page.getByRole("dialog", { name: "Für Elise" });
    await dna.getByRole("button", { name: "Ask about the piece" }).click();
    const ask = popover(page);
    await expect(ask.getByRole("radio", { checked: true })).toHaveText("Whole piece");
    await expect(dna).toBeVisible();
    await ask.locator(".knows summary").click();
    await expect(ask.locator(".knows").getByLabel(/^The Piece DNA map: \d+ ideas?$/)).toBeChecked();
    await expect(ask.locator(".suggestions").getByRole("button")).toHaveCount(3);

    // Escape puts the popover away and leaves the map as it was.
    await ask.getByRole("textbox").press("Escape");
    await expect(popover(page)).toHaveCount(0);
    await expect(dna).toBeVisible();
    expect(errors).toEqual([]);
});

test("in Russian, the assistant and its offer are in Russian", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "Ещё для выделения" }).click();
    await page.locator(".ctx .menu-more").getByRole("button", { name: "Спросить", exact: true }).click();
    const ask = popover(page);
    await expect(ask.getByRole("radio", { name: "Вся пьеса" })).toBeVisible();
    await expect(ask.locator(".knows summary")).toHaveText(/^Что он знает · \d+ пункт(а|ов)?$/);
    await expect(ask.locator(".pro-offer")).toContainText("поэтому он входит в Pro. Ничего не отправлено.");
    await expect(ask.locator(".ask-foot")).toContainText("Разговор хранится в Инсайтах");
    await expect(ask.getByRole("button", { name: "Открыть в Инсайтах ›" })).toBeVisible();
    expect(errors).toEqual([]);
});
