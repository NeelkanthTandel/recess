import AppKit
import SwiftUI

/// One break in progress: owns the per-screen windows, countdown, stage actions and buttons.
@MainActor
final class BreakSession: ObservableObject {
    enum Result {
        case done(TimeInterval)
        case endedEarly(TimeInterval)
        case postponed(Double)
        case skipped
    }

    enum Phase { case fadingIn, running, waitingForEnd, fadingOut }

    let breakItem: Break
    let isPreview: Bool
    let settings: AppSettings
    let theme: Theme
    private let completion: (Result) -> Void

    @Published private(set) var elapsed: TimeInterval = 0
    @Published private(set) var phase: Phase = .fadingIn

    private let startDate = Date()
    private var windows: [NSWindow] = []
    private var timer: Timer?
    private var firedAfterFadeIn = false
    private var firedBeforeEnd: Set<UUID> = []
    private var finished = false
    private var previousPresentation: NSApplication.PresentationOptions = []
    private var previousFrontApp: NSRunningApplication?

    init(breakItem: Break, isPreview: Bool, settings: AppSettings, completion: @escaping (Result) -> Void) {
        self.breakItem = breakItem
        self.isPreview = isPreview
        self.settings = settings
        self.theme = ThemeManager.shared.theme(id: breakItem.appearance.themeID)
        self.completion = completion
        // A calendar break lasts until its event ends, even if it started late.
        if breakItem.schedule.adjustment == .calendar, let end = breakItem.state.nextEventEnd, !isPreview {
            duration = max(5, min(breakItem.duration, end.timeIntervalSinceNow))
        } else {
            duration = breakItem.duration
        }
    }

    let duration: TimeInterval
    var remaining: TimeInterval { max(0, duration - elapsed) }
    var progress: Double { duration > 0 ? min(1, elapsed / duration) : 1 }

    var buttonsEnabled: Bool {
        elapsed >= breakItem.controls.disableButtonsSeconds + breakItem.controls.fadeInSeconds || isPreview
    }

    var showEndEarly: Bool {
        breakItem.controls.showEndEarly && elapsed >= breakItem.controls.endEarlyAfter.timeInterval && phase == .running
    }

    var canPostpone: Bool { isPreview || breakItem.canPostpone }
    var canSkip: Bool { isPreview || breakItem.canSkip }

    // MARK: - Lifecycle

    func begin() {
        previousFrontApp = NSWorkspace.shared.frontmostApplication
        if !isPreview { Scheduler.shared.performActions(breakItem, stage: .atStart) }
        buildWindows()
        let fadeIn = max(0.05, breakItem.controls.fadeInSeconds)
        for w in windows {
            w.alphaValue = 0
            w.orderFrontRegardless()
        }
        NSApp.activate(ignoringOtherApps: true)
        windows.first?.makeKey()
        applyPresentationOptions()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = fadeIn
            windows.forEach { $0.animator().alphaValue = 1 }
        }
        // Common modes keep the countdown running while a menu is open.
        let timer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick(now: Date()) }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func tick(now: Date) {
        guard !finished else { return }
        elapsed = now.timeIntervalSince(startDate)
        let c = breakItem.controls
        if phase == .fadingIn && elapsed >= c.fadeInSeconds { phase = .running }
        if !firedAfterFadeIn && elapsed >= c.fadeInSeconds {
            firedAfterFadeIn = true
            if !isPreview { Scheduler.shared.performActions(breakItem, stage: .afterFadeIn) }
        }
        if !isPreview {
            for a in breakItem.actions where a.enabled && a.stage == .beforeEnd && !firedBeforeEnd.contains(a.id) {
                if remaining <= a.offset {
                    firedBeforeEnd.insert(a.id)
                    ActionPerformer.shared.perform(a, for: breakItem)
                }
            }
        }
        // Keep the break windows in front if something else grabbed focus.
        if theme.id != ThemeID.none, Format.int(elapsed * 4) % 8 == 0, NSApp.isActive == false, phase != .fadingOut {
            NSApp.activate(ignoringOtherApps: true)
            windows.forEach { $0.orderFrontRegardless() }
        }
        if elapsed >= duration && phase != .waitingForEnd && phase != .fadingOut {
            if c.endWhenDone && !isPreview {
                phase = .waitingForEnd
                NSSound.beep()
            } else {
                finish(.done(duration))
            }
        }
        // Runaway guard.
        if elapsed > duration + 4 * 3600 { finish(.done(duration)) }
    }

    func finish(_ result: Result) {
        guard !finished else { return }
        finished = true
        phase = .fadingOut
        timer?.invalidate()
        timer = nil
        if case .done = result, !isPreview { Scheduler.shared.performActions(breakItem, stage: .atEnd) }
        if case .endedEarly = result, !isPreview { Scheduler.shared.performActions(breakItem, stage: .atEnd) }
        let fadeOut = max(0.05, breakItem.controls.fadeOutSeconds)
        let windows = self.windows
        NSAnimationContext.runAnimationGroup(
            { ctx in
                ctx.duration = fadeOut
                windows.forEach { $0.animator().alphaValue = 0 }
            },
            completionHandler: { [weak self] in
                MainActor.assumeIsolated {
                    windows.forEach {
                        $0.orderOut(nil); $0.contentView = nil
                    }
                    self?.restorePresentationOptions()
                    if let app = self?.previousFrontApp, app.bundleIdentifier != Bundle.main.bundleIdentifier {
                        app.activate()
                    }
                    self?.completion(result)
                }
            })
    }

    // MARK: - User commands from the overlay

    func postpone(_ slot: Int) {
        let c = breakItem.controls
        postpone(minutes: slot == 1 ? c.postponeMinutes1 : c.postponeMinutes2)
    }

    /// Postpone and skip honour the break's limits and the "disable the buttons" delay,
    /// whether they come from the overlay, the menu bar, a shortcut or a URL.
    func postpone(minutes: Double) {
        guard canPostpone, buttonsEnabled else { NSSound.beep(); return }
        finish(.postponed(minutes))
    }

    func skip() {
        guard canSkip, buttonsEnabled else { NSSound.beep(); return }
        finish(.skipped)
    }

    func endEarly() { finish(.endedEarly(elapsed)) }

    func endWhenDone() { finish(.done(elapsed)) }

    // MARK: - Windows

    private func buildWindows() {
        let screens = NSScreen.screens
        // With no display attached (e.g. while switching monitors) the break just counts down.
        guard let primary = screens.first else { return }
        let main: NSScreen = {
            switch breakItem.appearance.mainScreen {
            case .activeMenubar: return NSScreen.main ?? primary
            case .primary: return primary
            }
        }()
        let level: NSWindow.Level = settings.showMenuBarDuringBreak ? .mainMenu - 1 : .screenSaver

        if theme.id == ThemeID.none {
            let size = NSSize(width: 360, height: 210)
            let frame = NSRect(
                x: main.visibleFrame.maxX - size.width - 20, y: main.visibleFrame.minY + 20, width: size.width, height: size.height)
            let panel = BreakWindow(contentRect: frame, styleMask: [.borderless], backing: .buffered, defer: false)
            configure(panel, level: .floating)
            panel.contentView = NSHostingView(rootView: CompactBreakView(session: self))
            windows = [panel]
            return
        }

        for screen in screens {
            let isMain = screen == main
            let mode: OverlayMode
            if isMain {
                mode = .main
            } else {
                switch breakItem.appearance.otherScreens {
                case .theme: mode = .mirror
                case .dim: mode = .dim
                case .none: continue
                }
            }
            let w = BreakWindow(contentRect: screen.frame, styleMask: [.borderless], backing: .buffered, defer: false)
            configure(w, level: level)
            w.setFrame(screen.frame, display: false)
            w.contentView = NSHostingView(rootView: BreakOverlayView(session: self, mode: mode))
            if isMain { windows.insert(w, at: 0) } else { windows.append(w) }
        }
    }

    private func configure(_ w: NSWindow, level: NSWindow.Level) {
        w.level = level
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        w.isReleasedWhenClosed = false
        w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        w.animationBehavior = .none
    }

    private func applyPresentationOptions() {
        guard settings.disableAppSwitching, !isPreview, theme.id != ThemeID.none else { return }
        previousPresentation = NSApp.presentationOptions
        NSApp.presentationOptions = [
            .hideDock, .hideMenuBar, .disableProcessSwitching, .disableForceQuit, .disableSessionTermination, .disableHideApplication,
        ]
    }

    private func restorePresentationOptions() {
        guard settings.disableAppSwitching, !isPreview, theme.id != ThemeID.none else { return }
        NSApp.presentationOptions = previousPresentation
    }
}

final class BreakWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

enum OverlayMode { case main, mirror, dim }
