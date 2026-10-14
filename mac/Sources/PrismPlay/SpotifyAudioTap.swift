import CoreAudio
import Foundation
import PrismPlayCore
import WebKit

/// Reads Spotify's audio with a Core Audio process tap (macOS 14.2+, the AudioCap approach) and sends the overlay the most recent
/// 1024 mono samples about 30 times a second. The tap is unmuted, so Spotify keeps playing out loud. Main thread only, except
/// the IO block, which only writes `ring`. Call `stop()` before dropping it (the timers retain it).
@available(macOS 14.2, *)
final class SpotifyAudioTap: NSObject {
    private let web: WKWebView
    private let failed: () -> Void
    private let ring = Ring()
    private let queue = DispatchQueue(label: "io.github.lincolnchoi9.prismplay.audio", qos: .userInitiated)
    private var tap = kAudioObjectUnknown, device = kAudioObjectUnknown
    private var proc: AudioDeviceIOProcID?
    private var rate = 0
    private var timer: Timer?

    init(web: WKWebView, failed: @escaping () -> Void) {
        self.web = web; self.failed = failed
        super.init()
        attempt()
    }

    @objc private func attempt() {
        let processes = Self.spotifyProcesses()
        // Spotify registers with Core Audio when it first plays; until then, look again every 2 s.
        if processes.isEmpty { timer = Timer.scheduledTimer(timeInterval: 2, target: self, selector: #selector(attempt), userInfo: nil, repeats: false); return }
        do { try open(processes) } catch { stop(); failed(); return }
        timer = Timer.scheduledTimer(timeInterval: 1.0 / 30, target: self, selector: #selector(send), userInfo: nil, repeats: true)
    }

    private func open(_ processes: [AudioObjectID]) throws {
        let description = CATapDescription(stereoMixdownOfProcesses: processes)
        description.muteBehavior = .unmuted
        description.isPrivate = true
        try check(AudioHardwareCreateProcessTap(description, &tap))
        var output = kAudioObjectUnknown, format = AudioStreamBasicDescription()
        try check(Self.get(AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice, &output))
        guard let outputUID = Self.string(output, kAudioDevicePropertyDeviceUID) else { throw OSStatusError(status: kAudioHardwareBadDeviceError) }
        try check(Self.get(tap, kAudioTapPropertyFormat, &format))
        rate = Int(format.mSampleRate)
        let aggregate: [String: Any] = [
            kAudioAggregateDeviceNameKey: "PrismPlay waveform", kAudioAggregateDeviceUIDKey: UUID().uuidString,
            kAudioAggregateDeviceMainSubDeviceKey: outputUID, kAudioAggregateDeviceIsPrivateKey: true, kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceSubDeviceListKey: [[kAudioSubDeviceUIDKey: outputUID]],
            kAudioAggregateDeviceTapListKey: [[kAudioSubTapUIDKey: description.uuid.uuidString, kAudioSubTapDriftCompensationKey: true]],
        ]
        try check(AudioHardwareCreateAggregateDevice(aggregate as CFDictionary, &device))
        let ring = self.ring
        try check(AudioDeviceCreateIOProcIDWithBlock(&proc, device, queue) { _, input, _, _, _ in ring.write(input) })
        try check(AudioDeviceStart(device, proc))
    }

    @objc private func send() {
        web.evaluateJavaScript("window.__prismplay({type:'audio',rate:\(rate),pcm:'\(AudioFrame.encode(mono: ring.read()))'})")
    }

    /// Idempotent; after a failure the tap stays stopped until the owner replaces it.
    func stop() {
        timer?.invalidate(); timer = nil
        if let proc { AudioDeviceStop(device, proc); AudioDeviceDestroyIOProcID(device, proc) }
        if device != kAudioObjectUnknown { AudioHardwareDestroyAggregateDevice(device) }
        if tap != kAudioObjectUnknown { AudioHardwareDestroyProcessTap(tap) }
        proc = nil; device = kAudioObjectUnknown; tap = kAudioObjectUnknown
    }

    private struct OSStatusError: Error { let status: OSStatus }
    private func check(_ status: OSStatus) throws {
        guard status != noErr else { return }
        NSLog("PrismPlay: audio tap failed %d", status)
        throw OSStatusError(status: status)
    }

    // Every audio process object whose bundle ID starts with Spotify's, so its helper processes are included.
    private static func spotifyProcesses() -> [AudioObjectID] {
        let system = AudioObjectID(kAudioObjectSystemObject)
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyProcessObjectList, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(system, &address, 0, nil, &size) == noErr else { return [] }
        var ids = [AudioObjectID](repeating: kAudioObjectUnknown, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(system, &address, 0, nil, &size, &ids) == noErr else { return [] }
        return ids.prefix(Int(size) / MemoryLayout<AudioObjectID>.size).filter { string($0, kAudioProcessPropertyBundleID)?.hasPrefix(SpotifyPlayback.bundleID) == true }
    }
    private static func get<T>(_ object: AudioObjectID, _ selector: AudioObjectPropertySelector, _ value: inout T) -> OSStatus {
        var address = AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var size = UInt32(MemoryLayout<T>.size)
        return withUnsafeMutableBytes(of: &value) { AudioObjectGetPropertyData(object, &address, 0, nil, &size, $0.baseAddress!) }
    }
    // Core Audio returns CFStrings retained (+1).
    private static func string(_ object: AudioObjectID, _ selector: AudioObjectPropertySelector) -> String? {
        var value: Unmanaged<CFString>?
        guard get(object, selector, &value) == noErr, let value else { return nil }
        return value.takeRetainedValue() as String
    }
}

/// The last 1024 mono samples. Written on the IO queue without allocating, read on the main thread.
private final class Ring: @unchecked Sendable {
    private let lock = NSLock()
    private var samples = [Float](repeating: 0, count: AudioFrame.count), next = 0

    // Downmixes interleaved or planar Float32 buffers: each frame is the mean of every channel in every buffer.
    func write(_ input: UnsafePointer<AudioBufferList>) {
        let buffers = UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: input))
        var frames = Int.max, channels = 0
        for b in buffers where b.mNumberChannels > 0 && b.mData != nil {
            frames = min(frames, Int(b.mDataByteSize) / (MemoryLayout<Float>.size * Int(b.mNumberChannels))); channels += Int(b.mNumberChannels)
        }
        guard channels > 0 else { return }
        lock.lock(); defer { lock.unlock() }
        for f in 0..<frames {
            var sum: Float = 0
            for b in buffers where b.mNumberChannels > 0 {
                guard let data = b.mData?.assumingMemoryBound(to: Float.self) else { continue }
                let n = Int(b.mNumberChannels)
                for c in 0..<n { sum += data[f * n + c] }
            }
            samples[next] = sum / Float(channels); next = (next + 1) % samples.count
        }
    }

    func read() -> [Float] {
        lock.lock(); defer { lock.unlock() }
        return (0..<samples.count).map { samples[(next + $0) % samples.count] }
    }
}
