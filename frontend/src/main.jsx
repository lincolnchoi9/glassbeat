import React, { useState, useEffect, useLayoutEffect, useRef, useCallback, memo } from 'react';
import { createRoot } from 'react-dom/client';
import LiquidGlass from 'liquid-glass-react';
import { Play, Pause, SkipBack, SkipForward, Captions, Maximize2, Gamepad2, Settings2, Music2, RefreshCw } from 'lucide-react';
import { pointerToTime, positionAt, formatTime, activeLyric, lyricProgress, clamp, decodePcm, spectrumBands, level } from './model';
import { version } from '../package.json';
import './style.css';

const native = window.chrome?.webview;
const send = (type, detail = {}) => native?.postMessage({ type, ...detail });
const osReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
// Visible heights. FRAME, the tallest, is the host's fixed window frame (NativeWindow.FrameHeight): card + hover row + Settings.
const CARD = 156, EXTRAS = 44, DRAWER = { lyrics: 208, settings: 280 }, FRAME = CARD + EXTRAS + DRAWER.settings;
// A closed drawer stays mounted while the card collapses over it: the 520 ms --spring height transition in style.css.
const SETTLE = 520;
const initial = {
  track: null, position: 0, sampledAt: Date.now(), playing: false, gameMode: false, moving: false,
  canPlay: false, canPause: false, canNext: false, canPrevious: false, canSeek: false,
  lyrics: { lines: [], plain: '', status: 'Play a song to see lyrics' },
  preferences: { scale: 1, opacity: .64, theme: 'Album', corners: 22, lyricsSize: 18, lyricsOffset: 0, lyricsOpen: false, reducedMotion: false, hotkey: 'G', waveform: false, waveformStyle: 'Bars' },
  status: 'Open Spotify to get started', artwork: null, settingsOpen: false, startup: false, lyricsStatus: '', generalStatus: '', waveformStatus: ''
};
// The newest host audio frame, about 30 a second while the waveform is on. Kept out of React state so frames never re-render App.
const BANDS = 14, audio = { bands: new Array(BANDS).fill(0), level: 0, at: -Infinity, wake: null };

function Button({ label, icon: Icon, onClick, disabled, active, primary }) {
  return <button type="button" className={`icon-button ${primary ? 'primary' : ''} ${active ? 'selected' : ''}`} aria-label={label} title={label} disabled={disabled} onClick={onClick}><Icon size={primary ? 21 : 16} strokeWidth={1.65} fill={primary ? 'currentColor' : 'none'} /></button>;
}

const Seek = memo(function Seek({ state, onScrub }) {
  // `position` is what the labels show; it changes only when a whole-second label does. The bar follows `live` through --progress.
  const [position, setPosition] = useState(() => positionAt(state));
  const bar = useRef(null), live = useRef(position), labels = useRef('');
  const drag = useRef(null);
  const pending = useRef(null);
  const current = useRef(state); current.current = state;
  const key = state.track?.key;
  const show = value => {
    const duration = current.current.track?.duration || 0;
    live.current = value;
    bar.current?.style.setProperty('--progress', clamp(value / (duration || 1), 0, 1));
    const text = `${formatTime(value)}/${formatTime(duration - value)}`;
    if (text !== labels.current) { labels.current = text; setPosition(value); }
  };
  useEffect(() => { drag.current = null; pending.current = null; show(positionAt(state)); onScrub(false); }, [key]);
  useEffect(() => {
    if (state.seekRejected) pending.current = null;
    if (pending.current && Math.abs(positionAt(state) - pending.current.value) < 1) pending.current = null;
    if (drag.current === null && !pending.current) show(positionAt(state));
  }, [state]);
  useEffect(() => {
    let frame;
    const update = () => {
      if (pending.current && Date.now() > pending.current.until) pending.current = null;
      if (drag.current === null) show(pending.current?.value ?? positionAt(current.current));
      if (current.current.playing || pending.current) frame = requestAnimationFrame(update);
    };
    update(); return () => cancelAnimationFrame(frame);
  }, [state.playing, state.sampledAt]);
  const commit = value => {
    const target = clamp(value, 0, state.track?.duration || 0);
    pending.current = { value: target, until: Date.now() + 1800 };
    show(target); send('seek', { position: target });
    // Paused playback also needs a bounded fallback if the session rejects silently.
    setTimeout(() => { if (pending.current && Date.now() >= pending.current.until) { pending.current = null; show(positionAt(current.current)); } }, 1850);
  };
  const locate = e => { const rect = e.currentTarget.getBoundingClientRect(); return pointerToTime(e.clientX, rect.left, rect.width, state.track?.duration || 0); };
  return <div className="timeline">
    <div ref={bar} className={`seek ${state.canSeek ? '' : 'disabled'}`} role="slider" tabIndex={state.canSeek ? 0 : -1} aria-label="Playback position" aria-valuemin={0} aria-valuemax={state.track?.duration || 0} aria-valuenow={Math.round(position)} aria-valuetext={formatTime(position)} aria-disabled={!state.canSeek}
      onPointerDown={e => { if (!state.canSeek || e.button !== 0) return; e.preventDefault(); e.currentTarget.setPointerCapture(e.pointerId); drag.current = locate(e); onScrub(true); show(drag.current); }}
      onPointerMove={e => { if (drag.current === null) return; drag.current = locate(e); show(drag.current); }}
      onPointerUp={e => { if (drag.current === null) return; const value = locate(e); drag.current = null; e.currentTarget.releasePointerCapture(e.pointerId); onScrub(false); commit(value); }}
      onLostPointerCapture={() => { if (drag.current !== null) { drag.current = null; onScrub(false); show(positionAt(state)); } }}
      onKeyDown={e => { const at = live.current, value = { ArrowLeft: at - 5, ArrowDown: at - 5, ArrowRight: at + 5, ArrowUp: at + 5, Home: 0, End: state.track?.duration || 0 }[e.key]; if (state.canSeek && value !== undefined) { e.preventDefault(); commit(value); } }}>
      <div className="seek-track"><div className="seek-fill"/><span className="seek-thumb"/></div>
    </div>
    <div className="time-labels"><span>{formatTime(position)}</span><span className="mode-label">{state.status?.startsWith('SEEK') ? state.status : state.gameMode ? 'GAME MODE' : state.playing ? 'NOW PLAYING' : state.track ? 'PAUSED' : 'PRISMPLAY'}</span><span>−{formatTime((state.track?.duration || 0) - position)}</span></div>
  </div>;
});

const Lyrics = memo(function Lyrics({ state }) {
  const { lines, plain, status } = state.lyrics;
  const reduced = state.preferences.reducedMotion || osReducedMotion.matches;
  const at = () => positionAt(state) + state.preferences.lyricsOffset;
  const [active, setActive] = useState(() => activeLyric(lines, at()));
  const [offset, setOffset] = useState(0);
  const viewport = useRef(null), list = useRef(null);
  // Each frame while playing: the current line, and --p (letters revealed, read by style.css) written straight onto its <p>.
  // A layout effect, so --p is in place before the first paint of a newly current line.
  useLayoutEffect(() => {
    let frame;
    const update = () => {
      const position = at(), index = activeLyric(lines, position);
      setActive(index);
      if (!reduced) list.current?.children[index]?.style.setProperty('--p', lyricProgress(lines, index, position, state.track?.duration));
      if (state.playing && lines.length) frame = requestAnimationFrame(update);
    };
    update();
    return () => cancelAnimationFrame(frame);
  }, [state, lines]);
  useLayoutEffect(() => {
    if (!viewport.current || !list.current) return;
    const center = () => {
      const line = list.current?.children[Math.max(0, active)];
      if (line && viewport.current) setOffset(viewport.current.clientHeight / 2 - line.offsetTop - line.offsetHeight / 2);
    };
    center();
    const observer = new ResizeObserver(center);
    observer.observe(viewport.current); observer.observe(list.current);
    return () => observer.disconnect();
  }, [active, lines, state.preferences.lyricsSize]);
  return <section className="lyrics" style={{ fontSize: state.preferences.lyricsSize }} aria-label="Lyrics">
    {!lines.length && <div className="lyrics-heading"><div className="lyrics-label">{plain ? 'Unsynced' : status.replace(/ · LRCLIB/g, '')}</div>{!plain && !status.includes('Looking') && <Button label="Retry lyrics" icon={RefreshCw} onClick={() => send('retryLyrics')}/>}</div>}
    {lines.length ? <div className="lyrics-viewport" ref={viewport}><div className="lyric-lines" ref={list} style={{ transform: `translate3d(0, ${offset}px, 0)` }}>
      {lines.map((line, i) => <p key={`${line.seconds}-${i}`} data-active={i === active} className={i === active ? 'current-line' : ''}>
        {i === active && !reduced ? Array.from(line.text || '♪', (letter, n) => <span key={n} style={{ '--i': n }}>{letter}</span>) : line.text || '♪'}</p>)}
    </div></div> : <div className="plain-lyrics">{plain || (status.includes('Looking') ? 'Finding your lyrics…' : status.startsWith('Instrumental') ? '♪' : 'Lyrics aren’t available for this track yet.')}</div>}
  </section>;
});

// Pointer moves re-render only this glass shell. The card content arrives as an unchanged `children` element, so React skips it.
// No top/left here: the library's edge overlays default to 50%/50% (centred, stage-sized in CSS); .liquid-card pins the card to the top.
function Glass({ follow, corners, children }) {
  const [mouse, setMouse] = useState({ x: 0, y: 0, offsetX: 0, offsetY: 0 });
  follow.current = setMouse;
  return <LiquidGlass className="liquid-card" mode="standard" padding="0" cornerRadius={corners} displacementScale={18} blurAmount={.12} saturation={125} aberrationIntensity={.25} elasticity={0}
    globalMousePos={{ x: mouse.x, y: mouse.y }} mouseOffset={{ x: mouse.offsetX, y: mouse.offsetY }} style={{ position: 'absolute', width: '100%' }}>{children}</LiquidGlass>;
}

const setPreference = key => value => send('setPreference', { key, value });
const percent = value => `${Math.round(value * 100)}%`;
const offset = value => `${value > 0 ? '+' : value < 0 ? '−' : ''}${Math.abs(value)} s`;

// Tabs (Settings sections) or radios (Tint). Arrow keys, Home and End move the selection; the pill thumb slides under it.
function Segmented({ label, options, value, onChange, tabs }) {
  const index = Math.max(0, options.indexOf(value)), buttons = useRef([]);
  const keys = e => {
    const next = { ArrowLeft: index - 1, ArrowUp: index - 1, ArrowRight: index + 1, ArrowDown: index + 1, Home: 0, End: options.length - 1 }[e.key];
    if (next === undefined) return;
    e.preventDefault();
    const i = (next + options.length) % options.length;
    onChange(options[i]); buttons.current[i]?.focus();
  };
  return <div className="segmented" role={tabs ? 'tablist' : 'radiogroup'} aria-label={label} style={{ '--count': options.length, '--index': index }} onKeyDown={keys}>
    {options.map((option, i) => <button key={option} ref={el => { buttons.current[i] = el; }} type="button" role={tabs ? 'tab' : 'radio'} tabIndex={i === index ? 0 : -1}
      {...tabs ? { id: `tab-${option}`, 'aria-controls': `pane-${option}`, 'aria-selected': i === index } : { 'aria-checked': i === index }} onClick={() => onChange(option)}>{option}</button>)}
  </div>;
}

// Live sliders apply on every input. `release` sliders (Scale) apply on the native change event, which fires on pointer release
// and per keyboard step, so the panel does not rescale under the pointer; the label follows the drag meanwhile.
function Slider({ label, min, max, step, value, format, onChange, release }) {
  const [draft, setDraft] = useState(null), input = useRef(null);
  const shown = draft ?? value;
  useEffect(() => setDraft(null), [value]);
  useEffect(() => {
    if (!release) return;
    const el = input.current, commit = () => onChange(+el.value);
    el.addEventListener('change', commit);
    return () => el.removeEventListener('change', commit);
  });
  return <div className="row slider-row"><span>{label}</span><span className="value" aria-hidden="true">{format(shown)}</span>
    <input ref={input} className="range" type="range" min={min} max={max} step={step} value={shown} aria-label={label} aria-valuetext={format(shown)} style={{ '--fill': (shown - min) / (max - min) }}
      onChange={e => { const v = +e.target.value; setDraft(v); if (!release) onChange(v); }}/>
  </div>;
}

function Switch({ label, checked, onChange }) {
  return <div className="row"><span>{label}</span><button type="button" className="switch" role="switch" aria-checked={!!checked} aria-label={label} onClick={() => onChange(!checked)}><span/></button></div>;
}

// Click to record; the next letter or digit is sent to the host, which registers it. Esc cancels (and does not close Settings).
function Shortcut({ hotkey, mac }) {
  const [recording, setRecording] = useState(false);
  useEffect(() => {
    if (!recording) return;
    const key = e => {
      if (e.key === 'Escape') setRecording(false);
      else if (/^[a-z0-9]$/i.test(e.key)) { setRecording(false); send('setHotkey', { key: e.key.toUpperCase() }); }
      else return;
      e.preventDefault(); e.stopImmediatePropagation();
    };
    window.addEventListener('keydown', key, true);
    return () => window.removeEventListener('keydown', key, true);
  }, [recording]);
  return <div className="row"><span>Game mode shortcut</span>
    <button type="button" className={`keys ${recording ? 'recording' : ''}`} aria-label={recording ? 'Press a letter or number' : `Game mode shortcut ${mac ? 'Control+Option' : 'Ctrl+Alt'}+${hotkey}, change`} onClick={() => setRecording(true)} onBlur={() => setRecording(false)}>
      {recording ? 'Press a key…' : <><kbd>{mac ? '⌃' : 'Ctrl'}</kbd><kbd>{mac ? '⌥' : 'Alt'}</kbd><kbd>{hotkey}</kbd></>}
    </button></div>;
}

// Beside the title. Each animation frame eases toward `audio` (fast attack, slow decay); 250 ms after the last frame it eases to a
// flat rest and the loop stops until the next frame wakes it. The colour is the canvas's CSS colour, so it follows the tint.
const W = 64, H = 28;
function Waveform({ style, scale, theme }) {
  const canvas = useRef(null);
  useEffect(() => {
    const el = canvas.current, ctx = el.getContext('2d'), css = getComputedStyle(el), k = devicePixelRatio * scale;
    el.width = Math.round(W * k); el.height = Math.round(H * k); ctx.scale(k, k);
    const bars = new Array(BANDS).fill(0), phase = [0, 0, 0], history = new Array(W / 2).fill(0);
    const ease = (from, to) => from + (to - from) * (to > from ? .5 : .12), mean = (a, b) => bars.slice(a, b).reduce((x, y) => x + y) / (b - a);
    let loud = 0, frame = 0, last = performance.now(), pushed = 0;
    const draw = now => {
      const live = now - audio.at < 250, dt = Math.min(.05, (now - last) / 1000);
      last = now;
      bars.forEach((v, i) => { bars[i] = ease(v, live ? audio.bands[i] : 0); });
      loud = ease(loud, live ? audio.level : 0);
      if (now - pushed >= 33) { pushed = now; history.push(loud); history.shift(); }
      if (!live) history.forEach((v, i) => { history[i] = v * .88; });
      ctx.clearRect(0, 0, W, H); ctx.fillStyle = ctx.strokeStyle = css.color; ctx.beginPath();
      if (style === 'Wave') {
        // Bass, mid and treble lines, tapered to the ends; they drift faster as the music gets louder.
        ctx.lineWidth = 1.5;
        [[1.5, 1, mean(0, 5)], [2.2, .55, mean(5, 10)], [3, .3, mean(10, BANDS)]].forEach(([cycles, alpha, amp], i) => {
          phase[i] += dt * (1.5 + 10 * loud) * (1 + i * .4);
          ctx.globalAlpha = alpha; ctx.beginPath();
          for (let x = 0; x <= W; x++) ctx.lineTo(x, H / 2 + amp * (H / 2 - 1) * Math.sin(Math.PI * x / W) * Math.sin(2 * Math.PI * cycles * x / W + phase[i]));
          ctx.stroke();
        });
      } else if (style === 'Strip') {
        history.forEach((v, i) => { const h = Math.max(1, v * H); ctx.globalAlpha = Math.min(1, (i + 1) / (history.length * .4)); ctx.fillRect(i * 2 + 1, (H - h) / 2, 1, h); });
      } else {
        const pitch = W / BANDS, width = pitch * .6;
        bars.forEach((v, i) => { const h = Math.max(2, v * H); ctx.roundRect(i * pitch + (pitch - width) / 2, (H - h) / 2, width, h, width / 2); });
        ctx.fill();
      }
      ctx.globalAlpha = 1;
      const rest = !live && Math.max(loud, ...bars, ...history) < .01;
      frame = rest ? 0 : requestAnimationFrame(draw);
    };
    audio.wake = () => { if (!frame) { last = performance.now(); frame = requestAnimationFrame(draw); } };
    frame = requestAnimationFrame(draw);
    return () => { cancelAnimationFrame(frame); audio.wake = null; };
  }, [style, scale, theme]);
  return <canvas ref={canvas} className="waveform" aria-hidden="true"/>;
}

const SECTIONS = ['Appearance', 'Lyrics', 'General'];
function Settings({ state }) {
  const [section, setSection] = useState(SECTIONS[0]);
  const prefs = state.preferences, mac = state.platform === 'mac', reduced = prefs.reducedMotion || osReducedMotion.matches;
  return <section className="settings" aria-label="Settings">
    <Segmented tabs label="Settings sections" options={SECTIONS} value={section} onChange={setSection}/>
    <div className="pane" role="tabpanel" id={`pane-${section}`} aria-labelledby={`tab-${section}`}>
      {section === 'Appearance' && <>
        <div className="group">
          <Slider label="Scale" min={.75} max={2} step={.05} value={prefs.scale} format={percent} onChange={setPreference('scale')} release/>
          <Slider label="Opacity" min={.15} max={.95} step={.05} value={prefs.opacity} format={percent} onChange={setPreference('opacity')}/>
          <div className="row"><span>Tint</span><Segmented label="Tint" options={['Album', 'Dark', 'Light']} value={prefs.theme} onChange={setPreference('theme')}/></div>
          <Slider label="Corners" min={0} max={32} step={1} value={prefs.corners} format={String} onChange={setPreference('corners')}/>
        </div>
        <div className="group"><Switch label="Reduced motion" checked={prefs.reducedMotion} onChange={setPreference('reducedMotion')}/></div>
        <div className="group">
          <Switch label="Waveform" checked={prefs.waveform} onChange={setPreference('waveform')}/>
          {prefs.waveform && <div className="row"><span>Style</span><Segmented label="Waveform style" options={['Bars', 'Wave', 'Strip']} value={prefs.waveformStyle} onChange={setPreference('waveformStyle')}/></div>}
        </div>
        {state.waveformStatus && <p className="note status" role="status">{state.waveformStatus}</p>}
        {prefs.waveform && reduced && <p className="note">The waveform is paused while Reduced motion is on.</p>}
        <p className="note">Reads Spotify’s audio on this device to draw the wave. Nothing is recorded or sent.</p>
        {mac && <p className="note">If it stays flat, allow PrismPlay in System Settings › Privacy &amp; Security › Screen &amp; System Audio Recording.</p>}
      </>}
      {section === 'Lyrics' && <>
        <div className="group">
          <Slider label="Text size" min={12} max={28} step={1} value={prefs.lyricsSize} format={String} onChange={setPreference('lyricsSize')}/>
          <Slider label="Timing offset" min={-10} max={10} step={.25} value={prefs.lyricsOffset} format={offset} onChange={setPreference('lyricsOffset')}/>
        </div>
        <p className="note">Positive offsets show lyrics earlier.</p>
        <button type="button" className="pill" onClick={() => send('importLyrics')}>Import lyrics…</button>
        {state.lyricsStatus && <p className="note status" role="status">{state.lyricsStatus}</p>}
        <p className="note">Lyrics load automatically. Track details are shared with LRCLIB to find a match.</p>
      </>}
      {section === 'General' && <>
        <div className="group">
          <Switch label={mac ? 'Open at login' : 'Start with Windows'} checked={state.startup} onChange={enabled => send('startup', { enabled })}/>
          <Shortcut hotkey={prefs.hotkey} mac={mac}/>
        </div>
        {state.generalStatus && <p className="note status" role="status">{state.generalStatus}</p>}
        <p className="note">Game mode locks the overlay and lets clicks pass through. Use the {mac ? 'menu bar' : 'tray'} icon to unlock it anytime.</p>
        <p className="note footer">PrismPlay {version} · No account. No telemetry. Saved on this {mac ? 'Mac' : 'PC'}.</p>
      </>}
    </div>
  </section>;
}

// Drawer layers: the open one, plus one closed less than SETTLE ago, which stays mounted while the card collapses over it or
// cross-fades under the other. Reduced motion keeps only the open one.
function useLayers(open, reduced) {
  const [layers, setLayers] = useState([]);
  if (open && !layers.includes(open)) setLayers([...layers, open]);
  useEffect(() => {
    const timer = setTimeout(() => setLayers(open ? [open] : []), reduced ? 0 : SETTLE);
    return () => clearTimeout(timer);
  }, [open, reduced]);
  return reduced ? (open ? [open] : []) : layers;
}

function App() {
  const [state, setState] = useState(initial);
  const [hover, setHover] = useState(false);
  const [focused, setFocused] = useState(false);
  const [dragging, setDragging] = useState(false);
  const [scrubbing, setScrubbing] = useState(false);
  const stage = useRef(null), collapse = useRef(), frame = useRef(), dragRef = useRef(false), follow = useRef(), sent = useRef({});
  const prefs = state.preferences;
  const reduced = prefs.reducedMotion || osReducedMotion.matches;
  // One drawer slot below the hover row: Settings while open, else lyrics. Settings keeps the hover row out.
  const drawer = state.settingsOpen ? 'settings' : prefs.lyricsOpen ? 'lyrics' : null;
  const expanded = !state.gameMode && (hover || focused || dragging || scrubbing || state.settingsOpen);
  const height = CARD + (expanded ? EXTRAS : 0) + (drawer ? DRAWER[drawer] : 0);
  const layers = useLayers(drawer, reduced);
  const connected = state !== initial;
  // The macOS host adds platform: 'mac'; Windows sends no platform field.
  const mac = state.platform === 'mac';
  useEffect(() => { document.documentElement.dataset.platform = mac ? 'mac' : 'windows'; }, [mac]);
  useEffect(() => {
    const receive = e => {
      if (e.data.type === 'snapshot') setState(e.data.state);
      else if (e.data.type === 'audio') {
        const samples = decodePcm(e.data.pcm);
        Object.assign(audio, { bands: spectrumBands(samples, e.data.rate, BANDS), level: level(samples), at: performance.now() }).wake?.();
      }
    };
    native?.addEventListener('message', receive); send('ready');
    return () => native?.removeEventListener('message', receive);
  }, []);
  // One size message per change of the visible height. The host's transparent frame stays FRAME tall; it sizes its hit-test region
  // from this, and near the screen bottom moves the window with the curve .stage uses (--spring). The first message and reduced
  // motion snap. Nothing is posted before the first snapshot; the host starts at the collapsed height.
  useLayoutEffect(() => {
    const last = sent.current;
    if (!connected || height === last.height && prefs.scale === last.scale) return;
    sent.current = { height, scale: prefs.scale };
    send('size', { height: Math.ceil(height * prefs.scale), animate: !reduced && last.height !== undefined });
  }, [connected, height, prefs.scale, reduced]);
  useEffect(() => {
    if (!state.settingsOpen) return;
    const close = e => { if (e.key === 'Escape') send('closeSettings'); };
    window.addEventListener('keydown', close);
    return () => window.removeEventListener('keydown', close);
  }, [state.settingsOpen]);
  useEffect(() => { document.documentElement.style.zoom = prefs.scale; document.documentElement.dataset.theme = prefs.theme.toLowerCase(); }, [prefs.scale, prefs.theme]);
  useEffect(() => { if (state.gameMode) { setHover(false); finishDrag(); } }, [state.gameMode]);
  useEffect(() => () => { clearTimeout(collapse.current); cancelAnimationFrame(frame.current); }, []);
  const finishDrag = () => { if (!dragRef.current) return; dragRef.current = false; setDragging(false); send('dragEnd'); };
  const onScrub = useCallback(value => setScrubbing(value), []);
  return <main className="frame" style={{ height: FRAME, '--radius': `${prefs.corners}px`, '--glass-opacity': prefs.opacity, '--height': `${height}px` }}
    onPointerEnter={() => { clearTimeout(collapse.current); setHover(true); }}
    onPointerLeave={() => { clearTimeout(collapse.current); collapse.current = setTimeout(() => setHover(false), 280); }}
    /* Keyboard focus (not a mouse click's focus) expands like hover, so Tab can reach the actions. */
    onFocusCapture={e => { if (e.target.matches(':focus-visible')) setFocused(true); }}
    onBlur={e => { if (!e.currentTarget.contains(e.relatedTarget)) setFocused(false); }}
    onPointerMove={e => { if (dragRef.current || prefs.reducedMotion || osReducedMotion.matches) return; const x = e.clientX, y = e.clientY; cancelAnimationFrame(frame.current); frame.current = requestAnimationFrame(() => { const rect = stage.current.getBoundingClientRect(); follow.current?.({ x, y, offsetX: (x - rect.left - rect.width / 2) / rect.width * 100, offsetY: (y - rect.top - rect.height / 2) / rect.height * 100 }); }); }}>
    {/* The window is transparent: this rounded card is the whole visible shape, and its height transition is the expansion. */}
    <div ref={stage} className={`stage ${expanded ? 'expanded' : ''} ${prefs.reducedMotion ? 'reduced' : ''} ${dragging ? 'dragging' : ''}`} data-drawer={drawer ?? undefined}>
      <div className="album-atmosphere" style={state.artwork && prefs.theme === 'Album' ? { backgroundImage: `url("${state.artwork}")` } : undefined}/>
      <Glass follow={follow} corners={prefs.corners}>
        <div className="card-content">
          <header className="track-header" onPointerDown={e => { if (state.gameMode || e.button !== 0) return; e.preventDefault(); e.currentTarget.setPointerCapture(e.pointerId); dragRef.current = true; setDragging(true); send('dragBegin'); }} onPointerUp={e => { finishDrag(); if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId); }} onLostPointerCapture={finishDrag} onPointerCancel={finishDrag}>
            <div className="cover">{state.artwork ? <img src={state.artwork} draggable="false" alt="Album artwork"/> : <Music2 size={23}/>}</div>
            <div className="track-copy"><div className="track-title" title={state.track?.title}>{state.track?.title || 'Your music, floating.'}</div><div className="track-artist">{state.track?.artist || state.status}</div></div>
            {prefs.waveform && !reduced && !state.hotkeyError && <Waveform style={prefs.waveformStyle} scale={prefs.scale} theme={prefs.theme}/>}
            {state.hotkeyError && <span className="hotkey-chip" role="status" title={`Another app uses this game mode shortcut. ${mac ? 'Click the PrismPlay menu bar icon' : 'Right-click the PrismPlay tray icon'} and choose Settings to pick another key.`}>{state.hotkeyError}</span>}
          </header>
          <nav className="transport" aria-label="Playback controls"><Button icon={SkipBack} label="Previous track" disabled={!state.canPrevious} onClick={() => send('previous')}/><Button icon={state.playing ? Pause : Play} primary label={state.playing ? 'Pause' : 'Play'} disabled={state.playing ? !state.canPause : !state.canPlay} onClick={() => send(state.playing ? 'pause' : 'play')}/><Button icon={SkipForward} label="Next track" disabled={!state.canNext} onClick={() => send('next')}/></nav>
          <Seek state={state} onScrub={onScrub}/>
          {/* From Settings, Lyrics switches the drawer to lyrics (opening them if needed) rather than toggling them out of sight. */}
          <div className="extras" inert={!expanded}><div className="extra-buttons"><Button icon={Captions} label="Toggle lyrics" active={drawer === 'lyrics'} onClick={() => { if (state.settingsOpen) send('closeSettings'); if (!state.settingsOpen || !prefs.lyricsOpen) send('lyrics'); }}/><Button icon={Maximize2} label="Cycle scale" onClick={() => send('scale')}/><Button icon={Gamepad2} label="Game mode" onClick={() => send('game')}/><Button icon={Settings2} label="Settings" active={state.settingsOpen} onClick={() => send('settings')}/></div></div>
          {/* Laid out at full height at once and revealed by the growing card; layers cross-fade when lyrics and Settings swap. */}
          {layers.length > 0 && <div className="drawer">{['lyrics', 'settings'].filter(name => layers.includes(name)).map(name =>
            <div key={name} className={`layer ${name === drawer ? '' : 'leaving'}`} inert={name !== drawer}>{name === 'lyrics' ? <Lyrics state={state}/> : <Settings state={state}/>}</div>)}</div>}
        </div>
      </Glass>
    </div>
  </main>;
}
createRoot(document.getElementById('root')).render(<App/>);
