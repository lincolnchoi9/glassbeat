import AppKit
import PrismPlayCore

struct Playback {
    var track: Track?
    var playing = false
    var position = 0.0
    var sampledAt = Date()
    var canPlay = false, canPause = false, canPrevious = false, canNext = false, canSeek = false
    var artwork: String?        // data: URL
    var artworkURL: URL?
    var status = ""
    static func waiting(_ status: String = "Open Spotify to get started") -> Playback { Playback(status: status) }
    var positionNow: Double {
        guard let track else { return 0 }
        return min(max(position + (playing ? Date().timeIntervalSince(sampledAt) : 0), 0), max(0, track.duration))
    }
}

/// Spotify through AppleScript (the macOS counterpart of SpotifyPlayback.cs). Scripts run on one serial background queue; every
/// other member is main-thread only. Spotify is never scripted unless it is running, because a `tell` would launch it.
final class SpotifyPlayback: NSObject {
    static let bundleID = "com.spotify.client"
    static let automationDenied = "Allow PrismPlay to control Spotify in System Settings › Privacy & Security › Automation"
    var changed: ((Playback) -> Void)?
    private let queue = DispatchQueue(label: "io.github.lincolnchoi9.prismplay.spotify")
    private var readScript: NSAppleScript?          // queue only
    private var lastError: Int?                     // queue only
    private var busy = false, again = false
    private var last: Playback?
    private var artworkKey: String?, artwork: String?
    private var timer: Timer?

    private var running: Bool { NSRunningApplication.runningApplications(withBundleIdentifier: Self.bundleID).contains { !$0.isTerminated } }

    func start() {
        DistributedNotificationCenter.default().addObserver(self, selector: #selector(refresh), name: Notification.Name("com.spotify.client.PlaybackStateChanged"), object: nil)
        for name in [NSWorkspace.didLaunchApplicationNotification, NSWorkspace.didTerminateApplicationNotification] {
            NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(refresh), name: name, object: nil)
        }
        // The notification covers play/pause/track changes; the 1 s poll catches seeks made in Spotify itself.
        timer = Timer.scheduledTimer(timeInterval: 1, target: self, selector: #selector(refresh), userInfo: nil, repeats: true)
        refresh()
    }

    // Coalesce bursts: while a read runs, any further request shares one follow-up read.
    @objc private func refresh() {
        if busy { again = true; return }
        guard running else { deliver(.waiting()); return }
        busy = true
        queue.async {
            let state = self.read()
            DispatchQueue.main.async {
                self.busy = false
                self.deliver(state)
                if self.again { self.again = false; self.refresh() }
            }
        }
    }

    private static let readSource = """
    if application id "com.spotify.client" is running then
        tell application id "com.spotify.client"
            if player state is stopped then return {"stopped"}
            set t to current track
            return {(player state is playing), player position, name of t, artist of t, album of t, duration of t, artwork url of t, id of t}
        end tell
    end if
    return {"stopped"}
    """

    private func read() -> Playback {
        if readScript == nil { readScript = NSAppleScript(source: Self.readSource) }
        var error: NSDictionary?
        let result = readScript?.executeAndReturnError(&error)
        if let error {
            let code = error[NSAppleScript.errorNumber] as? Int
            if code != lastError { lastError = code; NSLog("PrismPlay: Spotify AppleScript error %@", error) }
            return .waiting(code == -1743 ? Self.automationDenied : "Waiting for Spotify…")
        }
        lastError = nil
        guard let result, result.numberOfItems >= 8, let title = result.atIndex(3)?.stringValue else { return .waiting("Play a song in Spotify") }
        let track = Track(title, result.atIndex(4)?.stringValue ?? "", result.atIndex(5)?.stringValue ?? "", max(0, (result.atIndex(6)?.doubleValue ?? 0) / 1000))
        let ad = (result.atIndex(8)?.stringValue ?? "").hasPrefix("spotify:ad:")
        let position = result.atIndex(2)?.doubleValue ?? 0
        return Playback(track: track, playing: result.atIndex(1)?.booleanValue ?? false, position: position.isFinite ? position : 0, sampledAt: Date(),
                        canPlay: true, canPause: true, canPrevious: !ad, canNext: !ad, canSeek: !ad && track.duration > 0,
                        artworkURL: result.atIndex(7)?.stringValue.flatMap { $0.hasPrefix("https://") ? URL(string: $0) : nil })
    }

    private func deliver(_ next: Playback) {
        var state = next
        if state.track?.key != artworkKey {
            artworkKey = state.track?.key; artwork = nil
            if let key = artworkKey, let url = state.artworkURL { fetchArtwork(url, key) }
        }
        state.artwork = artwork
        // Polls only reach the overlay when something visible changed (or the position jumped, e.g. a seek in Spotify).
        if let last, Self.same(last, state) { return }
        last = state; changed?(state)
    }

    /// One image per track, sent as a data URL (the page's CSP allows data: images only).
    private func fetchArtwork(_ url: URL, _ key: String) {
        URLSession.shared.dataTask(with: url) { data, response, _ in
            guard let data, data.count <= 8 * 1024 * 1024, (response as? HTTPURLResponse)?.statusCode == 200 else { return }
            let type = response?.mimeType.flatMap { $0.hasPrefix("image/") ? $0 : nil } ?? "image/jpeg"
            let image = "data:\(type);base64,\(data.base64EncodedString())"
            DispatchQueue.main.async {
                guard self.artworkKey == key, var state = self.last else { return }
                self.artwork = image; state.artwork = image; self.last = state; self.changed?(state)
            }
        }.resume()
    }

    private static func same(_ a: Playback, _ b: Playback) -> Bool {
        a.track == b.track && a.playing == b.playing && a.status == b.status && a.artwork == b.artwork && a.canPlay == b.canPlay && a.canPause == b.canPause
            && a.canPrevious == b.canPrevious && a.canNext == b.canNext && a.canSeek == b.canSeek && abs(a.positionNow - b.positionNow) < 1
    }

    func command(_ name: String, _ position: Double, done: @escaping (Bool) -> Void) {
        let verb: String
        switch name {
        case "play": verb = "play"
        case "pause": verb = "pause"
        case "next": verb = "next track"
        case "previous": verb = "previous track"
        case "seek": verb = String(format: "set player position to %.3f", position)
        default: return done(false)
        }
        guard running else { return done(false) }
        queue.async {
            var error: NSDictionary?
            _ = NSAppleScript(source: "if application id \"\(Self.bundleID)\" is running then tell application id \"\(Self.bundleID)\" to \(verb)")?.executeAndReturnError(&error)
            if let error { NSLog("PrismPlay: Spotify command %@ failed %@", name, error) }
            DispatchQueue.main.async {
                done(error == nil)
                if error == nil { self.refresh() }
            }
        }
    }
}
