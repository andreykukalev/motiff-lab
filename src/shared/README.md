# Shared score layer

What the iPad app bundles from the former web project ([0033](../../docs/architecture/adr/0033-repository-layout.md)); iPad
work owns it. `src/ipad/App/project.yml` copies it into the app.

| Path | What it is |
| --- | --- |
| `Scripts/score`, `roll`, `layout`, `storage`, `playback` | TypeScript the iPad's score page and roll run (`src/ipad/App/Web`); maps in `docs/code-map/score.md` and `roll.md` |
| `wwwroot/js/` | Their compiled output, by `dotnet build src/shared` (or the solution build); not committed |
| `wwwroot/i18n` | The interface words in English and Russian that the pages or more than one iPad folder say (the rest: each folder's `Words`) |
| `wwwroot/audio/piano` | The piano samples (`tools/piano`) |
| `wwwroot/lib/verovio` | Verovio's unmodified npm build, which the node tests engrave with (`tools/verovio`) |
| `Playback/FurElise.musicxml` | The example piece (`tools/fur-elise`) |
| `MotiffLab.Shared.csproj`, `tsconfig.json` | The TypeScript build (Microsoft.TypeScript.MSBuild) |
| `package.json`, `eslint.config.mjs` | ESLint for the quality check (`tools/quality`) |

Tests: `tests/shared` (`node --test "tests/shared/*.test.mjs"` after the build). Nothing here imports `archive/`.
