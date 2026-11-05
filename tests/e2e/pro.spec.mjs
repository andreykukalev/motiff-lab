// Pro (decision record 0018, section 12; preparation item 14): what needs the cloud is Pro. The API tells the app the
// player's plan (GET /account), Settings shows it, a tester gets a switch whose choice goes with every request as the
// Motiff-Plan header, and without Pro saving online and the hosted scan server are offered no further. The build under
// test has no sign-in, so the test gives it sign-in settings, plays MSAL's script as a signed-in player, and plays the API.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

const api = "https://api.example.test";
const scan = "https://scan.example.test";
const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "*" };

test.use({ serviceWorkers: "block" });

/** A signed-in player whose account the API answers as the plan the tester chose, or `own` without a choice. */
async function signedIn(page, { tester, own }) {
    const choices = [];
    await page.route("**/appsettings.json", route => route.fulfill({
        json: {
            Account: {
                Authority: "https://motifflab.ciamlogin.com/00000000-0000-0000-0000-000000000001",
                ClientId: "00000000-0000-0000-0000-000000000003",
                ApiScope: "api://00000000-0000-0000-0000-000000000002/Projects.ReadWrite",
                ScanScope: "api://00000000-0000-0000-0000-000000000002/Scan",
                Api: api,
                Scan: scan,
            },
        },
    }));
    // MSAL's script, as a player who is signed in and gets every token without a prompt.
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
    await page.route(`${api}/**`, route => {
        const request = route.request();
        if (request.method() === "OPTIONS") {
            return route.fulfill({ status: 204, headers: { ...cors, "Access-Control-Allow-Methods": "GET, PUT, DELETE" } });
        }
        if (new URL(request.url()).pathname === "/account") {
            const choice = tester ? request.headers()["motiff-plan"] ?? null : null;
            choices.push(choice);
            const plan = choice ?? own;
            const allowed = plan === "pro";
            return route.fulfill({
                headers: cors,
                json: { plan, tester, chosen: choice, features: { scan: allowed, transcribe: allowed, saveOnline: allowed, assistant: allowed } },
            });
        }
        return route.fulfill({ headers: cors, json: [] });
    });
    await page.route(`${scan}/health`, route => route.fulfill({ headers: cors, json: { engine: "homr", version: "0.7.0", maxPages: 8, signIn: true } }));
    return choices;
}

test("a free player is told what Pro adds and cannot turn saving online on", async ({ page }) => {
    await signedIn(page, { tester: false, own: "free" });
    await page.goto("settings");

    await expect(page.locator("#plan")).toContainText("Free");
    await expect(page.getByLabel("Testing")).toHaveCount(0);
    await expect(page.getByLabel("Save my projects online")).toBeDisabled();
    await expect(page.locator("#online-status")).toHaveText("Saving online is part of Pro.");

    await page.goto("import/scan");
    await expect(page.getByText("Scanning runs on Motiff Lab's server and is part of Pro.")).toBeVisible();
    await expect(page.getByLabel("Choose a PDF or photo of printed music to scan")).toHaveCount(0);
});

test("a tester switches between Pro and no Pro, and the choice goes to the API", async ({ page }) => {
    const choices = await signedIn(page, { tester: true, own: "pro" });
    await page.goto("settings");

    await expect(page.locator("#plan")).toContainText("Pro");
    await expect(page.getByLabel("Save my projects online")).toBeEnabled();

    await page.getByLabel("Testing").selectOption("free");
    await expect(page.locator("#plan")).toContainText("Free");
    await expect(page.getByLabel("Save my projects online")).toBeDisabled();
    expect(choices.at(-1)).toBe("free");

    // The choice stays on this device.
    await page.reload();
    await expect(page.getByLabel("Testing")).toHaveValue("free");
    await expect(page.locator("#plan")).toContainText("Free");

    await page.getByLabel("Testing").selectOption("");
    await expect(page.locator("#plan")).toContainText("Pro");
    expect(choices.at(-1)).toBeNull();

    await page.goto("import/scan");
    await expect(page.getByLabel("Choose a PDF or photo of printed music to scan")).toHaveCount(1);
    await expect(page.getByText("Scanning runs on Motiff Lab's server and is part of Pro.")).toHaveCount(0);
});

test("the assistant offers Pro to a free player and, in Pro, says the conversation is coming", async ({ page }) => {
    await signedIn(page, { tester: true, own: "free" });
    await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "More for the selection" }).click();
    await page.locator(".ctx .menu-more").getByRole("button", { name: "Ask", exact: true }).click();
    const ask = page.locator(".sheet.insights .assistant");
    await expect(ask.locator(".pro-offer")).toContainText("so it is part of Pro. Nothing has been sent.");
    await expect(ask.locator(".pro-offer").getByRole("link", { name: "Your plan" })).toHaveAttribute("href", "settings#plan");

    await page.goto("settings");
    await page.getByLabel("Testing").selectOption("pro");
    await expect(page.locator("#plan")).toContainText("Pro");
    await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "More for the selection" }).click();
    await page.locator(".ctx .menu-more").getByRole("button", { name: "Ask", exact: true }).click();
    await expect(ask.locator(".conversation")).toHaveText("The conversation is coming to Pro soon. Nothing is sent yet.");
    await expect(ask.locator(".pro-offer")).toHaveCount(0);
});
