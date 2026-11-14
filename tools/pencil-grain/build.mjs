// The grain of the pencil on the iPad (#479 item 7): one tile, ipad/App/Web/grain.png, that the score page tiles over its
// pencil marks (Web/score.html) and Swift over the stroke being written and the marks on a PDF's pages (InkInput.swift,
// InkPaint), so a stroke shows the same grain before and after the Pencil lifts.
//   96 x 96 pixels, drawn as 48 page pixels: white, its alpha the grain (1 where the graphite covers, lower in the
//   speckles of paper a pencil leaves). Each row is the same as the one as far from the other edge, so the tile is the
//   same drawn upside down (a PDF's page and UIKit count y the other way from the web page), and it tiles both ways.
// Run after changing it:   node tools/pencil-grain/build.mjs
// The tile is the same on every run: the noise comes from a fixed seed.

import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { deflateSync } from "node:zlib";

export const SIZE = 96;
export const FILE = "ipad/App/Web/grain.png";

/** A small seeded generator (mulberry32): the same numbers on every run. */
function generator(seed) {
    let state = seed >>> 0;
    return () => {
        state = (state + 0x6d2b79f5) >>> 0;
        let t = state;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

/** Noise on a coarse grid that wraps across, eased between its points. */
function coarse(random, cell) {
    const columns = SIZE / cell;
    const rows = SIZE / 2 / cell + 1;
    const grid = Array.from({ length: rows }, () => Array.from({ length: columns }, random));
    return (x, y) => {
        const gx = x / cell;
        const gy = y / cell;
        const x0 = Math.floor(gx);
        const y0 = Math.floor(gy);
        const tx = gx - x0;
        const ty = gy - y0;
        const at = (cx, cy) => grid[Math.min(cy, rows - 1)][cx % columns];
        const top = at(x0, y0) * (1 - tx) + at(x0 + 1, y0) * tx;
        const bottom = at(x0, y0 + 1) * (1 - tx) + at(x0 + 1, y0 + 1) * tx;
        return top * (1 - ty) + bottom * ty;
    };
}

/** The alpha of every pixel, row by row, the bottom half the top half turned over. */
export function grain(seed = 479) {
    const random = generator(seed);
    const lumps = coarse(random, 4);
    const half = [];
    for (let y = 0; y < SIZE / 2; y++) {
        const row = [];
        for (let x = 0; x < SIZE; x++) {
            const noise = 0.75 * random() + 0.25 * lumps(x, y);
            // Most of the line is graphite; where the noise is high, paper shows through.
            const paper = Math.max(0, noise - 0.62) / 0.38;
            row.push(Math.round(255 * Math.max(0.45, 1 - 1.2 * paper)));
        }
        half.push(row);
    }
    return [...half, ...[...half].reverse()];
}

/** The tile as a PNG: white with the grain's alpha (8-bit grey and alpha). */
export function png(rows) {
    const raw = Buffer.alloc(rows.length * (1 + SIZE * 2));
    let at = 0;
    for (const row of rows) {
        raw[at++] = 0;
        for (const alpha of row) {
            raw[at++] = 255;
            raw[at++] = alpha;
        }
    }
    const header = Buffer.alloc(13);
    header.writeUInt32BE(SIZE, 0);
    header.writeUInt32BE(rows.length, 4);
    header.set([8, 4, 0, 0, 0], 8);
    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk("IHDR", header),
        chunk("IDAT", deflateSync(raw, { level: 9 })),
        chunk("IEND", Buffer.alloc(0)),
    ]);
}

function chunk(type, data) {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, crc]);
}

function crc32(bytes) {
    let crc = 0xffffffff;
    for (const byte of bytes) {
        crc ^= byte;
        for (let bit = 0; bit < 8; bit++) {
            crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
        }
    }
    return (crc ^ 0xffffffff) >>> 0;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
    const root = fileURLToPath(new URL("../../", import.meta.url));
    writeFileSync(`${root}${FILE}`, png(grain()));
    console.log(`${FILE} written`);
}
