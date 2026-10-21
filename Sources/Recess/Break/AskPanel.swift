import AppKit
import SwiftUI

/// The "ask me first" due behaviour: a small panel with a button to start the break, auto-starting after a delay.
@MainActor
final class AskPanelController {
    static let shared = AskPanelController()

    enum Choice { case start, postpone(Double), skip }

    private var panel: NSPanel?
    private var breakID: UUID?
    private var handler: ((Choice) -> Void)?

    func show(for b: Break, handler: @escaping (Choice) -> Void) {
        dismiss()
        self.breakID = b.id
        self.handler = handler
        let deadline = Date().addingTimeInterval(b.schedule.askAutoStart.timeInterval)
        let view = AskView(breakItem: b, deadline: deadline) { [weak self] choice in self?.choose(choice) }
        let size = NSSize(width: 460, height: 130)
        guard let screen = NSScreen.main ?? NSScreen.screens.first else {
            handler(.start)
            return
        }
        let frame = NSRect(
            x: screen.visibleFrame.maxX - size.width - 16, y: screen.visibleFrame.maxY - size.height - 16, width: size.width,
            height: size.height)
        let p = NSPanel(
            contentRect: frame, styleMask: [.titled, .nonactivatingPanel, .fullSizeContentView], backing: .buffered, defer: false)
        p.titleVisibility = .hidden
        p.titlebarAppearsTransparent = true
        p.isMovableByWindowBackground = true
        p.level = .floating
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        p.isReleasedWhenClosed = false
        p.contentView = NSHostingView(rootView: view)
        p.orderFrontRegardless()
        panel = p
    }

    private func choose(_ choice: Choice) {
        let h = handler
        dismiss()
        h?(choice)
    }

    func dismiss() {
        panel?.orderOut(nil)
        panel = nil
        breakID = nil
        handler = nil
    }

    func dismiss(ifShowing id: UUID) {
        if breakID == id { dismiss() }
    }

    func isShowing(_ id: UUID) -> Bool { breakID == id && panel != nil }
}

private struct AskView: View {
    let breakItem: Break
    let deadline: Date
    let choose: (AskPanelController.Choice) -> Void
    @State private var fired = false

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { ctx in
            let left = deadline.timeIntervalSince(ctx.date)
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Circle().fill(breakItem.color.color).frame(width: 10, height: 10)
                    Text("\(breakItem.name) break is due").font(.headline)
                    Spacer()
                }
                Text("Starts automatically in \(Format.countdown(left)), or start it when you're ready.")
                    .font(.callout).foregroundStyle(.secondary)
                HStack {
                    Button("Start Break") { choose(.start) }.keyboardShortcut(.defaultAction)
                    if breakItem.controls.showPostpone1 && breakItem.canPostpone {
                        Button("Postpone \(Format.int(breakItem.controls.postponeMinutes1))m") {
                            choose(.postpone(breakItem.controls.postponeMinutes1))
                        }
                    }
                    if breakItem.controls.showPostpone2 && breakItem.canPostpone {
                        Button("Postpone \(Format.int(breakItem.controls.postponeMinutes2))m") {
                            choose(.postpone(breakItem.controls.postponeMinutes2))
                        }
                    }
                    if breakItem.controls.showSkip && breakItem.canSkip { Button("Skip") { choose(.skip) } }
                }
            }
            .padding(18)
            .onChange(of: left <= 0) { _, expired in
                if expired && !fired { fired = true; choose(.start) }
            }
        }
        .frame(width: 460, height: 130)
    }
}
