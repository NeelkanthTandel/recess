import AVFoundation
import AppKit
import UserNotifications

/// Performs break actions: notifications, sounds, speech, screen flashes and scripts.
@MainActor
final class ActionPerformer: NSObject {
    static let shared = ActionPerformer()

    private var sounds: [NSSound] = []
    private let synthesizer = AVSpeechSynthesizer()
    private var flashWindows: [NSWindow] = []

    var isSoundPlaying: Bool { sounds.contains { $0.isPlaying } || synthesizer.isSpeaking }

    func perform(_ action: BreakAction, for b: Break) {
        guard action.enabled else { return }
        switch action.kind {
        case .notification: notify(action, b)
        case .sound: play(action)
        case .fadeSound: fadeOutSounds(over: action.fadeSeconds)
        case .speech: speak(action, b)
        case .flashScreen: flash()
        case .script: runScript(action, b)
        }
    }

    static func expand(_ text: String, _ b: Break) -> String {
        text.replacingOccurrences(of: "{break}", with: b.name)
            .replacingOccurrences(of: "{duration}", with: Format.full(b.duration))
            .replacingOccurrences(of: "{time}", with: Format.time(Date()))
    }

    // MARK: Notification

    func requestNotificationPermission(_ completion: (@MainActor (Bool) -> Void)? = nil) {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { granted, error in
            if let error { NSLog("Recess: notification authorization: \(error)") }
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    NotificationAccess.shared.refresh()
                    completion?(granted)
                }
            }
        }
    }

    private func notify(_ a: BreakAction, _ b: Break) {
        let title = Self.expand(a.title.isEmpty ? b.name : a.title, b)
        let body = Self.expand(a.message, b)
        if !a.soundName.isEmpty { playNamed(a.soundName, volume: a.volume, loops: false) }
        let color = b.color.nsColor
        // Post a system notification when allowed; otherwise show Recess's own banner so the
        // reminder is never silently lost.
        UNUserNotificationCenter.current().getNotificationSettings { settings in
            let status = settings.authorizationStatus
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    switch status {
                    case .authorized, .provisional:
                        self.postSystemNotification(title: title, body: body, color: color)
                    case .notDetermined:
                        self.requestNotificationPermission { granted in
                            if granted {
                                self.postSystemNotification(title: title, body: body, color: color)
                            } else {
                                BannerController.shared.show(title: title, body: body, color: color)
                            }
                        }
                    default:
                        BannerController.shared.show(title: title, body: body, color: color)
                    }
                }
            }
        }
    }

    private func postSystemNotification(title: String, body: String, color: NSColor) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        let request = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request) { error in
            guard let error else { return }
            NSLog("Recess: unable to display notification: \(error)")
            DispatchQueue.main.async {
                MainActor.assumeIsolated { BannerController.shared.show(title: title, body: body, color: color) }
            }
        }
    }

    // MARK: Sound

    /// Sound files available: system sounds, the user's ~/Library/Sounds, and Recess's Sounds folder.
    static func soundLibrary() -> [(name: String, url: URL)] {
        var result: [(String, URL)] = []
        let folders = [
            URL(fileURLWithPath: "/System/Library/Sounds"),
            FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Sounds"),
            Store.subfolder("Sounds"),
        ]
        for folder in folders {
            let files = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
            for f in files where ["aiff", "aif", "mp3", "wav", "m4a", "caf"].contains(f.pathExtension.lowercased()) {
                let name = f.deletingPathExtension().lastPathComponent
                if !result.contains(where: { $0.0 == name }) { result.append((name, f)) }
            }
        }
        return result.sorted { $0.0 < $1.0 }
    }

    private func soundURL(named name: String) -> URL? {
        Self.soundLibrary().first { $0.name == name }?.url
    }

    private func play(_ a: BreakAction) {
        if !a.soundPath.isEmpty, FileManager.default.fileExists(atPath: a.soundPath) {
            playURL(URL(fileURLWithPath: a.soundPath), volume: a.volume, loops: a.loops)
        } else {
            playNamed(a.soundName, volume: a.volume, loops: a.loops)
        }
    }

    private func playNamed(_ name: String, volume: Double, loops: Bool) {
        if let url = soundURL(named: name) {
            playURL(url, volume: volume, loops: loops)
        } else if let s = NSSound(named: NSSound.Name(name)) {
            s.volume = Float(volume)
            s.loops = loops
            s.play()
            sounds.append(s)
        }
    }

    private func playURL(_ url: URL, volume: Double, loops: Bool) {
        guard let s = NSSound(contentsOf: url, byReference: true) else { return }
        s.volume = Float(volume)
        s.loops = loops
        s.play()
        sounds.removeAll { !$0.isPlaying }
        sounds.append(s)
    }

    func preview(_ a: BreakAction, for b: Break) {
        perform(a, for: b)
    }

    func fadeOutSounds(over seconds: Double) {
        let playing = sounds.filter(\.isPlaying)
        guard !playing.isEmpty else { return }
        let steps = max(1, Format.int(seconds * 10))
        let start = playing.map(\.volume)
        var step = 0
        Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { timer in
            MainActor.assumeIsolated {
                step += 1
                let f = Float(max(0, 1 - Double(step) / Double(steps)))
                for (s, v) in zip(playing, start) { s.volume = v * f }
                if step >= steps {
                    timer.invalidate()
                    playing.forEach { $0.stop() }
                }
            }
        }
    }

    /// "Loop until the break ends": stops looping sounds once a break is over.
    func stopLoopingSounds() {
        for s in sounds where s.loops { s.stop() }
        sounds.removeAll { !$0.isPlaying }
    }

    func stopAllSound() {
        sounds.forEach { $0.stop() }
        sounds.removeAll()
        synthesizer.stopSpeaking(at: .immediate)
    }

    // MARK: Speech

    private func speak(_ a: BreakAction, _ b: Break) {
        let u = AVSpeechUtterance(string: Self.expand(a.message, b))
        if !a.voiceIdentifier.isEmpty, let v = AVSpeechSynthesisVoice(identifier: a.voiceIdentifier) { u.voice = v }
        u.rate = Float(
            AVSpeechUtteranceMinimumSpeechRate + (AVSpeechUtteranceMaximumSpeechRate - AVSpeechUtteranceMinimumSpeechRate) * Float(a.rate))
        u.pitchMultiplier = Float(a.pitch)
        u.volume = Float(a.volume)
        synthesizer.speak(u)
    }

    // MARK: Flash

    private func flash() {
        flashWindows.forEach { $0.orderOut(nil) }
        flashWindows = NSScreen.screens.map { screen in
            let w = NSWindow(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
            w.level = .screenSaver
            w.backgroundColor = .white
            w.isOpaque = false
            w.alphaValue = 0.85
            w.ignoresMouseEvents = true
            w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
            w.isReleasedWhenClosed = false
            w.orderFrontRegardless()
            return w
        }
        let windows = flashWindows
        NSAnimationContext.runAnimationGroup(
            { ctx in
                ctx.duration = 0.6
                windows.forEach { $0.animator().alphaValue = 0 }
            },
            completionHandler: {
                MainActor.assumeIsolated { windows.forEach { $0.orderOut(nil) } }
            })
    }

    // MARK: Script

    private func runScript(_ a: BreakAction, _ b: Break) {
        guard !a.scriptFilename.isEmpty else { return }
        ScriptRunner.run(
            a.scriptFilename,
            environment: [
                "RECESS_BREAK_NAME": b.name,
                "RECESS_BREAK_ID": b.id.uuidString,
                "RECESS_BREAK_DURATION": String(Int(b.duration)),
                "RECESS_STAGE": a.stage.rawValue,
            ])
    }
}
