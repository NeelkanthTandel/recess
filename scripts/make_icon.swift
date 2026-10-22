import AppKit

// Renders the 1024×1024 app icon: a sunlit countdown ring on a dusk-blue tile, with a sun
// rising over the horizon inside it. Everything is drawn here, so no third-party artwork
// (including SF Symbols, which may not be used in app icons) ends up in the icon.
//
// Usage: swift scripts/make_icon.swift out.png [--windows]
//
// --windows drops Apple's margin and fills the canvas, since Windows draws icons edge to edge.

let out = CommandLine.arguments[1]
let windows = CommandLine.arguments.contains("--windows")
let size: CGFloat = 1024

func color(_ hex: UInt32, _ alpha: CGFloat = 1) -> NSColor {
    NSColor(
        srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
        blue: CGFloat(hex & 0xFF) / 255, alpha: alpha)
}

let image = NSImage(size: NSSize(width: size, height: size), flipped: false) { _ in
    if windows {
        // Scale the 824 pt tile up to a 984 pt one, leaving a 20 pt margin for the edge.
        let scale: CGFloat = 984 / 824
        let transform = NSAffineTransform()
        transform.translateX(by: 512, yBy: 512)
        transform.scale(by: scale)
        transform.translateX(by: -512, yBy: -512)
        transform.concat()
    }
    // Tile: Apple's icon grid leaves a 100 pt margin around an 824 pt rounded square.
    let tile = NSRect(x: 100, y: 100, width: 824, height: 824)
    let tilePath = NSBezierPath(roundedRect: tile, xRadius: 186, yRadius: 186)
    NSGradient(colors: [color(0x33467A), color(0x1D2847), color(0x121A33)], atLocations: [0, 0.55, 1], colorSpace: .sRGB)!
        .draw(in: tilePath, angle: -90)

    let center = NSPoint(x: 512, y: 512)
    let radius: CGFloat = 262
    let width: CGFloat = 62

    // Ring track.
    let track = NSBezierPath()
    track.appendArc(withCenter: center, radius: radius, startAngle: 0, endAngle: 360)
    track.lineWidth = width
    color(0xFFFFFF, 0.12).setStroke()
    track.stroke()

    // Countdown arc: starts at the top and runs clockwise, three quarters still to go.
    let arc = NSBezierPath()
    arc.appendArc(withCenter: center, radius: radius, startAngle: 90, endAngle: 90 - 290, clockwise: true)
    arc.lineWidth = width
    arc.lineCapStyle = .round
    color(0xF5B14C).setStroke()
    arc.stroke()

    // Sun rising over the horizon, clipped to the horizon line.
    let horizonY: CGFloat = 470
    NSGraphicsContext.saveGraphicsState()
    NSBezierPath(rect: NSRect(x: 0, y: horizonY, width: size, height: size)).addClip()
    let sun = NSBezierPath(ovalIn: NSRect(x: 512 - 118, y: horizonY - 118, width: 236, height: 236))
    NSGradient(colors: [color(0xFFE3A8), color(0xF5B14C)])!.draw(in: sun, angle: -90)
    NSGraphicsContext.restoreGraphicsState()

    // Horizon line.
    let horizon = NSBezierPath()
    horizon.move(to: NSPoint(x: 512 - 178, y: horizonY - 14))
    horizon.line(to: NSPoint(x: 512 + 178, y: horizonY - 14))
    horizon.lineWidth = 26
    horizon.lineCapStyle = .round
    color(0xDFE8F1).setStroke()
    horizon.stroke()

    // A shorter line below, like water or a distant shore, to read as "far away".
    let shore = NSBezierPath()
    shore.move(to: NSPoint(x: 512 - 104, y: horizonY - 72))
    shore.line(to: NSPoint(x: 512 + 104, y: horizonY - 72))
    shore.lineWidth = 22
    shore.lineCapStyle = .round
    color(0xDFE8F1, 0.45).setStroke()
    shore.stroke()
    return true
}

let rep = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: Int(size), pixelsHigh: Int(size), bitsPerSample: 8, samplesPerPixel: 4,
    hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
image.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: out))
