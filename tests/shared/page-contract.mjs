// Readers of a page's source for the pages' contract tests (decision record 0031): score-page-contract.test.mjs reads
// the score page's modules (src/shared/Scripts/score/ipad-page) with them, roll-page-contract.test.mjs
// src/ipad/App/Web/roll.js. They read the source as text (no build needed): the functions the page offers the app, the
// messages it posts and the fields of each.

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

/** The keys of the first object literal a page's function returns (`return { colourBy, modes }`); else null. */
export function returnedKeys(source, name) {
    const at = new RegExp(`function\\s+${name}\\s*\\(`).exec(source)?.index;
    if (at === undefined) return null;
    const match = /\breturn\s*\{/.exec(source.slice(at));
    return match ? keysOf(source, at + match.index + match[0].length - 1) : null;
}

/** The index of the bracket that closes the one at `open` (`(`, `[` or `{`). */
function closing(source, open) {
    let depth = 0;
    for (let i = open; i < source.length; i++) {
        const c = source[i];
        if (c === "(" || c === "[" || c === "{") depth += 1;
        else if (c === ")" || c === "]" || c === "}") {
            depth -= 1;
            if (depth === 0) return i;
        }
    }
    return source.length;
}

/**
 * The fields a page's function reads from its one argument, at any depth, as dotted paths ("" the argument itself, "[]"
 * an element of a list): the names it destructures, and every member read off them, off the argument itself, off a
 * `const` taken from one of them, or off the element a `map`, `flatMap`, `filter`, `forEach`, `some` or `every` callback
 * is given (`places ?? []` then `.flatMap((place) => place.noteIds)` reads "places.[].noteIds"). A member that is called
 * is a method, not a field. Null without the function.
 */
export function readPaths(source, name) {
    const head = new RegExp(`function\\s+${name}\\s*\\(`).exec(source);
    if (!head) return null;
    const open = head.index + head[0].length - 1;
    const close = closing(source, open);
    const parameter = source.slice(open + 1, close).trim();
    const start = source.indexOf("{", close);
    const body = source.slice(start, closing(source, start) + 1);
    const roots = new Map();
    if (parameter.startsWith("{")) {
        for (const key of keysOf(source, source.indexOf("{", open))) roots.set(key, [key]);
    } else if (parameter) {
        roots.set(parameter, []);
    }
    for (const [, alias, from] of body.matchAll(/\bconst\s+(\w+)\s*=\s*(\w+)\b(?!\s*[.(])/g)) {
        if (roots.has(from)) roots.set(alias, roots.get(from));
    }
    for (const [, list, item] of body.matchAll(/\b(\w+)\.(?:flatMap|map|filter|forEach|some|every)\(\s*\(?\s*(\w+)\s*\)?\s*=>/g)) {
        if (roots.has(list)) roots.set(item, [...roots.get(list), "[]"]);
    }
    const paths = new Set([...roots.values()].map((path) => path.join(".")));
    for (const [, root, chain, called] of body.matchAll(/\b(\w+)((?:\??\.\w+)+)(\s*\()?/g)) {
        if (!roots.has(root)) continue;
        const members = chain.split(/\??\./).filter(Boolean);
        if (called) members.pop();
        for (let i = 1; i <= members.length; i++) paths.add([...roots.get(root), ...members.slice(0, i)].join("."));
    }
    return [...paths].sort();
}

/** The dotted paths of an example value's fields, at any depth ("[]" an element of a list of objects). */
export function fieldPaths(value, prefix = []) {
    if (Array.isArray(value)) {
        const objects = value.filter((item) => item !== null && typeof item === "object" && !Array.isArray(item));
        if (objects.length === 0) return [];
        const item = [...prefix, "[]"];
        return [item.join("."), ...new Set(objects.flatMap((object) => fieldPaths(object, item)))];
    }
    if (value === null || typeof value !== "object") return [];
    return Object.entries(value).flatMap(([key, field]) => [[...prefix, key].join("."), ...fieldPaths(field, [...prefix, key])]);
}
