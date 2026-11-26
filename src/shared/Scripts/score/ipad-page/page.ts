// The score in the iPad app (decision record 0019 section 1.4; 0031 step 11): Verovio's A4 pages, and over them the marks
// and the selection drawn by the shared score modules (Scripts/score), as one page that Swift fills through `window.motiff`
// and that tells Swift what the player did through `webkit.messageHandlers.score`, both as Scripts/score/page-contract.ts
// types them. Web/score.html loads it through Web/score.js. Each part is its own module beside this one
// (docs/code-map/score-page.md); here they are put on the page in the order their listeners must run, and offered to
// Swift. The phrases are not brackets (Andrei, 2026-11-13, #479 item 14: the strip above the top staff read as an extra
// staff line): each start has its breath mark and number (v1.2, page-phrases.ts).
import type { PageCommands } from "../page-contract.js";
import { frames, inView, inset, layout, listenResize, listenScroll, show } from "./page-layout.js";
import { anchor, draw, erase, eraseAlongPath, rub, rubEnd } from "./page-ink.js";
import { bookmarks } from "./page-bookmarks.js";
import { checks, layers, notes } from "./page-marks.js";
import { showVersion } from "./page-versions.js";
import { phraseMarks } from "./page-phrases.js";
import { listenPhraseDrag } from "./page-phrase-drag.js";
import { beats, clearLeft, installMenu, installSelection, menuClosed, menuMoved, menuSize, select, selectionControls } from "./page-selection.js";
import { installColouring } from "./page-colouring.js";
import { installPlayheads, playheads, practice, returnToPlayhead } from "./page-playheads.js";
import { listenHandScroll, playing, writing } from "./page-sounding.js";
import { focus, installCard, showCard } from "./page-card.js";
import { input, listenPointerKind, listenTaps, mode, pen } from "./page-taps.js";
import { highlight } from "./page-rings.js";
import { listenMarkPress } from "./page-mark-place.js";
import { firstTickOn, lineAt, outline, placeAt, reveal, showPage, showTick } from "./page-navigation.js";

// The page's own listeners first, then the layers', each pointer's press reaching them in this order.
listenPointerKind();
listenMarkPress();
listenScroll();
installSelection();
installColouring();
installPlayheads();
installMenu();
listenTaps();
listenPhraseDrag();
listenResize();
installCard();
listenHandScroll();

window.motiff = { show, layout, anchor, erase, eraseAlong: eraseAlongPath, rub, rubEnd, input, draw, frames, inView, select, menuSize, menuMoved, menuClosed, pen, beats, focus, card: showCard, inset, highlight, bookmarks, clearLeft, outline, showPage, showTick, firstTickOn, placeAt, lineAt, reveal, notes, checks, performance: showVersion, phraseMarks, layers, playing, returnToPlayhead, mode, playheads, practice, selectionControls, writing } satisfies PageCommands;
window.webkit?.messageHandlers?.score?.postMessage("ready");
