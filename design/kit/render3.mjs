import createVerovioModule from 'verovio/wasm';
import { VerovioToolkit } from 'verovio/esm';
import fs from 'fs';
const mod = await createVerovioModule(); const tk = new VerovioToolkit(mod);
for (const k of ['a','b','c']) {
  tk.setOptions({ pageHeight: 400, pageWidth: 1800, scale: 50, font: 'Leland', header: 'none', footer: 'none', breaks: 'none', adjustPageHeight: true, adjustPageWidth: true, pageMarginTop: 20, pageMarginBottom: 20, pageMarginLeft: 20, pageMarginRight: 20, svgViewBox: true, svgRemoveXlink: true });
  tk.loadData(fs.readFileSync(`motif_${k}.musicxml`,'utf8')); fs.writeFileSync(`motif_${k}.svg`, tk.renderToSVG(1));
}
console.log('done');
