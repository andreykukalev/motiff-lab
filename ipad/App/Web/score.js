// The score in the iPad app (decision record 0019 section 1.4). The pages are Verovio's SVG; where each bar sits is
// read by the web app's own score geometry, and pencil marks are anchored to bars and drawn exactly as the web app
// does (Scripts/score/pencil-marks.ts, compiled into the app as score/). PencilKit draws the live stroke in Swift; when
// it ends, Swift hands its points here to be kept against its bar (`anchor`), saves the stroke in the project file
// through music-core (`ink.add`), and gives the marks back to be drawn (`draw`).
import { ScoreGeometry, readPage } from "../score/score-geometry.js";
import { ScoreTimeline } from "../score/score-timeline.js";
import { HIGHLIGHTER_SIZE, PEN_SIZE, anchorMark, framesByTick, outlinePath, placeMark, touches } from "../score/pencil-marks.js";

const SVG = "http://www.w3.org/2000/svg";

/** The eraser takes marks within this many pixels of its path, as in the web app. */
const ERASER_REACH = 10;

const state = { pages: [], timeline: null, frames: [], byTick: new Map(), marks: [] };
let nextId = 0;

/** Shows the pages and reads where their bars are; `timemap` is Verovio's, with measures and rests. */
function show({ pages, timemap, dark }) {
    document.documentElement.toggleAttribute("data-theme", false);
    if (dark) {
        document.documentElement.setAttribute("data-theme", "dark");
    }
    const host = document.getElementById("pages");
    host.innerHTML = pages.map((svg) => `<div class="page">${svg}</div>`).join("");
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
    state.frames = geometry.barFrames();
    state.byTick = framesByTick(state.frames);
    redraw();
    return { bars: state.frames.length, space: state.frames[0]?.space ?? 0 };
}

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
    resizing = requestAnimationFrame(() => layout());
});

/** Where each bar is now, in the document's pixels: for the tests. */
function frames() {
    return state.frames;
}

window.motiff = { show, layout, anchor, erase, draw, frames };
window.webkit?.messageHandlers?.score?.postMessage("ready");
