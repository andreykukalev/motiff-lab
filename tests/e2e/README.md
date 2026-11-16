# Browser tests (archived)

Playwright specs on the published web app (browsers, offline, WCAG 2.2 AA). Archived with the web app on 2026-11-16
([0032](../../docs/decisions/0032-ipad-first-web-archived.md)): they are not part of `build-test`, version tags or
releases, and run only by hand from the Actions tab (`Browser tests`). They were red at archival (since v0.6.1).

To run them locally against a published build:

```bash
dotnet publish src/MotiffLab.Web -c Release -o publish && (cd tests/e2e && npm ci && npx playwright test)
```

Reactivation: [`docs/web-archive.md`](../../docs/web-archive.md).
