// The roll page's contract (decision record 0031, step 12; the v0.7 blueprint's B7): the functions src/ipad/App/Web/roll.js
// offers the iPad app (window.roll), the fields each reads from its argument and answers, the messages it posts with their
// fields, and the host callbacks of PianoRoll it passes on, are exactly what tests/contracts/roll-page.json lists; the
// app's RollPage (Platform/RollPage.swift), the one place that builds the roll's calls, is checked against the same file
// by RollPageContractTests on macOS.
//   node --test tests/shared   (needs no build: it reads the page's source)

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";
import { fieldPaths, keysOf, offered, postedObjects, posted, readPaths, returnedKeys } from "./page-contract.mjs";

const root = new URL("../../", import.meta.url);
const contract = JSON.parse(readFileSync(new URL("tests/contracts/roll-page.json", root), "utf8"));
const page = readFileSync(new URL("src/ipad/App/Web/roll.js", root), "utf8");
const roll = readFileSync(new URL("src/shared/Scripts/roll/piano-roll.ts", root), "utf8");
/** PianoRoll and every part of the roll that may call its host for it. */
const rollFolder = new URL("src/shared/Scripts/roll/", root);
const callers = readdirSync(rollFolder).filter((file) => file.endsWith(".ts")).map((file) => readFileSync(new URL(file, rollFolder), "utf8")).join("\n");
const viewport = readFileSync(new URL("src/shared/Scripts/roll/roll-viewport.ts", root), "utf8");

const sorted = (list) => [...list].sort();
const kindOf = (message) => Object.keys(message)[0];
const isObject = (value) => value !== null && typeof value === "object" && !Array.isArray(value);

test("the page offers exactly the functions the app's RollCommand names", () => {
    assert.deepEqual(sorted(offered(page, "roll")), sorted(contract.commands));
});

/** Whether the dotted path lies below `above` ("" is the argument itself). */
const below = (path, above) => (above === "" ? path !== "" : path.startsWith(`${above}.`));

test("every function has an example argument, with exactly the fields the page reads, at every depth", () => {
    assert.deepEqual(sorted(Object.keys(contract.arguments)), sorted(contract.commands));
    for (const name of contract.commands) {
        const reads = readPaths(page, name);
        const fields = fieldPaths(contract.arguments[name]);
        for (const read of reads.filter((path) => path !== "")) {
            assert.ok(fields.includes(read), `${name} reads ${read}, which its example lacks`);
        }
        // A field is read, or lies under one the page reads and passes on whole (the labels, the frame, the tuplets).
        const whole = reads.filter((read) => !reads.some((other) => below(other, read)));
        for (const field of fields) {
            const read = reads.includes(field) || reads.some((path) => below(path, field)) || whole.some((path) => below(field, path));
            assert.ok(read, `${name}'s example has ${field}, which the page does not read`);
        }
    }
});

test("the answers kept are of the page's functions, and load answers the fields it returns", () => {
    for (const name of Object.keys(contract.answers)) {
        assert.ok(contract.commands.includes(name), `${name} answers`);
    }
    assert.deepEqual(sorted(Object.keys(contract.answers.load)), sorted(returnedKeys(page, "load")));
});

test("the page posts only the message kinds the app's RollEvent decodes, and every one of them", () => {
    assert.deepEqual(sorted(posted(page)), sorted(contract.events));
});

test("every message kind has an example, and each message the page writes out has one with exactly its fields", () => {
    const examples = contract.messages;
    for (const message of examples) {
        assert.equal(Object.keys(message).length, 1, `one kind a message: ${JSON.stringify(message)}`);
        assert.ok(contract.events.includes(kindOf(message)), `a kind of the contract: ${kindOf(message)}`);
    }
    assert.deepEqual(sorted(new Set(examples.map(kindOf))), sorted(contract.events));
    const written = postedObjects(page).filter((object) => object.fields !== null);
    for (const { kind, fields } of written) {
        const matching = examples.filter((m) => kindOf(m) === kind && isObject(m[kind]));
        assert.ok(matching.some((m) => String(sorted(Object.keys(m[kind]))) === String(sorted(fields))), `an example of ${kind} with ${fields}`);
    }
    for (const message of examples) {
        const kind = kindOf(message);
        const literals = written.filter((object) => object.kind === kind);
        if (literals.length > 0) {
            const keys = String(sorted(Object.keys(message[kind])));
            assert.ok(literals.some((object) => String(sorted(object.fields)) === keys), `the page posts ${kind} with ${keys}`);
        }
    }
});

test("the zoom's example has the fields PianoRoll reports (OnRollZoom, passed on as it is)", () => {
    assert.match(roll, /const now: AxisZoom = this\.view\.axisZoom\(/, "piano-roll.ts reports the viewport's axisZoom as `now`");
    const at = /axisZoom\([^)]*\): AxisZoom \{[^]*?return \{/.exec(viewport);
    assert.ok(at, "roll-viewport.ts's axisZoom returns the zoom as an object");
    const zoom = contract.messages.find((message) => kindOf(message) === "zoom").zoom;
    assert.deepEqual(sorted(Object.keys(zoom)), sorted(keysOf(viewport, at.index + at[0].length - 1)));
});

test("roll.js passes on the host callbacks the contract lists and drops only the listed ones", () => {
    const passedOn = [...page.matchAll(/case\s+"(On\w+)"/g)].map((m) => m[1]);
    assert.deepEqual(sorted(passedOn), sorted(contract.hostCallbacks.passedOn));
    const called = new Set([...callers.matchAll(/(?:invokeMethodAsync|host\.post)\("(\w+)"/g)].map((m) => m[1]));
    assert.deepEqual(sorted(called), sorted([...contract.hostCallbacks.passedOn, ...contract.hostCallbacks.dropped]));
});

test("only RollPage builds a call of the roll's page", () => {
    const folder = new URL("src/ipad/App/MotiffLab/", root);
    const builders = readdirSync(folder, { recursive: true })
        .filter((file) => file.endsWith(".swift") && /["(]return roll\./.test(readFileSync(new URL(file, folder), "utf8")));
    assert.deepEqual(builders, []);
    assert.match(readFileSync(new URL("Platform/RollPage.swift", folder), "utf8"), /pageScript\("roll"/);
});

test("the readers see fields through shorthand keys, calls and nested objects, and leave spreads out", () => {
    const source = "post({ seek: { tick, play: Boolean(play) } }); post({ zoom: args[0] }); post({ moved: { notes, gesture } });"
        + " post({ edit: { kind: \"add\", ...rest, length: f(a, b) } });";
    assert.deepEqual(postedObjects(source), [
        { kind: "seek", fields: ["tick", "play"] },
        { kind: "zoom", fields: null },
        { kind: "moved", fields: ["notes", "gesture"] },
        { kind: "edit", fields: ["kind", "length"] },
    ]);
    const functions = "function edit({ on, tool, picked }) { view.set(on, picked?.id); } function lanes(on) { view.setLanes(on); }"
        + " function load(x) { if (x) { f({ a }); } return { colourBy: c, modes }; }"
        + " function lit({ places, span }) { const chosen = places ?? []; view.set(chosen.flatMap((place) => [place.onset, place.ids.at(0)]), span.end); }";
    assert.deepEqual(readPaths(functions, "edit"), ["on", "picked", "picked.id", "tool"]);
    assert.deepEqual(readPaths(functions, "lanes"), [""]);
    assert.deepEqual(readPaths(functions, "lit"), ["places", "places.[]", "places.[].ids", "places.[].onset", "span", "span.end"]);
    assert.equal(readPaths(functions, "missing"), null);
    assert.deepEqual(fieldPaths({ places: [{ onset: 0, ids: ["n1"] }], span: { end: 1 }, on: true }),
        ["places", "places.[]", "places.[].onset", "places.[].ids", "span", "span.end", "on"]);
    assert.deepEqual(returnedKeys(functions, "load"), ["colourBy", "modes"]);
});
