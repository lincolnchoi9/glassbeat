# GlassBeat

A small glass Spotify overlay for Windows 11. Drag the header to place it, hover to reveal controls, and press **Ctrl+Alt+G** to toggle click-through game mode. Works with the Spotify desktop app on the same computer; no Spotify login is needed.

## Run

Extract the entire `GlassBeat-win-x64.zip` to a permanent folder and run `GlassBeat.exe`. Keep all included files, including the `Web` folder, together. The download includes .NET and Windows App SDK dependencies. Windows 11 x64 and Microsoft Edge WebView2 Runtime are required; WebView2 normally ships with Windows 11. This development build is unsigned.

The overlay stays visible while paused or waiting for Spotify. Previous/play/next sit between the track details and timeline. Hover to reveal lyrics, scale, game mode, and settings. Drag the artwork or track text to move it, with a short easing response and no dark dragging state. Click the timeline or drag its circular handle to seek when Spotify exposes that capability. The scale button cycles presets; Settings offers finer control. Lyrics and Settings open as a drawer that extends out of the bottom of the card, in the same glass; Esc or the Settings button closes Settings. While Settings is open, game mode is paused and a click inside the panel lets the overlay take keyboard focus.

The overlay uses [rdev/liquid-glass-react](https://github.com/rdev/liquid-glass-react), pinned to 1.1.1, with album-derived color, refraction, and subtle highlights. Refraction samples the app's own background; it does not capture or refract the game beneath the window. The window itself is transparent, so the card has smooth rounded edges; there is no blur of what is behind it, and the tint (Opacity) keeps text readable. The native window frame is removed. Reduced motion disables easing and UI transitions.

**Game mode:** mouse clicks pass through the card. Hover controls and dragging are disabled. Press the shortcut again, or right-click the GlassBeat tray icon and choose **Exit game mode**. The tray also offers Show, Settings, and Quit. A conflicting shortcut is reported on the card and can be changed in Settings. Exclusive fullscreen and anti-cheat compatibility are not promised; select borderless/windowed mode in the game.

**Lyrics:** click the lyrics button in the hover tray. Lyrics are fetched automatically from LRCLIB when Spotify changes tracks—no account, API key, or imported file is needed. Timed lines glide into the center and crossfade in emphasis as playback advances or seeks. Reduced motion removes the transitions. If only plain lyrics exist, they are labeled unsynced; unavailable tracks show a clear message and a retry button. Positive timing offsets in Settings advance lyrics. Local `.lrc` imports remain optional overrides. Coverage depends on LRCLIB; no lyrics files are bundled with the app.

**Customization:** scale, opacity, album/dark/light color, roundness, lyrics size and offset, reduced motion, and the Ctrl+Alt shortcut key. Startup is opt-in and uses the current executable location. Turn startup off before moving/removing the app.

Settings live in `%LOCALAPPDATA%\GlassBeat\settings.json`. There is no telemetry, account, or app backend. Automatic lyrics send track title, artist, album, and duration to LRCLIB. Up to 100 matched responses are cached locally, so previously fetched lyrics work offline.

## Build and test

Requires .NET 8 SDK and Node.js 22.12+ on Windows. NuGet restores Windows App SDK 1.8 and Windows SDK tools. React assets are bundled automatically by the .NET build; no development server is needed to run the app.

```powershell
npm.cmd ci --prefix frontend
dotnet build src/GlassBeat/GlassBeat.csproj -c Release
dotnet run --project tests/GlassBeat.Tests/GlassBeat.Tests.csproj -c Release
npm.cmd test --prefix frontend
npm.cmd run test:ui --prefix frontend
./scripts/publish.ps1
```

The publish script produces `artifacts/GlassBeat-win-x64.zip`. Optional `-ArtifactRoot`, `-BuildRoot`, and `-PackageCache` arguments redirect large build files to another drive.

Run `GlassBeat.exe --demo` for a synthetic playback preview (it does not connect to Spotify and uses separate `GlassBeat-Demo` preferences). Close any existing instance first. `--inspect` gives the real overlay a normal taskbar presence for UI inspection. `--diagnostics` writes a local startup/capability log without track titles or artists.

Updates are manual: quit, replace the extracted app folder, and restart. Preferences remain in Local AppData. To uninstall, disable startup, quit, and remove the extracted folder; preferences and imported lyrics can optionally be removed separately.

## Architecture

- `IPlaybackService`: Windows media sessions, Spotify filtering, capability flags, timestamped playback snapshots, and transport requests.
- `ILyricsProvider`: automatic LRCLIB lookup plus a search fallback for album/punctuation differences, title/artist/duration validation, bounded cache, cancellation, and explicit unavailable/instrumental/plain states. Late responses cannot replace lyrics for a newer track.
- `IOverlayInteraction`: native topmost/no-activate window, click-through mode, drag, display clamping, hotkey, and tray recovery.
- `IPreferencesStore`: local JSON with input normalization and replace-on-save.
- React 19 overlay using `liquid-glass-react` in WebView2. A local-only message bridge connects transport, seeking, settings, and window dragging to the native host. All scripts and images are bundled/local; CSP blocks external web connections and navigation.
- WinUI 3 host; Settings is part of the React overlay. The transparent window frame has a fixed size; the rounded CSS card is the visible shape, and a rectangular window region limits hit-testing to it. Native movement uses a frame-rate-independent easing curve and never enters the blocking Windows modal move loop or changes the glass while dragging.

See [verification notes](docs/VERIFICATION.md) for tested behavior and remaining live checks.
