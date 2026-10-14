import test from 'node:test';
import assert from 'node:assert/strict';
import { pointerToTime, positionAt, formatTime, activeLyric, lyricProgress } from '../src/model.js';
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
test('lyric fill runs about 8 letters/s, 1 to 5 s, never past the next line or the track end', () => {
  const lines = [{ seconds: 10, text: 'a'.repeat(16) }, { seconds: 20, text: 'b'.repeat(80) }, { seconds: 50, text: 'c'.repeat(40) }, { seconds: 52, text: '' }, { seconds: 53, text: 'd'.repeat(24) }];
  const at = (i, position, duration = 60) => lyricProgress(lines, i, position, duration);
  assert.equal(at(0, 9), 0); assert.equal(at(0, 10), 0); assert.equal(at(0, 11), 9.5); assert.equal(at(0, 12), 19); assert.equal(at(0, 15), 19); // 2 s, ends at chars + 3
  assert.equal(at(1, 22.5), 41.5); assert.equal(at(1, 25), 83); // 10 s capped at 5
  assert.equal(at(2, 51), 21.5); assert.equal(at(2, 52), 43); // 5 s capped by the next line at 2
  assert.equal(at(3, 52.5), 2); // empty line is one '♪', at least 1 s
  assert.equal(at(4, 53.75, 54.5), 13.5); assert.equal(at(4, 54.5, 54.5), 27); assert.equal(at(4, 54.5, 0), 13.5); // last line: track end, or 3 s if unknown
  assert.equal(at(-1, 30), 0);
});
