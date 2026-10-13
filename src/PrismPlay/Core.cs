using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrismPlay;

public sealed record Track(string Title, string Artist, string Album, double Duration)
{
    public string Key => $"{Title}\n{Artist}\n{Album}\n{Duration:F0}";
}
public sealed record LyricLine(double Seconds, string Text);
public sealed record Lyrics(IReadOnlyList<LyricLine> Lines, string Plain, string Status)
{
    public static Lyrics Empty(string status) => new([], "", status);
    public static Lyrics Parse(string? synced, string? plain)
    {
        var lines = new List<LyricLine>();
        foreach (var line in (synced ?? "").Split('\n'))
        {
            var matches = Regex.Matches(line, @"\[(\d+):(\d{2}(?:\.\d+)?)\]");
            var text = Regex.Replace(line, @"\[[^\]]*\]", "").Trim();
            foreach (Match m in matches)
                lines.Add(new(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), text));
        }
        return new(lines.OrderBy(l => l.Seconds).ToArray(), plain ?? "", lines.Count > 0 ? "Synced lyrics" : string.IsNullOrWhiteSpace(plain) ? "No lyrics found" : "Unsynced lyrics");
    }
    public int ActiveIndex(double seconds) => Lines.Select((line, i) => (line, i)).Where(x => x.line.Seconds <= seconds).Select(x => x.i).DefaultIfEmpty(-1).Last();
}
public sealed class Preferences
{
    public double Scale { get; set; } = 1;
    public double Opacity { get; set; } = .64;
    public string Theme { get; set; } = "Album";
    public double Corners { get; set; } = 22;
    public double LyricsSize { get; set; } = 18;
    public double LyricsOffset { get; set; }
    public bool ReducedMotion { get; set; }
    public bool LyricsOpen { get; set; }
    public string Hotkey { get; set; } = "G";
    public int X { get; set; } = 80;
    public int Y { get; set; } = 80;
    public void Normalize()
    {
        Scale = FiniteClamp(Scale, .75, 2, 1); Opacity = FiniteClamp(Opacity, .15, .95, .64);
        Corners = FiniteClamp(Corners, 0, 32, 22); LyricsSize = FiniteClamp(LyricsSize, 12, 28, 18);
        LyricsOffset = FiniteClamp(LyricsOffset, -10, 10, 0);
        if (Theme is not ("Album" or "Dark" or "Light")) Theme = "Album";
        if (Hotkey.Length != 1 || !char.IsAsciiLetterOrDigit(Hotkey[0])) Hotkey = "G";
        Hotkey = Hotkey.ToUpperInvariant();
    }
    private static double FiniteClamp(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
public interface IPreferencesStore { Preferences Load(); void Save(Preferences preferences); }
public sealed class PreferencesStore : IPreferencesStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetCommandLineArgs().Contains("--demo") ? "PrismPlay-Demo" : "PrismPlay");
    private readonly string path = Path.Combine(Folder, "settings.json");
    public Preferences Load()
    {
        try { var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new(); p.Normalize(); return p; }
        catch { return new(); }
    }
    public void Save(Preferences preferences)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
// PrismPlay was formerly GlassBeat. On the first run, copy settings and lyrics (cached and imported) from the old folder; never
// its WebView2 cache, and never delete the old folder.
public static class LegacyData
{
    public static bool Copy(string legacy, string current)
    {
        if (Directory.Exists(current) || !Directory.Exists(legacy)) return false;
        Directory.CreateDirectory(current);
        var settings = Path.Combine(legacy, "settings.json");
        if (File.Exists(settings)) File.Copy(settings, Path.Combine(current, "settings.json"));
        foreach (var name in new[] { "lyrics-cache", "imported-lyrics" })
        {
            var from = Path.Combine(legacy, name);
            if (!Directory.Exists(from)) continue;
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(current, name, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
        }
        return true;
    }
}
