// Code-quality checks for the TypeScript layer (#332). Only size and complexity rules: style is tsc's job.
// The thresholds come from governance/quality/settings.json, the one place they are kept; the ratchet in
// tools/quality decides what fails (governance/quality/README.md), so the rules here only warn.
import { readFileSync } from 'node:fs';
import sonarjs from 'eslint-plugin-sonarjs';
import tseslint from 'typescript-eslint';

const { thresholds } = JSON.parse(
  readFileSync(new URL('../../governance/quality/settings.json', import.meta.url), 'utf8'));

export default [
  {
    files: ['Scripts/**/*.ts'],
    languageOptions: { parser: tseslint.parser, ecmaVersion: 2022, sourceType: 'module' },
    plugins: { sonarjs },
    // Inline directives are ignored, so a comment cannot hide a violation; the check "suppressions" reports them.
    linterOptions: { noInlineConfig: true, reportUnusedDisableDirectives: 'off' },
    rules: {
      'sonarjs/cognitive-complexity': ['warn', thresholds.functionComplexity],
      'max-lines-per-function': ['warn', { max: thresholds.functionLines, IIFEs: true }],
      'max-depth': ['warn', thresholds.nesting],
      'max-lines': ['warn', { max: thresholds.fileLines }],
    },
  },
];
