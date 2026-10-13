import AppKit

// Single instance: a second launch (e.g. `open -n`) exits; LaunchServices already reuses a running copy for normal opens.
if let id = Bundle.main.bundleIdentifier, NSRunningApplication.runningApplications(withBundleIdentifier: id).contains(where: { $0.processIdentifier != getpid() }) {
    exit(0)
}
let app = NSApplication.shared
app.setActivationPolicy(.accessory)   // menu bar app; Info.plist also sets LSUIElement
withExtendedLifetime(Overlay()) { app.run() }
