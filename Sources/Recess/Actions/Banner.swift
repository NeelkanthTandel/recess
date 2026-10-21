import AppKit
import SwiftUI
import UserNotifications

/// Tracks whether macOS lets Recess post notifications, for the settings UI.
@MainActor
final class NotificationAccess: ObservableObject {
    static let shared = NotificationAccess()

    @Published private(set) var status: UNAuthorizationStatus = .notDetermined

    private init() { refresh() }

    func refresh() {
        UNUserNotificationCenter.current().getNotificationSettings { settings in
            let status = settings.authorizationStatus
            DispatchQueue.main.async {
                MainActor.assumeIsolated { self.status = status }
            }
        }
    }

    var isAllowed: Bool { status == .authorized || status == .provisional }

    var description: String {
        switch status {
        case .authorized, .provisional, .ephemeral: "Allowed"
        case .denied: "Turned off in System Settings; Recess shows its own banner instead"
        case .notDetermined: "Not yet requested"
        @unknown default: "Unknown"
        }
    }

    static func openSystemSettings() {
        let id = Bundle.main.bundleIdentifier ?? "com.neel.recess"
        let url = URL(string: "x-apple.systempreferences:com.apple.Notifications-Settings.extension?id=\(id)")!
        NSWorkspace.shared.open(url)
    }
}

/// Recess's own notification-style banner, used when system notifications aren't allowed.
@MainActor
final class BannerController {
    static let shared = BannerController()

    private var panel: NSPanel?
    private var dismissWork: DispatchWorkItem?

    func show(title: String, body: String, color: NSColor, seconds: TimeInterval = 8) {
        dismiss()
        let size = NSSize(width: 360, height: 84)
        guard let screen = NSScreen.main ?? NSScreen.screens.first else { return }
        let frame = NSRect(
            x: screen.visibleFrame.maxX - size.width - 14, y: screen.visibleFrame.maxY - size.height - 10,
            width: size.width, height: size.height)
        let p = NSPanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.level = .statusBar
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = true
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        p.isReleasedWhenClosed = false
        p.contentView = NSHostingView(
            rootView: BannerView(title: title, message: body, color: Color(nsColor: color)) { [weak self] in
                self?.dismiss()
            })
        p.alphaValue = 0
        p.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup {
            $0.duration = 0.25; p.animator().alphaValue = 1
        }
        panel = p
        let work = DispatchWorkItem { [weak self] in self?.dismiss() }
        dismissWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
    }

    func dismiss() {
        dismissWork?.cancel()
        dismissWork = nil
        guard let p = panel else { return }
        panel = nil
        NSAnimationContext.runAnimationGroup(
            {
                $0.duration = 0.25; p.animator().alphaValue = 0
            },
            completionHandler: {
                MainActor.assumeIsolated { p.orderOut(nil) }
            })
    }
}

private struct BannerView: View {
    let title: String
    let message: String
    let color: Color
    let dismiss: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "cup.and.saucer.fill")
                .font(.system(size: 20))
                .foregroundStyle(color)
                .frame(width: 32, height: 32)
                .background(Circle().fill(color.opacity(0.18)))
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.headline).lineLimit(1)
                Text(message).font(.callout).foregroundStyle(.secondary).lineLimit(2)
            }
            Spacer(minLength: 0)
        }
        .padding(14)
        .frame(width: 360, height: 84, alignment: .topLeading)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(.white.opacity(0.08)))
        .contentShape(Rectangle())
        .onTapGesture(perform: dismiss)
    }
}
