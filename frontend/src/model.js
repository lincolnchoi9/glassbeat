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
// Waveform. Host audio frames are base64 of little-endian Int16 mono samples; returns -1..1.
export function decodePcm(base64) {
  const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0)), view = new DataView(bytes.buffer);
  return Float32Array.from({ length: bytes.length >> 1 }, (_, i) => view.getInt16(i * 2, true) / 32768);
}
// `count` log-spaced bands from 50 Hz to 12 kHz: the peak Hann-windowed FFT magnitude in each, as dBFS mapped −60..0 → 0..1.
export function spectrumBands(samples, rate, count) {
  const n = 2 ** Math.floor(Math.log2(samples.length || 1)), re = new Float64Array(n), im = new Float64Array(n);
  for (let i = 0; i < n; i++) re[i] = samples[i] * (.5 - .5 * Math.cos(2 * Math.PI * i / (n - 1)));
  for (let i = 1, j = 0; i < n; i++) { // bit-reversal permutation, then iterative radix-2 butterflies
    let bit = n >> 1;
    for (; j & bit; bit >>= 1) j ^= bit;
    j ^= bit;
    if (i < j) { [re[i], re[j]] = [re[j], re[i]]; [im[i], im[j]] = [im[j], im[i]]; }
  }
  for (let size = 2; size <= n; size *= 2) for (let start = 0; start < n; start += size) for (let k = 0; k < size / 2; k++) {
    const a = start + k, b = a + size / 2, c = Math.cos(-2 * Math.PI * k / size), s = Math.sin(-2 * Math.PI * k / size);
    const tr = re[b] * c - im[b] * s, ti = re[b] * s + im[b] * c;
    re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
  }
  const bin = hz => hz * n / rate, top = n / 2 - 1;
  return Array.from({ length: count }, (_, band) => {
    const lo = 50 * 240 ** (band / count), hi = 50 * 240 ** ((band + 1) / count);
    let from = Math.ceil(bin(lo)), to = Math.ceil(bin(hi)) - 1;
    if (to < from) from = to = Math.round(bin(Math.sqrt(lo * hi))); // a low band narrower than one bin takes its nearest bin
    let peak = 0;
    for (let k = clamp(from, 1, top); k <= clamp(to, 1, top); k++) peak = Math.max(peak, Math.hypot(re[k], im[k]));
    return clamp((20 * Math.log10(peak / (n / 4)) + 60) / 60, 0, 1); // a full-scale sine peaks at n/4 under the Hann window
  });
}
// RMS loudness in dBFS, mapped −50..0 → 0..1.
export function level(samples) {
  let sum = 0;
  for (const s of samples) sum += s * s;
  return clamp((10 * Math.log10(sum / (samples.length || 1)) + 50) / 50, 0, 1);
}
