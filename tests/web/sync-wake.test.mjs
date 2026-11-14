// Tests for when the library syncs again (version 0.2): the page shown again, or the network back. Run in Node on the
// compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { stop, watch } from "../../src/MotiffLab.Web/wwwroot/js/account/sync-wake.js";

test("the library is woken when the page is shown again or the network returns, and not after it stops", () => {
    const page = new EventTarget();
    page.visibilityState = "visible";
    globalThis.document = page;
    globalThis.window = new EventTarget();
    const calls = [];
    const library = { invokeMethodAsync: async (method) => { calls.push(method); } };

    const unwatch = watch(library);
    page.dispatchEvent(new Event("visibilitychange"));
    window.dispatchEvent(new Event("online"));
    page.visibilityState = "hidden";
    page.dispatchEvent(new Event("visibilitychange"));
    assert.deepEqual(calls, ["Wake", "Wake"]);

    stop(unwatch);
    page.visibilityState = "visible";
    page.dispatchEvent(new Event("visibilitychange"));
    window.dispatchEvent(new Event("online"));
    assert.deepEqual(calls, ["Wake", "Wake"]);
    delete globalThis.document;
    delete globalThis.window;
});
