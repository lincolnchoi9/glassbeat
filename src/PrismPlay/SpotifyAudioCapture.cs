using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PrismPlay;

// Reads Spotify's own output with WASAPI process loopback (Microsoft's ApplicationLoopback sample), for the waveform only: a
// separate capture client, so Spotify's playback is never touched. Everything runs on one background MTA thread; `frame` gets
// AudioFrame base64 about every 33 ms while new audio arrives, and `failed` once if capture stops on an error. Neither is
// called after Dispose.
internal sealed class SpotifyAudioCapture : IDisposable
{
    public const int Rate = 44100;
    private readonly int processId;
    private readonly Action<string> frame;
    private readonly Action failed;
    private volatile bool stopping;

    public SpotifyAudioCapture(int processId, Action<string> frame, Action failed)
    {
        this.processId = processId; this.frame = frame; this.failed = failed;
        var thread = new Thread(Run) { IsBackground = true, Name = "Waveform capture" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }
    public void Dispose() => stopping = true;

    public static int? FindSpotify()
    {
        var processes = Process.GetProcessesByName("Spotify");
        static DateTime Started(Process p) { try { return p.StartTime; } catch { return DateTime.MaxValue; } }
        // ponytail: earliest Spotify process = root of its tree
        try { return processes.OrderBy(Started).Select(p => (int?)p.Id).FirstOrDefault(); }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    private void Run()
    {
        using var wake = new AutoResetEvent(false);
        IAudioClient? client = null; IAudioCaptureClient? reader = null;
        try
        {
            client = Activate(processId);
            var format = new WaveFormat { Tag = 1, Channels = 2, Rate = Rate, AvgBytes = Rate * 4, BlockAlign = 4, Bits = 16 };
            client.Initialize(0, Loopback | EventCallback, 200000, 0, ref format, 0);
            client.SetEventHandle(wake.SafeWaitHandle.DangerousGetHandle());
            var iid = typeof(IAudioCaptureClient).GUID;
            client.GetService(ref iid, out var service);
            reader = (IAudioCaptureClient)service;
            client.Start();
            var history = new short[AudioFrame.Samples * 2];
            var clock = Stopwatch.StartNew();
            long next = 0; var fresh = false;
            while (!stopping)
            {
                wake.WaitOne(100);
                for (reader.GetNextPacketSize(out var size); size > 0 && !stopping; reader.GetNextPacketSize(out size))
                {
                    reader.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    // Shift the newest `n` samples in at the end; a silent packet's buffer is to be read as zeros.
                    var n = (int)Math.Min(frames, AudioFrame.Samples) * 2;
                    Array.Copy(history, n, history, 0, history.Length - n);
                    if ((flags & Silent) != 0) Array.Clear(history, history.Length - n, n);
                    else Marshal.Copy(data + ((int)frames * 2 - n) * 2, history, history.Length - n, n);
                    reader.ReleaseBuffer(frames);
                    fresh |= frames > 0;
                }
                // Only new audio is sent: when Spotify stops rendering, frames stop and the overlay eases to rest.
                if (fresh && clock.ElapsedMilliseconds >= next && !stopping) { next = (clock.ElapsedMilliseconds / 33 + 1) * 33; fresh = false; frame(AudioFrame.Encode(history, 2)); }
            }
            client.Stop();
        }
        catch (Exception e)
        {
            DiagnosticLog.Write("Waveform capture: " + e.Message);
            if (!stopping) failed();
        }
        finally
        {
            if (reader is not null) Marshal.ReleaseComObject(reader);
            if (client is not null) Marshal.ReleaseComObject(client);
        }
    }

    private static IAudioClient Activate(int processId)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS { PROCESS_LOOPBACK, { TargetProcessId, INCLUDE_TARGET_PROCESS_TREE } }, passed as a VT_BLOB.
        var parameters = Marshal.AllocHGlobal(12);
        IActivateAudioInterfaceAsyncOperation? operation = null;
        try
        {
            Marshal.WriteInt32(parameters, 0, 1); Marshal.WriteInt32(parameters, 4, processId); Marshal.WriteInt32(parameters, 8, 0);
            var blob = new PropVariant { Type = 65, Size = 12, Data = parameters };
            var completion = new Completion();
            ActivateAudioInterfaceAsync("VAD\\Process_Loopback", typeof(IAudioClient).GUID, ref blob, completion, out operation);
            if (!completion.Done.Wait(5000)) throw new TimeoutException("Process loopback activation did not complete.");
            operation.GetActivateResult(out var result, out var activated);
            Marshal.ThrowExceptionForHR(result);
            return (IAudioClient)activated;
        }
        finally
        {
            if (operation is not null) Marshal.ReleaseComObject(operation);
            Marshal.FreeHGlobal(parameters);
        }
    }

    private const int Loopback = 0x00020000, EventCallback = 0x00040000, Silent = 0x2;
    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
        ref PropVariant parameters, IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation operation);
    [StructLayout(LayoutKind.Explicit)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public int Size; [FieldOffset(16)] public nint Data; }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat { public ushort Tag, Channels; public int Rate, AvgBytes; public ushort BlockAlign, Bits, Extra; }

    // The completion arrives on a system MTA thread, so the handler is agile (IAgileObject), as Windows requires.
    private sealed class Completion : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new();
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation) => Done.Set();
    }
    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject { }
    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler { void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation); }
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation { void GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object activated); }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, ref WaveFormat format, nint sessionGuid);
        // Vtable slots this class never calls: GetBufferSize, GetStreamLatency, GetCurrentPadding, IsFormatSupported, GetMixFormat, GetDevicePeriod.
        void Unused1(); void Unused2(); void Unused3(); void Unused4(); void Unused5(); void Unused6();
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(nint handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        void GetBuffer(out nint data, out uint frames, out int flags, out long devicePosition, out long qpcPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }
}
