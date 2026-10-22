import AppKit

/// Decides when breaks happen. Each one-second cycle handles idle detection, time away,
/// sleep, rules, priorities and availability, then starts any break that is due.
@MainActor
final class Scheduler {
    static let shared = Scheduler()

    private let model = AppModel.shared
    private let env = EnvironmentMonitor.shared
    private var timer: Timer?
    private var lastCycle = Date()

    // Natural break tracking
    private var idleStart: Date?
    private var naturalBreakIDs: Set<UUID> = []

    // Due handling
    private var dueSince: [UUID: Date] = [:]
    private var askingBreakID: UUID?
    /// Remembers which "before due" actions have fired, and for which due date.
    private var firedBeforeActions: [UUID: Date] = [:]
    /// When sleep was last handled, so a wake isn't counted twice.
    private var lastSleepHandled = Date.distantPast

    private(set) var session: BreakSession?
    /// Idle time accumulated during a break isn't a natural break.
    private var lastSessionEnd: Date?

    private init() {}

    func start() {
        for i in model.breaks.indices
        where model.breaks[i].state.nextDue == nil || model.breaks[i].state.nextDue! < Date().addingTimeInterval(-12 * 3600) {
            reschedule(&model.breaks[i], from: Date(), reason: .reset)
        }
        restoreAfterLaunch()
        env.configureEventMonitor(enabled: model.settings.idleDetector == .eventMonitor)
        // Common modes keep the cycle running while menus are open and during modal panels.
        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.cycle() }
        }
        timer.tolerance = 0.1
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
        cycle()
    }

    /// Breaks overdue from before launch get a fresh start instead of firing immediately.
    private func restoreAfterLaunch() {
        let now = Date()
        for i in model.breaks.indices {
            guard let due = model.breaks[i].state.nextDue, due < now, model.breaks[i].schedule.adjustment != .calendar else { continue }
            reschedule(&model.breaks[i], from: now, reason: .reset)
            model.breaks[i].state.status = "restarted after Recess was closed"
        }
    }

    // MARK: - Cycle

    func cycle() {
        let now = Date()
        var dt = now.timeIntervalSince(lastCycle)
        lastCycle = now
        Ticker.shared.now = now

        // Only write back when something changed: every write re-renders the UI and queues a save.
        for i in model.breaks.indices {
            var counts = model.breaks[i].state.counts
            counts.rollIfNeeded(now: now)
            if counts != model.breaks[i].state.counts { model.breaks[i].state.counts = counts }
        }
        resumeExpiredPauses(now: now)

        // Time asleep counts as time away. The wake notification is the main signal; a long gap
        // between cycles counts only if the system idle timer agrees, so a busy main thread
        // isn't mistaken for sleep. Each wake is handled once.
        var sleepInterval: TimeInterval = 0
        if let pending = env.pendingSleepInterval {
            env.pendingSleepInterval = nil
            if now.timeIntervalSince(lastSleepHandled) > 120 { sleepInterval = pending }
        } else if dt > 30 && EnvironmentMonitor.hidIdleSeconds() >= dt - 5 {
            sleepInterval = dt
        }
        if dt > 30 { dt = 1 }
        if sleepInterval > 0 {
            lastSleepHandled = now
            handleSleep(sleepInterval, now: now)
        }

        if let session {
            session.tick(now: now)
            ActivityRecorder.shared.tick(now: now, idle: true)
            return
        }

        let settings = model.settings
        let snapshot = env.snapshot()
        var idle = env.idleSeconds(detector: settings.idleDetector)
        if let end = lastSessionEnd { idle = min(idle, now.timeIntervalSince(end)) }
        if RulesEngine.shared.ignoreIdle(rules: settings.rules, env: snapshot) != nil {
            idle = 0
        } else if RulesEngine.shared.treatAsIdle(rules: settings.rules, env: snapshot) != nil {
            idle = max(idle, now.timeIntervalSince(idleStart ?? now) + settings.naturalBreakThreshold)
        }
        if Int(idle) != Int(Ticker.shared.idleSeconds) { Ticker.shared.idleSeconds = idle }

        let officiallyIdle = idle >= max(5, settings.naturalBreakThreshold)
        refreshCalendarBreaks(now: now)
        handleNaturalBreak(officiallyIdle: officiallyIdle, idle: idle, dt: dt, now: now)
        ActivityRecorder.shared.tick(now: now, idle: officiallyIdle)

        // No point warning someone who isn't there.
        if !officiallyIdle { performBeforeDueActions(now: now) }
        autoSkipForHigherPriority(now: now)

        if !officiallyIdle {
            startDueBreak(idle: idle, now: now, env: snapshot)
        }
    }

    private func resumeExpiredPauses(now: Date) {
        for i in model.breaks.indices {
            if let until = model.breaks[i].state.pausedUntil, until <= now {
                model.breaks[i].state.pausedUntil = nil
                resetBreak(model.breaks[i].id, reason: "resumed")
            }
        }
    }

    private func handleSleep(_ interval: TimeInterval, now: Date) {
        for i in model.breaks.indices where model.breaks[i].isActive && model.breaks[i].schedule.adjustment != .calendar {
            let b = model.breaks[i]
            let threshold = b.schedule.resetWhenIdleInterval?.timeInterval ?? b.duration
            if b.schedule.resetWhenIdle && interval >= threshold {
                reschedule(&model.breaks[i], from: now, reason: .reset)
                model.breaks[i].state.status = "rested while the Mac slept"
                model.breaks[i].state.counts.breakPostpone = 0
                model.breaks[i].state.counts.breakSkip = 0
                clearDueState(b.id)
            } else if b.schedule.idleHandling == .pauseCountdown, let due = b.state.nextDue {
                model.breaks[i].state.nextDue = due.addingTimeInterval(interval)
            }
        }
    }

    private func handleNaturalBreak(officiallyIdle: Bool, idle: TimeInterval, dt: TimeInterval, now: Date) {
        if officiallyIdle {
            if idleStart == nil { idleStart = now.addingTimeInterval(-idle) }
            if !Ticker.shared.isNaturalBreak { Ticker.shared.isNaturalBreak = true }
            let idleLength = now.timeIntervalSince(idleStart!)
            for i in model.breaks.indices where model.breaks[i].isActive && model.breaks[i].schedule.adjustment != .calendar {
                var b = model.breaks[i]
                guard var due = b.state.nextDue else { continue }
                switch b.schedule.idleHandling {
                case .continueCountdown:
                    b.state.status = "away; still counting down"
                case .pauseCountdown:
                    due = due.addingTimeInterval(dt)
                    b.state.status = "away; clock stopped"
                case .countBackwards:
                    // Winding back never brings a break forward.
                    due = max(due, min(due.addingTimeInterval(2 * dt), now.addingTimeInterval(b.frequency)))
                    b.state.status = "away; winding the clock back"
                }
                let threshold = b.schedule.resetWhenIdleInterval?.timeInterval ?? b.duration
                if b.schedule.resetWhenIdle && idleLength >= threshold {
                    naturalBreakIDs.insert(b.id)
                    due = max(due, now.addingTimeInterval(b.frequency))
                    b.state.status = "rested while away"
                }
                // Never let a break fire while the user is away.
                if due < now { due = now }
                b.state.nextDue = due
                if b != model.breaks[i] { model.breaks[i] = b }
            }
        } else if let start = idleStart {
            // Back from a natural break.
            idleStart = nil
            Ticker.shared.isNaturalBreak = false
            let length = now.timeIntervalSince(start)
            for id in naturalBreakIDs {
                guard let i = model.index(of: id) else { continue }
                reschedule(&model.breaks[i], from: now, reason: .reset)
                model.breaks[i].state.lastDone = now
                model.breaks[i].state.lastDuration = length
                model.breaks[i].state.counts.breakPostpone = 0
                model.breaks[i].state.counts.breakSkip = 0
                model.breaks[i].state.status = "rested while away"
                clearDueState(id)
                ActivityRecorder.shared.log(model.breaks[i], .naturalBreak, duration: length)
            }
            naturalBreakIDs.removeAll()
        }
    }

    private func performBeforeDueActions(now: Date) {
        for b in model.breaks where b.isActive {
            guard let due = b.state.nextDue, due > now else { continue }
            for a in b.actions where a.enabled && a.stage == .beforeStart {
                // Small shifts of the due time (e.g. a stopped clock) don't count as a new break.
                let alreadyFired = firedBeforeActions[a.id].map { abs($0.timeIntervalSince(due)) < 30 } ?? false
                if now >= due.addingTimeInterval(-a.offset) && !alreadyFired {
                    firedBeforeActions[a.id] = due
                    ActionPerformer.shared.perform(a, for: b)
                }
            }
        }
    }

    /// Skips a break when a more important one is due soon.
    private func autoSkipForHigherPriority(now: Date) {
        let breaks = model.breaks
        for (index, b) in breaks.enumerated() where b.isActive && b.schedule.skipForHigherPriority {
            guard let due = b.state.nextDue, due <= now.addingTimeInterval(1) else { continue }
            let window = b.schedule.skipForHigherPriorityInterval.timeInterval
            let higher = breaks[..<index].first { h in
                guard h.isActive, let hDue = h.state.nextDue else { return false }
                return hDue.timeIntervalSince(now) <= window
            }
            if let higher {
                model.update(b.id) {
                    reschedule(&$0, from: now, reason: .skip)
                    $0.state.status = "skipped for higher priority (\(higher.name))"
                }
                clearDueState(b.id)
            }
        }
    }

    private func startDueBreak(idle: TimeInterval, now: Date, env snapshot: EnvironmentSnapshot) {
        let due = model.breaks.filter { $0.isActive && ($0.state.nextDue ?? .distantFuture) <= now }
        // Forget "due since" times for breaks that are no longer due.
        dueSince = dueSince.filter { id, _ in due.contains { $0.id == id } }
        guard let b = due.first else { return }

        // A calendar break whose event is already over (e.g. the Mac was asleep) moves on.
        if b.schedule.adjustment == .calendar, let end = b.state.nextEventEnd, end <= now.addingTimeInterval(5) {
            model.update(b.id) {
                reschedule(&$0, from: now, reason: .skip)
                $0.state.status = "missed a calendar break that has ended"
            }
            return
        }

        if let asking = askingBreakID {
            if AskPanelController.shared.isShowing(asking) { return }
            askingBreakID = nil
        }

        if let rule = RulesEngine.shared.dueRule(for: b, rules: model.settings.rules, env: snapshot) {
            if let minutes = rule.action.postponeMinutes {
                model.update(b.id) {
                    $0.state.nextDue = now.addingTimeInterval(minutes * 60)
                    $0.state.status = "postponed \(Format.int(minutes)) minutes by rule"
                }
            } else {
                model.update(b.id) {
                    reschedule(&$0, from: now, reason: .skip)
                    $0.state.status = "skipped by rule"
                }
                ActivityRecorder.shared.log(b, .skipped, detail: "rule")
            }
            dueSince[b.id] = nil
            return
        }

        if env.isMouseButtonDown {
            model.update(b.id) { $0.state.status = "waiting while a mouse button is pressed" }
            return
        }

        let since = dueSince[b.id] ?? now
        dueSince[b.id] = since

        switch b.schedule.dueBehavior {
        case .startImmediately:
            startBreak(b.id, manual: false)
        case .waitForNaturalBreak:
            if idle >= Self.pauseLength || now.timeIntervalSince(since) >= Self.longestWait {
                startBreak(b.id, manual: false)
            } else {
                model.update(b.id) { $0.state.status = "waiting for a pause in your work" }
            }
        case .ask:
            askingBreakID = b.id
            model.update(b.id) { $0.state.status = "asking when to start" }
            AskPanelController.shared.show(for: b) { [weak self] choice in
                self?.askingBreakID = nil
                self?.dueSince[b.id] = nil
                switch choice {
                case .start: self?.startBreak(b.id, manual: false)
                case .postpone(let m): self?.postpone(b.id, minutes: m)
                case .skip: self?.skip(b.id)
                }
            }
        }
    }

    /// "Wait for a pause in my work": start after this many seconds without input…
    nonisolated static let pauseLength: TimeInterval = 15
    /// …or after this long, whichever comes first.
    nonisolated static let longestWait: TimeInterval = 60

    /// What a due break is waiting for before it starts, or nil if it isn't waiting.
    /// Shown in the menu bar and Settings so a late break doesn't look stuck.
    func waitingDescription(for b: Break, now: Date = Date()) -> String? {
        guard b.isActive, session?.breakItem.id != b.id, let due = b.state.nextDue, due <= now else { return nil }
        if askingBreakID == b.id { return "waiting for your answer" }
        if b.schedule.dueBehavior == .waitForNaturalBreak, let since = dueSince[b.id] {
            let left = max(1, Self.longestWait - now.timeIntervalSince(since))
            return "waiting for a pause in your work (starts within \(Format.brief(left)))"
        }
        return b.state.status
    }

    // MARK: - Commands

    func startBreak(_ id: UUID, manual: Bool, preview: Bool = false) {
        guard session == nil, let b = model.breakWith(id: id) else { return }
        AskPanelController.shared.dismiss()
        askingBreakID = nil
        dueSince[id] = nil
        if !preview {
            model.update(id) {
                $0.state.lastStarted = Date()
                $0.state.counts.todayStart += 1
                $0.state.counts.totalStart += 1
                $0.state.status = "having the break"
            }
            ActivityRecorder.shared.log(b, .started)
        }
        model.activeBreakID = id
        let s = BreakSession(breakItem: model.breakWith(id: id) ?? b, isPreview: preview, settings: model.settings) { [weak self] result in
            self?.sessionEnded(id: id, result: result, preview: preview)
        }
        session = s
        s.begin()
    }

    private func sessionEnded(id: UUID, result: BreakSession.Result, preview: Bool) {
        session = nil
        ActionPerformer.shared.stopLoopingSounds()
        model.activeBreakID = nil
        lastCycle = Date()
        lastSessionEnd = Date()
        idleStart = nil
        naturalBreakIDs.removeAll()
        guard !preview, let b = model.breakWith(id: id) else { return }
        let now = Date()
        switch result {
        case .done(let elapsed), .endedEarly(let elapsed):
            let early: Bool = { if case .endedEarly = result { return true } else { return false } }()
            model.update(id) {
                $0.state.lastDone = now
                $0.state.lastDuration = elapsed
                $0.state.counts.todayDone += 1
                $0.state.counts.totalDone += 1
                $0.state.counts.todayDuration += elapsed
                $0.state.counts.totalDuration += elapsed
                $0.state.counts.breakPostpone = 0
                $0.state.counts.breakSkip = 0
                reschedule(&$0, from: now, reason: .done)
                $0.state.status = early ? "ended early" : "completed"
            }
            ActivityRecorder.shared.log(b, early ? .endedEarly : .done, duration: elapsed)
            resetLowerPriorityAfterFinishing(id)
        case .postponed(let minutes):
            postpone(id, minutes: minutes, fromBreak: true)
        case .skipped:
            skip(id, fromBreak: true)
        }
    }

    /// Lower-priority breaks restart their timers after a more important break finishes.
    private func resetLowerPriorityAfterFinishing(_ id: UUID) {
        guard let index = model.index(of: id), let finished = model.breakWith(id: id) else { return }
        for i in model.breaks.indices where i > index {
            let b = model.breaks[i]
            guard b.isActive, b.schedule.resetWhenFinishingHigherPriority, b.duration <= finished.duration else { continue }
            reschedule(&model.breaks[i], from: Date(), reason: .reset)
            model.breaks[i].state.status = "reset for higher priority (\(finished.name))"
            clearDueState(b.id)
        }
    }

    func startNextBreak() {
        if let b = model.nextBreakAnyLength { startBreak(b.id, manual: true) }
    }

    func postpone(_ id: UUID, minutes: Double, fromBreak: Bool = false) {
        guard let b = model.breakWith(id: id) else { return }
        if !fromBreak, session?.breakItem.id == id { session?.postpone(minutes: minutes); return }
        guard fromBreak || b.canPostpone else { limitReached(b, "postpone"); return }
        AskPanelController.shared.dismiss(ifShowing: id)
        model.update(id) {
            $0.state.nextDue = Date().addingTimeInterval(minutes * 60)
            $0.state.lastDeferred = Date()
            $0.state.counts.breakPostpone += 1
            $0.state.counts.hourPostpone += 1
            $0.state.counts.todayPostpone += 1
            $0.state.counts.totalPostpone += 1
            $0.state.status = "postponed \(Format.int(minutes)) minute\(minutes == 1 ? "" : "s")"
        }
        dueSince[id] = nil
        ActivityRecorder.shared.log(b, .postponed, detail: "\(Format.int(minutes)) min")
        performActions(b, stage: .afterPostponeOrSkip)
    }

    func postponeNext(slot: Int) {
        if let s = session {
            s.postpone(slot)
            return
        }
        guard let b = model.nextBreakAnyLength else { return }
        postpone(b.id, minutes: slot == 1 ? b.controls.postponeMinutes1 : b.controls.postponeMinutes2)
    }

    /// Postpones the break in progress, or else the next one, by a number of minutes.
    func postponeCurrentOrNext(minutes: Double) {
        if let s = session { s.postpone(minutes: minutes); return }
        if let b = model.nextBreakAnyLength { postpone(b.id, minutes: minutes) }
    }

    func skip(_ id: UUID, fromBreak: Bool = false) {
        guard let b = model.breakWith(id: id) else { return }
        if !fromBreak, session?.breakItem.id == id { session?.skip(); return }
        guard fromBreak || b.canSkip else { limitReached(b, "skip"); return }
        AskPanelController.shared.dismiss(ifShowing: id)
        model.update(id) {
            reschedule(&$0, from: Date(), reason: .skip)
            $0.state.lastDeferred = Date()
            $0.state.counts.breakSkip += 1
            $0.state.counts.hourSkip += 1
            $0.state.counts.todaySkip += 1
            $0.state.counts.totalSkip += 1
            $0.state.status = "skipped"
        }
        dueSince[id] = nil
        ActivityRecorder.shared.log(b, .skipped)
        performActions(b, stage: .afterPostponeOrSkip)
    }

    func skipNext() {
        if let s = session { s.skip(); return }
        if let b = model.nextBreakAnyLength { skip(b.id) }
    }

    /// Forgets waiting/asking state for a break whose due time just changed.
    private func clearDueState(_ id: UUID) {
        dueSince[id] = nil
        if askingBreakID == id {
            AskPanelController.shared.dismiss(ifShowing: id)
            askingBreakID = nil
        }
    }

    /// Feedback when a postpone/skip limit blocks a request from the menu, a shortcut or a URL.
    func limitReached(_ b: Break, _ what: String) {
        NSSound.beep()
        model.update(b.id) { $0.state.status = "\(what) limit reached" }
    }

    func makeDue(_ id: UUID, in seconds: TimeInterval) {
        model.update(id) {
            $0.state.nextDue = Date().addingTimeInterval(seconds)
            $0.state.status = "made due"
        }
        clearDueState(id)
    }

    func resetBreak(_ id: UUID, reason: String = "reset") {
        model.update(id) {
            reschedule(&$0, from: Date(), reason: .reset)
            $0.state.counts.breakPostpone = 0
            $0.state.counts.breakSkip = 0
            $0.state.status = reason
        }
        clearDueState(id)
    }

    func resetAll() {
        for b in model.breaks { resetBreak(b.id) }
    }

    /// `nil` seconds = indefinitely.
    func pause(_ id: UUID, seconds: TimeInterval?) {
        model.update(id) {
            $0.state.pausedUntil = seconds.map { Date().addingTimeInterval($0) } ?? .distantFuture
            $0.state.status = seconds == nil ? "paused until resumed" : "paused until \(Format.time(Date().addingTimeInterval(seconds!)))"
        }
        clearDueState(id)
    }

    func pauseAll(seconds: TimeInterval?) {
        for b in model.breaks { pause(b.id, seconds: seconds) }
    }

    func resume(_ id: UUID) {
        model.update(id) { $0.state.pausedUntil = nil }
        resetBreak(id, reason: "resumed")
    }

    func resumeAll() {
        for b in model.breaks where b.state.pausedUntil != nil { resume(b.id) }
    }

    func setEnabled(_ id: UUID, _ enabled: Bool) {
        model.update(id) { $0.enabled = enabled }
        if enabled { resetBreak(id) }
    }

    func addBreak() -> UUID {
        var b = Break.makeNormal()
        b.name = "Break \(model.breaks.count + 1)"
        b.actions = []
        reschedule(&b, from: Date(), reason: .reset)
        model.breaks.append(b)
        return b.id
    }

    func duplicateBreak(_ id: UUID) -> UUID? {
        guard let i = model.index(of: id) else { return nil }
        var copy = model.breaks[i].duplicate()
        reschedule(&copy, from: Date(), reason: .reset)
        model.breaks.insert(copy, at: i + 1)
        return copy.id
    }

    func deleteBreak(_ id: UUID) {
        if session?.breakItem.id == id { session?.finish(.skipped) }
        model.breaks.removeAll { $0.id == id }
    }

    /// Re-times a break after its schedule was edited. Postpone/skip counters are left alone,
    /// so editing settings can't be used to get around the limits.
    func scheduleChanged(_ id: UUID) {
        model.update(id) {
            reschedule(&$0, from: Date(), reason: .reset)
            $0.state.status = "schedule edited"
        }
        clearDueState(id)
    }

    func performActions(_ b: Break, stage: ActionStage) {
        for a in b.actions where a.enabled && a.stage == stage {
            ActionPerformer.shared.perform(a, for: b)
        }
    }

    // MARK: - Due date calculation

    typealias RescheduleReason = DueDates.Reason

    func reschedule(_ b: inout Break, from now: Date, reason: RescheduleReason) {
        if b.schedule.adjustment == .calendar {
            let s = b.schedule
            let result = CalendarManager.shared.nextEvent(
                calendarID: s.calendarIdentifier, filter: s.calendarEventFilter, after: now.addingTimeInterval(1))
            b.state.nextEventStart = result?.start
            b.state.nextEventEnd = result?.end
            b.state.nextDue = result?.start ?? .distantFuture
        } else {
            b.state.nextDue = DueDates.next(for: b, from: now, reason: reason)
        }
    }

    private var lastCalendarRefresh = Date.distantPast

    /// Picks up new or moved calendar events every few minutes.
    private func refreshCalendarBreaks(now: Date) {
        guard now.timeIntervalSince(lastCalendarRefresh) > 120 else { return }
        lastCalendarRefresh = now
        for i in model.breaks.indices where model.breaks[i].schedule.adjustment == .calendar && model.breaks[i].isActive {
            let due = model.breaks[i].state.nextDue ?? .distantFuture
            if due > now.addingTimeInterval(5) { reschedule(&model.breaks[i], from: now, reason: .reset) }
        }
    }
}
