import AppKit
import Combine
import UserNotifications

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, UNUserNotificationCenterDelegate {
    private let model = AppModel.shared
    private var cancellables: Set<AnyCancellable> = []

    func applicationWillFinishLaunching(_ notification: Notification) {
        NSAppleEventManager.shared().setEventHandler(
            self, andSelector: #selector(handleURL(_:reply:)),
            forEventClass: AEEventClass(kInternetEventClass), andEventID: AEEventID(kAEGetURL))
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "").count > 1 {
            let alert = NSAlert()
            alert.messageText = "Recess is already open."
            alert.informativeText = "Only one copy can run at a time, or breaks would happen twice."
            alert.runModal()
            NSApp.terminate(nil)
            return
        }

        installSampleScripts()
        buildMainMenu()
        applyPresentation()
        UNUserNotificationCenter.current().delegate = self
        // Ask once (macOS only prompts while the choice is undecided) so "before due" warnings can be shown.
        if model.settings.hasCompletedSetup { ActionPerformer.shared.requestNotificationPermission() }
        _ = ThemeManager.shared
        _ = CalendarManager.shared
        Scheduler.shared.start()
        registerShortcuts()

        NotificationCenter.default.publisher(for: .shortcutsChanged).sink { [weak self] _ in self?.registerShortcuts() }.store(
            in: &cancellables)
        NotificationCenter.default.publisher(for: .presentationChanged).sink { [weak self] _ in self?.applyPresentation() }.store(
            in: &cancellables)
        // Per-break shortcuts live on the breaks.
        model.$breaks.map { $0.map(\.startShortcut) }.removeDuplicates().dropFirst()
            .sink { [weak self] _ in DispatchQueue.main.async { self?.registerShortcuts() } }.store(in: &cancellables)

        if !model.settings.hasCompletedSetup {
            SetupAssistantController.shared.show()
        } else if model.settings.showDockIcon == false && model.settings.showStatusMenu == false {
            SettingsWindowController.shared.show()
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        SettingsWindowController.shared.show()
        return true
    }

    func applicationWillTerminate(_ notification: Notification) {
        model.saveNow()
        ActivityRecorder.shared.save()
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if let s = Scheduler.shared.session, s.settings.disableAppSwitching, !s.isPreview { return .terminateCancel }
        return .terminateNow
    }

    // MARK: - Presentation

    private func applyPresentation() {
        let s = model.settings
        if SettingsWindowController.shared.isVisible == false {
            NSApp.setActivationPolicy(s.showDockIcon ? .regular : .accessory)
        } else if s.showDockIcon {
            NSApp.setActivationPolicy(.regular)
        }
        StatusItemController.shared.setVisible(s.showStatusMenu)
    }

    private func buildMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu(title: "Recess")
        appMenu.addItem(withTitle: "About Recess", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Settings…", action: #selector(showSettings), keyEquivalent: ",").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Hide Recess", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(withTitle: "Quit Recess", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        let editItem = NSMenuItem()
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "Z")
        edit.addItem(.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)

        let breaksItem = NSMenuItem()
        let breaks = NSMenu(title: "Breaks")
        breaks.addItem(withTitle: "Start Next Break", action: #selector(startNext), keyEquivalent: "b").target = self
        breaks.addItem(withTitle: "Postpone Next Break", action: #selector(postponeNext), keyEquivalent: "").target = self
        breaks.addItem(withTitle: "Skip Next Break", action: #selector(skipNext), keyEquivalent: "").target = self
        breaks.addItem(.separator())
        breaks.addItem(withTitle: "Pause All Breaks Indefinitely", action: #selector(pauseAll), keyEquivalent: "").target = self
        breaks.addItem(withTitle: "Resume All Breaks", action: #selector(resumeAll), keyEquivalent: "").target = self
        breaks.addItem(withTitle: "Reset All Breaks", action: #selector(resetAll), keyEquivalent: "").target = self
        breaks.addItem(.separator())
        breaks.addItem(withTitle: "Stop Sound", action: #selector(stopSound), keyEquivalent: "").target = self
        breaksItem.submenu = breaks
        main.addItem(breaksItem)

        let windowItem = NSMenuItem()
        let window = NSMenu(title: "Window")
        window.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        window.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowItem.submenu = window
        main.addItem(windowItem)
        NSApp.windowsMenu = window
        NSApp.mainMenu = main
    }

    @objc private func showSettings() { SettingsWindowController.shared.show() }
    @objc private func startNext() { Scheduler.shared.startNextBreak() }
    @objc private func postponeNext() { Scheduler.shared.postponeNext(slot: 1) }
    @objc private func skipNext() { Scheduler.shared.skipNext() }
    @objc private func pauseAll() { Scheduler.shared.pauseAll(seconds: nil) }
    @objc private func resumeAll() { Scheduler.shared.resumeAll() }
    @objc private func resetAll() { Scheduler.shared.resetAll() }
    @objc private func stopSound() { ActionPerformer.shared.stopAllSound() }

    // MARK: - Shortcuts

    private func registerShortcuts() {
        let hk = HotKeyCenter.shared
        hk.unregisterAll()
        let s = model.settings.shortcuts
        let sched = Scheduler.shared
        if let k = s.startNext { hk.register(k) { sched.startNextBreak() } }
        if let k = s.postpone1 { hk.register(k) { sched.postponeNext(slot: 1) } }
        if let k = s.postpone2 { hk.register(k) { sched.postponeNext(slot: 2) } }
        if let k = s.skip { hk.register(k) { sched.skipNext() } }
        if let k = s.pauseIndefinitely { hk.register(k) { sched.pauseAll(seconds: nil) } }
        if let k = s.resume { hk.register(k) { sched.resumeAll() } }
        if let k = s.reset { hk.register(k) { sched.resetAll() } }
        if let k = s.stopSound { hk.register(k) { ActionPerformer.shared.stopAllSound() } }
        for b in model.breaks {
            if let k = b.startShortcut {
                let id = b.id
                hk.register(k) { sched.startBreak(id, manual: true) }
            }
        }
    }

    // MARK: - URL commands (recess://…)

    @objc private func handleURL(_ event: NSAppleEventDescriptor, reply: NSAppleEventDescriptor) {
        guard let s = event.paramDescriptor(forKeyword: keyDirectObject)?.stringValue, let url = URLComponents(string: s) else { return }
        // Any app or web page can open recess:// links, so they can be turned off, and every
        // number is checked before use.
        guard model.settings.allowURLCommands else {
            NSLog("Recess: ignored \(url.host ?? "") link (URL commands are turned off)")
            return
        }
        let command = (url.host ?? url.path).lowercased()
        let q = Dictionary((url.queryItems ?? []).map { ($0.name.lowercased(), $0.value ?? "") }, uniquingKeysWith: { a, _ in a })
        let sched = Scheduler.shared
        let target = q["break"].flatMap { name in model.breaks.first { $0.name.caseInsensitiveCompare(name) == .orderedSame } }
        /// A finite number within `range`, or nil.
        func number(_ key: String, _ range: ClosedRange<Double>) -> Double? {
            guard let v = q[key].flatMap(Double.init), v.isFinite else { return nil }
            return min(max(v, range.lowerBound), range.upperBound)
        }
        let minutes = number("minutes", 0...1440)
        switch command {
        case "start":
            if let target { sched.startBreak(target.id, manual: true) } else { sched.startNextBreak() }
        case "postpone":
            if let target {
                sched.postpone(target.id, minutes: minutes ?? target.controls.postponeMinutes1)
            } else if let minutes {
                sched.postponeCurrentOrNext(minutes: minutes)
            } else {
                sched.postponeNext(slot: 1)
            }
        case "skip":
            if let target { sched.skip(target.id) } else { sched.skipNext() }
        case "pause":
            let secs = minutes.map { $0 * 60 }
            if let target { sched.pause(target.id, seconds: secs) } else { sched.pauseAll(seconds: secs) }
        case "resume":
            if let target { sched.resume(target.id) } else { sched.resumeAll() }
        case "makedue":
            let secs = number("seconds", 0...86400) ?? (minutes ?? 1) * 60
            if let b = target ?? model.nextBreakAnyLength { sched.makeDue(b.id, in: secs) }
        case "reset":
            if let target { sched.resetBreak(target.id) } else { sched.resetAll() }
        case "stopsound":
            ActionPerformer.shared.stopAllSound()
        case "settings", "preferences":
            if let page = q["page"] {
                let item: SidebarItem? = [
                    "general": .general, "shortcuts": .shortcuts, "rules": .rules, "activity": .activity, "advanced": .advanced,
                ][page]
                SettingsWindowController.shared.show(selecting: item ?? target.map { .breakItem($0.id) })
            } else {
                SettingsWindowController.shared.show(selecting: target.map { .breakItem($0.id) })
            }
        #if DEBUG
            case "testnotify":
                // Test hook: performs a break's first notification action (only with RECESS_DATA_DIR set).
                guard ProcessInfo.processInfo.environment["RECESS_DATA_DIR"] != nil,
                    let b = target ?? model.breaks.first,
                    let a = b.actions.first(where: { $0.kind == .notification })
                        ?? Optional(BreakAction(kind: .notification, stage: .atStart))
                else { return }
                ActionPerformer.shared.perform(a, for: b)
                UNUserNotificationCenter.current().getNotificationSettings { s in
                    let line =
                        "authorization=\(s.authorizationStatus.rawValue) alert=\(s.alertSetting.rawValue) style=\(s.alertStyle.rawValue)\n"
                    try? line.write(
                        to: Store.subfolder("Snapshots").appendingPathComponent("notify-status.txt"), atomically: true, encoding: .utf8)
                }
            case "snapshot":
                // Test hook: writes PNGs of Recess's own visible windows (only with RECESS_DATA_DIR set).
                guard ProcessInfo.processInfo.environment["RECESS_DATA_DIR"] != nil else { return }
                let name = String((q["name"] ?? "snap").filter { $0.isLetter || $0.isNumber || $0 == "-" || $0 == "_" }.prefix(40))
                Self.snapshotWindows(name: name.isEmpty ? "snap" : name)
        #endif
        default:
            NSLog("Recess: unknown URL command \(s)")
        }
    }

    #if DEBUG
        static func texts(in view: NSView) -> [String] {
            var out: [String] = []
            if let tf = view as? NSTextField { out.append(tf.stringValue) }
            if let label = view.accessibilityLabel(), !label.isEmpty { out.append(label) }
            for s in view.subviews { out += texts(in: s) }
            return out
        }

        static func snapshotWindows(name: String) {
            let folder = Store.subfolder("Snapshots")
            for (i, w) in NSApp.windows.enumerated() where w.isVisible {
                guard let view = w.contentView?.superview ?? w.contentView,
                    let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds)
                else { continue }
                view.cacheDisplay(in: view.bounds, to: rep)
                let file = folder.appendingPathComponent("\(name)-\(i)-\(Int(w.frame.width))x\(Int(w.frame.height)).png")
                try? rep.representation(using: .png, properties: [:])?.write(to: file)
                var lines: [String] = []
                func walk(_ v: NSView, _ depth: Int) {
                    if let t = v as? NSTableView {
                        lines.append("table rows=\(t.numberOfRows)")
                        for r in 0..<t.numberOfRows {
                            if let cell = t.view(atColumn: 0, row: r, makeIfNecessary: false) {
                                lines.append("  row \(r): " + Self.texts(in: cell).joined(separator: " | "))
                            }
                        }
                    }
                    v.subviews.forEach { walk($0, depth + 1) }
                }
                walk(view, 0)
                try? lines.joined(separator: "\n").write(
                    to: file.deletingPathExtension().appendingPathExtension("txt"), atomically: true, encoding: .utf8)
            }
        }
    #endif

    // MARK: - Notifications

    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter, willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner, .list])
    }

    // MARK: - Sample scripts

    private func installSampleScripts() {
        let folder = Store.scriptsFolder
        let marker = folder.appendingPathComponent(".installed-v1")
        guard !FileManager.default.fileExists(atPath: marker.path) else { return }
        let samples: [String: String] = [
            "Zoom Meeting.sh": Self.udpScript("zoom"),
            "Teams Meeting.sh": Self.udpScript("teams"),
            "FaceTime Call.sh": Self.udpScript("avconferenced"),
            "Pause Music.applescript": "if application \"Music\" is running then tell application \"Music\" to pause\n",
            "Play Music.applescript": "if application \"Music\" is running then tell application \"Music\" to play\n",
            "Start Screensaver.sh": "#!/bin/sh\nopen -a ScreenSaverEngine\n",
            "Speak Time.applescript": "say \"It is \" & time string of (current date)\n",
            "Lock Screen.sh": "#!/bin/sh\npmset displaysleepnow\n",
        ]
        for (name, body) in samples {
            let url = folder.appendingPathComponent(name)
            if !FileManager.default.fileExists(atPath: url.path) {
                try? body.write(to: url, atomically: true, encoding: .utf8)
                if name.hasSuffix(".sh") { try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: url.path) }
            }
        }
        FileManager.default.createFile(atPath: marker.path, contents: nil)
    }

    /// Detects an active call by counting UDP sockets owned by the process (meeting apps open several).
    private static func udpScript(_ process: String) -> String {
        """
        #!/bin/sh
        # Prints "true" while a \(process) call appears to be active.
        count=$(/usr/sbin/lsof -nP -i 4UDP 2>/dev/null | /usr/bin/awk 'BEGIN { c = 0 } tolower($0) ~ /\(process)/ { c++ } END { print c }')
        if [ "${count:-0}" -gt 1 ]; then echo "true"; else echo "false"; fi

        """
    }
}

extension SettingsWindowController {
    var isVisible: Bool { NSApp.windows.contains { $0.title == "Recess" && $0.isVisible } }
}
