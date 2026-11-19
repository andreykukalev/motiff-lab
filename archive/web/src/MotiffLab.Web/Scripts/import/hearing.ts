// Hears a long recording on the scan server in parts (0014): music-core plans the parts, and this sends their sound,
// up to `most` at once, keeps each part heard on the device until music-core has joined them, and goes on from the
// parts already heard when a hearing was cut off. A busy or restarting server (429, 502, 503, 504, a lost connection, a
// request that takes too long) is asked again after a pause that grows, and fewer parts go at once: one, after such an
// answer, then more again as parts come back. Only the stretch's sound goes, part by part, to the address the player
// set; the notes heard stay on the device.

import { browserParts, type PartStore } from "../storage/heard-parts.js";
import { soundBetween } from "./recording.js";

/** A part of the stretch as music-core plans it, in milliseconds on the stretch's own clock. */
export interface PartToHear {
    index: number;
    sendFrom: number;
    sendTo: number;
    keepFrom: number;
    keepTo: number;
}

/** What to hear: the scan server, the stretch of the held recording (from `start` to `end`), its parts, at most how many at once. */
export interface HearRequest {
    address: string;
    start: number;
    end: number;
    parts: PartToHear[];
    most: number;
    fingerprint: string;
}

/** A part heard: its notes as MIDI in base64, or null with the server's words when it heard none in this part. */
export interface HeardPart {
    index: number;
    midi: string | null;
    engine: string;
    message: string | null;
}

/** Why the hearing stopped: the HTTP status, or 0 when no answer came ("network", "timeout"), and the server's words. */
export interface Failure {
    status: number;
    kind: "http" | "network" | "timeout";
    message: string | null;
}

/** The parts heard, in order, and why the hearing stopped short when it did; the parts heard are kept either way. */
export interface Hearing {
    parts: HeardPart[];
    failure: Failure | null;
}

/** One request's outcome: a part heard, a failure worth asking again (after `wait` ms when the server said), or one not. */
export type Outcome = { heard: HeardPart } | { retry: true; failure: Failure; wait?: number } | { failure: Failure };

/** What hearing needs from the browser, so tests can stand in for it. */
export interface Hearer {
    engine(): Promise<string | null>;
    send(part: PartToHear): Promise<Outcome>;
    store: PartStore;
    progress(done: number, total: number, retrying: boolean): void;
    wait(milliseconds: number): Promise<void>;
    now(): number;
}

/** How many times a part is sent before the hearing gives up. */
export const MOST_TRIES = 6;

/** Parts sent at once when hearing starts, at most. */
export const START_AT_ONCE = 2;

/** A part heard this many times slower than the fastest, for its length, waited behind another at the server. */
const SLOW = 1.6;

/** Parts heard in a row at the most allowed before one more may go at once again. */
const RAISE_AFTER = 6;

/** The pause before asking again, after `tries` failures: 4 s, doubling, at most a minute, and up to a second more. */
export function backoff(tries: number): number {
    return Math.min(60_000, 4000 * 2 ** Math.max(0, tries - 1)) + Math.floor(Math.random() * 1000);
}

/**
 * How many parts go at once (`allowed`): it starts at START_AT_ONCE and goes up by one once as many parts as are allowed
 * came back quickly, up to `most`. A busy answer drops it to one and caps it one below where it was; parts heard in a
 * row lift the cap again. Answers to requests sent before the last change do not count, so one burst of busy answers
 * drops it once.
 */
export class Pacer {
    allowed: number;
    private ceiling: number;
    private epoch = 0;
    private streak = 0;
    private fastest = Number.POSITIVE_INFINITY;

    constructor(readonly most: number) {
        this.ceiling = Math.max(1, most);
        this.allowed = Math.min(START_AT_ONCE, this.ceiling);
    }

    /** The mark a request is sent with. */
    ticket(): number {
        return this.epoch;
    }

    /** A part of `seconds` heard `elapsed` ms after it was sent with `ticket`. */
    heard(ticket: number, elapsed: number, seconds: number): void {
        const pace = elapsed / Math.max(1, seconds);
        const slow = Number.isFinite(this.fastest) && pace > this.fastest * SLOW && elapsed > 30_000;
        this.fastest = Math.min(this.fastest, pace);
        if (ticket !== this.epoch) {
            return;
        }
        if (slow) {
            // It waited behind another part: more at once would wait longer still.
            this.streak = 0;
            return;
        }
        this.streak++;
        if (this.allowed < this.ceiling && this.streak >= this.allowed) {
            this.allowed++;
            this.epoch++;
            this.streak = 0;
        } else if (this.allowed === this.ceiling && this.ceiling < this.most && this.streak >= RAISE_AFTER) {
            this.ceiling++;
            this.streak = 0;
        }
    }

    /** A busy answer, a lost connection or a timeout for a request sent with `ticket`. */
    failed(ticket: number): void {
        if (ticket !== this.epoch) {
            return;
        }
        this.ceiling = Math.max(1, this.allowed - 1);
        this.allowed = 1;
        this.epoch++;
        this.streak = 0;
    }
}

/** What every part of one stretch's hearing is kept under, before the part's own name. */
export function keyPrefix(request: HearRequest): string {
    return `${request.fingerprint}|${request.start}-${request.end}|${request.address}|`;
}

/** What a part is kept under: the recording's sound, the stretch, the server, and the part with its bounds. */
export function partKey(request: HearRequest, part: PartToHear): string {
    return `${keyPrefix(request)}${part.index}:${part.sendFrom}-${part.sendTo}:${part.keepFrom}-${part.keepTo}`;
}

/** The parts of this hearing kept from before, heard by the engine the server runs now when it says which. */
async function keptParts(request: HearRequest, hearer: Hearer, engine: string | null): Promise<Map<number, HeardPart>> {
    const heard = new Map<number, HeardPart>();
    if (!request.fingerprint) {
        return heard;
    }
    for (const part of request.parts) {
        const kept = await hearer.store.get(partKey(request, part));
        if (kept && (engine === null || kept.engine === engine)) {
            heard.set(part.index, { index: part.index, midi: kept.midi, engine: kept.engine, message: null });
        }
    }
    return heard;
}

/** Hears the parts not heard before, keeping each as it comes back; see the file's header for the rules. */
export async function hearParts(request: HearRequest, hearer: Hearer): Promise<Hearing> {
    const engine = await hearer.engine();
    const heard = await keptParts(request, hearer, engine);
    const total = request.parts.length;
    const waiting = request.parts.filter(part => !heard.has(part.index));
    const pacer = new Pacer(request.most);
    const tries = new Map<number, number>();
    const running = new Set<Promise<void>>();
    let failure: Failure | null = null;
    let retrying = 0;
    hearer.progress(heard.size, total, false);

    const one = async (part: PartToHear) => {
        const ticket = pacer.ticket();
        const sent = hearer.now();
        const outcome = await hearer.send(part);
        const count = (tries.get(part.index) ?? 0) + 1;
        // A server never reached is not asked again and again: only one that answered before is.
        const reachable = engine !== null || heard.size > 0 || ("failure" in outcome && outcome.failure.kind !== "network");
        if ("heard" in outcome) {
            pacer.heard(ticket, hearer.now() - sent, (part.sendTo - part.sendFrom) / 1000);
            heard.set(part.index, outcome.heard);
            if (request.fingerprint) {
                await hearer.store.put(partKey(request, part), { midi: outcome.heard.midi, engine: outcome.heard.engine, at: Date.now() });
            }
        } else if ("retry" in outcome && count < MOST_TRIES && reachable) {
            pacer.failed(ticket);
            tries.set(part.index, count);
            retrying++;
            hearer.progress(heard.size, total, true);
            await hearer.wait(outcome.wait ?? backoff(count));
            retrying--;
            waiting.unshift(part);
        } else {
            failure ??= outcome.failure;
        }
        hearer.progress(heard.size, total, retrying > 0);
    };

    while (failure === null && (waiting.length > 0 || running.size > 0)) {
        while (failure === null && waiting.length > 0 && running.size < pacer.allowed) {
            const job: Promise<void> = one(waiting.shift()!).finally(() => running.delete(job));
            running.add(job);
        }
        if (running.size > 0) {
            await Promise.race(running);
        }
    }
    await Promise.allSettled([...running]);
    return { parts: [...heard.values()].sort((a, b) => a.index - b.index), failure };
}

/** The .NET side: the request headers for the scan server (null when signed out), and where progress goes. */
interface Page {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** How long a request may take: the hosted server's gateway gives up after four minutes, a player's own server may be slow. */
const REQUEST_MILLISECONDS = 10 * 60 * 1000;

function base64(bytes: Uint8Array): string {
    let binary = "";
    for (let i = 0; i < bytes.length; i += 0x8000) {
        binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    }
    return btoa(binary);
}

/** A signal that aborts after some milliseconds. */
function timeout(milliseconds: number): AbortSignal {
    if (typeof AbortSignal.timeout === "function") {
        return AbortSignal.timeout(milliseconds);
    }
    const controller = new AbortController();
    setTimeout(() => controller.abort(), milliseconds);
    return controller.signal;
}

/** The "message" of a JSON answer, or null. */
async function messageOf(response: Response): Promise<string | null> {
    try {
        const body: unknown = await response.json();
        return body && typeof body === "object" && "message" in body && typeof body.message === "string" ? body.message : null;
    } catch {
        return null;
    }
}

/** Which engine the server hears with, as its answers name it ("transkun 2.0.1"); null when it does not answer. */
async function engineAt(address: string): Promise<string | null> {
    try {
        const response = await fetch(new URL("health", address), { signal: timeout(30_000) });
        const body: unknown = response.ok ? await response.json() : null;
        const transcribe = body && typeof body === "object" && "transcribe" in body ? (body.transcribe as { engine?: unknown; version?: unknown }) : null;
        return transcribe && typeof transcribe.engine === "string" ? `${transcribe.engine} ${String(transcribe.version ?? "")}`.trim() : null;
    } catch {
        return null;
    }
}

/** Sends one part's sound to be heard. */
async function sendPart(request: HearRequest, part: PartToHear, page: Page): Promise<Outcome> {
    const sound = soundBetween(request.start + part.sendFrom, request.start + part.sendTo);
    const headers = (await page.invokeMethodAsync("HeadersAsync")) as Record<string, string> | null;
    if (!sound || !headers) {
        return { failure: { status: sound ? 401 : 0, kind: "http", message: null } };
    }
    const form = new FormData();
    form.append("file", new Blob([sound.wav as BlobPart], { type: "application/octet-stream" }), "recording.wav");
    let response: Response;
    try {
        response = await fetch(new URL("transcribe", request.address), { method: "POST", body: form, headers, signal: timeout(REQUEST_MILLISECONDS) });
    } catch (error) {
        const timedOut = error instanceof DOMException && (error.name === "TimeoutError" || error.name === "AbortError");
        return { retry: true, failure: { status: 0, kind: timedOut ? "timeout" : "network", message: null } };
    }
    if (response.ok) {
        const midi = base64(new Uint8Array(await response.arrayBuffer()));
        return { heard: { index: part.index, midi, engine: response.headers.get("X-Engine") ?? "unknown engine", message: null } };
    }
    const failure: Failure = { status: response.status, kind: "http", message: await messageOf(response) };
    if (response.status === 422) {
        return { heard: { index: part.index, midi: null, engine: "", message: failure.message } };
    }
    const after = Number(response.headers.get("Retry-After"));
    return [429, 502, 503, 504].includes(response.status)
        ? { retry: true, failure, wait: Number.isFinite(after) && after > 0 ? Math.min(120, after) * 1000 : undefined }
        : { failure };
}

/** Hears a stretch of the held recording on the scan server: the browser's own fetch, IndexedDB and the .NET page. */
export function hear(request: HearRequest, page: Page): Promise<Hearing> {
    return hearParts(request, {
        engine: () => engineAt(request.address),
        send: part => sendPart(request, part, page),
        store: browserParts,
        progress: (done, total, retrying) => {
            page.invokeMethodAsync("Progress", done, total, retrying).catch(() => undefined);
        },
        wait: milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)),
        now: () => Date.now(),
    });
}

/** Forgets the parts of a hearing once music-core has joined them. */
export function forgetHeard(request: HearRequest): Promise<void> {
    return browserParts.remove(keyPrefix(request));
}
