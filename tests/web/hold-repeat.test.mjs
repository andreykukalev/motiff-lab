// The tempo control's − and + held to repeat (version 0.5.4, #511 section 17; Scripts/layout/hold-repeat.ts), run in Node on
// the compiled module: a tap steps once, a hold steps again and again, the release is not one step more, and a press in
// the control is not also the pill's long press.
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { HoldRepeat, milliseconds } from "../../src/MotiffLab.Web/wwwroot/js/layout/hold-repeat.js";

function setUp(t) {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    // Node has no DOM: the button is an event target the module takes for an Element.
    globalThis.Element ??= class extends EventTarget {};
    globalThis.window ??= globalThis;
    const root = new EventTarget();
    root.contains = () => true;
    const button = new globalThis.Element();
    button.disabled = false;
    button.closest = () => button;
    let steps = 0;
    const event = (type, init = {}) => {
        const e = new Event(type, { cancelable: true, bubbles: true });
        Object.assign(e, { isPrimary: true, button: 0, pointerId: 1, detail: 1, ...init });
        Object.defineProperty(e, "target", { value: button });
        return e;
    };
    button.click = () => root.dispatchEvent(event("click", { detail: 0 }));
    const hold = new HoldRepeat(root, { delay: 320, repeat: 120 });
    // A click that is not swallowed is a step, as Blazor's @onclick takes it (after the control's own listener, which
    // catches the click on its way down).
    root.addEventListener("click", (e) => {
        if (!e.defaultPrevented) steps++;
    });
    // Time goes by a step at a time, so each step's timer is set before the next comes due.
    const wait = (ms) => {
        for (let gone = 0; gone < ms; gone += 10) t.mock.timers.tick(10);
    };
    return { root, button, event, hold, wait, steps: () => steps };
}

test("a token's time is read in milliseconds or seconds", () => {
    assert.equal(milliseconds("320ms"), 320);
    assert.equal(milliseconds(" 0.12s"), 120);
    assert.equal(milliseconds(""), null);
    assert.equal(milliseconds("fast"), null);
});

test("a tap steps once", (t) => {
    const { root, event, wait, steps } = setUp(t);
    root.dispatchEvent(event("pointerdown"));
    wait(100);
    root.dispatchEvent(event("pointerup"));
    root.dispatchEvent(event("click"));
    assert.equal(steps(), 1);
});

test("a hold steps again and again, and its release is not one step more", (t) => {
    const { root, event, wait, steps } = setUp(t);
    root.dispatchEvent(event("pointerdown"));
    wait(320);
    assert.equal(steps(), 1);
    wait(120 * 4);
    assert.equal(steps(), 5);
    root.dispatchEvent(event("pointerup"));
    root.dispatchEvent(event("click"));
    assert.equal(steps(), 5);
    // Stopped: nothing more, and the next tap is a step.
    wait(1000);
    assert.equal(steps(), 5);
    root.dispatchEvent(event("pointerdown"));
    root.dispatchEvent(event("pointerup"));
    root.dispatchEvent(event("click"));
    assert.equal(steps(), 6);
});

test("the steps stop at the end of the range and when the finger slides off", (t) => {
    const { root, button, event, wait, steps } = setUp(t);
    root.dispatchEvent(event("pointerdown"));
    wait(320 + 120);
    assert.equal(steps(), 2);
    button.disabled = true;
    wait(120 * 3);
    assert.equal(steps(), 2);

    button.disabled = false;
    root.dispatchEvent(event("pointerdown"));
    wait(320);
    root.dispatchEvent(event("pointerleave"));
    wait(120 * 3);
    assert.equal(steps(), 3);
});

test("a press in the control is not also the pill's long press", (t) => {
    const { root, event } = setUp(t);
    // The pill holds the control and listens for its long press above it: the control stops the press from going on up.
    const down = event("pointerdown");
    root.dispatchEvent(down);
    assert.equal(down.cancelBubble, true);
});
