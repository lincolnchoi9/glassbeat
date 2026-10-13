import CryptoKit
import Foundation

// Port of src/PrismPlay/LyricsProvider.cs: LRCLIB exact lookup, search fallback, validation, a 100-entry cache, and local imports.

func hashName(_ track: Track) -> String { SHA256.hash(data: Data(track.key.utf8)).map { String(format: "%02X", $0) }.joined() }

public struct LocalLyricsStore {
    public let folder: URL
    public init(folder: URL) { self.folder = folder }
    private func file(_ track: Track) -> URL { folder.appendingPathComponent(hashName(track) + ".lrc") }
    public func read(_ track: Track) -> String? { try? String(contentsOf: file(track), encoding: .utf8) }
    public func save(_ track: Track, _ content: String) throws {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try content.write(to: file(track), atomically: true, encoding: .utf8)
    }
}

public struct HTTPStatusError: Error { public let status: Int }

public final class LyricsProvider {
    /// Returns the body and HTTP status. Throws for transport failures (URLError) and cancellation.
    public typealias Fetch = (URL) async throws -> (Data, Int)
    private let fetch: Fetch, cache: URL, imports: LocalLyricsStore, enabled: Bool
    public init(fetch: @escaping Fetch, cache: URL, imports: LocalLyricsStore, enabled: Bool = true) {
        self.fetch = fetch; self.cache = cache; self.imports = imports; self.enabled = enabled
    }
    public static func live(cache: URL, imports: LocalLyricsStore) -> LyricsProvider {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForResource = 12
        config.httpAdditionalHeaders = ["User-Agent": "PrismPlay/0.4 (macOS)"]
        let session = URLSession(configuration: config)
        return LyricsProvider(fetch: { url in
            let (data, response) = try await session.data(from: url)
            return (data, (response as? HTTPURLResponse)?.statusCode ?? 0)
        }, cache: cache, imports: imports)
    }

    /// allowPlainUpgrade (Retry): a cached plain-only entry re-runs the synced search instead of being served as-is.
    /// Throws only CancellationError; every other failure is a status.
    public func get(_ track: Track, allowPlainUpgrade: Bool = false) async throws -> Lyrics {
        try Task.checkCancellation()
        if let imported = imports.read(track) { return .parse(imported, imported) }
        if !enabled { return .empty("Online lyrics are disabled") }
        if track.duration <= 0 || Self.blank(track.artist) { return .empty("Lyrics unavailable for this item") }
        do {
            let file = cache.appendingPathComponent(hashName(track) + ".json")
            var json: Data?, cached: Data?
            // A corrupt or unreadable cache entry falls back to the network.
            if let text = try? Data(contentsOf: file), Self.matches(try? JSONSerialization.jsonObject(with: text), track) { json = text; cached = text }
            if cached == nil {
                let (body, status) = try await fetch(Self.url("get", [("track_name", track.title), ("artist_name", track.artist), ("album_name", track.album), ("duration", Self.number(track.duration))]))
                if status != 404 {
                    guard (200..<300).contains(status) else { throw HTTPStatusError(status: status) }
                    if Self.matches(try JSONSerialization.jsonObject(with: body), track) { json = body }
                }
            }
            // Album names, punctuation, and release editions frequently differ from Spotify.
            // Keep the title, artist, and duration checks even when searching without the album.
            // ponytail: no negative cache; misses and plain-only Retries query LRCLIB each time. Add one if rate limits bite.
            if (cached == nil || allowPlainUpgrade) && (json.map(Self.hasSynced) != true) {
                do { if let fallback = try await search(track) { json = fallback } }
                catch let error where json != nil && !Task.isCancelled && (error is HTTPStatusError || error is URLError) {}
            }
            guard let json else { return .empty("No lyrics found for this track") }
            if json != cached { store(json, file) }
            guard let root = try JSONSerialization.jsonObject(with: json) as? [String: Any], Self.matches(root, track) else { return .empty("No matching lyrics found") }
            if root["instrumental"] as? Bool == true { return .empty("Instrumental") }
            var result = Lyrics.parse(Self.text(root, "syncedLyrics"), Self.text(root, "plainLyrics"))
            result.status = !result.lines.isEmpty ? "Synced lyrics · LRCLIB" : !result.plain.isEmpty ? "Unsynced lyrics · LRCLIB" : "No lyrics found for this track"
            return result
        } catch {
            if Task.isCancelled || error is CancellationError { throw CancellationError() }
            return .empty("Lyrics unavailable · try again later")
        }
    }

    private func search(_ track: Track) async throws -> Data? {
        let (body, status) = try await fetch(Self.url("search", [("track_name", track.title), ("artist_name", track.artist)]))
        if status == 404 { return nil }
        guard (200..<300).contains(status) else { throw HTTPStatusError(status: status) }
        guard let rows = try JSONSerialization.jsonObject(with: body) as? [Any] else { return nil }
        let album = Self.normalize(track.album)
        let best = rows.enumerated().compactMap { (i, row) -> (key: (Int, Int, Double, Int), row: [String: Any])? in
            guard let row = row as? [String: Any], Self.matches(row, track) else { return nil }
            let synced = Self.blank(Self.text(row, "syncedLyrics")) ? 1 : 0, sameAlbum = Self.normalize(Self.text(row, "albumName")) == album ? 0 : 1
            return ((synced, sameAlbum, abs(Self.duration(row)! - track.duration), i), row)
        }.min { $0.key < $1.key }
        return try best.map { try JSONSerialization.data(withJSONObject: $0.row) }
    }

    // The disk cache is optional; a read-only or denied folder still shows the downloaded lyrics.
    private func store(_ json: Data, _ file: URL) {
        let manager = FileManager.default
        guard (try? manager.createDirectory(at: cache, withIntermediateDirectories: true)) != nil, (try? json.write(to: file, options: .atomic)) != nil,
              let files = try? manager.contentsOfDirectory(at: cache, includingPropertiesForKeys: [.contentModificationDateKey]) else { return }
        let dated = files.filter { $0.pathExtension == "json" }.map { ($0, (try? $0.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast) }
        for (old, _) in dated.sorted(by: { $0.1 > $1.1 }).dropFirst(100) { try? manager.removeItem(at: old) }
    }

    private static let unreserved = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
    private static func url(_ endpoint: String, _ query: [(String, String)]) -> URL {
        URL(string: "https://lrclib.net/api/\(endpoint)?" + query.map { "\($0.0)=\($0.1.addingPercentEncoding(withAllowedCharacters: unreserved)!)" }.joined(separator: "&"))!
    }
    private static func number(_ value: Double) -> String { value == value.rounded() && abs(value) < 1e15 ? String(Int(value)) : String(value) }
    private static func blank(_ value: String?) -> Bool { (value ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    private static func text(_ row: [String: Any], _ name: String) -> String? { row[name] as? String }
    private static func duration(_ row: [String: Any]) -> Double? {
        guard let value = row["duration"] as? NSNumber, CFGetTypeID(value) != CFBooleanGetTypeID() else { return nil }
        return value.doubleValue
    }
    private static func hasSynced(_ json: Data) -> Bool {
        guard let row = (try? JSONSerialization.jsonObject(with: json)) as? [String: Any] else { return false }
        return !blank(text(row, "syncedLyrics")) || row["instrumental"] as? Bool == true
    }
    /// Letters and decimal digits after canonical decomposition, lowercased (C#'s char.IsLetterOrDigit after FormD).
    static func normalize(_ value: String?) -> String {
        let kept: Set<Unicode.GeneralCategory> = [.uppercaseLetter, .lowercaseLetter, .titlecaseLetter, .modifierLetter, .otherLetter, .decimalNumber]
        var scalars = String.UnicodeScalarView()
        scalars.append(contentsOf: (value ?? "").decomposedStringWithCanonicalMapping.unicodeScalars.filter { kept.contains($0.properties.generalCategory) })
        return String(scalars).lowercased()
    }
    static func matches(_ root: Any?, _ track: Track) -> Bool {
        guard let row = root as? [String: Any], let seconds = duration(row), abs(seconds - track.duration) <= 3 else { return false }
        return normalize(text(row, "trackName")) == normalize(track.title) && normalize(text(row, "artistName")) == normalize(track.artist)
    }
}
