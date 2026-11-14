// The web test site (governance/README.md, "Branches"): develop deploys to the Static Web App's environment "develop",
// whose address reads almost like the main one. On a test site the browser tab says so, so the player never mistakes it
// for the main address: the title starts with «Тест · » or "Test · " (the interface language), and the tab's icon carries
// a dot in the danger colour. The main address, a local run and the browser tests on localhost are left as they are.
// index.html loads this module; it needs nothing from the app.

import { token } from "./tokens.js";

/**
 * True for the address of a Static Web App environment, <app>-<environment>.<region>.<n>.azurestaticapps.net; the
 * main address is <app>.<n>.azurestaticapps.net, one label shorter.
 */
export function isTestSite(hostname: string): boolean {
    const host = hostname.toLowerCase();
    return host.endsWith(".azurestaticapps.net") && host.split(".").length >= 5;
}

/** The word in front of a test site's title, in the interface language (the document's lang, which the app sets). */
export function testPrefix(language: string): string {
    return language.toLowerCase().startsWith("ru") ? "Тест · " : "Test · ";
}

/** The title with the test prefix in front, once: a title that already has either language's prefix gets the right one. */
export function testTitle(title: string, language: string): string {
    const bare = title.replace(/^(Тест|Test) · /, "");
    return testPrefix(language) + bare;
}

const ICON = "icon-192.png";
const SIZE = 64;

/** The app's icon with a dot in the danger colour in its lower right corner, as a data address; null when it cannot be drawn. */
async function markedIcon(): Promise<string | null> {
    const image = new Image();
    image.src = ICON;
    try {
        await image.decode();
    } catch {
        return null;
    }
    const canvas = document.createElement("canvas");
    canvas.width = SIZE;
    canvas.height = SIZE;
    const context = canvas.getContext("2d");
    if (context === null) {
        return null;
    }
    context.drawImage(image, 0, 0, SIZE, SIZE);
    const radius = SIZE * 0.22;
    const centre = SIZE - radius - 1;
    context.beginPath();
    context.arc(centre, centre, radius, 0, Math.PI * 2);
    context.fillStyle = token("danger");
    context.fill();
    context.lineWidth = SIZE * 0.06;
    context.strokeStyle = token("surface");
    context.stroke();
    return canvas.toDataURL("image/png");
}

function markTitle(): void {
    const language = document.documentElement.lang || "en";
    const wanted = testTitle(document.title, language);
    if (document.title !== wanted) {
        document.title = wanted;
    }
}

async function markIcon(): Promise<void> {
    const address = await markedIcon();
    const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
    if (address !== null && link !== null) {
        link.href = address;
    }
}

/** Marks the tab when this is a test site, and keeps the title marked as the app changes it or its language. */
export function markTestSite(): void {
    if (!isTestSite(location.hostname)) {
        return;
    }
    markTitle();
    new MutationObserver(markTitle).observe(document.head, { childList: true, subtree: true, characterData: true });
    new MutationObserver(markTitle).observe(document.documentElement, { attributes: true, attributeFilter: ["lang"] });
    void markIcon();
}

if (typeof document !== "undefined" && typeof location !== "undefined") {
    markTestSite();
}
