// The player's photo for their profile (Andrei, 2026-11-06): the middle square of the picture they chose, made 256
// pixels across and kept as a JPEG data URL, a few tens of kilobytes, so it travels inside the profile itself. The same
// size and quality as the iPad app's (Account.swift).

/** What Blazor hands over for a stream (DotNetStreamReference). */
interface StreamReference {
    arrayBuffer(): Promise<ArrayBuffer>;
}

export const PHOTO_SIZE = 256;
export const PHOTO_QUALITY = 0.85;

/** The picture's middle square, `size` pixels across, as a JPEG data URL; null when the file is no picture the browser reads. */
export async function squarePhoto(stream: StreamReference, type: string, size: number = PHOTO_SIZE): Promise<string | null> {
    let bitmap: ImageBitmap;
    try {
        bitmap = await createImageBitmap(new Blob([await stream.arrayBuffer()], { type }));
    } catch {
        return null;
    }

    const side = Math.min(bitmap.width, bitmap.height);
    const canvas = document.createElement("canvas");
    canvas.width = size;
    canvas.height = size;
    const context = canvas.getContext("2d");
    if (context === null || side === 0) {
        bitmap.close();
        return null;
    }

    context.imageSmoothingQuality = "high";
    context.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, size, size);
    bitmap.close();
    return canvas.toDataURL("image/jpeg", PHOTO_QUALITY);
}
