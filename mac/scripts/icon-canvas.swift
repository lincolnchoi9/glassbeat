// Draws assets/icon.png on Apple's app-icon grid: the tile at 824 px, centred on a transparent 1024 px canvas.
// Usage: swift icon-canvas.swift <icon.png> <out.png>
import CoreGraphics
import Foundation
import ImageIO

let args = CommandLine.arguments
guard let source = CGImageSourceCreateWithURL(URL(fileURLWithPath: args[1]) as CFURL, nil),
      let tile = CGImageSourceCreateImageAtIndex(source, 0, nil),
      let canvas = CGContext(data: nil, width: 1024, height: 1024, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                             bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
      let output = CGImageDestinationCreateWithURL(URL(fileURLWithPath: args[2]) as CFURL, "public.png" as CFString, 1, nil)
else { fatalError("cannot read \(args[1]) or write \(args[2])") }
canvas.interpolationQuality = .high
canvas.draw(tile, in: CGRect(x: 100, y: 100, width: 824, height: 824))
CGImageDestinationAddImage(output, canvas.makeImage()!, nil)
guard CGImageDestinationFinalize(output) else { fatalError("cannot write \(args[2])") }
