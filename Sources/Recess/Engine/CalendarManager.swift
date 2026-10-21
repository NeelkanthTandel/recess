import EventKit
import Foundation

/// Calendar-based schedules and calendar rules.
@MainActor
final class CalendarManager: ObservableObject {
    static let shared = CalendarManager()

    let store = EKEventStore()
    @Published private(set) var authorized = false
    @Published private(set) var calendars: [EKCalendar] = []

    private init() {
        refreshAuthorization()
        NotificationCenter.default.addObserver(forName: .EKEventStoreChanged, object: store, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.reloadCalendars() }
        }
    }

    func refreshAuthorization() {
        authorized = EKEventStore.authorizationStatus(for: .event) == .fullAccess
        reloadCalendars()
    }

    func requestAccess() {
        store.requestFullAccessToEvents { [weak self] _, _ in
            DispatchQueue.main.async { self?.refreshAuthorization() }
        }
    }

    func reloadCalendars() {
        calendars = authorized ? store.calendars(for: .event).sorted { $0.title < $1.title } : []
    }

    private func matches(_ event: EKEvent, filter: String) -> Bool {
        if event.isAllDay { return false }
        let terms = filter.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces).lowercased() }.filter { !$0.isEmpty }
        guard !terms.isEmpty else { return true }
        let haystack = [event.title, event.location, event.notes, event.url?.absoluteString].compactMap { $0 }.joined(separator: " ")
            .lowercased()
        return terms.contains { haystack.contains($0) }
    }

    private func events(calendarID: String?, from: Date, to: Date) -> [EKEvent] {
        guard authorized else { return [] }
        var cals: [EKCalendar]? = nil
        if let calendarID, let cal = store.calendar(withIdentifier: calendarID) { cals = [cal] }
        let predicate = store.predicateForEvents(withStart: from, end: to, calendars: cals)
        return store.events(matching: predicate).sorted { $0.startDate < $1.startDate }
    }

    /// Next matching event starting after `date` (within two weeks).
    func nextEvent(calendarID: String?, filter: String, after date: Date) -> (start: Date, end: Date)? {
        let list = events(calendarID: calendarID, from: date, to: date.addingTimeInterval(14 * 86400))
        guard let e = list.first(where: { $0.startDate > date && matches($0, filter: filter) }) else { return nil }
        return (e.startDate, e.endDate)
    }

    private var busyCache: [String: (checked: Date, busy: Bool)] = [:]

    /// True when a matching event is in progress right now. Cached for 30 seconds, because
    /// rules can ask every second.
    func isBusy(calendarID: String?, filter: String, at date: Date = Date()) -> Bool {
        let key = (calendarID ?? "*") + "|" + filter
        if let hit = busyCache[key], date.timeIntervalSince(hit.checked) < 30 { return hit.busy }
        let busy = events(calendarID: calendarID, from: date.addingTimeInterval(-86400), to: date.addingTimeInterval(60))
            .contains { $0.startDate <= date && $0.endDate > date && matches($0, filter: filter) && $0.availability != .free }
        busyCache[key] = (date, busy)
        return busy
    }
}
