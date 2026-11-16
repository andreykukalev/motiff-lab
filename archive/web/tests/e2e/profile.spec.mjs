// The player's profile (Andrei, 2026-11-06): the name and photo they choose are kept with the account on any plan (the
// API's /profile) and shown in place of the sign-in's name, in the account panel and the library's avatar. A sign-in
// that names the player "unknown" (External ID, when no name was given at sign-up) shows their email instead. The build
// under test has no sign-in, so the test gives it sign-in settings, plays MSAL's script and plays the API, as pro.spec does.
import { expect, test } from "@playwright/test";

const api = "https://api.example.test";
const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "*" };
// A 1×1 PNG.
const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==", "base64");

test.use({ serviceWorkers: "block" });

async function signedIn(page) {
    const profile = { name: null, photo: null };
    const saved = [];
    await page.route("**/appsettings.json", route => route.fulfill({
        json: {
            Account: {
                Authority: "https://motifflab.ciamlogin.com/00000000-0000-0000-0000-000000000001",
                ClientId: "00000000-0000-0000-0000-000000000003",
                ApiScope: "api://00000000-0000-0000-0000-000000000002/Projects.ReadWrite",
                ScanScope: "api://00000000-0000-0000-0000-000000000002/Scan",
                Api: api,
                Scan: "https://scan.example.test",
            },
        },
    }));
    await page.route("**/AuthenticationService.js", route => route.fulfill({
        contentType: "text/javascript",
        body: `window.AuthenticationService = {
            init: async () => {},
            getUser: async () => ({ name: "unknown", email: "andrei@example.com" }),
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
        const path = new URL(request.url()).pathname;
        if (path === "/account") {
            return route.fulfill({ headers: cors, json: { plan: "free", tester: false, chosen: null, features: { scan: false, transcribe: false, saveOnline: false, assistant: false } } });
        }
        if (path === "/profile" && request.method() === "PUT") {
            const body = request.postDataJSON();
            saved.push(body);
            Object.assign(profile, { name: body.name ?? null, photo: body.photo ?? null });
            return route.fulfill({ headers: cors, json: profile });
        }
        if (path === "/profile") {
            return route.fulfill({ headers: cors, json: profile });
        }
        return route.fulfill({ headers: cors, json: [] });
    });
    return saved;
}

test("a player on any plan sets a name and a photo, shown in place of the sign-in's", async ({ page }) => {
    const saved = await signedIn(page);
    await page.goto("settings");

    // External ID's "unknown" is no name: the email shows until the player chooses one.
    const head = page.locator(".account-head");
    await expect(head.locator(".who strong")).toHaveText("andrei@example.com");
    await expect(head.locator(".avatar")).toHaveText("A");

    await page.getByRole("button", { name: "Edit profile" }).click();
    const editor = page.getByRole("group", { name: "Edit profile" });
    await editor.getByLabel("Name").fill("Андрей К.");
    await editor.locator("input[type=file]").setInputFiles({ name: "me.png", mimeType: "image/png", buffer: png });
    await expect(editor.locator(".avatar img")).toHaveAttribute("src", /^data:image\/jpeg;base64,/);
    await editor.getByRole("button", { name: "Save" }).click();

    await expect(page.getByRole("status").filter({ hasText: "Saved." })).toBeVisible();
    expect(saved).toHaveLength(1);
    expect(saved[0].name).toBe("Андрей К.");
    expect(saved[0].photo).toMatch(/^data:image\/jpeg;base64,/);
    await expect(head.locator(".who strong")).toHaveText("Андрей К.");
    await expect(head.locator(".avatar img")).toBeVisible();

    // The library's avatar shows the photo, named by the chosen name.
    await page.goto("./");
    const avatar = page.getByRole("button", { name: /Андрей К\./ });
    await expect(avatar.locator("img")).toBeVisible();

    // Removing the photo brings the initial back.
    await page.goto("settings");
    await page.getByRole("button", { name: "Edit profile" }).click();
    await page.getByRole("button", { name: "Remove the photo" }).click();
    await page.getByRole("group", { name: "Edit profile" }).getByRole("button", { name: "Save" }).click();
    await expect(head.locator(".avatar")).toHaveText("А");
    expect(saved[1].photo).toBeNull();
});
