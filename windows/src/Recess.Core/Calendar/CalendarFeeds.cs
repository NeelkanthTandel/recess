using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;

namespace Recess.Core;

/// <summary>A timed (not all-day) event from a calendar.</summary>
public sealed record CalendarEventInfo(DateTime Start, DateTime End, string Text, bool Busy);

/// <summary>
/// The user's calendars (iCal feeds or .ics files), parsed, for calendar-timed breaks and
/// "busy" rules. The app downloads or reads each feed and hands the text to <see cref="Update"/>.
/// </summary>
public sealed class CalendarFeeds : ICalendarSource
{
    /// <summary>How far ahead to look for the next event, as on the Mac.</summary>
    public static readonly TimeSpan LookAhead = TimeSpan.FromDays(14);

    // A recurrence rule that never matches (or matches every second) can't run away.
    private const int MaxOccurrences = 5000;

    private readonly Dictionary<string, Ical.Net.Calendar> calendars = [];
    private readonly Dictionary<string, (DateTime Checked, bool Busy)> busyCache = [];

    /// <summary>Raised when a calendar's events change.</summary>
    public event Action? Changed;

    /// <summary>Replaces a feed's events with newly downloaded or read iCal text. Throws <see cref="FormatException"/> if it isn't iCal.</summary>
    public void Update(string feedId, string icsText)
    {
        var calendar = Parse(icsText);
        calendars[feedId] = calendar;
        busyCache.Clear();
        Changed?.Invoke();
    }

    public void Remove(string feedId)
    {
        if (calendars.Remove(feedId))
        {
            busyCache.Clear();
            Changed?.Invoke();
        }
    }

    public bool Has(string feedId) => calendars.ContainsKey(feedId);

    public static Ical.Net.Calendar Parse(string icsText)
    {
        if (!icsText.TrimStart().StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("That isn't an iCal calendar.");
        }
        try
        {
            return Ical.Net.Calendar.Load(icsText) ?? throw new FormatException("That isn't an iCal calendar.");
        }
        catch (Exception ex) when (ex is not FormatException)
        {
            throw new FormatException("The calendar couldn't be read: " + ex.Message, ex);
        }
    }

    /// <summary>Timed events overlapping a period, from one calendar (or all), soonest first.</summary>
    public List<CalendarEventInfo> Events(string? calendarId, DateTime from, DateTime to)
    {
        var result = new List<CalendarEventInfo>();
        foreach (var (id, calendar) in calendars)
        {
            if (calendarId is not null && id != calendarId)
            {
                continue;
            }
            result.AddRange(Events(calendar, from, to));
        }
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    public static IEnumerable<CalendarEventInfo> Events(Ical.Net.Calendar calendar, DateTime from, DateTime to)
    {
        var options = new EvaluationOptions { MaxUnmatchedIncrementsLimit = 1000 };
        // Start early enough to catch events already under way (up to a day long).
        var start = new CalDateTime(from.AddDays(-1).ToUniversalTime(), "UTC");
        IEnumerable<Occurrence> occurrences;
        try
        {
            occurrences = calendar.GetOccurrences<CalendarEvent>(start, options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Ical.Net.Evaluation.EvaluationLimitExceededException)
        {
            yield break;
        }
        using var e = occurrences.GetEnumerator();
        for (var n = 0; n < MaxOccurrences; n++)
        {
            Occurrence occ;
            try
            {
                if (!e.MoveNext())
                {
                    yield break;
                }
                occ = e.Current;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Ical.Net.Evaluation.EvaluationLimitExceededException)
            {
                yield break;
            }
            if (occ.Source is not CalendarEvent ev || !occ.Period.StartTime.HasTime || ev.IsAllDay)
            {
                continue;
            }
            var startUtc = occ.Period.StartTime.AsUtc;
            if (startUtc >= to)
            {
                yield break;
            }
            var endUtc = occ.Period.EffectiveEndTime?.AsUtc ?? startUtc;
            if (endUtc <= from || string.Equals(ev.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var text = string.Join(' ', new[] { ev.Summary, ev.Location, ev.Description, ev.Url?.ToString() }.Where(s => !string.IsNullOrEmpty(s)));
            var busy = !string.Equals(ev.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase);
            yield return new CalendarEventInfo(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), DateTime.SpecifyKind(endUtc, DateTimeKind.Utc), text, busy);
        }
    }

    /// <summary>
    /// Whether an event matches a filter: comma-separated words, any of which may appear in its
    /// title, location, notes or link (ignoring case). An empty filter matches every event.
    /// </summary>
    public static bool Matches(CalendarEventInfo e, string filter)
    {
        var terms = filter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return terms.Length == 0 || terms.Any(t => e.Text.Contains(t, StringComparison.CurrentCultureIgnoreCase));
    }

    public (DateTime Start, DateTime End)? NextEvent(string? calendarId, string filter, DateTime after)
    {
        var next = Events(calendarId, after, after + LookAhead).FirstOrDefault(e => e.Start > after && Matches(e, filter));
        return next is null ? null : (next.Start, next.End);
    }

    /// <summary>True while a matching, busy event is happening. Cached for 30 seconds, because rules can ask every second.</summary>
    public bool IsBusy(string? calendarId, string filter, DateTime now)
    {
        var key = (calendarId ?? "*") + "|" + filter;
        if (busyCache.TryGetValue(key, out var hit) && (now - hit.Checked).TotalSeconds is >= 0 and < 30)
        {
            return hit.Busy;
        }
        var busy = Events(calendarId, now, now.AddMinutes(1)).Any(e => e.Start <= now && e.End > now && e.Busy && Matches(e, filter));
        busyCache[key] = (now, busy);
        return busy;
    }
}
