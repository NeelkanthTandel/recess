import AppKit
import SwiftUI

/// Amount + units editor.
struct IntervalField: View {
    @Binding var interval: Interval
    var units: [TimeUnit] = [.seconds, .minutes, .hours]
    var minimum: Double = 0

    var body: some View {
        HStack(spacing: 4) {
            TextField(
                "",
                value: Binding(
                    get: { interval.amount },
                    set: { interval.amount = max(minimum, $0) }
                ), format: .number.precision(.fractionLength(0...1))
            )
            .frame(width: 56)
            .multilineTextAlignment(.trailing)
            Stepper("", value: Binding(get: { interval.amount }, set: { interval.amount = max(minimum, $0) }), in: minimum...10000)
                .labelsHidden()
            Picker("", selection: $interval.units) {
                ForEach(units) { u in Text(u.name(plural: interval.amount != 1)).tag(u) }
            }
            .labelsHidden()
            .fixedSize()
        }
    }
}

struct NumberField: View {
    @Binding var value: Double
    var range: ClosedRange<Double> = 0...10000
    var step: Double = 1
    var suffix: String = ""

    var body: some View {
        HStack(spacing: 4) {
            TextField(
                "", value: Binding(get: { value }, set: { value = min(max($0, range.lowerBound), range.upperBound) }),
                format: .number.precision(.fractionLength(0...1))
            )
            .frame(width: 52)
            .multilineTextAlignment(.trailing)
            Stepper("", value: $value, in: range, step: step).labelsHidden()
            if !suffix.isEmpty { Text(suffix).foregroundStyle(.secondary) }
        }
    }
}

struct IntField: View {
    @Binding var value: Int
    var range: ClosedRange<Int> = 0...1000
    var suffix: String = ""

    var body: some View {
        HStack(spacing: 4) {
            TextField("", value: $value, format: .number).frame(width: 44).multilineTextAlignment(.trailing)
            Stepper("", value: $value, in: range).labelsHidden()
            if !suffix.isEmpty { Text(suffix).foregroundStyle(.secondary) }
        }
    }
}

extension Binding where Value == RGBA {
    var color: Binding<Color> {
        Binding<Color>(get: { wrappedValue.color }, set: { wrappedValue = RGBA($0) })
    }
}

/// Click to record a global shortcut (MASShortcutView equivalent).
struct ShortcutRecorder: View {
    @Binding var combo: KeyCombo?
    @State private var recording = false
    @State private var monitor: Any?

    var body: some View {
        HStack(spacing: 4) {
            Button {
                recording ? stop() : start()
            } label: {
                Text(recording ? "Type Shortcut" : (combo?.display ?? "Record Shortcut"))
                    .frame(minWidth: 120)
                    .foregroundStyle(recording ? Color.accentColor : (combo == nil ? .secondary : .primary))
            }
            .help("Click, then press the keys you want. Delete clears it; Escape cancels.")
            if combo != nil && !recording {
                Button {
                    combo = nil
                } label: {
                    Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary)
                }
                .buttonStyle(.plain)
                .help("Clear shortcut")
            }
        }
        .onDisappear { stop() }
    }

    private func start() {
        recording = true
        HotKeyCenter.shared.suspended = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            let mods = event.modifierFlags.intersection([.command, .option, .control, .shift])
            if event.keyCode == 53 {  // escape
                stop()
            } else if (event.keyCode == 51 || event.keyCode == 117) && mods.isEmpty {  // delete
                combo = nil
                stop()
            } else if !mods.isEmpty || (96...122).contains(Int(event.keyCode)) {  // needs a modifier unless F-key
                combo = KeyCombo(keyCode: UInt32(event.keyCode), modifiers: mods.rawValue)
                stop()
            } else {
                NSSound.beep()
            }
            return nil
        }
    }

    private func stop() {
        recording = false
        HotKeyCenter.shared.suspended = false
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
    }
}

struct SectionHeader: View {
    let title: String
    var body: some View {
        Text(title).font(.headline).padding(.top, 6)
    }
}

/// Small caption explaining a setting.
struct Hint: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View {
        Text(text).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
    }
}
