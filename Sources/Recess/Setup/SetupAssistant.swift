import AppKit
import SwiftUI

@MainActor
final class SetupAssistantController: NSObject, NSWindowDelegate {
    static let shared = SetupAssistantController()
    private var window: NSWindow?

    func show() {
        if window == nil {
            let w = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 560, height: 460), styleMask: [.titled, .closable, .fullSizeContentView],
                backing: .buffered, defer: false)
            w.title = "Welcome to Recess"
            w.titlebarAppearsTransparent = true
            w.isReleasedWhenClosed = false
            w.delegate = self
            w.contentViewController = NSHostingController(
                rootView: SetupView { [weak self] in self?.window?.close() }.environmentObject(AppModel.shared))
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func windowWillClose(_ notification: Notification) {
        AppModel.shared.settings.hasCompletedSetup = true
        window = nil
    }
}

private struct SetupView: View {
    @EnvironmentObject var model: AppModel
    let done: () -> Void
    @State private var step = 0
    @State private var includeNormal = true
    @State private var normalDuration = Interval.minutes(10)
    @State private var normalFrequency = Interval.minutes(60)
    @State private var includeMicro = true
    @State private var microDuration = Interval.seconds(15)
    @State private var microFrequency = Interval.minutes(15)
    @State private var launchAtLogin = true

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            switch step {
            case 0: welcome
            case 1: breaks
            default: finish
            }
            Spacer()
            HStack {
                if step > 0 { Button("Back") { step -= 1 } }
                Spacer()
                Button(step == 2 ? "Done" : "Continue") {
                    if step == 2 { apply(); done() } else { step += 1 }
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(28)
        .frame(width: 560, height: 460)
        .onAppear {
            if let n = model.breaks.first(where: { $0.name == "Normal" }) {
                normalDuration = n.schedule.duration; normalFrequency = n.schedule.frequency
            }
            if let m = model.breaks.first(where: { $0.name == "Micro" }) {
                microDuration = m.schedule.duration; microFrequency = m.schedule.frequency
            }
        }
    }

    private var welcome: some View {
        VStack(alignment: .leading, spacing: 14) {
            Image(systemName: "cup.and.saucer.fill").font(.system(size: 44)).foregroundStyle(Color.accentColor)
            Text("Welcome to Recess").font(.largeTitle.weight(.semibold))
            Text(
                "Recess reminds you to take regular breaks from the computer. It gently dims the screen when a break is due; you can postpone or skip if you're in the middle of something."
            )
            Text(
                "There are two kinds of breaks to start with: a **Normal** break for a few minutes every hour or so, and very brief, frequent **Micro** breaks to relax your eyes and muscles. You can add as many breaks as you like later."
            )
            Text("If you step away from the computer, Recess notices the natural break and resets the timers.").foregroundStyle(.secondary)
        }
    }

    private var breaks: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Choose your breaks").font(.title.weight(.semibold))
            GroupBox {
                VStack(alignment: .leading) {
                    Toggle("Normal break", isOn: $includeNormal).font(.headline)
                    HStack {
                        Text("Break for"); IntervalField(interval: $normalDuration, minimum: 1); Text("every");
                        IntervalField(interval: $normalFrequency, units: [.minutes, .hours], minimum: 1)
                    }
                    .disabled(!includeNormal)
                }.padding(6)
            }
            GroupBox {
                VStack(alignment: .leading) {
                    Toggle("Micro break", isOn: $includeMicro).font(.headline)
                    HStack {
                        Text("Break for"); IntervalField(interval: $microDuration, minimum: 1); Text("every");
                        IntervalField(interval: $microFrequency, units: [.minutes, .hours], minimum: 1)
                    }
                    .disabled(!includeMicro)
                }.padding(6)
            }
        }
    }

    private var finish: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Almost done").font(.title.weight(.semibold))
            Toggle("Start Recess automatically when you log in", isOn: $launchAtLogin)
            Text(
                "Recess lives in the menu bar. Click it to see when breaks are due, start one now, or pause them. Option-click for extra commands."
            )
            Text("You'll be asked to allow notifications so Recess can warn you a minute before a break.").foregroundStyle(.secondary)
            Button("Preview a break") {
                // The shortest break makes the quickest preview; it isn't counted.
                if let id = model.breaks.min(by: { $0.duration < $1.duration })?.id {
                    Scheduler.shared.startBreak(id, manual: true, preview: true)
                }
            }
        }
    }

    private func apply() {
        func upsert(name: String, include: Bool, duration: Interval, frequency: Interval, make: () -> Break) {
            if let i = model.breaks.firstIndex(where: { $0.name == name }) {
                model.breaks[i].enabled = include
                model.breaks[i].schedule.duration = duration
                model.breaks[i].schedule.frequency = frequency
                Scheduler.shared.resetBreak(model.breaks[i].id)
            } else if include {
                var b = make()
                b.schedule.duration = duration
                b.schedule.frequency = frequency
                Scheduler.shared.reschedule(&b, from: Date(), reason: .reset)
                model.breaks.append(b)
            }
        }
        upsert(name: "Normal", include: includeNormal, duration: normalDuration, frequency: normalFrequency, make: Break.makeNormal)
        upsert(name: "Micro", include: includeMicro, duration: microDuration, frequency: microFrequency, make: Break.makeMicro)
        if launchAtLogin != LaunchAtLogin.isEnabled {
            _ = LaunchAtLogin.set(launchAtLogin)
            model.settings.launchAtLogin = LaunchAtLogin.isEnabled
        }
        model.settings.hasCompletedSetup = true
        ActionPerformer.shared.requestNotificationPermission()
    }
}
