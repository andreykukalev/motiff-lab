// What sounds, on the iPad's score page: while the piece plays, the notes sounding turn cobalt on the page and on the card
// in focus (`playing`), and the page follows unless the player turned that off: the system playing is kept below the bar,
// so the next one shows beneath it (0018 section 7). A scroll by hand, or a stroke of the pen, wins for a few seconds, as
// on the web, and the next Play follows again.
import type { PageCommands } from "../page-contract.js";
import { READING_LINE_PX, state } from "./page-state.js";
import { drawHeads, placePlayhead, reportPlace } from "./page-playheads.js";
import { card, cardScore, drawCardHeads } from "./page-card.js";

/** After the player scrolls by hand, playback leaves the page where they put it for this long before following again (score-view.ts). */
const FOLLOW_PAUSE_MS = 4000;

/** The notes sounding now, marked on the page or on the card, and when they last changed. */
const sounding = { page: new Set<string>(), card: new Set<string>(), key: "", byHand: -Infinity };

/** Where the player is, as Swift last said (`playing`): what the playheads are drawn at when no drag shows another place. */
export const heard = { tick: 0, playing: false };

/** Whether the piece has played, so a pause leaves the playhead resting where it stopped (testing item 28). */
let played = false;

/** New pages, or a new card, have nothing marked as sounding. */
export function forgetSounding(where: "page" | "card"): void {
    sounding[where] = new Set();
    sounding.key = "";
}

/** The page follows the playing again at once, as after a new Play (Back to playback). */
export function followAgain(): void {
    sounding.byHand = -Infinity;
}

/** Listens for the player's own scroll: a touch or the wheel moving the page, after which playback leaves the page alone for a while. */
export function listenHandScroll(): void {
    for (const name of ["touchmove", "wheel"]) {
        window.addEventListener(name, () => { sounding.byHand = performance.now(); }, { passive: true });
    }
}

/**
 * A stroke of the pen began (`on`) or ended (#521 10): while it is written playback leaves the page where it is, so the
 * music never moves under the pen, and after it for as long as after a scroll by hand.
 */
export function writing({ on }: Parameters<PageCommands["writing"]>[0]): void {
    sounding.byHand = on ? Infinity : performance.now();
}

/**
 * Marks the notes sounding at `tick` on the page, or on the card in focus, as the web app's score view does; while
 * `playing`, the page keeps the system with them below the bar. Called about thirty times a second from Swift with the
 * player's position; `follow` true (a new Play) follows again at once after a scroll by hand. Returns the top the page
 * scrolls to when it follows, or null.
 */
export function playing({ tick, playing, follow, auto = true }: Parameters<PageCommands["playing"]>[0]): number | null {
    if (follow) {
        followAgain();
    }
    played ||= playing;
    heard.tick = tick;
    heard.playing = playing;
    if (state.heads !== null) {
        reportPlace(drawHeads(), !playing);
        drawCardHeads();
    } else {
        // Stopped, the playhead stays where playing stopped, in graphite, so the place is not lost (testing item 28).
        const resting = !playing && played && tick > 0;
        placePlayhead((playing || resting) && !state.focus ? tick : null, !playing);
    }
    const onPage = playing && state.timeline !== null ? state.timeline.soundingAt(tick) : [];
    const onCard = playing && state.focus && card.timeline ? card.timeline.soundingAt(tick - (card.offset ?? 0)) : [];
    const key = `${onPage.join(" ")}|${onCard.join(" ")}`;
    if (key === sounding.key) {
        return null;
    }
    sounding.key = key;
    sounding.page = mark(sounding.page, onPage, (id) => document.querySelector(`#pages [id="${CSS.escape(id)}"]`));
    sounding.card = mark(sounding.card, onCard, (id) => cardScore.querySelector(`[id="${CSS.escape(id)}"]`));
    if (playing && auto && !state.focus && onPage.length > 0 && performance.now() - sounding.byHand >= FOLLOW_PAUSE_MS) {
        return keepInView(document.querySelector("#pages .playing"));
    }
    return null;
}

/** The ids marked now: those no longer sounding lose the class, the new ones take it. */
function mark(before: ReadonlySet<string>, ids: readonly string[], find: (id: string) => Element | null): Set<string> {
    const next = new Set(ids);
    for (const id of before) {
        if (!next.has(id)) {
            find(id)?.classList.remove("playing");
        }
    }
    for (const id of next) {
        if (!before.has(id)) {
            find(id)?.classList.add("playing");
        }
    }
    return next;
}

/** Keeps the system (line of staves) with the sounding notes in view, below the reading line under the bar; the top scrolled to, or null. */
function keepInView(note: Element | null): number | null {
    const target = note?.closest("g.system") ?? note;
    if (!target) {
        return null;
    }
    const box = target.getBoundingClientRect();
    if (box.top >= READING_LINE_PX - 16 && box.bottom <= window.innerHeight) {
        return null;
    }
    const top = Math.max(0, window.scrollY + box.top - READING_LINE_PX);
    window.scrollTo({ top, behavior: "smooth" });
    return top;
}
