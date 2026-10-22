# Verovio (score view)

The score view (S1-W7, decision record 0008) engraves notation with Verovio. The files in `src/MotiffLab.Web/wwwroot/lib/verovio` are the unmodified npm build; nothing in them is changed.

## Source and licence

| | |
| --- | --- |
| Library | Verovio 6.3.0 (`verovio@6.3.0` on npm, published 2026-08-19, toolkit version `6.3.0-425dd7b`) |
| Authors | Laurent Pugin, RISM Digital Center, and contributors |
| Source | <https://github.com/rism-digital/verovio>, tag `version-6.3.0` |
| Licence | GNU Lesser General Public License, version 3 or later (LGPL-3.0-or-later) |

| File | From the npm package | SHA-256 |
| --- | --- | --- |
| `verovio-module.mjs` | `dist/verovio-module.mjs`: the toolkit compiled to WebAssembly, as one ES module with the WebAssembly inside (MusicXML and MEI input, no Humdrum) | `cd7c77d0738e9359b9f77956ae398556bf9949f35e4c5b3471ab3198fd660431` |
| `verovio.mjs` | `dist/verovio.mjs`: the JavaScript wrapper (`VerovioToolkit`) | `3db61c94295aeb4983baff06b5e03b2fc0e397fa405fa2461f81427954ec874b` |
| `COPYING.LESSER.txt`, `COPYING.txt` | `COPYING.LESSER` and `COPYING` from the source tag: the LGPL and the GPL it builds on | |

The package tarball `verovio-6.3.0.tgz` has SHA-256 `503fa08873eea0b49208bed2450835603c49835abf0faacca5357bb9a9ca0a41`.

How Motiff Lab meets the LGPL (section 4, combined works): Verovio is loaded at run time as separate, unmodified files, so a player can replace them with another build; the licence texts are published next to them; and the score view credits Verovio, names the licence and links to the source. Our own code is not a modification of Verovio and stays under its own terms.

## Updating

```bash
npm pack verovio@<version>
tar xzf verovio-<version>.tgz
cp package/dist/verovio-module.mjs package/dist/verovio.mjs src/MotiffLab.Web/wwwroot/lib/verovio/
```

Then, in the same change: update the version and checksums above, the licence files if they changed, `VEROVIO_CACHE` in `wwwroot/service-worker.published.js` (so devices fetch the new files), the credit in the score view, and run the Node tests, which load the vendored build.
