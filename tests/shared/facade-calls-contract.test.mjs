// The facade's call names (audit C2, blueprint 5.4): every call name the iPad app's Swift sends to music-core must be one
// the facade answers, as tests/contracts/facade-calls.json lists them. FacadeCallsContractTests (xUnit) keeps that file
// equal to what MusicCoreFacade.Call really dispatches; this test reads the Swift sources under src/ipad/App/MotiffLab,
// every folder below it, so a call renamed or removed in C# fails here on Linux and not only on the iPad.
//   node --test tests/shared   (needs no build: it reads the Swift sources)
//
// The forms a call name takes in the Swift, all read here:
//   1. A literal in MusicCore.call(piece: …, "name", …), the one function that reaches `ml_call`, spelled `MusicCore.call`
//      or, inside `enum`/`extension MusicCore`, `Self.call`, `self.call` or `call`. `MusicCore.run { … }` only runs work
//      on the core's thread, `facadeRequest` only encodes the request's fields, `MusicCore.open` and `MusicCore.close` are
//      `ml_open` and `ml_close`, not calls by name.
//   2. A parameter passed on as the name: a helper such as `correct(_ call: String, …)`, `mergeCall(_ piece:, _ call:, …)`
//      or `FolderPlan.call(_ name:, …)` hands its own parameter to MusicCore.call or to another such helper; the names
//      are then the literals at the helper's call sites, followed through any depth of helpers, by position for an
//      unlabelled parameter and by label for a labelled one. A site reaches the helper through its type's name, through
//      `self` or unqualified in the same type or file, or through a value whose declared type is the helper's
//      (`pdf: PdfController`); a site through a value of a type the reader cannot tell, where another function has the
//      same name, is read when it passes literals in the helper's name place and is unread otherwise.
//   3. A labelled parameter's default, `layOut(…, call: String = "notation", …)`, which every site that leaves it out sends.
//   4. A choice of literals, `cond ? "a" : "b"`, and a local `let name = <literal or choice>` assigned nowhere else.
// Nothing call-shaped is dropped: anything else in the name's place (a name built from parts, a `var` or a reassigned
// name, a collection, a helper or MusicCore.call passed as a function value, an unattributable site without literals)
// is unread, and an unread place must be listed in `unresolved` below with the names it stands for, or the test fails.
// Only what is not a call of a function is passed over: an enum's case or a pattern (`case .change(…)`), an implicit
// member (`.change(…)`, which only a static function could be), and a closure or variable the caller declares itself.
// The piece argument is followed too: a literal 0 is a call on piece 0, anything else a call on an open piece.

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { basename, join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const swiftRoot = join(root, "src", "ipad", "App", "MotiffLab");
const contract = JSON.parse(readFileSync(join(root, "tests", "contracts", "facade-calls.json"), "utf8"));

/**
 * The places where the Swift passes a call name the reader cannot follow, as "file › function › expression", with the
 * names they stand for and why. Each listed name must be written as a literal in that same function, so a rename there
 * fails too, and is checked against the contract like any name read.
 */
const unresolved = new Map([
    // The Pen's Undo and Redo hand InkStep.undo and .redo (InkGeometry.swift) the two names, which pass them back to the
    // closure as `call`.
    ["ScoreController.swift › undo › call", { names: ["ink.remove", "ink.add"] }],
    ["ScoreController.swift › redo › call", { names: ["ink.remove", "ink.add"] }],
    ["PdfInk.swift › undo › call", { names: ["document.ink.remove", "document.ink.add"] }],
    ["PdfInk.swift › redo › call", { names: ["document.ink.remove", "document.ink.add"] }],
]);

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

/** The types (class, struct, enum, actor, protocol, extension) with the span of their bodies. */
function typesIn(code) {
    const types = [];
    for (const match of code.matchAll(/\b(?:class|struct|enum|actor|protocol|extension)\s+([A-Za-z_][\w.]*)[^{};=]*\{/g)) {
        const open = match.index + match[0].length - 1;
        types.push({ name: match[1].split(".").pop(), from: open, to: closing(code, open) });
    }
    return types;
}

/** The functions declared in a file: name, parameters (label, name, default), body span, enclosing type, privacy. */
function functionsIn(file, lexed) {
    const { code } = lexed;
    const types = typesIn(code);
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
            return {
                label: head ? (head[1] ?? head[2]) : null,
                name: head ? head[2] : null,
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
        const type = types.filter((t) => t.from < match.index && match.index < t.to).sort((a, b) => b.from - a.from)[0];
        functions.push({
            file,
            name: match[1],
            at: match.index,
            params,
            bodyFrom,
            bodyTo,
            type: type?.name ?? null,
            isPrivate: /\b(?:private|fileprivate)\b/.test(modifiers),
            isStatic: /\b(?:static|class)\b/.test(modifiers),
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

/**
 * Every call name the sources send, with where and on which piece ("piece0" for a literal 0, "open" otherwise), and the
 * places the reader could not follow with the string literals of the function around each. `sources` maps a file's
 * path to its text; a place is named by the file's name, so moving files between folders changes no place.
 */
function sentNames(sources) {
    const files = Object.entries(sources).map(([file, text]) => ({ file, lexed: lex(text) }));
    const functions = files.flatMap(({ file, lexed }) => functionsIn(file, lexed));
    const sent = [];
    const unread = [];
    const carriers = new Set();
    const queue = [];

    /** The innermost function whose body holds `at` in `file`. */
    const enclosing = (file, at) => functions
        .filter((f) => f.file === file && f.bodyFrom < at && at < f.bodyTo)
        .sort((a, b) => b.bodyFrom - a.bodyFrom)[0] ?? null;
    const placeOf = (file, fn) => `${basename(file)} › ${fn?.name ?? "(top level)"}`;
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

    /** A place the reader cannot follow, with the literals written in its function (what a listed place must still hold). */
    function unreadAt(file, lexed, at, text) {
        const fn = enclosing(file, at);
        const literals = fn ? lexed.strings.filter((s) => fn.bodyFrom < s.start && s.end < fn.bodyTo && !s.interpolated).map((s) => s.text) : [];
        unread.push({ place: `${placeOf(file, fn)} › ${text}`, literals });
    }

    /**
     * The name in a site's name argument: literals are sent; the enclosing function's parameter makes that function a
     * helper whose own sites are read (`piece` is how it reaches the piece: "piece0", "open" or {param}); else unread.
     */
    function nameAt(file, lexed, arg, piece, through) {
        const fn = enclosing(file, arg.at);
        const names = literalsOf(lexed, arg.at, arg.end) ?? localLiterals(lexed, fn, arg.text);
        if (names) {
            const where = `${placeOf(file, fn)} (${through})`;
            for (const name of names) sent.push({ name, piece: typeof piece === "string" ? piece : "open", where });
            return;
        }
        const param = parameterOf(fn, arg.text);
        if (param < 0) {
            unreadAt(file, lexed, arg.at, lexed.source.slice(arg.at, arg.end));
            return;
        }
        if (!carriers.has(fn)) {
            carriers.add(fn);
            queue.push({ fn, nameParam: param, piece });
        }
    }

    /** The piece a `MusicCore.call` names: "piece0" for a literal 0, the enclosing function's parameter, or "open". */
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

    // Form 1: MusicCore.call itself, as `MusicCore.call`, or as `Self.call`, `self.call` or `call` inside MusicCore.
    for (const { file, lexed } of files) {
        for (const match of lexed.code.matchAll(/(?:\b([A-Za-z_]\w*)\s*\.\s*)?\bcall\b(\s*\()?/g)) {
            const qualifier = qualifierAt(lexed.code, match.index, match[1]);
            const insideCore = enclosing(file, match.index)?.type === "MusicCore";
            const facade = qualifier === "MusicCore" || (insideCore && (qualifier === "Self" || qualifier === "self" || (qualifier === null && match[2])));
            if (!facade || /\bfunc\s*$/.test(lexed.code.slice(Math.max(0, match.index - 8), match.index))) continue;
            const parts = match[2] ? argumentsAt(lexed.code, match.index + match[0].length - 1).parts : [];
            const nameArg = parts.filter((p) => p.label === null)[0];
            if (!nameArg) {
                unreadAt(file, lexed, match.index, match[2] ? "MusicCore.call without a name" : "MusicCore.call passed as a value");
                continue;
            }
            nameAt(file, lexed, nameArg, pieceAt(file, parts.find((p) => p.label === "piece")), "MusicCore.call");
        }
    }

    /** A site of a helper's name reached through `fn`: the name and piece it passes, read as `nameAt` reads them. */
    function readSite(fn, nameParam, piece, file, lexed, parts) {
        const arg = argumentFor(fn, nameParam, parts);
        if (!arg) return; // the parameter's default, read with the helper
        const pieceArg = typeof piece === "string" ? null : argumentFor(fn, piece.param, parts);
        nameAt(file, lexed, arg, typeof piece === "string" ? piece : pieceArg ? pieceAt(file, pieceArg) : "open", fn.name);
    }

    // Forms 2 and 3: the helpers, followed until no new one turns up. A site that may reach a helper or another function
    // of the same name (through an instance of a type the reader cannot tell) is read when it passes literals where a
    // helper takes its name; one that passes none is unread, never dropped.
    const helpers = new Map();
    const unsure = new Map();
    const settled = new Set();
    while (queue.length > 0) {
        while (queue.length > 0) {
            const { fn, nameParam, piece } = queue.shift();
            helpers.set(fn, { nameParam, piece });
            const param = fn.params[nameParam];
            if (param.defaultText !== null) {
                const lexed = files.find((f) => f.file === fn.file).lexed;
                const names = literalsOf(lexed, param.defaultAt, param.defaultAt + param.defaultText.length);
                if (names) for (const name of names) sent.push({ name, piece: typeof piece === "string" ? piece : "open", where: `${placeOf(fn.file, fn)} (default of ${param.name})` });
                else unreadAt(fn.file, lexed, fn.bodyFrom + 1, `default ${param.defaultText}`);
            }
            for (const { file, lexed } of files) {
                for (const match of lexed.code.matchAll(new RegExp(`(?:\\b([A-Za-z_]\\w*)\\s*\\.\\s*)?\\b${fn.name}\\s*\\(`, "g"))) {
                    if (/\bfunc\s*$/.test(lexed.code.slice(Math.max(0, match.index - 8), match.index)) || inCase(lexed.code, match.index)) continue;
                    const qualifier = qualifierAt(lexed.code, match.index, match[1]);
                    const reached = attribution(functions, fn.name, file, lexed, qualifier, enclosing(file, match.index), files);
                    if (!reached.functions.includes(fn)) continue;
                    const { parts } = argumentsAt(lexed.code, match.index + match[0].length - 1);
                    if (reached.sure) readSite(fn, nameParam, piece, file, lexed, parts);
                    else unsure.set(`${file}#${match.index}`, { file, lexed, at: match.index, text: match[0], parts, reached: reached.functions });
                }
                // A helper handed on as a function value (`through: change`) sends names this reader does not follow.
                for (const match of lexed.code.matchAll(new RegExp(`[(,:]\\s*(?:self\\s*\\.\\s*)?(${fn.name})\\s*[,)]`, "g"))) {
                    const at = match.index + match[0].indexOf(fn.name, 1);
                    const caller = enclosing(file, at);
                    if (parameterOf(caller, fn.name) >= 0 || declaredIn(lexed, caller, fn.name) || inCase(lexed.code, at)) continue;
                    if (!attribution(functions, fn.name, file, lexed, null, caller, files).functions.includes(fn)) continue;
                    unreadAt(file, lexed, at, `${fn.name} passed as a value`);
                }
            }
        }
        for (const [key, site] of unsure) {
            if (settled.has(key)) continue;
            settled.add(key);
            const reachable = site.reached.filter((f) => helpers.has(f));
            const literal = reachable.filter((f) => {
                const arg = argumentFor(f, helpers.get(f).nameParam, site.parts);
                return arg && literalsOf(site.lexed, arg.at, arg.end);
            });
            if (literal.length > 0) {
                const { nameParam, piece } = helpers.get(literal[0]);
                readSite(literal[0], nameParam, piece, site.file, site.lexed, site.parts);
            } else {
                unreadAt(site.file, site.lexed, site.at, `${site.text.replace(/\s*\($/, "")}(…) may be ${reachable.map((f) => `${f.type ?? basename(f.file)}.${f.name}`).join(" or ")}`);
            }
        }
    }

    return { sent, unread };
}

/**
 * Whether the function (or, outside any function, the file) declares `name` as a value: a `let`, a `var`, a closure's
 * parameter (`{ name, other in`) or a loop's (`for (i, name) in`).
 */
function declaredIn(lexed, fn, name) {
    const body = fn ? lexed.code.slice(fn.bodyFrom, fn.bodyTo) : lexed.code;
    const bound = `\\(?[\\w\\s,]*\\b${name}\\b[\\w\\s,]*\\)?\\s*in\\b`;
    return new RegExp(`\\b(?:let|var)\\s+${name}\\b|\\{\\s*(?:\\[[^\\]]*\\]\\s*)?${bound}|\\bfor\\s+(?:case\\s+let\\s+)?${bound}`).test(body);
}

/** Whether the place is in a `case` (an enum's cases or a pattern, `case let .change(…)`), where no function is called. */
function inCase(code, at) {
    return /\bcase\b/.test(code.slice(code.lastIndexOf("\n", at) + 1, at));
}

/**
 * What qualifies a name at `at`: the identifier before its dot, "?" for another expression (`a().b`, `x?.b`), "." for an
 * implicit member (`= .change(…)`, a static member or an enum case of a type the context gives), or null for none.
 */
function qualifierAt(code, at, identifier) {
    if (identifier) return identifier;
    const before = code.slice(0, at).trimEnd();
    if (!before.endsWith(".")) return null;
    return /[\w)\]?!"]$/.test(before.slice(0, -1).trimEnd()) ? "?" : ".";
}

/**
 * The functions a call of `name` in `file`, qualified by `qualifier` ("?" for an expression) inside `caller`, may reach,
 * and whether the reader is sure: through a type's name, that type's; unqualified or through `self`, a closure or
 * variable of the caller's when it has one (none), else the caller's type's, else the file's, else a global; as an
 * implicit member, the static ones; through an instance whose declaration names its type, that type's instance
 * functions. Otherwise every instance function of that name it could reach, sure only when there is one.
 */
function attribution(functions, name, file, lexed, qualifier, caller, files) {
    const named = functions.filter((f) => f.name === name && (!f.isPrivate || f.file === file));
    const sure = (found) => ({ functions: found, sure: true });
    if (qualifier && /^[A-Z]/.test(qualifier) && qualifier !== "Self") return sure(named.filter((f) => f.type === qualifier));
    if (!qualifier || qualifier === "self" || qualifier === "Self") {
        if (!qualifier && caller && (caller.params.some((p) => p.name === name) || declaredIn(lexed, caller, name))) return sure([]);
        for (const found of [named.filter((f) => caller?.type && f.type === caller.type), named.filter((f) => f.file === file), named.filter((f) => f.type === null)]) {
            if (found.length > 0) return sure(found);
        }
        return { functions: named, sure: named.length <= 1 };
    }
    if (qualifier === ".") return sure(named.filter((f) => f.isStatic));
    const members = named.filter((f) => !f.isStatic);
    const type = qualifier === "?" ? null : declaredType(qualifier, lexed, files);
    if (type) return sure(members.filter((f) => f.type === type));
    return { functions: members, sure: members.length <= 1 };
}

/**
 * The type a variable, property or parameter is declared with (`pdf: PdfController`, `var sources = SourcesController(…)`): the
 * file's own declaration, else the one type every other file declares it with; null when it cannot be told.
 */
function declaredType(name, lexed, files) {
    const pattern = new RegExp(`(?<![\\w.])${name}\\s*:\\s*([A-Z]\\w*)|\\b(?:let|var)\\s+${name}\\s*=\\s*([A-Z]\\w*)\\s*\\(`, "g");
    const typesIn = (code) => [...new Set([...code.matchAll(pattern)].map((m) => m[1] ?? m[2]))];
    const own = typesIn(lexed.code);
    if (own.length > 0) return own.length === 1 ? own[0] : null;
    const everywhere = [...new Set(files.flatMap((f) => typesIn(f.lexed.code)))];
    return everywhere.length === 1 ? everywhere[0] : null;
}

// ---------------------------------------------------------------------------------------------------------------------

const sources = Object.fromEntries(swiftFiles(swiftRoot).map((file) => [file, readFileSync(file, "utf8")]));
const { sent, unread } = sentNames(sources);
const calls = new Map(contract.calls.map((call) => [call.name, call]));
const listed = [...unresolved].flatMap(([place, { names }]) => names.map((name) => ({ name, where: `${place} (listed)` })));

test("every call name the Swift sends is one the facade answers", () => {
    const unknown = [...sent, ...listed].filter((s) => !calls.has(s.name)).map((s) => `${s.name}  ← ${s.where}`);
    assert.deepEqual(unknown, [], "names not in tests/contracts/facade-calls.json");
});

test("every call is sent to a piece the facade answers it on", () => {
    const answered = (s) => (s.piece === "piece0" ? ["piece0"] : ["piece", "pdf"]).some((on) => calls.get(s.name).on.includes(on));
    const misplaced = sent
        .filter((s) => calls.has(s.name) && !answered(s))
        .map((s) => `${s.name} on ${s.piece === "piece0" ? "piece 0" : "an open piece"}  ← ${s.where}  (the facade answers it on ${calls.get(s.name).on.join(", ")})`);
    assert.deepEqual(misplaced, []);
});

test("every name the reader cannot follow is listed with the names it stands for, and every listed place is still there", () => {
    assert.deepEqual(unread.map((u) => u.place).filter((place) => !unresolved.has(place)), [], "list these in `unresolved`");
    assert.deepEqual([...unresolved.keys()].filter((place) => !unread.some((u) => u.place === place)), [], "no longer in the sources: take these out of `unresolved`");
    const stale = unread
        .filter((u) => unresolved.has(u.place))
        .flatMap((u) => unresolved.get(u.place).names.filter((name) => !u.literals.includes(name)).map((name) => `${name} is no longer written in ${u.place}`));
    assert.deepEqual(stale, []);
});

test("the reader still sees the Swift's calls", () => {
    // A floor, not a count: if the calls change form (a typed port in place of MusicCore.call), this reader must follow it.
    assert.ok(sent.length >= 100, `only ${sent.length} sites read`);
});

test("the calls no Swift sends (reported, not a failure)", (t) => {
    const used = new Set([...sent, ...listed].map((s) => s.name));
    const unsent = contract.calls.map((c) => c.name).filter((name) => !used.has(name));
    t.diagnostic(`${unsent.length} of ${contract.calls.length} facade calls are sent by no Swift: ${unsent.join(", ")}`);
});

test("the reader follows literals, choices, locals, helpers, labels, defaults and the piece, and lists what it cannot read", () => {
    const { sent: found, unread: missed } = sentNames({
        "/x/A.swift": `
            enum Core {
                // MusicCore.call(piece: 0, "commented.out")
                /* MusicCore.call(piece: 0, "block.comment") */
                static func plain() { _ = MusicCore.call(piece: 0, "version") }
                static func choice(_ on: Bool) { _ = MusicCore.call(piece: p, on ? "a.on" : "a.off", "{\\"x\\": \\"(y)\\"}") }
                static func local() { let call = "b.local"; _ = MusicCore.call(piece: p, call) }
                static func changed(_ x: Bool) { var call = "b.var"; if x { call = "b.other" }; _ = MusicCore.call(piece: p, call) }
                static func again(_ x: Bool) { let call = "b.first"; _ = MusicCore.call(piece: p, call); call = "b.later" }
                private func send(_ piece: Int32, _ name: String, _ fields: [String: Any] = [:]) { _ = MusicCore.call(piece: piece, name, facadeRequest(fields)) }
                private func relay(_ name: String) { send(0, name) }
                func use() { send(id, "c.open"); relay("c.zero"); send(id, "x.\\(part)") }
                func steps() { step.undo(remove: "d.remove") { call, fields in send(id, call, fields) } }
                func handed() { step.redo(through: send) }
                static func lay(piece: Int32, call: String = "notation", request: String) { _ = MusicCore.call(piece: piece, call, request) }
                static func both() { lay(piece: id, request: "{}"); Core.lay(piece: id, call: "merge.notation", request: "{}") }
            }`,
        "/x/B.swift": `
            struct Other {
                private func send(_ a: Int32, _ b: String) { print(b) }
                func elsewhere() { send(1, "not.a.call"); _ = MusicCore.call(piece: 0, "\\(prefix).add") }
            }`,
        // A helper that is not the only function of its name, called from other types and files.
        "/x/Pdf/PdfController.swift": `
            final class PdfController {
                func change(_ call: String, _ fields: [String: Any]) async { _ = MusicCore.call(piece: piece, call, facadeRequest(fields)) }
            }
            final class Metronome { func change(bpm: Int) {} }
            enum Edit { case change(id: String) }`,
        "/x/Features/Tool.swift": `
            struct Tool {
                func typed(pdf: PdfController) async { await pdf.change("e.typed", [:]) }
                func untyped() async { await controller.change("e.untyped", [:]) }
                func unreadable(_ name: String) async { await controller.change(name.lowercased(), [:]) }
                func others(metronome: Metronome) async { metronome.change(bpm: 90); let edit = Edit.change(id: "x"); if case let .change(id) = edit {} }
                func loop(_ names: [String]) { for (i, change) in names.enumerated() { print(i, change) } }
            }`,
        // The facade's own function, spelled without "MusicCore." inside its type.
        "/x/Core/MusicCoreExtras.swift": `
            extension MusicCore {
                static func ping() -> String { call(piece: 0, "f.plain") }
                static func pong() -> String { Self.call(piece: 0, "f.self") }
                static func handed() -> Any { Self.call }
            }`,
    });
    assert.deepEqual(found.map((s) => `${s.name}@${s.piece}`).sort(), [
        "a.off@open", "a.on@open", "b.local@open", "c.open@open", "c.zero@piece0", "e.typed@open", "e.untyped@open",
        "f.plain@piece0", "f.self@piece0", "merge.notation@open", "notation@open", "version@piece0",
    ]);
    assert.deepEqual(missed.map((m) => m.place).sort(), [
        "A.swift › again › call",
        "A.swift › changed › call",
        "A.swift › handed › send passed as a value",
        "A.swift › steps › call",
        'A.swift › use › "x.\\(part)"',
        'B.swift › elsewhere › "\\(prefix).add"',
        "MusicCoreExtras.swift › handed › MusicCore.call passed as a value",
        "Tool.swift › unreadable › controller.change(…) may be PdfController.change",
    ]);
    assert.deepEqual(missed.find((m) => m.place === "A.swift › steps › call").literals, ["d.remove"]);
});
