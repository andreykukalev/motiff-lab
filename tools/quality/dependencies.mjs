// The dependency check (CLAUDE.md, "Writing code"): which module may use which, where neither the build nor CI
// would notice a break. Pure: the caller passes the file lists and a reader, so the tests need no repository.
//
// 1. music-core depends on nothing in the solution and on no web, cloud or AI library (CLAUDE.md, "Stack").
// 2. The iPad's score and roll pages (src/ipad/App/Web/*.js) load the web app's compiled modules from the folders the
//    app bundles (src/ipad/App/project.yml, wwwroot/js/<folder>). CI does not build the iPad app, so an import that
//    reaches a folder the app does not bundle would only fail on the device, as a page that never loads.
import { posix } from 'node:path';

export const CORE = 'src/backend/MotiffLab.MusicCore';
const SCRIPTS = 'src/shared/Scripts';
export const IPAD_PAGES = 'src/ipad/App/Web';
export const IPAD_PROJECT = 'src/ipad/App/project.yml';

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
