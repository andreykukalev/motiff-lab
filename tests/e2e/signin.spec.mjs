// Signing in (decision record 0014): in a build with sign-in, Sign in and Create an account leave for the External ID
// pages in the app's language, and Create an account opens the sign-up form. The build under test has no sign-in, so
// the test gives the app sign-in settings and plays the sign-in tenant: it answers MSAL's metadata request and stops
// at the sign-in page, where the test reads the address the app sent the player to.
import { expect, test } from "@playwright/test";

const authority = "https://motifflab.ciamlogin.com/00000000-0000-0000-0000-000000000001";
const apiApp = "00000000-0000-0000-0000-000000000002";

// The app's service worker would answer appsettings.json before the test could.
test.use({ serviceWorkers: "block" });

/** Gives the app sign-in settings and plays the tenant; returns the sign-in page addresses the browser opened. */
async function signInTenant(page) {
    const opened = [];
    await page.route("**/appsettings.json", route => route.fulfill({
        json: {
            Account: {
                Authority: authority,
                ClientId: "00000000-0000-0000-0000-000000000003",
                ApiScope: `api://${apiApp}/Projects.ReadWrite`,
                ScanScope: `api://${apiApp}/Scan`,
                Api: "https://api.example.test",
                Scan: "https://scan.example.test",
            },
        },
    }));
    await page.route("https://motifflab.ciamlogin.com/**", route => {
        const url = route.request().url();
        if (url.includes("/.well-known/openid-configuration")) {
            return route.fulfill({
                json: {
                    issuer: `${authority}/v2.0`,
                    authorization_endpoint: `${authority}/oauth2/v2.0/authorize`,
                    token_endpoint: `${authority}/oauth2/v2.0/token`,
                    end_session_endpoint: `${authority}/oauth2/v2.0/logout`,
                    jwks_uri: `${authority}/discovery/v2.0/keys`,
                    response_types_supported: ["code"],
                    scopes_supported: ["openid", "offline_access"],
                },
            });
        }
        if (url.includes("/oauth2/v2.0/authorize")) {
            opened.push(new URL(url));
            return route.fulfill({ contentType: "text/html", body: "<title>Sign in</title><h1>Sign in</h1>" });
        }
        return route.abort();
    });
    return opened;
}

test("Sign in opens the sign-in page in the app's language", async ({ page }) => {
    const opened = await signInTenant(page);
    const errors = [];
    page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
    await page.goto("/settings");
    await page.getByRole("button", { name: "Русский" }).click();
    await page.getByRole("button", { name: "Войти" }).first().click();

    await expect(page).toHaveTitle("Sign in");
    expect(errors.filter(text => text.includes("Unhandled"))).toEqual([]);
    const query = opened.at(-1).searchParams;
    expect(query.get("ui_locales")).toBe("ru-RU");
    expect(query.get("mkt")).toBe("ru-RU");
    expect(query.get("prompt")).not.toBe("create");
});

test("Create an account opens the sign-up form", async ({ page }) => {
    const opened = await signInTenant(page);
    await page.goto("/settings");
    await page.getByRole("button", { name: "Create an account" }).click();

    await expect(page).toHaveTitle("Sign in");
    const query = opened.at(-1).searchParams;
    expect(query.get("prompt")).toBe("create");
    expect(query.get("ui_locales")).toBe("en-US");
});
