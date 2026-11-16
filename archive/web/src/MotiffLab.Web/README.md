# Motiff Lab web app (archived)

Archived on 2026-11-16 by [0032](../../../../docs/architecture/adr/0032-ipad-first-web-archived.md): the iPad app is the only
actively developed client. Moved here the same day by [0033](../../../../docs/architecture/adr/0033-repository-layout.md):
this folder is out of the solution and CI; it is not built, developed, tested or deployed, and nothing in it changes.

What the iPad bundles from the former project (the score, roll, layout, storage and playback scripts, the words, the
piano, Verovio's npm build and the example piece) moved to [`src/shared`](../../../../src/shared/README.md), so this project
does not compile by itself until it is reactivated.

The restore point (tag `v0.6.5`, `main` 9b98d6b), what moved where, the archived workflows and how to reactivate the web:
[`docs/web-archive.md`](../../../../docs/web-archive.md).
