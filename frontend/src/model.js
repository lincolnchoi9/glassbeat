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
