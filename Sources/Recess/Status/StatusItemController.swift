import AppKit
import Combine

/// The menu bar item: a live title plus a menu of break commands.
@MainActor
final class StatusItemController: NSObject, NSMenuDelegate {
    static let shared = StatusItemController()

    private var item: NSStatusItem?
    private let model = AppModel.shared
    private var cancellables: Set<AnyCancellable> = []
    private var quickMenu = false

    func setVisible(_ visible: Bool) {
        if visible, item == nil {
            let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
            let menu = NSMenu()
            menu.delegate = self
            item.menu = menu
            item.button?.imagePosition = .imageLeading
            self.item = item
            Ticker.shared.$now.sink { [weak self] _ in self?.refresh() }.store(in: &cancellables)
            refresh()
        } else if !visible, let item {
            NSStatusBar.system.removeStatusItem(item)
            self.item = nil
            cancellables.removeAll()
        }
    }

    #if DEBUG
        /// The current menu bar title and tooltip, for the debug snapshot hook.
        var debugStatus: String {
            "title: \(item?.button?.attributedTitle.string ?? "")\ntooltip: \(item?.button?.toolTip ?? "")"
        }
    #endif

    // MARK: - Title

    private func refresh() {
        guard let button = item?.button else { return }
        let s = model.settings
        let next = model.nextBreak
        let last = model.lastDoneBreak
        let now = model.now

        let image: NSImage?
        switch s.statusIcon {
        case .none: image = nil
        case .lastDoneLabel: image = Self.dot(last?.color.nsColor ?? .secondaryLabelColor)
        case .nextDueLabel: image = Self.dot(next?.color.nsColor ?? .secondaryLabelColor)
        default:
            image = NSImage(systemSymbolName: s.statusIcon.symbol ?? "cup.and.saucer", accessibilityDescription: "Recess")
            image?.isTemplate = true
        }
        if let pieBreak = next, pieBreak.labelAsPie, s.statusIcon == .nextDueLabel {
            button.image = Self.pie(pieBreak)
        } else {
            button.image = image
        }

        var title = ""
        if let active = model.activeBreakID.flatMap(model.breakWith(id:)), let session = Scheduler.shared.session {
            title = "\(active.name) \(Format.countdown(session.remaining))"
        } else if model.allPaused {
            title = "Paused"
        } else if let next, Scheduler.shared.waitingDescription(for: next, now: now) != nil,
            [.briefCountdown, .fullCountdown].contains(s.statusTitle)
        {
            // A due break that's waiting to start: say so rather than showing "0s".
            title = "waiting"
        } else {
            switch s.statusTitle {
            case .onlyIcon: title = ""
            case .briefSinceLast: title = last?.state.lastDone.map { Format.brief(now.timeIntervalSince($0)) } ?? ""
            case .fullSinceLast: title = last?.state.lastDone.map { Format.full(now.timeIntervalSince($0)) } ?? ""
            case .doneTimeOfLast: title = last?.state.lastDone.map(Format.time) ?? ""
            case .durationOfLast: title = last.map { Format.brief($0.state.lastDuration) } ?? ""
            case .briefCountdown: title = dueDate(next).map { Format.brief($0.timeIntervalSince(now)) } ?? ""
            case .fullCountdown: title = dueDate(next).map { Format.countdown($0.timeIntervalSince(now)) } ?? ""
            case .dueTimeOfNext: title = dueDate(next).map(Format.time) ?? ""
            case .finishTimeOfNext: title = next.flatMap { b in dueDate(b).map { Format.time($0.addingTimeInterval(b.duration)) } } ?? ""
            case .durationOfNext: title = next.map { Format.brief($0.duration) } ?? ""
            }
        }
        let font = NSFont.monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .regular)
        button.attributedTitle = NSAttributedString(
            string: title.isEmpty ? "" : (image == nil ? title : " " + title), attributes: [.font: font])
        if image == nil && title.isEmpty { button.title = "Recess" }
        button.toolTip = tooltip(next)
    }

    private func dueDate(_ b: Break?) -> Date? {
        guard let d = b?.state.nextDue, d != .distantFuture else { return nil }
        return max(d, model.now)
    }

    private func tooltip(_ next: Break?) -> String {
        guard let next, let due = dueDate(next) else { return "Recess — no breaks scheduled" }
        if let waiting = Scheduler.shared.waitingDescription(for: next, now: model.now) {
            return "\(next.name) break is \(waiting)\nOption-click for quick commands."
        }
        var s = "\(next.name) break due \(Format.relative(due, from: model.now))"
        if let last = next.state.lastDone { s += " (last done \(Format.full(model.now.timeIntervalSince(last))) ago)" }
        return s + "\nOption-click for quick commands."
    }

    static func dot(_ color: NSColor) -> NSImage {
        let img = NSImage(size: NSSize(width: 12, height: 12), flipped: false) { r in
            color.setFill()
            NSBezierPath(ovalIn: r.insetBy(dx: 1.5, dy: 1.5)).fill()
            return true
        }
        return img
    }

    /// Mini pie of postponed / skipped / done today.
    static func pie(_ b: Break) -> NSImage {
        let c = b.state.counts
        let values: [(Int, NSColor)] = [(c.todayPostpone, .systemOrange), (c.todaySkip, .systemRed), (c.todayDone, .systemGreen)]
        let total = max(1, values.reduce(0) { $0 + $1.0 })
        return NSImage(size: NSSize(width: 14, height: 14), flipped: false) { r in
            let center = NSPoint(x: r.midX, y: r.midY)
            var angle: CGFloat = 90
            if values.allSatisfy({ $0.0 == 0 }) {
                b.color.nsColor.setFill()
                NSBezierPath(ovalIn: r.insetBy(dx: 1, dy: 1)).fill()
                return true
            }
            for (v, color) in values where v > 0 {
                let sweep = CGFloat(v) / CGFloat(total) * 360
                let p = NSBezierPath()
                p.move(to: center)
                p.appendArc(withCenter: center, radius: r.width / 2 - 1, startAngle: angle, endAngle: angle - sweep, clockwise: true)
                p.close()
                color.setFill()
                p.fill()
                angle -= sweep
            }
            return true
        }
    }

    // MARK: - Menu

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        quickMenu = NSEvent.modifierFlags.contains(.option)
        let s = model.settings
        let sched = Scheduler.shared

        if let session = sched.session {
            let header = NSMenuItem(
                title: "\(session.breakItem.name) break: \(Format.countdown(session.remaining)) left", action: nil, keyEquivalent: "")
            header.isEnabled = false
            menu.addItem(header)
            menu.addItem(
                limited(
                    item("Postpone \(Format.int(session.breakItem.controls.postponeMinutes1)) Minutes", #selector(postponeCurrent1)),
                    allowed: session.canPostpone))
            menu.addItem(limited(item("Skip This Break", #selector(skipCurrent)), allowed: session.canSkip))
            menu.addItem(.separator())
        }

        if model.breaks.isEmpty {
            let none = NSMenuItem(title: "No breaks", action: nil, keyEquivalent: "")
            none.isEnabled = false
            menu.addItem(none)
        }

        for b in model.breaks {
            let title = NSMutableAttributedString(string: b.name, attributes: [.font: NSFont.menuFont(ofSize: 0)])
            title.append(
                NSAttributedString(
                    string: "  " + statusText(b),
                    attributes: [.foregroundColor: NSColor.secondaryLabelColor, .font: NSFont.menuFont(ofSize: NSFont.smallSystemFontSize)])
            )
            let mi = NSMenuItem(title: b.name, action: nil, keyEquivalent: "")
            mi.attributedTitle = title
            mi.image = b.labelAsPie ? Self.pie(b) : Self.dot(b.enabled ? b.color.nsColor : .tertiaryLabelColor)
            mi.submenu = breakSubmenu(b)
            menu.addItem(mi)
        }

        menu.addItem(.separator())
        menu.addItem(item("Start Next Break", #selector(startNext)))
        if let next = model.nextBreakAnyLength {
            menu.addItem(
                limited(
                    item("Postpone \(next.name) \(Format.int(next.controls.postponeMinutes1)) Minutes", #selector(postponeNext1)),
                    allowed: next.canPostpone))
            menu.addItem(limited(item("Skip \(next.name) Break", #selector(skipNext)), allowed: next.canSkip))
        }
        menu.addItem(.separator())
        if !s.hidePause || quickMenu {
            let pause = NSMenuItem(title: "Pause All Breaks", action: nil, keyEquivalent: "")
            pause.submenu = pauseMenu(nil)
            menu.addItem(pause)
        }
        if model.breaks.contains(where: { $0.state.pausedUntil != nil }) {
            menu.addItem(item("Resume All Breaks", #selector(resumeAll)))
        }
        if !s.hideReset || quickMenu { menu.addItem(item("Reset All Breaks", #selector(resetAll))) }
        if ActionPerformer.shared.isSoundPlaying { menu.addItem(item("Stop Sound", #selector(stopSound))) }
        menu.addItem(.separator())
        menu.addItem(item("Settings…", #selector(openSettings), key: ","))
        if !s.hideQuit || quickMenu { menu.addItem(item("Quit Recess", #selector(quit), key: "q")) }
    }

    private func statusText(_ b: Break) -> String {
        if !b.enabled { return "disabled" }
        if b.isPaused { return b.state.pausedUntil == .distantFuture ? "paused" : "paused until \(Format.time(b.state.pausedUntil!))" }
        if model.activeBreakID == b.id { return "now" }
        guard let due = b.state.nextDue else { return "" }
        if due == .distantFuture { return "no upcoming events" }
        if let waiting = Scheduler.shared.waitingDescription(for: b, now: model.now) { return waiting }
        return "due " + model.dateString(due)
    }

    private func item(_ title: String, _ action: Selector, key: String = "", represented: Any? = nil, tag: Int = 0) -> NSMenuItem {
        let mi = NSMenuItem(title: title, action: action, keyEquivalent: key)
        mi.target = self
        mi.representedObject = represented
        mi.tag = tag
        return mi
    }

    /// Disables a postpone/skip item once the break's limit is used up.
    private func limited(_ mi: NSMenuItem, allowed: Bool) -> NSMenuItem {
        if !allowed {
            mi.action = nil
            mi.title += " (limit reached)"
        }
        return mi
    }

    private func breakSubmenu(_ b: Break) -> NSMenu {
        let s = model.settings
        let m = NSMenu()
        let info = NSMenuItem(title: "Last done: \(model.dateString(b.state.lastDone))", action: nil, keyEquivalent: "")
        info.isEnabled = false
        m.addItem(info)
        let status = NSMenuItem(title: "Status: \(b.state.status)", action: nil, keyEquivalent: "")
        status.isEnabled = false
        m.addItem(status)
        m.addItem(.separator())
        m.addItem(item("Start \(b.name) Break", #selector(startBreak(_:)), represented: b.id))
        m.addItem(
            limited(
                item(
                    "Postpone \(Format.int(b.controls.postponeMinutes1)) Minutes", #selector(postponeBreak(_:)), represented: b.id, tag: 1),
                allowed: b.canPostpone))
        m.addItem(
            limited(
                item(
                    "Postpone \(Format.int(b.controls.postponeMinutes2)) Minutes", #selector(postponeBreak(_:)), represented: b.id, tag: 2),
                allowed: b.canPostpone))
        m.addItem(limited(item("Skip Break", #selector(skipBreak(_:)), represented: b.id), allowed: b.canSkip))
        if let limits = LimitText.remaining(b) {
            let info = NSMenuItem(title: limits, action: nil, keyEquivalent: "")
            info.isEnabled = false
            m.addItem(info)
        }
        if !s.hideMakeDue || quickMenu {
            let due = NSMenuItem(title: "Make Due In", action: nil, keyEquivalent: "")
            let dm = NSMenu()
            for (t, secs) in Self.makeDueChoices {
                dm.addItem(item(t, #selector(makeDue(_:)), represented: b.id, tag: secs))
            }
            due.submenu = dm
            m.addItem(due)
        }
        m.addItem(.separator())
        if b.state.pausedUntil != nil {
            m.addItem(item("Resume Break", #selector(resumeBreak(_:)), represented: b.id))
        } else if !s.hidePause || quickMenu {
            let p = NSMenuItem(title: "Pause Break", action: nil, keyEquivalent: "")
            p.submenu = pauseMenu(b.id)
            m.addItem(p)
        }
        if !s.hideReset || quickMenu { m.addItem(item("Reset Break", #selector(resetBreak(_:)), represented: b.id)) }
        if !s.hideDisable || quickMenu {
            m.addItem(item(b.enabled ? "Disable Break" : "Enable Break", #selector(toggleEnabled(_:)), represented: b.id))
        }
        m.addItem(.separator())
        m.addItem(item("Edit Break…", #selector(editBreak(_:)), represented: b.id))
        return m
    }

    static let makeDueChoices: [(String, Int)] = [
        ("30 Seconds", 30), ("1 Minute", 60), ("2 Minutes", 120), ("3 Minutes", 180), ("4 Minutes", 240), ("5 Minutes", 300),
        ("10 Minutes", 600), ("15 Minutes", 900), ("20 Minutes", 1200), ("30 Minutes", 1800), ("45 Minutes", 2700), ("1 Hour", 3600),
    ]

    static let pauseChoices: [(String, Int)] = [
        ("1 Minute", 60), ("2 Minutes", 120), ("5 Minutes", 300), ("10 Minutes", 600), ("15 Minutes", 900), ("30 Minutes", 1800),
        ("1 Hour", 3600), ("2 Hours", 7200), ("3 Hours", 10800), ("4 Hours", 14400), ("5 Hours", 18000), ("8 Hours", 28800),
        ("12 Hours", 43200), ("24 Hours", 86400), ("Indefinitely", 0),
    ]

    private func pauseMenu(_ id: UUID?) -> NSMenu {
        let m = NSMenu()
        for (t, secs) in Self.pauseChoices {
            if secs == 0 { m.addItem(.separator()) }
            m.addItem(item(t, #selector(pause(_:)), represented: id, tag: secs))
        }
        return m
    }

    // MARK: - Actions

    @objc private func startNext() { Scheduler.shared.startNextBreak() }
    @objc private func postponeNext1() { Scheduler.shared.postponeNext(slot: 1) }
    @objc private func skipNext() { Scheduler.shared.skipNext() }
    @objc private func postponeCurrent1() { Scheduler.shared.session?.postpone(1) }
    @objc private func skipCurrent() { Scheduler.shared.session?.skip() }
    @objc private func resumeAll() { Scheduler.shared.resumeAll() }
    @objc private func resetAll() { Scheduler.shared.resetAll() }
    @objc private func stopSound() { ActionPerformer.shared.stopAllSound() }
    @objc private func openSettings() { SettingsWindowController.shared.show() }
    @objc private func quit() { NSApp.terminate(nil) }

    @objc private func startBreak(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID else { return }
        Scheduler.shared.startBreak(id, manual: true)
    }
    @objc private func postponeBreak(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID, let b = model.breakWith(id: id) else { return }
        Scheduler.shared.postpone(id, minutes: sender.tag == 1 ? b.controls.postponeMinutes1 : b.controls.postponeMinutes2)
    }
    @objc private func skipBreak(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID else { return }
        Scheduler.shared.skip(id)
    }
    @objc private func makeDue(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID else { return }
        Scheduler.shared.makeDue(id, in: TimeInterval(sender.tag))
    }
    @objc private func pause(_ sender: NSMenuItem) {
        let secs: TimeInterval? = sender.tag == 0 ? nil : TimeInterval(sender.tag)
        if let id = sender.representedObject as? UUID {
            Scheduler.shared.pause(id, seconds: secs)
        } else {
            Scheduler.shared.pauseAll(seconds: secs)
        }
    }
    @objc private func resumeBreak(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID else { return }
        Scheduler.shared.resume(id)
    }
    @objc private func resetBreak(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID else { return }
        Scheduler.shared.resetBreak(id)
    }
    @objc private func toggleEnabled(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID, let b = model.breakWith(id: id) else { return }
        Scheduler.shared.setEnabled(id, !b.enabled)
    }
    @objc private func editBreak(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID else { return }
        SettingsWindowController.shared.show(selecting: .breakItem(id))
    }
}
