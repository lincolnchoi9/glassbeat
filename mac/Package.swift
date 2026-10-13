// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "PrismPlay",
    platforms: [.macOS(.v13)],
    targets: [
        // Pure logic shared with the Windows host's Core.cs, LyricsProvider.cs and DragMath.cs.
        .target(name: "PrismPlayCore"),
        // AppKit + WebKit host for the bundled React overlay (frontend/dist).
        .executableTarget(name: "PrismPlay", dependencies: ["PrismPlayCore"]),
        .testTarget(name: "PrismPlayCoreTests", dependencies: ["PrismPlayCore"]),
    ]
)
