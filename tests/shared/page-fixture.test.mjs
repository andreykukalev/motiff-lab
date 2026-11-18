// The page scenarios' fixture script (tests/pages/fixture.mjs) without the dotnet command: it fails with one line that
// says what it needs and how to put it on PATH, not with a raw ENOENT. In Node, after `dotnet build src/shared`:
//   node --test tests/shared
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const fixture = fileURLToPath(new URL("../pages/fixture.mjs", import.meta.url));

test("the fixture check without dotnet on PATH says in one line what it needs", () => {
    const empty = mkdtempSync(join(tmpdir(), "no-dotnet-"));
    try {
        const run = spawnSync(process.execPath, [fixture, "--check"], { encoding: "utf8", env: { ...process.env, PATH: empty } });
        assert.equal(run.status, 1);
        const lines = `${run.stdout}${run.stderr}`.trim().split("\n");
        assert.equal(lines.length, 1, lines.join("\n"));
        assert.match(lines[0], /needs the dotnet command .* PATH/);
        assert.doesNotMatch(lines[0], /ENOENT/);
    } finally {
        rmSync(empty, { recursive: true });
    }
});
