# Motiff Lab web app (archived)

Archived on 2026-11-16 by [0032](../../docs/decisions/0032-ipad-first-web-archived.md): the iPad app is the only
actively developed client. This folder stays in place and keeps compiling; it is not developed, browser-tested or
deployed.

Not everything here is frozen. The iPad app bundles these files, so they are **live** and maintained by iPad work:
`Scripts/score`, `Scripts/roll`, `Scripts/layout`, `Scripts/storage`, `Scripts/playback` (compiled to `wwwroot/js/` by
`dotnet build`), `wwwroot/i18n`, `wwwroot/audio/piano`, `Playback/FurElise.musicxml`, the design kit's mirrors (`wwwroot/css/tokens.css`,
`wwwroot/css/fonts`, `wwwroot/img/icons.svg`), and the TypeScript build in
`MotiffLab.Web.csproj`, `tsconfig.json` and `package.json`. Everything else is **frozen**: no features, fixes or ports;
only the smallest mechanical fix if a change elsewhere breaks its compilation.

The restore point (tag `v0.6.5`, `main` 9b98d6b), the full list of live and frozen files, the archived workflows and how
to reactivate the web: [`docs/web-archive.md`](../../docs/web-archive.md).
