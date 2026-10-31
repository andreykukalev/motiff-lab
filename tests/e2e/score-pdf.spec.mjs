// A4 pages and the PDF export (decision record 0017): the score opens on A4 pages headed by the
// piece's title, switches to a strip and back, and downloads as a PDF of A4 pages, in every engine.
import { test, expect } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { openFurElise } from "./steps.mjs";

test("the score shows A4 pages and downloads as a PDF", async ({ page }) => {
    const errors = await openFurElise(page);
    const score = page.locator("div.score");
    await expect(score).toHaveClass(/\bpaged\b/);
    const sheet = score.locator(".score-page svg").first();
    await expect(sheet).toHaveAttribute("viewBox", "0 0 2100 2970");
    // The title heads the first page.
    await expect(score.locator(".score-page").first().locator("g.pgHead")).toContainText("Für Elise");

    // A strip the width of the view, then pages again.
    await page.getByRole("button", { name: "Strip" }).click();
    await expect(score).not.toHaveClass(/\bpaged\b/);
    await expect(score.locator(".score-page svg").first()).not.toHaveAttribute("viewBox", "0 0 2100 2970");
    await page.getByRole("button", { name: "A4 pages" }).click();
    await expect(score).toHaveClass(/\bpaged\b/);
    await expect(score.locator(".score-page svg").first()).toHaveAttribute("viewBox", "0 0 2100 2970");

    // The name is read from the link the page clicks, as some headless browsers report a name with non-ASCII letters as "download".
    await page.evaluate(() => {
        const click = HTMLAnchorElement.prototype.click;
        HTMLAnchorElement.prototype.click = function () {
            window.__downloaded = this.download;
            click.call(this);
        };
    });
    const [download] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: "Download PDF" }).click()]);
    expect(await page.evaluate(() => window.__downloaded)).toBe("Für Elise (Beethoven, opening).pdf");
    await download.saveAs(test.info().outputPath("score.pdf"));
    const pdf = await readFile(await download.path());
    const text = pdf.toString("latin1");
    expect(text.startsWith("%PDF-1.4")).toBe(true);
    expect(text).toMatch(/\/MediaBox \[0 0 595\.276 841\.89\]/);
    expect(text).toMatch(/\/Type \/Pages \/Kids \[[^\]]+\] \/Count [1-9]/);
    // The page's text (the title) is drawn over the vectors.
    expect(text).toContain("/XObject << /Ink");
    expect(text.trimEnd().endsWith("%%EOF")).toBe(true);
    await expect(page.getByRole("button", { name: "Download PDF" })).toBeEnabled();
    expect(errors).toEqual([]);
});
