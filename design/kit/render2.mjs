import createVerovioModule from 'verovio/wasm';
import { VerovioToolkit } from 'verovio/esm';
import fs from 'fs';
const mod = await createVerovioModule();
const tk = new VerovioToolkit(mod);
for (const [file,out] of [['phrase.musicxml','phrase.svg'],['ossia.musicxml','ossia.svg']]) {
  tk.setOptions({ pageHeight: 600, pageWidth: 3400, scale: 50, font: 'Leland', header: 'none', footer: 'none', breaks: 'none', adjustPageHeight: true, adjustPageWidth: true, pageMarginTop: 20, pageMarginBottom: 20, pageMarginLeft: 20, pageMarginRight: 20, svgViewBox: true, svgRemoveXlink: true });
  tk.loadData(fs.readFileSync(file,'utf8'));
  fs.writeFileSync(out, tk.renderToSVG(1));
  console.log(out, tk.getPageCount());
}
