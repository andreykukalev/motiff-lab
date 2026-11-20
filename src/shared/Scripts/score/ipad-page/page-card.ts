// Phrase focus on the iPad's score page (0018 section 4): the selection lifted into a card across the screen with the
// page dimmed behind it (`focus`, `card`). Swift engraves the phrase's bars with Verovio at the card's width, with the web
// app's own card layout (Scripts/score/phrase-card.ts), and the page shows the SVG. A tap on the dimmed page goes back to
// it (`leave`). A variation being tried is engraved above the phrase as an ossia, in the kit's dashed plum frame named by
// an overline, and its added notes are plum. In Expand the card has the page's black playhead too, dragged inside the
// passage (v0.6.4, spec 8: `drawCardHeads`), and a tap on a bar of the card puts it on the beat tapped (`place`). The
// phrases' marks stand over its bars as over the pages (page-phrases.ts), only to read.
import { cardLayout } from "../phrase-card.js";
import { ScoreGeometry, readPage, staffFrames, type PageGeometry, type Playhead } from "../score-geometry.js";
import { ScoreTimeline } from "../score-timeline.js";
import { ScoreBeats } from "../selection-beats.js";
import { beatUnder } from "../score-modes.js";
import { ScorePlayheadMarks } from "../playheads.js";
import { ScoreGrab } from "../playhead-grab.js";
import { viewOf } from "../../playback/playback-model.js";
import { PlayheadDrag } from "../../playback/playhead-drag.js";
import type { PageCommands, PageOssia, PagePoint, TickRange } from "../page-contract.js";
import { post, state } from "./page-state.js";
import { tint } from "./page-marks.js";
import { drawHeads, grabs, press, reportPlace } from "./page-playheads.js";
import { forgetSounding, heard } from "./page-sounding.js";
import { placeCardPhrases } from "./page-phrases.js";

/** Only a change of the card's width beyond this engraves it again, as in the web app. */
const RELAYOUT_THRESHOLD_PX = 24;

/** Room round the ossia's staves inside its frame, in pixels, as on the web. */
const FRAME_PAD_X = 10;
const FRAME_PAD_Y = 8;

const focusLayer = document.createElement("div");
focusLayer.id = "focus";
focusLayer.hidden = true;
focusLayer.innerHTML = '<button type="button" class="veil" tabindex="-1"></button><div class="stage"><section class="card"><div class="card-score"></div></section></div>';
const veil = focusLayer.querySelector(".veil") as HTMLElement;
const cardSection = focusLayer.querySelector(".card") as HTMLElement;
export const cardScore = focusLayer.querySelector(".card-score") as HTMLElement;

/** The phrase on the card: its timeline, where its first bar is in the piece, its width, ossias, pages and bars. */
export const card: { timeline: ScoreTimeline | null; offset: number; width: number; ossias: PageOssia[]; pages: PageGeometry[]; geometry: ScoreGeometry | null } = { timeline: null, offset: 0, width: 0, ossias: [], pages: [], geometry: null };

/**
 * The black playhead on the card in Expand (v0.6.4, spec 8): the page's track under the system with its dot
 * (Scripts/score/playheads.ts), at the position held inside the passage, Expand's selection that always loops (Swift's
 * ExpandRules.held: the playback model's place kept to the passage), so where the music is while it is inside and at the
 * passage's start while it is not. Its dot is dragged by the Pencil or a finger as on the page (playhead-grab.ts), inside the
 * passage on the sixteenth grid, the notes passed sounding; Swift keeps it inside (Player.handle(_:within:)). The card's
 * ticks count from its first bar, `card.offset` in the piece.
 */
export const cardHeads = new ScorePlayheadMarks(cardScore, {
    at: (tick) => cardPlayhead(tick),
    lineBottom: (bar) => card.geometry?.lineInkBottom(bar.system) ?? bar.bottom,
    grab: (_handle, event) => cardGrabs.begin("blue", event),
});

export const cardGrabs = new ScoreGrab({
    geometry: () => card.geometry,
    onsetOf: (id) => card.timeline?.onsetOf(id),
    selection: () => cardPassage(),
    // Nothing writes in Expand: the Pencil and a finger both take the dot.
    takes: () => state.focus && state.heads !== null,
    point: (event) => cardPoint(event),
    moved: () => drawCardHeads(),
}, new PlayheadDrag((message) => post({ drag: "tick" in message ? { ...message, tick: message.tick + card.offset } : message })));

/** The piece's beats in the card's ticks, for a tap on the card to find the beat under it as a tap on the page does (page-taps.ts). */
const cardBeats = new ScoreBeats({ beats: () => state.beats?.map((line) => line - card.offset) ?? null, tickX: (tick) => cardPlayhead(tick)?.x });

/** Adds the focus layer to the page: a tap on the veil leaves focus; the card's lift ending lays its bars out again. */
export function installCard(): void {
    document.body.append(focusLayer);
    veil.addEventListener("click", () => post({ leave: true }));
    /**
     * The card's lift has ended (score.css `card-lift`, the kit's slow time): its bars are read again where they now stand
     * (#729). Bars that came during the lift were read while the card was still scaled, so the dot, a tap's beat and the
     * ossias' frames were off by up to 4 % until a resize; only the card's own animation counts, not one inside it.
     */
    cardSection.addEventListener("animationend", (event) => {
        if (event.target !== event.currentTarget) {
            return;
        }
        placeOssias();
        layoutCard();
        drawCardHeads();
    });
}

/** Where a tick of the card sounds on it, across its bar and its system's staves. */
function cardPlayhead(tick: number): Playhead | undefined {
    const timeline = card.timeline;
    return timeline === null ? undefined : card.geometry?.playhead(tick, (id) => timeline.onsetOf(id));
}

/** A pointer's place in the card's own pixels (it scrolls inside itself), as `layoutCard` places its bars. */
function cardPoint(event: MouseEvent): PagePoint {
    const view = cardScore.getBoundingClientRect();
    return { x: event.clientX - view.left + cardScore.scrollLeft, y: event.clientY - view.top + cardScore.scrollTop };
}

/**
 * A tap on a bar of the card in Expand (finding 2 of the 0.8 test, Andrei's answer 4 of 2026-11-18): the black playhead goes
 * to the beat tapped by the page's own rule for a tap in Navigate (page-taps.ts `tapUnderModes`, PLAY-05); Swift keeps it
 * inside the passage and starts nothing, and playing it plays on from there (ScoreWorkspace.placeTapped). Nothing writes in
 * Expand, so the Pencil and a finger tap alike, as both take the dot (`cardGrabs`); a tap on the dot or beside the bars does
 * nothing.
 */
export function cardTapped(event: MouseEvent): void {
    const geometry = card.geometry;
    if (geometry === null || state.heads === null || (event.target as Element).closest(".grip") !== null) {
        return;
    }
    const point = cardPoint(event);
    const beat = beatUnder(geometry, cardBeats, point.x, point.y);
    if (beat !== undefined) {
        post({ place: { start: beat.start + card.offset, end: beat.end + card.offset } });
    }
}

/** Expand's passage in the card's ticks: the selection it was opened on, as Swift's playheads give it; null without one. */
function cardPassage(): TickRange | null {
    const chosen = state.heads?.selection ?? null;
    return chosen === null ? null : { start: chosen.start - card.offset, end: chosen.end - card.offset };
}

/** Where a page of the card is drawn, in the card's own pixels: its view box fitted into its SVG and centred. */
function placementOf(svg: Element, page: PageGeometry, view: DOMRect): { left: number; top: number; scale: number } {
    const box = svg.getBoundingClientRect();
    const scale = Math.min(box.width / page.width, box.height / page.height);
    return {
        left: box.left - view.left + cardScore.scrollLeft + (box.width - page.width * scale) / 2,
        top: box.top - view.top + cardScore.scrollTop + (box.height - page.height * scale) / 2,
        scale,
    };
}

/**
 * Where every bar of the card sits, in the card's own pixels (it scrolls inside itself), as `layout` does for the pages;
 * the phrases' marks drawn over them again.
 */
export function layoutCard(): void {
    const timeline = card.timeline;
    if (!state.focus || timeline === null) {
        card.geometry = null;
        placeCardPhrases(null);
        return;
    }
    const view = cardScore.getBoundingClientRect();
    const placements: { left: number; top: number; scale: number }[] = [];
    cardScore.querySelectorAll(":scope > .card-page > svg").forEach((svg, index) => {
        const page = card.pages[index];
        if (page !== undefined) {
            placements.push(placementOf(svg, page, view));
        }
    });
    card.geometry = new ScoreGeometry(card.pages, placements, (id) => timeline.measureOnset(id));
    placeCardPhrases({
        root: cardScore,
        geometry: card.geometry,
        offset: card.offset,
        end: timeline.spans.reduce((end, span) => Math.max(end, span.end), 0),
        onsetOf: (id) => timeline.onsetOf(id),
        local: (box) => {
            const at = cardScore.getBoundingClientRect();
            const [x, y] = [cardScore.scrollLeft - at.left, cardScore.scrollTop - at.top];
            return { left: box.left + x, top: box.top + y, right: box.right + x, bottom: box.bottom + y };
        },
        system: (bar) => cardScore.querySelector(`[id="${CSS.escape(bar)}"]`)?.closest("g.system") ?? null,
        targets: false,
    });
}

/** Draws the card's black playhead (Expand under the playback model), or hides it. */
export function drawCardHeads(): void {
    if (!state.focus || state.heads === null || card.geometry === null) {
        cardHeads.hide();
        return;
    }
    const tick = cardGrabs.drag.heard(heard.tick - card.offset);
    const view = viewOf({ selection: cardPassage(), loop: true, tick, transport: heard.playing ? "playing" : "stopped" });
    cardHeads.place({ global: view.blue ?? view.black, selection: null, globalPlays: view.playing, held: cardGrabs.held !== null });
}

/**
 * Lifts the selection into focus, or puts it back: the page dimmed behind an empty card, which `card` fills. Returns
 * Verovio's options for a card as wide as this one, for Swift to engrave the phrase with.
 */
export function focus({ open, label, back }: Parameters<PageCommands["focus"]>[0]): ReturnType<PageCommands["focus"]> {
    state.focus = Boolean(open);
    focusLayer.hidden = !state.focus;
    if (state.focus) {
        grabs.cancel();
        press.cancel();
    }
    // The page's playheads go under the veil and come back out of it (drawHeads), the card's go with it.
    if (state.heads !== null) {
        reportPlace(drawHeads(), !heard.playing);
    }
    if (!state.focus) {
        cardGrabs.cancel();
        cardHeads.hide();
        cardScore.replaceChildren();
        card.timeline = null;
        card.geometry = null;
        card.ossias = [];
        placeCardPhrases(null);
        return {};
    }
    veil.setAttribute("aria-label", back ?? "");
    cardSection.setAttribute("aria-label", label ?? "");
    card.width = cardScore.clientWidth;
    return { options: cardLayout(card.width) };
}

/**
 * The phrase engraved on its own: its pages as SVG and Verovio's timemap, which starts at 0 at the start of the bar
 * that holds `start`. Returns how many notes it engraved.
 */
export function showCard({ pages, timemap, start, ossias }: Parameters<PageCommands["card"]>[0]): number {
    // A drag on the card engraved before lets go first: its dot goes with the old pages.
    cardGrabs.cancel();
    forgetSounding("card");
    cardScore.innerHTML = (pages ?? []).map((svg) => `<div class="card-page">${svg}</div>`).join("");
    card.timeline = ScoreTimeline.fromTimemap(timemap ?? []);
    card.offset = state.geometry?.barAt(start)?.tick ?? start;
    card.pages = (pages ?? []).map((svg, index) => readPage(svg, index + 1));
    card.ossias = ossias ?? [];
    placeOssias();
    tint(cardScore, card.timeline, new Set(card.ossias.flatMap((ossia) => ossia.added ?? [])), "version-added");
    layoutCard();
    drawCardHeads();
    return card.timeline.spans.length;
}

/** Each ossia's frame round its staves on every system of the card, its name over the first (phrase-card.ts placeFrames). */
export function placeOssias(): void {
    for (const old of cardScore.querySelectorAll(":scope > .ossia-frame")) {
        old.remove();
    }
    const view = cardScore.getBoundingClientRect();
    const svgs = [...cardScore.querySelectorAll(":scope > .card-page > svg")];
    for (const ossia of card.ossias) {
        let first = true;
        svgs.forEach((svg, index) => {
            const page = card.pages[index];
            if (page === undefined) {
                return;
            }
            const { left, top, scale } = placementOf(svg, page, view);
            for (const frame of staffFrames(page, ossia.staves)) {
                const element = document.createElement("div");
                element.className = "ossia-frame";
                element.setAttribute("aria-hidden", "true");
                element.style.left = `${left + frame.left * scale - FRAME_PAD_X}px`;
                element.style.top = `${top + frame.top * scale - FRAME_PAD_Y}px`;
                element.style.width = `${(frame.right - frame.left) * scale + FRAME_PAD_X * 2}px`;
                element.style.height = `${(frame.bottom - frame.top) * scale + FRAME_PAD_Y * 2}px`;
                if (first && ossia.label) {
                    const name = document.createElement("span");
                    name.className = "overline";
                    name.textContent = ossia.label;
                    element.append(name);
                }
                cardScore.append(element);
                first = false;
            }
        });
    }
}

/** In focus, a new width for the card goes to Swift, which engraves it again at that width. */
export function reportCardWidth(): void {
    if (!state.focus || Math.abs(cardScore.clientWidth - card.width) < RELAYOUT_THRESHOLD_PX) {
        return;
    }
    card.width = cardScore.clientWidth;
    post({ cardLayout: cardLayout(card.width) });
}
