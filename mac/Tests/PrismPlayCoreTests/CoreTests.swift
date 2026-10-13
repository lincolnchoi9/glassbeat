import Foundation
import XCTest
@testable import PrismPlayCore

// Mirrors the checks in tests/PrismPlay.Tests/Program.cs for the ported pieces.
final class CoreTests: XCTestCase {
    func testDragMath() {
        let eased = DragMath.step(10, 100, 0.016, false)
        XCTAssert(eased > 10 && eased < 100, "Drag easing moves toward target without overshoot")
        XCTAssertEqual(DragMath.step(DragMath.step(0, 100, 0.008, false), 100, 0.008, false), DragMath.step(0, 100, 0.016, false), accuracy: 0.00001, "Drag easing is independent of frame rate")
        XCTAssertEqual(DragMath.step(10, 100, 0.016, true), 100, "Reduced motion follows pointer directly")
        var settled = 0.0
        for _ in 0..<15 { settled = DragMath.step(settled, 100, 0.008, false) }
        XCTAssertLessThan(abs(100 - settled), 1, "Released drag settles within 120 milliseconds")
        let spring = (0...60).map { DragMath.spring(156, 0, 480, Double($0) / 100) }
        let mid = DragMath.spring(156, 0, 480, 0.1), retarget = DragMath.spring(mid.x, mid.v, 200, 0)
        XCTAssert(spring[0].x == 156 && zip(spring, spring.dropFirst()).allSatisfy { $1.x >= $0.x && $1.x <= 480 } && 480 - spring.last!.x < 0.5
            && abs(retarget.x - mid.x) < 1e-9 && abs(retarget.v - mid.v) < 1e-9, "Region spring rises without overshoot, settles by 0.6 s, and retargets without a jump")
    }

    func testLyricsParse() {
        let lyrics = Lyrics.parse("[ar:Artist]\n[00:03.50][01:03.500]Repeat\r\n[00:01.00]First\nmalformed\n[00:05]Last", nil)
        XCTAssertEqual(lyrics.lines.count, 4, "Multiple timestamps and metadata handling")
        XCTAssertEqual(lyrics.lines[0].text, "First", "Unordered input is sorted")
        XCTAssertEqual(lyrics.lines[1].text, "Repeat", "CRLF line endings are trimmed")
        XCTAssertEqual(lyrics.activeIndex(0.9), -1, "No lyric before first timestamp")
        XCTAssertEqual(lyrics.activeIndex(3.5), 1, "Exact timestamp selects new line")
        XCTAssert(lyrics.activeIndex(2) == 0 && lyrics.activeIndex(64) == 3, "Seek backward and forward select correctly")
        XCTAssertEqual(Lyrics.parse(nil, "Plain lyrics").status, "Unsynced lyrics", "Plain text never gets fabricated timestamps")
        XCTAssertEqual(Lyrics.parse(nil, nil).status, "No lyrics found", "Missing lyrics state")
    }

    func testPreferences() throws {
        var prefs = Preferences()
        prefs.scale = .nan; prefs.opacity = 5; prefs.theme = "invalid"; prefs.corners = -20; prefs.hotkey = "!!!"
        prefs.normalize()
        XCTAssert(prefs.scale == 1 && prefs.opacity == 0.95 && prefs.corners == 0, "Invalid numeric preferences recover safely")
        XCTAssert(prefs.theme == "Album" && prefs.hotkey == "G", "Invalid theme and shortcut recover safely")
        let decoded = try JSONDecoder().decode(Preferences.self, from: Data(#"{"Scale":1.5,"Hotkey":"k","X":10}"#.utf8))
        XCTAssert(decoded.scale == 1.5 && decoded.x == 10 && decoded.y == 80 && decoded.opacity == 0.64, "settings.json keeps the Windows key names; missing keys keep defaults")
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("PrismPlay-prefs-" + UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: folder) }
        let store = PreferencesStore(folder: folder)
        try store.save(decoded)
        XCTAssertEqual(store.load().hotkey, "K", "Saved preferences load back normalized")
    }

    func testLyricsProvider() async throws {
        let cache = FileManager.default.temporaryDirectory.appendingPathComponent("PrismPlay-test-" + UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: cache) }
        let fake = Fake(), imports = LocalLyricsStore(folder: cache.appendingPathComponent("imports"))
        let provider = LyricsProvider(fetch: fake.fetch, cache: cache, imports: imports)
        let track = Track("Song", "Artist", "Album", 120)
        fake.json = #"{"trackName":"Song","artistName":"Artist","duration":120,"syncedLyrics":"[00:01.00]Hello","plainLyrics":"Hello","instrumental":false}"#
        var result = try await provider.get(track)
        XCTAssertEqual(result.lines.count, 1, "Matching provider response is accepted")
        result = try await provider.get(track)
        XCTAssert(result.lines.count == 1 && fake.calls == 1, "Cache avoids duplicate network requests")
        fake.json = #"{"trackName":"Wrong song","artistName":"Artist","duration":130,"syncedLyrics":"[00:01.00]Wrong","plainLyrics":"Wrong"}"#
        result = try await provider.get(titled(track, "Another"))
        XCTAssertEqual(result.lines.count, 0, "Wrong track or duration never displays mismatched lyrics")
        fake.status = 404
        result = try await provider.get(titled(track, "Missing"))
        XCTAssertEqual(result.status, "No lyrics found for this track", "Missing provider result is explicit")
        fake.status = 429
        result = try await provider.get(titled(track, "Limited"))
        XCTAssert(result.status.contains("unavailable"), "Rate limiting degrades without breaking playback")
        let cancelled = Task { () async throws -> Lyrics in
            withUnsafeCurrentTask { $0?.cancel() }
            return try await provider.get(track)
        }
        do { _ = try await cancelled.value; XCTFail("Cancellation propagates") } catch { XCTAssert(error is CancellationError, "Cancellation propagates") }
        let before = fake.calls
        _ = try await LyricsProvider(fetch: fake.fetch, cache: cache, imports: imports, enabled: false).get(track)
        XCTAssertEqual(fake.calls, before, "Explicitly disabled test provider makes no network requests")

        fake.respond = { url in url.path.hasSuffix("/search")
            ? (Data(#"[{"trackName":"Search Song!","artistName":"Artist","albumName":"Other edition","duration":121,"syncedLyrics":"[00:01]Found automatically","plainLyrics":"Found automatically"},{"trackName":"Search Song - Live","artistName":"Artist","duration":120,"syncedLyrics":"[00:01]Wrong version"}]"#.utf8), 200)
            : (Data(), 404) }
        var searchTrack = titled(track, "Search Song"); searchTrack.album = "Spotify edition"
        result = try await provider.get(searchTrack)
        XCTAssert(result.lines.count == 1 && result.lines[0].text == "Found automatically", "Search fallback tolerates album and punctuation differences while rejecting other versions")
        fake.respond = { url in url.path.hasSuffix("/search") ? (Data(), 429) : (Data(#"{"trackName":"Plain Song","artistName":"Artist","duration":120,"syncedLyrics":null,"plainLyrics":"Plain available"}"#.utf8), 200) }
        result = try await provider.get(titled(track, "Plain Song"))
        XCTAssertEqual(result.plain, "Plain available", "Plain lyrics survive a failed enhancement search")
        fake.respond = { _ in throw URLError(.notConnectedToInternet) }
        result = try await provider.get(searchTrack)
        XCTAssertEqual(result.lines.count, 1, "Cached synced lyrics remain available offline")
        result = try await provider.get(titled(track, "Not cached"))
        XCTAssert(result.status.contains("unavailable"), "Uncached offline request has a clear error state")

        // A broken cache folder must never discard lyrics that were downloaded successfully.
        let readOnly = titled(track, "Read-only cache")
        fake.respond = { _ in (row(readOnly.title, synced: true), 200) }
        try FileManager.default.setAttributes([.posixPermissions: 0o555], ofItemAtPath: cache.path)
        result = try await provider.get(readOnly)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: cache.path)
        XCTAssertEqual(result.lines.count, 1, "Unwritable lyrics cache still returns downloaded lyrics")
        let unreadable = titled(track, "Unreadable cache")
        fake.respond = { _ in (row(unreadable.title, synced: true), 200) }
        let entry = cache.appendingPathComponent(hashName(unreadable) + ".json")
        FileManager.default.createFile(atPath: entry.path, contents: row(unreadable.title, synced: false), attributes: [.posixPermissions: 0o000])
        result = try await provider.get(unreadable)
        XCTAssertEqual(result.lines.count, 1, "Unreadable lyrics cache falls back to the network")

        // Retry can upgrade a cached plain-only entry once synced lyrics become available.
        let upgrade = titled(track, "Upgrade Song")
        fake.respond = { url in url.path.hasSuffix("/search") ? (Data(), 404) : (row(upgrade.title, synced: false), 200) }
        _ = try await provider.get(upgrade)
        fake.respond = { url in url.path.hasSuffix("/search") ? (rows([upgrade.title]), 200) : (row(upgrade.title, synced: false), 200) }
        let calls = fake.calls
        result = try await provider.get(upgrade)
        XCTAssert(result.plain == upgrade.title && fake.calls == calls, "Normal lookup serves a cached plain-only entry without network")
        result = try await provider.get(upgrade, allowPlainUpgrade: true)
        XCTAssertEqual(result.lines.count, 1, "Retry upgrades a cached plain-only entry to synced lyrics")
        fake.respond = { _ in throw URLError(.notConnectedToInternet) }
        result = try await provider.get(upgrade)
        XCTAssertEqual(result.lines.count, 1, "Upgraded synced lyrics replace the cached plain-only entry")
        result = try await provider.get(titled(track, "Plain Song"), allowPlainUpgrade: true)
        XCTAssertEqual(result.plain, "Plain available", "Offline Retry keeps a cached plain-only entry")

        try imports.save(track, "[00:02.00]Imported")
        result = try await provider.get(track)
        XCTAssertEqual(result.lines.first?.text, "Imported", "An imported .lrc wins over the online lookup")
    }

    func testLiveLookup() async throws {
        try XCTSkipUnless(ProcessInfo.processInfo.environment["PRISMPLAY_LIVE"] != nil, "Set PRISMPLAY_LIVE=1 for a real LRCLIB request")
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("PrismPlay-live-" + UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: folder) }
        let live = try await LyricsProvider.live(cache: folder, imports: LocalLyricsStore(folder: folder)).get(Track("Hymn for the Weekend", "Coldplay", "A Head Full of Dreams", 258))
        XCTAssertGreaterThan(live.lines.count, 10, "Live automatic LRCLIB lookup returns timed lyrics without any local import")
    }
}

private func titled(_ track: Track, _ title: String) -> Track { var copy = track; copy.title = title; return copy }
private func row(_ title: String, synced: Bool) -> Data {
    try! JSONSerialization.data(withJSONObject: ["trackName": title, "artistName": "Artist", "duration": 120, "syncedLyrics": synced ? "[00:01.00]" + title : NSNull(), "plainLyrics": title] as [String: Any])
}
private func rows(_ titles: [String]) -> Data {
    Data(("[" + titles.map { String(decoding: row($0, synced: true), as: UTF8.self) }.joined(separator: ",") + "]").utf8)
}

private final class Fake {
    var calls = 0, json = "{}", status = 200
    var respond: ((URL) throws -> (Data, Int))?
    func fetch(_ url: URL) async throws -> (Data, Int) {
        calls += 1
        if let respond { return try respond(url) }
        return (Data(json.utf8), status)
    }
}
