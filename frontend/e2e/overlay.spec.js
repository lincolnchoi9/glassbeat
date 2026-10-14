import { test, expect } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    window.commands = [];
    const listeners = [];
    window.chrome = window.chrome || {};
    window.chrome.webview = {
      addEventListener: (_, handler) => listeners.push(handler), removeEventListener() {},
      postMessage: message => {
        window.commands.push(message);
        if (message.type === 'ready') queueMicrotask(() => window.receive({
          track: { key: 'test', title: 'A little space', artist: 'Your soundtrack', duration: 200 },
          playing: false, position: 50, sampledAt: Date.now(), gameMode: false, moving: false,
          canPlay: true, canPause: true, canNext: true, canPrevious: true, canSeek: true,
          preferences: { scale: 1, opacity: .64, theme: 'Album', corners: 22, lyricsSize: 18, lyricsOffset: 0, lyricsOpen: false, reducedMotion: false, hotkey: 'G' },
          lyrics: { lines: [], plain: '', status: 'Import lyrics' }, artwork: null, status: ''
        }));
      }
    };
    window.receive = patch => {
      window.snapshot = { ...window.snapshot, ...patch };
      listeners.forEach(handler => handler({ data: { type: 'snapshot', state: window.snapshot } }));
    };
    window.emit = data => listeners.forEach(handler => handler({ data }));
  });
  await page.goto('http://127.0.0.1:4176');
  await expect(page.getByText('A little space')).toBeVisible();
});

test('uses upstream glass, centered transport, and a fully visible circular thumb', async ({ page }) => {
  await expect(page.locator('.glass__warp')).toHaveCount(1);
  const header = await page.locator('.track-header').boundingBox();
  const controls = await page.locator('.transport').boundingBox();
  const track = await page.getByRole('slider').boundingBox();
  const thumb = await page.locator('.seek-thumb').boundingBox();
  expect(controls.y).toBeGreaterThanOrEqual(header.y + header.height);
  expect(controls.y + controls.height).toBeLessThanOrEqual(track.y);
  expect(Math.abs(thumb.width - thumb.height)).toBeLessThan(.1);
  expect(thumb.y).toBeGreaterThan(track.y);
  expect(thumb.y + thumb.height).toBeLessThan(track.y + track.height);
});

test('click and captured drag send real seek messages once on release', async ({ page }) => {
  const bar = page.getByRole('slider');
  await bar.click({ position: { x: 135, y: 12 } });
  await expect.poll(() => page.evaluate(() => window.commands.filter(c => c.type === 'seek').length)).toBe(1);
  expect(await page.evaluate(() => window.commands.find(c => c.type === 'seek').position)).toBeCloseTo(100, 0);
  const box = await bar.boundingBox();
  await page.mouse.move(box.x + 40, box.y + 12); await page.mouse.down();
  await page.mouse.move(box.x + box.width + 20, box.y + 12, { steps: 10 });
  expect(await page.evaluate(() => window.commands.filter(c => c.type === 'seek').length)).toBe(1);
  await page.mouse.up();
  await expect.poll(() => page.evaluate(() => window.commands.filter(c => c.type === 'seek').length)).toBe(2);
  expect(await page.evaluate(() => window.commands.filter(c => c.type === 'seek').at(-1).position)).toBe(200);
});

test('drag preserves the glass and delegates window movement without a busy cursor', async ({ page }) => {
  const header = page.locator('.track-header');
  await header.hover();
  const before = await page.locator('.stage').evaluate(el => getComputedStyle(el).backgroundColor);
  const box = await header.boundingBox();
  await page.mouse.move(box.x + 100, box.y + 30); await page.mouse.down();
  await page.mouse.move(box.x + 180, box.y + 40, { steps: 10 });
  await expect(page.locator('.stage')).toHaveClass(/dragging/);
  expect(await page.locator('.stage').evaluate(el => getComputedStyle(el).backgroundColor)).toBe(before);
  expect(await header.evaluate(el => getComputedStyle(el).cursor)).toBe('default');
  await page.mouse.up();
  expect(await page.evaluate(() => window.commands.filter(c => c.type.startsWith('drag')).map(c => c.type))).toEqual(['dragBegin', 'dragEnd']);
});

test('hover reveals extras, playback buttons send commands, and reduced motion is respected', async ({ page }) => {
  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await page.getByRole('button', { name: 'Next track' }).click();
  await page.getByRole('button', { name: 'Previous track' }).click();
  expect(await page.evaluate(() => window.commands.filter(c => ['play', 'next', 'previous'].includes(c.type)).map(c => c.type))).toEqual(['play', 'next', 'previous']);
  await expect(page.locator('.stage')).toHaveClass(/expanded/);
  await page.mouse.move(500, 500); await expect(page.locator('.stage')).not.toHaveClass(/expanded/);
  await page.evaluate(() => window.receive({ preferences: { ...window.snapshot.preferences, reducedMotion: true } }));
  expect(await page.locator('.stage').evaluate(el => getComputedStyle(el).transitionDuration)).toBe('0s');
});

test('game mode hides actions and makes no drag request', async ({ page }) => {
  await page.evaluate(() => window.receive({ gameMode: true }));
  await page.locator('.track-header').click();
  expect(await page.evaluate(() => window.commands.some(c => c.type === 'dragBegin'))).toBe(false);
  await expect(page.locator('.stage')).not.toHaveClass(/expanded/);
});

test('synced lyrics scroll stable lines smoothly on forward and backward seeks', async ({ page }) => {
  await page.evaluate(() => window.receive({
    preferences: { ...window.snapshot.preferences, lyricsOpen: true }, position: 1, sampledAt: Date.now(),
    lyrics: { status: 'Synced lyrics · LRCLIB', plain: '', lines: [{ seconds: 0, text: 'First test line' }, { seconds: 10, text: 'Second test line' }, { seconds: 20, text: 'Third test line' }, { seconds: 30, text: 'Fourth test line' }] }
  }));
  await expect(page.locator('.current-line')).toHaveText('First test line');
  await page.evaluate(() => window.originalLine = document.querySelector('.lyric-lines p'));
  const before = await page.locator('.lyric-lines').getAttribute('style');
  await page.evaluate(() => window.receive({ position: 21, sampledAt: Date.now() }));
  await expect(page.locator('.current-line')).toHaveText('Third test line');
  await expect.poll(() => page.locator('.lyric-lines').getAttribute('style')).not.toBe(before);
  expect(await page.evaluate(() => window.originalLine === document.querySelector('.lyric-lines p'))).toBe(true);
  expect(await page.locator('.lyric-lines').evaluate(el => getComputedStyle(el).transitionDuration)).toBe('0.42s');
  await page.evaluate(() => window.receive({ position: 11, sampledAt: Date.now() }));
  await expect(page.locator('.current-line')).toHaveText('Second test line');
  await page.evaluate(() => window.receive({ preferences: { ...window.snapshot.preferences, reducedMotion: true } }));
  expect(await page.locator('.lyric-lines').evaluate(el => getComputedStyle(el).transitionDuration)).toBe('0s');
});

const sizes = page => page.evaluate(() => window.commands.filter(c => c.type === 'size').map(({ height, animate }) => ({ height, animate })));
const sent = (page, type) => page.evaluate(type => window.commands.filter(c => c.type === type), type);
const clearCommands = page => page.evaluate(() => { window.commands.length = 0; });
const setPrefs = (page, patch) => page.evaluate(patch => window.receive({ preferences: { ...window.snapshot.preferences, ...patch } }), patch);
// Rows of the rendered card in a 1 px screenshot column: real pixels, independent of DOM geometry APIs, with the
// card painted black for the measurement.
const renderedHeight = async page => {
  const style = await page.addStyleTag({ content: '.stage { background: #000 }' });
  const png = await page.screenshot({ clip: { x: 150, y: 0, width: 1, height: 650 } });
  await style.evaluate(el => el.remove());
  return page.evaluate(async src => {
    const img = new Image(); img.src = src; await img.decode();
    const ctx = Object.assign(document.createElement('canvas'), { width: 1, height: img.height }).getContext('2d');
    ctx.drawImage(img, 0, 0);
    const data = ctx.getImageData(0, 0, 1, img.height).data;
    let last = 0; for (let y = 0; y < img.height; y++) if (data[y * 4] < 200) last = y + 1;
    return last;
  }, 'data:image/png;base64,' + png.toString('base64'));
};

const stageHeight = page => page.locator('.stage').evaluate(el => getComputedStyle(el).height);
// Controls in the drawer take pointer input once the card has grown over them.
const openSettings = async page => { await page.evaluate(() => window.receive({ settingsOpen: true })); await expect.poll(() => stageHeight(page)).toBe('480px'); };
// Applies a snapshot patch, then samples in the commit that changes the drawer and on every frame for at least 900 ms and until
// nothing animates or is leaving (capped at 3 s): the card (.stage) height, and the drawer layer's height and opacity (null once
// unmounted). Also returns the card's running transitions.
const flipDrawer = (page, patch) => page.evaluate(patch => new Promise(resolve => {
  const stage = document.querySelector('.stage'), samples = [];
  const sample = start => { const layer = document.querySelector('.layer'); samples.push({ t: performance.now() - start, card: parseFloat(getComputedStyle(stage).height), height: layer ? layer.getBoundingClientRect().height : null, opacity: layer ? +getComputedStyle(layer).opacity : null }); };
  new MutationObserver((_, observer) => {
    observer.disconnect();
    const start = performance.now(); sample(start);
    const transitions = stage.getAnimations().map(a => `${a.transitionProperty} ${a.effect.getTiming().duration} ${getComputedStyle(stage).transitionTimingFunction.slice(0, 7)}`);
    const settled = () => !stage.getAnimations({ subtree: true }).length && !document.querySelector('.layer.leaving');
    const frame = () => { sample(start); const { t } = samples.at(-1); if (t < 3000 && (t < 900 || !settled())) requestAnimationFrame(frame); else resolve({ samples, transitions }); };
    requestAnimationFrame(frame);
  }).observe(stage, { attributeFilter: ['data-drawer'] });
  window.receive({ ...patch, preferences: { ...window.snapshot.preferences, ...patch.preferences } });
}), patch);

test('one hover posts one animated size each way', async ({ page }) => {
  await page.mouse.move(500, 600); await page.waitForTimeout(300);
  await clearCommands(page);
  await page.locator('.track-header').hover();
  await expect.poll(() => stageHeight(page)).toBe('200px');
  await page.mouse.move(500, 600);
  await expect.poll(() => stageHeight(page)).toBe('156px');
  expect(await sizes(page)).toEqual([{ height: 200, animate: true }, { height: 156, animate: true }]);
});

test('opening and closing lyrics posts one size each way; reduced motion snaps', async ({ page }) => {
  await clearCommands(page);
  await setPrefs(page, { lyricsOpen: true });
  await expect.poll(() => sizes(page)).toEqual([{ height: 364, animate: true }]);
  await setPrefs(page, { lyricsOpen: false });
  await expect.poll(() => sizes(page)).toEqual([{ height: 364, animate: true }, { height: 156, animate: true }]);
  await setPrefs(page, { reducedMotion: true }); await clearCommands(page);
  await setPrefs(page, { lyricsOpen: true });
  await expect.poll(() => sizes(page)).toEqual([{ height: 364, animate: false }]);
  await setPrefs(page, { lyricsOpen: false });
  await expect.poll(() => sizes(page)).toEqual([{ height: 364, animate: false }, { height: 156, animate: false }]);
});

test('the lyrics drawer is laid out at once, revealed by the springing card, and unmounts after it collapses', { tag: '@motion' }, async ({ page }) => {
  const opening = await flipDrawer(page, { preferences: { lyricsOpen: true } });
  expect(opening.samples[0]).toMatchObject({ height: 208, card: 156 }); // full height in the commit that opens it; the card grows over it
  expect(opening.samples[0].opacity).toBeLessThan(1);
  expect(opening.transitions).toEqual(['height 520 linear(']);
  expect(opening.samples.some(s => s.card > 156 && s.card < 364)).toBe(true); // a midway frame; headless first frames can be long
  opening.samples.forEach((s, i) => i && expect(s.card).toBeGreaterThanOrEqual(opening.samples[i - 1].card)); // no overshoot
  expect(opening.samples.at(-1)).toMatchObject({ card: 364, height: 208, opacity: 1 });
  const closing = await flipDrawer(page, { preferences: { lyricsOpen: false } });
  expect(closing.samples[0]).toMatchObject({ height: 208, opacity: 1 }); // still mounted when the close arrives
  const gone = closing.samples.findIndex(s => s.height === null);
  expect(gone).toBeGreaterThan(0);
  expect(closing.samples[gone].t).toBeGreaterThanOrEqual(500); // mounted until the spring has settled
  expect(closing.samples.slice(0, gone).some(s => s.opacity > 0 && s.opacity < 1)).toBe(true);
  expect(closing.samples.find(s => s.t >= 200).opacity).toBe(0); // the content fades out quickly
  expect(closing.samples.slice(gone).every(s => s.height === null)).toBe(true);
  expect(closing.samples.at(-1).card).toBe(156);
});

test('with reduced motion the drawer mounts and unmounts in the commit that opens or closes it', async ({ page }) => {
  await setPrefs(page, { reducedMotion: true });
  const opening = await flipDrawer(page, { preferences: { lyricsOpen: true } }), closing = await flipDrawer(page, { preferences: { lyricsOpen: false } });
  expect(opening.samples[0]).toMatchObject({ card: 364, height: 208, opacity: 1 });
  expect(opening.transitions).toEqual([]);
  expect(closing.samples[0]).toMatchObject({ card: 156, height: null });
});

test('every overlay button shares one transition and has hover, press, selected and focus states', async ({ page }) => {
  await page.evaluate(() => window.receive({ preferences: { ...window.snapshot.preferences, lyricsOpen: true }, lyrics: { status: 'Lyrics unavailable · try again later', plain: '', lines: [] } }));
  await expect(page.getByRole('button', { name: 'Retry lyrics' })).toBeVisible();
  const transitions = await page.locator('button').evaluateAll(buttons => buttons.map(b => { const s = getComputedStyle(b); return [s.transitionProperty, s.transitionDuration, s.transitionTimingFunction].join(' | '); }));
  expect(transitions).toHaveLength(8);
  expect(new Set(transitions).size, transitions.join('\n')).toBe(1);
  const [properties, durations, easings] = transitions[0].split(' | ').map(v => v.split(/,\s*(?![^(]*\))/));
  for (const property of ['transform', 'background-color', 'box-shadow']) expect(properties).toContain(property);
  expect(new Set(durations).size).toBe(1); expect(new Set(easings).size).toBe(1);
  await page.keyboard.press('Tab');
  expect(await page.evaluate(() => getComputedStyle(document.activeElement).outlineStyle)).toBe('solid');
  const lyricsButton = page.getByRole('button', { name: 'Toggle lyrics' });
  const look = locator => locator.evaluate(el => { const s = getComputedStyle(el); return { scale: new DOMMatrix(s.transform).a, background: s.backgroundColor }; });
  await expect.poll(async () => (await look(lyricsButton)).background).toBe('rgba(255, 255, 255, 0.094)'); // selected while lyrics are open
  const rest = await look(lyricsButton);
  await lyricsButton.hover();
  await expect.poll(async () => (await look(lyricsButton)).scale).toBeCloseTo(1.08, 2);
  expect((await look(lyricsButton)).background).not.toBe(rest.background); // hover brightens the selected state
  await page.mouse.down();
  await expect.poll(async () => (await look(lyricsButton)).scale).toBeCloseTo(.91, 2);
  await page.mouse.move(500, 500); await page.mouse.up();
});

test('the operating system reduced-motion setting disables transitions', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.evaluate(() => window.receive({ preferences: { ...window.snapshot.preferences, lyricsOpen: true }, lyrics: { status: '', plain: '', lines: [{ seconds: 0, text: 'Only line' }] } }));
  await expect(page.locator('.current-line')).toHaveText('Only line');
  for (const selector of ['.stage', '.extras', '.lyrics', '.lyric-lines', '.lyric-lines p', '.seek-thumb', '.icon-button'])
    expect(await page.locator(selector).first().evaluate(el => getComputedStyle(el).transitionDuration), selector).toBe('0s');
});

test('keyboard focus reveals the hover actions so Tab reaches them', async ({ page }) => {
  const stage = page.locator('.stage');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Previous track' })).toBeFocused();
  await expect(stage).toHaveClass(/expanded/);
  for (let i = 0; i < 4; i++) await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Toggle lyrics' })).toBeFocused();
  for (let i = 0; i < 3; i++) await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Settings' })).toBeFocused();
  await page.keyboard.press('Enter');
  expect(await page.evaluate(() => window.commands.some(c => c.type === 'settings'))).toBe(true);
  await page.evaluate(() => document.activeElement.blur());
  await expect(stage).not.toHaveClass(/expanded/);
});

test('at scale 1.25 the size sent to the native host equals the rendered card height', { tag: '@motion' }, async ({ page }) => {
  await setPrefs(page, { scale: 1.25 });
  await expect.poll(async () => (await sizes(page)).at(-1)?.height).toBe(195);
  expect(Math.abs(await renderedHeight(page) - 195)).toBeLessThanOrEqual(1);
  await setPrefs(page, { lyricsOpen: true });
  await expect.poll(async () => (await sizes(page)).at(-1)).toEqual({ height: 455, animate: true });
  await page.waitForTimeout(700);
  expect(Math.abs(await renderedHeight(page) - 455)).toBeLessThanOrEqual(1);
});

test('Settings opens as a drawer from settingsOpen, keeps the hover row, and the gear toggles it', async ({ page }) => {
  await page.mouse.move(500, 600); await clearCommands(page);
  await page.evaluate(() => window.receive({ settingsOpen: true }));
  await expect(page.getByRole('region', { name: 'Settings' })).toBeVisible();
  await expect(page.locator('.stage')).toHaveClass(/expanded/);
  await expect(page.getByRole('tab', { name: 'Appearance' })).toHaveAttribute('aria-selected', 'true');
  expect(await sizes(page)).toEqual([{ height: 480, animate: true }]);
  const gear = page.getByRole('button', { name: 'Settings', exact: true });
  await expect(gear).toHaveClass(/selected/);
  await page.getByRole('tab', { name: 'Appearance' }).focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Lyrics' })).toBeFocused();
  await expect(page.getByRole('tab', { name: 'Lyrics' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('tabpanel')).toHaveAttribute('aria-labelledby', 'tab-Lyrics');
  await gear.click();
  expect((await sent(page, 'settings')).length).toBe(1);
  await page.evaluate(() => window.receive({ settingsOpen: false }));
  await expect(page.locator('.settings')).toHaveCount(0);
});

test('each Settings control posts its message, and Scale applies only on release', async ({ page }) => {
  await openSettings(page);
  await clearCommands(page);
  const preferences = () => page.evaluate(() => window.commands.filter(c => c.type === 'setPreference').map(({ key, value }) => [key, value]));
  const scale = page.getByRole('slider', { name: 'Scale' }), box = await scale.boundingBox();
  await page.mouse.move(box.x + 20, box.y + box.height / 2); await page.mouse.down();
  await page.mouse.move(box.x + box.width * .6, box.y + box.height / 2, { steps: 8 });
  expect(await preferences()).toEqual([]);
  await expect(page.locator('.slider-row .value').first()).not.toHaveText('100%'); // the label follows the drag
  await page.mouse.up();
  await expect.poll(preferences).toHaveLength(1);
  expect((await preferences())[0][0]).toBe('scale');
  await scale.press('ArrowRight'); // one keyboard step, one message
  await expect.poll(preferences).toHaveLength(2);
  await clearCommands(page);
  await page.getByRole('slider', { name: 'Opacity' }).press('ArrowRight');
  await page.getByRole('radio', { name: 'Dark' }).click();
  await page.getByRole('slider', { name: 'Corners' }).press('ArrowLeft');
  await page.getByRole('switch', { name: 'Reduced motion' }).click();
  await page.getByRole('tab', { name: 'Lyrics' }).click();
  await page.getByRole('slider', { name: 'Text size' }).press('ArrowRight');
  await page.getByRole('slider', { name: 'Timing offset' }).press('ArrowRight');
  await expect(page.getByText('+0.25 s')).toBeVisible();
  expect(await preferences()).toEqual([['opacity', .7], ['theme', 'Dark'], ['corners', 21], ['reducedMotion', true], ['lyricsSize', 19], ['lyricsOffset', .25]]);
  await page.getByRole('button', { name: 'Import lyrics…' }).click();
  expect((await sent(page, 'importLyrics')).length).toBe(1);
  await page.evaluate(() => window.receive({ lyricsStatus: 'Lyrics saved for this track.' }));
  await expect(page.locator('.note.status')).toHaveText('Lyrics saved for this track.');
  await page.getByRole('tab', { name: 'General' }).click();
  await page.getByRole('switch', { name: 'Start with Windows' }).click();
  expect((await sent(page, 'startup')).map(c => c.enabled)).toEqual([true]);
  await expect(page.getByText(/PrismPlay \d+\.\d+\.\d+ · No account/)).toBeVisible();
});

test('Esc closes Settings; shortcut recording sends the next key, and Esc first cancels recording', async ({ page }) => {
  await openSettings(page);
  await page.getByRole('tab', { name: 'General' }).click();
  await clearCommands(page);
  const keys = page.getByRole('button', { name: /Game mode shortcut Ctrl\+Alt\+G/ });
  await keys.click();
  await expect(page.getByText('Press a key…')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByText('Press a key…')).toHaveCount(0);
  await keys.click();
  await page.keyboard.press('Shift'); // ignored: not a letter or digit
  await page.keyboard.press('k');
  expect(await sent(page, 'setHotkey')).toEqual([{ type: 'setHotkey', key: 'K' }]);
  await expect(keys).toBeVisible();
  expect(await sent(page, 'closeSettings')).toEqual([]);
  await page.keyboard.press('Escape');
  expect((await sent(page, 'closeSettings')).length).toBe(1);
});

test('lyrics and Settings swap in one drawer slot with a cross-fade', async ({ page }) => {
  await page.mouse.move(500, 600);
  await setPrefs(page, { lyricsOpen: true });
  await expect(page.locator('.layer')).toHaveCount(1);
  await clearCommands(page);
  await page.evaluate(() => window.receive({ settingsOpen: true }));
  await expect(page.locator('.layer')).toHaveCount(2); // the lyrics fade out under Settings, in the same slot
  await expect(page.locator('.layer.leaving .lyrics')).toHaveCount(1);
  expect(new Set(await page.locator('.layer').evaluateAll(layers => layers.map(l => l.offsetTop))).size).toBe(1);
  expect(await sizes(page)).toEqual([{ height: 480, animate: true }]);
  await expect(page.locator('.layer')).toHaveCount(1);
  await expect(page.locator('.layer .settings')).toBeVisible();
  // From Settings, the lyrics button returns to the open lyrics.
  await page.getByRole('button', { name: 'Toggle lyrics' }).click();
  expect((await page.evaluate(() => window.commands)).map(c => c.type).filter(t => t !== 'size')).toEqual(['closeSettings']);
  await page.evaluate(() => window.receive({ settingsOpen: false }));
  await expect(page.locator('.layer:not(.leaving) .lyrics')).toHaveCount(1);
  await page.mouse.move(500, 600);
  await expect.poll(async () => (await sizes(page)).at(-1)).toEqual({ height: 364, animate: true });
});

test('playing advances the seek bar and time label; pausing stops them', async ({ page }) => {
  const progress = () => page.getByRole('slider').evaluate(el => parseFloat(el.style.getPropertyValue('--progress')));
  const start = await progress();
  expect(start).toBeCloseTo(.25, 3);
  await page.evaluate(() => window.receive({ playing: true, position: 50, sampledAt: Date.now() }));
  await expect(page.locator('.time-labels span').first()).toHaveText('0:51', { timeout: 2000 });
  expect(await progress()).toBeGreaterThan(start);
  await page.evaluate(() => window.receive({ playing: false, position: 52, sampledAt: Date.now() }));
  const paused = await progress();
  await page.waitForTimeout(300);
  expect(await progress()).toBe(paused);
  await expect(page.locator('.time-labels span').first()).toHaveText('0:52');
});

test('a hotkey conflict stays visible on the card while a track plays and changes', async ({ page }) => {
  const chip = page.locator('.hotkey-chip');
  await expect(chip).toHaveCount(0);
  await page.evaluate(() => window.receive({ playing: true, position: 50, sampledAt: Date.now(), hotkeyError: 'Ctrl+Alt+G in use' }));
  await expect(chip).toBeVisible();
  await expect(chip).toHaveText('Ctrl+Alt+G in use');
  await expect(page.getByText('A little space')).toBeVisible();
  await page.evaluate(() => window.receive({ track: { key: 'next', title: 'Another song with a much longer title', artist: 'Someone', duration: 180 }, position: 0, sampledAt: Date.now(), status: '' }));
  await expect(page.getByText('Another song with a much longer title')).toBeVisible();
  await expect(chip).toBeVisible();
  // The long title ellipsizes before the chip, and the chip stays inside the 300 px card.
  const box = await chip.boundingBox(), title = await page.locator('.track-title').boundingBox();
  expect(box.x + box.width).toBeLessThanOrEqual(300);
  expect(title.x + title.width).toBeLessThanOrEqual(box.x);
  await page.evaluate(() => window.receive({ hotkeyError: '' }));
  await expect(chip).toHaveCount(0);
});

test('missing automatic lyrics offer retry without requiring a file', async ({ page }) => {
  await page.evaluate(() => window.receive({ preferences: { ...window.snapshot.preferences, lyricsOpen: true }, lyrics: { status: 'Lyrics unavailable · try again later', plain: '', lines: [] } }));
  await page.getByRole('button', { name: 'Retry lyrics' }).click();
  expect(await page.evaluate(() => window.commands.some(c => c.type === 'retryLyrics'))).toBe(true);
  await page.evaluate(() => window.receive({ lyrics: { status: 'Unsynced lyrics · LRCLIB', plain: 'Plain test lyrics', lines: [] } }));
  await expect(page.locator('.plain-lyrics')).toHaveText('Plain test lyrics');
  await expect(page.locator('.current-line')).toHaveCount(0);
});

test('the macOS host gets its own labels, keycaps and a full-height stage; Windows stays the default', async ({ page }) => {
  await expect(page.locator('html')).toHaveAttribute('data-platform', 'windows');
  await page.evaluate(() => window.receive({ platform: 'mac', hotkeyError: '⌃⌥G in use' }));
  await expect(page.locator('html')).toHaveAttribute('data-platform', 'mac');
  // Collapsed, yet the stage fills the 480 px page: the native panel's height is the visible edge on macOS.
  expect(await page.locator('.stage').evaluate(el => getComputedStyle(el).height)).toBe('480px');
  await expect(page.locator('.hotkey-chip')).toHaveAttribute('title', /menu bar icon/);
  await page.evaluate(() => window.receive({ settingsOpen: true }));
  await page.getByRole('tab', { name: 'General' }).click();
  await expect(page.getByRole('switch', { name: 'Open at login' })).toBeVisible();
  await expect(page.locator('.keys kbd')).toHaveText(['⌃', '⌥', 'G']);
  await expect(page.getByText('Use the menu bar icon to unlock it anytime.')).toBeVisible();
  await expect(page.locator('.glass__warp')).toBeHidden();
});

test('the Waveform setting posts its preferences, and the canvas reacts to audio beside the title unless a shortcut conflict shows', async ({ page }) => {
  await openSettings(page); await clearCommands(page);
  await expect(page.locator('.waveform')).toHaveCount(0);
  await page.getByRole('switch', { name: 'Waveform' }).click();
  expect((await sent(page, 'setPreference')).map(({ key, value }) => [key, value])).toEqual([['waveform', true]]);
  await setPrefs(page, { waveform: true, waveformStyle: 'Bars' });
  await expect(page.locator('.waveform')).toBeVisible();
  // A loud 1 kHz frame lifts a bar into the canvas's top quarter, which a flat rest never reaches.
  await page.evaluate(() => {
    const pcm = Int16Array.from({ length: 1024 }, (_, i) => 26000 * Math.sin(2 * Math.PI * 1000 * i / 44100));
    window.emit({ type: 'audio', rate: 44100, pcm: btoa(String.fromCharCode(...new Uint8Array(pcm.buffer))) });
  });
  await page.waitForFunction(() => { const c = document.querySelector('.waveform'); return c.getContext('2d').getImageData(0, 0, c.width, c.height / 4).data.some(v => v); });
  await page.getByRole('radio', { name: 'Wave' }).click();
  expect((await sent(page, 'setPreference')).at(-1)).toMatchObject({ key: 'waveformStyle', value: 'Wave' });
  await page.evaluate(() => window.receive({ hotkeyError: 'Ctrl+Alt+G in use' }));
  await expect(page.locator('.waveform')).toHaveCount(0);
});
