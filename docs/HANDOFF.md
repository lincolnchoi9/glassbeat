# PrismPlay — engineering handoff

Updated: October 13, 2026
Workspace: `C:\Users\linco\OneDrive\Documents\ChatGPT\spot overlay`

## Current state

PrismPlay is a working Windows 11 x64 Spotify overlay, version 0.4.2. The native C# host detects local Spotify media sessions; a bundled React interface renders the overlay in WebView2. Automatic lyrics are implemented and have been observed working with a real Spotify track. The latest build and self-contained publish succeeded.

Latest deliverable: `PrismPlay-0.4.0-win-x64.zip`, on E: only (location, size, and SHA-256 are recorded in the workspace copy of this file, under "0.4.0 PrismPlay rebrand"). The 0.2.0 and 0.3.0–0.3.5 packages are kept under their original names. Extract the whole archive; run PrismPlay.exe with its Web folder and runtime files alongside it. Edge WebView2 Runtime is required. The executable is unsigned. Updates are manual.

Git: `origin` is the project's GitHub repository; `main` holds the 0.3.5 and 0.4.0 work. Older source snapshots before broad changes: `<BuildRoot>\snapshot-0.3.0-src` and `snapshot-0.3.2-src` (the 0.3.2 source, taken before the 0.3.3 work). Build output, dependencies, and archives are ignored.

## User intent and accepted requirements

- A draggable, always-on-top glass music card over windowed/borderless games; local Spotify detection without login.
- Use the requested `https://github.com/rdev/liquid-glass-react` library. The React overlay pins version 1.1.1.
- Smooth movement without a dark dragging state or busy cursor. Smooth buttons, hover expansion, and lyrics; respect reduced motion.
- Put previous/play/next between track information and timeline. Use a fully visible circular seek handle and functional seeking.
- Hover reveals lyrics, scale, game mode, and settings. Lyrics remain open when hover controls collapse.
- Game mode locks movement and passes clicks through; Ctrl+Alt+G by default, with tray recovery.
- Automatic lyrics without requiring uploads. Local LRC imports remain optional.
- Latest request: remove “Synced lyrics · LRCLIB” from the lyrics panel and make Settings substantially more minimal and consistent with glass.
- Exclusive fullscreen, injection, anti-cheat integration, remote Spotify playback, signing, and store distribution are outside current scope.

## Most recent changes

### 0.4.2 karaoke lyrics

- The current synced line fills letter by letter (per-letter `<span>`s): about 8 letters/s, 1–5 s per line, never past the next line's start; a 3-letter soft opacity edge and a trailing `currentColor` glow (off in Light; uses `color-mix`, so macOS 13.1+). Reduced motion keeps the line fully lit.
- A `requestAnimationFrame` loop writes `--p` on the current `<p>`; the progress comes from `lyricProgress` in `model.js`.
- Verified: C# Release build 0/0, `npm test` 4/4, Playwright 22/22. Clean publish: `<BuildRoot>\artifacts\PrismPlay-0.4.2-win-x64.zip`, 85,867,896 bytes, SHA-256 `04CA7E338CF388D083B0E9C8F665040C4229C17B88769412462B3698CC01C62D`, 512 files; `PrismPlay.exe` reports 0.4.2 (no launch; the docs inside the ZIP predate this line).

### 0.4.1 app icon (branch `mac-port`)

- `assets/icon.png` (1024², the tile edge to edge, transparent outside its rim) and `src/PrismPlay/PrismPlay.ico` (16–256 px; 16/20/24 use a hinted ring glyph) come from `assets/make-icons.py <source artwork>`.
- Windows: `<ApplicationIcon>`, `AppWindow.SetIcon` and the tray icon (`LoadImage` of the loose `PrismPlay.ico` at the small-icon size). macOS: `bundle.sh` builds `AppIcon.icns` on Apple's grid (`mac/scripts/icon-canvas.swift`, `sips`, `iconutil`); the menu bar uses a drawn template prism glyph.
- Verified: C# build 0/0; the built exe's associated icon is the new artwork. Clean publish: `<BuildRoot>\artifacts\PrismPlay-0.4.1-win-x64.zip`, 85,867,455 bytes, SHA-256 `8479B26E06C157C8EABB67B643644C14D69883713085C6565833262385FC7EA1`, 512 files (no launch; the docs inside the ZIP predate this line).

### 0.4.0 PrismPlay rebrand

- Renamed to PrismPlay: `src/PrismPlay/PrismPlay.csproj` (exe `PrismPlay.exe`, namespace `PrismPlay`), `tests/PrismPlay.Tests`, `prismplay-ui`, virtual host `prismplay.local`, mutexes `Local\PrismPlay.Overlay[.Demo]`, Run value `PrismPlay`, data in `%LOCALAPPDATA%\PrismPlay` (`PrismPlay-Demo`), and LRCLIB user agent `PrismPlay/0.4`. The build folder (`<BuildRoot>`) keeps its old name.
- Migration: 0.4.0 copies the old app's `settings.json`, `lyrics-cache` and `imported-lyrics` (never `WebView2`) into `%LOCALAPPDATA%\PrismPlay` once, if that folder is missing, and moves the old Run value to `PrismPlay`; the old folder is never deleted. This one-time migration and its runner check were removed after 0.4.0 (runner 31).
- Verified: builds 0/0, runner 32/32, `npm test` 3/3, Playwright 21/21. Clean publish: `<BuildRoot>\artifacts\PrismPlay-0.4.0-win-x64.zip`, 85,338,977 bytes, SHA-256 `D72FDC9300F6BA9F6815117BD8618136D8AA88083A3D16467DEDC266362BA7EE`, 511 files (smoke launch skipped; the docs inside the ZIP predate this line).

### 0.3.5 material (visual only: `style.css` and the `Glass()` props)

- `.stage` tint is a vertical gradient ending at a fixed 240 px (lighter top, deeper bottom; alpha still `Opacity × 1.2`). `.stage::after` (z-index 2) draws a 1 px hairline, a brighter top specular line and ~3 % SVG grain. The library rim spans lose their blurred shadows. The Album wash is the artwork at blur 56 px, saturate 1.5, opacity .5, masked to the top; without artwork it is a neutral white top glow. Drawer separator and Settings groups match; Light uses a frosted white gradient, a 6 % dark hairline and white groups. `Glass()`: displacementScale 18 (was 34), aberrationIntensity .25 (was .7).
- Found, not fixed: the library's `.glass__warp` SVG filter (per-channel `screen` blends) dims a semi-transparent backdrop by its alpha and returns it nearly opaque. At Opacity 0.64 the card is 254/255 alpha, and Light renders grey (about 182 instead of 240). Without the warp it would be 189/255. Measured in Playwright with a transparent page.
- Package: the 0.3.5 package in `<BuildRoot>\artifacts`, 85,337,699 bytes, SHA-256 `5059300756881EEC99F1FC9438A9F89B9A8FBD7F3C367455AB10C4C35AD47CAA` (clean publish; smoke launch skipped; the docs inside the ZIP predate this line).

### 0.3.4 smooth edges

- The user found the card edges pixelated: the GDI window region is 1-bit. They chose smooth edges over the native blur. `DesktopAcrylicBackdrop` is replaced by `TransparentBackdrop` (`OverlayWindow.cs`), a `SystemBackdrop` whose brush is fully transparent (the WinUIEx `TransparentTintBackdrop` pattern), so the window is per-pixel transparent.
- The rounded CSS `.stage` paints the tint and atmosphere again; its spring height transition is the visible animation. The full-frame `.tint` is gone. The tint alpha is `Opacity × 1.2` (0.77 at the default 0.64; was ×0.68 over acrylic), and the Opacity range is unchanged. It was readable over white and over busy stripes in headless Edge (`<BuildRoot>\screens\034`).
- The window region is now a hit-test rectangle: it grows at once and keeps its height for 560 ms on a shrink, so it never clips the card. The host spring now only moves the window near the bottom edge. Rounded regions and `CreateRoundRectRgn` were removed. Clicks on the transparent corners still hit the overlay (`ponytail:` comment).
- **Transparency fix (first 0.3.4 build showed a #202020 rectangle around the card).** Cause: WinUI's WebView2 sets its system visual bridge background to a theme brush it looks up in `WebView2.Resources` as `BrushForThemeBackgroundColor` (`_HC` in high contrast), regardless of `DefaultBackgroundColor`. `OverlayWindow` now pre-seeds both keys with a transparent brush. Found by elimination in `--demo`: an opaque red backdrop brush showed only below a temporarily shortened WebView, and the strings and logic were read from `Microsoft.UI.Xaml.Controls.dll` and the WinUI WebView2 source. `NativeWindow` also does what WinUIEx `TransparentTintBackdrop` does: `DwmExtendFrameIntoClientArea` with zero margins, `DwmEnableBlurBehindWindow` with an off-window region, and a black `WM_ERASEBKGND`. That last part was not isolated as necessary. The backdrop brush is set before `base.OnTargetConnected`, and failures are logged under `--diagnostics`. `--demo` now uses its own single-instance mutex (`Local\PrismPlay.Overlay.Demo`), so it runs beside the real overlay.
- Verified in `--demo` over a white helper form: pixels outside the rounded corners read #FFFFFF; zoomed corner `<BuildRoot>\screens\034\fixed-corner.png`. Game mode (ex-style `0x80128`, layered + transparent) stays transparent with lyrics open (`fixed-gamemode.png`); click-through was not tested with real input. Builds 0/0, runner 31, `npm test` 3/3, Playwright 21/21. Not verified: light-theme Windows, high contrast, a smoke launch of the republished ZIP.
- Package (republished after the transparency fix): the 0.3.4 package in `<BuildRoot>\artifacts`, 85,336,008 bytes, SHA-256 `F278FEE593D5EBA4A0EFBA6FBF89C796EC158D6394236851F24DFFBE5151130E`, 511 files (the docs inside the ZIP predate these lines).

### 0.3.3 drawer + in-overlay Settings

Request: the lyrics button should make the card extend smoothly out of the bottom instead of "making a new screen and snapping"; Settings should be a drawer of the overlay in the same glass, not a separate window; everything should look and move like high-quality Apple UI.

Cause of the snap (diagnosed before this batch): React posted `size` in 6 steps during a 180 ms CSS transition, and each step ran `AppWindow.MoveAndResize` + `SetWindowRgn`. Every window resize made WebView2 re-layout asynchronously and the acrylic jump, so the edge visibly stepped, and the strip between card and window edge was untinted. Near the bottom of the work area, the pre-clamped position moved the whole card up once at the start.

Host (`NativeWindow.cs`, `OverlayWindow.cs`, `DragMath.cs`):
- The window frame is always `300·scale × 480·scale` DIP (`NativeWindow.FrameHeight` = 156 card + 44 hover row + 280 Settings drawer; `FRAME` in `main.jsx` is the same sum). It changes size only with scale or DPI, and may hang below the screen.
- The visible card is the window region (`CreateRoundRectRgn`, or `CreateRectRgn` at corners 0). `Resize(width, height, animate)` springs the region height toward the target with `DragMath.Spring`: critically damped, `x(t) = target + (d0 + (v0 + ω·d0)·t)·e^(−ωt)`, ω = 2π/0.42 s. A retarget samples the current height and velocity and restarts from them. It is driven by `CompositionTarget.Rendering` (subscribed only while moving) with Stopwatch time, stops when |x−target| < 0.25 px and |v| < 5 px/s, and calls `SetWindowRgn` only when the whole-pixel height or radius changes. `animate:false`, the first message, and a frame size change snap.
- Each frame, the window follows `preferences.X/Y` clamped so the visible rectangle (not the frame) stays on the work area of the display containing it (`ClampVisible`), using a move-only `SetWindowPos` in the same tick as the region. Near the bottom edge the card therefore rises with the spring and returns to its saved Y as it collapses. Position updates are skipped while dragging; the region keeps animating. `Clamp()`/`FinishDrag` use the visible height; the 0.3.1 zero-length-drag rule is unchanged. DPI/display changes reset `frame` and resize as before.
- `size` messages are clamped to [156, 480] logical and carry `animate`.
- Settings: `SettingsWindow.cs` and `Motion.cs` (used only by it) are deleted. The host owns `settingsOpen` and publishes `settingsOpen`, `startup` (Run key read when Settings opens), `lyricsStatus`, `generalStatus` (both cleared on close; a pending debounced save is flushed on close). Messages: `settings` (toggle), `closeSettings`, `setPreference {key, value}` (whitelist scale/opacity/corners/lyricsSize/lyricsOffset as finite numbers, reducedMotion as bool, theme as Album/Dark/Light; anything else is rejected, then `ApplySettings` normalizes), `setHotkey {key}` (same texts as the old window), `startup {enabled}` (same Run-key logic; the published `startup` is re-read after the change), `importLyrics` (`FileOpenPicker` on the overlay hwnd, then `ImportLyrics`). Tray double-click and the tray Settings item call `Show()`, open Settings, and activate the overlay (`SetForegroundWindow`, WebView focus). The overlay button does not activate.
- Game mode: opening Settings suspends game mode and closing restores it. Turning game mode on (hotkey, tray, button) while Settings is open closes Settings first.
- Focus: while Settings is open, `WS_EX_NOACTIVATE` is cleared (with `SWP_FRAMECHANGED`) and `WM_MOUSEACTIVATE` returns `MA_ACTIVATE`, so a click in the panel activates the overlay. Both are restored on close. In preview mode the style is never set, but `MA_ACTIVATE` still applies while Settings is open. Side effect: while the overlay is the foreground window, Alt+F4 closes the app, as for any focused window.

Web (`main.jsx`, `style.css`, `model.js`):
- `.frame` (300 × 480) holds a full-frame `.tint` (the tint, light theme, and album atmosphere) and the glass `.stage`. Every pixel the region reveals is tinted, even when the region and the CSS height are a frame apart. The atmosphere is fixed at the card + hover-row size, so it never re-rasterises while the card grows; below it the drawer has the plain tint.
- `.stage` height is `--height` (the visible height), so the liquid-glass rim sits at the visible edge. It transitions over 520 ms with `--spring: linear(...)`, 31 samples of `1 − (1 + ωt)·e^(−ωt)` (the host curve). `.extras` uses the same curve.
- One `size` message per change: `{ height: ceil(visible·scale), animate: !reduced }`; the first message snaps. The 6-step logic and `easeTime` are deleted.
- One drawer slot under the hover row, with a 1 px `rgba(ink, .08)` hairline inset 16 px. It shows Settings while open, else lyrics (208 px), else nothing. Visible height = 156 + 44 (hover, focus, drag, scrub, or Settings open) + drawer. Layers are laid out at full size immediately and revealed by the growing card. They enter with opacity 0→1 and translateY −6 px→0 (260 ms), leave with a 150 ms fade, and stay mounted for 520 ms after closing. Lyrics and Settings share one grid cell, so a swap cross-fades while the height springs 408↔480. With reduced motion (app or OS) there are no transitions, mount/unmount is immediate, and `animate:false` is posted.
- Settings UI: a sliding-thumb segmented control (Appearance · Lyrics · General; `tablist`/`tab`, arrow keys/Home/End) and inset groups (12 px radius, hairline dividers, 13 px labels, tabular values at reduced opacity). Sliders are custom `input[type=range]` controls with `aria-label`/`aria-valuetext`; the visible value is `aria-hidden` (an `<output>` would have been a second live region). Switches are `role="switch"` and Tint is a `radiogroup`. Scale applies on the native `change` event (pointer release, each key step); the other sliders apply live. The shortcut is shown as keycaps; clicking records, the next A–Z/0–9 sends `setHotkey`, and Esc cancels recording first, then closes Settings. The footer version comes from `package.json`. All three panes fit 280 px without scrolling, including the longest status texts.
- Addition beyond the plan: the lyrics button in the hover row, while Settings is open, closes Settings and shows lyrics (opening them if needed) instead of toggling them out of sight. Its selected state follows the visible drawer.

Decisions and deviations:
- The temporary per-frame `--diagnostics` log line was removed. With it on, the first measured close showed one 230 ms gap (synchronous file append per frame, on a C: drive with ~3 GB free); measured externally without it, there were no gaps. Use external polling of `GetWindowRgnBox`/`GetWindowRect` (about 1 ms) to check smoothness.
- `SetWindowRgn` and the move are two calls. An external 1 ms poll sometimes sees the new region one sample (≤ 2 ms) before the matching Y. One display frame could therefore show the bottom edge one step (≤ 7 px) off near the work-area bottom. Not seen in screenshots. A `ponytail:` comment in `ApplyRegion` names the upgrade (a composition clip).
- Corner changes re-apply the region at the current height without restarting the spring.

Verification is summarised under "Verification evidence" and detailed in `docs/VERIFICATION.md`.

- **Package:** the 0.3.3 package in `<BuildRoot>\artifacts` (a copy of the script's `PrismPlay-win-x64.zip`; the generated `artifacts\PrismPlay-win-x64` folder was deleted before `scripts/publish.ps1` ran with the E: arguments). Size 85,332,992 bytes. SHA-256 `A63A01509EA42C2DADED0738A903B8D6C29FD0D903BC69BE931D65A837B00A41`. 511 files; `Web/assets` holds only `index-CdxHRive.js` and `index-CuH6djj2.css`; `SettingsWindow`/`Motion` are absent. `PrismPlay.exe` file version 0.3.3.0, product version 0.3.3. **Only on E:** (C: has about 3 GB free; not copied into the workspace `artifacts`).
- **Smoke test:** extracted into the new folder `<BuildRoot>\smoke-0.3.3`; `PrismPlay.exe --demo` showed its window after 3 s and was sampled every 2 s for 10 s: running, visible, not cloaked, frame 300 × 480 at (80, 80), region 156, extended style `0x108` at every sample. `WM_CLOSE` → exit code 0. The demo `settings.json` hash was unchanged. The copies of this file and `VERIFICATION.md` inside the ZIP predate the archive and lack these two entries.

### Lyrics

Online LRCLIB lookup is enabled by default. A former artificial online-lookup gate was removed. Exact lookup uses title, artist, album, and duration; search fallback tolerates normalized punctuation/album differences while retaining title/artist and duration checks. Version words such as live or sped-up are preserved to avoid mismatches.

Requests are canceled for outdated tracks; late results cannot replace current-track lyrics. Up to 100 results are cached locally. A plain result survives an unsuccessful enhancement lookup. Unavailable and instrumental tracks have explicit states; missing results offer Retry.

The React view keeps a stable list of lyric nodes and translates the list to center the active line. It uses a 420 ms transition, an 80 ms active-line timer only while playing with timed lyrics, and reduced-motion support. Forward/backward seeks recenter correctly.

The latest UI edit removes the entire heading for timed lyrics, growing its viewport from 164 to 189 pixels without increasing the overall panel height. Plain lyrics retain the short label “Unsynced.” Provider identification remains in documentation and the concise settings privacy explanation.

### Settings (0.3.2 and earlier; replaced in 0.3.3)

`SettingsWindow.cs` used three compact tabs: Appearance, Lyrics, and General. The window is 460 × 590 logical pixels (scaled by DPI, see below), with independent scrollable tab content. It retains every existing option, shortcut conflict handling, startup opt-in, and import functionality.

The old long opaque form was replaced with Desktop Acrylic, a translucent dark surface, rounded tab/buttons, shorter labels, and inline slider values. A final refinement extends content into the title bar and changes slider/toggle accent resources to a pale neutral color.

Important distinction: Settings is still native WinUI 3 with Acrylic. The overlay uses the requested React glass library. Settings has not been migrated to React. Acrylic is subject to Windows transparency/compositor behavior.

### Settings usability pass (code-review batch)

Changes are limited to `SettingsWindow.cs` and the save path in `OverlayWindow.cs`:

- **DPI-aware size.** The 460 × 590 size is now logical. It is multiplied by `GetDpiForWindow / 96`, clamped to the display work area, and the window is moved back onto the work area if needed. At 100% the size is the same as before. The root has `MinWidth = 360` so the tab row does not clip. The size is set once at open; a later move to a monitor with a different DPI is left to WinUI.
- **Title bar.** A full-width, transparent 40-DIP row holds the "Settings" heading and is passed to `SetTitleBar`, so the whole row drags the window. The row's right margin equals `AppWindow.TitleBar.RightInset`, converted to DIPs and recomputed on `XamlRoot.Changed`, so the caption buttons stay with the system. The caption buttons are white, with 40/255 white on hover and 72/255 on press. Assumption: Settings is always dark (`RequestedTheme = Dark`), so "match the theme" means fixed white caption glyphs.
- **Tabs.** The selected tab now has a 56/255 white background, full-strength text, and a 2-DIP underline in the pale accent colour. Unselected tabs are dimmed through foreground alpha instead of element `Opacity`, so the system focus ring is not dimmed; the ring sits 3 DIP outside the pill. Left and Right arrows move between tabs and select them, wrapping at the ends. The selected state is exposed through `AutomationProperties.ItemStatus`.
- **Keyboard.** Esc closes Settings. It is handled on bubbling `KeyDown`, so an open ComboBox should close its dropdown first. That ComboBox case is unverified: it has not been tried in the app (see `docs/VERIFICATION.md`). Enter in the shortcut box runs Set. Sliders now use `SmallChange = step`; before this, arrow keys moved by the default of 1 (for example, Scale jumped from 100% to 200%). Tab order: the three tabs, then the visible page's controls top to bottom.
- **Opaque fallback.** When `DesktopAcrylicController.IsSupported()` is false or Windows transparency effects are off, no backdrop is set and the background is fully opaque (24, 28, 36) instead of a 65/255 tint. This is checked when Settings opens; toggling transparency while it is open is not tracked.
- **Debounced saving.** `ApplySettings` still normalizes, resizes, and publishes immediately, but it now restarts a 400 ms one-shot timer instead of writing `settings.json`. Any direct `Save()` (position change or app exit) cancels the timer and writes everything. A pending save is flushed when Settings closes and when the app exits.
- **Stale Scale slider.** Of the two options, closing Settings when the overlay's scale button is used was the simpler one, so that is what was done. Reopening Settings shows the current value. Scale is the only preference with a control both on the overlay and in Settings. LyricsOpen also changes from two places: the overlay's lyrics button, and a lyric import in Settings, which sets it to true. Settings has no LyricsOpen control, so the import cannot leave a stale value on screen.

### Lyrics and playback robustness batch (code-review batch 2)

Changes are in `LyricsProvider.cs`, `SpotifyPlayback.cs`, the Retry path of `OverlayWindow.cs`, and `tests/PrismPlay.Tests`:

- **Cache failures no longer discard lyrics.** The cache write and old-entry cleanup now catch `UnauthorizedAccessException` as well as `IOException`. Before, a read-only, denied, or antivirus-locked cache folder turned a successful download into "Lyrics unavailable". A cache read that fails with either exception now falls back to the network instead of failing.
- **Retry can upgrade a cached plain-only result.** `ILyricsProvider.GetAsync` has a new `allowPlainUpgrade` parameter (default false). The overlay's Retry passes true. On a cache hit that has plain lyrics only, it re-runs the LRCLIB search, which prefers synced rows, and caches a synced result if one is found. If the search fails or finds nothing, the cached plain lyrics are still shown, also when offline. Normal lookups and synced or instrumental cache hits are unchanged and make no network request. There is still no negative cache, so misses and plain-only Retries query LRCLIB every time. That is left for later.
- **A transient playback error no longer blanks the UI.** Before, any exception in a playback refresh published `Waiting` (track = null), which cleared the card and cancelled the lyrics fetch. Now the error is written to the diagnostic log and the last snapshot is kept. `Waiting` is published only when the refresh belongs to the current session and Windows' session list no longer contains that session. If the session list itself throws, the session is treated as still present. Assumption: real session removal or a switch to another session is reported through `SessionsChanged` → `SelectSession`, which already publishes `Waiting`. The removal check only covers a refresh that fails before that event arrives.
- **Refresh coalescing.** Media, playback, and timeline events each used to queue a full refresh behind the lock. Now, while a refresh runs, any number of further events share one queued follow-up refresh. A flag is cleared only after the lock is taken, so the follow-up reads state newer than the last event. A transport command's `await RefreshAsync()` can therefore return before that queued follow-up finishes, and its result arrives through `Changed` as usual.
- **Fresh state after the artwork load.** After the thumbnail await on a track change, the timeline and playback info are re-read, with the existing current-session check, before the final publish. The final snapshot no longer carries a position or controls from before the image load.
- **Ads (S3) deferred.** The code has no reliable advertisement signal. Lookups are already skipped when the session reports no artist or a zero duration. Title matching ("Advertisement") was not added because it is unconfirmed against a real Spotify ad and could catch a real song with that title.
- **Test runner.** `PrismPlay.Tests.csproj` now excludes `obj/**` and `bin/**` from default items, so `-p:DefaultItemExcludes=obj/**` is no longer needed. See the build section for a `dotnet run` problem found during this work.

### Overlay interaction and motion batch (code-review batch 3)

Changes are in `frontend/src/main.jsx`, `frontend/src/style.css`, and `frontend/e2e/overlay.spec.js`. No C# file changed. `NativeWindow.Resize` and the host's `size` handling did not need changes once the messages stopped arriving every frame.

- **One native resize per expand or collapse.** Before, a ResizeObserver on `.stage` posted `size` on every frame of the 180 ms height transition. The host then ran Resize, the region update, Move, and Clamp for each one. The baseline e2e run recorded 19 `size` messages for one hover. Now React computes the final height (156, plus 44 when expanded, plus 208 with lyrics) and posts `ceil(height × scale)` once per change. A larger size is posted immediately, before the transition, so the window is already full size while CSS animates the card inside it. A smaller size is posted 200 ms later (the 180 ms transition plus one frame), so the collapse is not clipped. With reduced motion (app or OS setting) it is posted immediately. **Option chosen:** grow up front, and shrink at the end of the transition. Each option on its own clips one direction: sizing up front clips the collapse, and debouncing clips the expansion. Nothing is posted before the first snapshot; the host has already sized the window from its stored height. **Trade-off:** during the 180 ms, the strip between the animating card and the window edge shows the window's acrylic backdrop without the card's tint.
- **Scale (S1).** Measured in Edge 154 through Playwright with root zoom 1.25: `getBoundingClientRect().height` of the 156 px stage is 195, and hit testing agrees. So the host's division by scale was correct. However, ResizeObserver does not fire when only the zoom changes (0 callbacks in the probe). The old code therefore never re-posted after a scale change, and its first post was measured at zoom 1, before the first snapshot arrived. Inferred from code but not observed in the app: at scale 0.75 this left the window 156 DIP tall for a 117 DIP card until the next hover. The new code posts when the scale changes. An e2e test at 1.25 checks that the posted height (195, and 455 with lyrics) equals the card's rendered pixel rows in a screenshot.
- **Seek bar.** The animation-frame loop now writes `--progress` directly to the slider element. React state changes only when the elapsed or remaining label text changes. A stub DevTools hook measured 3 React commits in 3 s of playback, against 541 animation frames in a comparable 3 s. Before, `setPosition` ran on every frame. Pause still ends the loop. Arrow-key seeking now starts from the live position.
- **OS reduced motion.** `@media (prefers-reduced-motion: reduce)` applies the same rule as `.reduced` to the overlay. The OS setting also stops the pointer-following glass highlight, as the app setting already did. Native drag easing (`DragMath`) still follows only the app setting.
- **Keyboard.** When focus inside the card matches `:focus-visible`, the card expands as it does on hover. It collapses when focus leaves the card. Focus from a mouse click does not count, so the card still collapses after a click once the pointer leaves. Tab now reaches Lyrics, Scale, Game mode, and Settings. **Limit:** in normal mode the overlay is `WS_EX_NOACTIVATE` and answers `MA_NOACTIVATE`, so Windows does not give it keyboard focus. This change helps in `--inspect`/`--demo`, and with tools that focus the WebView directly.
- **Text size.** `.mode-label` went from 7 px to 10 px. Time labels and `.lyrics-label` stay at 9 px, which is the new minimum. Assumption: the request meant "minimum 9 px, and 10 px for the mode label". Text scales with Scale: at 0.75, 10 px renders as 7.5 px and 9 px as 6.75 px. At the default scale, the card lays out without clipping (`PAUSED` and `NOW PLAYING` checked in the app).
- **Pointer moves.** The glass mouse state moved into a small `Glass` component. A pointer frame now re-renders only that component and `LiquidGlass`. The card content is passed as an unchanged `children` element, so React skips it. `LiquidGlass` receives the same props as before. The Lyrics memo was left as it was: every snapshot carries a new `sampledAt`, which the active-line sync needs, so narrowing its props would not save any renders. Pointer moves never reached Lyrics or Seek anyway.

### Native window, tray, and hotkey batch (code-review batch 4)

Changes are in `NativeWindow.cs`, the hotkey and snapshot code in `OverlayWindow.cs`, `frontend/src/main.jsx`, `frontend/src/style.css`, and `frontend/e2e/overlay.spec.js`. `Core.cs`, `SettingsWindow.cs`, `LyricsProvider.cs`, and `SpotifyPlayback.cs` did not change. No C# check was added: the clamp is one `Math.Clamp` per axis inside `NativeWindow`, which the runner cannot load, so it was checked in the app instead.

- **Hotkey conflict on the card.** Before, a failed registration set `status`. `Receive` resets `status` on every track change, and the card shows it only when there is no track, so the message was effectively never seen. Now `OverlayWindow.SetHotkey` wraps `NativeWindow.SetHotkey` and keeps a separate `hotkeyError`, published as the snapshot field `hotkeyError`. It reads "Ctrl+Alt+<key> in use" while no shortcut is registered, and is empty otherwise. Track changes do not touch it. Settings' Set button goes through the same wrapper, so a successful registration clears it. A failed change while another shortcut is registered leaves it empty, because that shortcut still works. The card shows it as a small amber chip at the right of the header. The track title ellipsizes before the chip, and the chip's tooltip says how to fix it. `NativeWindow.SetHotkey` also changes the tray tooltip to "PrismPlay · Ctrl+Alt+<key> is in use · right-click for Settings" while no shortcut is registered, and back to the normal text once one is. The `TaskbarCreated` re-add uses the current text.
- **Swapped mouse buttons.** The drag-end poll used `GetAsyncKeyState(VK_LBUTTON)`. That function reads physical buttons, so with swapped buttons the primary button is `VK_RBUTTON`, and the drag ended on the first frame. The poll now uses `VK_RBUTTON` when `GetSystemMetrics(SM_SWAPBUTTON)` is non-zero. This is based on the documented API (the `GetAsyncKeyState` remarks). It has not been tried on a machine with swapped buttons: this one reports `SM_SWAPBUTTON` = 0, and the Windows setting was not changed.
- **Edge clamp.** `Resize` now clamps the saved `preferences.X/Y` for the new size first. It uses the work area of the display at that rectangle (`DisplayArea.GetFromRect`, nearest). Then it makes one `AppWindow.MoveAndResize` call. Before, it resized, moved to the saved position, and then clamped, so an expanding card near the bottom briefly overlapped the taskbar. The saved-position logic is unchanged: only `FinishDrag` writes `preferences.X/Y`. A collapse clamps the saved position for the smaller size, so the card returns to it. `Clamp()`, used by `FinishDrag`, shares the same helper.
- **Tray.** (a) `TrackPopupMenu` is now followed by `PostMessage(hwnd, WM_NULL)`. This is the documented pattern for notification-icon menus, together with the existing `SetForegroundWindow`. (b) A single left-click (`WM_LBUTTONUP`) does the same as the menu's **Show overlay**: `Show()` uses `SW_SHOWNOACTIVATE`, topmost, and `SWP_NOACTIVATE`. It is idempotent, so a double-click still only opens Settings. A visibility toggle was not chosen: nothing else in the app hides the overlay, and a double-click would hide it and show it again. (c) The icon stays the generic `IDI_APPLICATION`. The project has no `.ico` asset, and none was added in this batch.
- **Mixed DPI during a drag (read only, no code change).** `WM_DPICHANGED` and `WM_DISPLAYCHANGE` enqueue `lastShape = default; Resize(...)`. During a drag `Resize` returns early, but `lastShape` stays reset, so the `Resize` in `FinishDrag` applies the size and region for the new DPI. The region therefore cannot stay stale after the drag ends, and no FinishDrag fix was needed. Between the DPI change and the release, the region keeps the old pixel size while the default handler resizes the window to the suggested rectangle. That could clip the card or leave square corners until release. This was not observed: both displays here are 96 DPI. A comment at the handler records it.
- **Focus.** `WS_EX_NOACTIVATE` (with `WS_EX_TOOLWINDOW`) is still set outside preview mode, and `WM_MOUSEACTIVATE` still returns `MA_NOACTIVATE`. The hotkey path (`ToggleGameMode` → `SetGameMode`) only uses `SWP_NOACTIVATE`. The tray left-click uses `Show()`, which does not activate. The tray right-click calls `SetForegroundWindow` before `TrackPopupMenu`. That call was already there and the documented menu pattern requires it; it runs only after the user clicks the tray, when the game has already lost focus. Opening Settings (double-click or menu) activates the Settings window, which the user asked for.

Found while testing, not changed in batch 4 (both fixed in 0.3.1, see below):
- A click on the header with no movement counts as a zero-length drag, and `FinishDrag` saves the current position. If the card is expanded and clamped near the bottom at that moment, the clamped position becomes the saved one. In the app, Y went from 862 to 832 this way.
- After a failed Set, Settings says "Previous shortcut retained" even when no shortcut was registered at startup.

### 0.3.1 final pass

- **Zero-length drag** (`NativeWindow.cs` `FinishDrag`, lines 163–171). `FinishDrag` compares the window position before its own `Clamp()` with the position at `BeginDrag`. Only when they differ does it write `preferences.X/Y` and raise `PositionChanged` (which saves). A header click without movement therefore keeps the saved position, and the display clamp is not written back. `Clamp()` and `Resize` still run, so the card stays on the work area. Assumption: a drag that ends exactly where it started counts as "not moved".
- **Shortcut text** (`SettingsWindow.cs` line 140; constructor line 14). Settings now receives a `hotkeyActive` callback (`() => native.HotkeyRegistered`, `OverlayWindow.cs` line 208). After a failed Set it shows "Shortcut in use. Previous shortcut retained." only when a shortcut is registered; otherwise "Shortcut in use. No shortcut is active; the default could not be registered."
- **Save failure shown at once** (`OverlayWindow.cs` lines 39 and 202–203). `Save()` now calls `Publish()` after setting "Settings could not be saved". This covers the debounce timer, Settings closing, and the drag save. `Publish` never calls `Save`, so it cannot loop, and after `Closed` it does nothing. The debounce tick no longer publishes after a successful save; `ApplySettings` has already published that state.
- **Version 0.3.1** in `PrismPlay.csproj`, `frontend/package.json`, the root entries of `frontend/package-lock.json`, and this file. The "PrismPlay 0.3" note in Settings and the `PrismPlay/0.3` LRCLIB user agent are major.minor and were left unchanged. The README has no version number.
- **Packaging.** `scripts/publish.ps1` reuses its output folder and does not clean it, so the hashed web bundles of earlier publishes stayed in it. The 0.3.0 ZIP contains four unreferenced files: `Web/assets/index-BS8JGwtf.css`, `index-CjKUh2MD.js`, `index-BEs4iVRC.css`, and `index-uvrprPYq.js`. Before the 0.3.1 publish, the generated folder `<BuildRoot>\artifacts\PrismPlay-win-x64` was deleted and the script was run again. The script itself was not changed: it still writes `PrismPlay-win-x64.zip`, and the versioned name is a copy of that file.
- The copy of this file and `VERIFICATION.md` inside the 0.3.1 ZIP was written before the archive existed. It does not contain the archive's own hash or the smoke-test result; the workspace copy does.
- **Package:** the 0.3.1 package in `<BuildRoot>\artifacts`, also copied to the workspace `artifacts` folder (C: had 582 MB free before the copy). Size 85,327,802 bytes. SHA-256 `1C587748784F16921FFEEEFF1661BABAE8539AB2DC10D00A5FBABAB810C27A57`.
- **Smoke test:** extracted into the new folder `<BuildRoot>\smoke-0.3.1`, then `PrismPlay.exe --demo` ran for 10 s. The window was visible (not cloaked), 300 × 156, and it closed with exit code 0. However, it was not on top of the foreground game window, and its extended style has no `WS_EX_TOPMOST`. 0.3.0 behaves the same in `--demo`. See `docs/VERIFICATION.md`.

### Always-on-top fix (after 0.3.1, source only; not packaged)

- **Cause, from readings.** In-process, the presenter reported `Kind = Overlapped` and `IsAlwaysOnTop = true`, but `WS_EX_TOPMOST` was clear, both at the end of the `NativeWindow` constructor and after `window.Activate()`. `SetWindowPos(HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE)` returned TRUE but left the bit clear, whether called in-process or from another process, so the WinAppSDK 1.8 AppWindow drops raw topmost changes. The existing `SetWindowPos(-1)` calls in `SetGameMode` and `Show` therefore never re-asserted topmost. Setting `IsAlwaysOnTop` to false and then true did set the bit. The constructor's `SetWindowPos` with flags `0x37` includes `SWP_NOZORDER` (0x4), so it never touched the z-order.
- **Fix.** `NativeWindow.KeepOnTop()` (`NativeWindow.cs` lines 68–75). If `WS_EX_TOPMOST` is clear, it sets the presenter's `IsAlwaysOnTop` to false and then true. It always logs `WS_EX_TOPMOST before -> after` through `DiagnosticLog` (`--diagnostics` only). It never activates the window and never calls `SetForegroundWindow`. Called at the end of the constructor (line 66), after `window.Activate()` (`App.xaml.cs` line 22, via `OverlayWindow.KeepOnTop`, line 18), in `SetGameMode` (line 106), in `Show` (line 181), and in the `WM_DPICHANGED`/`WM_DISPLAYCHANGE` handler (line 209).
- **Read, without input** (`--demo`, development build): `WS_EX_TOPMOST` went from false to true in the constructor, stayed true after `Activate()`, and stayed true after WM_DISPLAYCHANGE and the tray Show callback were posted to the window. Extended style `0x108`; z-order placed the overlay above the foreground `RobloxPlayerBeta` window, which stayed in the foreground. Before the fix: `0x100`, below Roblox. See `docs/VERIFICATION.md`.
- Not verified: topmost while actually playing a game, exclusive-fullscreen games (a topmost window is not expected to show over them), Roblox in true fullscreen, and normal (non-demo) mode, where the `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` style write also runs. No new package was built; the 0.3.1 ZIP does not have this fix.

### 0.3.2 package

- Contains the always-on-top fix above. Version 0.3.2 in `PrismPlay.csproj`, `frontend/package.json`, the two root entries of `frontend/package-lock.json`, and this file; other "0.3" strings unchanged. `PrismPlay.exe` file version 0.3.2.0, product version 0.3.2.
- Release build of the app and the runner: zero warnings and errors. Runner (`<BuildRoot>\bin\Release\net8.0\PrismPlay.Tests.exe`, run directly, up to date) passed 30 offline. `npm test` passed 3/3. Playwright and `--live` were not run.
- `<BuildRoot>\artifacts\PrismPlay-win-x64` was deleted before `scripts/publish.ps1` ran with the E: arguments. The ZIP has 511 files; `Web/assets` holds only `index-53HuwKe6.css` and `index-BOxK3oDv.js`.
- **Package:** the 0.3.2 package in `<BuildRoot>\artifacts` (a copy of the script's `PrismPlay-win-x64.zip`). Size 85,330,501 bytes. SHA-256 `4A59577626B5EC77E4D56253F53D41EA15C80A7B61058304D937A01109C7E377`. It is **only on E:**. The copy into the workspace `artifacts` folder was skipped because C: had about 0.49 GB free.
- **Smoke test:** extracted into the new folder `<BuildRoot>\smoke-0.3.2`, then `PrismPlay.exe --demo` was sampled every 2 s for 10 s: running, visible, not cloaked, 300 × 156 at (80, 80), extended style `0x108` (`WS_EX_TOPMOST` set) at every sample. Closed with `WM_CLOSE`, exit code 0. No mouse or keyboard input was sent. Demo and normal `settings.json` hashes were unchanged. (A first run was also closed with `WM_CLOSE` after 37 s and showed `0x108`, but its exit code was not captured because of a script error.)
- The copy of this file inside the ZIP predates the archive and lacks this section.

## Files and responsibilities

| Path | Responsibility |
| --- | --- |
| `src/PrismPlay/OverlayWindow.cs` | WebView2 host, restricted local navigation, JSON bridge, playback/lyrics orchestration, Settings state and messages (preferences, shortcut, startup, lyric import), game-mode suspension |
| `src/PrismPlay/NativeWindow.cs` | Topmost/no-activate (activatable while Settings is open), tray, hotkey, click-through, custom movement, fixed frame with a sprung window region, visible-rect display clamping |
| `src/PrismPlay/SpotifyPlayback.cs` | Spotify media-session selection, capabilities, metadata/artwork, transport and seeking |
| `src/PrismPlay/LyricsProvider.cs` | LRCLIB lookup/search, validation, cache, optional local overrides |
| `src/PrismPlay/Core.cs` | Models, LRC parser, preferences normalization and persistence |
| `src/PrismPlay/DragMath.cs` | Frame-rate-independent movement easing; critically damped region spring |
| `src/PrismPlay/DiagnosticLog.cs` | Opt-in bounded diagnostic log |
| `frontend/src/main.jsx` | Glass card, transport, captured-pointer seeking, hover actions, drawer slot, stable lyrics view, Settings drawer |
| `frontend/src/style.css` | Glass appearance, full-frame tint, `--spring` curve, layout, interaction, drawer, Settings controls |
| `frontend/src/model.js` | Playback interpolation, seek geometry, lyric selection, time formatting |
| `frontend/e2e/overlay.spec.js` | Twenty-two Edge/Playwright interaction checks with a mocked native bridge |
| `tests/PrismPlay.Tests` | Dependency-free C# check runner, including optional live provider check |
| `scripts/publish.ps1` | Self-contained x64 publish and ZIP creation |
| `mac/` | Native macOS host and core port; `mac/scripts/bundle.sh` builds the .app (see "macOS port") |
| `README.md`, `THIRD-PARTY.md`, `docs/VERIFICATION.md` | Usage, attribution, and tested/untested behavior |

## Build environment

C: has limited free space. Keep large NuGet caches, intermediate output, and publish output on E:. Existing locations:

- .NET SDK: 8.0.403
- Node.js 24: `E:\Program Files\nodejs`
- Build root, written `<BuildRoot>` here and in `VERIFICATION.md`: a build folder on a drive with free space (on E:)
- NuGet: `<BuildRoot>\nuget`
- Published app: `<BuildRoot>\artifacts\PrismPlay-win-x64`

Use PowerShell from the workspace:

```powershell
$env:NUGET_PACKAGES='<BuildRoot>/nuget'
dotnet build src/PrismPlay/PrismPlay.csproj -c Release `
  -p:BaseOutputPath=<BuildRoot>/bin/ `
  -p:BaseIntermediateOutputPath=<BuildRoot>/obj/

# Offline checks, then an optional real LRCLIB request. Pass the build paths as environment
# variables, not -p: (see below).
$env:BaseOutputPath='<BuildRoot>/bin/'; $env:BaseIntermediateOutputPath='<BuildRoot>/obj/'
dotnet run --project tests/PrismPlay.Tests/PrismPlay.Tests.csproj -c Release
dotnet run --project tests/PrismPlay.Tests/PrismPlay.Tests.csproj -c Release -- --live
Remove-Item Env:BaseOutputPath, Env:BaseIntermediateOutputPath
npm.cmd test --prefix frontend
npm.cmd run build --prefix frontend
npm.cmd run test:ui --prefix frontend

$env:NPM_CONFIG_CACHE='<BuildRoot>/npm-cache'
./scripts/publish.ps1 -ArtifactRoot <BuildRoot>/artifacts `
  -BuildRoot <BuildRoot> `
  -PackageCache <BuildRoot>/nuget
```

C# test runner and output paths. Observed with .NET SDK 8.0.403:

- `dotnet run ... -p:BaseOutputPath=E:/... -p:BaseIntermediateOutputPath=E:/...` builds into E: but then launches `tests/PrismPlay.Tests/bin/Release/net8.0/PrismPlay.Tests.exe`. If that local copy is older, its result is printed and the new build is never run. This was seen during this batch: the E: build had a failing check, and the command still printed "24 checks passed" from the 4:19 PM local copy. `--property:` behaves the same way. `-property:`, `/p:`, and `/property:` are not applied by `dotnet run` at all, so both build and run use the local `tests/PrismPlay.Tests/bin|obj` on C:.
- Environment variables (as in the block above) apply to both the build and the launch. Another option is `dotnet build` with `-p:` and then `dotnet <BuildRoot>/bin/Release/net8.0/PrismPlay.Tests.dll [--live]`.
- `-p:DefaultItemExcludes=obj/**` is no longer needed. The test project excludes `obj/**` and `bin/**` itself.
- Simplest of all: build `tests/PrismPlay.Tests/PrismPlay.Tests.csproj` with the environment variables set, then run `<BuildRoot>\bin\Release\net8.0\PrismPlay.Tests.exe [--live]` directly, after checking its timestamp. Batch 3 used this method.
- Earlier "24 checks" results run through the `-p:` command came from the local copy, built at 4:19 PM on October 8. None of the runner's sources (`Program.cs`, `Core.cs`, `LyricsProvider.cs`, `DragMath.cs`) changed between that build and this batch, so those counts still applied to the code at that time.

The .NET project invokes the frontend production build and copies bundled assets into Web after build/publish. On a fresh checkout run `npm.cmd ci --prefix frontend` first. Playwright uses installed Microsoft Edge. Stop only the running PrismPlay instance before replacing its loaded binaries.

Useful launch flags: `--inspect` makes the window discoverable on the taskbar for UI inspection; `--diagnostics` writes bounded local diagnostics; `--demo` supplies synthetic playback and separate demo preferences. The app is single-instance.

## Verification evidence

- Release build: zero warnings and errors before the final title-bar/resource refinement; the subsequent self-contained publish containing that refinement succeeded.
- C# runner: 24 offline checks; 25 with the live lookup. The live request for Coldplay's “Hymn for the Weekend” returned 53 timed lines without an imported file.
- Actual app: Spotify “prom dress” by mxmtoon produced 62 timed lines and rendered automatically in the live overlay. Remote-to-native-to-React flow was observed.
- React model: three tests passed earlier in this work.
- Edge/Playwright: all seven interaction tests passed after the heading removal and lyrics viewport change.
- Desktop inspection: new Settings tabs and compact layout were visible and navigable. The final custom title-bar and neutral accent refinement compiled/published but has not received a fresh visual inspection.
- Real Windows media commands previously returned success for seek and play/pause during user testing. Mock UI tests additionally check command dispatch.
- Settings usability pass: Release build had zero warnings and errors, 24 C# checks passed, and the React build plus 3 model tests passed. The built app was run with `--inspect --demo` at 100% scale (96 DPI) only, and Settings was driven with Win32 input. See `docs/VERIFICATION.md` for what was observed and what was not checked.
- Lyrics and playback robustness batch: Release build had zero warnings and errors. The C# runner passed 30 offline checks, built and run from E: (6 new: unwritable and unreadable cache, plain-only cache hit without network, Retry upgrade, upgraded entry persisted, offline Retry keeps plain). With `--live` it passed 31, and the live lookup returned 53 timed lines. `npm run build` succeeded and `npm test` passed 3/3. The playback changes (C, D) are compiled only. They were not exercised against a live Spotify session, and nothing in this batch was checked in the UI.
- Overlay interaction and motion batch: the Release build had zero warnings and errors. The C# runner, `<BuildRoot>\bin\Release\net8.0\PrismPlay.Tests.exe`, was run directly. Its build was up to date (19:30), and no runner source changed in this batch. It passed 30 offline and 31 with `--live` (53 timed lines). `npm run build` succeeded, `npm test` passed 3/3, and Playwright passed **12/12**: the 7 existing tests plus 5 new ones. Four of the new tests failed against the old frontend (size messages, OS reduced motion, keyboard, scale 1.25). The seek test passed on both. The built app was checked with `--inspect --demo` at 96 DPI and scale 1; see `docs/VERIFICATION.md`.
- Native window, tray, and hotkey batch: the Release build of the app and the runner had zero warnings and errors. The runner was launched directly from E: (no runner source changed) and passed 30 offline and 31 with `--live` (53 timed lines). `npm run build` succeeded, `npm test` passed 3/3, and Playwright passed **13/13**: the 12 existing tests plus the new hotkey-chip test. The new test failed against a build without the chip. In the app (`--inspect --demo`, two 96-DPI displays), the following were observed: the chip with a real Ctrl+Alt+G conflict, the chip clearing after a successful Set, the bottom-edge clamp with no intermediate frame, the tray left-click showing a hidden overlay, and the tray menu opening twice and closing on an outside click. The tray messages were simulated. See `docs/VERIFICATION.md`.

- 0.3.1 final pass: the Release build of the app and the runner had zero warnings and errors. The runner was launched directly from E: (no runner source changed; build 19:30) and passed 30 offline and 31 with `--live` (53 timed lines). `npm run build` succeeded, `npm test` passed 3/3, and Playwright passed 13/13. The self-contained publish succeeded and `PrismPlay.exe` reports version 0.3.1. The zero-length drag fix was checked once in the app; see `docs/VERIFICATION.md`.

- 0.3.3 drawer + in-overlay Settings: see the 0.3.3 section above and `docs/VERIFICATION.md`. Release builds 0/0; runner 31 offline (30 + the region spring check); `npm test` 3/3; Playwright 21/21.

## Remaining checks and limitations

1. Settings at 125/150/200% scale and across monitors with different DPI, with transparency effects off, and with Narrator. Title-bar drag, caption buttons, tabs, Esc, Enter, and the save debounce were checked at 100% only. Repackage with `scripts/publish.ps1` (and a clean output folder, see 0.3.1) before handing out a new ZIP. Avoid changing the user's stored preferences unnecessarily.
2. Verify actual click delivery to a borderless game, no focus stealing during updates, tray recovery from a real tray click, and frame pacing while dragging. A hotkey conflict was observed on the card in batch 4. No games are certified.
3. Verify monitor removal, mixed scaling, screen-edge expansion, saved placement, and transparency-disabled fallback.
4. Test the extracted package on a clean Windows 11 x64 machine without development tooling; WebView2 remains a prerequisite.
5. Playback robustness changes on a live Spotify session: transient COM errors keeping the card, Spotify exit still showing Waiting, event bursts during seeks and track skips, and artwork with up-to-date position. The runner cannot load `SpotifyPlayback.cs` because it needs WinRT, so none of this has automated coverage. Ads (S3) and a lyrics negative cache are deferred. Also note that the artwork is keyed by track identity. If Spotify reports a new title before its new thumbnail, the image is not reloaded for that track. This was observed in code only, not confirmed live.
6. Overlay batch 3 open items:
   - Hover, reduced motion, and keyboard expansion were checked in the app at 96 DPI and scale 1 only. Scale 1.25 was checked in Playwright only, and 0.75 not at all.
   - Not checked in the app: the real Windows animation setting (it was emulated through CDP), the light theme, game mode, and opening or closing lyrics.
   - Near the bottom of the work area, the expanded window is clamped and returns to its saved position afterward. The card content now moves up once, at the start of the expansion, instead of in small steps. The brief taskbar overlap was fixed in batch 4 with one pre-clamped `MoveAndResize`.
7. Batch 4 open items:
   - Not verified: swapped mouse buttons, mixed-DPI drag (no mixed-DPI hardware here), game click-through, and tray menu dismissal after a real tray click when the outside click lands on another app or the desktop.
   - The tray tooltip text was not observed: the icon is in the hidden overflow, where UI Automation did not expose it.
   - Not checked on a real tray icon: the left-click and the menu were tested by sending the icon's callback message.
   - No-activate behavior in normal mode was checked by reading the code only. `--demo` and `--inspect` windows can be activated by design.
   - Zero-length header drags and the "Previous shortcut retained" text: fixed in 0.3.1.
8. 0.3.1 open items. Not verified:
   - Game click-through.
   - Mixed-DPI drag (no mixed-DPI hardware here).
   - Swapped mouse buttons.
   - Tray menu dismissal after a real tray click.
   - The Spotify-facing changes from this session (`SpotifyPlayback.cs`, batch 2) against a live Spotify session.
   - Installing and running the 0.3.1 package on a clean machine.
   - The new shortcut text and the save-failure publish were not seen in the app. They compile, and their logic was checked by reading the code only.
   - After a zero-length click, the return of the card to its saved Y on collapse was not observed (see `docs/VERIFICATION.md`).
   - Always-on-top: 0.3.0 and the 0.3.1 ZIP run without `WS_EX_TOPMOST` in `--demo`. This is fixed in source (see "Always-on-top fix" above); the development build reads `WS_EX_TOPMOST` set and sits above a foreground Roblox window in z-order. Still open: in-game use, exclusive fullscreen, Roblox fullscreen, and normal mode. Repackaged as 0.3.2 (see "0.3.2 package").
9. 0.3.3 open items. Not verified: keystrokes reaching Settings in a normal (non-preview) run with real input (the host side was checked: ex-style and the `WM_MOUSEACTIVATE` answer); scales other than 1 and DPI other than 96 in the app (scale 1.25 in Playwright only); a DPI change or monitor move during the region spring; transparency effects off; the lyric import picker, the startup switch, and shortcut recording in the app (Playwright only, against the mocked host); Narrator. The one-sample region/move ordering transient near the bottom edge is described in the 0.3.3 section.
10. Before public redistribution, confirm applicable lyrics/provider rights and terms. Public API availability is not proof of lyric redistribution rights. No lyrics are bundled. Review signing/distribution separately.

The React refraction samples the overlay's own background. It does not capture/refract the game image underneath. Do not claim universal compatibility or measured low CPU/frame-time improvements without measuring them.

## macOS port (`mac/`, branch `mac-port`)

- SwiftPM package. `PrismPlayCore` ports `Core.cs`, `LyricsProvider.cs` and `DragMath.cs` (keep them in step); `PrismPlayCoreTests` mirrors the C# runner's checks (`PRISMPLAY_LIVE=1` adds the live LRCLIB lookup). `PrismPlay` is the AppKit host: `Overlay.swift` (OverlayWindow.cs + NativeWindow.cs), `Spotify.swift` (AppleScript), `main.swift`.
- The React UI is shared. The host adds `platform: "mac"` to the snapshot; `main.jsx`/`style.css` key a few labels and the `[data-platform="mac"]` stage/tint rules on it. Windows sends no field, so it renders exactly as before.
- Window: non-activating `NSPanel` (key only while Settings is open), a continuous-corner container with an `NSVisualEffectView` and a fixed 300×480 (×scale) transparent `WKWebView`. Expand/collapse animates the panel height with `DragMath.spring`; the page never re-lays out. `X`/`Y` in settings.json are the top-left in points, measured down from the primary display's top.
- Bridge: a document-start user script provides `window.chrome.webview` over `webkit.messageHandlers.prismplay`; the page is served from `prismplay://app/` (`WKURLSchemeHandler`), and other navigation is cancelled.
- No Mac here: CI (`.github/workflows/mac.yml`, macos-14) is the compiler. Build errors and test failures become annotations readable from the public check-runs API. Everything that needs a real Mac (window, blur, hover, drag, hotkey, AppleScript, login item) is untested.

## Local state and privacy

Normal preferences: `%LOCALAPPDATA%\PrismPlay\settings.json`. Demo state uses PrismPlay-Demo. Preferences include position, scale, theme, opacity, corners, lyric size/offset/open state, reduced motion, and hotkey.

No app account, telemetry, or hosted backend. Automatic lyrics send title, artist, album, and duration to LRCLIB. Startup uses the current executable path in the user's Run registry key; it is opt-in. Keep the app in a permanent folder if enabling startup.

## Next working step

Open the 0.3.3 package, review the drawer motion and the Settings drawer, then address any concrete visual or behavior feedback. The automatic smooth-lyrics feature is implemented; remaining compatibility checks should stay explicitly documented rather than blocking the user from trying the build.
