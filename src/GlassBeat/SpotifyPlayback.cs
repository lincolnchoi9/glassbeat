using Windows.Media.Control;

namespace GlassBeat;
public sealed record Playback(Track? Track, bool Playing, double Position, DateTimeOffset SampledAt, bool CanPlay, bool CanPause, bool CanPrevious, bool CanNext, bool CanSeek, byte[]? Artwork, string Status)
{
    public static Playback Waiting(string status = "Open Spotify to get started") => new(null, false, 0, DateTimeOffset.UtcNow, false, false, false, false, false, null, status);
    public double PositionNow => Track is null ? 0 : Math.Clamp(Position + (Playing ? (DateTimeOffset.UtcNow - SampledAt).TotalSeconds : 0), 0, Math.Max(0, Track.Duration));
}
public interface IPlaybackService : IDisposable
{
    event Action<Playback>? Changed;
    Task StartAsync();
    Task<bool> CommandAsync(string command, double position = 0);
}
public sealed class SpotifyPlayback : IPlaybackService
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private readonly SemaphoreSlim refreshLock = new(1);
    private bool disposed;
    private int refreshQueued;
    private string? artworkKey;
    private byte[]? artwork;
    public event Action<Playback>? Changed;
    public async Task StartAsync()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            DiagnosticLog.Write($"Media sessions available: {manager.GetSessions().Count}; Spotify sessions: {manager.GetSessions().Count(s => s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))}");
            manager.SessionsChanged += SessionsChanged;
            SelectSession();
        }
        catch { Changed?.Invoke(Playback.Waiting("Windows media access unavailable · restart to retry")); }
    }
    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => SelectSession();
    private void SelectSession()
    {
        if (disposed) return;
        var next = manager?.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
        if (Equals(next, session)) { _ = RefreshAsync(); return; }
        Detach(); session = next; artworkKey = null; artwork = null;
        Changed?.Invoke(Playback.Waiting());
        if (session is not null)
        {
            session.MediaPropertiesChanged += MediaChanged;
            session.PlaybackInfoChanged += PlaybackChanged;
            session.TimelinePropertiesChanged += TimelineChanged;
        }
        _ = RefreshAsync();
    }
    private void MediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshAsync();
    private void PlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = RefreshAsync();
    private void TimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ = RefreshAsync();
    private async Task RefreshAsync()
    {
        // Coalesce event bursts: while one refresh runs, every further event shares a single queued follow-up.
        // The flag is cleared only after the lock is taken, so the follow-up always reads state newer than the last event.
        if (Interlocked.Exchange(ref refreshQueued, 1) == 1) return;
        await refreshLock.WaitAsync();
        Interlocked.Exchange(ref refreshQueued, 0);
        var current = session;
        try
        {
            if (current is null || disposed) return;
            var media = await current.TryGetMediaPropertiesAsync();
            if (current != session || disposed) return;
            var timeline = current.GetTimelineProperties();
            var info = current.GetPlaybackInfo();
            DiagnosticLog.Write($"Spotify snapshot: status={info.PlaybackStatus}; duration={(timeline.EndTime - timeline.StartTime).TotalSeconds}; artwork={media.Thumbnail is not null}; seek={info.Controls.IsPlaybackPositionEnabled}; next={info.Controls.IsNextEnabled}; previous={info.Controls.IsPreviousEnabled}");
            var track = new Track(media.Title, media.Artist, media.AlbumTitle, Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds));
            if (track.Key != artworkKey)
            {
                artworkKey = track.Key; artwork = null;
                // Publish immediately so the previous track's image and lyrics disappear.
                Publish(null);
                if (media.Thumbnail is not null)
                {
                    try
                    {
                        using var stream = await media.Thumbnail.OpenReadAsync();
                        if (stream.Size <= 8 * 1024 * 1024)
                        {
                            using var reader = new Windows.Storage.Streams.DataReader(stream);
                            await reader.LoadAsync((uint)stream.Size);
                            var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); artwork = bytes;
                        }
                    }
                    catch { artwork = null; }
                }
                // The thumbnail read can take a while; publish the position and controls as they are now.
                if (current != session || disposed) return;
                timeline = current.GetTimelineProperties(); info = current.GetPlaybackInfo();
            }
            Publish(artwork);
            void Publish(byte[]? bytes)
            {
                if (current != session || disposed) return;
                var controls = info.Controls;
                Changed?.Invoke(new(track, info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    (timeline.Position - timeline.StartTime).TotalSeconds, timeline.LastUpdatedTime,
                    controls.IsPlayEnabled, controls.IsPauseEnabled, controls.IsPreviousEnabled, controls.IsNextEnabled,
                    controls.IsPlaybackPositionEnabled && track.Duration > 0, bytes, ""));
            }
        }
        catch (Exception e)
        {
            DiagnosticLog.Write("Spotify refresh failed: " + e.Message);
            // Keep the last snapshot (and its lyrics fetch) through a transient COM error. Blank only when Windows
            // positively no longer lists this session; a switch to another session is published by SelectSession.
            if (!disposed && current is not null && current == session && !Listed(current)) Changed?.Invoke(Playback.Waiting("Waiting for Spotify…"));
        }
        finally { refreshLock.Release(); }
    }
    // Unknown (the session list itself failed) counts as listed, so a wider COM hiccup does not blank the UI.
    private bool Listed(GlobalSystemMediaTransportControlsSession target)
    {
        try { return manager?.GetSessions().Contains(target) != false; } catch { return true; }
    }
    public async Task<bool> CommandAsync(string command, double position = 0)
    {
        var current = session;
        if (current is null) return false;
        try
        {
            var succeeded = command switch
            {
                "play" => await current.TryPlayAsync(), "pause" => await current.TryPauseAsync(),
                "previous" => await current.TrySkipPreviousAsync(), "next" => await current.TrySkipNextAsync(),
                "seek" => await current.TryChangePlaybackPositionAsync(current.GetTimelineProperties().StartTime.Ticks + TimeSpan.FromSeconds(position).Ticks), _ => false
            };
            DiagnosticLog.Write($"Playback command: {command}; success={succeeded}; target={position:F2}");
            if (succeeded) await RefreshAsync();
            return succeeded;
        }
        catch { return false; }
    }
    private void Detach()
    {
        if (session is null) return;
        session.MediaPropertiesChanged -= MediaChanged; session.PlaybackInfoChanged -= PlaybackChanged; session.TimelinePropertiesChanged -= TimelineChanged;
    }
    public void Dispose() { disposed = true; Detach(); if (manager is not null) manager.SessionsChanged -= SessionsChanged; }
}
