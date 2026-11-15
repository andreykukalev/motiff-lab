// Photos of pages kept as one original (version 0.6, Sources sheet «Фото страниц»): the pictures the player picked, in
// order, become the pages of one PDF made on the device, a JPEG a page, as the project file keeps photos (kind "images",
// its bytes a PDF). The pictures never leave the device here; only Recognise sends the PDF, after the player says so.

/** The longer side of a page, in PDF points: an A4 page's height. */
const LONGER_SIDE = 842;

/** The JPEG quality a picture is written at: text and staff lines stay sharp, a page stays a few hundred kilobytes. */
const QUALITY = 0.9;

interface Picture {
    jpeg: Uint8Array;
    width: number;
    height: number;
}

async function asJpeg(bytes: Uint8Array, type: string): Promise<Picture> {
    const bitmap = await createImageBitmap(new Blob([bytes as BlobPart], { type }));
    const canvas = document.createElement("canvas");
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    const context = canvas.getContext("2d");
    if (!context) {
        throw new Error("No canvas to draw the picture on.");
    }
    // A transparent PNG goes onto white paper.
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.drawImage(bitmap, 0, 0);
    bitmap.close();
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/jpeg", QUALITY));
    if (!blob) {
        throw new Error("The picture could not be written as a JPEG.");
    }
    return { jpeg: new Uint8Array(await blob.arrayBuffer()), width: canvas.width, height: canvas.height };
}

/** The PDF of the pictures, a page each, scaled so a page's longer side is an A4 page's height. */
export function pdfOf(pictures: Picture[]): Uint8Array {
    const encoder = new TextEncoder();
    const parts: Uint8Array[] = [];
    const offsets: number[] = [];
    let length = 0;
    const write = (part: Uint8Array | string) => {
        const bytes = typeof part === "string" ? encoder.encode(part) : part;
        parts.push(bytes);
        length += bytes.length;
    };
    const object = (number: number, body: () => void) => {
        offsets[number] = length;
        write(`${number} 0 obj\n`);
        body();
        write("\nendobj\n");
    };
    const count = 2 + pictures.length * 3;
    write("%PDF-1.4\n");
    object(1, () => write("<< /Type /Catalog /Pages 2 0 R >>"));
    const kids = pictures.map((_, index) => `${3 + index * 3} 0 R`).join(" ");
    object(2, () => write(`<< /Type /Pages /Kids [${kids}] /Count ${pictures.length} >>`));
    pictures.forEach((picture, index) => {
        const page = 3 + index * 3;
        const scale = LONGER_SIDE / Math.max(picture.width, picture.height);
        const width = Math.round(picture.width * scale);
        const height = Math.round(picture.height * scale);
        const content = `q ${width} 0 0 ${height} 0 0 cm /P Do Q`;
        object(page, () =>
            write(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${width} ${height}] /Contents ${page + 1} 0 R /Resources << /XObject << /P ${page + 2} 0 R >> >> >>`),
        );
        object(page + 1, () => write(`<< /Length ${content.length} >>\nstream\n${content}\nendstream`));
        object(page + 2, () => {
            write(`<< /Type /XObject /Subtype /Image /Width ${picture.width} /Height ${picture.height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${picture.jpeg.length} >>\nstream\n`);
            write(picture.jpeg);
            write("\nendstream");
        });
    });
    const xref = length;
    write(`xref\n0 ${count + 1}\n0000000000 65535 f \n`);
    for (let number = 1; number <= count; number++) {
        write(`${String(offsets[number]).padStart(10, "0")} 00000 n \n`);
    }
    write(`trailer\n<< /Size ${count + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);
    const pdf = new Uint8Array(length);
    let at = 0;
    for (const part of parts) {
        pdf.set(part, at);
        at += part.length;
    }
    return pdf;
}

/** The PDF of the pictures picked, in the order given (PNG or JPEG, each with its type); null when one cannot be read. */
export async function photosToPdf(pictures: Uint8Array[], types: string[]): Promise<Uint8Array | null> {
    try {
        const pages: Picture[] = [];
        for (let index = 0; index < pictures.length; index++) {
            pages.push(await asJpeg(pictures[index], types[index] ?? "image/jpeg"));
        }
        return pages.length > 0 ? pdfOf(pages) : null;
    } catch {
        return null;
    }
}
