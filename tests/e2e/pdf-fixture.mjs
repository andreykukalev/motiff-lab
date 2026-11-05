// A PDF made in the test, so no binary fixture is checked in (0023).

/** A small PDF of the given number of pages, each with a staff of five lines, a page number in Helvetica, and a box. */
export function pdf(pages) {
    const objects = [];
    const add = (body) => {
        objects.push(body);
        return objects.length;
    };
    const catalog = add("");
    const tree = add("");
    const font = add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
    const kids = [];
    for (let number = 1; number <= pages; number++) {
        const lines = [0, 1, 2, 3, 4].map((line) => `60 ${700 - line * 8} m 535 ${700 - line * 8} l S`).join("\n");
        const stream = `0.8 w\n${lines}\nBT /F1 18 Tf 280 60 Td (${number}) Tj ET\n100 400 395 120 re S\n`;
        const content = add(`<< /Length ${stream.length} >>\nstream\n${stream}endstream`);
        kids.push(add(`<< /Type /Page /Parent ${tree} 0 R /MediaBox [0 0 595 842] /Contents ${content} 0 R /Resources << /Font << /F1 ${font} 0 R >> >> >>`));
    }
    objects[catalog - 1] = `<< /Type /Catalog /Pages ${tree} 0 R >>`;
    objects[tree - 1] = `<< /Type /Pages /Kids [${kids.map((kid) => `${kid} 0 R`).join(" ")}] /Count ${pages} >>`;

    let text = "%PDF-1.4\n";
    const offsets = [];
    objects.forEach((body, index) => {
        offsets.push(text.length);
        text += `${index + 1} 0 obj\n${body}\nendobj\n`;
    });
    const xref = text.length;
    text += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
    text += offsets.map((offset) => `${String(offset).padStart(10, "0")} 00000 n \n`).join("");
    text += `trailer\n<< /Size ${objects.length + 1} /Root ${catalog} 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
    return Buffer.from(text, "latin1");
}
