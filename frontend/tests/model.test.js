import test from 'node:test';
import assert from 'node:assert/strict';
import { pointerToTime, positionAt, formatTime, activeLyric } from '../src/model.js';
test('seek includes full circular thumb clearance and clamps captured pointer', () => {
  assert.equal(pointerToTime(16, 10, 272, 200), 0);
  assert.equal(pointerToTime(146, 10, 272, 200), 100);
  assert.equal(pointerToTime(276, 10, 272, 200), 200);
  assert.equal(pointerToTime(-100, 10, 272, 200), 0);
  assert.equal(pointerToTime(900, 10, 272, 200), 200);
  assert.equal(pointerToTime(900, 10, 0, 200), 0);
});
test('playback interpolates only while playing and stays in track bounds', () => {
  const state = { position: 50, sampledAt: 1000, playing: true, track: { duration: 100 } };
  assert.equal(positionAt(state, 2500), 51.5);
  assert.equal(positionAt({ ...state, playing: false }, 2500), 50);
  assert.equal(positionAt(state, 999999), 100);
  assert.equal(positionAt(state, 0), 50);
});
test('lyrics seek in either direction and times do not round early', () => {
  const lines = [{ seconds: 1 }, { seconds: 20 }];
  assert.equal(activeLyric(lines, .9), -1); assert.equal(activeLyric(lines, 20), 1); assert.equal(activeLyric(lines, 4), 0);
  assert.equal(formatTime(59.9), '0:59'); assert.equal(formatTime(60), '1:00');
});
