# pdf.js (PDF pieces)

A PDF the player adds to read as it is (decision record 0023) is shown page by page with Mozilla's pdf.js. The files in `src/MotiffLab.Web/wwwroot/lib/pdfjs` are the unmodified npm build; nothing in them is changed. `Scripts/pdf/pdf-view.ts` loads them with a dynamic import the first time a PDF piece opens, and the service worker caches them then (`pdfjs-…` in `wwwroot/service-worker.published.js`), so they are not installed with the app.

## Source and licence

| | |
| --- | --- |
| Library | pdf.js 6.4.299 (`pdfjs-dist@6.4.299` on npm, published 2026-10-03) |
| Authors | Mozilla Foundation and contributors |
| Source | <https://github.com/mozilla/pdf.js>, tag `v6.4.299` |
| Licence | Apache License 2.0 (`LICENSE`); the image decoders in `wasm/` are BSD (PDFium's JBIG2, OpenJPEG) and MIT (qcms), licences beside them; the fonts in `standard_fonts/` are Foxit's (BSD-style, `LICENSE_FOXIT`) |

| File | From the npm package | SHA-256 |
| --- | --- | --- |
| `pdf.min.mjs` | `build/pdf.min.mjs` | `57456c8e0c81e46be31174b499ef77f2b9f5ee46d04412ba627320a36755d4c2` |
| `pdf.worker.min.mjs` | `build/pdf.worker.min.mjs` | `9536359f1b8367850d485731ca1d5e45c159a7b7a0912325e539937aa21ceb18` |
| `wasm/jbig2.wasm`, `wasm/openjpeg.wasm`, `wasm/qcms_bg.wasm` and their licences | `wasm/` (decoders for scanned pages: JBIG2 and JPEG 2000 images, colour profiles) | |
| `standard_fonts/` | `standard_fonts/Foxit*` and `LICENSE_FOXIT` (the standard PDF fonts, for files that do not embed them). The Liberation Sans files are left out (GPL with a font exception); a sans-serif title in a file that does not embed it falls back to the browser's font | |
| `LICENSE` | `LICENSE` | |

The package tarball `pdfjs-dist-6.4.299.tgz` has SHA-256 `86269b40170eb41740ea05ad2102d71d33de4f122b34eb2f478be0ea7779c415`. The CJK character maps (`cmaps/`) are left out: printed music rarely needs them, and a page that does still shows its music.

## Updating

```bash
npm pack pdfjs-dist@<version>
tar xzf pdfjs-dist-<version>.tgz
cp package/build/pdf.min.mjs package/build/pdf.worker.min.mjs package/LICENSE src/MotiffLab.Web/wwwroot/lib/pdfjs/
cp package/wasm/*.wasm package/wasm/LICENSE_* src/MotiffLab.Web/wwwroot/lib/pdfjs/wasm/   # leave out the quickjs files
cp package/standard_fonts/Foxit* package/standard_fonts/LICENSE_FOXIT src/MotiffLab.Web/wwwroot/lib/pdfjs/standard_fonts/
```

Then, in the same change: update the version and checksums above, `pdfjsCache` in `wwwroot/service-worker.published.js` (so devices fetch the new files), and run the browser test of PDF pieces.
