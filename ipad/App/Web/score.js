// The score in the iPad app (decision record 0019 section 1.4). The pages are Verovio's SVG; where each bar sits is
// read by the web app's own score geometry, and pencil marks are anchored to bars and drawn exactly as the web app
// does (Scripts/score/pencil-marks.ts, compiled into the app as score/). PencilKit draws the live stroke in Swift; when
// it ends, Swift hands its points here to be kept against its bar (`anchor`), saves the stroke in the project file
// through music-core (`ink.add`), and gives the marks back to be drawn (`draw`).
//
// The score is also the selection surface (0018 section 3): the phrases are drawn as pencil brackets above the top
// staff (`phrases`), and the web app's own selection layer (Scripts/score/selection-layer.ts) draws the band and its
// handles and turns a long press and a drag across bars into a choice of bars. What the player does on the score goes
// to Swift as a message: a bracket, a note or a bar tapped, bars chosen, and where the selection menu goes.
//
// Phrase focus (0018 section 4) lifts the selection into a card across the screen with the page dimmed behind it
// (`focus`, `card`): Swift engraves the phrase's bars with Verovio at the card's width, with the web app's own card
// layout (Scripts/score/phrase-card.ts), and the page shows the SVG. A tap on the dimmed page goes back to it.
//
// A fact chosen in the Insights stream (0018 section 6) rings its places on the score (`highlight`), as the web app's
// score view does: the notes turn cobalt with a box round each notehead, and the first comes into view.
import { cardLayout } from "../score/phrase-card.js";
import { ScoreGeometry, readPage } from "../score/score-geometry.js";
import { SelectionLayer } from "../score/selection-layer.js";
import { ScoreTimeline, highlightedIds } from "../score/score-timeline.js";
import { HIGHLIGHTER_SIZE, PEN_SIZE, anchorMark, framesByTick, outlinePath, placeMark, touches } from "../score/pencil-marks.js";

const SVG = "http://www.w3.org/2000/svg";

/** The eraser takes marks within this many pixels of its path, as in the web app. */
const ERASER_REACH = 10;

/** The reading line, this far below the top of the view: the pages it crosses are the ones the bar names, as on the web. */
const READING_LINE_PX = 72;

const state = { pages: [], timeline: null, geometry: null, frames: [], byTick: new Map(), marks: [], phrases: [], pen: false, focus: false, ringed: new Set() };
let nextId = 0;

/** Shows the pages and reads where their bars are; `timemap` is Verovio's, with measures and rests. */
function show({ pages, timemap, dark }) {
    document.documentElement.toggleAttribute("data-theme", false);
    if (dark) {
        document.documentElement.setAttribute("data-theme", "dark");
    }
    reported = "";
    state.ringed = new Set();
    const host = document.getElementById("pages");
    host.innerHTML = pages.map((svg, index) => `<div class="page" data-page="${index + 1}">${svg}</div>`).join("");
    state.pages = pages.map((svg, index) => readPage(svg, index + 1));
    state.timeline = ScoreTimeline.fromTimemap(timemap);
    return layout();
}

/** Where every bar sits now, in the document's pixels: after showing the pages and whenever the width changes. */
function layout() {
    const placements = [];
    document.querySelectorAll("#pages > .page > svg").forEach((svg, index) => {
        const page = state.pages[index];
        if (page === undefined) {
            return;
        }
        // The page's view box is fitted into the element and centred, as preserveAspectRatio has it.
        const box = svg.getBoundingClientRect();
        const scale = Math.min(box.width / page.width, box.height / page.height);
        placements.push({
            left: box.left + window.scrollX + (box.width - page.width * scale) / 2,
            top: box.top + window.scrollY + (box.height - page.height * scale) / 2,
            scale,
        });
    });
    const timeline = state.timeline;
    const geometry = new ScoreGeometry(state.pages, placements, (id) => timeline?.measureOnset(id));
    state.geometry = geometry;
    state.frames = geometry.barFrames();
    state.byTick = framesByTick(state.frames);
    redraw();
    placePhrases();
    placeRings();
    selection.place();
    reportPages();
    return { bars: state.frames.length, space: state.frames[0]?.space ?? 0 };
}

/**
 * The pages under the reading line: { first, last, count }, "p. 2 of 4" or, with two to a row, "pp. 1–2 of 4". Between
 * two rows the next row down is the one being read.
 */
function inView() {
    const pages = [...document.querySelectorAll("#pages > .page")];
    if (pages.length === 0) {
        return { first: 0, last: 0, count: 0 };
    }
    let shown = pages.filter((page) => {
        const box = page.getBoundingClientRect();
        return box.top <= READING_LINE_PX && box.bottom > READING_LINE_PX;
    });
    if (shown.length === 0) {
        const next = pages.find((page) => page.getBoundingClientRect().top > READING_LINE_PX) ?? pages[pages.length - 1];
        const top = next.getBoundingClientRect().top;
        shown = pages.filter((page) => Math.abs(page.getBoundingClientRect().top - top) < 2);
    }
    const numbers = shown.map((page) => Number(page.dataset.page));
    return { first: Math.min(...numbers), last: Math.max(...numbers), count: pages.length };
}

let reported = "";

/** Tells Swift which pages the bar names, when they change. */
function reportPages() {
    const pages = inView();
    const key = `${pages.first}-${pages.last}-${pages.count}`;
    if (key !== reported) {
        reported = key;
        window.webkit?.messageHandlers?.score?.postMessage({ pages });
    }
}

let scrolling = 0;
window.addEventListener("scroll", () => {
    cancelAnimationFrame(scrolling);
    scrolling = requestAnimationFrame(() => {
        reportPages();
        selection.placeMenu();
    });
}, { passive: true });

function post(message) {
    window.webkit?.messageHandlers?.score?.postMessage(message);
}

/**
 * The phrases at the level shown, as pencil brackets above the top staff, numbered at the left end; the selected one
 * in the accent. Each phrase is { start, end, number, kind, current, label }.
 */
function phrases({ units }) {
    state.phrases = units ?? [];
    placePhrases();
    return state.phrases.length;
}

function placePhrases() {
    for (const old of document.querySelectorAll("body > .bracket")) {
        old.remove();
    }
    const geometry = state.geometry;
    if (geometry === null) {
        return;
    }
    for (const phrase of state.phrases) {
        for (const run of geometry.bracket(phrase.start, phrase.end)) {
            const bracket = document.createElement("button");
            bracket.type = "button";
            bracket.className = `bracket ${phrase.kind}`;
            bracket.classList.toggle("on", phrase.current);
            bracket.classList.toggle("from", !run.first);
            bracket.classList.toggle("on-to", !run.last);
            bracket.dataset.phrase = String(phrase.number);
            bracket.style.left = `${run.left}px`;
            bracket.style.top = `${run.top}px`;
            bracket.style.width = `${Math.max(4, run.right - run.left)}px`;
            if (run.first) {
                bracket.setAttribute("aria-label", phrase.label);
                bracket.setAttribute("aria-pressed", phrase.current ? "true" : "false");
                const number = document.createElement("span");
                number.className = "n";
                number.setAttribute("aria-hidden", "true");
                number.textContent = String(phrase.number);
                bracket.append(number);
            } else {
                // The rest of a phrase that runs on to the next system is the same target, said once.
                bracket.tabIndex = -1;
                bracket.setAttribute("aria-hidden", "true");
            }
            document.body.append(bracket);
        }
    }
}

/**
 * The menu's stand-in: an invisible box the size of Swift's menu, which the selection layer places over the band as
 * it places the web app's menu. Its place goes to Swift, which draws the menu there.
 */
const menu = document.createElement("div");
menu.id = "menu-place";
menu.setAttribute("aria-hidden", "true");
menu.hidden = true;
document.body.append(menu);
let menuReported = "";

function reportMenu() {
    const place = menu.hidden ? null : { left: parseFloat(menu.style.left) || 0, top: parseFloat(menu.style.top) || 0, below: menu.dataset.below === "true" };
    const key = JSON.stringify(place);
    if (key !== menuReported) {
        menuReported = key;
        post({ menu: place });
    }
}

const selection = new SelectionLayer(document.body, {
    geometry: () => state.geometry,
    // While the Pen is on a finger writes, and in focus the page under the veil takes no choice of bars.
    penOn: () => state.pen || state.focus,
    chosen: (first, last) => {
        const end = state.geometry?.barAt(last)?.end;
        if (end !== undefined) {
            post({ chosen: { start: first, end } });
        }
    },
});
const placeMenu = selection.placeMenu.bind(selection);
selection.placeMenu = () => {
    placeMenu();
    reportMenu();
};
selection.setMenu(menu);

/** The selection's span in ticks, with what a screen reader says for each handle; null for none. */
function select(chosen) {
    selection.setSelection(chosen ?? null);
    return chosen ? document.querySelectorAll("body > .sel-band").length : 0;
}

/** The size of Swift's selection menu, for the selection layer to place it. */
function menuSize({ width, height }) {
    menu.style.width = `${width}px`;
    menu.style.height = `${height}px`;
    selection.placeMenu();
}

/** While the bar's Pen is on, a finger writes: it neither selects nor taps. */
function pen(on) {
    state.pen = Boolean(on);
}

/** A tap on the score: a bracket selects its phrase, a note plays from it, a bar away from its notes selects that bar. */
document.body.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    if (target?.closest("#focus")) {
        return;
    }
    const bracket = target?.closest(".bracket");
    if (bracket) {
        post({ bracket: Number(bracket.dataset.phrase) });
        return;
    }
    if (selection.swallowsClick() || state.pen || target === null || target.closest(".sel-handle")) {
        return;
    }
    const timeline = state.timeline;
    const hit = target.closest("g.note, g.chord");
    const note = hit?.matches("g.chord") ? hit.querySelector("g.note") : hit;
    const onset = note?.id ? timeline?.onsetOf(note.id) : undefined;
    if (onset !== undefined) {
        post({ note: onset });
        return;
    }
    const measure = target.closest("g.measure");
    const start = measure?.id ? timeline?.measureOnset(measure.id) : undefined;
    const bar = start === undefined ? undefined : state.geometry?.barAt(start);
    post(bar ? { bar: { start: bar.tick, end: bar.end } } : { blank: true });
});

/**
 * Keeps a stroke, its points in the document's pixels, against the bar it starts in; an empty object when there is
 * no bar (WebKit's async calls do not take a null back).
 */
function anchor({ points, tool, colour }) {
    nextId += 1;
    const id = `m${Date.now().toString(36)}${nextId.toString(36)}`;
    const size = tool === "highlighter" ? HIGHLIGHTER_SIZE : PEN_SIZE;
    return anchorMark(state.frames, points, { id, tool, colour: tool === "highlighter" ? "amber" : colour, size }) ?? {};
}

/** The marks an eraser path, in the document's pixels, passes over: their ids. */
function erase({ points }) {
    const ids = [];
    for (const mark of state.marks) {
        const place = placeMark(mark, state.byTick);
        if (place !== null && points.some((point) => touches(place.points, place.radius, point.x, point.y, ERASER_REACH))) {
            ids.push(mark.id);
        }
    }
    return ids;
}

/** Draws the piece's marks, as music-core answers them (`marks.ink`). */
function draw({ marks }) {
    state.marks = marks ?? [];
    redraw();
    return state.marks.length;
}

function redraw() {
    const ink = document.getElementById("ink");
    ink.setAttribute("width", document.documentElement.scrollWidth);
    ink.setAttribute("height", document.documentElement.scrollHeight);
    ink.replaceChildren();
    for (const mark of state.marks) {
        const place = placeMark(mark, state.byTick);
        if (place === null) {
            continue;
        }
        const path = document.createElementNS(SVG, "path");
        path.setAttribute("class", `mark ${mark.tool} ${mark.colour}`);
        path.setAttribute("d", outlinePath(place.points, place.radius, mark.tool === "pen"));
        path.dataset.id = mark.id;
        ink.append(path);
    }
}

let resizing = 0;
window.addEventListener("resize", () => {
    cancelAnimationFrame(resizing);
    resizing = requestAnimationFrame(() => {
        layout();
        reportCardWidth();
    });
});

/** Only a change of the card's width beyond this engraves it again, as in the web app. */
const RELAYOUT_THRESHOLD_PX = 24;

const focusLayer = document.createElement("div");
focusLayer.id = "focus";
focusLayer.hidden = true;
focusLayer.innerHTML = '<button type="button" class="veil" tabindex="-1"></button><div class="stage"><section class="card"><div class="card-score"></div></section></div>';
document.body.append(focusLayer);
const cardScore = focusLayer.querySelector(".card-score");
const card = { timeline: null, offset: 0, width: 0 };

focusLayer.querySelector(".veil").addEventListener("click", () => post({ leave: true }));
cardScore.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    const hit = target?.closest("g.note, g.chord");
    const note = hit?.matches("g.chord") ? hit.querySelector("g.note") : hit;
    const onset = note?.id ? card.timeline?.onsetOf(note.id) : undefined;
    if (onset !== undefined) {
        post({ cardNote: onset + card.offset });
    }
});

/**
 * Lifts the selection into focus, or puts it back: the page dimmed behind an empty card, which `card` fills. Returns
 * Verovio's options for a card as wide as this one, for Swift to engrave the phrase with.
 */
function focus({ open, label, back }) {
    state.focus = Boolean(open);
    focusLayer.hidden = !state.focus;
    if (!state.focus) {
        cardScore.replaceChildren();
        card.timeline = null;
        return {};
    }
    focusLayer.querySelector(".veil").setAttribute("aria-label", back ?? "");
    focusLayer.querySelector(".card").setAttribute("aria-label", label ?? "");
    card.width = cardScore.clientWidth;
    return { options: cardLayout(card.width) };
}

/**
 * The phrase engraved on its own: its pages as SVG and Verovio's timemap, which starts at 0 at the start of the bar
 * that holds `start`. Returns how many notes it engraved.
 */
function showCard({ pages, timemap, start }) {
    cardScore.innerHTML = (pages ?? []).map((svg) => `<div class="card-page">${svg}</div>`).join("");
    card.timeline = ScoreTimeline.fromTimemap(timemap ?? []);
    card.offset = state.geometry?.barAt(start)?.tick ?? start;
    return card.timeline.spans.length;
}

/** In focus, a new width for the card goes to Swift, which engraves it again at that width. */
function reportCardWidth() {
    if (!state.focus || Math.abs(cardScore.clientWidth - card.width) < RELAYOUT_THRESHOLD_PX) {
        return;
    }
    card.width = cardScore.clientWidth;
    post({ cardLayout: cardLayout(card.width) });
}

/** A passage with the bar before it and the bar after it, within the piece: what Lead-in plays (0018 section 4). */
function lead({ start, end }) {
    const geometry = state.geometry;
    const before = start > 0 ? geometry?.barAt(start - 1)?.tick : undefined;
    const after = geometry?.barAt(end)?.end;
    return { start: before ?? start, end: after ?? end };
}

/**
 * The places of a fact chosen in the Insights stream, each { onset, end, noteIds }, as music-core gives them: their
 * engraved notes turn cobalt with a box round each notehead, and with `scroll` the first is brought into view. A place
 * without notes, such as a phrase or a silence, rings every note and rest in its stretch of time. Engraved ids are
 * music-core's, a tied note's later pieces with "-2", "-3", … (GeneratedNotation). Empty places clear the rings.
 * Returns how many notes are ringed.
 */
function highlight({ places, scroll }) {
    for (const id of state.ringed) {
        document.getElementById(id)?.classList.remove("highlight");
    }
    const timeline = state.timeline;
    const chosen = places ?? [];
    const ringed = new Set();
    if (timeline !== null && chosen.length > 0) {
        const ids = new Set(chosen.flatMap((place) => place.noteIds ?? []));
        const stretches = chosen.filter((place) => (place.noteIds ?? []).length === 0).map((place) => ({ start: place.onset, end: place.end, notes: [] }));
        for (const id of highlightedIds(timeline, stretches, () => undefined)) {
            ringed.add(id);
        }
        for (const span of timeline.spans) {
            if (ids.has(span.id) || ids.has(span.id.replace(/-\d+$/, ""))) {
                ringed.add(span.id);
            }
        }
    }
    state.ringed = ringed;
    for (const id of ringed) {
        document.getElementById(id)?.classList.add("highlight");
    }
    placeRings();
    if (scroll) {
        const first = document.querySelector("body > .ring");
        const box = first?.getBoundingClientRect();
        if (box && (box.top < 0 || box.bottom > window.innerHeight)) {
            window.scrollTo({ top: window.scrollY + box.top - window.innerHeight / 3, behavior: "smooth" });
        }
    }
    return ringed.size;
}

/** The boxes round the ringed noteheads, in the document's pixels, in reading order; again whenever the pages move. */
function placeRings() {
    for (const old of document.querySelectorAll("body > .ring")) {
        old.remove();
    }
    const boxes = [];
    for (const id of state.ringed) {
        const element = document.getElementById(id);
        if (element === null || element.closest("#focus")) {
            continue;
        }
        // The box goes round the notehead, not the stem, so neighbouring notes keep their own boxes.
        const box = (element.querySelector("g.notehead") ?? element).getBoundingClientRect();
        if (box.width > 0) {
            boxes.push(box);
        }
    }
    boxes.sort((a, b) => a.top - b.top || a.left - b.left);
    for (const box of boxes) {
        const ring = document.createElement("span");
        ring.className = "ring";
        ring.setAttribute("aria-hidden", "true");
        ring.style.left = `${box.left + window.scrollX - 3}px`;
        ring.style.top = `${box.top + window.scrollY - 3}px`;
        ring.style.width = `${box.width + 6}px`;
        ring.style.height = `${box.height + 6}px`;
        document.body.append(ring);
    }
}

/** Room under the last page for a drawer over the bottom of the screen, in pixels, so the end of the piece can be read. */
function inset({ bottom }) {
    document.body.style.paddingBottom = bottom > 0 ? `calc(var(--pad-bottom) + ${bottom}px)` : "";
    return layout();
}

/** Where each bar is now, in the document's pixels: for the tests. */
function frames() {
    return state.frames;
}

window.motiff = { show, layout, anchor, erase, draw, frames, inView, phrases, select, menuSize, pen, focus, card: showCard, lead, inset, highlight };
window.webkit?.messageHandlers?.score?.postMessage("ready");
