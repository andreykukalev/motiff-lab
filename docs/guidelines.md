# Code guidelines

What every change follows. The official guides below are the reference; this file does not retell them. Size and complexity limits and the ratchet: `governance/quality/README.md`. Product rules
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

## Project rules

The project's own rules, the ones a linter cannot check (reuse before adding, the smallest diff, one model per
concept, the right layer, what a duplicate is, side effects, tests, deletion, the cleanup pass), are in `CLAUDE.md`,
«Writing code», so that every agent has them in its context. They are the pull request reviewer's checklist.
