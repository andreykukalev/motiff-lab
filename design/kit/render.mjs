import createVerovioModule from 'verovio/wasm';
import { VerovioToolkit } from 'verovio/esm';
import fs from 'fs';
const mod = await createVerovioModule();
const tk = new VerovioToolkit(mod);
const font = process.argv[2] || 'Leland';
tk.setOptions({
  pageHeight: 2970, pageWidth: 2100, scale: 50, font,
  header: 'auto', footer: 'none', breaks: 'auto', justifyVertically: false,
  pageMarginTop: 500, pageMarginBottom: 160, pageMarginLeft: 150, pageMarginRight: 150,
  spacingSystem: 20, spacingStaff: 10, svgViewBox: true, adjustPageHeight: false,
  svgRemoveXlink: true, svgHtml5: false
});
tk.loadData(fs.readFileSync('elise.musicxml','utf8'));
const n = tk.getPageCount();
console.log('pages', n);
for (let p=1;p<=n;p++){ fs.writeFileSync(`page${p}_${font}.svg`, tk.renderToSVG(p)); }
