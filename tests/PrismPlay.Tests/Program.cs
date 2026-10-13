using PrismPlay;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
Check(DragMath.Step(10, 100, .016, false) is > 10 and < 100, "Drag easing moves toward target without overshoot");
Check(Math.Abs(DragMath.Step(DragMath.Step(0, 100, .008, false), 100, .008, false) - DragMath.Step(0, 100, .016, false)) < .00001, "Drag easing is independent of frame rate");
Check(DragMath.Step(10, 100, .016, true) == 100, "Reduced motion follows pointer directly");
var settled = 0d; for (var i = 0; i < 15; i++) settled = DragMath.Step(settled, 100, .008, false);
Check(Math.Abs(100 - settled) < 1, "Released drag settles within 120 milliseconds");
var spring = Enumerable.Range(0, 61).Select(i => DragMath.Spring(156, 0, 480, i / 100d)).ToArray();
var (midX, midV) = DragMath.Spring(156, 0, 480, .1); var retarget = DragMath.Spring(midX, midV, 200, 0);
Check(spring[0].X == 156 && spring.Zip(spring.Skip(1)).All(p => p.Second.X >= p.First.X && p.Second.X <= 480) && 480 - spring[^1].X < .5
    && Math.Abs(retarget.X - midX) < 1e-9 && Math.Abs(retarget.V - midV) < 1e-9, "Region spring rises without overshoot, settles by 0.6 s, and retargets without a jump");
var lyrics = Lyrics.Parse("[ar:Artist]\n[00:03.50][01:03.500]Repeat\n[00:01.00]First\nmalformed\n[00:05]Last", null);
Check(lyrics.Lines.Count == 4, "Multiple timestamps and metadata handling");
Check(lyrics.Lines[0].Text == "First", "Unordered input is sorted");
Check(lyrics.ActiveIndex(.9) == -1, "No lyric before first timestamp");
Check(lyrics.ActiveIndex(3.5) == 1, "Exact timestamp selects new line");
Check(lyrics.ActiveIndex(2) == 0 && lyrics.ActiveIndex(64) == 3, "Seek backward and forward select correctly");
Check(Lyrics.Parse(null, "Plain lyrics").Status == "Unsynced lyrics", "Plain text never gets fabricated timestamps");
Check(Lyrics.Parse(null, null).Status == "No lyrics found", "Missing lyrics state");
var prefs = new Preferences { Scale = double.NaN, Opacity = 5, Theme = "invalid", Corners = -20, Hotkey = "!!!" };
prefs.Normalize();
Check(prefs.Scale == 1 && prefs.Opacity == .95 && prefs.Corners == 0, "Invalid numeric preferences recover safely");
Check(prefs.Theme == "Album" && prefs.Hotkey == "G", "Invalid theme and shortcut recover safely");
var legacyRoot = Path.Combine(Path.GetTempPath(), "PrismPlay-migrate-" + Guid.NewGuid().ToString("N"));
var (legacy, current) = (Path.Combine(legacyRoot, "GlassBeat"), Path.Combine(legacyRoot, "PrismPlay"));
foreach (var dir in new[] { "lyrics-cache", "imported-lyrics", "WebView2" }) Directory.CreateDirectory(Path.Combine(legacy, dir));
File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}"); File.WriteAllText(Path.Combine(legacy, "lyrics-cache", "a.json"), "c");
File.WriteAllText(Path.Combine(legacy, "imported-lyrics", "b.lrc"), "i"); File.WriteAllText(Path.Combine(legacy, "WebView2", "x"), "w");
Check(LegacyData.Copy(legacy, current) && File.Exists(Path.Combine(current, "settings.json")) && File.ReadAllText(Path.Combine(current, "lyrics-cache", "a.json")) == "c"
    && File.Exists(Path.Combine(current, "imported-lyrics", "b.lrc")) && !Directory.Exists(Path.Combine(current, "WebView2")) && File.Exists(Path.Combine(legacy, "WebView2", "x"))
    && !LegacyData.Copy(legacy, current), "GlassBeat data migrates once, without WebView2, keeping the old folder");
Directory.Delete(legacyRoot, true);
var cache = Path.Combine(Path.GetTempPath(), "PrismPlay-test-" + Guid.NewGuid().ToString("N"));
var handler = new FakeHandler();
var provider = new LyricsProvider(new HttpClient(handler), cache, true);
var track = new Track("Song", "Artist", "Album", 120);
handler.Json = """{"trackName":"Song","artistName":"Artist","duration":120,"syncedLyrics":"[00:01.00]Hello","plainLyrics":"Hello","instrumental":false}""";
Check((await provider.GetAsync(track, default)).Lines.Count == 1, "Matching provider response is accepted");
Check((await provider.GetAsync(track, default)).Lines.Count == 1 && handler.Calls == 1, "Cache avoids duplicate network requests");
handler.Json = """{"trackName":"Wrong song","artistName":"Artist","duration":130,"syncedLyrics":"[00:01.00]Wrong","plainLyrics":"Wrong"}""";
Check((await provider.GetAsync(track with { Title = "Another" }, default)).Lines.Count == 0, "Wrong track or duration never displays mismatched lyrics");
handler.Status = System.Net.HttpStatusCode.NotFound;
Check((await provider.GetAsync(track with { Title = "Missing" }, default)).Status == "No lyrics found for this track", "Missing provider result is explicit");
handler.Status = System.Net.HttpStatusCode.TooManyRequests;
Check((await provider.GetAsync(track with { Title = "Limited" }, default)).Status.Contains("unavailable"), "Rate limiting degrades without breaking playback");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await provider.GetAsync(track, cancelled.Token); Check(false, "Cancellation propagates"); } catch (OperationCanceledException) { Check(true, "Cancellation propagates"); }
var disabled = new LyricsProvider(new HttpClient(handler), cache, false);
var before = handler.Calls;
await disabled.GetAsync(track, default);
Check(handler.Calls == before, "Explicitly disabled test provider makes no network requests");
handler.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/search")
    ? new(System.Net.HttpStatusCode.OK) { Content = new StringContent("""[{"trackName":"Search Song!","artistName":"Artist","albumName":"Other edition","duration":121,"syncedLyrics":"[00:01]Found automatically","plainLyrics":"Found automatically"},{"trackName":"Search Song - Live","artistName":"Artist","duration":120,"syncedLyrics":"[00:01]Wrong version"}]""") }
    : new(System.Net.HttpStatusCode.NotFound);
var searched = await provider.GetAsync(track with { Title = "Search Song", Album = "Spotify edition" }, default);
Check(searched.Lines.Count == 1 && searched.Lines[0].Text == "Found automatically", "Search fallback tolerates album and punctuation differences while rejecting other versions");
handler.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/search")
    ? new(System.Net.HttpStatusCode.TooManyRequests)
    : new(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"trackName":"Plain Song","artistName":"Artist","duration":120,"syncedLyrics":null,"plainLyrics":"Plain available"}""") };
Check((await provider.GetAsync(track with { Title = "Plain Song" }, default)).Plain == "Plain available", "Plain lyrics survive a failed enhancement search");
handler.Respond = _ => throw new HttpRequestException("Offline");
Check((await provider.GetAsync(track with { Title = "Search Song", Album = "Spotify edition" }, default)).Lines.Count == 1, "Cached synced lyrics remain available offline");
Check((await provider.GetAsync(track with { Title = "Not cached" }, default)).Status.Contains("unavailable"), "Uncached offline request has a clear error state");
// A broken cache folder must never discard lyrics that were downloaded successfully.
var readOnly = track with { Title = "Read-only cache" };
handler.Respond = _ => Ok(Row(readOnly.Title, true));
var blocked = CacheFile(cache, readOnly) + ".tmp";
File.WriteAllText(blocked, ""); File.SetAttributes(blocked, FileAttributes.ReadOnly);
Check((await provider.GetAsync(readOnly, default)).Lines.Count == 1, "Unwritable lyrics cache still returns downloaded lyrics");
File.SetAttributes(blocked, FileAttributes.Normal);
var locked = track with { Title = "Locked cache" };
handler.Respond = _ => Ok(Row(locked.Title, true));
using (new FileStream(CacheFile(cache, locked), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
    Check((await provider.GetAsync(locked, default)).Lines.Count == 1, "Unreadable lyrics cache falls back to the network");
// Retry can upgrade a cached plain-only entry once synced lyrics become available.
var upgrade = track with { Title = "Upgrade Song" };
handler.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/search") ? new(System.Net.HttpStatusCode.NotFound) : Ok(Row(upgrade.Title, false));
await provider.GetAsync(upgrade, default);
handler.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/search") ? Ok(new[] { Row(upgrade.Title, true) }) : Ok(Row(upgrade.Title, false));
before = handler.Calls;
Check((await provider.GetAsync(upgrade, default)).Plain == upgrade.Title && handler.Calls == before, "Normal lookup serves a cached plain-only entry without network");
Check((await provider.GetAsync(upgrade, default, allowPlainUpgrade: true)).Lines.Count == 1, "Retry upgrades a cached plain-only entry to synced lyrics");
handler.Respond = _ => throw new HttpRequestException("Offline");
Check((await provider.GetAsync(upgrade, default)).Lines.Count == 1, "Upgraded synced lyrics replace the cached plain-only entry");
Check((await provider.GetAsync(track with { Title = "Plain Song" }, default, allowPlainUpgrade: true)).Plain == "Plain available", "Offline Retry keeps a cached plain-only entry");
Directory.Delete(cache, true);
if (args.Contains("--live"))
{
    var live = await new LyricsProvider().GetAsync(new Track("Hymn for the Weekend", "Coldplay", "A Head Full of Dreams", 258), default);
    Check(live.Lines.Count > 10, "Live automatic LRCLIB lookup returns timed lyrics without any local import");
    Console.WriteLine($"Live lookup: {live.Lines.Count} timed lines; {live.Status}");
}
Console.WriteLine($"{checks} checks passed.");

static object Row(string title, bool synced) => new { trackName = title, artistName = "Artist", duration = 120, syncedLyrics = synced ? "[00:01.00]" + title : null, plainLyrics = title };
static HttpResponseMessage Ok(object body) => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body)) };
static string CacheFile(string folder, Track t) => Path.Combine(folder, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(t.Key))) + ".json");

sealed class FakeHandler : HttpMessageHandler
{
    public int Calls; public string Json = "{}"; public System.Net.HttpStatusCode Status = System.Net.HttpStatusCode.OK;
    public Func<HttpRequestMessage, HttpResponseMessage>? Respond;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Calls++; return Task.FromResult(Respond?.Invoke(request) ?? new HttpResponseMessage(Status) { Content = new StringContent(Json) }); }
}
