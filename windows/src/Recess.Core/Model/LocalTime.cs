namespace Recess.Core;

/// <summary>
/// Calendar maths in the user's time zone. Dates are stored in UTC; days, hours and weekdays
/// are what the user's clock shows. Unlike TimeZoneInfo, nothing here throws on the hour
/// that's skipped or repeated when daylight saving starts or ends.
/// </summary>
public static class LocalTime
{
    /// <summary>A marker for "never", e.g. paused until resumed or no upcoming events.</summary>
    public static readonly DateTime DistantFuture = new(4001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime DistantPast = new(1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The user's calendar day for a moment.</summary>
    public static DateOnly Date(DateTime utc, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(utc, zone));

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>Calendar weekday, 1 = Sunday … 7 = Saturday (the numbering the settings file uses).</summary>
    public static int Weekday(DateOnly day) => (int)day.DayOfWeek + 1;

    /// <summary>
    /// The moment the clock shows <paramref name="hour"/>:<paramref name="minute"/> on <paramref name="day"/>.
    /// A time skipped by daylight saving moves forward by the length of the skip (2:30 becomes
    /// 3:30, as on the Mac); a time that happens twice is the first of the two.
    /// </summary>
    public static DateTime At(DateOnly day, int hour, int minute, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
        TimeSpan offset;
        if (zone.IsInvalidTime(local))
        {
            // Use the offset from just before the skip, which lands the same distance past it.
            var before = local;
            for (var i = 0; i < 48 && zone.IsInvalidTime(before); i++)
            {
                before = before.AddMinutes(-30);
            }
            offset = zone.GetUtcOffset(before);
        }
        else if (zone.IsAmbiguousTime(local))
        {
            offset = zone.GetAmbiguousTimeOffsets(local).Max();
        }
        else
        {
            offset = zone.GetUtcOffset(local);
        }
        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

    public static DateTime StartOfDay(DateTime utc, TimeZoneInfo zone) => At(Date(utc, zone), 0, 0, zone);

    public static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    public static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
