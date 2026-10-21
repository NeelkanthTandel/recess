import AppKit

// Renders the app icon: a cup on a warm gradient squircle.
let out = CommandLine.arguments[1]
let size: CGFloat = 1024
let image = NSImage(size: NSSize(width: size, height: size), flipped: false) { rect in
    let inset = rect.insetBy(dx: 100, dy: 100)
    let path = NSBezierPath(roundedRect: inset, xRadius: 185, yRadius: 185)
    NSGradient(colors: [NSColor(srgbRed: 0.27, green: 0.72, blue: 0.62, alpha: 1), NSColor(srgbRed: 0.13, green: 0.42, blue: 0.62, alpha: 1)])!.draw(in: path, angle: -70)
    let config = NSImage.SymbolConfiguration(pointSize: 470, weight: .medium).applying(.init(paletteColors: [.white]))
    if let sym = NSImage(systemSymbolName: "cup.and.saucer.fill", accessibilityDescription: nil)?.withSymbolConfiguration(config) {
        let s = sym.size
        sym.draw(in: NSRect(x: (size - s.width) / 2, y: (size - s.height) / 2 - 10, width: s.width, height: s.height))
    }
    return true
}
let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size), pixelsHigh: Int(size), bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
image.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: out))
