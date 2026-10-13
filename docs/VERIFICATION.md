# Verification

## Automated checks

The C# runner passes **31 offline checks** (since 0.3.3, including the critically damped region spring). They cover drag easing, lyric parsing, provider matching, search fallback across album/punctuation differences, version rejection, caching/offline use, plain-text preservation on search failure, cancellation, error states, unwritable and unreadable cache folders, and the Retry upgrade of a cached plain-only entry. An optional `--live` check brings this to **31**: a real automatic LRCLIB request for a known track returned **53 timed lines**, with no local import. Lyrics text is not printed by the test.

The React model suite passes **3 tests** for circular seek geometry, playback interpolation, and lyric/time boundaries. The headless Edge/Playwright suite passes **21 interaction tests** (13 before 0.3.3; see the 0.3.3 section for the rewritten and new ones) against a mocked native bridge, including persistent lyric DOM nodes, smooth centering after forward/backward seeks, reduced motion, and missing/plain lyrics with retry. The other tests cover glass rendering/layout, seek messages, dragging, transport, hover, and game mode. Since batch 3 it also covers native size messages per hover, the OS reduced-motion setting, keyboard expansion, the size posted at scale 1.25, and seek progress while playing or paused. Since batch 4 it also covers the hotkey-conflict chip staying visible while a track plays and changes. These tests check UI behavior and host messages; the optional C# live check verifies actual remote lyric retrieval.

Release build succeeded with **zero warnings and zero errors** on Windows 11 x64 with .NET SDK 8.0.403.

## Observed desktop behavior

- Real Spotify desktop session detected automatically. Track title, artist, artwork, duration, and playback position displayed in the React interface.
- During user testing, the Windows media API returned success for multiple seeks to different positions and for play/pause commands. This was confirmed in the opt-in diagnostic log.
- The actual React surface was inspected using Windows Computer Use. It uses `liquid-glass-react`, a centered playback row between the header and timeline, and a circular thumb with full vertical clearance.
- Dragging no longer changes opacity/backdrop or enters the native modal move loop. The easing math and frontend drag message sequence are covered by tests; end-to-end frame pacing across hardware remains a manual check.
- Original native tray/hotkey and settings integration is retained; game-mode rendering may fall back where layered-window composition suppresses Acrylic.
- Imported lyrics are stored by track identity for future playback. Synthetic demo preferences are isolated from the real overlay's settings.

These observations do not validate actual click delivery through a game, monitor/DPI changes, or a clean Windows installation. Those checks remain below.

## Settings usability pass (October 8, 2026)

Automated, run after the final edit:

- `dotnet build src/PrismPlay/PrismPlay.csproj -c Release` (E: output paths): **0 warnings, 0 errors**.
- C# runner: **24 checks passed**. With E: intermediate paths, this needs `-p:DefaultItemExcludes=obj/**` because of the old `tests/PrismPlay.Tests/obj` folder (see HANDOFF).
- `npm run build` succeeded; `npm test` passed **3/3**. The Playwright suite (`test:ui`) was not run; the frontend was not changed.

Observed in the built `PrismPlay.exe --inspect --demo` on one display at **100% (96 DPI)**. Input was sent with Win32 calls (`SendMessage`, `SetCursorPos`, `mouse_event`, `keybd_event`) from PowerShell. Screenshots are in `<BuildRoot>\screens` (`<BuildRoot>` is defined in HANDOFF "Build environment").

- **Size:** visible frame 446 × 583 px (460 × 590 including the invisible resize borders), the same as before the change. Nothing was clipped on any tab.
- **Title bar:** `WM_NCHITTEST` returns HTCAPTION across the row from x = 6 to 300 DIP and y = 5 to 39 DIP. It returns HTMINBUTTON, HTMAXBUTTON, and HTCLOSE over the caption buttons, and HTCLIENT at y = 44 and in the content. Real mouse drags from the far-left and middle of the row moved the window by exactly the drag distance.
- **Close button:** a real click on it closed Settings.
- **Tabs:** clicking Lyrics switched pages. With a tab focused, Right moved to General and showed the focus ring and underline, and repeated arrows wrap in both directions. Tab moved through the tabs, then the General controls (toggle, shortcut box) in order.
- **Esc** closed Settings, including straight after opening with nothing in the content focused.
- **Enter** in the shortcut box showed "Shortcut saved." (the value was left at G).
- **Debounce:** 8 rapid Scale arrow presses (each now 0.05 after the `SmallChange` fix) caused no writes to `settings.json` during the burst and one write about 400 ms after the last key. Pressing Esc 40 ms after a change wrote the file 3 ms later. Closing the overlay 70 ms after a change also saved the new value.
- **Stale Scale:** clicking the overlay's scale button while Settings was open closed Settings and saved the new scale.
- The demo preferences were returned to their starting values. Real preferences, the Startup registry value, and Spotify were not touched.

Not verified: 125/150/200% scaling, moving between monitors with different DPI, a small work area, transparency effects off, Acrylic unsupported (opaque fallback), Narrator or other UI Automation output, an open ComboBox consuming Esc, and the packaged/published build.

## Lyrics and playback robustness batch (October 8, 2026)

Automated, run after the final code edit:

- `dotnet build src/PrismPlay/PrismPlay.csproj -c Release` (E: output paths): **0 warnings, 0 errors**.
- C# runner, built into and launched from `<BuildRoot>\bin` using the environment-variable form in HANDOFF: **30 checks passed**. With `-- --live`: **31 passed**, and the live LRCLIB lookup returned **53 timed lines**. The network was available.
- The 6 new checks: a read-only `.tmp` cache file (`UnauthorizedAccessException` on write) still returns downloaded lyrics; an exclusively locked cache file (`IOException` on read and replace) falls back to the network; a cached plain-only entry is served with no network request; Retry (`allowPlainUpgrade`) upgrades it when the search has a synced row; the upgrade is persisted and is served offline; an offline Retry keeps the cached plain lyrics.
- The unwritable-cache check failed against the old code ("Unwritable lyrics cache still returns downloaded lyrics") and passes now. The other new checks were not run against the old code; the Retry checks need the new parameter.
- The compile command line now lists only `Program.cs` and the three linked sources, not the stale `tests/PrismPlay.Tests/obj` files. No `DefaultItemExcludes` flag was passed.
- `npm run build` succeeded; `npm test` passed **3/3**. Playwright (`test:ui`) was not run; the frontend was not changed.

Runner command finding: `dotnet run` with `-p:BaseOutputPath=E:/...` builds into E: but launches the local `tests/PrismPlay.Tests/bin` copy. It printed "24 checks passed" while the E: build had a failing check. See HANDOFF for the details and the working command.

Not verified: the `SpotifyPlayback.cs` changes (keeping the snapshot on transient errors, `Waiting` only when the session is no longer listed, event coalescing, and timeline/info re-read after artwork). These compile but cannot run in the test runner because they need WinRT. They were not observed with a live Spotify session. The Retry button was not clicked in the app; the Retry path was checked only at the provider level. No UI verification was done. Ads (S3) were not changed.

## Overlay interaction and motion batch (October 8, 2026)

Automated, run after the final code edit:

- `dotnet build` of the app and of the test runner, Release, E: output paths: **0 warnings, 0 errors**. No C# file changed in this batch.
- C# runner, launched directly as `<BuildRoot>\bin\Release\net8.0\PrismPlay.Tests.exe`. The build was up to date, timestamped 19:30 on October 8, and no runner source changed. Results: **30 passed**, and with `--live` **31 passed**, with 53 timed lines.
- `npm run build` succeeded; `npm test` passed **3/3**; `npm run test:ui` passed **12/12** (7 existing + 5 new) on a fresh build.
- The new tests were first run against the unchanged frontend. Four failed as expected:
  - One hover sent 19 `size` messages (167, 177, 184, 200, ..., 157, 156) instead of `[200, 156]`.
  - The OS reduced-motion setting left transitions on.
  - Tab did not expand the card.
  - At scale 1.25 the last posted size was 156 instead of 195.
  
  The seek-progress test passed on both versions; it guards the new CSS-variable path.
- Probes run once and then deleted:
  - Root zoom 1.25 in Edge 154 gives `getBoundingClientRect().height` 195 for the 156 px stage. Hit testing is inside at y = 194 and outside at y = 196. A ResizeObserver fired 0 times on the zoom change.
  - A stub React DevTools hook counted 3 commits in 3 s of playback.

Observed in the built `PrismPlay.exe --inspect --demo`: one display, 96 DPI, scale 1, work area 1920 × 1032. Pointer input came from `SetCursorPos` and `mouse_event`. Keyboard input and media emulation went to the app's own WebView2 over CDP. The app was started with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9229`, so no keys were sent to other windows. Screenshots `20`–`31` are in `<BuildRoot>\screens`.

- **Hover resizing:** the window height was polled every 2 ms over 5 hover cycles. Each cycle had exactly one change each way. 156 → 200 came 4–16 ms after the pointer entered. In 4 cycles, 200 → 156 came 509–522 ms after the pointer left, which is the 280 ms collapse delay plus 200 ms. In the first cycle it came at 235 ms, which was not explained. The expanded card was not clipped (`21`).
- **Bottom edge:** the demo profile's Y was set to 862 temporarily. Expanding moved the window to Y = 832, so it ends at the work-area bottom (`30`). Collapsing returned it to 862. One poll sample caught 200 px at Y = 862 between the resize and the clamp move. The demo `settings.json` was restored byte for byte afterward (hash compared).
- **Seek:** in demo playback the label went from 0:55 to 0:57 and the thumb moved (`23`, `24`). Pause stopped both.
- **Keyboard:** Tab expanded the card, and the window grew to 200. After four more Tabs, Game mode had a visible focus ring (`26`). Blur collapsed the card to 156 (`27`).
- **OS reduced motion:** emulated through CDP `Emulation.setEmulatedMedia`; the Windows setting was not changed. The media query matched. `.stage` and `.extras` transitions were 0s with the app setting off, and the shrink had no 200 ms delay (`28`).
- **Click, then leave:** the clicked Play button did not match `:focus-visible`. With continuous pointer motion out of the card, it collapsed (`25`). A synthetic `SetCursorPos` jump straight after a click left Chromium's `:hover` set, and the card stayed expanded (`25a`). This is hover state, not the new focus code. It was not compared with the old build.
- **Text:** the 10 px `PAUSED` and `NOW PLAYING` labels fit at scale 1 (`20`, `23`).
- No PrismPlay instance remains running. Real preferences, Windows settings, and Spotify were not touched.

Not verified:
- In the app: scales other than 1 (1.25 only in Playwright, 0.75 not at all), DPI other than 96, the real Windows animation setting, the light theme, game mode, and lyric open/close transitions.
- Keyboard use in normal (non-preview) mode. The window does not take focus there; see HANDOFF.
- CPU and frame-time effects of these changes. None were measured beyond the commit count above.

## Native window, tray, and hotkey batch (October 8, 2026)

Automated, run after the final code edit:

- `dotnet build` of the app and of the test runner, Release, E: output paths: **0 warnings, 0 errors**.
- C# runner, launched directly as `<BuildRoot>\bin\Release\net8.0\PrismPlay.Tests.exe`. Its build is up to date (19:30); no runner source changed, and no check was added. Results: **30 passed**, and with `--live` **31 passed**, with 53 timed lines.
- `npm run build` succeeded; `npm test` passed **3/3**; `npm run test:ui` passed **13/13** (12 existing + 1 new).
- The new test was first run against a build with the chip line temporarily removed from `main.jsx`. It failed with "element(s) not found". The file was then restored (hash identical) and `dist` rebuilt.

Observed in the built `PrismPlay.exe --inspect --demo`. Setup: two displays, both 96 DPI, primary work area 1920 × 1032, `SM_SWAPBUTTON` = 0. The demo profile's X/Y were temporarily set to 1560/862. X was moved because another always-on-top window covered the left part of the screen. The demo `settings.json` was restored byte for byte afterward (hash compared). Screenshots `40`–`49` are in `<BuildRoot>\screens`.

- **Hotkey conflict.** Before launch, a helper PowerShell process registered Ctrl+Alt+G as a thread hotkey. The card showed the chip "Ctrl+Alt+G in use" (`40`), and it stayed while playing showed NOW PLAYING (`41`, `42`). Settings was opened with a simulated tray double-click; the General tab and **Set** were invoked through UI Automation. While G was still held, Set showed "Shortcut in use. Previous shortcut retained." and the chip stayed (`48`). After the helper released G, Set showed "Shortcut saved." and the chip disappeared (`49`).
- **Bottom edge.** The window rectangle was polled about every 1 ms over 3 hover cycles. Each cycle went 862/156 → 832/200 → 862/156 with no intermediate sample. The batch 3 run caught one sample of 200 px at Y = 862. The expanded bottom is 1032, the work-area bottom (`42`, `43`). The foreground window did not change during the cycles.
- **Tray left-click.** Simulated by sending the icon's callback message (`0x8001` with `WM_LBUTTONUP`) to the overlay; the real icon was not clicked. After `ShowWindow(SW_HIDE)`, the message made the window visible again at the same rectangle (`44`, `45`). With PrismPlay's Settings window in the foreground, the same message left Settings in the foreground.
- **Tray menu.** Opened twice by posting the callback with `WM_RBUTTONUP`. Both times a visible `#32768` menu window appeared (`46`, `47`), and a click outside the menu closed it.
  - Limits: the outside click landed on the overlay itself, which owns the menu. The overlay was already the foreground window, and the shell did not send the message.
  - That click also reached the header as a zero-length drag and saved Y = 832; see HANDOFF.
- **Tray tooltip.** Not observed. The icon is in the hidden overflow, and UI Automation found only the `--inspect` taskbar button.
- No PrismPlay instance or helper process remains running. Real preferences, Windows settings, and Spotify were not touched.

Not verified:
- Swapped mouse buttons. The change follows the `GetAsyncKeyState` documentation only.
- Mixed-DPI dragging: both displays are 96 DPI. The post-drag region refresh was confirmed by reading the code only.
- Game click-through.
- Tray menu dismissal after a real tray click, when the outside click lands on another app or the desktop.
- The tray tooltip text.
- No-activate behavior in normal (non-preview) mode. `--demo` and `--inspect` windows can be activated by design, so this was checked by reading the code only.

## 0.3.1 final pass (October 8, 2026)

Automated, run after the final code edit:

- `dotnet build` of the app and of the test runner, Release, E: output paths: **0 warnings, 0 errors**.
- C# runner, launched directly as `<BuildRoot>\bin\Release\net8.0\PrismPlay.Tests.exe`. Its build is up to date (19:30); no runner source changed, and no check was added. Results: **30 passed**, and with `--live` **31 passed**, with 53 timed lines.
- `npm run build` succeeded; `npm test` passed **3/3**; `npm run test:ui` passed **13/13**.
- `scripts/publish.ps1` with the E: arguments succeeded, into a freshly emptied output folder. `PrismPlay.exe` file and product version: 0.3.1.

Observed in the built `PrismPlay.exe --inspect --demo` (development build, not the ZIP). Setup: two 96-DPI displays, primary work area 1920 × 1032. The demo profile's X/Y were temporarily set to 1560/862, and the demo `settings.json` was restored byte for byte afterward (hash compared). Input came from `SetCursorPos` and `mouse_event`.

- **Zero-length click (fix 1).** A press and release on the header with no movement expanded the card, and the window went to Y = 832 (the display clamp). `settings.json` stayed at Y = 862, with the same hash as before the click. Before the fix, the same click saved Y = 832 (batch 4).
- **Real drag still saves.** A 40 px upward drag from the expanded position moved the window to Y = 792, and `settings.json` was written with Y = 792.
- Not observed: the card returning to Y = 862 on collapse after the zero-length click. `SetCursorPos` jumps did not end the hover (the batch 3 Chromium quirk). Two retries with continuous `mouse_event` motion had no effect on the overlay. A check afterward found that `RobloxPlayerBeta` was the foreground window and covered that area, so the retries' pointer moves, click, and 40 px drag probably went to that window. Input injection was stopped then.
- Not seen in the app: the new shortcut text in Settings and the save-failure message. Both were checked by reading the code only.

Package hash and smoke test: recorded in the workspace copy of this file after the archive was built.

- **Package.** the 0.3.1 package in `<BuildRoot>\artifacts`, copied to the workspace `artifacts` folder. Size 85,327,802 bytes. SHA-256 `1C587748784F16921FFEEEFF1661BABAE8539AB2DC10D00A5FBABAB810C27A57` (the two copies have the same hash). It has 511 files, and `Web/assets` holds only the current bundle (`index-53HuwKe6.css`, `index-BOxK3oDv.js`).
- **Smoke test.** The archive was extracted into the new, empty folder `<BuildRoot>\smoke-0.3.1`, and `PrismPlay.exe --demo` was started from it. It was sampled every 2 s for 10 s. From 4 s on, the process was running, `IsWindowVisible` was true, the window was not DWM-cloaked, and it was 300 × 156 at the demo position (80, 80). It closed through `WM_CLOSE` with exit code 0. Only that instance was started and stopped, and the demo `settings.json` hash was unchanged.
- **Smoke-test limit: the window was not on top.** At the overlay's center, `WindowFromPoint` returned the foreground `RobloxPlayerBeta` window, and a screen capture of the area showed the game, not the card. The overlay's extended style was `0x100`, without `WS_EX_TOPMOST`. The 0.3.0 package shows the same `0x100` in `--demo`, so this is not new in 0.3.1. Normal (non-demo) mode was not checked, because that would use the real preferences.

## Always-on-top fix (October 8, 2026; development build, not packaged)

No mouse or keyboard input was sent. All readings came from `GetForegroundWindow`, `GetWindowLongPtr(GWL_EXSTYLE) & 0x8`, and a z-order walk with `GetTopWindow`/`GetWindow(GW_HWNDNEXT)`, run by a PowerShell probe. Only window messages posted to the overlay's own hwnd were used: `WM_DISPLAYCHANGE`, the tray callback `0x8001` with `WM_LBUTTONUP` (which calls `Show()`), and `WM_CLOSE`. The foreground window throughout was `RobloxPlayerBeta` (`Roblox`, extended style `0x0`, not topmost); it stayed in the foreground in every run. Each launch was `--demo` (plus `--diagnostics` for the in-process log), and only the launched instance was closed. The demo `settings.json` hash was unchanged, and the real `PrismPlay\settings.json` was not touched.

- **Before (unfixed build):** extended style `0x100`, `WS_EX_TOPMOST` clear, overlay below Roblox in z-order. It was still clear after the posted display change and Show callback. A `SetWindowPos(HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE)` from the probe returned TRUE and left the bit clear.
- **Diagnosis (temporary in-process logging, since removed):** presenter `Overlapped`, `IsAlwaysOnTop = true`, bit clear at constructor end and after `Activate()`. In-process `SetWindowPos(HWND_TOPMOST)` also left it clear. `IsAlwaysOnTop = false` then `true` set it.
- **After (fixed build), `diagnostics.log`:** `WS_EX_TOPMOST False -> True` at constructor end; `True -> True` after `Window activated`; `True -> True` twice after the posted display change and Show callback.
- **After, probe:** extended style `0x108`, `WS_EX_TOPMOST` set, overlay z-index 17 versus Roblox 64, so the overlay is above the foreground window. This was unchanged after the posted messages.
- Builds 0/0. Runner: 30 passed, 31 with `--live`. `npm run build` OK, `npm test` 3/3, `npm run test:ui` 13/13.
- Not verified: whether the card is visible while a game is being played, exclusive-fullscreen games, Roblox in true fullscreen, normal (non-demo) mode, a monitor change in practice (only a posted `WM_DISPLAYCHANGE`), and a repackaged ZIP.


## 0.3.3 drawer + in-overlay Settings (October 9, 2026)

Automated, run after the final code edit:

- `dotnet build` of the app and of the test runner, Release, E: output paths: **0 warnings, 0 errors**.
- C# runner, built with the environment-variable form and launched directly as `<BuildRoot>\bin\Release\net8.0\PrismPlay.Tests.exe`: **31 passed** (30 existing + "Region spring rises without overshoot, settles by 0.6 s, and retargets without a jump"). `--live` was not run.
- `npm test` **3/3**; `npm run build` succeeded; `npm run test:ui` **21/21**. The five stepped-size tests were rewritten for the new contract (one `size` per change with `animate`; drawer laid out at full height and kept mounted until the spring settles; reduced motion mounts/unmounts in the same commit; scale 1.25 posts 195/455 and renders that many pixel rows). New: the Settings drawer from `settingsOpen`; every control's message, with Scale only on release or per key step; Esc and shortcut recording; the lyrics↔Settings swap. Two existing timing assumptions were made robust (wait for the selected-button background transition; sample drawer transitions until settled). One motion test asserted a mid-transition frame in a fixed band and failed once under load in headless Edge (first frame after the drawer mounts can take over 100 ms there); it now accepts any midway frame. After the change: three consecutive full runs 21/21.

Observed in the built `PrismPlay.exe` (development build, E:). Setup: two 96-DPI 1920 × 1080 displays (primary work area 1920 × 1032), primary at 179 Hz, scale 1. Input to the page went over CDP to the app's own WebView2 (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9229`); native readings came from `GetWindowRect`, `GetWindowRgnBox`, `GetWindowLongPtr`, `GetForegroundWindow`. No mouse or keyboard input was injected into Windows. Screenshots `01`–`10` are in `<BuildRoot>\screens\033` (`033-pw` holds headless renders).

- **Frame and region.** `--inspect --demo`: window 300 × 480 at every state; region 156 collapsed (`01`), 200 on hover (`02`), 408 with lyrics (`03`), 480 with Settings on each tab (`04`–`06`) and in the Light tint (`07`).
- **Smoothness**, polled from a separate process about every 1 ms with no in-process logging. Hover 364→408, lyrics close 408→200, lyrics open 200→408, and collapse 408→364 were all monotonic. Steps were at most 7 px, at a cadence of about 5.5 ms (one per display frame at 179 Hz), with no gaps. The spring timing matched the formula: 404 of 408 px reached at 0.4 s.
- **WebView frames during drawer transitions** (rAF in the app's page; lyrics, Settings, and the lyrics↔Settings swap, 8 transitions): about 6 ms frames, worst 13 ms. Heights were monotonic and ended exactly at 364/480/156.
- **With `--diagnostics` per-frame logging** (a temporary line, since removed): one close showed a 230 ms gap (408 → 230 with no frames between). External polling without the log showed none, so the log line was removed.
- **Bottom edge.** Demo X/Y were set to 1560/876 (card bottom on the work-area bottom). Hover, then lyrics open: Y went 876 → 832 → 624 in step with the region (Y + region = 1032 at every settled sample), then back 624 → 832 → 876 on close and collapse. There was no up-front jump (`08`–`10`). The poll sometimes saw the new region one sample (≤ 2 ms) before the matching Y.
- **Reduced motion** (app switch in Settings): lyrics open/close changed the region in one step each way (364 → 156 → 364).
- **Esc** over CDP closed Settings; the drawer fell back to the open lyrics.
- **Game mode interplay**, using window messages posted to the overlay only (`WM_HOTKEY`; tray callback `0x8001` with `WM_LBUTTONUP` + `WM_LBUTTONDBLCLK`):
  - Hotkey with Settings open: Settings closed, game mode on (ex-style `0x80128`).
  - Tray double-click in game mode: Settings open, game mode suspended (`0x108`, region 480).
  - Esc: Settings closed, game mode restored (`0x80128`).
  - Hotkey: game mode off (`0x108`).
- **Normal (non-preview) run, focus.**
  - Default: ex-style `0x8000188` (`WS_EX_NOACTIVATE` set); `SendMessage(WM_MOUSEACTIVATE)` returned 3 (`MA_NOACTIVATE`).
  - Settings opened via the overlay button (DOM click over CDP): `0x188`, `MA_ACTIVATE` (1), foreground unchanged.
  - Settings closed: back to `0x8000188` and 3.
  - Keystrokes with real input were **not** tested, because real input was in use on the machine (see below).
- **Other interaction on the machine during these runs** (observed, cause not determined):
  - The demo instance's diagnostics log recorded hover and a lyrics open at times when no automation was running.
  - The demo and normal instances were moved by hundreds of pixels several times. The drag code follows the physical cursor and needs the physical primary button held (`GetCursorPos`, `GetAsyncKeyState`), and no automation sent a header press.
  - The cursor was sampled resting over the overlay.
  - During a 45 s passive watch of the normal instance, Settings opened and the overlay became the foreground window 2.6 s later. That is consistent with a real click activating the open panel. No keydown reached the page.
  - The normal instance then exited cleanly at about 17:20:55, with no Application-log error. No automation closed it.
- **Prefs.** The real `settings.json` had been rewritten by that instance (corners 29, lyrics open, X/Y 1414/523). It was restored from the backup taken before the run, and the hash matched (`CEE695BF…`). The demo `settings.json` was also restored (`8C51D4C7…`). The real `lyrics-cache` gained an entry from the normal run. The HKCU Run key has no PrismPlay value; its earlier state was not recorded.

- **Package:** the 0.3.3 package in `<BuildRoot>\artifacts` (a copy of the script's `PrismPlay-win-x64.zip`; the generated `artifacts\PrismPlay-win-x64` folder was deleted before `scripts/publish.ps1` ran with the E: arguments). Size 85,332,992 bytes. SHA-256 `A63A01509EA42C2DADED0738A903B8D6C29FD0D903BC69BE931D65A837B00A41`. 511 files; `Web/assets` holds only `index-CdxHRive.js` and `index-CuH6djj2.css`; `SettingsWindow`/`Motion` are absent. `PrismPlay.exe` file version 0.3.3.0, product version 0.3.3. **Only on E:** (C: has about 3 GB free; not copied into the workspace `artifacts`).
- **Smoke test:** extracted into the new folder `<BuildRoot>\smoke-0.3.3`; `PrismPlay.exe --demo` showed its window after 3 s and was sampled every 2 s for 10 s: running, visible, not cloaked, frame 300 × 480 at (80, 80), region 156, extended style `0x108` at every sample. `WM_CLOSE` → exit code 0. The demo `settings.json` hash was unchanged. The copies of this file and `VERIFICATION.md` inside the ZIP predate the archive and lack these two entries.

Not verified: real keystrokes in Settings in a normal run; scales other than 1 and DPI other than 96 in the app; DPI or monitor changes during the spring; transparency effects off; the lyric import picker, the startup switch, and shortcut recording in the app (Playwright only); Narrator; the published package beyond the smoke test below.

## 0.3.4 smooth edges (October 9, 2026)

Builds 0/0; runner 31; `npm test` 3/3; Playwright 21/21 (two `.tint` references updated). Headless Edge renders over white and over busy stripes were legible with tint ×1.2. The first build showed a #202020 rectangle round the card in the app; fixed as described in HANDOFF (WebView2 theme brush). After the fix, in `--demo` over a white form: pixels outside the corners #FFFFFF, also in game mode (`0x80128`); screenshots `<BuildRoot>\screens\034\fixed-corner.png` and `fixed-gamemode.png`. Game-mode click-through with real input, light-theme Windows, and a smoke launch of the republished ZIP were not checked.
Package (republished after the transparency fix): the 0.3.4 package in `<BuildRoot>\artifacts`, 85,336,008 bytes, SHA-256 `F278FEE593D5EBA4A0EFBA6FBF89C796EC158D6394236851F24DFFBE5151130E`, 511 files (the docs inside the ZIP predate these lines).

## Live acceptance checklist

- Spotify: launch/exit, pause/resume, next/previous, seeking, ads, artwork replacement, missing timeline, and reconnect.
- Interaction: hover/collapse, dragging, scale, persistent lyrics, game-mode hotkey, actual click-through, tray recovery, and no focus theft.
- Displays: move between different DPI monitors, disconnect a monitor, restore saved position, expand near an edge.
- Lyrics: import a valid LRC, plain text fallback, adjust timing, seek backward/forward, rapidly change tracks during import/lookup, offline and no results.
- Appearance: bright/dark content, light/dark/album themes, transparency disabled, reduced motion.
- Resource use: idle waiting/paused, active playback, visible/hidden lyrics. No repeating UI timer should run while paused or waiting.
- Packaging: launch the extracted self-contained package on Windows 11 x64 without developer tools installed.

No games have been certified. A successful desktop smoke test does not establish universal borderless-game or anti-cheat compatibility.
