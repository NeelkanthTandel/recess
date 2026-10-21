import ServiceManagement
import SwiftUI

struct GeneralSettingsView: View {
    @EnvironmentObject var model: AppModel
    @State private var loginError: String?

    var body: some View {
        Form {
            Section {
                Toggle(
                    "Open Recess when I log in",
                    isOn: Binding(
                        get: { model.settings.launchAtLogin },
                        set: { v in
                            loginError = LaunchAtLogin.set(v)
                            model.settings.launchAtLogin = LaunchAtLogin.isEnabled
                        }
                    )
                )
                .help("So breaks keep happening after a restart.")
                if let loginError { Text(loginError).font(.caption).foregroundStyle(.red) }
            }
            Section("Dock & menu bar") {
                Picker("Dock", selection: $model.settings.showDockIcon) {
                    Text("Show in the Dock and app switcher").tag(true)
                    Text("Menu bar only").tag(false)
                }
                .pickerStyle(.radioGroup)
                Hint(
                    model.settings.showDockIcon
                        ? "Recess gets a Dock icon, appears in Cmd-Tab and has its own menus."
                        : "No Dock icon; reach breaks and settings from the menu bar.")
                Toggle("Menu bar item", isOn: $model.settings.showStatusMenu)
                    .help("Shows the next break and quick commands in the menu bar.")
                if !model.settings.showDockIcon && !model.settings.showStatusMenu {
                    Text("With no Dock icon and no menu bar item, open Recess again from the Finder to get back to these settings.")
                        .font(.caption).foregroundStyle(.orange)
                }
                Picker("Icon", selection: $model.settings.statusIcon) {
                    ForEach(StatusIcon.allCases) { i in
                        Label(i.title, systemImage: i.symbol ?? "circle.fill").tag(i)
                    }
                }
                .help("The picture shown in the menu bar.")
                Picker("Title", selection: $model.settings.statusTitle) {
                    ForEach(StatusTitle.allCases) { Text($0.title).tag($0) }
                }
                .help("The text shown next to the menu bar picture.")
                Toggle("Ignore breaks shorter than a minute", isOn: $model.settings.statusOnlyLong)
                    .help("Keeps short micro-breaks out of the menu bar countdown.")
            }
            Section("Dates") {
                Picker("Show break times as", selection: $model.settings.relativeDates) {
                    Text("Time from now (in 20 minutes)").tag(true)
                    Text("Clock time (at 3:40 PM)").tag(false)
                }
                .pickerStyle(.radioGroup)
            }
            Section("Data") {
                HStack {
                    Text(Store.folder.path).font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
                    Spacer()
                    Button("Reveal Data Folder") { NSWorkspace.shared.activateFileViewerSelecting([Store.folder]) }
                }
                Button("Setup Assistant…") { SetupAssistantController.shared.show() }
                NotificationPermissionRow()
            }
        }
        .formStyle(.grouped)
        .navigationTitle("General")
    }
}

/// Shows whether macOS allows Recess's notifications, with a way to fix it.
struct NotificationPermissionRow: View {
    @ObservedObject private var access = NotificationAccess.shared

    var body: some View {
        LabeledContent("Notifications") {
            HStack {
                Text(access.description).foregroundStyle(access.isAllowed ? Color.secondary : Color.orange)
                switch access.status {
                case .notDetermined: Button("Allow…") { ActionPerformer.shared.requestNotificationPermission() }
                case .denied: Button("Open System Settings") { NotificationAccess.openSystemSettings() }
                default: EmptyView()
                }
            }
        }
        .onAppear { access.refresh() }
    }
}

enum LaunchAtLogin {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    /// Returns an error message, if any.
    static func set(_ enabled: Bool) -> String? {
        do {
            if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            if SMAppService.mainApp.status == .requiresApproval {
                SMAppService.openSystemSettingsLoginItems()
                return "Approve Recess in System Settings › General › Login Items."
            }
            return nil
        } catch {
            return error.localizedDescription
        }
    }
}

struct ShortcutsSettingsView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        Form {
            Section {
                Hint("Shortcuts here work in every app.")
                row("Start Next Break", \.startNext)
                row("Postpone Next Break (first button)", \.postpone1)
                row("Postpone Next Break (second button)", \.postpone2)
                row("Skip Current or Next Break", \.skip)
                row("Pause Everything", \.pauseIndefinitely)
                row("Resume All Breaks", \.resume)
                row("Reset All Breaks", \.reset)
                row("Stop Sound", \.stopSound)
            }
            Section("Start a specific break") {
                ForEach($model.breaks) { $b in
                    LabeledContent("Start \(b.name) Break") { ShortcutRecorder(combo: $b.startShortcut) }
                }
            }
        }
        .formStyle(.grouped)
        .navigationTitle("Shortcuts")
    }

    private func row(_ title: String, _ path: WritableKeyPath<Shortcuts, KeyCombo?>) -> some View {
        LabeledContent(title) {
            ShortcutRecorder(
                combo: Binding(get: { model.settings.shortcuts[keyPath: path] }, set: { model.settings.shortcuts[keyPath: path] = $0 }))
        }
    }
}

struct AdvancedSettingsView: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject private var ticker = Ticker.shared

    private let thresholds: [(String, Double)] = [
        ("30 Seconds", 30), ("1 Minute", 60), ("2 Minutes", 120), ("3 Minutes", 180), ("5 Minutes", 300),
        ("10 Minutes", 600), ("15 Minutes", 900), ("20 Minutes", 1200), ("30 Minutes", 1800), ("45 Minutes", 2700), ("1 Hour", 3600),
    ]

    var body: some View {
        Form {
            Section("Time away") {
                Picker("Detect when I'm away using", selection: $model.settings.idleDetector) {
                    ForEach(IdleDetector.allCases) { Text($0.title).tag($0) }
                }
                .onChange(of: model.settings.idleDetector) { _, v in
                    EnvironmentMonitor.shared.configureEventMonitor(enabled: v == .eventMonitor)
                }
                Hint(
                    "How Recess tells you've stepped away. The system idle timer needs no permissions; input monitoring also listens for keyboard and mouse events and needs Accessibility access. Choose None to turn off time-away detection."
                )
                Picker("Count as away after", selection: $model.settings.naturalBreakThreshold) {
                    ForEach(thresholds, id: \.1) { Text($0.0).tag($0.1) }
                }
                Hint("No keyboard or mouse input for this long counts as a natural break.")
                LabeledContent(
                    "Currently idle for", value: Format.full(ticker.idleSeconds) + (ticker.isNaturalBreak ? " (natural break)" : ""))
            }
            Section("Remove temptations") {
                Toggle("Hide Pause", isOn: $model.settings.hidePause)
                Toggle("Hide Make Due", isOn: $model.settings.hideMakeDue)
                Toggle("Hide Reset", isOn: $model.settings.hideReset)
                Toggle("Hide Disable", isOn: $model.settings.hideDisable)
                Toggle("Hide Delete", isOn: $model.settings.hideDelete)
                Toggle("Hide Quit", isOn: $model.settings.hideQuit)
                Hint("Hidden commands are still available by Option-clicking the menu bar item.")
                Toggle("Lock the Mac into the break (no Cmd-Tab, Force Quit or Shut Down)", isOn: $model.settings.disableAppSwitching)
                Hint("During a break, the app switcher, Force Quit window and shut-down dialog are blocked.")
                Toggle("Keep the menu bar visible during breaks", isOn: $model.settings.showMenuBarDuringBreak)
            }
            Section("URL commands") {
                Toggle("Accept recess:// links from other apps and web pages", isOn: $model.settings.allowURLCommands)
                Hint(
                    "Control Recess from scripts, Shortcuts or a browser: recess://start, recess://start?break=Micro, recess://postpone?minutes=5, recess://skip, recess://makedue?break=Normal&seconds=30, recess://pause?minutes=30, recess://resume, recess://reset, recess://settings"
                )
            }
        }
        .formStyle(.grouped)
        .navigationTitle("Advanced")
    }
}

struct RulesSettingsView: View {
    @EnvironmentObject var model: AppModel
    @EnvironmentObject var calendars: CalendarManager
    @State private var scripts = ScriptRunner.availableScripts()

    var body: some View {
        Form {
            Section {
                Hint(
                    "Rules change what happens when a break comes due, or whether time counts as being away. "
                        + "The first rule that matches is used; drag to reorder."
                )
            }
            Section("Rules") {
                if model.settings.rules.isEmpty {
                    Text("No rules. Add one to postpone breaks during meetings, games, presentations and more.").foregroundStyle(.secondary)
                }
                ForEach($model.settings.rules) { $rule in
                    RuleRow(rule: $rule, scripts: scripts, breaks: model.breaks, calendars: calendars) {
                        model.settings.rules.removeAll { $0.id == rule.id }
                    }
                }
                .onMove { model.settings.rules.move(fromOffsets: $0, toOffset: $1) }
                Menu("Add Rule") {
                    Button("Postpone during a video call (Zoom, Teams, FaceTime…)") {
                        var r = Rule()
                        r.condition = .frontApp
                        r.apps = [
                            "us.zoom.xos", "com.microsoft.teams2", "com.apple.FaceTime", "com.cisco.webexmeetingsapp", "com.hnc.Discord",
                        ]
                        r.action = .postpone5
                        model.settings.rules.append(r)
                    }
                    Button("Postpone while a fullscreen app is active") {
                        var r = Rule(); r.condition = .fullscreen; r.action = .postpone5; model.settings.rules.append(r)
                    }
                    Button("Skip while presenting in Keynote") {
                        var r = Rule(); r.condition = .frontApp; r.apps = ["com.apple.iWork.Keynote"]; r.action = .skip;
                        model.settings.rules.append(r)
                    }
                    Button("Treat screensaver as a natural break") {
                        var r = Rule(); r.condition = .screensaver; r.action = .treatAsNaturalBreak; model.settings.rules.append(r)
                    }
                    Button("Postpone during busy calendar events") {
                        var r = Rule(); r.condition = .calendarEvent; r.action = .postpone10; model.settings.rules.append(r)
                    }
                    Button("Ignore idle while watching video (QuickTime, VLC)") {
                        var r = Rule(); r.condition = .frontApp;
                        r.apps = ["com.apple.QuickTimePlayerX", "org.videolan.vlc", "com.apple.TV"]; r.action = .ignoreNaturalBreak
                        model.settings.rules.append(r)
                    }
                    Divider()
                    Button("Custom Rule") { model.settings.rules.append(Rule()) }
                }
                .fixedSize()
            }
            Section("Scripts") {
                Hint(
                    "Script rules run a script from the Scripts folder every 20 seconds; it should print \"true\" when the rule should match. Samples are included for Zoom, Teams and FaceTime meetings."
                )
                HStack {
                    Button("Reveal Scripts") { NSWorkspace.shared.open(Store.scriptsFolder) }
                    Button("Refresh") { scripts = ScriptRunner.availableScripts() }
                }
            }
        }
        .formStyle(.grouped)
        .navigationTitle("Rules")
    }
}

private struct RuleRow: View {
    @Binding var rule: Rule
    let scripts: [String]
    let breaks: [Break]
    @ObservedObject var calendars: CalendarManager
    let remove: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Toggle("", isOn: $rule.enabled).labelsHidden()
                Image(systemName: rule.condition.symbol).frame(width: 18)
                Text("When").foregroundStyle(.secondary)
                Picker("", selection: $rule.condition) {
                    ForEach(RuleCondition.allCases) { Text($0.title).tag($0) }
                }
                .labelsHidden().fixedSize()
                Picker("", selection: $rule.negate) {
                    Text(rule.condition == .script ? "returns true" : "is active").tag(false)
                    Text(rule.condition == .script ? "returns false" : "is not active").tag(true)
                }
                .labelsHidden().fixedSize()
                Text("then").foregroundStyle(.secondary)
                Picker("", selection: $rule.action) {
                    ForEach(RuleAction.allCases) { Text($0.title).tag($0) }
                }
                .labelsHidden().fixedSize()
                Spacer()
                Button(action: remove) { Image(systemName: "minus.circle.fill").foregroundStyle(.secondary) }
                    .buttonStyle(.plain).help("Delete rule")
            }
            Group {
                if rule.condition.needsApps {
                    HStack {
                        ForEach(rule.apps, id: \.self) { id in
                            HStack(spacing: 3) {
                                Image(nsImage: AppInfo.icon(forBundleID: id)).resizable().frame(width: 16, height: 16)
                                Text(AppInfo.name(forBundleID: id)).lineLimit(1)
                                Button {
                                    rule.apps.removeAll { $0 == id }
                                } label: {
                                    Image(systemName: "xmark").font(.caption2)
                                }.buttonStyle(.plain)
                            }
                            .padding(.horizontal, 6).padding(.vertical, 2)
                            .background(Capsule().fill(.quaternary))
                        }
                        Button("Add Apps…") { rule.apps += AppInfo.chooseApps().filter { !rule.apps.contains($0) } }
                            .controlSize(.small)
                    }
                } else if rule.condition == .script {
                    Picker("Script", selection: $rule.scriptFilename) {
                        Text("None").tag("")
                        ForEach(scripts, id: \.self) { Text($0).tag($0) }
                    }
                    .fixedSize()
                } else if rule.condition == .calendarEvent {
                    HStack {
                        if !calendars.authorized {
                            Button("Grant Calendar Access") { calendars.requestAccess() }
                        } else {
                            Picker("Calendar", selection: $rule.calendarIdentifier) {
                                Text("All Calendars").tag(String?.none)
                                ForEach(calendars.calendars, id: \.calendarIdentifier) {
                                    Text($0.title).tag(Optional($0.calendarIdentifier))
                                }
                            }
                            .fixedSize()
                            TextField("matching", text: $rule.calendarFilter, prompt: Text("any event"))
                        }
                    }
                } else if rule.condition == .focus {
                    Hint(
                        "Focus detection reads the Do Not Disturb database, which needs Full Disk Access for Recess (System Settings › Privacy & Security). See SECURITY.md for the trade-off before granting it."
                    )
                }
                if rule.action.isDueAction {
                    HStack {
                        Text("Applies to").foregroundStyle(.secondary)
                        Menu(
                            rule.breakIDs.isEmpty
                                ? "All Breaks" : breaks.filter { rule.breakIDs.contains($0.id) }.map(\.name).joined(separator: ", ")
                        ) {
                            Button("All Breaks") { rule.breakIDs = [] }
                            Divider()
                            ForEach(breaks) { b in
                                Button {
                                    if rule.breakIDs.contains(b.id) {
                                        rule.breakIDs.removeAll { $0 == b.id }
                                    } else {
                                        rule.breakIDs.append(b.id)
                                    }
                                } label: {
                                    if rule.breakIDs.contains(b.id) { Label(b.name, systemImage: "checkmark") } else { Text(b.name) }
                                }
                            }
                        }
                        .fixedSize()
                    }
                    .font(.callout)
                }
            }
            .padding(.leading, 52)
        }
        .padding(.vertical, 4)
        .opacity(rule.enabled ? 1 : 0.55)
    }
}
