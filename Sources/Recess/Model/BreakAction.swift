import Foundation

enum ActionKind: String, Codable, CaseIterable, Identifiable {
    case notification, sound, fadeSound, speech, flashScreen, script
    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .notification: "Notification"
        case .sound: "Play Sound"
        case .fadeSound: "Fade Out Sound"
        case .speech: "Speak"
        case .flashScreen: "Flash Screen"
        case .script: "Run Script"
        }
    }

    var symbol: String {
        switch self {
        case .notification: "bell.badge"
        case .sound: "speaker.wave.2"
        case .fadeSound: "speaker.wave.1"
        case .speech: "waveform"
        case .flashScreen: "sun.max"
        case .script: "applescript"
        }
    }
}

/// When during a break's life an action is performed. `offset` applies to the "before" stages.
enum ActionStage: String, Codable, CaseIterable, Identifiable {
    case beforeStart, atStart, afterFadeIn, beforeEnd, atEnd, afterPostponeOrSkip
    var id: String { rawValue }
    var title: String {
        switch self {
        case .beforeStart: "ahead of the break"
        case .atStart: "at start"
        case .afterFadeIn: "after fade in"
        case .beforeEnd: "before end"
        case .atEnd: "when it ends"
        case .afterPostponeOrSkip: "when postponed or skipped"
        }
    }
    var usesOffset: Bool { self == .beforeStart || self == .beforeEnd }
}

struct BreakAction: Codable, Identifiable, Hashable {
    var id = UUID()
    var kind: ActionKind
    var stage: ActionStage
    /// Seconds before the stage (for `beforeStart` / `beforeEnd`).
    var offset: Double = 0
    var enabled = true

    // Notification
    var title = ""
    var message = ""
    // Sound / notification sound
    var soundName = ""
    var soundPath = ""
    var volume: Double = 0.8
    var loops = false
    // Fade
    var fadeSeconds: Double = 3
    // Speech
    var voiceIdentifier = ""
    var rate: Double = 0.5
    var pitch: Double = 1
    // Script
    var scriptFilename = ""

    init(kind: ActionKind, stage: ActionStage, offset: Double = 0) {
        self.kind = kind
        self.stage = stage
        self.offset = offset
        switch kind {
        case .notification:
            title = "Break time"
            message = "Time for a {break} break."
        case .speech:
            message = "Time for a {break} break."
        case .sound:
            soundName = "Glass"
        default: break
        }
    }

    var summary: String {
        switch kind {
        case .notification: title.isEmpty ? message : title
        case .sound: soundName.isEmpty ? (soundPath as NSString).lastPathComponent : soundName
        case .fadeSound: "over \(Format.int(fadeSeconds)) seconds"
        case .speech: message
        case .flashScreen: "flash all screens"
        case .script: scriptFilename.isEmpty ? "No script chosen" : scriptFilename
        }
    }

    enum CodingKeys: String, CodingKey {
        case id, kind, stage, offset, enabled, title, message, soundName, soundPath, volume, loops, fadeSeconds, voiceIdentifier, rate,
            pitch, scriptFilename
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kind = try c.decode(ActionKind.self, forKey: .kind)
        stage = (try? c.decode(ActionStage.self, forKey: .stage)) ?? .atStart
        id = (try? c.decode(UUID.self, forKey: .id)) ?? UUID()
        offset = (try? c.decode(Double.self, forKey: .offset)) ?? 0
        enabled = (try? c.decode(Bool.self, forKey: .enabled)) ?? true
        title = (try? c.decode(String.self, forKey: .title)) ?? ""
        message = (try? c.decode(String.self, forKey: .message)) ?? ""
        soundName = (try? c.decode(String.self, forKey: .soundName)) ?? ""
        soundPath = (try? c.decode(String.self, forKey: .soundPath)) ?? ""
        volume = (try? c.decode(Double.self, forKey: .volume)) ?? 0.8
        loops = (try? c.decode(Bool.self, forKey: .loops)) ?? false
        fadeSeconds = (try? c.decode(Double.self, forKey: .fadeSeconds)) ?? 3
        voiceIdentifier = (try? c.decode(String.self, forKey: .voiceIdentifier)) ?? ""
        rate = (try? c.decode(Double.self, forKey: .rate)) ?? 0.5
        pitch = (try? c.decode(Double.self, forKey: .pitch)) ?? 1
        scriptFilename = (try? c.decode(String.self, forKey: .scriptFilename)) ?? ""
    }
}
