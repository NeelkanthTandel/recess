import Foundation

/// Evaluates the user's rules.
@MainActor
final class RulesEngine {
    static let shared = RulesEngine()

    /// Cached script results, refreshed in the background so the scheduler never blocks.
    private var scriptResults: [String: Bool] = [:]
    private var scriptRunning: Set<String> = []
    private var scriptCheckedAt: [String: Date] = [:]
    private let scriptRefreshInterval: TimeInterval = 20

    private func conditionMatches(_ rule: Rule, env: EnvironmentSnapshot) -> Bool {
        let raw: Bool
        switch rule.condition {
        case .frontApp:
            raw = env.frontAppBundleID.map { rule.apps.contains($0) } ?? false
        case .openApp:
            raw = !env.runningBundleIDs.isDisjoint(with: rule.apps)
        case .focus:
            raw = env.isFocusOn
        case .fullscreen:
            raw = env.isFullscreen
        case .screensaver:
            raw = env.isScreensaver
        case .displayAsleep:
            raw = env.isDisplayAsleep
        case .calendarEvent:
            raw = CalendarManager.shared.isBusy(calendarID: rule.calendarIdentifier, filter: rule.calendarFilter)
        case .script:
            raw = scriptResult(rule.scriptFilename)
        }
        return rule.negate ? !raw : raw
    }

    private func matching(_ rules: [Rule], env: EnvironmentSnapshot, where predicate: (Rule) -> Bool) -> Rule? {
        rules.first { $0.enabled && predicate($0) && conditionMatches($0, env: env) }
    }

    /// A postpone/skip rule that matches for a due break.
    func dueRule(for b: Break, rules: [Rule], env: EnvironmentSnapshot) -> Rule? {
        matching(rules, env: env) { $0.action.isDueAction && $0.applies(to: b) }
    }

    func treatAsIdle(rules: [Rule], env: EnvironmentSnapshot) -> Rule? {
        matching(rules, env: env) { $0.action == .treatAsNaturalBreak }
    }

    func ignoreIdle(rules: [Rule], env: EnvironmentSnapshot) -> Rule? {
        matching(rules, env: env) { $0.action == .ignoreNaturalBreak }
    }

    func describe(_ rule: Rule) -> String {
        var s = rule.condition.title + (rule.negate ? " is not" : " is")
        if rule.condition.needsApps {
            s += " " + (rule.apps.isEmpty ? "(no apps)" : rule.apps.map(AppInfo.name(forBundleID:)).joined(separator: ", "))
        } else if rule.condition == .script {
            s += " true for " + (rule.scriptFilename.isEmpty ? "(no script)" : rule.scriptFilename)
        } else {
            s += " active"
        }
        return s + " → " + rule.action.title
    }

    // MARK: - Scripts

    private func scriptResult(_ filename: String) -> Bool {
        guard !filename.isEmpty else { return false }
        let stale = scriptCheckedAt[filename].map { Date().timeIntervalSince($0) > scriptRefreshInterval } ?? true
        if stale && !scriptRunning.contains(filename) {
            scriptRunning.insert(filename)
            ScriptRunner.run(filename) { [weak self] output, _ in
                guard let self else { return }
                let value = output.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
                self.scriptResults[filename] = ["true", "yes", "1"].contains(value)
                self.scriptCheckedAt[filename] = Date()
                self.scriptRunning.remove(filename)
            }
        }
        return scriptResults[filename] ?? false
    }
}

/// Runs AppleScript, shell and Automator scripts off the main thread.
///
/// Scripts are only ever run from the user's Scripts folder, by path, with no shell
/// interpolation, and are stopped if they run too long.
enum ScriptRunner {
    static let timeout: TimeInterval = 30
    private static let maxOutput = 64 * 1024

    /// Resolves a script name to a file inside the Scripts folder. Names containing path
    /// separators, and symlinks that point outside the folder, are refused.
    static func resolve(_ filename: String, in scriptsFolder: URL = Store.scriptsFolder) -> URL? {
        guard !filename.isEmpty, !filename.contains("/"), !filename.contains("\\"), filename != ".", filename != ".." else { return nil }
        let folder = scriptsFolder.resolvingSymlinksInPath().standardizedFileURL
        let url = folder.appendingPathComponent(filename).resolvingSymlinksInPath().standardizedFileURL
        guard url.deletingLastPathComponent().path == folder.path,
            FileManager.default.fileExists(atPath: url.path)
        else { return nil }
        return url
    }

    static func run(_ filename: String, environment: [String: String] = [:], completion: (@MainActor (String, Int32) -> Void)? = nil) {
        guard let url = resolve(filename) else {
            NSLog("Recess: refusing to run script \(filename): not a file in the Scripts folder")
            if let completion { DispatchQueue.main.async { MainActor.assumeIsolated { completion("", -1) } } }
            return
        }
        let process = Process()
        switch url.pathExtension.lowercased() {
        case "scpt", "applescript", "scptd":
            process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
            process.arguments = [url.path]
        case "js":
            process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
            process.arguments = ["-l", "JavaScript", url.path]
        case "workflow":
            process.executableURL = URL(fileURLWithPath: "/usr/bin/automator")
            process.arguments = [url.path]
        default:
            if FileManager.default.isExecutableFile(atPath: url.path) {
                // Executable scripts run directly so their #! line picks the interpreter.
                process.executableURL = url
                process.arguments = []
            } else {
                process.executableURL = URL(fileURLWithPath: "/bin/sh")
                process.arguments = [url.path]
            }
        }
        var env = ProcessInfo.processInfo.environment
        env.merge(environment) { $1 }
        process.environment = env
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        process.standardInput = FileHandle.nullDevice
        DispatchQueue.global(qos: .utility).async {
            var output = Data()
            var status: Int32 = -1
            do {
                try process.run()
                let watchdog = DispatchWorkItem { if process.isRunning { process.terminate() } }
                DispatchQueue.global().asyncAfter(deadline: .now() + timeout, execute: watchdog)
                let handle = pipe.fileHandleForReading
                // Keep draining so the script never blocks on a full pipe, but keep only the start.
                while case let chunk = handle.availableData, !chunk.isEmpty {
                    if output.count < maxOutput { output.append(chunk.prefix(maxOutput - output.count)) }
                }
                process.waitUntilExit()
                watchdog.cancel()
                status = process.terminationStatus
            } catch {
                NSLog("Recess: script \(url.lastPathComponent) failed: \(error)")
            }
            if let completion {
                let text = String(decoding: output, as: UTF8.self)
                DispatchQueue.main.async { MainActor.assumeIsolated { completion(text, status) } }
            }
        }
    }

    static func availableScripts() -> [String] {
        let exts: Set<String> = ["scpt", "applescript", "scptd", "sh", "workflow", "js", "py", "zsh", "bash", "command"]
        let items = (try? FileManager.default.contentsOfDirectory(atPath: Store.scriptsFolder.path)) ?? []
        return items.filter { exts.contains(($0 as NSString).pathExtension.lowercased()) }.sorted()
    }
}
