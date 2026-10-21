import AppKit
import SwiftUI

enum SidebarItem: Hashable {
    case breakItem(UUID)
    case general, shortcuts, rules, activity, advanced
}

@MainActor
final class SettingsNavigation: ObservableObject {
    @Published var selection: SidebarItem?
}

@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    static let shared = SettingsWindowController()

    private var window: NSWindow?
    let navigation = SettingsNavigation()

    func show(selecting item: SidebarItem? = nil) {
        if let item { navigation.selection = item }
        if navigation.selection == nil {
            navigation.selection = AppModel.shared.breaks.first.map { .breakItem($0.id) } ?? .general
        }
        if window == nil {
            let root = SettingsRootView()
                .environmentObject(AppModel.shared)
                .environmentObject(navigation)
                .environmentObject(ThemeManager.shared)
                .environmentObject(CalendarManager.shared)
            let w = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 980, height: 680),
                styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                backing: .buffered, defer: false)
            w.title = "Recess"
            w.contentViewController = NSHostingController(rootView: root)
            w.setContentSize(NSSize(width: 980, height: 680))
            w.minSize = NSSize(width: 820, height: 560)
            w.isReleasedWhenClosed = false
            w.delegate = self
            w.center()
            w.setFrameAutosaveName("RecessSettings")
            window = w
        }
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func windowWillClose(_ notification: Notification) {
        AppModel.shared.saveNow()
        if !AppModel.shared.settings.showDockIcon {
            DispatchQueue.main.async { NSApp.setActivationPolicy(.accessory) }
        }
    }
}

struct SettingsRootView: View {
    @EnvironmentObject var model: AppModel
    @EnvironmentObject var nav: SettingsNavigation

    var body: some View {
        NavigationSplitView {
            SidebarView()
                .navigationSplitViewColumnWidth(min: 230, ideal: 250, max: 320)
        } detail: {
            detail
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
    }

    @ViewBuilder private var detail: some View {
        switch nav.selection {
        case .breakItem(let id) where model.breakWith(id: id) != nil:
            BreakEditorView(breakID: id).id(id)
        case .general: GeneralSettingsView()
        case .shortcuts: ShortcutsSettingsView()
        case .rules: RulesSettingsView()
        case .activity: ActivitySettingsView()
        case .advanced: AdvancedSettingsView()
        default:
            ContentUnavailableView("Select a break or option", systemImage: "cup.and.saucer")
        }
    }
}

struct SidebarView: View {
    @EnvironmentObject var model: AppModel
    @EnvironmentObject var nav: SettingsNavigation
    @State private var confirmDelete: UUID?

    var body: some View {
        List(selection: $nav.selection) {
            Section("Breaks") {
                ForEach(model.breaks) { b in
                    BreakRow(b: b)
                        .tag(SidebarItem.breakItem(b.id))
                        .contextMenu { contextMenu(b) }
                }
                .onMove { from, to in model.breaks.move(fromOffsets: from, toOffset: to) }
            }
            Section("Options") {
                Label("General", systemImage: "gearshape").tag(SidebarItem.general)
                Label("Shortcuts", systemImage: "command").tag(SidebarItem.shortcuts)
                Label("Rules", systemImage: "list.bullet.rectangle").tag(SidebarItem.rules)
                Label("Activity", systemImage: "chart.bar.xaxis").tag(SidebarItem.activity)
                Label("Advanced", systemImage: "slider.horizontal.3").tag(SidebarItem.advanced)
            }
        }
        .listStyle(.sidebar)
        .safeAreaInset(edge: .bottom) {
            HStack(spacing: 10) {
                Menu {
                    Button("New Break") { nav.selection = .breakItem(Scheduler.shared.addBreak()) }
                    if case .breakItem(let id) = nav.selection {
                        Button("Duplicate Break") {
                            if let new = Scheduler.shared.duplicateBreak(id) { nav.selection = .breakItem(new) }
                        }
                    }
                } label: {
                    Image(systemName: "plus")
                }
                .menuStyle(.borderlessButton)
                .fixedSize()
                .help("Create a break, or copy the selected one.")

                Button {
                    model.allPaused ? Scheduler.shared.resumeAll() : Scheduler.shared.pauseAll(seconds: nil)
                } label: {
                    Image(systemName: model.allPaused ? "play.fill" : "pause.fill")
                }
                .buttonStyle(.borderless)
                .help("Pause every break, or resume them.")

                Spacer()
                Menu {
                    Button("Start Next Break") { Scheduler.shared.startNextBreak() }
                    Button("Postpone Next Break") { Scheduler.shared.postponeNext(slot: 1) }
                    Button("Skip Next Break") { Scheduler.shared.skipNext() }
                    Button("Reset All Breaks") { Scheduler.shared.resetAll() }
                    Button("Stop Sound") { ActionPerformer.shared.stopAllSound() }
                    Divider()
                    Button("Reveal Data Folder") { NSWorkspace.shared.activateFileViewerSelecting([Store.folder]) }
                    Button("Setup Assistant…") { SetupAssistantController.shared.show() }
                    Divider()
                    Button("Quit Recess") { NSApp.terminate(nil) }
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
                .menuStyle(.borderlessButton)
                .fixedSize()
                .help("More commands.")
            }
            .padding(10)
        }
        .alert("Delete this break?", isPresented: Binding(get: { confirmDelete != nil }, set: { if !$0 { confirmDelete = nil } })) {
            Button("Delete", role: .destructive) {
                if let id = confirmDelete {
                    Scheduler.shared.deleteBreak(id)
                    nav.selection = model.breaks.first.map { .breakItem($0.id) } ?? .general
                }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This can't be undone.")
        }
    }

    @ViewBuilder private func contextMenu(_ b: Break) -> some View {
        Button("Start Break") { Scheduler.shared.startBreak(b.id, manual: true) }
        Menu("Make Due In") {
            ForEach(StatusItemController.makeDueChoices, id: \.1) { t, s in
                Button(t) { Scheduler.shared.makeDue(b.id, in: TimeInterval(s)) }
            }
        }
        if b.state.pausedUntil != nil {
            Button("Resume Break") { Scheduler.shared.resume(b.id) }
        } else if !model.settings.hidePause {
            Menu("Pause Break") {
                ForEach(StatusItemController.pauseChoices, id: \.1) { t, s in
                    Button(t) { Scheduler.shared.pause(b.id, seconds: s == 0 ? nil : TimeInterval(s)) }
                }
            }
        }
        if !model.settings.hideReset { Button("Reset Break") { Scheduler.shared.resetBreak(b.id) } }
        if !model.settings.hideDisable {
            Button(b.enabled ? "Disable Break" : "Enable Break") { Scheduler.shared.setEnabled(b.id, !b.enabled) }
        }
        Divider()
        Button("Duplicate Break") { _ = Scheduler.shared.duplicateBreak(b.id) }
        if !model.settings.hideDelete {
            Button("Delete Break…", role: .destructive) { confirmDelete = b.id }
        }
    }
}

struct BreakRow: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject private var ticker = Ticker.shared
    let b: Break
    @State private var hover = false

    var body: some View {
        HStack(spacing: 10) {
            Group {
                if b.labelAsPie {
                    Image(nsImage: StatusItemController.pie(b))
                } else {
                    Circle().fill(b.enabled ? b.color.color : Color.gray.opacity(0.4)).frame(width: 12, height: 12)
                }
            }
            .frame(width: 14)
            VStack(alignment: .leading, spacing: 2) {
                Text(b.name).fontWeight(.medium)
                Text(subtitle).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                Text("Every \(b.schedule.frequency.fullDescription) for \(b.schedule.duration.fullDescription)")
                    .font(.caption2).foregroundStyle(.tertiary).lineLimit(1)
            }
            Spacer()
            if hover && model.activeBreakID == nil {
                Button("Start") { Scheduler.shared.startBreak(b.id, manual: true) }
                    .controlSize(.small)
                    .help("Start this break now.")
            }
        }
        .padding(.vertical, 3)
        .onHover { hover = $0 }
    }

    private var subtitle: String {
        if !b.enabled { return "Disabled" }
        if model.activeBreakID == b.id { return "Having the break now" }
        if b.isPaused { return b.state.pausedUntil == .distantFuture ? "Paused" : "Paused until \(Format.time(b.state.pausedUntil!))" }
        if let waiting = Scheduler.shared.waitingDescription(for: b, now: ticker.now) { return "Due now; \(waiting)" }
        return "Next: \(model.dateString(b.state.nextDue))"
    }
}
