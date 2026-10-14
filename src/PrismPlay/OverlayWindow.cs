using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Windows.Storage.Pickers;

namespace PrismPlay;

// Windows owns playback/windowing. The bundled React app owns the entire overlay surface.
public sealed class OverlayWindow : Window
{
    private readonly IPreferencesStore store = new PreferencesStore();
    private readonly Preferences preferences;
    private readonly IPlaybackService playbackService = new SpotifyPlayback();
    private readonly ILyricsProvider lyricsProvider = new LyricsProvider();
    private readonly NativeWindow native;
    public void KeepOnTop() => native.KeepOnTop();
    private readonly WebView2 web = new() { DefaultBackgroundColor = Colors.Transparent };
    private readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly bool demo;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer saveTimer;
    private Playback playback = Playback.Waiting();
    private Lyrics lyrics = Lyrics.Empty("Play a song to see lyrics");
    private CancellationTokenSource lyricsCancellation = new();
    // Settings is a drawer of the overlay; the host owns whether it is open.
    private bool settingsOpen, suspendedGameMode, startup;
    private bool ready, disposed, moving, seekRejected;
    private string status = "Open Spotify to get started";
    private string hotkeyError = "", lyricsStatus = "", generalStatus = "", waveformStatus = "";
    private SpotifyAudioCapture? capture;
    private bool waveformWanted;
    private double logicalHeight = 156;
    private byte[]? lastArtwork;
    private string? artwork;
    private int commandVersion;

    public OverlayWindow(bool demo)
    {
        this.demo = demo; preferences = store.Load(); Title = "PrismPlay";
        // Slider drags call ApplySettings per tick; write the file once the value settles.
        saveTimer = DispatcherQueue.CreateTimer(); saveTimer.Interval = TimeSpan.FromMilliseconds(400); saveTimer.IsRepeating = false;
        saveTimer.Tick += (_, _) => Save();
        SystemBackdrop = new TransparentBackdrop();
        Content = web;
        // WinUI's WebView2 paints this theme brush (#202020 in dark) behind the page, ignoring DefaultBackgroundColor; it only creates
        // the brush when the key is missing, so a transparent one lets the card's antialiased corners show the desktop.
        foreach (var key in new[] { "BrushForThemeBackgroundColor", "BrushForThemeBackgroundColor_HC" }) web.Resources[key] = new SolidColorBrush(Colors.Transparent);
        native = new NativeWindow(this, preferences, demo || Environment.GetCommandLineArgs().Contains("--inspect"));
        // The tray click already moved the foreground, so opening from there may activate the overlay for the keyboard.
        native.ToggleRequested += ToggleGameMode; native.SettingsRequested += () => { native.Show(); SetSettings(true, activate: true); }; native.QuitRequested += Close;
        native.PositionChanged += Save;
        native.MoveStateChanged += value => { moving = value; Publish(); };
        native.Resize(300 * preferences.Scale, logicalHeight * preferences.Scale);
        SetHotkey(preferences.Hotkey[0]);
        playbackService.Changed += OnPlayback;
        Closed += (_, _) =>
        {
            disposed = true; ready = false; lyricsCancellation.Cancel(); playbackService.Dispose(); native.Dispose(); capture?.Dispose();
            Save(); web.Close(); Application.Current.Exit();
        };
        _ = InitializeWeb();
        if (demo)
        {
            Receive(new(new Track("Weightless", "PrismPlay · preview session", "After hours", 213), false, 54, DateTimeOffset.UtcNow, true, true, true, true, true, null, ""));
            lyrics = Lyrics.Parse("[00:00.00]Let the world slow down\n[00:50.00]We float above the city lights\n[01:00.00]And leave the noise behind", null);
        }
        else _ = playbackService.StartAsync();
    }
    private async Task InitializeWeb()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(PreferencesStore.Folder, "WebView2"), new CoreWebView2EnvironmentOptions());
            if (disposed) return;
            await web.EnsureCoreWebView2Async(environment);
            if (disposed) return;
            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = Environment.GetCommandLineArgs().Contains("--inspect");
            core.Settings.IsZoomControlEnabled = false; core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.SetVirtualHostNameToFolderMapping("prismplay.local", Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://prismplay.local/", StringComparison.Ordinal)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += Message;
            core.NavigationCompleted += (_, e) => { if (!e.IsSuccess) DiagnosticLog.Write("Web UI navigation failed: " + e.WebErrorStatus); };
            core.Navigate("https://prismplay.local/index.html");
        }
        catch (Exception e)
        {
            DiagnosticLog.Write("WebView initialization: " + e);
            Content = new TextBlock { Text = "PrismPlay couldn't load its interface.\nEnsure Microsoft Edge WebView2 Runtime is installed, then restart.\nRight-click the tray icon for Settings or Quit.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), FontSize = 12 };
        }
    }
    private async void Message(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (disposed || !args.Source.StartsWith("https://prismplay.local/", StringComparison.Ordinal)) return;
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            var root = message.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready": ready = true; Publish(); break;
                case "size":
                    if (root.TryGetProperty("height", out var h) && h.TryGetDouble(out var height) && double.IsFinite(height))
                    {
                        logicalHeight = Math.Clamp(height / preferences.Scale, 156, NativeWindow.FrameHeight);
                        native.Resize(300 * preferences.Scale, logicalHeight * preferences.Scale, root.TryGetProperty("animate", out var a) && a.ValueKind == JsonValueKind.True);
                    }
                    break;
                case "dragBegin": native.BeginDrag(); break;
                case "dragEnd": native.EndDrag(); break;
                case "seek":
                    if (root.TryGetProperty("position", out var p) && p.TryGetDouble(out var position) && double.IsFinite(position) && playback.CanSeek)
                        await Command("seek", Math.Clamp(position, 0, playback.Track?.Duration ?? 0));
                    break;
                case "play": if (playback.CanPlay) await Command("play"); break;
                case "pause": if (playback.CanPause) await Command("pause"); break;
                case "previous": if (playback.CanPrevious) await Command("previous"); break;
                case "next": if (playback.CanNext) await Command("next"); break;
                case "lyrics": preferences.LyricsOpen = !preferences.LyricsOpen; ApplySettings(); break;
                case "retryLyrics":
                    if (playback.Track is not null)
                    {
                        lyricsCancellation.Cancel(); lyricsCancellation.Dispose(); lyricsCancellation = new();
                        lyrics = Lyrics.Empty("Looking for lyrics…"); Publish();
                        _ = FetchLyrics(playback.Track, lyricsCancellation.Token, allowPlainUpgrade: true);
                    }
                    break;
                case "scale": preferences.Scale = preferences.Scale >= 1.5 ? .75 : preferences.Scale + .25; ApplySettings(); break;
                case "game": ToggleGameMode(); break;
                case "settings": SetSettings(!settingsOpen); break;
                case "closeSettings": SetSettings(false); break;
                case "setPreference":
                    if (root.TryGetProperty("key", out var key) && root.TryGetProperty("value", out var value) && SetPreference(key.GetString(), value)) ApplySettings();
                    break;
                case "setHotkey":
                    var text = root.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()!.Trim().ToUpperInvariant() : "";
                    if (text.Length != 1 || !char.IsAsciiLetterOrDigit(text[0])) generalStatus = "Enter one letter or number.";
                    else if (!SetHotkey(text[0])) generalStatus = native.HotkeyRegistered ? "Shortcut in use. Previous shortcut retained." : "Shortcut in use. No shortcut is active; the default could not be registered.";
                    else { preferences.Hotkey = text; ApplySettings(); generalStatus = "Shortcut saved."; }
                    Publish(); break;
                case "startup":
                    if (root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        try
                        {
                            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
                            if (enabled.GetBoolean()) run.SetValue("PrismPlay", $"\"{Environment.ProcessPath}\""); else run.DeleteValue("PrismPlay", false);
                        }
                        catch { generalStatus = "Windows did not allow the startup setting to change."; }
                        startup = StartupEnabled(); Publish();
                    }
                    break;
                case "importLyrics":
                    try
                    {
                        var picker = new FileOpenPicker(); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this)); picker.FileTypeFilter.Add(".lrc");
                        var file = await picker.PickSingleFileAsync();
                        if (file is null) break;
                        var result = await ImportLyrics(file.Path);
                        if (settingsOpen) lyricsStatus = result;
                    }
                    catch { lyricsStatus = "Could not open the file picker."; }
                    Publish(); break;
            }
        }
        catch (Exception e) { DiagnosticLog.Write("UI command rejected: " + e.Message); }
    }
    private void OnPlayback(Playback state) => DispatcherQueue.TryEnqueue(() => { if (!disposed) Receive(state); });
    private void Receive(Playback state)
    {
        var changed = state.Track?.Key != playback.Track?.Key;
        playback = state;
        if (changed)
        {
            commandVersion++;
            lyricsCancellation.Cancel(); lyricsCancellation.Dispose(); lyricsCancellation = new();
            lyrics = Lyrics.Empty(state.Track is null ? "Play a song to see lyrics" : "Looking for lyrics…");
            status = state.Track is null ? state.Status : ""; seekRejected = false;
            if (state.Track is not null && !demo) _ = FetchLyrics(state.Track, lyricsCancellation.Token);
        }
        if (!ReferenceEquals(lastArtwork, state.Artwork))
        {
            lastArtwork = state.Artwork;
            artwork = state.Artwork is null ? null : "data:image/png;base64," + Convert.ToBase64String(state.Artwork);
        }
        UpdateWaveform(); Publish();
    }
    private async Task FetchLyrics(Track track, CancellationToken token, bool allowPlainUpgrade = false)
    {
        try
        {
            var result = await lyricsProvider.GetAsync(track, token, allowPlainUpgrade);
            if (!token.IsCancellationRequested && !disposed && playback.Track?.Key == track.Key)
            {
                lyrics = result;
                DiagnosticLog.Write($"Lyrics result: {result.Status}; timedLines={result.Lines.Count}; plainCharacters={result.Plain.Length}");
                Publish();
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task Command(string command, double position = 0)
    {
        var version = ++commandVersion;
        status = ""; seekRejected = false;
        if (demo)
        {
            playback = playback with { Position = command == "seek" ? position : playback.PositionNow, SampledAt = DateTimeOffset.UtcNow, Playing = command == "play" || command != "pause" && playback.Playing };
            Publish(); return;
        }
        var succeeded = await playbackService.CommandAsync(command, position);
        if (disposed || version != commandVersion) return;
        if (!succeeded) { status = command == "seek" ? "SEEK UNAVAILABLE" : "Control unavailable"; seekRejected = command == "seek"; }
        Publish();
    }
    private void Publish()
    {
        if (!ready || disposed) return;
        var state = new
        {
            playback.Track, playback.Playing, Position = playback.PositionNow, SampledAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            playback.CanPlay, playback.CanPause, playback.CanPrevious, playback.CanNext, playback.CanSeek,
            Artwork = artwork, Preferences = preferences, GameMode = native.GameMode, Moving = moving,
            Lyrics = lyrics, Status = status, SeekRejected = seekRejected, HotkeyError = hotkeyError,
            SettingsOpen = settingsOpen, Startup = startup, LyricsStatus = lyricsStatus, GeneralStatus = generalStatus, WaveformStatus = waveformStatus
        };
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { Type = "snapshot", State = state }, json));
    }
    // Kept apart from `status`, which track changes reset: the card shows the conflict until some shortcut is registered.
    private bool SetHotkey(char key)
    {
        var registered = native.SetHotkey(key);
        hotkeyError = native.HotkeyRegistered ? "" : $"Ctrl+Alt+{char.ToUpperInvariant(key)} in use";
        Publish(); return registered;
    }
    // Turning game mode on (hotkey, tray, button) closes Settings first; Settings had suspended it, so there is nothing to restore.
    private void ToggleGameMode() { if (settingsOpen) { suspendedGameMode = false; SetSettings(false); } native.SetGameMode(!native.GameMode); Publish(); }
    private void ApplySettings() { preferences.Normalize(); native.Resize(300 * preferences.Scale, logicalHeight * preferences.Scale); saveTimer.Start(); UpdateWaveform(); Publish(); }
    // Captures Spotify's audio only while the waveform is on and a track plays (callers publish). Acting on changes only, a failure
    // is retried at the next play or Waveform toggle. Frames go straight to the page, outside the snapshot.
    private void UpdateWaveform()
    {
        var wanted = preferences.Waveform && playback.Playing && !disposed;
        if (wanted == waveformWanted) return;
        waveformWanted = wanted; capture?.Dispose(); capture = null; waveformStatus = "";
        if (!wanted) return;
        if (SpotifyAudioCapture.FindSpotify() is not int pid) { waveformStatus = "Spotify's audio isn't available"; return; }
        SpotifyAudioCapture? started = null;
        started = capture = new SpotifyAudioCapture(pid,
            pcm => DispatcherQueue.TryEnqueue(() =>
            {
                if (ready && capture == started) web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { Type = "audio", Rate = SpotifyAudioCapture.Rate, Pcm = pcm }, json));
            }),
            () => DispatcherQueue.TryEnqueue(() =>
            {
                if (capture != started) return;
                capture = null; waveformStatus = "Couldn't read Spotify's audio"; Publish();
            }));
    }
    // Publish never saves, so reporting the failure cannot loop; after Closed, Publish is a no-op.
    private void Save() { saveTimer.Stop(); try { store.Save(preferences); } catch { status = "Settings could not be saved"; Publish(); } }
    // Open Settings suspends game mode (clicks must reach it) and lets the overlay take keyboard focus; closing restores both.
    private void SetSettings(bool open, bool activate = false)
    {
        if (open != settingsOpen)
        {
            settingsOpen = open;
            if (open) { startup = StartupEnabled(); if (native.GameMode) { suspendedGameMode = true; native.SetGameMode(false); } }
            else
            {
                lyricsStatus = generalStatus = "";
                if (saveTimer.IsRunning) Save();
                if (suspendedGameMode) { suspendedGameMode = false; native.SetGameMode(true); }
            }
            native.SetActivatable(open);
        }
        if (open && activate) { native.Activate(); web.Focus(FocusState.Programmatic); }
        Publish();
    }
    // Settings writes one whitelisted preference at a time; an unknown key or a value of the wrong type is rejected.
    private bool SetPreference(string? key, JsonElement value)
    {
        double? number = value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
        bool? flag = value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        switch (key, number, flag, value.ValueKind == JsonValueKind.String ? value.GetString() : null)
        {
            case ("scale", double v, _, _): preferences.Scale = v; return true;
            case ("opacity", double v, _, _): preferences.Opacity = v; return true;
            case ("corners", double v, _, _): preferences.Corners = v; return true;
            case ("lyricsSize", double v, _, _): preferences.LyricsSize = v; return true;
            case ("lyricsOffset", double v, _, _): preferences.LyricsOffset = v; return true;
            case ("reducedMotion", _, bool on, _): preferences.ReducedMotion = on; return true;
            case ("waveform", _, bool on, _): preferences.Waveform = on; return true;
            case ("waveformStyle", _, _, "Bars" or "Wave" or "Strip"): preferences.WaveformStyle = value.GetString()!; return true;
            case ("theme", _, _, "Album" or "Dark" or "Light"): preferences.Theme = value.GetString()!; return true;
            default: return false;
        }
    }
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("PrismPlay") is string;
    }
    private async Task<string> ImportLyrics(string path)
    {
        var track = playback.Track;
        if (track is null) return "Play a track before importing lyrics.";
        try
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) return "Choose a lyrics file smaller than 2 MB.";
            var content = await File.ReadAllTextAsync(path);
            await LocalLyricsStore.SaveAsync(track, content);
            if (playback.Track?.Key != track.Key) return "Lyrics saved for the original track. The current track has changed.";
            lyricsCancellation.Cancel(); lyrics = Lyrics.Parse(content, content); preferences.LyricsOpen = true; ApplySettings();
            return "Lyrics saved for this track.";
        }
        catch { return "Could not read or save the lyrics file."; }
    }
}
// A per-pixel transparent window, so the CSS card's antialiased rounded edge is the visible shape (the pattern of WinUIEx's
// TransparentTintBackdrop). There is no blur behind the card.
internal sealed class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? compositor;
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        // The brush must be set before base.OnTargetConnected, as WinUIEx does.
        try
        {
            // A system Compositor needs a Windows.System DispatcherQueue on this thread; WinUI only creates the Microsoft one.
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null) CreateDispatcherQueueController(new() { Size = 12, ThreadType = 2, ApartmentType = 2 }, out _);
            compositor ??= new Windows.UI.Composition.Compositor();
            target.SystemBackdrop = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
        catch (Exception e) { DiagnosticLog.Write("Transparent backdrop failed: " + e); }
        base.OnTargetConnected(target, root);
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target) { target.SystemBackdrop = null; base.OnTargetDisconnected(target); }
    [StructLayout(LayoutKind.Sequential)] private struct Options { public int Size, ThreadType, ApartmentType; }
    // ponytail: the controller is never released; it must outlive the UI thread's compositor anyway.
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(Options options, out nint controller);
}
