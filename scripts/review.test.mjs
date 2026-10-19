import assert from 'node:assert/strict';
import test from 'node:test';
import { validateResults } from './review.mjs';

const head = 'a'.repeat(40);
const agents = [{ name: 'craft', threshold: 7 }, { name: 'security', threshold: 9 }, { name: 'docs', enabled: false, threshold: 8 }];
const complete = {
  craft: { verdict: 'PASS', score: 8, reviewedCommit: head, reviewer: 'craft-reviewer' },
  security: { verdict: 'PASS', score: 9, reviewedCommit: head, reviewer: 'security-reviewer' },
};

test('complete passing results bind every enabled lens to the reviewed head', () => {
  assert.equal(validateResults(complete, agents, head), complete);
});

test('a partial passing lens and sub-threshold PASS cannot be attested', () => {
  for (const invalid of [
    { craft: complete.craft },
    { ...complete, security: { ...complete.security, score: 0 } },
    { ...complete, security: { ...complete.security, verdict: 'FAIL' } },
    { ...complete, security: { ...complete.security, score: '9' } },
  ]) {
    assert.throws(() => validateResults(invalid, agents, head), /REVIEW_RESULTS_INVALID/);
  }
});

test('stale, anonymous and extra review results are rejected', () => {
  for (const invalid of [
    { ...complete, craft: { ...complete.craft, reviewedCommit: 'b'.repeat(40) } },
    { ...complete, craft: { ...complete.craft, reviewer: '' } },
    { ...complete, docs: { ...complete.craft } },
    null, [], {},
  ]) {
    assert.throws(() => validateResults(invalid, agents, head), /REVIEW_RESULTS_INVALID/);
  }
});
