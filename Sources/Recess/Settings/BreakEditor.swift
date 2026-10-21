import AVFoundation
import SwiftUI

enum BreakPage: String, CaseIterable, Identifiable {
    case name = "Name", schedule = "Schedule", appearance = "Appearance", controls = "Postpone & Skip", actions = "Actions"
    var id: String { rawValue }
}

struct BreakEditorView: View {
    @EnvironmentObject var model: AppModel
    let breakID: UUID
    @AppStorage("breakPage") private var page: BreakPage = .schedule

    private var binding: Binding<Break> {
        Binding(
            get: { model.breakWith(id: breakID) ?? Break() },
            set: { new in model.update(breakID) { $0 = new } }
        )
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Circle().fill(binding.wrappedValue.color.color).frame(width: 14, height: 14)
                Text(binding.wrappedValue.name).font(.title2.weight(.semibold))
                Spacer()
                Picker("", selection: $page) {
                    ForEach(BreakPage.allCases) { Text($0.rawValue).tag($0) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
                Spacer()
                Button("Try It") { Scheduler.shared.startBreak(breakID, manual: true, preview: true) }
                    .help("Preview the break to try the settings; this isn't counted.")
                    .disabled(model.activeBreakID != nil)
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 12)
            Divider()
            Group {
                switch page {
                case .name: NamePage(b: binding)
                case .schedule: SchedulePage(b: binding)
                case .appearance: AppearancePage(b: binding)
                case .controls: ControlsPage(b: binding)
                case .actions: ActionsPage(b: binding)
                }
            }
        }
        // Only timing changes move the next due time; other schedule options take effect as they are.
        .onChange(of: binding.wrappedValue.schedule.timing) { _, _ in
            Scheduler.shared.scheduleChanged(breakID)
        }
    }
}

// MARK: - Name

private struct NamePage: View {
    @Binding var b: Break

    var body: some View {
        Form {
            Section {
                TextField("Name", text: $b.name)
                    .help("What this break is called in menus and on the break screen.")
                ColorPicker("Label colour", selection: $b.color.color, supportsOpacity: false)
                    .help("Colour used for this break in the sidebar, menu bar and break screen.")
                Toggle("Use a pie of today's postpones, skips and completions as the label", isOn: $b.labelAsPie)
                    .help("Replaces the colour dot with a tiny pie chart of today's results for this break.")
                Toggle(
                    "Enabled",
                    isOn: Binding(get: { b.enabled }, set: { v in DispatchQueue.main.async { Scheduler.shared.setEnabled(b.id, v) } }))
            }
            Section("Start shortcut") {
                LabeledContent("Start this break from any app") { ShortcutRecorder(combo: $b.startShortcut) }
                Hint("A keyboard shortcut that starts this break immediately, whichever app is in front.")
            }
            Section("Comments") {
                TextEditor(text: $b.comments)
                    .frame(minHeight: 80)
                    .font(.body)
            }
            Section("Today") {
                LabeledContent("Status", value: b.state.status)
                LabeledContent(
                    "Postponed / skipped / done",
                    value: "\(b.state.counts.todayPostpone) / \(b.state.counts.todaySkip) / \(b.state.counts.todayDone)")
                LabeledContent("Time on break today", value: Format.full(b.state.counts.todayDuration))
                LabeledContent("All time", value: "\(b.state.counts.totalDone) breaks, \(Format.full(b.state.counts.totalDuration))")
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Schedule

private struct SchedulePage: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject private var ticker = Ticker.shared
    @EnvironmentObject var calendars: CalendarManager
    @Binding var b: Break

    private let weekdaySymbols = Calendar.current.shortWeekdaySymbols

    var body: some View {
        Form {
            Section {
                LabeledContent("Break for") { IntervalField(interval: $b.schedule.duration, minimum: 1) }
                    .help("How long the break lasts.")
                    .disabled(b.schedule.adjustment == .calendar)
                LabeledContent("Every") {
                    HStack {
                        IntervalField(interval: $b.schedule.frequency, units: [.minutes, .hours], minimum: 1)
                        Text("(work for \(Format.full(max(0, b.frequency - b.schedule.duration.timeInterval))))").foregroundStyle(
                            .secondary)
                    }
                }
                .help("Time from the start of one break to the start of the next (the break itself is included).")
                .disabled(b.schedule.adjustment == .calendar)

                Picker("Next due from", selection: $b.schedule.adjustment) {
                    ForEach(DueAdjustment.allCases) { Text($0.title).tag($0) }
                }
                .help(
                    "What the next break is timed from: when the last one was due, when it finished, a set minute of each hour, or your calendar."
                )
                if b.schedule.adjustment == .fixedTime {
                    LabeledContent("At") {
                        IntField(value: $b.schedule.fixedMinutesPastHour, range: 0...59, suffix: "minutes past the hour")
                    }
                }
                if b.schedule.adjustment == .calendar {
                    calendarControls
                }
                LabeledContent("Next due") {
                    HStack {
                        Text(Scheduler.shared.waitingDescription(for: b, now: ticker.now) ?? model.dateString(b.state.nextDue))
                            .foregroundStyle(.secondary)
                        if let due = b.state.nextDue, due != .distantFuture {
                            DatePicker(
                                "",
                                selection: Binding(
                                    get: { due },
                                    set: { v in DispatchQueue.main.async { Scheduler.shared.makeDue(b.id, in: v.timeIntervalSinceNow) } }),
                                displayedComponents: [.hourAndMinute]
                            )
                            .labelsHidden()
                            .frame(width: 90)
                        }
                    }
                }
                .help("When this break will next happen. Pick a time to move it.")
            }

            Section("Availability") {
                Picker("Time of day", selection: $b.schedule.availability) {
                    ForEach(Availability.allCases) { Text($0.title).tag($0) }
                }
                .help("Restrict the hours this break can happen in.")
                if b.schedule.availability == .between {
                    LabeledContent("From") {
                        DatePicker("", selection: $b.schedule.fromTime.asDate, displayedComponents: .hourAndMinute).labelsHidden()
                    }
                    .help("The day's first break comes one interval after this time.")
                    LabeledContent("To") {
                        DatePicker("", selection: $b.schedule.toTime.asDate, displayedComponents: .hourAndMinute).labelsHidden()
                    }
                    .help("No break of this kind starts at or after this time.")
                }
                LabeledContent("Days") {
                    HStack(spacing: 4) {
                        ForEach(1...7, id: \.self) { day in
                            let on = b.schedule.weekdays.contains(day)
                            Button(weekdaySymbols[day - 1]) {
                                // At least one day must stay on.
                                if on {
                                    if b.schedule.weekdays.count > 1 { b.schedule.weekdays.remove(day) }
                                } else {
                                    b.schedule.weekdays.insert(day)
                                }
                            }
                            .buttonStyle(.bordered)
                            .tint(on ? .accentColor : .gray)
                            .controlSize(.small)
                        }
                    }
                }
                .help("Click a day to turn this break on or off for it.")
            }

            Section("Time away") {
                Picker("While you're away from the Mac", selection: $b.schedule.idleHandling) {
                    ForEach(IdleHandling.allCases) { Text($0.title).tag($0) }
                }
                Toggle(isOn: $b.schedule.resetWhenIdle) {
                    HStack {
                        Text("Start the timer over after being away for at least")
                        IntervalField(
                            interval: Binding(
                                get: { b.schedule.resetWhenIdleInterval ?? b.schedule.duration },
                                set: { b.schedule.resetWhenIdleInterval = $0 }
                            ), minimum: 1
                        )
                        .disabled(!b.schedule.resetWhenIdle)
                    }
                }
                Toggle("Start the timer over when a more important break finishes", isOn: $b.schedule.resetWhenFinishingHigherPriority)
                Toggle(isOn: $b.schedule.skipForHigherPriority) {
                    HStack {
                        Text("Don't bother with this break when a more important one is coming within")
                        IntervalField(interval: $b.schedule.skipForHigherPriorityInterval, units: [.seconds, .minutes], minimum: 0)
                            .disabled(!b.schedule.skipForHigherPriority)
                    }
                }
                Hint("Breaks nearer the top of the sidebar are more important. Drag to reorder them.")
            }

            Section("When it's time") {
                Picker("Start", selection: $b.schedule.dueBehavior) {
                    ForEach(DueBehavior.allCases) { Text($0.title).tag($0) }
                }
                switch b.schedule.dueBehavior {
                case .startImmediately, .waitForNaturalBreak:
                    Hint(b.schedule.dueBehavior.explanation)
                case .ask:
                    LabeledContent("Start anyway after") {
                        IntervalField(interval: $b.schedule.askAutoStart, units: [.seconds, .minutes], minimum: 5)
                    }
                    Hint("A small panel lets you begin when ready; if you ignore it, the break begins on its own after this long.")
                }
            }
        }
        .formStyle(.grouped)
    }

    @ViewBuilder private var calendarControls: some View {
        if !calendars.authorized {
            HStack {
                Text("Recess needs access to your calendars to time breaks from events.").foregroundStyle(.secondary)
                Button("Grant Calendar Access") { calendars.requestAccess() }
            }
        } else {
            Picker("Calendar", selection: $b.schedule.calendarIdentifier) {
                Text("All Calendars").tag(String?.none)
                ForEach(calendars.calendars, id: \.calendarIdentifier) { c in
                    Text(c.title).tag(Optional(c.calendarIdentifier))
                }
            }
            .help("Which calendar's events become breaks.")
            TextField("Matching", text: $b.schedule.calendarEventFilter, prompt: Text("any event"))
                .help(
                    "Only events whose title, location, notes or link contain one of these comma-separated words. Leave empty for every event."
                )
            HStack {
                if let s = b.state.nextEventStart, let e = b.state.nextEventEnd {
                    Text("Next event: \(Format.absolute(s)) – \(Format.time(e))").foregroundStyle(.secondary)
                } else {
                    Text("No upcoming matching events").foregroundStyle(.secondary)
                }
                Spacer()
                Button("Reload") { Scheduler.shared.scheduleChanged(b.id) }
                    .help("Look up calendar events again and reschedule.")
            }
        }
    }
}

// MARK: - Appearance

private struct AppearancePage: View {
    @EnvironmentObject var themes: ThemeManager
    @Binding var b: Break
    @State private var showThemeEditor = false

    private var selectedTheme: Theme { themes.theme(id: b.appearance.themeID) }

    var body: some View {
        Form {
            Section("Theme") {
                Picker("Show", selection: $b.appearance.themeID) {
                    ForEach(groupedThemes, id: \.0) { group, list in
                        Section(group) {
                            ForEach(list) { t in Text(t.name).tag(t.id) }
                        }
                    }
                }
                .help("What fills the screen during the break.")
                if !selectedTheme.comments.isEmpty { Hint(selectedTheme.comments) }
                if !selectedTheme.author.isEmpty || !selectedTheme.creditsName.isEmpty {
                    Hint("By \(selectedTheme.author)\(selectedTheme.creditsName.isEmpty ? "" : " · Source: \(selectedTheme.creditsName)")")
                }
                HStack {
                    Button("New Theme…") { showThemeEditor = true }
                    if selectedTheme.editable {
                        Button("Delete Theme", role: .destructive) {
                            themes.delete(selectedTheme)
                            b.appearance.themeID = ThemeID.defaultTheme
                        }
                    }
                    if let folder = selectedTheme.folder {
                        Button("Reveal in Finder") { NSWorkspace.shared.activateFileViewerSelecting([folder]) }
                    }
                    Button("Reveal Themes") { NSWorkspace.shared.open(Store.themesFolder) }
                    Spacer()
                    Button("Reload") { themes.reload() }
                }
                if b.appearance.themeID == ThemeID.defaultTheme || b.appearance.themeID == ThemeID.none {
                    TextField("Message", text: $b.appearance.message, prompt: Text("A random tip"))
                    Hint("Shown below the countdown. Use {break} for the break name.")
                }
            }

            Section("Screens") {
                Picker("On screen", selection: $b.appearance.mainScreen) {
                    ForEach(MainScreenChoice.allCases) { Text($0.title).tag($0) }
                }
                ColorPicker("Background colour", selection: $b.appearance.backgroundColor.color, supportsOpacity: true)
                    .help("Choose a color to display behind the theme, and how transparent it is.")
                Picker("Other screens", selection: $b.appearance.otherScreens) {
                    ForEach(OtherScreensMode.allCases) { Text($0.title).tag($0) }
                }
                if b.appearance.otherScreens == .dim {
                    ColorPicker("Other screens colour", selection: $b.appearance.otherScreensColor.color, supportsOpacity: true)
                }
                LabeledContent("Fade in over") { NumberField(value: $b.controls.fadeInSeconds, range: 0...30, suffix: "seconds") }
                    .help("How gradually the break screen appears.")
                LabeledContent("Fade out over") { NumberField(value: $b.controls.fadeOutSeconds, range: 0...30, suffix: "seconds") }
                    .help("How gradually the break screen disappears.")
            }

        }
        .formStyle(.grouped)
        .sheet(isPresented: $showThemeEditor) {
            ThemeEditorSheet { theme in b.appearance.themeID = theme.id }
        }
    }

    private var groupedThemes: [(String, [Theme])] {
        let order = ["Recess", "My Themes"]
        let groups = Dictionary(grouping: themes.themes, by: \.sourceLabel)
        return order.compactMap { k in groups[k].map { (k, $0) } }
    }
}

// MARK: - Postpone & Skip

private struct ControlsPage: View {
    @Binding var b: Break

    var body: some View {
        Form {
            Section("Buttons") {
                Toggle(isOn: $b.controls.showPostpone1) {
                    HStack {
                        Text("Offer a postpone button for");
                        NumberField(value: $b.controls.postponeMinutes1, range: 1...240, suffix: "minutes")
                    }
                }
                .help("Lets you push the break back. You can also set a keyboard shortcut for this under Shortcuts.")
                Toggle(isOn: $b.controls.showPostpone2) {
                    HStack {
                        Text("Offer a second postpone button for");
                        NumberField(value: $b.controls.postponeMinutes2, range: 1...240, suffix: "minutes")
                    }
                }
                Toggle("Offer a skip button", isOn: $b.controls.showSkip)
                    .help("Lets you skip this break entirely. You can also set a keyboard shortcut for this under Shortcuts.")
                Toggle(isOn: $b.controls.showEndEarly) {
                    HStack {
                        Text("Offer an end-now button once");
                        IntervalField(interval: $b.controls.endEarlyAfter, units: [.seconds, .minutes]); Text("have passed")
                    }
                }
                .help("For when you've rested enough before the time is up.")
                Toggle("Stay on the break screen until I dismiss it", isOn: $b.controls.endWhenDone)
                    .help("When time is up, the screen stays until you click End Break.")
                Toggle("Show today's postpone, skip and completion counts", isOn: $b.controls.showCounters)
            }

            Section("Limits") {
                LabeledContent("Lock the buttons for") {
                    NumberField(value: $b.controls.disableButtonsSeconds, range: 0...600, suffix: "seconds after start")
                }
                .help("Postpone and skip can't be used until this many seconds after the break begins (on top of the fade in).")
                Toggle(isOn: $b.controls.limitPostpone) {
                    HStack {
                        Text("Allow at most")
                        IntField(value: $b.controls.postponeLimit, range: 0...100, suffix: "postpones per")
                        Picker("", selection: $b.controls.postponeLimitKind) { ForEach(LimitKind.allCases) { Text($0.title).tag($0) } }
                            .labelsHidden().fixedSize()
                    }
                }
                Toggle(isOn: $b.controls.limitSkip) {
                    HStack {
                        Text("Allow at most")
                        IntField(value: $b.controls.skipLimit, range: 0...100, suffix: "skips per")
                        Picker("", selection: $b.controls.skipLimitKind) { ForEach(LimitKind.allCases) { Text($0.title).tag($0) } }
                            .labelsHidden().fixedSize()
                    }
                }
            }
            Section("Today") {
                LabeledContent(
                    "Postponed",
                    value:
                        "\(b.state.counts.todayPostpone) today, \(b.state.counts.hourPostpone) this hour, \(b.state.counts.breakPostpone) this break"
                )
                LabeledContent(
                    "Skipped",
                    value: "\(b.state.counts.todaySkip) today, \(b.state.counts.hourSkip) this hour, \(b.state.counts.breakSkip) this break"
                )
                if let p = b.postponesRemaining { LabeledContent("Postpones remaining", value: "\(p)") }
                if let k = b.skipsRemaining { LabeledContent("Skips remaining", value: "\(k)") }
                Hint(
                    "Limits apply everywhere: the break screen, the menu bar, keyboard shortcuts and recess:// commands. Rules can still postpone or skip."
                )
            }
        }
        .formStyle(.grouped)
    }
}

struct ThemeEditorSheet: View {
    @Environment(\.dismiss) private var dismiss
    @State private var config = ThemeManager.GeneratedConfig()
    @State private var error: String?
    let created: (Theme) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("New Theme").font(.title2.weight(.semibold)).padding([.top, .horizontal], 20)
            Form {
                TextField("Name", text: $config.name)
                Picker("Type", selection: $config.kind) {
                    ForEach(ThemeManager.GeneratedKind.allCases) { Text($0.title).tag($0) }
                }
                switch config.kind {
                case .text:
                    TextEditor(text: $config.text).frame(height: 90)
                    TextField("Font", text: $config.fontName)
                    LabeledContent("Size") { NumberField(value: $config.fontSize, range: 12...200, suffix: "pt") }
                    ColorPicker("Colour", selection: $config.foreground.color, supportsOpacity: false)
                case .website:
                    TextField("URL", text: $config.url)
                case .youtube:
                    TextField("YouTube URL", text: $config.url)
                case .images:
                    LabeledContent("Folder") {
                        HStack {
                            Text(config.imagesFolder?.lastPathComponent ?? "None").foregroundStyle(.secondary)
                            Button("Choose…") {
                                let p = NSOpenPanel()
                                p.canChooseDirectories = true
                                p.canChooseFiles = false
                                if p.runModal() == .OK { config.imagesFolder = p.url }
                            }
                        }
                    }
                    LabeledContent("Change every") { NumberField(value: $config.cycleSeconds, range: 2...600, suffix: "seconds") }
                    Toggle("Random order", isOn: $config.randomOrder)
                }
                if let error { Text(error).foregroundStyle(.red) }
            }
            .formStyle(.grouped)
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Create") {
                    do {
                        let t = try ThemeManager.shared.createTheme(config)
                        created(t)
                        dismiss()
                    } catch {
                        self.error = error.localizedDescription
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(config.name.isEmpty)
            }
            .padding(20)
        }
        .frame(width: 480, height: 460)
    }
}

// MARK: - Actions

private struct ActionsPage: View {
    @Binding var b: Break
    @State private var selection: UUID?

    /// Binds an action by ID, so a removed action can never be written through a stale index.
    private func actionBinding(_ id: UUID) -> Binding<BreakAction>? {
        guard let current = b.actions.first(where: { $0.id == id }) else { return nil }
        return Binding(
            get: { b.actions.first(where: { $0.id == id }) ?? current },
            set: { new in
                if let i = b.actions.firstIndex(where: { $0.id == id }) { b.actions[i] = new }
            }
        )
    }

    var body: some View {
        HSplitView {
            VStack(spacing: 0) {
                List(selection: $selection) {
                    ForEach(ActionStage.allCases) { stage in
                        let items = b.actions.filter { $0.stage == stage }
                        if !items.isEmpty {
                            Section(stage.title.capitalized) {
                                ForEach(items) { a in
                                    HStack {
                                        Image(systemName: a.kind.symbol).frame(width: 18)
                                        VStack(alignment: .leading) {
                                            Text(a.kind.displayName)
                                            Text(a.summary).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                                        }
                                        Spacer()
                                        if !a.enabled { Text("off").font(.caption).foregroundStyle(.tertiary) }
                                    }
                                    .tag(a.id)
                                }
                            }
                        }
                    }
                }
                .overlay {
                    if b.actions.isEmpty {
                        ContentUnavailableView("No Actions", systemImage: "bolt.slash", description: Text("Click + to add one."))
                    }
                }
                HStack {
                    Menu {
                        ForEach(ActionKind.allCases) { k in
                            Button(k.displayName) {
                                let stage: ActionStage = k == .fadeSound ? .beforeEnd : (k == .notification ? .beforeStart : .atStart)
                                let a = BreakAction(kind: k, stage: stage, offset: stage.usesOffset ? 30 : 0)
                                b.actions.append(a)
                                selection = a.id
                            }
                        }
                    } label: {
                        Image(systemName: "plus")
                    }
                    .menuStyle(.borderlessButton).fixedSize()
                    .help("Add something to happen around this break.")
                    Button {
                        b.actions.removeAll { $0.id == selection }
                        selection = nil
                    } label: {
                        Image(systemName: "minus")
                    }
                    .buttonStyle(.borderless)
                    .disabled(selection == nil)
                    Spacer()
                }
                .padding(8)
            }
            .frame(minWidth: 240, idealWidth: 260)

            Group {
                if let id = selection, let action = actionBinding(id) {
                    ActionDetail(action: action, breakItem: b)
                } else {
                    ContentUnavailableView(
                        "Select an action", systemImage: "bolt",
                        description: Text(
                            "Actions run before, during and after breaks: notifications, sounds, speech, screen flashes and scripts."))
                }
            }
            .frame(minWidth: 340, maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

private struct ActionDetail: View {
    @Binding var action: BreakAction
    let breakItem: Break
    @State private var sounds = ActionPerformer.soundLibrary()
    @State private var scripts = ScriptRunner.availableScripts()

    var body: some View {
        Form {
            Section {
                Toggle("Enabled", isOn: $action.enabled)
                Picker("When", selection: $action.stage) {
                    ForEach(ActionStage.allCases) { Text($0.title).tag($0) }
                }
                if action.stage.usesOffset {
                    LabeledContent("Seconds \(action.stage == .beforeStart ? "before due" : "before end")") {
                        NumberField(value: $action.offset, range: 0...3600, step: 5)
                    }
                }
            }
            Section(action.kind.displayName) {
                switch action.kind {
                case .notification:
                    TextField("Title", text: $action.title)
                    TextField("Message", text: $action.message)
                    soundPicker(allowNone: true)
                    NotificationPermissionRow()
                case .sound:
                    soundPicker(allowNone: false)
                    Slider(value: $action.volume, in: 0...1) { Text("Volume") }
                    Toggle("Loop until the break ends", isOn: $action.loops)
                    HStack {
                        Text(action.soundPath.isEmpty ? "Or choose a file…" : (action.soundPath as NSString).lastPathComponent)
                            .foregroundStyle(.secondary)
                        Spacer()
                        Button("Choose File…") {
                            let p = NSOpenPanel()
                            p.allowedContentTypes = [.audio]
                            if p.runModal() == .OK, let u = p.url { action.soundPath = u.path }
                        }
                        if !action.soundPath.isEmpty { Button("Clear") { action.soundPath = "" } }
                    }
                case .fadeSound:
                    LabeledContent("Fade out over") { NumberField(value: $action.fadeSeconds, range: 0.5...60, suffix: "seconds") }
                    Hint("Fades out any sound started by a Play Sound action (for example a looping sound).")
                case .speech:
                    TextField("Message", text: $action.message)
                    Picker("Voice", selection: $action.voiceIdentifier) {
                        Text("System Default").tag("")
                        ForEach(
                            AVSpeechSynthesisVoice.speechVoices().filter {
                                $0.language.hasPrefix(Locale.current.language.languageCode?.identifier ?? "en")
                            }, id: \.identifier
                        ) { v in
                            Text("\(v.name) (\(v.language))").tag(v.identifier)
                        }
                    }
                    Slider(value: $action.rate, in: 0...1) { Text("Rate") }
                    Slider(value: $action.pitch, in: 0.5...2) { Text("Pitch") }
                    Slider(value: $action.volume, in: 0...1) { Text("Volume") }
                case .flashScreen:
                    Hint("Briefly flashes all screens white.")
                case .script:
                    Picker("Script", selection: $action.scriptFilename) {
                        Text("None").tag("")
                        ForEach(scripts, id: \.self) { Text($0).tag($0) }
                    }
                    HStack {
                        Button("Reveal Scripts") { NSWorkspace.shared.open(Store.scriptsFolder) }
                        Button("Refresh") { scripts = ScriptRunner.availableScripts() }
                    }
                    Hint(
                        "AppleScript (.scpt/.applescript), shell (.sh) and Automator (.workflow) scripts in the Scripts folder. The environment includes RECESS_BREAK_NAME and RECESS_STAGE."
                    )
                }
                Hint("Use {break}, {duration} or {time} in messages.")
            }
            Section {
                Button("Try It") { ActionPerformer.shared.preview(action, for: breakItem) }
            }
        }
        .formStyle(.grouped)
    }

    @ViewBuilder private func soundPicker(allowNone: Bool) -> some View {
        Picker("Sound", selection: $action.soundName) {
            if allowNone { Text("None").tag("") }
            ForEach(sounds, id: \.name) { Text($0.name).tag($0.name) }
        }
    }
}
