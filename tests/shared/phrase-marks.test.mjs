// Tests for where the phrase marks stand over the score (Scripts/score/phrase-marks.ts; the phrases spec, v1.2), run in
// Node on the compiled module:   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

import { commaOf, liftOver, markStarts, phraseAt, placeMarks } from "../../src/shared/wwwroot/js/score/phrase-marks.js";

const sizes = { gap: 4, height: 18, target: 44 };
const none = () => [];
const spot = (fields) => ({ system: 0, noteLeft: 100, staffTop: 200, comma: 8, letter: 0, number: 8, ...fields });

test("a mark stands its gap above the top staff, the comma right before the start note and the number over it", () => {
    const [mark] = placeMarks([spot({})], none, sizes);
    assert.equal(mark.bottom, 196);
    assert.equal(mark.top, 178);
    assert.equal(mark.comma, 100 - 8 - 2, "the comma ends half a gap before the note's head");
    assert.equal(mark.number, 100, "the number starts over the note");
    assert.equal(mark.letter, null);
    assert.equal(mark.letterTarget, null);
});

test("a section's start puts its boxed letter before the number; a section alone has only its letter", () => {
    const [both, alone] = placeMarks([spot({ letter: 14 }), spot({ system: 1, comma: 0, letter: 14, number: 0 })], none, sizes);
    assert.equal(both.letter, 100);
    assert.equal(both.number, 100 + 14 + 4);
    assert.equal(alone.comma, null);
    assert.equal(alone.number, null);
    assert.equal(alone.letter, 100);
    assert.equal(alone.numberTarget, null);
});

test("a mark is raised only over what is printed under it, not over what is printed beside it", () => {
    const printed = [
        { left: 95, right: 105, top: 170, bottom: 230 },  // a high note's stem over the start
        { left: 300, right: 340, top: 120, bottom: 140 }, // a tempo mark further on
    ];
    const [raised, beside] = placeMarks([spot({}), spot({ noteLeft: 200 })], () => printed, sizes);
    assert.equal(raised.bottom, 166);
    assert.equal(beside.bottom, 196, "the tempo mark is not under the second mark");
    assert.equal(liftOver(200, printed, 299, 301), 120);
    assert.equal(liftOver(200, printed, 340, 360), 200, "touching is not covering");
    assert.equal(liftOver(200, [{ left: 0, right: 500, top: 210, bottom: 230 }], 0, 10), 200, "what is lower than the staff's top line raises nothing");
});

test("the systems' marks are placed on their own: one system's printed things never raise another's", () => {
    const printed = (system) => (system === 1 ? [{ left: 0, right: 1000, top: 100, bottom: 150 }] : []);
    const [first, second] = placeMarks([spot({}), spot({ system: 1 })], printed, sizes);
    assert.equal(first.bottom, 196);
    assert.equal(second.bottom, 96);
});

test("a number closer to the one before it than its width moves right, and its comma stays at its note", () => {
    const [first, second, third] = placeMarks([spot({ number: 14 }), spot({ noteLeft: 106, number: 14 }), spot({ noteLeft: 300 })], none, sizes);
    assert.equal(first.number, 100);
    assert.equal(second.number, 100 + 14 + 4, "past the first number and a gap");
    assert.equal(second.comma, 106 - 8 - 2, "the comma at its own note");
    assert.equal(third.number, 300, "a far one stays over its note");
});

test("the marks are spread along each system in the order of their notes, whatever order they come in", () => {
    const [late, early] = placeMarks([spot({ noteLeft: 106, number: 14 }), spot({ number: 14 })], none, sizes);
    assert.equal(early.number, 100);
    assert.equal(late.number, 118);
    const [nextLine] = placeMarks([spot({ system: 1, noteLeft: 106 }), spot({ number: 14 })], none, sizes);
    assert.equal(nextLine.number, 106, "on another system nothing is in the way");
});

test("a number's touch target is the kit's size, centred on it, standing on the line the mark is raised from", () => {
    const [mark] = placeMarks([spot({})], none, sizes);
    assert.deepEqual(mark.numberTarget, { left: 104 - 22, right: 104 + 22, top: 200 - 44, bottom: 200 });
});

test("close targets meet halfway between their centres, far ones keep their size", () => {
    const [letterAndNumber, near, far] = placeMarks([spot({ letter: 14 }), spot({ noteLeft: 150 }), spot({ noteLeft: 400 })], none, sizes);
    const letter = letterAndNumber.letterTarget;
    const number = letterAndNumber.numberTarget;
    const letterCentre = 107;
    const numberCentre = 122;
    assert.equal(letter.right, (letterCentre + numberCentre) / 2);
    assert.equal(number.left, (letterCentre + numberCentre) / 2);
    assert.equal(letter.left, letterCentre - 22, "the letter keeps its side away from the number");
    assert.equal(number.right, (numberCentre + 154) / 2, "and the number shares with the next mark's");
    assert.equal(near.numberTarget.left, (numberCentre + 154) / 2);
    assert.equal(near.numberTarget.right, 154 + 22);
    assert.deepEqual(far.numberTarget, { left: 404 - 22, right: 404 + 22, top: 156, bottom: 200 });
});

test("targets on different systems, or at different heights, are not split", () => {
    const tall = [{ left: 140, right: 170, top: 100, bottom: 120 }];
    const [low, high] = placeMarks([spot({}), spot({ noteLeft: 150 })], () => tall, sizes);
    assert.equal(high.bottom, 96);
    assert.equal(low.numberTarget.right, 104 + 22, "the raised neighbour's target is above this one's");
    const [one, two] = placeMarks([spot({}), spot({ system: 1, noteLeft: 110 })], none, sizes);
    assert.equal(one.numberTarget.right, 126);
    assert.equal(two.numberTarget.left, 92);
});

test("in focus a motif's comma takes the tap, centred on it, and the card's phrase marks take none", () => {
    const [phrase, motif, near] = placeMarks([
        spot({ letter: 14, tap: "none" }),
        spot({ noteLeft: 300, comma: 4, number: 0, tap: "comma" }),
        spot({ noteLeft: 320, comma: 4, number: 0, tap: "comma" }),
    ], none, sizes);
    assert.equal(phrase.letterTarget, null, "only to read");
    assert.equal(phrase.numberTarget, null);
    assert.equal(phrase.commaTarget, null);
    assert.equal(motif.comma, 300 - 4 - 2, "the small comma right before its note, as a phrase's");
    assert.equal(motif.number, null, "no number");
    assert.equal(motif.numberTarget, null);
    const centre = 294 + 2;
    assert.deepEqual({ ...motif.commaTarget, right: 0 }, { left: centre - 22, right: 0, top: 156, bottom: 200 });
    assert.equal(motif.commaTarget.right, (centre + 316) / 2, "a close motif's target shares the room between them");
    assert.equal(near.commaTarget.left, (centre + 316) / 2);
    const [own] = placeMarks([spot({ tap: "comma" })], none, sizes);
    assert.deepEqual(own.commaTarget, { left: 94 - 22, right: 94 + 22, top: 156, bottom: 200 }, "a phrase's comma takes its first motif's tap");
    assert.equal(own.numberTarget, null);
    assert.equal(placeMarks([spot({})], none, sizes)[0].commaTarget, null, "on the pages a comma takes no tap");
});

test("the starts drawn: one for each tick, the motifs only in focus, a motif at its phrase's start or a found start shown by that comma", () => {
    const phrases = [
        { number: 1, tick: 0, label: "1", motifs: [{ tick: 0, label: "m1" }, { tick: 960, label: "m2" }] },
        { number: 2, tick: 2520, label: "2", found: 2880, motifs: [{ tick: 2520, label: "m3" }, { tick: 2880, label: "m4" }] },
    ];
    const sections = [{ id: "section_1", tick: 0, letter: "A", label: "A" }];
    const page = markStarts(phrases, sections, false);
    assert.deepEqual(page.map((start) => [start.tick, commaOf(start)]), [[0, "comma"], [2520, "comma"], [2880, "comma ghost"]], "no motifs on the pages");
    assert.equal(page[0].section.id, "section_1");
    const card = markStarts(phrases, sections, true);
    assert.deepEqual(card.map((start) => [start.tick, commaOf(start), start.motif?.of, start.motif?.number]), [
        [0, "comma", 1, 1],
        [960, "comma motif", 1, 2],
        [2520, "comma", 2, 1],
        [2880, "comma ghost", 2, 2],
    ], "a motif at its phrase's start takes the phrase's comma, one at a found start the ghost, which still shows");
    assert.equal(card[3].ghost, true);
    assert.equal(commaOf({ tick: 0, section: sections[0] }), null, "a section alone has no comma");
});

test("the phrase playing is the last one that starts at or before the moment", () => {
    const starts = [0, 2880, 5760];
    assert.equal(phraseAt(starts, 0), 0);
    assert.equal(phraseAt(starts, 2879), 0);
    assert.equal(phraseAt(starts, 2880), 1);
    assert.equal(phraseAt(starts, 99999), 2);
    assert.equal(phraseAt([480, 960], 100), -1, "before the first start, none");
    assert.equal(phraseAt([], 100), -1);
});
