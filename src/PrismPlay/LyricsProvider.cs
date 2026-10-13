using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrismPlay;
public interface ILyricsProvider { Task<Lyrics> GetAsync(Track track, CancellationToken cancellationToken, bool allowPlainUpgrade = false); }
public sealed class LyricsProvider : ILyricsProvider
{
    private readonly HttpClient http;
    private readonly string cache;
    private readonly bool enabled;
    public LyricsProvider() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(12) }, Path.Combine(PreferencesStore.Folder, "lyrics-cache"), true) { }
    internal LyricsProvider(HttpClient http, string cache, bool enabled)
    {
        this.http = http; this.cache = cache; this.enabled = enabled;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PrismPlay/0.4 (Windows desktop overlay; LRCLIB client)");
    }
    // allowPlainUpgrade (Retry): a cached plain-only entry re-runs the synced search instead of being served as-is.
    public async Task<Lyrics> GetAsync(Track track, CancellationToken cancellationToken, bool allowPlainUpgrade = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var imported = await LocalLyricsStore.ReadAsync(track, cancellationToken);
        if (imported is not null) return Lyrics.Parse(imported, imported);
        if (!enabled) return Lyrics.Empty("Online lyrics are disabled");
        if (track.Duration <= 0 || string.IsNullOrWhiteSpace(track.Artist)) return Lyrics.Empty("Lyrics unavailable for this item");
        try
        {
            var file = Path.Combine(cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(track.Key))) + ".json");
            string? json = null, cached = null;
            if (File.Exists(file))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(file, cancellationToken);
                    using var parsed = JsonDocument.Parse(text);
                    if (Matches(parsed.RootElement, track)) json = cached = text;
                }
                // A corrupt, locked, or inaccessible cache entry falls back to the network.
                catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
            }
            if (cached is null)
            {
                var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(track.Title)}&artist_name={Uri.EscapeDataString(track.Artist)}&album_name={Uri.EscapeDataString(track.Album)}&duration={track.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                using var response = await http.GetAsync(url, cancellationToken);
                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    response.EnsureSuccessStatusCode();
                    var candidate = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var validate = JsonDocument.Parse(candidate);
                    if (Matches(validate.RootElement, track)) json = candidate;
                }
            }
            // Album names, punctuation, and release editions frequently differ from Spotify.
            // Keep the title, artist, and duration checks even when searching without the album.
            // ponytail: no negative cache; misses and plain-only Retries query LRCLIB each time. Add one if rate limits bite.
            if ((cached is null || allowPlainUpgrade) && (json is null || !HasSyncedLyrics(json)))
            {
                try
                {
                    var fallback = await SearchAsync(track, cancellationToken);
                    if (fallback is not null) json = fallback;
                }
                catch (HttpRequestException) when (json is not null) { }
                catch (OperationCanceledException) when (json is not null && !cancellationToken.IsCancellationRequested) { }
            }
            if (json is null) return Lyrics.Empty("No lyrics found for this track");
            if (json != cached)
            {
                try
                {
                    Directory.CreateDirectory(cache);
                    await File.WriteAllTextAsync(file + ".tmp", json, cancellationToken);
                    File.Move(file + ".tmp", file, true);
                    foreach (var old in new DirectoryInfo(cache).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(100)) old.Delete();
                }
                // Disk cache is optional; a locked, read-only, or denied folder still shows the downloaded lyrics.
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Matches(root, track)) return Lyrics.Empty("No matching lyrics found");
            if (root.TryGetProperty("instrumental", out var instrumental) && instrumental.GetBoolean()) return Lyrics.Empty("Instrumental");
            var result = Lyrics.Parse(Text(root, "syncedLyrics"), Text(root, "plainLyrics"));
            return result with { Status = result.Lines.Count > 0 ? "Synced lyrics · LRCLIB" : result.Plain.Length > 0 ? "Unsynced lyrics · LRCLIB" : "No lyrics found for this track" };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Lyrics.Empty("Lyrics unavailable · try again later"); }
    }
    private async Task<string?> SearchAsync(Track track, CancellationToken token)
    {
        using var response = await http.GetAsync($"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(track.Title)}&artist_name={Uri.EscapeDataString(track.Artist)}", token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
        var candidates = document.RootElement.EnumerateArray().Where(row => Matches(row, track))
            .OrderByDescending(row => !string.IsNullOrWhiteSpace(Text(row, "syncedLyrics")))
            .ThenByDescending(row => Normalize(Text(row, "albumName")) == Normalize(track.Album))
            .ThenBy(row => Math.Abs(row.GetProperty("duration").GetDouble() - track.Duration)).ToArray();
        return candidates.Length == 0 ? null : candidates[0].GetRawText();
    }
    private static bool HasSyncedLyrics(string json)
    {
        using var document = JsonDocument.Parse(json);
        return !string.IsNullOrWhiteSpace(Text(document.RootElement, "syncedLyrics")) ||
            document.RootElement.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True;
    }
    private static string? Text(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string Normalize(string? value) => string.Concat((value ?? "").Normalize(NormalizationForm.FormD).Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static bool Matches(JsonElement root, Track track) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("duration", out var duration) && duration.TryGetDouble(out var seconds) && Math.Abs(seconds - track.Duration) <= 3 &&
        Normalize(Text(root, "trackName")) == Normalize(track.Title) &&
        Normalize(Text(root, "artistName")) == Normalize(track.Artist);
}

public static class LocalLyricsStore
{
    private static string FileFor(Track track) => Path.Combine(PreferencesStore.Folder, "imported-lyrics", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(track.Key))) + ".lrc");
    public static async Task<string?> ReadAsync(Track track, CancellationToken token)
    {
        try { var file = FileFor(track); return File.Exists(file) ? await File.ReadAllTextAsync(file, token) : null; }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
    public static async Task SaveAsync(Track track, string content)
    {
        var file = FileFor(track); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file + ".tmp", content); File.Move(file + ".tmp", file, true);
    }
}
