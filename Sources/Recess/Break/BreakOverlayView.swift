import SwiftUI
import WebKit

struct BreakOverlayView: View {
    @ObservedObject var session: BreakSession
    let mode: OverlayMode

    private var appearance: BreakAppearance { session.breakItem.appearance }
    private var isHTML: Bool { session.theme.rootURL != nil }

    var body: some View {
        ZStack {
            (mode == .dim ? appearance.otherScreensColor.color : appearance.backgroundColor.color)
                .ignoresSafeArea()

            if mode != .dim {
                if isHTML {
                    ThemeWebView(theme: session.theme, breakItem: session.breakItem, remaining: { session.remaining })
                        .ignoresSafeArea()
                } else if session.theme.id == ThemeID.defaultTheme {
                    DefaultThemeView(session: session)
                }
            }

            if mode == .main {
                VStack {
                    Spacer()
                    BreakControlsView(session: session, compact: session.theme.id == ThemeID.defaultTheme)
                        .padding(.bottom, 60)
                }
            }
        }
        .preferredColorScheme(.dark)
    }
}

/// The built-in "Default" theme: countdown ring, break name, and a suggestion.
struct DefaultThemeView: View {
    @ObservedObject var session: BreakSession
    @State private var tip = Tips.random()

    var body: some View {
        VStack(spacing: 28) {
            Text(session.breakItem.name.uppercased())
                .font(.system(size: 22, weight: .semibold, design: .rounded))
                .tracking(6)
                .foregroundStyle(.white.opacity(0.7))
            ZStack {
                Circle()
                    .stroke(.white.opacity(0.12), lineWidth: 14)
                Circle()
                    .trim(from: 0, to: 1 - session.progress)
                    .stroke(session.breakItem.color.color, style: StrokeStyle(lineWidth: 14, lineCap: .round))
                    .rotationEffect(.degrees(-90))
                    .animation(.linear(duration: 0.25), value: session.progress)
                VStack(spacing: 6) {
                    Text(session.phase == .waitingForEnd ? "Done" : Format.countdown(session.remaining))
                        .font(.system(size: 72, weight: .light, design: .rounded))
                        .monospacedDigit()
                    Text(session.phase == .waitingForEnd ? "Click End Break when ready" : "remaining")
                        .font(.system(size: 15))
                        .foregroundStyle(.white.opacity(0.6))
                }
            }
            .frame(width: 300, height: 300)
            Text(message)
                .font(.system(size: 24, weight: .regular, design: .rounded))
                .multilineTextAlignment(.center)
                .foregroundStyle(.white.opacity(0.9))
                .frame(maxWidth: 720)
        }
        .foregroundStyle(.white)
        .padding(.bottom, 140)
    }

    private var message: String {
        let m = session.breakItem.appearance.message
        return m.isEmpty ? tip : ActionPerformer.expand(m, session.breakItem)
    }
}

struct BreakControlsView: View {
    @ObservedObject var session: BreakSession
    /// When the theme already shows the countdown, only show buttons.
    var compact: Bool

    private var c: BreakControls { session.breakItem.controls }

    var body: some View {
        VStack(spacing: 14) {
            if !compact {
                VStack(spacing: 6) {
                    Text(session.breakItem.name)
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundStyle(.white.opacity(0.75))
                    Text(session.phase == .waitingForEnd ? "Break complete" : Format.countdown(session.remaining))
                        .font(.system(size: 44, weight: .light, design: .rounded))
                        .monospacedDigit()
                    ProgressView(value: session.progress)
                        .progressViewStyle(.linear)
                        .tint(session.breakItem.color.color)
                        .frame(width: 320)
                }
                .padding(.horizontal, 28)
                .padding(.vertical, 16)
                .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 18))
            }

            HStack(spacing: 12) {
                if session.phase == .waitingForEnd {
                    OverlayButton(title: "End Break", prominent: true) { session.endWhenDone() }
                }
                if c.showPostpone1 && session.canPostpone {
                    OverlayButton(title: postponeTitle(c.postponeMinutes1)) { session.postpone(1) }
                }
                if c.showPostpone2 && session.canPostpone {
                    OverlayButton(title: postponeTitle(c.postponeMinutes2)) { session.postpone(2) }
                }
                if c.showSkip && session.canSkip {
                    OverlayButton(title: "Skip This Break") { session.skip() }
                }
                if session.showEndEarly {
                    OverlayButton(title: "End Break Early") { session.endEarly() }
                }
                if session.isPreview {
                    OverlayButton(title: "End Preview") { session.skip() }
                }
            }
            .disabled(!session.buttonsEnabled)
            .opacity(session.buttonsEnabled ? 1 : 0.45)

            if c.showCounters {
                Text(session.breakItem.countersString)
                    .font(.system(size: 13))
                    .foregroundStyle(.white.opacity(0.65))
            }
            if !session.isPreview, let remaining = LimitText.remaining(session.breakItem) {
                Text(remaining).font(.system(size: 13)).foregroundStyle(.white.opacity(0.65))
            }
        }
        .foregroundStyle(.white)
    }

    private func postponeTitle(_ minutes: Double) -> String {
        let m = Format.int(minutes)
        return "Postpone \(m) Minute\(m == 1 ? "" : "s")"
    }
}

struct OverlayButton: View {
    let title: String
    var prominent = false
    let action: () -> Void

    var body: some View {
        Button(title, action: action)
            .buttonStyle(OverlayButtonStyle(prominent: prominent))
            .focusEffectDisabled()
    }
}

/// Draws the whole button itself, so no system button chrome shows around the capsule.
private struct OverlayButtonStyle: ButtonStyle {
    var prominent: Bool

    func makeBody(configuration: Configuration) -> some View {
        OverlayButtonLabel(label: configuration.label, pressed: configuration.isPressed, prominent: prominent)
    }
}

private struct OverlayButtonLabel<Label: View>: View {
    let label: Label
    let pressed: Bool
    let prominent: Bool
    @State private var hover = false

    var body: some View {
        label
            .font(.system(size: 15, weight: .medium))
            .foregroundStyle(.white)
            .padding(.horizontal, 18)
            .padding(.vertical, 10)
            .background(Capsule().fill(fill))
            .overlay(CapsuleRing().fill(.white.opacity(0.25), style: FillStyle(eoFill: true)))
            .contentShape(Capsule())
            .onHover { hover = $0 }
    }

    private var fill: Color {
        if prominent { return Color.accentColor.opacity(pressed ? 0.8 : 1) }
        return Color.white.opacity(pressed ? 0.34 : (hover ? 0.26 : 0.16))
    }
}

/// A 1-point capsule outline drawn as a filled ring. Stroked capsules draw stray marks at
/// their ends on some macOS versions; a fill doesn't.
private struct CapsuleRing: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.addRoundedRect(in: rect, cornerSize: CGSize(width: rect.height / 2, height: rect.height / 2))
        let inner = rect.insetBy(dx: 1, dy: 1)
        path.addRoundedRect(in: inner, cornerSize: CGSize(width: inner.height / 2, height: inner.height / 2))
        return path
    }
}

/// Used for the "None" theme: a small floating panel instead of dimming the screen.
struct CompactBreakView: View {
    @ObservedObject var session: BreakSession

    var body: some View {
        VStack(spacing: 10) {
            HStack {
                Circle().fill(session.breakItem.color.color).frame(width: 10, height: 10)
                Text(session.breakItem.name + " break").font(.headline)
                Spacer()
                Text(session.phase == .waitingForEnd ? "Done" : Format.countdown(session.remaining))
                    .font(.system(size: 22, weight: .medium, design: .rounded)).monospacedDigit()
            }
            ProgressView(value: session.progress).tint(session.breakItem.color.color)
            Text(
                ActionPerformer.expand(
                    session.breakItem.appearance.message.isEmpty
                        ? "Look away from the screen and relax." : session.breakItem.appearance.message, session.breakItem)
            )
            .font(.callout).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading)
            HStack {
                if session.phase == .waitingForEnd { Button("End Break") { session.endWhenDone() }.keyboardShortcut(.defaultAction) }
                if session.breakItem.controls.showPostpone1 && session.canPostpone {
                    Button("+\(Format.int(session.breakItem.controls.postponeMinutes1))m") { session.postpone(1) }
                }
                if session.breakItem.controls.showPostpone2 && session.canPostpone {
                    Button("+\(Format.int(session.breakItem.controls.postponeMinutes2))m") { session.postpone(2) }
                }
                if session.breakItem.controls.showSkip && session.canSkip { Button("Skip") { session.skip() } }
                if session.showEndEarly || session.isPreview { Button("End") { session.endEarly() } }
                Spacer()
            }
            .disabled(!session.buttonsEnabled)
        }
        .padding(16)
        .frame(width: 360, height: 210)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
    }
}

/// Hosts an HTML theme. A local theme can call `getRecessInfo` to receive break details.
struct ThemeWebView: NSViewRepresentable {
    let theme: Theme
    let breakItem: Break
    let remaining: () -> TimeInterval

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.mediaTypesRequiringUserActionForPlayback = []
        // Only local themes get break details; website themes are just web pages.
        if theme.rootURL?.isFileURL == true {
            config.userContentController.add(context.coordinator, name: "getRecessInfo")
        }
        let web = WKWebView(frame: .zero, configuration: config)
        web.setValue(false, forKey: "drawsBackground")
        web.underPageBackgroundColor = .clear
        if let url = theme.rootURL {
            if url.isFileURL, let folder = theme.folder {
                web.loadFileURL(url, allowingReadAccessTo: folder)
            } else {
                web.load(URLRequest(url: url))
            }
        }
        return web
    }

    func updateNSView(_ nsView: WKWebView, context: Context) {}

    static func dismantleNSView(_ nsView: WKWebView, coordinator: Coordinator) {
        nsView.configuration.userContentController.removeAllScriptMessageHandlers()
        nsView.stopLoading()
    }

    final class Coordinator: NSObject, WKScriptMessageHandler {
        let parent: ThemeWebView
        init(_ parent: ThemeWebView) { self.parent = parent }

        func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
            // Answer only the theme's own top-level page, not embedded frames.
            guard message.frameInfo.isMainFrame, message.frameInfo.securityOrigin.protocol == "file" else { return }
            let level = message.body as? String ?? "summary"
            let b = parent.breakItem
            let info: [String: Any] = [
                "format": 1, "level": level,
                "appName": "Recess",
                "appVersion": Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "",
                "breakName": b.name, "breakIdentifier": b.id.uuidString,
                "breakDuration": b.duration, "breakRemaining": parent.remaining(),
                "breakColor": b.color.hex,
            ]
            guard let data = try? JSONSerialization.data(withJSONObject: info),
                let json = String(data: data, encoding: .utf8)
            else { return }
            let escaped = json.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "'", with: "\\'")
            message.webView?.evaluateJavaScript("typeof recessInfo === 'function' && recessInfo('\(escaped)')")
        }
    }
}

enum Tips {
    static let all = [
        "Stand up and stretch your arms above your head.",
        "Look at something at least 20 feet away for 20 seconds.",
        "Roll your shoulders backwards ten times, slowly.",
        "Take five slow, deep breaths.",
        "Get a glass of water.",
        "Gently tilt your head toward each shoulder.",
        "Close your eyes and relax your face and jaw.",
        "Walk around the room, or step outside for some fresh air.",
        "Shake out your hands and wrists.",
        "Stretch your fingers wide, then make a gentle fist. Repeat.",
        "Sit back, unclench your shoulders, and let your arms hang loose.",
        "Blink slowly a few times to rewet your eyes.",
        "Stand on your toes, then lower slowly. Repeat ten times.",
        "Twist gently at the waist to each side.",
        "Notice your posture: feet flat, back supported, screen at eye level.",
        "Look out of a window and let your eyes focus far away.",
        "Massage your temples and the back of your neck.",
        "Put your hands behind your head and open up your chest.",
        "Tidy one small thing on your desk.",
        "Smile. You're doing great.",
    ]
    static func random() -> String { all.randomElement()! }
}

enum LimitText {
    /// "2 postpones and 0 skips left this hour", or nil when no limits are set.
    static func remaining(_ b: Break) -> String? {
        var parts: [String] = []
        if let p = b.postponesRemaining {
            parts.append("\(p) postpone\(p == 1 ? "" : "s") left \(period(b.controls.postponeLimitKind))")
        }
        if let k = b.skipsRemaining {
            parts.append("\(k) skip\(k == 1 ? "" : "s") left \(period(b.controls.skipLimitKind))")
        }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }

    private static func period(_ kind: LimitKind) -> String {
        kind == .perDay ? "today" : "this \(kind.title)"
    }
}
