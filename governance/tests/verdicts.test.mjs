// Tests of .github/scripts/aom-verdicts.mjs (Task Record #300, criteria 1-4): node --test "governance/tests/*.test.mjs".

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CONTEXT, evaluate, parseVerdict } from '../../.github/scripts/aom-verdicts.mjs';

const A = 'a'.repeat(40);
const B = '0123456789abcdef0123456789abcdef01234567';
let id = 0;
const comment = (body, at, association = 'OWNER') => ({ id: ++id, created_at: at, author_association: association, body });
const review = (verdict, sha, at, association) => comment(`<!-- aom:review -->\nverdict: ${verdict}\nsha: ${sha}\nblocking: 0\nnon_blocking: 1\n\n1. [non-blocking][style] x`, at, association);
const qa = (verdict, sha, at, association) => comment(`<!-- aom:qa -->\nverdict: ${verdict}\nsha: ${sha}\ncriteria: 3 / pass 3 / fail 0 / not-checked 0\n\n1. [pass] ...`, at, association);

test('the status context is aom-verdicts', () => assert.equal(CONTEXT, 'aom-verdicts'));

test('parses the verdict comments the roles post, ignores others', () => {
  assert.deepEqual(parseVerdict(review('approve', A, '1').body), { kind: 'review', verdict: 'approve', sha: A });
  assert.deepEqual(parseVerdict(qa('fail', B, '1').body), { kind: 'qa', verdict: 'fail', sha: B });
  assert.deepEqual(parseVerdict(`\r\n<!-- aom:qa -->\r\nverdict: pass\r\nsha: ${B.toUpperCase()}\r\n`), { kind: 'qa', verdict: 'pass', sha: B });
  assert.equal(parseVerdict('Looks good to me'), null);
  assert.equal(parseVerdict(''), null);
  // The marker quoted inside other text, or a fenced example, is not a verdict.
  assert.equal(parseVerdict(`The format is:\n<!-- aom:review -->\nverdict: approve\nsha: ${A}`), null);
  assert.equal(parseVerdict(`\`\`\`\n<!-- aom:review -->\nverdict: approve\nsha: ${A}\n\`\`\``), null);
  // Only the header (up to the first blank line) counts.
  assert.deepEqual(parseVerdict(`<!-- aom:review -->\n\nverdict: approve\nsha: ${A}`), { kind: 'review', verdict: '', sha: '' });
});

test('criterion 1: success only with approve and pass for the head; otherwise says what is missing', () => {
  assert.deepEqual(evaluate([], A), { state: 'pending', description: 'waiting for Reviewer and QA on aaaaaaa' });
  assert.deepEqual(evaluate([review('approve', A, '1')], A), { state: 'pending', description: 'waiting for QA on aaaaaaa' });
  assert.deepEqual(evaluate([qa('pass', A, '1')], A), { state: 'pending', description: 'waiting for Reviewer on aaaaaaa' });
  assert.deepEqual(evaluate([review('approve', A, '1'), qa('pass', A, '2')], A), { state: 'success', description: 'Reviewer approve and QA pass on aaaaaaa' });
  assert.deepEqual(evaluate([review('changes-requested', A, '1')], A), { state: 'failure', description: 'Reviewer changes-requested; waiting for QA on aaaaaaa' });
  assert.deepEqual(evaluate([review('approve', A, '1'), qa('fail', A, '2')], A), { state: 'failure', description: 'QA fail on aaaaaaa' });
});

test('criterion 1: only OWNER, MEMBER or COLLABORATOR comments count', () => {
  for (const association of ['NONE', 'CONTRIBUTOR', 'FIRST_TIME_CONTRIBUTOR', null]) {
    assert.equal(evaluate([review('approve', A, '1', association), qa('pass', A, '2', association)], A).state, 'pending');
  }
  for (const association of ['OWNER', 'MEMBER', 'COLLABORATOR']) {
    assert.equal(evaluate([review('approve', A, '1', association), qa('pass', A, '2', association)], A).state, 'success');
  }
});

test('criterion 1: a template value or an unknown verdict is not a verdict that passes', () => {
  const s = evaluate([review('approve | changes-requested', A, '1'), qa('pass | fail', A, '2')], A);
  assert.deepEqual(s, { state: 'failure', description: 'Reviewer verdict unreadable; QA verdict unreadable on aaaaaaa' });
  assert.equal(evaluate([review('approved', A, '1'), qa('pass', A, '2')], A).state, 'failure');
  assert.equal(evaluate([review('', A, '1'), qa('pass', A, '2')], A).state, 'failure');
});

test('criterion 2: a sha that is not the full current head counts for nothing', () => {
  for (const sha of [A.slice(0, 7), A.slice(0, 39), `${A}0`, B, '<full SHA>', '']) {
    assert.equal(evaluate([review('approve', sha, '1'), qa('pass', sha, '2')], A).state, 'pending', sha);
  }
  // Case does not matter, the full SHA does.
  assert.equal(evaluate([review('approve', A.toUpperCase(), '1'), qa('pass', A, '2')], A.toUpperCase()).state, 'success');
});

test('criterion 2: after a new commit the head waits for both verdicts again', () => {
  const onA = [review('approve', A, '1'), qa('pass', A, '2')];
  assert.equal(evaluate(onA, A).state, 'success');
  assert.deepEqual(evaluate(onA, B), { state: 'pending', description: 'waiting for Reviewer and QA on 0123456' });
  assert.deepEqual(evaluate([...onA, review('approve', B, '3')], B), { state: 'pending', description: 'waiting for QA on 0123456' });
});

test('criterion 3: a later changes-requested or fail for the same SHA turns success back', () => {
  const ok = [review('approve', A, '1'), qa('pass', A, '2')];
  assert.equal(evaluate([...ok, review('changes-requested', A, '3')], A).state, 'failure');
  assert.equal(evaluate([...ok, qa('fail', A, '3')], A).state, 'failure');
  // And a later approve after changes-requested passes again; order is by time, not by list order.
  assert.equal(evaluate([qa('pass', A, '4'), review('approve', A, '3'), review('changes-requested', A, '1')], A).state, 'success');
  // A verdict about another commit does not change the head's status.
  assert.equal(evaluate([...ok, review('changes-requested', B, '3')], A).state, 'success');
});

test('criterion 3: an edited or deleted verdict is re-evaluated from the comments as they are now', () => {
  const r = review('approve', A, '1');
  const q = qa('pass', A, '2');
  assert.equal(evaluate([r, q], A).state, 'success');
  assert.equal(evaluate([r, { ...q, body: q.body.replace('verdict: pass', 'verdict: fail') }], A).state, 'failure');
  assert.equal(evaluate([r, { ...q, body: 'withdrawn' }], A).state, 'pending');
  assert.equal(evaluate([r], A).state, 'pending');
});

test('criterion 4: light path, one thread posts both on one SHA; a delta re-check posts both on the new SHA', () => {
  const thread = [comment('Starting review', '0'), review('changes-requested', A, '1'), qa('fail', A, '2')];
  assert.equal(evaluate(thread, A).state, 'failure');
  const recheck = [...thread, review('approve', B, '3'), qa('pass', B, '4')];
  assert.equal(evaluate(recheck, B).state, 'success');
  assert.equal(evaluate(recheck, A).state, 'failure');
});
