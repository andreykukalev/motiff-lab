// A component's string parameter written without "@" is text, not C#: <PlayerAvatar Name="_player" /> gave the avatar the
// word "_player", so the header always showed "P" whatever the player was called (Andrei, 2026-11-11). Every other
// parameter type reads the value as C#, which is why the same spelling works there. A field's name ("_" first) is never
// meant as text, so a string parameter given one is this mistake.
//   node --test tests/web

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { basename } from "node:path";
import { test } from "node:test";

const web = new URL("../../src/MotiffLab.Web/", import.meta.url);

function sources(dir, found = []) {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
        if (entry.isDirectory() && !["bin", "obj", "wwwroot", "node_modules"].includes(entry.name)) {
            sources(new URL(`${entry.name}/`, dir), found);
        } else if (entry.name.endsWith(".razor") || entry.name.endsWith(".razor.cs")) {
            found.push({ name: entry.name, text: readFileSync(new URL(entry.name, dir), "utf8") });
        }
    }
    return found;
}

/** Each component's string parameters, by component name (the file's name). */
function stringParameters(files) {
    const byComponent = new Map();
    for (const { name, text } of files) {
        const component = basename(name).replace(/\.razor(\.cs)?$/, "");
        for (const match of text.matchAll(/\[Parameter(?:\([^)]*\))?\]\s*public\s+string\??\s+(\w+)\s*\{/g)) {
            if (!byComponent.has(component)) byComponent.set(component, new Set());
            byComponent.get(component).add(match[1]);
        }
    }
    return byComponent;
}

/** Each place a component's string parameter is given a field's name as text: "<file>: <Component Param="_field">". */
function fieldNamesAsText(files) {
    const parameters = stringParameters(files);
    const found = [];
    for (const { name, text } of files) {
        for (const [component, names] of parameters) {
            for (const tag of text.matchAll(new RegExp(`<(?:\\w+\\.)*${component}\\b[^<]*`, "g"))) {
                for (const attribute of tag[0].matchAll(/\b(\w+)="(_\w*)"/g)) {
                    if (names.has(attribute[1])) found.push(`${name}: <${component} ${attribute[1]}="${attribute[2]}">`);
                }
            }
        }
    }
    return found;
}

test("finds the mistake it guards against", () => {
    const files = [
        { name: "PlayerAvatar.razor", text: "@code {\n    [Parameter]\n    public string? Name { get; set; }\n}" },
        { name: "AccountButton.razor", text: '<button class="avatar"><PlayerAvatar Name="_player" Photo="@_photo" /></button>' },
        { name: "AccountPanel.razor", text: '<PlayerAvatar Name="@_player" />' },
    ];
    assert.deepEqual(fieldNamesAsText(files), ['AccountButton.razor: <PlayerAvatar Name="_player">']);
});

test("no component's string parameter is given a field's name as text (write @_field)", () => {
    const files = sources(web);
    assert.ok(stringParameters(files).get("PlayerAvatar")?.has("Name"), "no string parameters found: the pattern no longer matches");
    assert.deepEqual(fieldNamesAsText(files), []);
});
