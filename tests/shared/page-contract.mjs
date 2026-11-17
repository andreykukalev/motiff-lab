// Readers of a page's source for the pages' contract tests (decision record 0031): score-page-contract.test.mjs reads
// src/ipad/App/Web/score.js with them, roll-page-contract.test.mjs src/ipad/App/Web/roll.js. They read the source as text
// (no build needed): the functions the page offers the app, the messages it posts and the fields of each.

/** The names a page offers the app, from `window.<object> = { show, eraseAlong: eraseAlongPath, … };`. */
export function offered(source, object) {
    const body = new RegExp(`window\\.${object}\\s*=\\s*\\{([^}]*)\\}`).exec(source)?.[1] ?? "";
    return body.split(",").map((entry) => entry.split(":")[0].trim()).filter(Boolean);
}

/**
 * Each object literal at the top of a `post(…)` or `postMessage(…)` argument: its first key (`kind`), and the keys of that
 * key's value when it is an object literal (`fields`), else null.
 */
export function postedObjects(source) {
    const objects = [];
    for (const call of source.matchAll(/\bpost(?:Message)?\(/g)) {
        let depth = 0;
        for (let i = call.index + call[0].length; i < source.length; i++) {
            const c = source[i];
            if (c === "(" || c === "[") depth += 1;
            else if (c === ")" || c === "]") {
                if (depth === 0) break;
                depth -= 1;
            } else if (c === "{") {
                if (depth === 0) {
                    const match = /^\s*(\w+)(\s*:\s*\{)?/.exec(source.slice(i + 1));
                    if (match) objects.push({ kind: match[1], fields: match[2] ? keysOf(source, i + match[0].length) : null });
                }
                depth += 1;
            } else if (c === "}") depth -= 1;
        }
    }
    return objects;
}

/** The first key of each object literal at the top of a `post(…)` or `postMessage(…)` argument, each once. */
export function posted(source) {
    return [...new Set(postedObjects(source).map((object) => object.kind))];
}

/** The keys of the object literal whose `{` is at `open`: shorthand and `key: value` entries; a `...spread` is left out. */
export function keysOf(source, open) {
    const keys = [];
    const push = (entry) => {
        const key = /^\s*(\w+)/.exec(entry)?.[1];
        if (key) keys.push(key);
    };
    let depth = 0;
    let start = open + 1;
    for (let i = open + 1; i < source.length; i++) {
        const c = source[i];
        if (c === "(" || c === "[" || c === "{") depth += 1;
        else if (c === ")" || c === "]" || c === "}") {
            if (depth === 0) {
                push(source.slice(start, i));
                break;
            }
            depth -= 1;
        } else if (c === "," && depth === 0) {
            push(source.slice(start, i));
            start = i + 1;
        }
    }
    return keys;
}

/** The fields a page's function reads from its one argument when it destructures it (`function load({ roll, … })`); else null. */
export function parameterKeys(source, name) {
    const match = new RegExp(`function\\s+${name}\\s*\\(\\s*\\{`).exec(source);
    return match ? keysOf(source, match.index + match[0].length - 1) : null;
}

/** The keys of the first object literal a page's function returns (`return { colourBy, modes }`); else null. */
export function returnedKeys(source, name) {
    const at = new RegExp(`function\\s+${name}\\s*\\(`).exec(source)?.index;
    if (at === undefined) return null;
    const match = /\breturn\s*\{/.exec(source.slice(at));
    return match ? keysOf(source, at + match.index + match[0].length - 1) : null;
}
