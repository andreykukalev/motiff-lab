# Code guidelines

What every change follows. The official guides below are the reference; this file does not retell them, it adds only
what a linter cannot check. Size and complexity limits and the ratchet: `governance/quality/README.md`. Product rules
(time, provenance, tokens, languages, privacy): `CLAUDE.md`.

## Official sources

- C#: [.NET coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions),
  [Framework Design Guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/),
  [code analysis rules](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview).
- TypeScript: [TypeScript handbook](https://www.typescriptlang.org/docs/handbook/),
  [typescript-eslint recommended and strict configs](https://typescript-eslint.io/users/configs/).
- Swift and SwiftUI: [Swift API Design Guidelines](https://www.swift.org/documentation/api-design-guidelines/),
  [SwiftLint rules](https://realm.github.io/SwiftLint/rule-directory.html).
- Blazor: [ASP.NET Core Blazor guidance](https://learn.microsoft.com/aspnet/core/blazor/).
- Accessibility: [WCAG 2.2 AA](https://www.w3.org/TR/WCAG22/).

## Project rules a linter cannot check

1. **Extend or replace, never stack.** One model per concept (selection, time, structure, playback state). A change
   that replaces behaviour deletes the old code path in the same pull request; no new layer beside one that still runs.
2. **The right layer.** Analysis, notes and rules in Music Core; the apps only display and route input. Shared score
   and roll behaviour lives once in `Scripts/score` and `Scripts/roll`, which both apps run.
3. **One responsibility per type and file.** When a change adds a responsibility to a big file, it moves that part to
   its own file; the code map of the area (`docs/code-map/`) names the new file.
4. **State has one owner.** A piece of state is written in one place; others read it or get an event. No second copy
   kept in sync by hand.
5. **No hidden side effects.** A function that changes shared state, starts a timer or posts an event says so in its
   name or signature; a change lists in its pull request what else reads the state it touches.
6. **A rule has a test.** A changed rule gets a test that fails without the change; a test is never rewritten to match
   a regression.
7. **Delete what is unused.** Dead code, an experiment called «убрать», a switch with no owner go in the change that
   finds them, if they are in its area.
