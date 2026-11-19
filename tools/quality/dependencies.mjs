// The dependency check (CLAUDE.md, "Writing code"): which module may use which, where neither the build nor CI
// would notice a break. Pure: the caller passes the file lists and a reader, so the tests need no repository.
//
// 1. music-core depends on nothing in the solution and on no web, cloud or AI library (CLAUDE.md, "Stack").
// 2. The iPad's score and roll pages (src/ipad/App/Web/*.js) load the web app's compiled modules from the folders the
//    app bundles (src/ipad/App/project.yml, wwwroot/js/<folder>). CI does not build the iPad app, so an import that
//    reaches a folder the app does not bundle would only fail on the device, as a page that never loads.
// 3. The iPad app's folders (src/ipad/App/MotiffLab/<Folder>) use only the folders IPAD_FOLDER_USES allows. The app is
//    one Swift module, so the compiler does not enforce them: the check indexes the types each folder declares and
//    reports a file that names a type of a folder it may not use. Today's edges are in the ratchet's baseline.
// 4. music-core's namespaces use only the namespaces of the layers below their own (CORE_LAYERS), so no two use each
//    other, directly or round a loop: the compiler allows such a loop within one project.
import { posix } from 'node:path';

export const CORE = 'src/backend/MotiffLab.MusicCore';
const SCRIPTS = 'src/shared/Scripts';
export const IPAD_PAGES = 'src/ipad/App/Web';
export const IPAD_PROJECT = 'src/ipad/App/project.yml';
export const IPAD_FOLDERS = 'src/ipad/App/MotiffLab';

/**
 * Which folder of the iPad app may use which (architecture blueprint v0.7, 5.2, «The iPad's folders»): `Platform` uses
 * nothing above it; a feature uses `Platform` and, where the table says so, reads Selection; only `Workspace` uses
 * every folder ('*'). A folder may always use its own types. governance/quality/README.md shows the same table, and
 * tools/quality/dependencies.test.mjs checks that the two agree.
 */
export const IPAD_FOLDER_USES = {
  Platform: [],
  Workspace: '*',
  Selection: ['Platform'],
  Playback: ['Platform', 'Selection'],
  Roll: ['Platform', 'Selection'],
  Ink: ['Platform'],
  Marks: ['Platform', 'Selection'],
  Sources: ['Platform', 'Selection'],
  Pdf: ['Platform'],
  Library: ['Platform'],
  Import: ['Platform'],
  Insights: ['Platform', 'Selection'],
  Reference: ['Platform'],
  Assistant: ['Platform', 'Selection'],
  Account: ['Platform'],
};

/**
 * music-core's namespaces (MotiffLab.MusicCore.<Namespace>; a sub-namespace such as Import.Scan counts as its top one)
 * in layers, from the bottom (docs/code-map/core.md, «Which namespace uses which»): a namespace uses only namespaces of
 * the layers before its own, never one of its own layer, so none uses another round a loop. `(root)` is
 * MotiffLab.MusicCore itself (`Words`), which the namespaces from layer 2 up may use; `Time`, `Settings` and `Accounts`
 * share its layer and use nothing. governance/quality/README.md shows the same
 * layers, and tools/quality/dependencies.test.mjs checks that the two agree.
 */
export const CORE_LAYERS = [
  ['(root)', 'Time', 'Settings', 'Accounts'],
  ['Events'],
  ['Motifs', 'Export'],
  ['Pieces'],
  ['Import', 'Playback'],
  ['Analysis', 'Performance'],
  ['Notation', 'Variations', 'Invitations'],
  ['Experiments', 'Assembly'],
  ['Analytics', 'Projects'],
  ['Assistant'],
  ['Facade'],
];

const CORE_FORBIDDEN = /^(Microsoft\.AspNetCore|Microsoft\.JSInterop|Microsoft\.Extensions\.AI|Microsoft\.Identity|Azure|OpenAI|System\.Net\.Http)\b/;

function lineAt(text, index) {
  return text.slice(0, index).split('\n').length;
}

function violation(rule, file, symbol, line, message) {
  return { tool: 'dependencies', rule, file, symbol, line, message };
}

/** music-core's project files and sources: no project reference, no forbidden package or namespace. */
export function coreViolations(files, read) {
  const out = [];
  for (const file of files) {
    const text = read(file);
    if (file.endsWith('.csproj')) {
      for (const m of text.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)) {
        out.push(violation('core-reference', file, m[1], lineAt(text, m.index),
          `music-core references ${m[1]}: it depends on nothing in the solution; move the shared code into music-core.`));
      }
      for (const m of text.matchAll(/<PackageReference\s+Include="([^"]+)"/g)) {
        if (CORE_FORBIDDEN.test(m[1])) {
          out.push(violation('core-package', file, m[1], lineAt(text, m.index),
            `music-core uses the package ${m[1]}: no web, cloud or AI dependency in music-core.`));
        }
      }
    } else {
      for (const m of text.matchAll(/^\s*(?:global\s+)?using\s+(?:static\s+)?([\w.]+)\s*;/gm)) {
        if (CORE_FORBIDDEN.test(m[1])) {
          out.push(violation('core-using', file, m[1], lineAt(text, m.index),
            `music-core uses ${m[1]}: no web, cloud or AI dependency in music-core; the apps or the API call it instead.`));
        }
      }
    }
  }
  return out;
}

// ---- music-core's namespaces --------------------------------------------------------------------------------
//
// An unqualified name in C# reaches another namespace only through a `using` line or a namespace that encloses the
// file's own (a sub-namespace sees its top one, and every file sees the root: so a use of the root is not seen, and
// `Time`, `Settings` or `Accounts` using `Words` would pass). So a file's edges are its `using` lines (a dead one too:
// the line is a use) and the names it writes qualified, `Performance.SavedPerformance` or
// `MotiffLab.MusicCore.Import.Scan.ScanCheck`, in code and in a cref, whose name needs the namespace as code does.
// A qualified name counts when its path leads to a type the namespace declares, so `Assembly.GetExecutingAssembly()`
// or a member named like a namespace is no use. Other comment text and string literals are no use. What it cannot see:
// a type reached without its name written (`var bars = piece.Bars`, a member of a member) needs no `using`, so it is no
// use; the edge runs through the file that declares the member. A `global using` in one file would count for that file
// only (music-core has none of its own namespaces).

const CS_NAMESPACE = /^[ \t]*namespace[ \t]+MotiffLab\.MusicCore((?:\.\w+)*)[ \t]*[;{]/m;
const CS_USING = /^[ \t]*(?:global[ \t]+)?using[ \t]+(?:static[ \t]+)?(?:\w+[ \t]*=[ \t]*)?(?:global::)?MotiffLab\.MusicCore\.(\w+)[\w.]*[ \t]*;/gm;
const CS_TYPE = /\b(?:record[ \t]+(?:struct|class)|class|struct|record|enum|interface)[ \t]+([A-Z]\w*)/g;
const CS_QUALIFIED = /(?<![\w.])(?:global::)?(?:MotiffLab\.)?(?:MusicCore\.)?([A-Z]\w*)((?:[ \t]*\.[ \t]*[A-Z]\w*)+)/g;

/** Where a C# string literal that starts at `i` ends: '…', "…", @"…", $"…" (blanked whole) or a raw """…""". */
function csharpLiteralEnd(text, i) {
  if (text[i] === "'") {
    const m = /^'(?:\\.|[^'\\\n])*'/.exec(text.slice(i, i + 12));
    return i + (m ? m[0].length : 1);
  }
  const prefix = /^[$@]*/.exec(text.slice(i, i + 4))[0];
  let k = i + prefix.length;
  const quotes = /^"*/.exec(text.slice(k))[0].length;
  if (quotes >= 3) {
    const end = text.indexOf('"'.repeat(quotes), k + quotes);
    return end < 0 ? text.length : end + quotes;
  }
  const verbatim = prefix.includes('@');
  for (k += 1; k < text.length; k += 1) {
    if (text[k] === '\\' && !verbatim) {
      k += 1;
    } else if (text[k] === '"' && verbatim && text[k + 1] === '"') {
      k += 1;
    } else if (text[k] === '"' || (text[k] === '\n' && !verbatim)) {
      return k + 1;
    }
  }
  return k;
}

/**
 * The C# source with its comments and string literals blanked, except the value of each cref, which names a type as
 * code does: each blanked character becomes a space and the line breaks stay, so line numbers still match the source.
 */
export function csharpCode(text) {
  const chars = text.split('');
  const blank = (from, to) => {
    for (let k = from; k < to; k++) {
      if (chars[k] !== '\n') {
        chars[k] = ' ';
      }
    }
  };
  let i = 0;
  while (i < text.length) {
    const two = text.slice(i, i + 2);
    let end = i + 1;
    if (two === '//') {
      end = text.indexOf('\n', i) < 0 ? text.length : text.indexOf('\n', i);
      blank(i, end);
      for (const m of text.slice(i, end).matchAll(/cref="([^"]*)"/g)) {
        const at = i + m.index + 6;
        [...m[1]].forEach((ch, k) => { chars[at + k] = ch; });
      }
    } else if (two === '/*') {
      end = text.indexOf('*/', i + 2) < 0 ? text.length : text.indexOf('*/', i + 2) + 2;
      blank(i, end);
    } else if (/^(?:'|[$@]*")/.test(text.slice(i, i + 4)) && !/\w/.test(text[i - 1] ?? '')) {
      end = csharpLiteralEnd(text, i);
      blank(i, end);
    }
    i = end;
  }
  return chars.join('');
}

/** The top namespace of a namespace path below MotiffLab.MusicCore (".Import.Scan" is "Import"); "" is "(root)". */
function coreTop(path) {
  return path ? path.split('.')[1] : '(root)';
}

/**
 * The full namespace path (".Import.Scan") that a qualified name's segments lead to a type of, or null. `declared`
 * holds the types of each namespace path and of each path above one ("" for none).
 */
function qualifiedPath(segments, declared) {
  let at = '';
  for (const segment of segments) {
    if (declared.has(`${at}.${segment}`)) {
      at = `${at}.${segment}`;
    } else {
      return at !== '' && declared.get(at).has(segment) ? at : null;
    }
  }
  return null;
}

/** The type names each namespace path declares, with an empty set for each path above one that has no file of its own. */
function coreDeclarations(scanned) {
  const declared = new Map();
  for (const f of scanned) {
    const parts = f.path.split('.');
    for (let k = 2; k < parts.length; k++) {
      declared.set(parts.slice(0, k).join('.'), declared.get(parts.slice(0, k).join('.')) ?? new Set());
    }
    declared.set(f.path, new Set([...(declared.get(f.path) ?? []), ...[...f.code.matchAll(CS_TYPE)].map((m) => m[1])]));
  }
  return declared;
}

/** The namespaces of music-core one file uses, each with the line of its first use. */
function coreUses(f, declared) {
  const uses = new Map();
  const use = (top, index) => {
    if (!uses.has(top)) {
      uses.set(top, lineAt(f.code, index));
    }
  };
  for (const m of f.code.matchAll(CS_USING)) {
    use(m[1], m.index);
  }
  for (const m of f.code.matchAll(CS_QUALIFIED)) {
    const path = qualifiedPath([m[1], ...m[2].split('.').map((part) => part.trim()).filter(Boolean)], declared);
    if (path !== null) {
      use(coreTop(path), m.index);
    }
  }
  return uses;
}

/**
 * Each namespace of music-core that a file uses although it is not in a layer below the file's own (rule
 * "core-namespace", one violation per file and namespace, the symbol "<Namespace>: <used namespace>"), and each file
 * of a namespace the layers do not list (rule "core-namespace-unlisted").
 */
export function coreNamespaceViolations(files, read, layers = CORE_LAYERS) {
  const scanned = files.filter((file) => file.endsWith('.cs')).map((file) => {
    const code = csharpCode(read(file));
    return { file, code, path: CS_NAMESPACE.exec(code)?.[1] };
  }).filter((f) => f.path !== undefined);
  const declared = coreDeclarations(scanned);
  const below = new Map(layers.flatMap((layer, index) => layer.map((name) => [name, new Set(layers.slice(0, index).flat())])));
  const out = [];
  for (const f of scanned) {
    const own = coreTop(f.path);
    if (!below.has(own)) {
      out.push(violation('core-namespace-unlisted', f.file, own, 1, `${f.file} is in the namespace ${own} of music-core, `
        + 'which CORE_LAYERS (tools/quality/dependencies.mjs) does not list: add it to the layer it belongs to.'));
      continue;
    }
    for (const [top, line] of coreUses(f, declared)) {
      if (top !== own && !below.get(own).has(top)) {
        out.push(violation('core-namespace', f.file, `${own}: ${top}`, line, `${f.file} (namespace ${own}) uses ${top}, `
          + `which is not in a layer below ${own}'s (CORE_LAYERS in tools/quality/dependencies.mjs): move the type down `
          + 'or the code up; a doc comment names a type of a higher namespace in plain text (<c>Type</c>), not by cref.'));
      }
    }
  }
  return out;
}

/** The folders of wwwroot/js the iPad app bundles, read from its XcodeGen project. */
export function bundledFolders(projectYml) {
  return new Set([...projectYml.matchAll(/wwwroot\/js\/([\w-]+)\s*$/gm)].map((m) => m[1]));
}

/** The relative modules a file loads when it runs; `import type` and `export type` are erased by the compiler. */
export function runtimeImports(text) {
  const out = [];
  const patterns = [
    /^[ \t]*(?:import|export)[ \t]+(?!type[ \t{])[^;]*?from[ \t]*["'](\.[^"']+)["']/gm,
    /^[ \t]*import[ \t]*["'](\.[^"']+)["']/gm,
    /\bimport\(\s*["'](\.[^"']+)["']\s*\)/g,
  ];
  for (const pattern of patterns) {
    for (const m of text.matchAll(pattern)) {
      out.push({ spec: m[1], line: lineAt(text, m.index + m[0].indexOf(m[1])) });
    }
  }
  return out;
}

/**
 * Follows the iPad pages' imports through the TypeScript sources they load (a compiled `x.js` is the source `x.ts`)
 * and reports each import that leaves the bundled folders or names a module that does not exist.
 */
export function ipadPageViolations(pages, read, exists, bundled) {
  const out = [];
  const seen = new Set();
  const queue = [...pages];
  while (queue.length) {
    const file = queue.pop();
    if (seen.has(file)) {
      continue;
    }
    seen.add(file);
    for (const { spec, line } of runtimeImports(read(file))) {
      const target = resolveImport(file, spec);
      const folder = target.startsWith(`${SCRIPTS}/`) ? posix.relative(SCRIPTS, target).split('/')[0] : null;
      if (folder !== null && !bundled.has(folder)) {
        out.push(violation('ipad-unbundled', file, spec, line,
          `the iPad page loads ${target} from wwwroot/js/${folder}, which the app does not bundle (${IPAD_PROJECT}).`));
      } else if (!exists(target)) {
        out.push(violation('ipad-missing', file, spec, line, `the iPad page loads ${spec}, but ${target} does not exist.`));
      } else {
        queue.push(target);
      }
    }
  }
  return out;
}

/** Where an import of the iPad page leads: a page's `../<folder>/x.js` is the bundled wwwroot/js/<folder>. */
function resolveImport(from, spec) {
  if (from.startsWith(`${IPAD_PAGES}/`)) {
    return spec.startsWith('../')
      ? `${SCRIPTS}/${spec.slice(3).replace(/\.js$/, '.ts')}`
      : posix.join(IPAD_PAGES, spec);
  }
  return posix.join(posix.dirname(from), spec.replace(/\.js$/, '.ts'));
}

// ---- The iPad app's folders ---------------------------------------------------------------------------------
//
// A scan, not a Swift parser. What it cannot see, and what it may see wrongly:
// - A type is a name declared with struct, class, enum, protocol, actor or typealias at the top of a file (brace depth
//   0), named with a capital letter. A private or fileprivate one belongs to its file only.
// - A type nested in a type is not indexed: it is named through its outer type (`Outer.Inner`), which the scan sees,
//   or, inside its outer type, from the same file. A type an extension adds (`extension Outer { struct Inner }`) in
//   another folder is indexed as `Outer.Inner` and found only when written so; written bare inside another extension
//   of `Outer`, it is missed.
// - A mention is a name in code (comments and strings blanked, interpolations kept), not after a dot. A file's own
//   declarations at any depth hide another folder's type of the same name. A name declared in the file's own folder is
//   that folder's; a name declared in two other folders counts for both (a possible false edge).
// - Regex literals (`/…/`, none in the app) would be read as code. A name used only through type inference (a value
//   passed along without its type written) is no mention: the edge then runs through the file that names the type.
// - Only types are indexed: a free function, a global constant or a member an extension adds to a type of its own or of
//   SwiftUI (a view helper) is no type, so a call of one from another folder is missed (`barsText`, 2026-11-17).

const SWIFT_TYPE = /\b(struct|class|enum|protocol|actor|typealias|extension)\s+([A-Z]\w*(?:\.[A-Z]\w*)*)/g;
const SWIFT_NAME = /(?<![\w.])([A-Za-z_]\w*)(?:\s*\.\s*([A-Z]\w*))?/g;

/**
 * The Swift source with its comments and string literals blanked: each of their characters becomes a space and the
 * line breaks stay, so positions and line numbers still match the source. The code of an interpolation is kept.
 * Handles nested block comments, `"…"`, `"""…"""` and raw strings (`#"…"#`, `\#(…)`). With `strings: false` only the
 * comments are blanked and the string literals kept (governance/tests/baseline.test.mjs reads the walk's journeys so).
 */
export function swiftCode(text, { strings = true } = {}) {
  const chars = text.split('');
  const blank = (from, to) => {
    for (let k = from; k < to; k++) {
      if (chars[k] !== '\n') {
        chars[k] = ' ';
      }
    }
  };
  const lineComment = (i) => {
    const end = text.indexOf('\n', i);
    const stop = end < 0 ? text.length : end;
    blank(i, stop);
    return stop;
  };
  const blockComment = (i) => {
    let k = i + 2;
    for (let depth = 1; k < text.length && depth > 0;) {
      depth += text.startsWith('/*', k) ? 1 : text.startsWith('*/', k) ? -1 : 0;
      k += text.startsWith('/*', k) || text.startsWith('*/', k) ? 2 : 1;
    }
    blank(i, k);
    return k;
  };
  const blankString = strings ? blank : () => {};
  const string = (i) => {
    const hashes = /^#*/.exec(text.slice(i, i + 16))[0].length;
    const multi = text.startsWith('"""', i + hashes);
    const close = (multi ? '"""' : '"') + '#'.repeat(hashes);
    const escape = '\\' + '#'.repeat(hashes);
    let from = i;
    let k = i + hashes + (multi ? 3 : 1);
    while (k < text.length) {
      if (text.startsWith(`${escape}(`, k)) {
        blankString(from, k + escape.length + 1);
        k = code(k + escape.length + 1, true);
        from = k;
        k += 1;
      } else if (text.startsWith(escape, k)) {
        k += escape.length + 1;
      } else if (text.startsWith(close, k)) {
        blankString(from, k + close.length);
        return k + close.length;
      } else if (!multi && text[k] === '\n') {
        break;
      } else {
        k += 1;
      }
    }
    blankString(from, k);
    return k;
  };
  // Code up to the end of the text, or, in an interpolation, up to the parenthesis that closes it.
  function code(i, interpolation) {
    let depth = 0;
    while (i < text.length) {
      const c = text[i];
      if (c === '/' && text[i + 1] === '/') {
        i = lineComment(i);
      } else if (c === '/' && text[i + 1] === '*') {
        i = blockComment(i);
      } else if (c === '"' || (c === '#' && /^#+"/.test(text.slice(i, i + 16)))) {
        i = string(i);
      } else if (interpolation && c === ')' && depth === 0) {
        return i;
      } else {
        depth += interpolation && c === '(' ? 1 : interpolation && c === ')' ? -1 : 0;
        i += 1;
      }
    }
    return i;
  }
  code(0, false);
  return chars.join('');
}

/** The brace depth before each position of the code. */
function braceDepths(code) {
  const depths = new Int32Array(code.length + 1);
  let depth = 0;
  for (let i = 0; i < code.length; i++) {
    depths[i] = depth;
    depth += code[i] === '{' ? 1 : code[i] === '}' ? -1 : 0;
  }
  depths[code.length] = depth;
  return depths;
}

/**
 * The types one Swift file declares: `shared`, its folder's (top-level, not private, and those an extension adds, as
 * `Outer.Inner`); `local`, every name it declares at any depth, which hides another folder's type of that name.
 */
function swiftDeclarations(code) {
  const depths = braceDepths(code);
  const shared = [];
  const local = new Set();
  let extension = null; // the top-level extension block around the position: its target and where it ends
  for (const m of code.matchAll(SWIFT_TYPE)) {
    const [, kind, name] = m;
    const depth = depths[m.index];
    if (kind === 'extension') {
      extension = depth === 0 ? { name, end: blockEnd(code, depths, m.index) } : extension;
      continue;
    }
    local.add(name);
    const line = code.slice(code.lastIndexOf('\n', m.index) + 1, m.index);
    if (depth === 0 && !/\b(private|fileprivate)\b/.test(line)) {
      shared.push(name);
    } else if (depth === 1 && extension && m.index < extension.end) {
      shared.push(`${extension.name}.${name}`);
    }
  }
  return { shared, local };
}

/** Where the block that opens after a top-level position closes. */
function blockEnd(code, depths, from) {
  const open = code.indexOf('{', from);
  if (open < 0) {
    return from;
  }
  let k = open + 1;
  while (k < code.length && depths[k + 1] > 0) {
    k += 1;
  }
  return k;
}

/** The type names a file's code mentions, each with the line of its first mention: `Outer.Inner` when so indexed. */
function swiftMentions(code, qualified) {
  const out = new Map();
  let line = 1;
  let at = 0;
  for (const m of code.matchAll(SWIFT_NAME)) {
    for (; at < m.index; at++) {
      line += code[at] === '\n' ? 1 : 0;
    }
    const [, name, member] = m;
    const key = member && qualified.has(`${name}.${member}`) ? `${name}.${member}` : name;
    if (!out.has(key)) {
      out.set(key, line);
    }
  }
  return out;
}

/** The folder of a file below src/ipad/App/MotiffLab: its first directory, '' for a file at the top. */
function ipadFolder(file) {
  const parts = posix.relative(IPAD_FOLDERS, file).split('/');
  return parts.length > 1 ? parts[0] : '';
}

function mayUse(from, to, uses) {
  const allowed = uses[from];
  return from === to || allowed === '*' || (Array.isArray(allowed) && allowed.includes(to));
}

function allowedText(folder, uses) {
  const allowed = uses[folder];
  return allowed === '*' ? 'every folder' : allowed.length ? allowed.join(', ') : 'nothing above it';
}

/**
 * Each type of another folder that an iPad file names although its folder may not use that folder (rule
 * "ipad-folder", one violation per file and type, the symbol "<Folder>: <Type>"), and each file in a folder the table
 * does not list (rule "ipad-folder-unlisted").
 */
export function ipadFolderViolations(files, read, uses = IPAD_FOLDER_USES) {
  const scanned = files.map((file) => ({ file, folder: ipadFolder(file), code: swiftCode(read(file)) }));
  const index = new Map();
  for (const f of scanned) {
    f.declared = swiftDeclarations(f.code);
    for (const name of f.declared.shared) {
      index.set(name, [...(index.get(name) ?? []), f]);
    }
  }
  const qualified = new Set([...index.keys()].filter((name) => name.includes('.')));
  const out = [];
  for (const f of scanned) {
    if (!(f.folder in uses)) {
      out.push(violation('ipad-folder-unlisted', f.file, f.folder, 1, `${f.file} is in a folder of the iPad app that `
        + 'IPAD_FOLDER_USES (tools/quality/dependencies.mjs) does not list: add it with the folders it may use.'));
      continue;
    }
    for (const [name, line] of swiftMentions(f.code, qualified)) {
      const owners = index.get(name) ?? [];
      if (f.declared.local.has(name) || owners.length === 0 || owners.some((o) => o.folder === f.folder)) {
        continue;
      }
      for (const folder of [...new Set(owners.map((o) => o.folder))].sort()) {
        if (!mayUse(f.folder, folder, uses)) {
          const where = owners.filter((o) => o.folder === folder).map((o) => o.file).join(', ');
          out.push(violation('ipad-folder', f.file, `${folder}: ${name}`, line, `${f.file} names ${name}, a type of `
            + `${folder}/ (${where}); ${f.folder}/ may use ${allowedText(f.folder, uses)}. Move the type down to `
            + 'Platform, read it through a protocol the folder may use, or let Workspace do it; if the task cannot, '
            + 'node tools/quality/quality.mjs --only dependencies --update-baseline --why "<reason> (#issue)".'));
        }
      }
    }
  }
  return out;
}
