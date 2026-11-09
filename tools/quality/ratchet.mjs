// The ratchet of the code-quality checks (#332, governance/quality/README.md), without I/O so that
// tools/quality/ratchet.test.mjs can test it.
//
// A violation is { tool, rule, file, symbol, line, value, limit, message }. Its key is tool, rule, file and
// symbol: never the line, so that code moving up or down a file keeps its baseline entry. Several violations may
// share a key (overloads, two duplicated blocks between the same files); the baseline then lists the key as often.

/** The baseline entry's identity. */
export function keyOf(v) {
  return [v.tool, v.rule, v.file, v.symbol ?? ''].join('\u0000');
}

/** Rules whose value gets worse as it falls (CA1505: maintainability index); every other value is worse higher. */
const LOWER_IS_WORSE = new Set(['CA1505']);

function worse(rule, now, then) {
  if (typeof now !== 'number' || typeof then !== 'number') {
    return false;
  }
  return LOWER_IS_WORSE.has(rule) ? now < then : now > then;
}

function groupBy(items) {
  const groups = new Map();
  for (const item of items) {
    const key = keyOf(item);
    if (!groups.has(key)) {
      groups.set(key, []);
    }
    groups.get(key).push(item);
  }
  return groups;
}

function byWorst(rule) {
  return (a, b) => (LOWER_IS_WORSE.has(rule) ? (a.value ?? 0) - (b.value ?? 0) : (b.value ?? 0) - (a.value ?? 0));
}

/**
 * Compares the current violations of the tools that ran with the baseline.
 * Returns:
 * - added: violations whose key is not in the baseline, or is there fewer times;
 * - stale: baseline entries no violation matches any more (the code was fixed, moved or renamed);
 * - grew: matched violations whose value got worse than the baseline's (reported, never failing);
 * - unjustified: baseline entries without a reason in `why`.
 * Baseline entries of tools that did not run are neither matched nor stale.
 */
export function compare(current, baseline, ranTools) {
  const ran = new Set(ranTools);
  const relevant = baseline.filter((e) => ran.has(e.tool));
  const now = groupBy(current);
  const then = groupBy(relevant);
  const added = [];
  const stale = [];
  const grew = [];
  for (const [key, list] of now) {
    const entries = [...(then.get(key) ?? [])];
    const rule = list[0].rule;
    const sorted = [...list].sort(byWorst(rule));
    entries.sort(byWorst(rule));
    sorted.forEach((violation, i) => {
      const entry = entries[i];
      if (!entry) {
        added.push(violation);
      } else if (worse(rule, violation.value, entry.value)) {
        grew.push({ violation, entry });
      }
    });
  }
  for (const [key, entries] of then) {
    const count = now.get(key)?.length ?? 0;
    stale.push(...entries.slice(count));
  }
  const unjustified = baseline.filter((e) => typeof e.why !== 'string' || e.why.trim() === '');
  return { added, stale, grew, unjustified };
}

/** Baseline entries in `head` that `base` does not have (as many times): what a pull request added. */
export function addedEntries(head, base) {
  const counts = new Map();
  for (const e of base) {
    counts.set(keyOf(e), (counts.get(keyOf(e)) ?? 0) + 1);
  }
  const added = [];
  for (const e of head) {
    const left = counts.get(keyOf(e)) ?? 0;
    if (left > 0) {
      counts.set(keyOf(e), left - 1);
    } else {
      added.push(e);
    }
  }
  return added;
}

/**
 * The new baseline after `--update-baseline`: stale entries of the tools that ran are removed, added violations
 * get an entry with `why` (empty unless given, so the check asks for a reason), every other entry stays as it is.
 */
export function updateBaseline(baseline, current, ranTools, why = '') {
  const { added, stale } = compare(current, baseline, ranTools);
  const removed = new Set(stale);
  const kept = baseline.filter((e) => !removed.has(e));
  const fresh = added.map((v) => ({
    tool: v.tool,
    rule: v.rule,
    file: v.file,
    symbol: v.symbol ?? '',
    value: v.value,
    why,
  }));
  return sortEntries([...kept, ...fresh]);
}

export function sortEntries(entries) {
  const text = (e) => [e.tool, e.file, e.rule, e.symbol ?? ''].join('\u0000');
  return [...entries].sort((a, b) => (text(a) < text(b) ? -1 : text(a) > text(b) ? 1 : (a.value ?? 0) - (b.value ?? 0)));
}

/** One entry per line, so that a pull request's diff of the baseline shows exactly what it added or removed. */
export function formatBaseline(entries, note) {
  const lines = sortEntries(entries).map((e) =>
    '    ' + JSON.stringify({ tool: e.tool, rule: e.rule, file: e.file, symbol: e.symbol ?? '', value: e.value, why: e.why ?? '' }));
  return `{\n  "note": ${JSON.stringify(note)},\n  "entries": [\n${lines.join(',\n')}\n  ]\n}\n`;
}

// ---- Parsers of the tools' output ---------------------------------------------------------------------------

const DOTNET_WARNING = /^\s*(?:\d+(?::\d+)?>)?(.+?)\((\d+),(\d+)\): warning (CA150[256]): (.*?)(?: \(https?:[^)]*\))? \[[^\]]*\]\s*$/;

/** The CA1502/CA1505/CA1506 warnings of a `dotnet build` log, once each (MSBuild may repeat a warning). */
export function parseDotnetLog(text, relative) {
  const seen = new Set();
  const out = [];
  for (const line of text.split(/\r?\n/)) {
    const m = DOTNET_WARNING.exec(line);
    if (!m) {
      continue;
    }
    const [, path, row, col, rule, message] = m;
    const file = relative(path);
    const id = `${file}:${row}:${col}:${rule}`;
    if (seen.has(id)) {
      continue;
    }
    seen.add(id);
    const quoted = [...message.matchAll(/'([^']*)'/g)].map((q) => q[1]);
    out.push({
      tool: 'dotnet',
      rule,
      file,
      symbol: quoted[0] ?? '',
      line: Number(row),
      value: quoted[1] !== undefined && /^\d+$/.test(quoted[1]) ? Number(quoted[1]) : undefined,
      message,
    });
  }
  return out;
}

/** A Swift declaration's name from the line SwiftLint points at; the trimmed line when it declares nothing. */
export function swiftSymbol(lineText) {
  const text = (lineText ?? '').trim();
  const decl = /\b(func|init|deinit|subscript|class|struct|enum|extension|actor|protocol|var|let)\b\s*([A-Za-z_][\w.]*)?/.exec(text);
  if (decl) {
    const [, kind, name] = decl;
    if (kind === 'init' || kind === 'deinit' || kind === 'subscript') {
      return kind;
    }
    if (name) {
      return kind === 'func' || kind === 'var' || kind === 'let' ? name : `${kind} ${name}`;
    }
  }
  return text.slice(0, 80);
}

/** The measured value in a tool's message: "too many lines (186)", "Complexity from 22 to the 15 allowed",
 * "currently contains 655", "currently complexity is 19". */
export function valueIn(message) {
  for (const pattern of [/currently\D*?(\d+)/, /from (\d+) to/, /\((\d+)\)/]) {
    const m = pattern.exec(message ?? '');
    if (m) {
      return Number(m[1]);
    }
  }
  return undefined;
}
