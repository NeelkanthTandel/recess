import AppKit
import SwiftUI

enum AppInfo {
    private static var nameCache: [String: String] = [:]

    static func url(forBundleID id: String) -> URL? {
        NSWorkspace.shared.urlForApplication(withBundleIdentifier: id)
    }

    static func name(forBundleID id: String) -> String {
        if let n = nameCache[id] { return n }
        var name = id
        if let running = NSRunningApplication.runningApplications(withBundleIdentifier: id).first, let n = running.localizedName {
            name = n
        } else if let url = url(forBundleID: id) {
            name = FileManager.default.displayName(atPath: url.path).replacingOccurrences(of: ".app", with: "")
        }
        nameCache[id] = name
        return name
    }

    static func icon(forBundleID id: String) -> NSImage {
        if let url = url(forBundleID: id) { return NSWorkspace.shared.icon(forFile: url.path) }
        return NSImage(systemSymbolName: "app", accessibilityDescription: nil) ?? NSImage()
    }

    /// Lets the user pick one or more apps; returns their bundle identifiers.
    @MainActor
    static func chooseApps() -> [String] {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.application]
        panel.allowsMultipleSelection = true
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.message = "Choose which applications to include (multiple selection available)."
        guard panel.runModal() == .OK else { return [] }
        return panel.urls.compactMap { Bundle(url: $0)?.bundleIdentifier }
    }
}

extension RGBA {
    var color: Color { Color(.sRGB, red: r, green: g, blue: b, opacity: a) }
    var nsColor: NSColor { NSColor(srgbRed: r, green: g, blue: b, alpha: a) }

    init(_ color: Color) {
        let ns = NSColor(color).usingColorSpace(.sRGB) ?? .black
        self.init(r: ns.redComponent, g: ns.greenComponent, b: ns.blueComponent, a: ns.alphaComponent)
    }

    var hex: String {
        func byte(_ v: Double) -> Int { max(0, min(255, Format.int(v * 255))) }
        return String(format: "#%02X%02X%02X", byte(r), byte(g), byte(b))
    }
}
