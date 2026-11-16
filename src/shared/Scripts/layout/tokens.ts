// The design kit's tokens (design/kit/tokens.css, 0018) as the page has them now, for what CSS cannot colour:
// drawing on a canvas. The theme can change while the app runs, so callers read the tokens when they draw.

/** A token's value on the element (the document by default), such as token("accent"); "" when it is not defined. */
export function token(name: string, element: Element = document.documentElement): string {
    return getComputedStyle(element).getPropertyValue(`--${name}`).trim();
}

/** The red, green, blue and alpha of a colour written as #rgb, #rrggbb, #rrggbbaa, rgb() or rgba(); null for anything else. */
export function parseColour(colour: string): [number, number, number, number] | null {
    const text = colour.trim();
    const hex = /^#([0-9a-f]{3}|[0-9a-f]{6}|[0-9a-f]{8})$/i.exec(text);
    if (hex !== null) {
        const digits = hex[1].length === 3 ? [...hex[1]].map((d) => d + d).join("") : hex[1];
        const value = (at: number): number => parseInt(digits.slice(at, at + 2), 16);
        return [value(0), value(2), value(4), digits.length === 8 ? value(6) / 255 : 1];
    }
    const functional = /^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:[\s,/]+([\d.]+%?))?\s*\)$/i.exec(text);
    if (functional !== null) {
        const alpha = functional[4] === undefined ? 1 : functional[4].endsWith("%") ? parseFloat(functional[4]) / 100 : parseFloat(functional[4]);
        return [Number(functional[1]), Number(functional[2]), Number(functional[3]), alpha];
    }
    return null;
}

/** The colour at the given opacity (its own opacity times alpha), as rgba(); the colour unchanged when it cannot be read. */
export function withAlpha(colour: string, alpha: number): string {
    const parts = parseColour(colour);
    if (parts === null) {
        return colour;
    }
    const [r, g, b, a] = parts;
    return `rgba(${r}, ${g}, ${b}, ${round(a * alpha)})`;
}

/** The first colour with the second laid over it at the given share (0 to 1), opaque; the first when either cannot be read. */
export function mix(under: string, over: string, share: number): string {
    const a = parseColour(under);
    const b = parseColour(over);
    if (a === null || b === null) {
        return under;
    }
    const weight = Math.min(1, Math.max(0, share * b[3]));
    const channel = (i: number): number => Math.round(a[i] * (1 - weight) + b[i] * weight);
    return `rgb(${channel(0)}, ${channel(1)}, ${channel(2)})`;
}

function round(value: number): number {
    return Math.round(value * 1000) / 1000;
}
