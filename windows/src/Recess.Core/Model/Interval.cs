using System.Globalization;

namespace Recess.Core;

public enum TimeUnit
{
    Seconds,
    Minutes,
    Hours,
    Days,
}

public static class TimeUnitExtensions
{
    public static double Seconds(this TimeUnit unit) => unit switch
    {
        TimeUnit.Seconds => 1,
        TimeUnit.Minutes => 60,
        TimeUnit.Hours => 3600,
        _ => 86400,
    };

    public static string Name(this TimeUnit unit, bool plural) => unit switch
    {
        TimeUnit.Seconds => plural ? "seconds" : "second",
        TimeUnit.Minutes => plural ? "minutes" : "minute",
        TimeUnit.Hours => plural ? "hours" : "hour",
        _ => plural ? "days" : "day",
    };
}

/// <summary>An amount plus units, e.g. 10 minutes.</summary>
public readonly record struct Interval(double Amount, TimeUnit Units)
{
    public static Interval FromSeconds(double v) => new(v, TimeUnit.Seconds);

    public static Interval FromMinutes(double v) => new(v, TimeUnit.Minutes);

    public static Interval FromHours(double v) => new(v, TimeUnit.Hours);

    public double TotalSeconds => Amount * Units.Seconds();

    public string FullDescription
    {
        get
        {
            var n = Amount == Math.Round(Amount)
                ? Format.Int(Amount).ToString(CultureInfo.InvariantCulture)
                : Amount.ToString("0.0", CultureInfo.InvariantCulture);
            return $"{n} {Units.Name(Amount != 1)}";
        }
    }
}

/// <summary>A time of day (hour and minute).</summary>
public readonly record struct TimeOfDay(int Hour, int Minute);

public static class Format
{
    /// <summary>Converts to an integer without failing on NaN, infinity or out-of-range values.</summary>
    public static long Int(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }
        return (long)Math.Max(-1e15, Math.Min(1e15, value));
    }

    /// <summary>"1:05:09", "4:09", "0:09"</summary>
    public static string Countdown(double seconds)
    {
        var s = Math.Max(0, Int(Math.Ceiling(seconds)));
        long h = s / 3600, m = s % 3600 / 60, sec = s % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }

    /// <summary>"5m", "1h 5m", "30s"</summary>
    public static string Brief(double seconds)
    {
        var s = Math.Max(0, Int(Round(seconds)));
        if (s < 60)
        {
            return $"{s}s";
        }
        long h = s / 3600, m = s % 3600 / 60;
        if (h == 0)
        {
            return $"{m}m";
        }
        if (h >= 24)
        {
            return $"{h / 24}d {h % 24}h";
        }
        return m == 0 ? $"{h}h" : $"{h}h {m}m";
    }

    /// <summary>"5 minutes", "1 hour, 5 minutes"</summary>
    public static string Full(double seconds)
    {
        var s = Math.Max(0, Int(Round(seconds)));
        static string Part(long v, string unit) => $"{v} {unit}{(v == 1 ? "" : "s")}";
        if (s < 60)
        {
            return Part(s, "second");
        }
        long d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60;
        var parts = new List<string>();
        if (d > 0)
        {
            parts.Add(Part(d, "day"));
        }
        if (h > 0)
        {
            parts.Add(Part(h, "hour"));
        }
        if (m > 0 && d == 0)
        {
            parts.Add(Part(m, "minute"));
        }
        return string.Join(", ", parts);
    }

    /// <summary>A clock time in the user's format, e.g. "14:05" or "2:05 PM".</summary>
    public static string Time(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("t", CultureInfo.CurrentCulture);

    public static string Relative(DateTime date, DateTime now)
    {
        var diff = (date - now).TotalSeconds;
        if (Math.Abs(diff) < 1)
        {
            return "now";
        }
        return diff > 0 ? $"in {Full(diff)}" : $"{Full(-diff)} ago";
    }

    public static string Absolute(DateTime date, DateTime now, TimeZoneInfo zone)
    {
        var day = LocalTime.Date(date, zone);
        var today = LocalTime.Date(now, zone);
        var time = Time(date, zone);
        if (day == today)
        {
            return $"today at {time}";
        }
        if (day == today.AddDays(1))
        {
            return $"tomorrow at {time}";
        }
        if (day == today.AddDays(-1))
        {
            return $"yesterday at {time}";
        }
        return TimeZoneInfo.ConvertTimeFromUtc(date, zone).ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary>Rounds halves away from zero, as people (and the Mac app) expect.</summary>
    public static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
