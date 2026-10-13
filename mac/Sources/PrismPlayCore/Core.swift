import Foundation

// Ports of src/PrismPlay/Core.cs and DragMath.cs; keep the two in step.

public struct Track: Equatable {
    public var title: String, artist: String, album: String
    public var duration: Double
    public init(_ title: String, _ artist: String, _ album: String, _ duration: Double) {
        self.title = title; self.artist = artist; self.album = album; self.duration = duration
    }
    public var key: String { "\(title)\n\(artist)\n\(album)\n\(String(format: "%.0f", duration))" }
}

public struct LyricLine: Equatable {
    public let seconds: Double, text: String
}

public struct Lyrics {
    public var lines: [LyricLine], plain: String, status: String
    public static func empty(_ status: String) -> Lyrics { Lyrics(lines: [], plain: "", status: status) }
    private static let time = try! NSRegularExpression(pattern: #"\[([0-9]+):([0-9]{2}(?:\.[0-9]+)?)\]"#)
    private static let tag = try! NSRegularExpression(pattern: #"\[[^\]]*\]"#)
    public static func parse(_ synced: String?, _ plain: String?) -> Lyrics {
        var lines: [(line: LyricLine, order: Int)] = []
        // components(separatedBy:) splits on "\n" inside "\r\n" too (Swift's split would treat CRLF as one Character).
        for line in (synced ?? "").components(separatedBy: "\n") {
            let ns = line as NSString, all = NSRange(location: 0, length: ns.length)
            let text = tag.stringByReplacingMatches(in: line, range: all, withTemplate: "").trimmingCharacters(in: .whitespacesAndNewlines)
            for m in time.matches(in: line, range: all) {
                let seconds = Double(ns.substring(with: m.range(at: 1)))! * 60 + Double(ns.substring(with: m.range(at: 2)))!
                lines.append((LyricLine(seconds: seconds, text: text), lines.count))
            }
        }
        let sorted = lines.sorted { ($0.line.seconds, $0.order) < ($1.line.seconds, $1.order) }.map(\.line)
        let blank = (plain ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        return Lyrics(lines: sorted, plain: plain ?? "", status: !sorted.isEmpty ? "Synced lyrics" : blank ? "No lyrics found" : "Unsynced lyrics")
    }
    public func activeIndex(_ seconds: Double) -> Int { lines.lastIndex { $0.seconds <= seconds } ?? -1 }
}

/// settings.json keeps the Windows (PascalCase) shape. X/Y are the card's top-left corner in points, measured right and DOWN from
/// the top-left of the primary display (the one with the menu bar), as on Windows; the host converts to AppKit's y-up space.
public struct Preferences: Codable, Equatable {
    public var scale = 1.0, opacity = 0.64, theme = "Album", corners = 22.0, lyricsSize = 18.0, lyricsOffset = 0.0
    public var reducedMotion = false, lyricsOpen = false, hotkey = "G", x = 80, y = 80
    enum CodingKeys: String, CodingKey {
        case scale = "Scale", opacity = "Opacity", theme = "Theme", corners = "Corners", lyricsSize = "LyricsSize", lyricsOffset = "LyricsOffset"
        case reducedMotion = "ReducedMotion", lyricsOpen = "LyricsOpen", hotkey = "Hotkey", x = "X", y = "Y"
    }
    public init() {}
    // Missing keys keep their defaults; a value of the wrong type fails the whole load (the store then uses defaults), like System.Text.Json.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func value<T: Decodable>(_ key: CodingKeys, _ fallback: T) throws -> T { try c.decodeIfPresent(T.self, forKey: key) ?? fallback }
        scale = try value(.scale, scale); opacity = try value(.opacity, opacity); theme = try value(.theme, theme)
        corners = try value(.corners, corners); lyricsSize = try value(.lyricsSize, lyricsSize); lyricsOffset = try value(.lyricsOffset, lyricsOffset)
        reducedMotion = try value(.reducedMotion, reducedMotion); lyricsOpen = try value(.lyricsOpen, lyricsOpen)
        hotkey = try value(.hotkey, hotkey); x = try value(.x, x); y = try value(.y, y)
    }
    public mutating func normalize() {
        scale = Self.clamp(scale, 0.75, 2, 1); opacity = Self.clamp(opacity, 0.15, 0.95, 0.64)
        corners = Self.clamp(corners, 0, 32, 22); lyricsSize = Self.clamp(lyricsSize, 12, 28, 18)
        lyricsOffset = Self.clamp(lyricsOffset, -10, 10, 0)
        if !["Album", "Dark", "Light"].contains(theme) { theme = "Album" }
        if !Self.isHotkey(hotkey) { hotkey = "G" }
        hotkey = hotkey.uppercased()
    }
    /// One ASCII letter or digit.
    public static func isHotkey(_ key: String) -> Bool { key.utf8.count == 1 && key.first.map { $0.isLetter || $0.isNumber } == true }
    private static func clamp(_ value: Double, _ low: Double, _ high: Double, _ fallback: Double) -> Double { value.isFinite ? min(max(value, low), high) : fallback }
}

public struct PreferencesStore {
    public let folder: URL
    public init(folder: URL) { self.folder = folder }
    private var file: URL { folder.appendingPathComponent("settings.json") }
    public func load() -> Preferences {
        guard let data = try? Data(contentsOf: file), var prefs = try? JSONDecoder().decode(Preferences.self, from: data) else { return Preferences() }
        prefs.normalize(); return prefs
    }
    public func save(_ prefs: Preferences) throws {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(prefs).write(to: file, options: .atomic)
    }
}

public enum DragMath {
    /// A short, frame-rate-independent response; no overshoot or prolonged trailing.
    public static func step(_ current: Double, _ target: Double, _ elapsed: Double, _ reducedMotion: Bool) -> Double {
        reducedMotion ? target : current + (target - current) * (1 - exp(-min(max(elapsed, 0), 0.1) / 0.024))
    }
    /// Critically damped spring, analytic so a retarget restarts from the sampled position and velocity:
    /// x(t) = target + (d0 + (v0 + ω·d0)·t)·e^(−ωt), d0 = x0 − target. The same curve is --spring in frontend/src/style.css.
    public static let springOmega = 2 * Double.pi / 0.42
    public static func spring(_ x0: Double, _ v0: Double, _ target: Double, _ t: Double) -> (x: Double, v: Double) {
        let d0 = x0 - target, b = v0 + springOmega * d0, e = exp(-springOmega * t)
        return (target + (d0 + b * t) * e, (b - springOmega * (d0 + b * t)) * e)
    }
}
