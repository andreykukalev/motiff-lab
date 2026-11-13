// Tests for the playback model of v0.5.2 (#495 UX-10, Scripts/playback/playback-model.ts) on the case file the iPad's
// Swift port runs too (ipad/App/MotiffLabTests/Golden/playback-model.json, PlaybackPositionTests.swift), in Node on the
// compiled module:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { engineLoop, next, viewOf } from "../../src/MotiffLab.Web/wwwroot/js/playback/playback-model.js";

const root = new URL("../../", import.meta.url);
const { cases } = JSON.parse(readFileSync(new URL("ipad/App/MotiffLabTests/Golden/playback-model.json", root), "utf8"));

const RULES = ["T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8", "T9", "T10", "T11", "T12", "T13", "T16"];

test("the case file covers every transition of the slice and the table of what the player sees", () => {
    const rules = new Set(cases.map((one) => one.rule));
    for (const rule of [...RULES, "view"]) {
        assert.ok(rules.has(rule), `a case for ${rule}`);
    }
    for (const one of cases) {
        assert.ok(one.event !== undefined || one.view !== undefined, `${one.rule} ${one.name}: an event or a view`);
    }
});

for (const one of cases) {
    test(`${one.rule}: ${one.name}`, () => {
        let state = one.given;
        if (one.event !== undefined) {
            const decided = next(one.given, one.event);
            assert.deepEqual(decided.steps, one.steps, "the steps to the engine");
            assert.deepEqual(decided.state, one.then, "the state once the engine has them");
            state = decided.state;
        }
        if (one.view !== undefined) {
            assert.deepEqual(viewOf(state), one.view, "what the views draw");
        }
    });
}

test("the model never changes the state it is given", () => {
    for (const one of cases.filter((each) => each.event !== undefined)) {
        const given = structuredClone(one.given);
        next(given, one.event);
        assert.deepEqual(given, one.given, `${one.rule} ${one.name}`);
    }
});

test("after every event the engine's loop is the selection while Цикл is on, and nothing else", () => {
    for (const one of cases.filter((each) => each.event !== undefined && each.event.type !== "report")) {
        const before = engineLoop(one.given);
        let loop = before;
        for (const step of one.steps) {
            if (step.type === "loop") {
                loop = { start: step.start, end: step.end };
            } else if (step.type === "loopOff") {
                loop = null;
            }
        }
        assert.deepEqual(loop, engineLoop(one.then), `${one.rule} ${one.name}`);
    }
});

test("the blue playhead is never outside the selection, and inside it is the black one", () => {
    for (const one of cases) {
        const state = one.event === undefined ? one.given : one.then;
        const view = viewOf(state);
        if (state.selection === null) {
            assert.equal(view.blue, null);
            continue;
        }
        assert.ok(view.blue >= state.selection.start && view.blue <= state.selection.end, `${one.rule} ${one.name}`);
        if (view.inside) {
            assert.equal(view.blue, view.black);
        }
    }
});
