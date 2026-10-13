import AppKit
import Carbon.HIToolbox
import PrismPlayCore
import QuartzCore
import ServiceManagement
import UniformTypeIdentifiers
import WebKit

/// The macOS host (OverlayWindow.cs + NativeWindow.cs). macOS owns playback and windowing; the bundled React app owns the overlay.
///
/// Window: a borderless non-activating panel. Its content is a rounded, continuous-corner container holding a behind-window blur
/// and, on top, a transparent WKWebView with a FIXED frame of 300 × 480 (× scale), anchored to the top. Expanding and collapsing
/// animate the PANEL's height with the analytic spring, so the web page never re-lays out; the panel edge is the visible edge.
///
/// Coordinates: Preferences.x/y are the card's top-left in points, measured right and down from the primary display's top-left
/// (as on Windows). AppKit's global space is y-up from the primary display's bottom-left: frame.maxY = primaryTop - y.
final class Overlay: NSObject {
    /// NativeWindow.FrameHeight: 156 card + 44 hover row + 280 Settings drawer (FRAME in frontend/src/main.jsx).
    static let frameHeight = 480.0
    private let demo = CommandLine.arguments.contains("--demo")
    private let store: PreferencesStore
    private var prefs: Preferences
    private let imports: LocalLyricsStore
    private let lyricsProvider: LyricsProvider
    private let spotify = SpotifyPlayback()
    private let panel = OverlayPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
    private let container = FlippedView()
    private let effect = NSVisualEffectView()
    private let web: OverlayWebView
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let gameItem = NSMenuItem(title: "Enter game mode", action: #selector(Overlay.toggleGameMode), keyEquivalent: "")

    private var playback = Playback.waiting()
    private var lyrics = Lyrics.empty("Play a song to see lyrics")
    private var lyricsTask: Task<Void, Never>?
    private var lyricsGeneration = 0
    // Settings is a drawer of the overlay; the host owns whether it is open.
    private var settingsOpen = false, suspendedGameMode = false, startup = false, gameMode = false
    private var ready = false, moving = false, seekRejected = false
    private var status = "Open Spotify to get started", hotkeyError = "", lyricsStatus = "", generalStatus = ""
    private var logicalHeight = 156.0
    private var commandVersion = 0
    private var saveTimer: Timer?

    // Visible-height spring (points), matching the CSS --spring. A retarget restarts it from the sampled height and velocity.
    private var frameWidth = 0.0, webHeight = 0.0
    private var springFrom = 0.0, springVelocity = 0.0, springTarget = 0.0, springHeight = 0.0, springStart = 0.0
    private lazy var springTicker = Ticker { [unowned self] in self.springFrame() }
    // Header drag: the panel follows the pointer with DragMath easing until the primary button is released.
    private lazy var dragTicker = Ticker { [unowned self] in self.dragFrame() }
    private var pointerDragging = false
    private var dragPointerOrigin = NSPoint.zero, dragWindowOrigin = NSPoint.zero
    private var dragX = 0.0, dragY = 0.0, targetX = 0.0, targetY = 0.0, lastDragFrame = 0.0

    private var hotkeyRef: EventHotKeyRef?
    private var hotkeyKey: Character?
    private var hotkeyID: UInt32 = 0

    override init() {
        let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent(CommandLine.arguments.contains("--demo") ? "PrismPlay-Demo" : "PrismPlay")
        store = PreferencesStore(folder: folder)
        prefs = store.load()
        imports = LocalLyricsStore(folder: folder.appendingPathComponent("imported-lyrics"))
        lyricsProvider = .live(cache: folder.appendingPathComponent("lyrics-cache"), imports: imports)
        let content = WKUserContentController()
        content.addUserScript(WKUserScript(source: Self.bridge, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        let config = WKWebViewConfiguration()
        config.userContentController = content
        config.setURLSchemeHandler(WebAssets(root: Bundle.main.resourceURL!.appendingPathComponent("Web")), forURLScheme: "prismplay")
        web = OverlayWebView(frame: .zero, configuration: config)
        super.init()
        content.add(self, name: "prismplay")

        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        panel.isOpaque = false; panel.backgroundColor = .clear; panel.hasShadow = true
        panel.hidesOnDeactivate = false; panel.isMovable = false; panel.isReleasedWhenClosed = false
        panel.acceptsMouseMovedEvents = true; panel.animationBehavior = .none
        container.wantsLayer = true
        container.layer?.cornerCurve = .continuous
        container.layer?.masksToBounds = true
        effect.material = .hudWindow; effect.blendingMode = .behindWindow; effect.state = .active
        effect.autoresizingMask = [.width, .height]
        container.addSubview(effect); container.addSubview(web)
        panel.contentView = container
        web.setValue(false, forKey: "drawsBackground")
        web.underPageBackgroundColor = .clear
        web.navigationDelegate = self
        if #available(macOS 13.3, *) { web.isInspectable = CommandLine.arguments.contains("--inspect") }
        resize(300 * prefs.scale, logicalHeight * prefs.scale)
        panel.orderFrontRegardless()

        statusItem.button?.image = Self.menuBarGlyph
        let menu = NSMenu()
        for item in [gameItem, NSMenuItem(title: "Show overlay", action: #selector(showOverlay), keyEquivalent: ""),
                     NSMenuItem(title: "Settings", action: #selector(openSettings), keyEquivalent: ""), NSMenuItem.separator(),
                     NSMenuItem(title: "Quit PrismPlay", action: #selector(quit), keyEquivalent: "")] {
            item.target = self; menu.addItem(item)
        }
        statusItem.menu = menu

        hotkeyPressed = { [weak self] in self?.toggleGameMode() }
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in hotkeyPressed?(); return OSStatus(noErr) }, 1, &spec, nil, nil)
        _ = setHotkey(Character(prefs.hotkey))

        NotificationCenter.default.addObserver(self, selector: #selector(screensChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(terminating), name: NSApplication.willTerminateNotification, object: nil)
        spotify.changed = { [weak self] in self?.receive($0) }
        web.load(URLRequest(url: URL(string: "prismplay://app/index.html")!))
        if demo {
            receive(Playback(track: Track("Weightless", "PrismPlay · preview session", "After hours", 213), position: 54,
                             canPlay: true, canPause: true, canPrevious: true, canNext: true, canSeek: true))
            lyrics = .parse("[00:00.00]Let the world slow down\n[00:50.00]We float above the city lights\n[01:00.00]And leave the noise behind", nil)
        } else {
            spotify.start()
        }
    }

    /// The prism play triangle as a monochrome template (Apple's menu bar convention), drawn at 18 pt.
    private static let menuBarGlyph: NSImage = {
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { _ in
            let prism = NSBezierPath()
            prism.move(to: NSPoint(x: 4.5, y: 15)); prism.line(to: NSPoint(x: 15.5, y: 9)); prism.line(to: NSPoint(x: 4.5, y: 3)); prism.close()
            prism.lineWidth = 2.4; prism.lineJoinStyle = .round
            NSColor.black.setStroke(); prism.stroke()
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "PrismPlay"
        return image
    }()

    // MARK: Bridge

    /// `window.chrome.webview` as main.jsx expects it (WebView2's shape), backed by a WebKit message handler.
    private static let bridge = """
    (() => {
      const listeners = new Set();
      window.chrome = window.chrome || {};
      window.chrome.webview = {
        postMessage: message => window.webkit.messageHandlers.prismplay.postMessage(message),
        addEventListener: (type, listener) => { if (type === 'message') listeners.add(listener); },
        removeEventListener: (type, listener) => { listeners.delete(listener); }
      };
      window.__prismplay = data => listeners.forEach(listener => listener({ data }));
    })();
    """

    fileprivate func handle(_ body: Any) {
        guard let message = body as? [String: Any], let type = message["type"] as? String else { return }
        switch type {
        case "ready": ready = true; publish()
        case "size":
            if let height = number(message["height"]) {
                logicalHeight = min(max(height / prefs.scale, 156), Self.frameHeight)
                resize(300 * prefs.scale, logicalHeight * prefs.scale, animate: flag(message["animate"]) == true)
            }
        case "dragBegin": beginDrag()
        case "dragEnd": endDrag()
        case "seek":
            if let position = number(message["position"]), playback.canSeek { command("seek", min(max(position, 0), playback.track?.duration ?? 0)) }
        case "play": if playback.canPlay { command("play") }
        case "pause": if playback.canPause { command("pause") }
        case "previous": if playback.canPrevious { command("previous") }
        case "next": if playback.canNext { command("next") }
        case "lyrics": prefs.lyricsOpen.toggle(); applySettings()
        case "retryLyrics":
            if let track = playback.track { lyrics = .empty("Looking for lyrics…"); publish(); fetchLyrics(track, allowPlainUpgrade: true) }
        case "scale": prefs.scale = prefs.scale >= 1.5 ? 0.75 : prefs.scale + 0.25; applySettings()
        case "game": toggleGameMode()
        case "settings": setSettings(!settingsOpen)
        case "closeSettings": setSettings(false)
        case "setPreference":
            if let key = message["key"] as? String, setPreference(key, message["value"]) { applySettings() }
        case "setHotkey":
            let text = ((message["key"] as? String) ?? "").trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
            if !Preferences.isHotkey(text) { generalStatus = "Enter one letter or number." }
            else if !setHotkey(Character(text)) {
                generalStatus = hotkeyRef != nil ? "Shortcut in use. Previous shortcut retained." : "Shortcut in use. No shortcut is active; the default could not be registered."
            } else { prefs.hotkey = text; applySettings(); generalStatus = "Shortcut saved." }
            publish()
        case "startup":
            guard let enabled = flag(message["enabled"]) else { break }
            do { if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() } }
            catch { generalStatus = "macOS did not allow the login item to change." }
            if SMAppService.mainApp.status == .requiresApproval { generalStatus = "Approve PrismPlay in System Settings › General › Login Items." }
            startup = Self.startupEnabled; publish()
        case "importLyrics": chooseLyricsFile()
        default: break
        }
    }

    // WebKit hands numbers and booleans over as NSNumber; tell them apart like the JSON value kinds on Windows.
    private func number(_ value: Any?) -> Double? {
        guard let n = value as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite else { return nil }
        return n.doubleValue
    }
    private func flag(_ value: Any?) -> Bool? {
        guard let n = value as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
        return n.boolValue
    }

    // Settings writes one whitelisted preference at a time; an unknown key or a value of the wrong type is rejected.
    private func setPreference(_ key: String, _ value: Any?) -> Bool {
        switch key {
        case "scale", "opacity", "corners", "lyricsSize", "lyricsOffset":
            guard let v = number(value) else { return false }
            switch key {
            case "scale": prefs.scale = v
            case "opacity": prefs.opacity = v
            case "corners": prefs.corners = v
            case "lyricsSize": prefs.lyricsSize = v
            default: prefs.lyricsOffset = v
            }
        case "reducedMotion":
            guard let on = flag(value) else { return false }
            prefs.reducedMotion = on
        case "theme":
            guard let theme = value as? String, ["Album", "Dark", "Light"].contains(theme) else { return false }
            prefs.theme = theme
        default: return false
        }
        return true
    }

    private func publish() {
        guard ready else { return }
        var track: Any = NSNull()
        if let t = playback.track { track = ["title": t.title, "artist": t.artist, "album": t.album, "duration": t.duration, "key": t.key] }
        let state: [String: Any] = [
            "track": track, "playing": playback.playing, "position": playback.positionNow, "sampledAt": Int(Date().timeIntervalSince1970 * 1000),
            "canPlay": playback.canPlay, "canPause": playback.canPause, "canPrevious": playback.canPrevious, "canNext": playback.canNext, "canSeek": playback.canSeek,
            "artwork": (playback.artwork as Any?) ?? NSNull(),
            "preferences": ["scale": prefs.scale, "opacity": prefs.opacity, "theme": prefs.theme, "corners": prefs.corners, "lyricsSize": prefs.lyricsSize,
                            "lyricsOffset": prefs.lyricsOffset, "reducedMotion": prefs.reducedMotion, "lyricsOpen": prefs.lyricsOpen, "hotkey": prefs.hotkey,
                            "x": prefs.x, "y": prefs.y] as [String: Any],
            "gameMode": gameMode, "moving": moving,
            "lyrics": ["lines": lyrics.lines.map { ["seconds": $0.seconds, "text": $0.text] as [String: Any] }, "plain": lyrics.plain, "status": lyrics.status] as [String: Any],
            "status": status, "seekRejected": seekRejected, "hotkeyError": hotkeyError, "settingsOpen": settingsOpen, "startup": startup,
            "lyricsStatus": lyricsStatus, "generalStatus": generalStatus, "platform": "mac",
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: ["type": "snapshot", "state": state]), let json = String(data: data, encoding: .utf8) else { return }
        web.evaluateJavaScript("window.__prismplay(\(json))")
    }

    // MARK: Playback and lyrics

    private func receive(_ state: Playback) {
        let changed = state.track?.key != playback.track?.key
        playback = state
        if changed {
            commandVersion += 1
            cancelLyrics()
            lyrics = .empty(state.track == nil ? "Play a song to see lyrics" : "Looking for lyrics…")
            seekRejected = false
            if let track = state.track, !demo { fetchLyrics(track) }
        }
        // Unlike Windows, a waiting state may change its message without a track change (e.g. Automation denied).
        if changed || state.track == nil { status = state.track == nil ? state.status : "" }
        publish()
    }

    private func cancelLyrics() { lyricsTask?.cancel(); lyricsGeneration += 1 }
    private func fetchLyrics(_ track: Track, allowPlainUpgrade: Bool = false) {
        cancelLyrics()
        let generation = lyricsGeneration, provider = lyricsProvider
        // Applied on the main thread; a cancelled or superseded lookup (or another track) is dropped.
        let apply = { [weak self] (result: Lyrics) in
            guard let self, self.lyricsGeneration == generation, self.playback.track?.key == track.key else { return }
            self.lyrics = result; self.publish()
        }
        lyricsTask = Task {
            guard let result = try? await provider.get(track, allowPlainUpgrade: allowPlainUpgrade) else { return }
            DispatchQueue.main.async { apply(result) }
        }
    }

    private func command(_ name: String, _ position: Double = 0) {
        commandVersion += 1
        let version = commandVersion
        status = ""; seekRejected = false
        if demo {
            playback.position = name == "seek" ? position : playback.positionNow
            playback.sampledAt = Date()
            playback.playing = name == "play" || name != "pause" && playback.playing
            publish(); return
        }
        spotify.command(name, position) { [weak self] succeeded in
            guard let self, version == self.commandVersion else { return }
            if !succeeded { self.status = name == "seek" ? "SEEK UNAVAILABLE" : "Control unavailable"; self.seekRejected = name == "seek" }
            self.publish()
        }
    }

    private func chooseLyricsFile() {
        let open = NSOpenPanel()
        open.allowedContentTypes = [UTType(filenameExtension: "lrc") ?? .plainText]
        NSApp.activate(ignoringOtherApps: true)
        open.begin { [weak self] response in
            guard let self, response == .OK, let url = open.url else { return }
            let result = self.importLyrics(url)
            if self.settingsOpen { self.lyricsStatus = result }
            self.publish()
        }
    }
    private func importLyrics(_ url: URL) -> String {
        guard let track = playback.track else { return "Play a track before importing lyrics." }
        do {
            if (try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0) > 2 * 1024 * 1024 { return "Choose a lyrics file smaller than 2 MB." }
            var content = String(decoding: try Data(contentsOf: url), as: UTF8.self)
            if content.hasPrefix("\u{FEFF}") { content.removeFirst() }
            try imports.save(track, content)
            if playback.track?.key != track.key { return "Lyrics saved for the original track. The current track has changed." }
            cancelLyrics(); lyrics = .parse(content, content); prefs.lyricsOpen = true; applySettings()
            return "Lyrics saved for this track."
        } catch { return "Could not read or save the lyrics file." }
    }

    // MARK: Settings, game mode, hotkey

    private static var startupEnabled: Bool { SMAppService.mainApp.status == .enabled }
    private func applySettings() { prefs.normalize(); resize(300 * prefs.scale, logicalHeight * prefs.scale); scheduleSave(); publish() }
    // Slider drags apply per tick; write the file once the value settles.
    private func scheduleSave() {
        saveTimer?.invalidate()
        saveTimer = Timer.scheduledTimer(timeInterval: 0.4, target: self, selector: #selector(save), userInfo: nil, repeats: false)
    }
    // Publish never saves, so reporting the failure cannot loop.
    @objc private func save() {
        saveTimer?.invalidate(); saveTimer = nil
        do { try store.save(prefs) } catch { status = "Settings could not be saved"; publish() }
    }

    // Open Settings suspends game mode (clicks must reach it) and lets the overlay take keyboard focus; closing restores both.
    private func setSettings(_ open: Bool, activate: Bool = false) {
        if open != settingsOpen {
            settingsOpen = open
            if open {
                startup = Self.startupEnabled
                if gameMode { suspendedGameMode = true; setGameMode(false) }
            } else {
                lyricsStatus = ""; generalStatus = ""
                if saveTimer != nil { save() }
                if suspendedGameMode { suspendedGameMode = false; setGameMode(true) }
            }
            panel.keyable = open
            // Hand the keyboard back to the app underneath (the game): a non-activating panel keeps key focus until it is ordered out.
            if !open && panel.isKeyWindow { panel.orderOut(nil); panel.orderFrontRegardless() }
        }
        if open && activate { panel.makeKeyAndOrderFront(nil); panel.makeFirstResponder(web) }
        publish()
    }
    // Turning game mode on (hotkey, menu, button) closes Settings first; Settings had suspended it, so there is nothing to restore.
    @objc private func toggleGameMode() {
        if settingsOpen { suspendedGameMode = false; setSettings(false) }
        setGameMode(!gameMode); publish()
    }
    private func setGameMode(_ enabled: Bool) {
        if moving { pointerDragging = false; finishDrag() }
        gameMode = enabled
        panel.ignoresMouseEvents = enabled
        gameItem.title = enabled ? "Exit game mode" : "Enter game mode"
    }
    @objc private func showOverlay() { panel.orderFrontRegardless() }
    @objc private func openSettings() { showOverlay(); setSettings(true, activate: true) }
    @objc private func quit() { NSApp.terminate(nil) }
    @objc private func terminating() {
        cancelLyrics(); dragTicker.stop(); springTicker.stop()
        if let hotkeyRef { UnregisterEventHotKey(hotkeyRef) }
        save()
    }

    private static let keyCodes: [Character: Int] = [
        "A": kVK_ANSI_A, "B": kVK_ANSI_B, "C": kVK_ANSI_C, "D": kVK_ANSI_D, "E": kVK_ANSI_E, "F": kVK_ANSI_F, "G": kVK_ANSI_G, "H": kVK_ANSI_H,
        "I": kVK_ANSI_I, "J": kVK_ANSI_J, "K": kVK_ANSI_K, "L": kVK_ANSI_L, "M": kVK_ANSI_M, "N": kVK_ANSI_N, "O": kVK_ANSI_O, "P": kVK_ANSI_P,
        "Q": kVK_ANSI_Q, "R": kVK_ANSI_R, "S": kVK_ANSI_S, "T": kVK_ANSI_T, "U": kVK_ANSI_U, "V": kVK_ANSI_V, "W": kVK_ANSI_W, "X": kVK_ANSI_X,
        "Y": kVK_ANSI_Y, "Z": kVK_ANSI_Z, "0": kVK_ANSI_0, "1": kVK_ANSI_1, "2": kVK_ANSI_2, "3": kVK_ANSI_3, "4": kVK_ANSI_4, "5": kVK_ANSI_5,
        "6": kVK_ANSI_6, "7": kVK_ANSI_7, "8": kVK_ANSI_8, "9": kVK_ANSI_9,
    ]
    // ⌃⌥ + key. Register the new ID first so a failed change keeps the working shortcut. Exclusive, so a combination another app
    // registered fails here instead of firing in both.
    private func registerHotkey(_ key: Character) -> Bool {
        if hotkeyRef != nil && hotkeyKey == key { return true }
        var registered = false
        if let code = Self.keyCodes[key] {
            let candidate: UInt32 = hotkeyID == 1 ? 2 : 1
            var ref: EventHotKeyRef?
            registered = RegisterEventHotKey(UInt32(code), UInt32(controlKey | optionKey), EventHotKeyID(signature: 0x5072_506C, id: candidate),
                                             GetApplicationEventTarget(), OptionBits(kEventHotKeyExclusive), &ref) == noErr
            if registered {
                if let old = hotkeyRef { UnregisterEventHotKey(old) }
                hotkeyRef = ref; hotkeyKey = key; hotkeyID = candidate
            }
        }
        // The tooltip reports a conflict only while no shortcut works.
        statusItem.button?.toolTip = hotkeyRef != nil ? "PrismPlay" : "PrismPlay · ⌃⌥\(key) is in use · choose Settings to pick another key"
        return registered
    }
    // Kept apart from `status`, which track changes reset: the card shows the conflict until some shortcut is registered.
    private func setHotkey(_ key: Character) -> Bool {
        let registered = registerHotkey(key)
        hotkeyError = hotkeyRef != nil ? "" : "⌃⌥\(key) in use"
        publish(); return registered
    }

    // MARK: Window size, position and drag

    private var now: Double { CACurrentMediaTime() }
    private var primaryTop: Double { Double(NSScreen.screens.first?.frame.maxY ?? 0) }

    /// `height` is the visible card height in points. With `animate` the spring runs there (retargeting mid-flight keeps its
    /// velocity); otherwise, or when the frame itself changes size (scale), it snaps.
    private func resize(_ width: Double, _ height: Double, animate: Bool = false) {
        let frame = Self.frameHeight * prefs.scale, target = min(height.rounded(.up), frame)
        let reframe = width != frameWidth || frame != webHeight
        if reframe { frameWidth = width; webHeight = frame; web.frame = NSRect(x: 0, y: 0, width: width, height: frame) }
        if reframe || !animate && target != springTarget {
            springTicker.stop(); springHeight = target; springTarget = target
        } else if target != springTarget {
            if springTicker.running {
                let s = DragMath.spring(springFrom, springVelocity, springTarget, now - springStart)
                springFrom = s.x; springVelocity = s.v
            } else { springFrom = springHeight; springVelocity = 0 }
            springTarget = target; springStart = now
            springTicker.start(on: container)
        }
        let radius = prefs.corners * prefs.scale
        container.layer?.cornerRadius = radius
        effect.maskImage = Self.mask(radius)
        applyFrame()
    }
    // The documented way to shape a behind-window blur; the container's continuous-corner mask clips everything else.
    private static func mask(_ radius: Double) -> NSImage {
        let image = NSImage(size: NSSize(width: radius * 2 + 1, height: radius * 2 + 1), flipped: false) { rect in
            NSColor.black.setFill(); NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill(); return true
        }
        image.capInsets = NSEdgeInsets(top: radius, left: radius, bottom: radius, right: radius); image.resizingMode = .stretch
        return image
    }
    private func springFrame() {
        var s = DragMath.spring(springFrom, springVelocity, springTarget, now - springStart)
        if abs(s.x - springTarget) < 0.25 && abs(s.v) < 5 { s.x = springTarget }
        springHeight = s.x; applyFrame()
        if s.x == springTarget { springTicker.stop(); panel.invalidateShadow() }
    }
    /// The panel follows the saved position, clamped for the visible height, so near the bottom edge the card rises and returns in
    /// step with the spring. Only a real drag writes the saved position.
    private func applyFrame() {
        let topLeft = moving ? NSPoint(x: dragX.rounded(), y: dragY.rounded()) : clampVisible(Double(prefs.x), Double(prefs.y))
        let frame = NSRect(x: topLeft.x, y: primaryTop - topLeft.y - springHeight, width: frameWidth, height: springHeight)
        if frame != panel.frame { panel.setFrame(frame, display: true) }
    }
    /// Keeps the visible card on the visible frame (no menu bar or Dock) of the display it is mostly on.
    private func clampVisible(_ x: Double, _ y: Double) -> NSPoint {
        let rect = NSRect(x: x, y: primaryTop - y - springHeight, width: frameWidth, height: springHeight)
        func overlap(_ screen: NSScreen) -> Double { let r = screen.frame.intersection(rect); return r.isNull ? 0 : Double(r.width * r.height) }
        guard let area = NSScreen.screens.max(by: { overlap($0) < overlap($1) })?.visibleFrame else { return NSPoint(x: x, y: y) }
        let left = Double(area.minX), right = Double(area.maxX), top = primaryTop - Double(area.maxY), bottom = top + Double(area.height)
        return NSPoint(x: min(max(x, left), max(left, right - frameWidth)), y: min(max(y, top), max(top, bottom - springHeight)))
    }
    @objc private func screensChanged() { applyFrame() }

    private var panelTopLeft: NSPoint { NSPoint(x: Double(panel.frame.minX), y: primaryTop - Double(panel.frame.maxY)) }
    private func beginDrag() {
        if gameMode { return }
        dragPointerOrigin = NSEvent.mouseLocation
        dragWindowOrigin = panelTopLeft
        dragX = dragWindowOrigin.x; targetX = dragX; dragY = dragWindowOrigin.y; targetY = dragY
        pointerDragging = true; moving = true
        lastDragFrame = now
        publish(); dragTicker.start(on: container)
    }
    private func updateDrag() {
        guard pointerDragging else { return }
        let pointer = NSEvent.mouseLocation   // y-up
        targetX = dragWindowOrigin.x + pointer.x - dragPointerOrigin.x
        targetY = dragWindowOrigin.y - (pointer.y - dragPointerOrigin.y)
    }
    private func endDrag() { guard pointerDragging else { return }; updateDrag(); pointerDragging = false }
    private func dragFrame() {
        if pointerDragging && NSEvent.pressedMouseButtons & 1 == 0 { endDrag() }
        // Sample once per frame, independent of the number of pointer events.
        updateDrag()
        let t = now, dt = t - lastDragFrame
        lastDragFrame = t
        dragX = DragMath.step(dragX, targetX, dt, prefs.reducedMotion)
        dragY = DragMath.step(dragY, targetY, dt, prefs.reducedMotion)
        applyFrame()
        if !pointerDragging && abs(dragX - targetX) < 0.4 && abs(dragY - targetY) < 0.4 { finishDrag() }
    }
    private func finishDrag() {
        dragTicker.stop(); moving = false
        // A header click without movement must not persist the display clamp (an expanded card near the bottom edge).
        let at = panelTopLeft
        let moved = abs(at.x - dragWindowOrigin.x) >= 0.5 || abs(at.y - dragWindowOrigin.y) >= 0.5
        if moved { let clamped = clampVisible(at.x, at.y); prefs.x = Int(clamped.x.rounded()); prefs.y = Int(clamped.y.rounded()) }
        applyFrame()
        if moved { save() }
        publish()
    }
}

/// Carbon delivers hot keys to a C callback, which cannot capture; it calls through this.
private var hotkeyPressed: (() -> Void)?

extension Overlay: WKScriptMessageHandler {
    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        let origin = message.frameInfo.securityOrigin
        guard message.frameInfo.isMainFrame, origin.protocol == "prismplay", origin.host == "app" else { return }
        handle(message.body)
    }
}

extension Overlay: WKNavigationDelegate {
    // Only the bundled page may load; links, redirects and anything else are cancelled.
    @MainActor func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction) async -> WKNavigationActionPolicy {
        let url = navigationAction.request.url
        return url?.scheme == "prismplay" && url?.host == "app" ? .allow : .cancel
    }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { NSLog("PrismPlay: web UI failed %@", error.localizedDescription) }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { NSLog("PrismPlay: web UI failed %@", error.localizedDescription) }
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) { ready = false; webView.reload() }
}

/// Serves Contents/Resources/Web (frontend/dist) at prismplay://app/.
final class WebAssets: NSObject {
    fileprivate let root: URL
    init(root: URL) { self.root = root.standardizedFileURL }
    fileprivate static let types = ["html": "text/html; charset=utf-8", "js": "text/javascript; charset=utf-8", "css": "text/css; charset=utf-8",
                                "svg": "image/svg+xml", "png": "image/png", "json": "application/json", "txt": "text/plain; charset=utf-8"]
}
extension WebAssets: WKURLSchemeHandler {
    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        guard let url = task.request.url, url.host == "app" else { return task.didFailWithError(URLError(.badURL)) }
        let file = root.appendingPathComponent(url.path).standardizedFileURL
        guard file.path.hasPrefix(root.path + "/"), let data = try? Data(contentsOf: file) else { return task.didFailWithError(URLError(.fileDoesNotExist)) }
        task.didReceive(HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1",
                                        headerFields: ["Content-Type": Self.types[file.pathExtension] ?? "application/octet-stream", "Content-Length": String(data.count)])!)
        task.didReceive(data)
        task.didFinish()
    }
    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {}
}

final class OverlayPanel: NSPanel {
    /// Only while Settings is open, so the keyboard works there; otherwise the overlay never takes the game's focus.
    var keyable = false
    override var canBecomeKey: Bool { keyable }
    override var canBecomeMain: Bool { false }
}

final class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

final class OverlayWebView: WKWebView {
    private var hover: NSTrackingArea?
    // The panel is rarely key: clicks must work without a focusing first click.
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    // With overlay scrollbars WebKit tracks the mouse only in the key window; hover (which reveals the controls) must work always.
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let hover { removeTrackingArea(hover) }
        let area = NSTrackingArea(rect: .zero, options: [.mouseMoved, .mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self, userInfo: nil)
        addTrackingArea(area); hover = area
    }
    override func willOpenMenu(_ menu: NSMenu, with event: NSEvent) { menu.removeAllItems() }
}

/// Calls `tick` once per display frame while running (CADisplayLink on macOS 14+, a 120 Hz timer on 13).
final class Ticker: NSObject {
    private let tick: () -> Void
    private var link: AnyObject?
    init(_ tick: @escaping () -> Void) { self.tick = tick }
    var running: Bool { link != nil }
    func start(on view: NSView) {
        guard link == nil else { return }
        if #available(macOS 14, *) {
            let displayLink = view.displayLink(target: self, selector: #selector(fire))
            displayLink.add(to: .main, forMode: .common)
            link = displayLink
        } else {
            let timer = Timer(timeInterval: 1.0 / 120, target: self, selector: #selector(fire), userInfo: nil, repeats: true)
            RunLoop.main.add(timer, forMode: .common)
            link = timer
        }
    }
    func stop() {
        (link as? Timer)?.invalidate()
        if #available(macOS 14, *) { (link as? CADisplayLink)?.invalidate() }
        link = nil
    }
    @objc private func fire() { tick() }
}
