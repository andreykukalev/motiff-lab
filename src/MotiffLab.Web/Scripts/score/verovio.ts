// Verovio, loaded once on first use and shared by every engraving on the page (decision record 0008): the
// score view, the phrase card in focus (0018 section 4) and the PDF export each make a toolkit of their own
// from the one module, so one never changes the layout or the ids of another.

import type { TimemapEntry } from "./score-timeline.js";

/** The part of Verovio's VerovioToolkit the app uses. */
export interface VerovioToolkit {
    setOptions(options: Record<string, unknown>): void;
    /** 1 when the data was read, 0 when not. */
    loadData(data: string): number;
    getPageCount(): number;
    renderToSVG(page: number): string;
    renderToTimemap(options?: Record<string, unknown>): TimemapEntry[];
    redoLayout(options?: Record<string, unknown>): void;
    /** The MIDI values of an engraved note, its key among them; empty for a rest. */
    getMIDIValuesForElement(id: string): { pitch?: number };
    getLog(): string;
    /** Frees the toolkit's memory; it cannot be used after. */
    destroy?(): void;
}

export interface VerovioWrapper {
    VerovioToolkit: new (module: unknown) => VerovioToolkit;
    enableLogToBuffer(value: boolean, module: unknown): void;
}

const VEROVIO_BASE = new URL("../../lib/verovio/", import.meta.url);

let verovio: Promise<{ module: unknown; wrapper: VerovioWrapper }> | null = null;

/** Verovio's module and its JavaScript wrapper, downloaded on the first call. */
export function loadVerovio(): Promise<{ module: unknown; wrapper: VerovioWrapper }> {
    verovio ??= (async () => {
        const [factory, wrapper] = await Promise.all([
            import(new URL("verovio-module.mjs", VEROVIO_BASE).href) as Promise<{ default: () => Promise<unknown> }>,
            import(new URL("verovio.mjs", VEROVIO_BASE).href) as Promise<VerovioWrapper>,
        ]);
        const module = await factory.default();
        wrapper.enableLogToBuffer(true, module);
        return { module, wrapper };
    })();
    // A failed download can be retried on the next load.
    verovio.catch(() => {
        verovio = null;
    });
    return verovio;
}

/** A toolkit of its own, for one engraving; destroy it when done. */
export async function newToolkit(): Promise<VerovioToolkit> {
    const { module, wrapper } = await loadVerovio();
    return new wrapper.VerovioToolkit(module);
}
