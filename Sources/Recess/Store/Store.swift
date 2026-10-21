import Foundation

/// JSON persistence in ~/Library/Application Support/Recess.
enum Store {
    static let folder: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        var url = base.appendingPathComponent("Recess", isDirectory: true)
        #if DEBUG
            // Debug builds can use a throwaway data folder (see CONTRIBUTING.md).
            if let override = ProcessInfo.processInfo.environment["RECESS_DATA_DIR"] {
                url = URL(fileURLWithPath: override, isDirectory: true)
            }
        #endif
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }()

    static func subfolder(_ name: String) -> URL {
        let url = folder.appendingPathComponent(name, isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    static var themesFolder: URL { subfolder("Themes") }
    static var scriptsFolder: URL { subfolder("Scripts") }
    static var activityFolder: URL { subfolder("Activity") }

    static let settingsURL = folder.appendingPathComponent("Settings.json")
    static let breaksURL = folder.appendingPathComponent("Breaks.json")

    static let encoder: JSONEncoder = {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        e.dateEncodingStrategy = .iso8601
        return e
    }()

    static let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return d
    }()

    static func write<T: Encodable>(_ value: T, to url: URL) {
        do {
            let data = try encoder.encode(value)
            try data.write(to: url, options: .atomic)
        } catch {
            NSLog("Recess: failed to save \(url.lastPathComponent): \(error)")
        }
    }

    static func read<T: Decodable>(_ type: T.Type, from url: URL) -> T? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        do {
            return try decoder.decode(type, from: data)
        } catch {
            NSLog("Recess: failed to read \(url.lastPathComponent): \(error)")
            return nil
        }
    }

    /// Decodes `data`, filling any keys missing from it with the values in `defaults`
    /// so that settings written by older builds keep loading.
    static func decodeMerging<T: Codable>(_ data: Data, defaults: T) -> T? {
        guard let saved = try? JSONSerialization.jsonObject(with: data),
            let defData = try? encoder.encode(defaults),
            let def = try? JSONSerialization.jsonObject(with: defData)
        else { return nil }
        let merged = merge(def, saved)
        guard let mergedData = try? JSONSerialization.data(withJSONObject: merged) else { return nil }
        do {
            return try decoder.decode(T.self, from: mergedData)
        } catch {
            NSLog("Recess: merge-decode failed: \(error)")
            return nil
        }
    }

    private static func merge(_ base: Any, _ over: Any) -> Any {
        guard let b = base as? [String: Any], let o = over as? [String: Any] else { return over }
        var result = b
        for (k, v) in o { result[k] = b[k].map { merge($0, v) } ?? v }
        return result
    }

    /// Fills each element of `array` with the keys of `element`'s defaults, so one rule or action
    /// missing a field doesn't make the whole file unreadable.
    private static func mergeElements<T: Encodable>(_ array: Any?, defaults element: T) -> [Any]? {
        guard let array = array as? [Any],
            let data = try? encoder.encode(element),
            let def = try? JSONSerialization.jsonObject(with: data)
        else { return nil }
        return array.map { merge(def, $0) }
    }

    static func loadSettings() -> AppSettings? {
        guard var data = try? Data(contentsOf: settingsURL) else { return nil }
        if var dict = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
            let rules = mergeElements(dict["rules"], defaults: Rule()),
            let fixed = try? JSONSerialization.data(
                withJSONObject: {
                    dict["rules"] = rules; return dict
                }())
        {
            data = fixed
        }
        guard let settings = decodeMerging(data, defaults: AppSettings()) else {
            backUpUnreadable(settingsURL)
            return nil
        }
        return settings
    }

    static func loadBreaks() -> [Break]? {
        guard let data = try? Data(contentsOf: breaksURL) else { return nil }
        guard let array = try? JSONSerialization.jsonObject(with: data) as? [Any] else {
            backUpUnreadable(breaksURL)
            return nil
        }
        let knownKinds = Set(ActionKind.allCases.map(\.rawValue))
        var lostSomething = false
        let breaks = array.compactMap { item -> Break? in
            var item = item
            // Drop only actions of a kind this version doesn't know, not the whole break.
            if var dict = item as? [String: Any], let actions = dict["actions"] as? [[String: Any]] {
                let kept = actions.filter { knownKinds.contains($0["kind"] as? String ?? "") }
                if kept.count != actions.count { lostSomething = true }
                dict["actions"] = kept
                item = dict
            }
            guard let d = try? JSONSerialization.data(withJSONObject: item),
                let b = decodeMerging(d, defaults: Break())
            else {
                lostSomething = true
                return nil
            }
            return b
        }
        // Keep a copy if anything couldn't be read, since the next save would drop it.
        if lostSomething { backUpUnreadable(breaksURL) }
        return breaks
    }

    /// Copies a file that failed to load to `Name.unreadable-<date>.json` before it can be overwritten.
    static func backUpUnreadable(_ url: URL) {
        let stamp = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: ":", with: "-")
        let backup = url.deletingPathExtension().appendingPathExtension("unreadable-\(stamp).json")
        do {
            try FileManager.default.copyItem(at: url, to: backup)
            NSLog("Recess: \(url.lastPathComponent) couldn't be read fully; saved a copy as \(backup.lastPathComponent)")
        } catch {
            NSLog("Recess: couldn't back up \(url.lastPathComponent): \(error)")
        }
    }
}
