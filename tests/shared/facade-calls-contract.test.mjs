// The facade's call names (audit C2, blueprint 5.4): every call name the iPad app's Swift sends to music-core must be one
// the facade answers, as tests/contracts/facade-calls.json lists them. FacadeCallsContractTests (xUnit) keeps that file
// equal to what MusicCoreFacade.Call really dispatches; this test reads the Swift sources under src/ipad/App/MotiffLab,
// every folder below it, so a call renamed or removed in C# fails here on Linux and not only on the iPad.
//   node --test tests/shared   (needs no build: it reads the Swift sources)
//
// The port (step B5, decision record 0031 step 1b, #654): every call by name goes through PieceCore, and only its files
// (`portFiles`) reach MusicCore.call, the one function that reaches `ml_call`. Anywhere else, MusicCore.call (spelled
// `MusicCore.call`, or `Self.call`, `self.call` or `call` inside `enum`/`extension MusicCore`), `ml_call` or a port enum
// of calls made from a name (`PieceCore.Call(rawValue:)`) fails, whatever it is called through; the features name a call
// by a case of the port's enums. `MusicCore.run { … }` only runs work on the core's thread, `facadeRequest` only encodes
// the request's fields, `MusicCore.open` and `MusicCore.close` are `ml_open` and `ml_close`, not calls by name.
//
// In the port, the name in MusicCore.call's name place is read as:
//   1. a literal, a choice of literals (`cond ? "a" : "b"`), or a local `let name = <literal or choice>` assigned nowhere else;
//   2. `call.rawValue` of a parameter typed as one of the port's enums of calls (`enum Call: String`): all its cases' names;
//   3. a parameter of a private function of the port: the names its sites in the same file pass (as 1 and 2, or another
//      such parameter), by position for an unlabelled parameter and by label for a labelled one, or its default.
// Anything else is unread and fails: a name built from parts, a `var`, a port function that is not private taking a name
// (the features could hand it any name), a helper or MusicCore.call passed as a function value, a helper reached through
// another value or sharing its name with another function of its file. The piece argument is followed too: a literal 0 is
// a call on piece 0, anything else a call on an open piece.

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { basename, join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const swiftRoot = join(root, "src", "ipad", "App", "MotiffLab");
const contract = JSON.parse(readFileSync(join(root, "tests", "contracts", "facade-calls.json"), "utf8"));

/** The port's files (src/ipad/App/MotiffLab/Platform): the only ones that call music-core by name. */
const portFiles = ["PieceCore.swift", "PieceCoreCalls.swift"];

// ---------------------------------------------------------------------------------------------------------------------
// A small Swift reader: comments and the inside of strings masked, string literals kept, brackets matched.

/**
 * The source with comments and the text of string literals replaced by spaces and underscores (same length, same line
 * breaks, interpolations `\( … )` kept as code), and the string literals with their place and text.
 */
function lex(source) {
    const code = source.split("");
    const strings = [];
    const blank = (from, to) => {
        for (let k = from; k < to; k++) if (code[k] !== "\n") code[k] = " ";
    };
    const n = source.length;

    function readString(start) {
        // At `#…#"` or `"`: raw hashes, then `"""` or `"`.
        let j = start;
        let hashes = 0;
        while (source[j] === "#") { hashes++; j++; }
        const multi = source.startsWith('"""', j);
        j += multi ? 3 : 1;
        const close = (multi ? '"""' : '"') + "#".repeat(hashes);
        const escape = "\\" + "#".repeat(hashes);
        let text = "";
        let interpolated = false;
        while (j < n) {
            if (source.startsWith(close, j)) {
                const end = j + close.length;
                strings.push({ start, end, text, interpolated });
                return end;
            }
            if (source.startsWith(escape, j)) {
                const after = j + escape.length;
                if (source[after] === "(") {
                    interpolated = true;
                    code.fill("_", j, after);
                    j = readCode(after + 1, ")");
                    continue;
                }
                text += source[after] ?? "";
                code.fill("_", j, after + 1);
                j = after + 1;
                continue;
            }
            text += source[j];
            if (source[j] !== "\n") code[j] = "_";
            j++;
        }
        return j;
    }

    function readCode(from, until) {
        // Code from `from` up to the bracket that closes `until` (or the end); returns the index after it.
        let depth = 0;
        let j = from;
        while (j < n) {
            const c = source[j];
            if (c === "/" && source[j + 1] === "/") {
                const end = source.indexOf("\n", j);
                const stop = end < 0 ? n : end;
                blank(j, stop);
                j = stop;
                continue;
            }
            if (c === "/" && source[j + 1] === "*") {
                let level = 1;
                let k = j + 2;
                while (k < n && level > 0) {
                    if (source.startsWith("/*", k)) { level++; k += 2; } else if (source.startsWith("*/", k)) { level--; k += 2; } else k++;
                }
                blank(j, k);
                j = k;
                continue;
            }
            if (c === '"' || (c === "#" && /^#+"/.test(source.slice(j, j + 8)))) {
                j = readString(j);
                continue;
            }
            if (until && (c === "(" || c === "[" || c === "{")) depth++;
            if (until && (c === ")" || c === "]" || c === "}")) {
                if (depth === 0) return j + 1;
                depth--;
            }
            j++;
        }
        return j;
    }

    readCode(0, null);
    return { code: code.join(""), strings, source };
}

/** The index of the bracket that closes the one at `open` in masked code. */
function closing(code, open) {
    let depth = 0;
    for (let j = open; j < code.length; j++) {
        const c = code[j];
        if (c === "(" || c === "[" || c === "{") depth++;
        else if (c === ")" || c === "]" || c === "}") {
            depth--;
            if (depth === 0) return j;
        }
    }
    return code.length;
}

/** The arguments between the bracket at `open` and its match: each with its label (or null), its text and its place. */
function argumentsAt(code, open) {
    const end = closing(code, open);
    const parts = [];
    let depth = 0;
    let from = open + 1;
    for (let j = open + 1; j <= end; j++) {
        const c = code[j];
        if (j === end || (c === "," && depth === 0)) {
            const raw = code.slice(from, j);
            if (raw.trim()) {
                const lead = raw.length - raw.trimStart().length;
                const label = /^\s*([A-Za-z_]\w*)\s*:(?!:)/.exec(raw);
                const valueFrom = from + (label ? label[0].length : lead);
                const text = code.slice(valueFrom, j).trim();
                const at = valueFrom + (code.slice(valueFrom, j).length - code.slice(valueFrom, j).trimStart().length);
                parts.push({ label: label ? label[1] : null, text, at, end: at + text.length });
            }
            from = j + 1;
            continue;
        }
        if (c === "(" || c === "[" || c === "{") depth++;
        else if (c === ")" || c === "]" || c === "}") depth--;
    }
    return { parts, end };
}

/** The types (class, struct, enum, actor, protocol, extension) with their kind and the span of their bodies. */
function typesIn(code) {
    const types = [];
    for (const match of code.matchAll(/\b(class|struct|enum|actor|protocol|extension)\s+([A-Za-z_][\w.]*)[^{};=]*\{/g)) {
        const open = match.index + match[0].length - 1;
        types.push({ kind: match[1], name: match[2].split(".").pop(), from: open, to: closing(code, open) });
    }
    return types;
}

/** The functions declared in a file: name, parameters (label, name, type, default), body span, privacy. */
function functionsIn(file, lexed) {
    const { code } = lexed;
    const functions = [];
    for (const match of code.matchAll(/\bfunc\s+([A-Za-z_]\w*)\s*(?:<[^>{}]*>)?\s*\(/g)) {
        const open = match.index + match[0].length - 1;
        const { parts, end } = argumentsAt(code, open);
        const params = parts.map((part) => {
            const full = part.label ? `${part.label}: ${part.text}` : part.text;
            const head = /^\s*(?:([A-Za-z_]\w*)\s+)?([A-Za-z_]\w*)\s*:/.exec(full);
            const equals = indexAtDepth0(part.text, "=");
            const defaultText = equals < 0 ? null : part.text.slice(equals + 1).trim();
            const defaultAt = equals < 0 ? -1 : part.at + equals + 1 + (part.text.slice(equals + 1).length - part.text.slice(equals + 1).trimStart().length);
            const declared = head ? full.slice(head[0].length) : "";
            const typeEnd = indexAtDepth0(declared, "=");
            return {
                label: head ? (head[1] ?? head[2]) : null,
                name: head ? head[2] : null,
                type: head ? (typeEnd < 0 ? declared : declared.slice(0, typeEnd)).trim().split(".").pop() : null,
                defaultText,
                defaultAt,
            };
        });
        const rest = code.slice(end + 1, end + 400);
        const brace = rest.indexOf("{");
        const between = brace < 0 ? rest : rest.slice(0, brace);
        const hasBody = brace >= 0 && !/[}=]|\b(?:func|var|let|case)\b/.test(between);
        const bodyFrom = hasBody ? end + 1 + brace : -1;
        const bodyTo = hasBody ? closing(code, bodyFrom) : -1;
        const lineStart = Math.max(code.lastIndexOf("\n", match.index), code.lastIndexOf(";", match.index), code.lastIndexOf("}", match.index), code.lastIndexOf("{", match.index)) + 1;
        const modifiers = code.slice(lineStart, match.index);
        functions.push({
            file,
            name: match[1],
            at: match.index,
            params,
            bodyFrom,
            bodyTo,
            isPrivate: /\b(?:private|fileprivate)\b/.test(modifiers),
        });
    }
    return functions;
}

/** The first `char` outside brackets in `text` (an `=` of `==`, `!=`, `<=`, `>=` is not one), or -1. */
function indexAtDepth0(text, char) {
    let depth = 0;
    for (let j = 0; j < text.length; j++) {
        const c = text[j];
        if (c === "(" || c === "[" || c === "{") depth++;
        else if (c === ")" || c === "]" || c === "}") depth--;
        else if (c === char && depth === 0 && text[j + 1] !== "=" && text[j - 1] !== "=" && text[j - 1] !== "!" && text[j - 1] !== "<" && text[j - 1] !== ">") return j;
    }
    return -1;
}

/** The literal's text when the masked expression at `at` is exactly one string literal without interpolation. */
function literalAt(lexed, at, end) {
    const literal = lexed.strings.find((s) => s.start === at);
    return literal && literal.end === end && !literal.interpolated ? literal.text : null;
}

/**
 * The names an expression stands for: one literal, a choice of literals (`c ? "a" : "b"`, nested), or null when it is
 * neither. `at` and `end` are its place in the file.
 */
function literalsOf(lexed, at, end) {
    const text = lexed.code.slice(at, end);
    const one = literalAt(lexed, at, end);
    if (one !== null) return [one];
    const question = indexAtDepth0(text, "?");
    if (question < 0) return null;
    const branches = text.slice(question + 1);
    const colon = indexAtDepth0(branches, ":");
    if (colon < 0) return null;
    const trim = (from, to) => {
        const slice = lexed.code.slice(from, to);
        const lead = slice.length - slice.trimStart().length;
        return [from + lead, from + lead + slice.trim().length];
    };
    const [a1, a2] = trim(at + question + 1, at + question + 1 + colon);
    const [b1, b2] = trim(at + question + 1 + colon + 1, end);
    const first = literalsOf(lexed, a1, a2);
    const second = literalsOf(lexed, b1, b2);
    return first && second ? [...first, ...second] : null;
}

// ---------------------------------------------------------------------------------------------------------------------
// The names Swift sends.

/** The Swift files under `dir`, every folder below it. */
function swiftFiles(dir) {
    return readdirSync(dir, { withFileTypes: true, recursive: true })
        .filter((entry) => entry.isFile() && entry.name.endsWith(".swift"))
        .map((entry) => join(entry.parentPath ?? entry.path, entry.name))
        .sort();
}

/** The camel-cased call name ("sources.view.hide" → "sourcesViewHide"), from its `from`th part on. */
function camel(name, from = 0) {
    return name.split(".").slice(from).map((part, k) => (k === 0 ? part : part[0].toUpperCase() + part.slice(1))).join("");
}

/** The enums of calls a port file declares (`enum Call: String { case check = "check", … }`): name → its cases. */
function callEnums(lexed) {
    const enums = new Map();
    for (const match of lexed.code.matchAll(/\benum\s+([A-Za-z_]\w*)\s*:\s*String\b[^{]*\{/g)) {
        const open = match.index + match[0].length - 1;
        const end = closing(lexed.code, open);
        const cases = [];
        for (const item of lexed.code.slice(open, end).matchAll(/(?:\bcase\b|,)\s*([A-Za-z_]\w*)\s*=\s*/g)) {
            const at = open + item.index + item[0].length;
            const literal = lexed.strings.find((s) => s.start === at && !s.interpolated);
            if (literal) cases.push({ name: item[1], call: literal.text });
        }
        enums.set(match[1], cases);
    }
    return enums;
}

/**
 * Every call name the sources send from the port, with where and on which piece ("piece0" for a literal 0, "open"
 * otherwise); the port's places the reader cannot follow; the places outside the port that reach music-core by name;
 * and the port's enums of calls. `sources` maps a file's path to its text; a place is named by the file's name.
 */
function sentNames(sources) {
    const files = Object.entries(sources).map(([file, text]) => ({ file, lexed: lex(text), port: portFiles.includes(basename(file)) }));
    const functions = files.flatMap(({ file, lexed }) => functionsIn(file, lexed));
    const enums = new Map(files.filter((f) => f.port).flatMap((f) => [...callEnums(f.lexed)]));
    const sent = [];
    const unread = [];
    const outside = [];
    const carriers = new Set();
    const queue = [];

    /** The innermost function whose body holds `at` in `file`. */
    const enclosing = (file, at) => functions
        .filter((f) => f.file === file && f.bodyFrom < at && at < f.bodyTo)
        .sort((a, b) => b.bodyFrom - a.bodyFrom)[0] ?? null;
    const placeOf = (file, at) => `${basename(file)} › ${enclosing(file, at)?.name ?? "(top level)"}`;
    const parameterOf = (fn, text) => (fn && /^[A-Za-z_]\w*$/.test(text) ? fn.params.findIndex((p) => p.name === text) : -1);

    /**
     * The one `let` of `name` in the function's body set to literals: those names; null when there is none, or a `var`,
     * a second declaration or any later assignment to it, since the name could then be another.
     */
    function localLiterals(lexed, fn, name) {
        if (!fn || !/^[A-Za-z_]\w*$/.test(name)) return null;
        const body = lexed.code.slice(fn.bodyFrom, fn.bodyTo);
        const declarations = [...body.matchAll(new RegExp(`\\b(let|var)\\s+${name}\\b`, "g"))];
        const assignments = [...body.matchAll(new RegExp(`(?<![\\w.])${name}\\s*=(?!=)`, "g"))];
        if (declarations.length !== 1 || declarations[0][1] !== "let" || assignments.length !== 1) return null;
        const found = new RegExp(`\\blet\\s+${name}\\s*(?::\\s*String\\s*)?=\\s*`).exec(body);
        if (!found) return null;
        const from = fn.bodyFrom + found.index + found[0].length;
        const stop = /[;\n]|$/.exec(lexed.code.slice(from)).index;
        return literalsOf(lexed, from, from + lexed.code.slice(from, from + stop).trimEnd().length);
    }

    /** `p.rawValue` of the function's parameter `p` typed as an enum of calls: its cases' names; else null. */
    function rawValues(fn, text) {
        const match = /^([A-Za-z_]\w*)\s*\.\s*rawValue$/.exec(text);
        const param = match && fn ? fn.params.find((p) => p.name === match[1]) : null;
        return param && enums.has(param.type) ? enums.get(param.type).map((c) => c.call) : null;
    }

    /** The name in a site's name argument (in the port): its names are sent, a private function's parameter is followed. */
    function nameAt(file, lexed, arg, piece, through) {
        const fn = enclosing(file, arg.at);
        const names = literalsOf(lexed, arg.at, arg.end) ?? localLiterals(lexed, fn, arg.text) ?? rawValues(fn, arg.text);
        if (names) {
            for (const name of names) sent.push({ name, piece: typeof piece === "string" ? piece : "open", where: `${placeOf(file, arg.at)} (${through})`, file });
            return;
        }
        const param = parameterOf(fn, arg.text);
        if (param < 0) {
            unread.push(`${placeOf(file, arg.at)} › ${lexed.source.slice(arg.at, arg.end)}`);
        } else if (!fn.isPrivate) {
            unread.push(`${placeOf(file, arg.at)} › takes a call's name and is not private`);
        } else if (!carriers.has(fn)) {
            carriers.add(fn);
            queue.push({ fn, nameParam: param, piece });
        }
    }

    /** The piece a call names: "piece0" for a literal 0, the enclosing function's parameter, or "open". */
    function pieceAt(file, arg) {
        if (arg?.text === "0") return "piece0";
        const param = arg ? parameterOf(enclosing(file, arg.at), arg.text) : -1;
        return param >= 0 ? { param } : "open";
    }

    /** The argument a site passes for a parameter: by label, or by place among the unlabelled ones. */
    function argumentFor(fn, index, parts) {
        const param = fn.params[index];
        return param.label !== "_"
            ? parts.find((a) => a.label === param.label)
            : parts.filter((a) => a.label === null)[fn.params.slice(0, index).filter((p) => p.label === "_").length];
    }

    for (const { file, lexed, port } of files) {
        const cores = typesIn(lexed.code).filter((t) => t.name === "MusicCore");
        // MusicCore.call itself, as `MusicCore.call`, or as `Self.call`, `self.call` or `call` inside MusicCore.
        for (const match of lexed.code.matchAll(/(?:\b([A-Za-z_]\w*)\s*\.\s*)?\bcall\b(\s*\()?/g)) {
            const qualifier = match[1] ?? (/\.\s*$/.test(lexed.code.slice(0, match.index)) ? "?" : null);
            const insideCore = cores.some((t) => t.from < match.index && match.index < t.to);
            const facade = qualifier === "MusicCore" || (insideCore && (qualifier === "Self" || qualifier === "self" || (qualifier === null && match[2])));
            if (!facade || /\bfunc\s*$/.test(lexed.code.slice(Math.max(0, match.index - 8), match.index))) continue;
            if (!port) {
                outside.push(`${placeOf(file, match.index)} › MusicCore.call`);
                continue;
            }
            const parts = match[2] ? argumentsAt(lexed.code, match.index + match[0].length - 1).parts : [];
            const nameArg = parts.filter((p) => p.label === null)[0];
            if (!nameArg) {
                unread.push(`${placeOf(file, match.index)} › ${match[2] ? "MusicCore.call without a name" : "MusicCore.call passed as a value"}`);
                continue;
            }
            nameAt(file, lexed, nameArg, pieceAt(file, parts.find((p) => p.label === "piece")), "MusicCore.call");
        }
        if (port || basename(file) === "MusicCore.swift") continue;
        for (const match of lexed.code.matchAll(/\bml_call\b/g)) outside.push(`${placeOf(file, match.index)} › ml_call`);
        for (const match of lexed.code.matchAll(/\b([A-Za-z_]\w*)\s*(?:\.\s*init\s*)?\(\s*rawValue\s*:/g)) {
            if (enums.has(match[1])) outside.push(`${placeOf(file, match.index)} › ${match[1]}(rawValue:)`);
        }
    }

    // The port's private helpers, followed in their own file until no new one turns up: reached unqualified or through
    // `self` or `Self`, never as a value, through another value, or beside another function of the same name.
    while (queue.length > 0) {
        const { fn, nameParam, piece } = queue.shift();
        const { lexed } = files.find((f) => f.file === fn.file);
        const param = fn.params[nameParam];
        if (param.defaultText !== null) {
            const names = literalsOf(lexed, param.defaultAt, param.defaultAt + param.defaultText.length);
            if (names) for (const name of names) sent.push({ name, piece: typeof piece === "string" ? piece : "open", where: `${placeOf(fn.file, fn.bodyFrom + 1)} (default of ${param.name})`, file: fn.file });
            else unread.push(`${placeOf(fn.file, fn.bodyFrom + 1)} › default ${param.defaultText}`);
        }
        if (functions.some((f) => f !== fn && f.file === fn.file && f.name === fn.name)) {
            unread.push(`${placeOf(fn.file, fn.bodyFrom + 1)} › another function is named ${fn.name} too`);
            continue;
        }
        for (const match of lexed.code.matchAll(new RegExp(`(?:\\b([A-Za-z_]\\w*)\\s*\\.\\s*)?\\b${fn.name}\\b(\\s*\\()?`, "g"))) {
            if (/\bfunc\s*$/.test(lexed.code.slice(Math.max(0, match.index - 8), match.index))) continue;
            const qualifier = match[1] ?? (/\.\s*$/.test(lexed.code.slice(0, match.index)) ? "?" : null);
            if (!match[2] || (qualifier !== null && qualifier !== "self" && qualifier !== "Self")) {
                unread.push(`${placeOf(fn.file, match.index)} › ${fn.name} ${match[2] ? "called through another value" : "passed as a value"}`);
                continue;
            }
            const { parts } = argumentsAt(lexed.code, match.index + match[0].length - 1);
            const arg = argumentFor(fn, nameParam, parts);
            if (!arg) continue; // the parameter's default, read above
            const pieceArg = typeof piece === "string" ? null : argumentFor(fn, piece.param, parts);
            nameAt(fn.file, lexed, arg, typeof piece === "string" ? piece : pieceArg ? pieceAt(fn.file, pieceArg) : "open", fn.name);
        }
    }

    return { sent, unread, outside, enums };
}

// ---------------------------------------------------------------------------------------------------------------------

const sources = Object.fromEntries(swiftFiles(swiftRoot).map((file) => [file, readFileSync(file, "utf8")]));
const { sent, unread, outside, enums } = sentNames(sources);
const calls = new Map(contract.calls.map((call) => [call.name, call]));

test("every call name the Swift sends is one the facade answers", () => {
    const unknown = sent.filter((s) => !calls.has(s.name)).map((s) => `${s.name}  ← ${s.where}`);
    assert.deepEqual(unknown, [], "names not in tests/contracts/facade-calls.json");
});

test("every call is sent to a piece the facade answers it on", () => {
    const answered = (s) => (s.piece === "piece0" ? ["piece0"] : ["piece", "pdf"]).some((on) => calls.get(s.name).on.includes(on));
    const misplaced = sent
        .filter((s) => calls.has(s.name) && !answered(s))
        .map((s) => `${s.name} on ${s.piece === "piece0" ? "piece 0" : "an open piece"}  ← ${s.where}  (the facade answers it on ${calls.get(s.name).on.join(", ")})`);
    assert.deepEqual(misplaced, []);
});

test("no file but the port calls music-core by name", () => {
    assert.deepEqual(outside, [], "make these calls through PieceCore (a case of PieceCore.Call or PieceCore.NoPiece)");
});

test("the reader follows every call the port makes", () => {
    assert.deepEqual(unread, [], "write the call's name where the port makes it, or as a case of its enums of calls");
});

test("each case of the port's enums of calls is named after the call it sends", () => {
    // The case is the call's name camel-cased, whole or from its second part on ("sources.view.hide": sourcesViewHide or
    // viewHide), so two names swapped fail here. The exceptions: `.bar` is the plain `corrections`, `.tryWay` is `try`.
    const named = { bar: "corrections", tryWay: "try" };
    const cases = [...enums.values()].flat();
    assert.ok(cases.length >= 100, `only ${cases.length} cases read`);
    const wrong = cases
        .filter(({ name, call }) => (name in named ? call !== named[name] : name !== camel(call) && name !== camel(call, 1)))
        .map(({ name, call }) => `.${name} → ${call}`);
    assert.deepEqual(wrong, []);
});

test("the reader still sees the Swift's calls", () => {
    // A floor, not a count: if the calls change form, this reader must follow it.
    assert.ok(sent.length >= 100, `only ${sent.length} sites read`);
});

test("the calls no Swift sends (reported, not a failure)", (t) => {
    const used = new Set(sent.map((s) => s.name));
    const unsent = contract.calls.map((c) => c.name).filter((name) => !used.has(name));
    t.diagnostic(`${unsent.length} of ${contract.calls.length} facade calls are sent by no Swift: ${unsent.join(", ")}`);
});

test("the reader follows the port's literals, choices, locals, enums, helpers, labels, defaults and pieces", () => {
    const { sent: found, unread: missed } = sentNames({
        "/x/Platform/PieceCore.swift": `
            struct PieceCore {
                enum Call: String { case aOne = "a.one", two = "a.two" }
                enum NoPiece: String {
                    // case commented = "commented.out"
                    case b = "b"
                }
                func literal() -> String { MusicCore.call(piece: piece, "c.literal") /* MusicCore.call(piece: 0, "block.comment") */ }
                func choice(_ on: Bool) -> String { MusicCore.call(piece: piece, on ? "c.on" : "c.off", "{\\"x\\": \\"(y)\\"}") }
                func local() -> String { let name = "c.local"; return MusicCore.call(piece: piece, name) }
                func send(_ call: Call, _ request: String? = nil) -> String { MusicCore.call(piece: piece, call.rawValue, request) }
                static func plain(_ call: NoPiece) -> String { MusicCore.call(piece: 0, call.rawValue) }
                func helped() async { _ = await helper("d.helped"); _ = await self.helper(); _ = await labelled(on: 0, call: "d.zero") }
                private func helper(_ name: String = "d.default") async -> String { MusicCore.call(piece: piece, name) }
                private func labelled(on number: Int32, call: String) async -> String { MusicCore.call(piece: number, call) }
                func forwards(_ name: String) -> String { MusicCore.call(piece: piece, name) }
                func built(_ part: String) -> String { MusicCore.call(piece: piece, "e.\\(part)") }
                func changed(_ x: Bool) -> String { var name = "e.var"; if x { name = "e.other" }; return MusicCore.call(piece: piece, name) }
                func handed() -> Any { helper }
                func through(_ other: PieceCore) async { _ = await other.helper("e.other") }
                func twice() async { _ = await again("f.one") }
                private func again(_ name: String) async -> String { MusicCore.call(piece: piece, name) }
                private func again(_ name: String, _ x: Int) async -> String { "" }
                func valued() -> Any { MusicCore.call }
            }`,
    });
    assert.deepEqual(found.map((s) => `${s.name}@${s.piece}`).sort(), [
        "a.one@open", "a.two@open", "b@piece0", "c.literal@open", "c.local@open", "c.off@open", "c.on@open",
        "d.default@open", "d.helped@open", "d.zero@piece0",
    ]);
    assert.deepEqual(missed.sort(), [
        "PieceCore.swift › again › another function is named again too",
        "PieceCore.swift › changed › name",
        'PieceCore.swift › built › "e.\\(part)"',
        "PieceCore.swift › forwards › takes a call's name and is not private",
        "PieceCore.swift › handed › helper passed as a value",
        "PieceCore.swift › through › helper called through another value",
        "PieceCore.swift › valued › MusicCore.call passed as a value",
    ].sort());
});

test("outside the port, a call by name fails in every shape that once hid one (#654)", () => {
    // Each file is one of the shapes the reader of scattered calls could not see; none needs to be read now: the call by
    // name outside the port fails wherever the name comes from.
    const { outside: found, sent: read } = sentNames({
        "/x/Platform/PieceCore.swift": `struct PieceCore { enum Call: String { case check = "check" } }`,
        // An overload with other argument labels hides a helper.
        "/x/A/Overload.swift": `
            final class Tool {
                func change(_ call: String, _ fields: [String: Any]) async { _ = MusicCore.call(piece: 1, call, facadeRequest(fields)) }
                func change(_ call: String, draws: Bool) async { await change(call, [:]) }
                func use() async { await change("x.hidden", draws: true) }
            }`,
        // An \`if let\` local typed from another file's declaration of the same name.
        "/x/A/IfLet.swift": `
            final class Sheet { func change(_ call: String) { _ = MusicCore.call(piece: 1, call) } }
            struct Use { func run(_ holder: Holder) { if let sheet = holder.sheet { sheet.change("x.iflet") } } }`,
        // Another function of the same name in the caller's file hides an inherited helper.
        "/x/A/SameName.swift": `
            class Base { func send(_ name: String) { _ = MusicCore.call(piece: 1, name) } }
            final class Sub: Base { func go() { send("x.inherited") } }
            func send(_ x: Int) {}`,
        // A helper used as a function value.
        "/x/A/Value.swift": `
            struct Core { func f(_ name: String) { _ = MusicCore.call(piece: 1, name) } }
            func all(_ core: Core) { ["x.a", "x.b"].forEach(core.f) }`,
        // An inherited class func called through a concrete subclass's name.
        "/x/A/ClassFunc.swift": `
            class Base { class func f(_ name: String) -> String { MusicCore.call(piece: 0, name) } }
            final class Sub: Base {}
            func use() { _ = Sub.f("x.class") }`,
        // A name passed in a case of a switch (\`keepRecognised\` was missed so), the facade's own function in another file,
        // the C function, and a port enum made from a name.
        "/x/A/Other.swift": `
            func kept(_ r: Result<Int, Error>) { switch r { case .success: keep("x.case"); case .failure: break } }
            func keep(_ name: String) { _ = MusicCore.call(piece: 1, name) }
            extension MusicCore { static func ping() -> String { call(piece: 0, "x.ext") } }
            func raw() { _ = ml_call(0, nil, nil) }
            func made(_ name: String) { _ = PieceCore.Call(rawValue: name) }`,
    });
    assert.deepEqual(read, []);
    assert.deepEqual(found.sort(), [
        "ClassFunc.swift › f › MusicCore.call",
        "IfLet.swift › change › MusicCore.call",
        "Other.swift › keep › MusicCore.call",
        "Other.swift › made › Call(rawValue:)",
        "Other.swift › ping › MusicCore.call",
        "Other.swift › raw › ml_call",
        "Overload.swift › change › MusicCore.call",
        "SameName.swift › send › MusicCore.call",
        "Value.swift › f › MusicCore.call",
    ].sort());
});
