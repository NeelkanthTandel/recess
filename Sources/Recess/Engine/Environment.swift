import AppKit
import CoreGraphics

/// The things rules can test. Each value is looked up only when a rule first asks for it,
/// so a cycle with no matching rules costs nothing.
@MainActor
final class EnvironmentSnapshot {
    private let monitor: EnvironmentMonitor
    init(monitor: EnvironmentMonitor) { self.monitor = monitor }

    private lazy var frontApp = NSWorkspace.shared.frontmostApplication
    lazy var frontAppBundleID: String? = frontApp?.bundleIdentifier
    lazy var runningBundleIDs: Set<String> = Set(NSWorkspace.shared.runningApplications.compactMap(\.bundleIdentifier))
    lazy var isFullscreen: Bool = EnvironmentMonitor.frontAppIsFullscreen(pid: frontApp?.processIdentifier)
    lazy var isScreensaver: Bool =
        monitor.isScreensaver
        || frontAppBundleID == "com.apple.ScreenSaver.Engine" || frontAppBundleID == "com.apple.ScreenSaver.Engine.legacyScreenSaver"
    lazy var isDisplayAsleep: Bool = monitor.isDisplayAsleep || CGDisplayIsAsleep(CGMainDisplayID()) != 0
    lazy var isFocusOn: Bool = EnvironmentMonitor.isFocusOn()
}

/// Tracks idle time, sleep, screensaver, display sleep, Focus and fullscreen apps.
@MainActor
final class EnvironmentMonitor {
    static let shared = EnvironmentMonitor()

    private(set) var isScreensaver = false
    private(set) var isDisplayAsleep = false
    private(set) var sleepDate: Date?
    /// Set on wake: how long the Mac slept. Consumed by the scheduler.
    var pendingSleepInterval: TimeInterval?

    private var lastEventDate = Date()
    private var globalMonitor: Any?
    private var localMonitor: Any?

    private init() {
        let ws = NSWorkspace.shared.notificationCenter
        ws.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.sleepDate = Date() }
        }
        ws.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                if let s = self.sleepDate { self.pendingSleepInterval = Date().timeIntervalSince(s) }
                self.sleepDate = nil
            }
        }
        ws.addObserver(forName: NSWorkspace.screensDidSleepNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.isDisplayAsleep = true }
        }
        ws.addObserver(forName: NSWorkspace.screensDidWakeNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.isDisplayAsleep = false }
        }
        let dnc = DistributedNotificationCenter.default()
        dnc.addObserver(forName: Notification.Name("com.apple.screensaver.didstart"), object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.isScreensaver = true }
        }
        dnc.addObserver(forName: Notification.Name("com.apple.screensaver.didstop"), object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.isScreensaver = false }
        }
    }

    /// Enables the Event Monitor idle detector (needs Accessibility permission for global events).
    func configureEventMonitor(enabled: Bool) {
        if enabled, globalMonitor == nil {
            let mask: NSEvent.EventTypeMask = [
                .mouseMoved, .leftMouseDown, .rightMouseDown, .otherMouseDown, .keyDown, .scrollWheel, .leftMouseDragged, .flagsChanged,
            ]
            globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: mask) { [weak self] _ in
                MainActor.assumeIsolated { self?.lastEventDate = Date() }
            }
            localMonitor = NSEvent.addLocalMonitorForEvents(matching: mask) { [weak self] e in
                MainActor.assumeIsolated { self?.lastEventDate = Date() }
                return e
            }
        } else if !enabled {
            if let g = globalMonitor { NSEvent.removeMonitor(g) }
            if let l = localMonitor { NSEvent.removeMonitor(l) }
            globalMonitor = nil
            localMonitor = nil
        }
    }

    func idleSeconds(detector: IdleDetector) -> TimeInterval {
        switch detector {
        case .none:
            return 0
        case .eventSource:
            return Self.hidIdleSeconds()
        case .eventMonitor:
            // Fall back to the HID value too, so idle is never under-reported if a monitor misses events.
            return min(Date().timeIntervalSince(lastEventDate), Self.hidIdleSeconds())
        }
    }

    static func hidIdleSeconds() -> TimeInterval {
        let anyInput = CGEventType(rawValue: ~0)!
        let v = CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: anyInput)
        // "Allegedly idle … years! don't believe that"
        return v > 60 * 60 * 24 * 365 ? 0 : v
    }

    var isMouseButtonDown: Bool { NSEvent.pressedMouseButtons != 0 }

    func snapshot() -> EnvironmentSnapshot { EnvironmentSnapshot(monitor: self) }

    /// True when the frontmost app has a window covering a whole screen.
    static func frontAppIsFullscreen(pid: pid_t?) -> Bool {
        guard let pid, pid != ProcessInfo.processInfo.processIdentifier else { return false }
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]]
        else { return false }
        let screens = NSScreen.screens.map { s -> CGRect in
            // Convert to the top-left origin space used by CGWindow bounds.
            let primaryHeight = NSScreen.screens.first?.frame.height ?? s.frame.height
            return CGRect(x: s.frame.minX, y: primaryHeight - s.frame.maxY, width: s.frame.width, height: s.frame.height)
        }
        for w in list {
            guard (w[kCGWindowOwnerPID as String] as? pid_t) == pid,
                (w[kCGWindowLayer as String] as? Int) == 0,
                let b = w[kCGWindowBounds as String] as? [String: CGFloat]
            else { continue }
            let rect = CGRect(x: b["X"] ?? 0, y: b["Y"] ?? 0, width: b["Width"] ?? 0, height: b["Height"] ?? 0)
            if screens.contains(where: {
                abs($0.width - rect.width) < 2 && abs($0.height - rect.height) < 2 && abs($0.minX - rect.minX) < 2
                    && abs($0.minY - rect.minY) < 2
            }) {
                return true
            }
        }
        return false
    }

    /// Best-effort Focus detection by reading the DND assertions database (works when the
    /// app has Full Disk Access; otherwise reports false).
    static func isFocusOn() -> Bool {
        let url = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/DoNotDisturb/DB/Assertions.json")
        guard let data = try? Data(contentsOf: url),
            let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
            let records = json["data"] as? [[String: Any]]
        else { return false }
        for r in records {
            if let assertions = r["storeAssertionRecords"] as? [Any], !assertions.isEmpty { return true }
        }
        return false
    }
}
