// The score in the iPad app (decision record 0019 section 1.4). The pages are Verovio's SVG; where each bar sits is
// read by the web app's own score geometry, and pencil marks are anchored to bars and drawn exactly as the web app
// does (Scripts/score/pencil-marks.ts, compiled into the app as score/). Swift draws the live stroke with the same
// outline and grain (InkInput.swift); when it ends, Swift hands its points here to be kept against its bar (`anchor`),
// saves the stroke in the project file through music-core (`ink.add`), and gives the marks back to be drawn (`draw`).
// The pencil's marks are filled with the grain of Web/grain.png (score.html, #479 item 7); the eraser cuts the marks
// it passes over into what is left of them (`eraseAlong`). Who selects, taps or writes with a finger or the Pencil is
// Swift's (`input`, InputRules.swift, #479 item 18).
//
// The score is also the selection surface (0018 section 3): the web app's own selection layer
// (Scripts/score/selection-layer.ts) draws the band and its handles and turns a long press and a drag across bars into a
// choice of bars. What the player does on the score goes to Swift as a message: a bar tapped, bars chosen, and where the
// selection menu goes. The phrases are no longer drawn as brackets above the top staff (Andrei, 2026-11-13, #479 item 14:
// the strip read as an extra staff line); they stay in music-core, and focus still steps by them.
//
// Phrase focus (0018 section 4) lifts the selection into a card across the screen with the page dimmed behind it
// (`focus`, `card`): Swift engraves the phrase's bars with Verovio at the card's width, with the web app's own card
// layout (Scripts/score/phrase-card.ts), and the page shows the SVG. A tap on the dimmed page goes back to it. A
// variation being tried is engraved above the phrase as an ossia, in the kit's dashed plum frame named by an overline,
// as the web app's card draws it.
//
// A fact chosen in the Insights stream (0018 section 6) rings its places on the score (`highlight`), as the web app's
// score view does: the notes turn cobalt with a box round each notehead, and the first comes into view.
//
// The player's bookmarks (0018 section 5) are brackets in their category's ink above the top staff, titled in
// italic at the left end and stacked where they overlap, as the web app's score view draws them (`bookmarks`); a tap
// gives the bookmark to Swift. The Contents drawer's Pages tab shows the pages as small pictures (`outline`), and a
// page or a bookmark chosen there is brought into view (`showPage`, `reveal`).
//
// Margin notes are numbered amber markers at the top right of their first bar, and highlights translucent bands in an
// ink behind the staves (`notes`), as the web app's score view draws them; a tap on a marker gives the note to Swift.
// The bars a scan or a transcription is unsure of carry an amber "?" at their top right (`checks`), the notes on the same
// bar to its left; a tap gives the bar to Swift. Each layer of marks can be hidden for a clean page (`layers`).
//
// Another version of the piece (0022) marks the bars it has otherwise with a plum label above the bar's top left, over the
// bookmarks' level and before any Check flag or note on the bar, and tints the printed notes it does not have
// (`performance`), as the web app's score view does; a tap on a label gives its bar to Swift with where the label is. These
// show only with Extra information on (testing item 33). While a merge draft shows the version or the result, the notes from
// the version are plum and the player's own amber; a thin plum line runs under each bar taken from a version. Above the
// phrase on the card in focus, the version's added notes are plum.
//
// While the piece plays, the notes sounding turn cobalt on the page and on the card in focus (`playing`), a cobalt line
// stands at the sounding moment with a wash over its bar (testing item 4, the web app's score view), and the page
// follows unless the player turned that off: the system playing is kept below the bar, so the next one shows beneath it
// (0018 section 7). A scroll by hand wins for a few seconds, as on the web, and the next Play follows again. While the
// line is out of sight, the bar it is in goes to Swift (`playheadAway`, -1 when it is back), for the chip that returns
// to it (`returnToPlayhead`).
import { cardLayout } from "../score/phrase-card.js";
import { BRACKET_LIFT, ScoreGeometry, bookmarkLevels, readPage, staffFrames } from "../score/score-geometry.js";
import { SelectionLayer } from "../score/selection-layer.js";
import { ScoreTimeline, highlightedIds } from "../score/score-timeline.js";
import { DEFAULT_WIDTH, HIGHLIGHTER_SIZES, PEN_SIZES, anchorMark, eraseAlong, framesByTick, outlinePath, placeMark, touches } from "../score/pencil-marks.js";
import { beatUnder, modeOf } from "../score/score-modes.js";
import { ScoreBeats } from "../score/selection-beats.js";
import { ScorePlayheadMarks, onePlace } from "../score/playheads.js";

const SVG = "http://www.w3.org/2000/svg";

/** The eraser takes marks within this many pixels of its path, as in the web app. */
const ERASER_REACH = 10;

/** The reading line, this far below the top of the view: the pages it crosses are the ones the bar names, as on the web. */
const READING_LINE_PX = 72;

/** Bookmarks sit this far above the top staff, clear of a version's labels over the bars, and overlapping ones stack by the next amount, as on the web. */
const BOOKMARK_LIFT = 40;
const BOOKMARK_STACK = 24;

/** A bookmark's title rises this far above its bracket; marks further above a band than the reach belong to the system above. */
const BOOKMARK_TITLE = 17;
const BOOKMARK_REACH = 120;

/** A margin note's marker sits this far right of the bar's top right corner and this far above it, as on the web. */
const NOTE_OFFSET_X = 4;
const NOTE_OFFSET_Y = 22;

/** Another version's label: 20 px of plum in a 24 px target, at least wide enough for a count and at most this wide. */
/** How far under a taken bar's lowest staff its plum line runs, in pixels, as the web app's score view has it. */
const TAKEN_GAP = 4;
const PERFORMANCE_HEIGHT = 20;
const PERFORMANCE_TARGET = 24;
const PERFORMANCE_MIN_WIDTH = 36;
const PERFORMANCE_MAX_WIDTH = 180;

/** The layers of marks that can be hidden: the pencil, notes and highlights, bookmarks, the analysis (rings) and Check. */
const LAYERS = ["ink", "notes", "bookmarks", "analysis", "check"];

/** After the player scrolls by hand, playback leaves the page where they put it for this long before following again (score-view.ts). */
const FOLLOW_PAUSE_MS = 4000;

const state = { pages: [], svgs: [], timeline: null, geometry: null, frames: [], byTick: new Map(), marks: [], bookmarks: [], notes: [], highlights: [], checks: [], performance: [], unheard: new Set(), added: new Set(), artefacts: new Set(), own: new Set(), taken: [], pen: false, focus: false, ringed: new Set(), beats: null, mode: null, heads: null, input: null };
let nextId = 0;

/** The kind of pointer that pressed last ("touch" for a finger, "pen" for the Pencil), for the tap that follows. */
let lastPointer = "touch";
document.addEventListener("pointerdown", (event) => { lastPointer = event.pointerType; }, true);

/** Shows the pages and reads where their bars are; `timemap` is Verovio's, with measures and rests. */
function show({ pages, timemap, dark }) {
    document.documentElement.toggleAttribute("data-theme", false);
    if (dark) {
        document.documentElement.setAttribute("data-theme", "dark");
    }
    reported = "";
    state.ringed = new Set();
    sounding.page = new Set();
    sounding.key = "";
    const host = document.getElementById("pages");
    host.innerHTML = pages.map((svg, index) => `<div class="page" data-page="${index + 1}">${svg}</div>`).join("");
    state.svgs = pages;
    state.pages = pages.map((svg, index) => readPage(svg, index + 1));
    state.timeline = ScoreTimeline.fromTimemap(timemap);
    tintUnheard();
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
    placeBookmarks();
    placeNotes();
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
 * The bookmarks, each { id, start, end, title, ink, label }: brackets in the category's ink ("graphite", "carmine",
 * "forest" or "ochre") above the top staff, the title in italic at the left end of every run, overlapping ones
 * stacked.
 */
function bookmarks({ items }) {
    state.bookmarks = items ?? [];
    placeBookmarks();
    selection.placeMenu();
    return document.querySelectorAll("body > .bmb").length;
}

function placeBookmarks() {
    for (const old of document.querySelectorAll("body > .bmb")) {
        old.remove();
    }
    const geometry = state.geometry;
    if (geometry === null || state.bookmarks.length === 0) {
        return;
    }
    const levels = bookmarkLevels(state.bookmarks);
    state.bookmarks.forEach((bookmark, index) => {
        for (const run of geometry.bracket(bookmark.start, bookmark.end, BOOKMARK_LIFT + levels[index] * BOOKMARK_STACK)) {
            const mark = document.createElement("button");
            mark.type = "button";
            mark.className = `bmb ink-${bookmark.ink}`;
            mark.classList.toggle("from", !run.first);
            mark.classList.toggle("on-to", !run.last);
            mark.dataset.bookmark = bookmark.id;
            mark.style.left = `${run.left}px`;
            mark.style.top = `${run.top}px`;
            mark.style.width = `${Math.max(4, run.right - run.left)}px`;
            // Every run has its hooks and its title, as a printed edition marks a passage across a system break (the
            // web app's score view): the continuation's title follows "…", so it does not read as a rule under the
            // system above.
            const title = document.createElement("span");
            title.className = "w";
            title.setAttribute("aria-hidden", "true");
            title.textContent = run.first ? bookmark.title : `…${bookmark.title}`;
            mark.append(title);
            if (run.first) {
                mark.setAttribute("aria-label", bookmark.label);
            } else {
                mark.tabIndex = -1;
                mark.setAttribute("aria-hidden", "true");
            }
            document.body.append(mark);
        }
    });
}

/**
 * The margin notes, each { id, tick, number, label }, as amber markers numbered in score order at the top right of the
 * bar they start in, side by side on one bar; and the highlights, each { id, start, end, ink }, as bands behind the
 * staves of their bars in "amber", "carmine", "forest" or "ochre".
 */
function notes({ notes, highlights }) {
    state.notes = notes ?? [];
    state.highlights = highlights ?? [];
    placeNotes();
    return document.querySelectorAll("body > .marker").length + document.querySelectorAll("body > .hl").length;
}

/** The bars Check asks about, each { bar, tick, label }: an amber "?" above the bar's top right; none draws none. */
function checks({ flags }) {
    state.checks = flags ?? [];
    placeNotes();
    return document.querySelectorAll("body > .check-flag").length;
}

/**
 * The bars another version has otherwise, each { bar, tick, text, label }, as plum labels above the bars, and the ids of
 * the printed notes it does not have, tinted in place (0022, section 4); the ids of notes from a version (`added`, plum) and
 * of the player's own (`own`, amber) on the score a merge draft shows, and the ticks of the bars `taken` from versions, a
 * thin plum line under each; empty lists draw none. Given to Swift as `performance`; named otherwise here, where
 * `performance` is the clock.
 */
function showVersion({ markers, unheard, added, artefacts, own, taken }) {
    state.performance = markers ?? [];
    state.unheard = new Set(unheard ?? []);
    state.added = new Set(added ?? []);
    state.artefacts = new Set(artefacts ?? []);
    state.own = new Set(own ?? []);
    state.taken = taken ?? [];
    placeNotes();
    tintUnheard();
    return document.querySelectorAll("body > .perf-marker").length;
}

/**
 * The printed notes the other version does not have in the warning colour, and on its layer the notes it adds in plum and
 * its likely artefacts in grey, wherever the page draws them.
 */
function tintUnheard() {
    const host = document.getElementById("pages");
    tint(host, state.timeline, state.unheard, "not-heard");
    tint(host, state.timeline, state.added, "version-added");
    tint(host, state.timeline, state.artefacts, "version-artefact");
    tint(host, state.timeline, state.own, "own-note");
}

/** Gives the notes engraved in `root` with these ids, or a later piece of one tied over ("…-2"), the class; none lose it. */
function tint(root, timeline, ids, name) {
    for (const old of root.querySelectorAll(`.${name}`)) {
        old.classList.remove(name);
    }
    if (timeline === null || ids.size === 0) {
        return;
    }
    for (const span of timeline.spans) {
        if (ids.has(span.id) || ids.has(span.id.replace(/-\d+$/, ""))) {
            root.querySelector(`#${CSS.escape(span.id)}`)?.classList.add(name);
        }
    }
}

function placeNotes() {
    for (const old of document.querySelectorAll("body > .marker, body > .hl, body > .check-flag, body > .perf-marker, body > .taken-bar")) {
        old.remove();
    }
    const geometry = state.geometry;
    if (geometry === null) {
        return;
    }
    for (const tick of state.taken) {
        const bar = geometry.barAt(tick);
        if (bar === undefined) {
            continue;
        }
        const line = document.createElement("div");
        line.className = "taken-bar";
        line.setAttribute("aria-hidden", "true");
        line.style.left = `${bar.left}px`;
        line.style.top = `${bar.bottom + TAKEN_GAP}px`;
        line.style.width = `${bar.right - bar.left}px`;
        document.body.append(line);
    }
    for (const highlight of state.highlights) {
        for (const run of geometry.band(highlight.start, highlight.end, 2)) {
            const band = document.createElement("div");
            band.className = `hl ink-${highlight.ink}`;
            band.dataset.highlight = highlight.id;
            band.setAttribute("aria-hidden", "true");
            band.style.left = `${run.left}px`;
            band.style.top = `${run.top}px`;
            band.style.width = `${run.right - run.left}px`;
            band.style.height = `${run.bottom - run.top}px`;
            document.body.append(band);
        }
    }
    // A Check flag sits at the bar's top right; notes on the same bar sit side by side to its left.
    const onBar = new Map();
    for (const check of state.checks) {
        const place = geometry.flag(check.tick);
        if (place === undefined) {
            continue;
        }
        onBar.set(geometry.barAt(check.tick)?.tick ?? check.tick, 1);
        const flag = document.createElement("button");
        flag.type = "button";
        flag.className = "check-flag";
        flag.dataset.bar = String(check.bar);
        flag.setAttribute("aria-label", check.label);
        const square = document.createElement("span");
        square.setAttribute("aria-hidden", "true");
        square.textContent = "?";
        flag.append(square);
        flag.style.left = `${place.x - NOTE_OFFSET_X - 24}px`;
        flag.style.top = `${place.y - NOTE_OFFSET_Y}px`;
        document.body.append(flag);
    }
    const flags = [];
    for (const check of state.checks) {
        const place = geometry.flag(check.tick);
        if (place !== undefined) {
            flags.push({ left: place.x - NOTE_OFFSET_X - 24, top: place.y - NOTE_OFFSET_Y, system: geometry.barAt(check.tick)?.system ?? -1 });
        }
    }
    for (const note of state.notes) {
        const place = geometry.flag(note.tick);
        if (place === undefined) {
            continue;
        }
        const bar = geometry.barAt(note.tick)?.tick ?? note.tick;
        const index = onBar.get(bar) ?? 0;
        onBar.set(bar, index + 1);
        const marker = document.createElement("button");
        marker.type = "button";
        marker.className = "marker";
        marker.dataset.note = note.id;
        marker.textContent = String(note.number);
        marker.setAttribute("aria-label", note.label);
        marker.style.left = `${place.x - NOTE_OFFSET_X - 24 - index * 28}px`;
        marker.style.top = `${place.y - NOTE_OFFSET_Y}px`;
        document.body.append(marker);
        flags.push({ left: place.x - NOTE_OFFSET_X - 24 - index * 28, top: place.y - NOTE_OFFSET_Y, system: geometry.barAt(note.tick)?.system ?? -1 });
    }
    placePerformance(geometry, flags);
}

/**
 * Another version's labels above the bars' top left, under the bookmarks: one may run on over the next bar, up to the
 * next label, Check flag or note on the same system, and goes up above the flags when a narrow bar leaves it too little room.
 */
function placePerformance(geometry, flags) {
    const placed = state.performance.map((marker) => ({ marker, bar: geometry.barAt(marker.tick) })).filter((entry) => entry.bar !== undefined);
    placed.forEach(({ marker, bar }, index) => {
        const next = placed[index + 1];
        let room = next !== undefined && next.bar.system === bar.system ? next.bar.left - bar.left - NOTE_OFFSET_X * 2 : PERFORMANCE_MAX_WIDTH;
        const ahead = flags.filter((flag) => flag.system === bar.system && flag.left > bar.left && flag.left - bar.left - NOTE_OFFSET_X * 2 < room);
        const flagRoom = Math.min(...ahead.map((flag) => flag.left - bar.left - NOTE_OFFSET_X * 2));
        const lifted = ahead.length > 0 && flagRoom < PERFORMANCE_MIN_WIDTH;
        if (ahead.length > 0 && !lifted) {
            room = flagRoom;
        }
        const top = (bar.staves[0]?.top ?? bar.top) - BRACKET_LIFT - PERFORMANCE_HEIGHT - NOTE_OFFSET_X - (PERFORMANCE_TARGET - PERFORMANCE_HEIGHT) / 2;
        const label = document.createElement("button");
        label.type = "button";
        label.className = "perf-marker";
        label.dataset.bar = String(marker.bar);
        const text = document.createElement("span");
        text.textContent = marker.text;
        label.append(text);
        label.setAttribute("aria-label", marker.label);
        label.style.left = `${bar.left + NOTE_OFFSET_X}px`;
        label.style.top = `${lifted ? Math.min(top, Math.min(...ahead.map((flag) => flag.top)) - PERFORMANCE_TARGET - 2) : top}px`;
        label.style.maxWidth = `${Math.max(PERFORMANCE_MIN_WIDTH, Math.min(PERFORMANCE_MAX_WIDTH, room))}px`;
        document.body.append(label);
    });
}

/**
 * Hides the layers named, for a clean page; the others show. With `slate`, Night stand shows light notes on a dark page. Returns what is hidden.
 */
function layers({ hidden, slate }) {
    const names = hidden ?? [];
    for (const layer of LAYERS) {
        document.body.classList.toggle(`hide-${layer}`, names.includes(layer));
    }
    document.documentElement.toggleAttribute("data-page", false);
    if (slate) {
        document.documentElement.setAttribute("data-page", "slate");
    }
    return LAYERS.filter((layer) => names.includes(layer));
}

/** The top of the bookmark titles over a band's first run, in window pixels, for the menu to clear; null when none is there. */
function bookmarksTop(band) {
    let top = null;
    for (const mark of document.querySelectorAll("body > .bmb")) {
        const box = mark.getBoundingClientRect();
        if (box.right > band.left && box.left < band.right && box.bottom <= band.top + 1 && band.top - box.top <= BOOKMARK_REACH) {
            top = Math.min(top ?? box.top, box.top - BOOKMARK_TITLE);
        }
    }
    return top;
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
    marksTop: (band) => bookmarksTop(band),
    // While the Pen is on a finger writes, and in focus the page under the veil takes no choice of bars. Once Swift
    // has sent the rules for each pointer (`input`, #479 item 18), they say who selects instead.
    penOn: () => state.focus || (state.input === null && state.pen),
    chosen: (first, last) => {
        const end = state.geometry?.barAt(last)?.end;
        if (end !== undefined) {
            post({ chosen: { start: first, end } });
        }
    },
    // Selection by beats (#349, Settings · Experimental): the beat lines music-core gives, which the drag and the handles
    // snap to, and where a tick sounds across its bar, for a band that starts or ends mid-bar.
    beats: () => state.beats,
    tickX: (tick) => state.geometry?.playhead(tick, (id) => state.timeline?.onsetOf(id))?.x,
    chosenSpan: (start, end) => post({ chosen: { start, end, beats: true } }),
    // The modes of version 0.5 (Settings · Experimental): only Select starts a selection with a drag, never from inside it,
    // and there a finger drags at once, as two fingers scroll.
    mode: () => state.mode,
    fingerDrags: () => state.mode === "select",
    dragStart: (pointerType) => (state.focus ? "none" : state.input?.[pointerType]?.select),
});

/** The piece's beats as the selection reads them, for a tap to find the beat under it (version 0.5). */
const scoreBeats = new ScoreBeats({ beats: () => state.beats, tickX: (tick) => state.geometry?.playhead(tick, (id) => state.timeline?.onsetOf(id))?.x });

/**
 * The one playback position of version 0.5.2 (#479 items 19 and 25), drawn as the web app's score view draws its playheads
 * (Scripts/score/playheads.ts): the ink track under the bar outside the selection, the cobalt line inside it.
 */
const heads = new ScorePlayheadMarks(document.body, {
    at: (tick) => (state.timeline === null ? undefined : state.geometry?.playhead(tick, (id) => state.timeline.onsetOf(id))),
    lineBottom: (bar) => state.geometry?.lineInkBottom(bar.system) ?? bar.bottom,
});

/**
 * The mode the bar chose (version 0.5, the experiment Two playheads and modes): "navigate", "select", "edit" or "pen";
 * none keeps the score as before.
 */
function mode({ name }) {
    state.mode = modeOf(name);
    if (state.mode === null) {
        delete document.body.dataset.mode;
    } else {
        document.body.dataset.mode = state.mode;
    }
    selection.place();
}

/**
 * Who does what on the score (#479 item 18, InputRules.swift): for "touch" (a finger) and "pen" (the Pencil), how a press
 * starts a selection ("now", "longPress" or "none") and whether a tap acts on the page. Before Swift sends them, the
 * rules before: the Pen on, nothing but writing.
 */
function input(rules) {
    state.input = rules ?? null;
    selection.place();
}

/** Whether the tap just made acts on the page: not by a pointer that writes. */
function tapActs() {
    const rule = state.input?.[lastPointer];
    return rule === undefined ? !state.pen : rule.tap;
}

/**
 * The one playback position under the modes (version 0.5.2): { selection: { start, end } or null }, the selection it turns
 * cobalt in; null draws the playhead of before the modes.
 */
function playheads(value) {
    state.heads = value ?? null;
    if (state.heads === null) {
        heads.hide();
    } else {
        placePlayhead(null);
    }
}

/**
 * A tap under the modes never changes the selection: in Edit notes a tap on a note opens it on the roll's editor, and
 * otherwise it places a playhead on the beat under it (inside the selection its own, else the global one), or puts the
 * sheet away off the bars. The Pen only writes.
 */
function tapUnderModes(event, target) {
    const geometry = state.geometry;
    // In the Pen a tap by a finger that does not write places the playhead, as in Navigate (#479 item 18).
    if (geometry === null || (state.mode === "pen" && state.input === null)) {
        return;
    }
    if (state.mode === "edit") {
        const hit = target.closest("g.note, g.chord");
        const note = hit?.matches("g.chord") ? hit.querySelector("g.note") : hit;
        const onset = note?.id ? state.timeline?.onsetOf(note.id) : undefined;
        if (onset !== undefined) {
            post({ editNote: { onset } });
            return;
        }
    }
    const view = document.body.getBoundingClientRect();
    const x = event.clientX - view.left + document.body.scrollLeft;
    const y = event.clientY - view.top + document.body.scrollTop;
    const beat = beatUnder(geometry, scoreBeats, x, y);
    post(beat === undefined ? { blank: true } : { place: { start: beat.start, end: beat.end } });
}

/** Selection by beats on (`lines`, the beat lines in ticks from music-core's playback score) or off (none). */
function beats({ lines }) {
    state.beats = Array.isArray(lines) && lines.length > 0 ? lines : null;
    selection.place();
}
const placeMenu = selection.placeMenu.bind(selection);
selection.placeMenu = () => {
    placeMenu();
    reportMenu();
    reportHandles();
};

let handlesReported = "";

/**
 * Where the selection's handles are, in the document's pixels, whenever they move: a touch on one moves it, and the
 * Pencil does not write there (#479 item 18).
 */
function reportHandles() {
    const handles = [...document.querySelectorAll("body > .sel-handle")].map((handle) => ({ x: parseFloat(handle.style.left) || 0, y: parseFloat(handle.style.top) || 0 }));
    const key = JSON.stringify(handles);
    if (key !== handlesReported) {
        handlesReported = key;
        post({ handles });
    }
}
selection.setMenu(menu);

/** A drawer over the left of the page (the Contents): the selection layer keeps the menu to its right, as on the web. */
const clearing = document.createElement("div");
clearing.dataset.menuClear = "left";
clearing.setAttribute("aria-hidden", "true");
clearing.style.cssText = "position: fixed; left: 0; top: 0; width: 0; height: 0; visibility: hidden; pointer-events: none;";
document.body.append(clearing);

/** How far from the left of the screen a drawer covers the page, in pixels; 0 for none. */
function clearLeft({ width }) {
    clearing.style.width = `${Math.max(0, width ?? 0)}px`;
    selection.placeMenu();
}

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

/** How far above or below a bar's staves a tap still lands on that bar, in pixels. */
const BAR_REACH = 24;

/**
 * The bar under a tap, found from where the bars are engraved rather than from the SVG element under the finger, so a tap on
 * the white between the staff lines lands on its bar as a tap on a note does (#338).
 */
function barUnder(event) {
    const geometry = state.geometry;
    if (!geometry) {
        return undefined;
    }
    const view = document.body.getBoundingClientRect();
    const x = event.clientX - view.left + document.body.scrollLeft;
    const y = event.clientY - view.top + document.body.scrollTop;
    // The staves, widened to the notes above and below them (ledger lines) and a finger's reach round both.
    return geometry.bars.find((bar) => x >= bar.left && x < bar.right
        && y >= Math.min(bar.top, bar.ink.top) - BAR_REACH && y <= Math.max(bar.bottom, bar.ink.bottom) + BAR_REACH);
}

/**
 * A tap on the score: a tap anywhere in a bar, on a note or between them, names that bar
 * to Swift, which selects it when nothing is selected. A tap never plays (#338): the music starts only with Play or Loop.
 */
document.body.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    if (target?.closest("#focus")) {
        return;
    }
    const flag = target?.closest(".check-flag");
    if (flag) {
        post({ checkFlag: Number(flag.dataset.bar) });
        return;
    }
    const version = target?.closest(".perf-marker");
    if (version) {
        const box = version.getBoundingClientRect();
        post({ performanceBar: Number(version.dataset.bar), left: box.left, bottom: box.bottom });
        return;
    }
    const marker = target?.closest(".marker");
    if (marker) {
        post({ noteMarker: marker.dataset.note });
        return;
    }
    const bookmark = target?.closest(".bmb");
    if (bookmark) {
        post({ bookmark: bookmark.dataset.bookmark });
        return;
    }
    if (selection.swallowsClick() || !tapActs() || target === null || target.closest(".sel-handle")) {
        return;
    }
    if (state.mode !== null) {
        tapUnderModes(event, target);
        return;
    }
    const bar = barUnder(event);
    post(bar ? { bar: { start: bar.tick, end: bar.end } } : { blank: true });
});

/**
 * Keeps a stroke, its points in the document's pixels, against the bar it starts in, at the thickness chosen (0 to 2);
 * an empty object when there is no bar (WebKit's async calls do not take a null back).
 */
function anchor({ points, tool, colour, width = DEFAULT_WIDTH }) {
    nextId += 1;
    const id = `m${Date.now().toString(36)}${nextId.toString(36)}`;
    // The thickness chosen in the palette, thin to thick, as the web app's pencil layer sizes its marks.
    const size = (tool === "highlighter" ? HIGHLIGHTER_SIZES : PEN_SIZES)[width] ?? (tool === "highlighter" ? HIGHLIGHTER_SIZES : PEN_SIZES)[DEFAULT_WIDTH];
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

/**
 * The eraser's path, in the document's pixels (#479 item 7): the marks it passed over, and what is left of each, kept
 * against its bars again, as a real eraser leaves a line in pieces. Swift adds the pieces and removes the marks.
 */
function eraseAlongPath({ points }) {
    const removed = [];
    const added = [];
    for (const mark of state.marks) {
        const place = placeMark(mark, state.byTick);
        const pieces = place === null ? null : eraseAlong(place.points, place.radius, points, ERASER_REACH);
        if (pieces === null) {
            continue;
        }
        removed.push(mark.id);
        for (const piece of pieces) {
            nextId += 1;
            const id = `m${Date.now().toString(36)}${nextId.toString(36)}`;
            const kept = anchorMark(state.frames, piece, { id, tool: mark.tool, colour: mark.colour, size: mark.size });
            if (kept !== null) {
                added.push(kept);
            }
        }
    }
    return { removed, added };
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
    // The highlighter's strokes are one layer under the pencil's, opaque inside it and translucent as a whole, so where
    // strokes overlap the amber is as light as anywhere else (#479 item 9).
    const highlights = document.createElementNS(SVG, "g");
    highlights.setAttribute("class", "highlights");
    ink.replaceChildren(highlights);
    for (const mark of state.marks) {
        const place = placeMark(mark, state.byTick);
        if (place === null) {
            continue;
        }
        const path = document.createElementNS(SVG, "path");
        path.setAttribute("class", `mark ${mark.tool} ${mark.colour}`);
        path.setAttribute("d", outlinePath(place.points, place.radius, mark.tool === "pen"));
        path.dataset.id = mark.id;
        (mark.tool === "highlighter" ? highlights : ink).append(path);
    }
}

let resizing = 0;
window.addEventListener("resize", () => {
    cancelAnimationFrame(resizing);
    resizing = requestAnimationFrame(() => {
        layout();
        placeOssias();
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
const card = { timeline: null, offset: 0, width: 0, ossias: [] };

focusLayer.querySelector(".veil").addEventListener("click", () => post({ leave: true }));

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
        card.ossias = [];
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
function showCard({ pages, timemap, start, ossias }) {
    sounding.card = new Set();
    sounding.key = "";
    cardScore.innerHTML = (pages ?? []).map((svg) => `<div class="card-page">${svg}</div>`).join("");
    card.timeline = ScoreTimeline.fromTimemap(timemap ?? []);
    card.offset = state.geometry?.barAt(start)?.tick ?? start;
    card.pages = (pages ?? []).map((svg, index) => readPage(svg, index + 1));
    card.ossias = ossias ?? [];
    placeOssias();
    tint(cardScore, card.timeline, new Set(card.ossias.flatMap((ossia) => ossia.added ?? [])), "version-added");
    return card.timeline.spans.length;
}

/** Room round the ossia's staves inside its frame, in pixels, as on the web. */
const FRAME_PAD_X = 10;
const FRAME_PAD_Y = 8;

/** Each ossia's frame round its staves on every system of the card, its name over the first (phrase-card.ts placeFrames). */
function placeOssias() {
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
            const box = svg.getBoundingClientRect();
            const scale = Math.min(box.width / page.width, box.height / page.height);
            const left = box.left - view.left + cardScore.scrollLeft + (box.width - page.width * scale) / 2;
            const top = box.top - view.top + cardScore.scrollTop + (box.height - page.height * scale) / 2;
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
function reportCardWidth() {
    if (!state.focus || Math.abs(cardScore.clientWidth - card.width) < RELAYOUT_THRESHOLD_PX) {
        return;
    }
    card.width = cardScore.clientWidth;
    post({ cardLayout: cardLayout(card.width) });
}

/** The notes sounding now, marked on the page or on the card, and when they last changed. */
const sounding = { page: new Set(), card: new Set(), key: "", byHand: -Infinity };

/**
 * The playhead's line and its bar's wash on the page, made on first use, the bar last reported out of sight, and whether
 * the piece has played, so a pause leaves the playhead resting where it stopped (testing item 28).
 */
const playhead = { line: null, wash: null, away: -1, played: false, top: 0, shown: false };

/** The pill's room at the bottom of the view: a playhead under it is out of sight. */
const PILL_ROOM_PX = 84;

/** A touch or the wheel moving the page is the player's own scroll: playback leaves the page alone for a while. */
for (const name of ["touchmove", "wheel"]) {
    window.addEventListener(name, () => { sounding.byHand = performance.now(); }, { passive: true });
}

/**
 * Marks the notes sounding at `tick` on the page, or on the card in focus, as the web app's score view does; while
 * `playing`, the page keeps the system with them below the bar. Called about thirty times a second from Swift with the
 * player's position; `follow` true (a new Play) follows again at once after a scroll by hand. Returns the top the page
 * scrolls to when it follows, or null.
 */
function playing({ tick, playing, follow, auto = true }) {
    if (follow) {
        sounding.byHand = -Infinity;
    }
    playhead.played ||= playing;
    if (state.heads !== null) {
        // The one position (version 0.5.2): it always shows out of focus, cobalt inside the selection and ink outside it.
        let at;
        if (state.focus) {
            heads.hide();
        } else {
            at = heads.place(onePlace(state.heads.selection ?? null, tick, playing));
        }
        reportPlace(at, !playing);
    } else {
        // Stopped, the playhead stays where playing stopped, in graphite, so the place is not lost (testing item 28).
        const resting = !playing && playhead.played && tick > 0;
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

/** Puts the line and the wash at a tick, as the web app's score view does, or hides them for null; `resting` where playing stopped. */
function placePlayhead(tick, resting = false) {
    const timeline = state.timeline;
    const at = tick === null || timeline === null ? undefined : state.geometry?.playhead(tick, (id) => timeline.onsetOf(id));
    if (at === undefined) {
        for (const element of [playhead.line, playhead.wash]) {
            if (element !== null) {
                element.hidden = true;
            }
        }
        reportPlace(undefined, resting);
        return;
    }
    playhead.line ??= overlay("playhead");
    playhead.wash ??= overlay("play-bar");
    const { line, wash } = playhead;
    for (const element of [wash, line]) {
        if (!element.isConnected) {
            document.body.append(element);
        }
        element.hidden = false;
        element.classList.toggle("resting", resting);
    }
    // Moved by a transform on layers of their own, so the page under them is not painted again on every frame (testing item 31).
    setStyle(line, "transform", `translate(${at.x - 1}px, ${at.top - 6}px)`);
    setStyle(line, "height", `${at.bottom - at.top + 12}px`);
    setStyle(wash, "transform", `translate(${at.bar.left}px, ${at.top - 4}px)`);
    setStyle(wash, "width", `${at.bar.right - at.bar.left}px`);
    setStyle(wash, "height", `${at.bottom - at.top + 8}px`);
    reportPlace(at, resting);
}

/** Keeps where the playhead that plays is, for Back to playback, and tells Swift when it is out of sight while playing. */
function reportPlace(at, resting) {
    playhead.shown = at !== undefined;
    if (at === undefined) {
        reportAway(-1);
        return;
    }
    playhead.top = at.top - 6;
    const shownTop = window.scrollY + READING_LINE_PX - 16;
    const shownBottom = window.scrollY + window.innerHeight - PILL_ROOM_PX;
    reportAway(!resting && (at.bottom < shownTop || at.top > shownBottom) ? at.bar.tick : -1);
}

/** Sets a style only when it changes, so an unchanged value does not make the browser lay out or paint again. */
function setStyle(element, name, value) {
    if (element.style[name] !== value) {
        element.style[name] = value;
    }
}

function overlay(className) {
    const element = document.createElement("span");
    element.className = className;
    element.setAttribute("aria-hidden", "true");
    element.hidden = true;
    return element;
}

function reportAway(barTick) {
    if (barTick !== playhead.away) {
        playhead.away = barTick;
        post({ playheadAway: barTick });
    }
}

/** Back to playback: the page scrolls to the line and follows the playing again. */
function returnToPlayhead() {
    if (!playhead.shown) {
        return false;
    }
    sounding.byHand = -Infinity;
    window.scrollTo({ top: Math.max(0, playhead.top + 6 - READING_LINE_PX), behavior: "smooth" });
    return true;
}

/** The ids marked now: those no longer sounding lose the class, the new ones take it. */
function mark(before, ids, find) {
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
function keepInView(note) {
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

/**
 * The pages as the Contents drawer's Pages tab shows them: each { page, start, end, image }, its stretch of the piece in
 * ticks and its picture as a PNG `width` pixels wide, drawn in the page's ink on its paper; a page whose picture cannot be
 * drawn comes without one.
 */
async function outline({ width }) {
    const geometry = state.geometry;
    if (geometry === null) {
        return [];
    }
    const style = getComputedStyle(document.documentElement);
    const ink = style.getPropertyValue("--notation").trim() || "black";
    const paper = style.getPropertyValue("--paper").trim() || "white";
    const pages = [];
    for (const [index, svg] of state.svgs.entries()) {
        const bars = geometry.bars.filter((bar) => bar.page === index + 1);
        if (bars.length === 0) {
            continue;
        }
        const start = Math.min(...bars.map((bar) => bar.tick));
        const end = Math.max(...bars.map((bar) => (Number.isFinite(bar.end) ? bar.end : Number.MAX_SAFE_INTEGER)));
        pages.push({ page: index + 1, start, end, image: await picture(svg, width, ink, paper) });
    }
    return pages;
}

/** A page drawn as a PNG on its paper, as a data URL; "" when it cannot be drawn. */
async function picture(svg, width, ink, paper) {
    const height = Math.round((width * 297) / 210);
    // Verovio's notation is black on the page's own scale; the picture takes the stand's ink, as the page does.
    const inked = svg.replace(/ color="black"/, ` color="${ink}" fill="${ink}"`);
    const url = URL.createObjectURL(new Blob([inked], { type: "image/svg+xml" }));
    try {
        const image = new Image();
        image.src = url;
        await image.decode();
        const canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext("2d");
        context.fillStyle = paper;
        context.fillRect(0, 0, width, height);
        context.drawImage(image, 0, 0, width, height);
        return canvas.toDataURL("image/png");
    } catch {
        return "";
    } finally {
        URL.revokeObjectURL(url);
    }
}

/** Brings a page to the top of the view, under the bar. */
function showPage({ page }) {
    const element = document.querySelector(`#pages > .page[data-page="${page}"]`);
    if (element === null) {
        return false;
    }
    const top = element.getBoundingClientRect().top + window.scrollY - parseFloat(getComputedStyle(document.body).paddingTop);
    window.scrollTo({ top: Math.max(0, top), behavior: "smooth" });
    return true;
}

/** Brings the bar that holds `start` into view, a third of the way down, unless it is in view already. */
function reveal({ start }) {
    const bar = state.geometry?.barAt(start);
    if (bar === undefined) {
        return false;
    }
    const top = bar.top - window.scrollY;
    if (top < READING_LINE_PX || bar.bottom - window.scrollY > window.innerHeight) {
        window.scrollTo({ top: Math.max(0, bar.top - window.innerHeight / 3), behavior: "smooth" });
    }
    return true;
}

/** Where the notes on a page (from 1) start: its first bar's tick, or null. PDF | Notes leads from it to the PDF's page (0023). */
function firstTickOn({ page }) {
    const ticks = (state.geometry?.bars ?? []).filter((bar) => bar.page === page).map((bar) => bar.tick);
    return ticks.length > 0 ? Math.min(...ticks) : null;
}

/** Brings the page a place in the piece is on to the top, under the bar, as the PDF's pages lead to the notes (0023). */
function showTick({ tick }) {
    const bars = state.geometry?.bars ?? [];
    const before = bars.filter((bar) => bar.tick <= tick);
    const bar = before.length > 0 ? before.reduce((latest, candidate) => (candidate.tick > latest.tick ? candidate : latest)) : bars[0];
    return bar === undefined ? false : showPage({ page: bar.page });
}

/** Where each bar is now, in the document's pixels: for the tests. */
function frames() {
    return state.frames;
}

window.motiff = { show, layout, anchor, erase, eraseAlong: eraseAlongPath, input, draw, frames, inView, select, menuSize, pen, beats, focus, card: showCard, lead, inset, highlight, bookmarks, clearLeft, outline, showPage, showTick, firstTickOn, reveal, notes, checks, performance: showVersion, layers, playing, returnToPlayhead, mode, playheads };
window.webkit?.messageHandlers?.score?.postMessage("ready");
