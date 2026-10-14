export const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
export function positionAt(snapshot, now = Date.now()) {
  return clamp((snapshot.position || 0) + (snapshot.playing ? Math.max(0, now - snapshot.sampledAt) / 1000 : 0), 0, snapshot.track?.duration || 0);
}
export function pointerToTime(x, left, width, duration) {
  return width <= 12 || duration <= 0 ? 0 : clamp((x - left - 6) / (width - 12), 0, 1) * duration;
}
export function formatTime(seconds) {
  const value = Math.max(0, Math.floor(seconds || 0));
  return `${Math.floor(value / 60)}:${String(value % 60).padStart(2, '0')}`;
}
export function activeLyric(lines, position) {
  let result = -1;
  for (let i = 0; i < lines.length && lines[i].seconds <= position; i++) result = i;
  return result;
}
// Letters of line `index` revealed at `position` (fractional): about 8 per second, 1 to 5 s per line, never past the next line
// (or the track end). Ends at chars + EDGE so the last letters reach full opacity; EDGE is the 3-letter soft edge in style.css.
const EDGE = 3;
export function lyricProgress(lines, index, position, duration) {
  const line = lines[index];
  if (!line) return 0;
  const chars = Array.from(line.text || '♪').length;
  const reveal = Math.min((lines[index + 1]?.seconds ?? (duration || Infinity)) - line.seconds, 5, Math.max(1, chars / 8));
  return reveal > 0 ? clamp((position - line.seconds) / reveal, 0, 1) * (chars + EDGE) : chars + EDGE;
}
