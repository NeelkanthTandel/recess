import AppKit

/// A break theme: a `.recesstheme` folder holding Info.json and a Documents/ folder.
struct Theme: Identifiable, Hashable {
    var id: String
    var name: String
    var comments: String = ""
    var author: String = ""
    var creditsName: String = ""
    var creditsURL: String = ""
    var folder: URL?
    var rootDocument: String?
    var remoteURL: URL?
    var source: Source
    var editable = false

    enum Source: String { case builtin, bundled, user }

    var rootURL: URL? {
        if let remoteURL { return remoteURL }
        guard let folder, let rootDocument else { return nil }
        return folder.appendingPathComponent("Documents").appendingPathComponent(rootDocument)
    }

    var sourceLabel: String {
        switch source {
        case .builtin, .bundled: "Recess"
        case .user: "My Themes"
        }
    }
}

enum ThemeError: LocalizedError {
    case invalidYouTubeLink
    var errorDescription: String? {
        switch self {
        case .invalidYouTubeLink: "That doesn't look like a YouTube video link."
        }
    }
}

@MainActor
final class ThemeManager: ObservableObject {
    static let shared = ThemeManager()

    @Published private(set) var themes: [Theme] = []

    /// Theme folders are named `Something.recesstheme`.
    static let fileExtension = "recesstheme"

    static let builtins: [Theme] = [
        Theme(
            id: ThemeID.defaultTheme, name: "Default", comments: "Break name, countdown ring, and a gentle suggestion.", source: .builtin),
        Theme(id: ThemeID.blank, name: "Blank", comments: "Dim the screen with no theme content.", source: .builtin),
        Theme(
            id: ThemeID.none, name: "None", comments: "Don't dim the screen; show a small panel with the countdown instead.",
            source: .builtin),
    ]

    private init() { reload() }

    func reload() {
        var list = Self.builtins
        if let res = Bundle.main.resourceURL?.appendingPathComponent("Themes") {
            list += load(folder: res, source: .bundled)
        }
        list += load(folder: Store.themesFolder, source: .user)
        var seen = Set<String>()
        themes = list.filter { seen.insert($0.id).inserted }
    }

    func theme(id: String) -> Theme {
        themes.first { $0.id == id } ?? Self.builtins[0]
    }

    private func load(folder: URL, source: Theme.Source) -> [Theme] {
        let items = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
        return items.filter { $0.pathExtension == Self.fileExtension }.compactMap { url -> Theme? in
            guard let data = try? Data(contentsOf: url.appendingPathComponent("Info.json")),
                let info = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
            else { return nil }
            let name = info["name"] as? String ?? url.deletingPathExtension().lastPathComponent
            let ident = (info["identifier"] as? String) ?? name
            let author = (info["author"] as? [String: Any])?["name"] as? String ?? ""
            let credits = info["credits"] as? [String: Any]
            var t = Theme(id: "\(source.rawValue).\(ident)", name: name, source: source)
            t.comments = info["comments"] as? String ?? ""
            t.author = author
            t.creditsName = credits?["name"] as? String ?? ""
            t.creditsURL = credits?["url"] as? String ?? ""
            t.folder = url
            t.rootDocument = info["rootDocument"] as? String
            if t.rootDocument == nil, let s = info["url"] as? String { t.remoteURL = URL(string: s) }
            t.editable = source == .user
            return t.rootURL == nil ? nil : t
        }
        .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    // MARK: - Theme editor (generates simple text / website / YouTube / image themes)

    enum GeneratedKind: String, CaseIterable, Identifiable {
        case text, website, youtube, images
        var id: String { rawValue }
        var title: String {
            switch self {
            case .text: "Text"
            case .website: "Website"
            case .youtube: "YouTube Video"
            case .images: "Folder of Images"
            }
        }
    }

    struct GeneratedConfig {
        var name = "My Theme"
        var kind: GeneratedKind = .text
        var text = "Stand up, stretch, and look into the distance."
        var fontName = "Helvetica Neue"
        var fontSize: Double = 64
        var foreground = RGBA(r: 1, g: 1, b: 1)
        var url = "https://"
        var imagesFolder: URL?
        var cycleSeconds: Double = 10
        var randomOrder = true
    }

    @discardableResult
    func createTheme(_ c: GeneratedConfig) throws -> Theme {
        // Never overwrite an existing theme: pick a free folder name instead.
        var base = c.name.replacingOccurrences(of: "/", with: "-").replacingOccurrences(of: ":", with: "-")
            .trimmingCharacters(in: .whitespacesAndNewlines.union(CharacterSet(charactersIn: ".")))
        if base.isEmpty { base = "Theme" }
        var folder = Store.themesFolder.appendingPathComponent("\(base).\(Self.fileExtension)")
        var n = 2
        while FileManager.default.fileExists(atPath: folder.path) {
            folder = Store.themesFolder.appendingPathComponent("\(base) \(n).\(Self.fileExtension)")
            n += 1
        }
        let docs = folder.appendingPathComponent("Documents")
        try FileManager.default.createDirectory(at: docs, withIntermediateDirectories: true)
        var info: [String: Any] = [
            "name": c.name, "identifier": "user-\(UUID().uuidString)", "version": 1,
            "comments": "Created with Recess (\(c.kind.title)).",
            "created": ISO8601DateFormatter().string(from: Date()),
        ]
        switch c.kind {
        case .website:
            info["url"] = c.url
        case .youtube:
            guard let id = Self.youTubeID(c.url) else {
                try? FileManager.default.removeItem(at: folder)
                throw ThemeError.invalidYouTubeLink
            }
            info["rootDocument"] = "index.html"
            let html = """
                <!DOCTYPE html><html><head><meta charset="utf-8"><style>html,body{margin:0;height:100%;background:transparent;overflow:hidden}
                iframe{position:absolute;inset:8vh 8vw;width:84vw;height:84vh;border:0;border-radius:16px}</style></head>
                <body><iframe src="https://www.youtube-nocookie.com/embed/\(id)?autoplay=1&loop=1&playlist=\(id)&mute=0" allow="autoplay; encrypted-media" allowfullscreen></iframe></body></html>
                """
            try html.write(to: docs.appendingPathComponent("index.html"), atomically: true, encoding: .utf8)
        case .text:
            info["rootDocument"] = "index.html"
            let escaped = c.text.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;")
                .replacingOccurrences(of: ">", with: "&gt;")
            let html = """
                <!DOCTYPE html><html><head><meta charset="utf-8"><style>html,body{margin:0;height:100%;background:transparent;overflow:hidden}
                body{display:flex;align-items:center;justify-content:center}
                #text{max-width:80vw;text-align:center;white-space:pre-wrap;color:\(c.foreground.hex);font-family:'\(Self.safeFontName(c.fontName))',-apple-system,sans-serif;font-size:\(Int(c.fontSize))px;font-weight:600;line-height:1.25;text-shadow:0 2px 12px rgba(0,0,0,.35)}
                </style></head><body><div id="text">\(escaped)</div></body></html>
                """
            try html.write(to: docs.appendingPathComponent("index.html"), atomically: true, encoding: .utf8)
        case .images:
            info["rootDocument"] = "index.html"
            var names: [String] = []
            if let src = c.imagesFolder {
                let files = (try? FileManager.default.contentsOfDirectory(at: src, includingPropertiesForKeys: nil)) ?? []
                for f in files where ["jpg", "jpeg", "png", "heic", "gif", "webp", "tiff"].contains(f.pathExtension.lowercased()) {
                    let dest = docs.appendingPathComponent(f.lastPathComponent)
                    try? FileManager.default.copyItem(at: f, to: dest)
                    names.append(f.lastPathComponent)
                }
            }
            let list = (try? String(data: JSONSerialization.data(withJSONObject: names), encoding: .utf8)) ?? "[]"
            let html = """
                <!DOCTYPE html><html><head><meta charset="utf-8"><style>html,body{margin:0;height:100%;background:transparent;overflow:hidden}
                img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;opacity:0;transition:opacity 1.5s}img.on{opacity:1}</style></head>
                <body><img id="a"><img id="b"><script>
                const items=\(list);const random=\(c.randomOrder);let i=-1,front='a';
                function next(){if(!items.length)return;i=random?Math.floor(Math.random()*items.length):(i+1)%items.length;
                const back=front==='a'?'b':'a';const el=document.getElementById(back);el.onload=()=>{el.classList.add('on');document.getElementById(front).classList.remove('on');front=back};el.src=encodeURI(items[i]);}
                next();setInterval(next,\(Int(c.cycleSeconds * 1000)));
                </script></body></html>
                """
            try html.write(to: docs.appendingPathComponent("index.html"), atomically: true, encoding: .utf8)
        }
        let data = try JSONSerialization.data(withJSONObject: info, options: [.prettyPrinted])
        try data.write(to: folder.appendingPathComponent("Info.json"))
        reload()
        return themes.first { $0.folder?.standardizedFileURL == folder.standardizedFileURL } ?? Self.builtins[0]
    }

    func delete(_ theme: Theme) {
        guard theme.editable, let folder = theme.folder else { return }
        try? FileManager.default.trashItem(at: folder, resultingItemURL: nil)
        reload()
    }

    /// Extracts a YouTube video ID from a link or a bare ID. Only well-formed IDs are
    /// returned, because the ID is written into generated HTML.
    nonisolated static func youTubeID(_ s: String) -> String? {
        let candidate: String?
        let trimmed = s.trimmingCharacters(in: .whitespacesAndNewlines)
        if let comps = URLComponents(string: trimmed), comps.host != nil {
            if let v = comps.queryItems?.first(where: { $0.name == "v" })?.value {
                candidate = v
            } else if comps.host?.hasSuffix("youtu.be") == true {
                candidate = String(comps.path.dropFirst())
            } else if comps.path.contains("/embed/") || comps.path.contains("/shorts/") {
                candidate = comps.path.split(separator: "/").last.map(String.init)
            } else {
                candidate = nil
            }
        } else {
            candidate = trimmed
        }
        guard let id = candidate, id.range(of: "^[A-Za-z0-9_-]{11}$", options: .regularExpression) != nil else { return nil }
        return id
    }

    /// Font names go into CSS, so keep only characters that can appear in a font name.
    nonisolated static func safeFontName(_ s: String) -> String {
        let cleaned = s.filter { $0.isLetter || $0.isNumber || $0 == " " || $0 == "-" }
        return cleaned.isEmpty ? "Helvetica Neue" : String(cleaned.prefix(64))
    }
}
