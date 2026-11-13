// Tests for the long press that opens an element's own settings (decision record 0018, section 10), run in Node on the
// compiled module: a press held in place counts, one that moves is a drag or a scroll.
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { HOLD_MS, LongPress, SLOP, heldStill } from "../../src/MotiffLab.Web/wwwroot/js/layout/long-press.js";

test("a press that stays within a few pixels is held still", () => {
    assert.equal(heldStill(0, 0), true);
    assert.equal(heldStill(3, -4), true);
    assert.equal(heldStill(SLOP, 0), true);
});

test("a press that moves further is a drag or a scroll", () => {
    assert.equal(heldStill(SLOP + 1, 0), false);
    assert.equal(heldStill(6, 6), false);
});

test("the hold is long enough not to catch a tap, short enough to feel immediate", () => {
    assert.ok(HOLD_MS >= 400 && HOLD_MS <= 800);
});

test("the release after a long press held a long time does not also tap the element", async (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    // Node has no DOM: the element pressed is an event target the module takes for an Element.
    globalThis.Element ??= class extends EventTarget {};
    globalThis.window ??= globalThis;
    const pill = new globalThis.Element();
    pill.dataset = { longPress: "pill" };
    pill.closest = () => pill;
    const root = new EventTarget();
    root.contains = () => true;
    const fired = [];
    const press = new LongPress(root, { invokeMethodAsync: async (method, name) => fired.push(`${method} ${name}`) });
    const event = (type, init = {}) => {
        const e = new Event(type, { cancelable: true });
        Object.assign(e, { isPrimary: true, button: 0, pointerId: 1, clientX: 0, clientY: 0, ...init });
        Object.defineProperty(e, "target", { value: pill });
        return e;
    };

    root.dispatchEvent(event("pointerdown"));
    t.mock.timers.tick(HOLD_MS);
    assert.deepEqual(fired, ["OnLongPress pill"]);
    // Held well past the press firing, as a slow device or a patient player does: the click is still swallowed.
    const realNow = performance.now;
    performance.now = () => realNow.call(performance) + 5000;
    try {
        root.dispatchEvent(event("pointerup"));
        const click = event("click");
        root.dispatchEvent(click);
        assert.equal(click.defaultPrevented, true);
        // The next tap is a tap.
        root.dispatchEvent(event("pointerdown"));
        root.dispatchEvent(event("pointerup"));
        const tap = event("click");
        root.dispatchEvent(tap);
        assert.equal(tap.defaultPrevented, false);
    } finally {
        performance.now = realNow;
        press.dispose();
    }
});
