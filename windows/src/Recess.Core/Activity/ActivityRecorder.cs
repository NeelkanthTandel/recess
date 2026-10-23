using System.Globalization;

namespace Recess.Core;

/// <summary>Per-day totals for the statistics chart.</summary>
public sealed record DayStats(DateOnly Day, int Postponed, int Skipped, int Done, double Duration);

/// <summary>Records breaks taken and app usage, one file per day, and sums them up for the statistics chart.</summary>
public sealed class ActivityRecorder
{
    private readonly string folder;
    private readonly Func<AppSettings> settings;
    private readonly IClock clock;
    private readonly Dictionary<string, ActivityDayRecord> days = [];
    private readonly HashSet<string> dirty = [];
    private DateTime lastSave = DateTime.MinValue;
    private DateTime? lastTick;

    public ActivityRecorder(string folder, Func<AppSettings> settings, IClock clock)
    {
        this.folder = folder;
        this.settings = settings;
        this.clock = clock;
    }

    /// <summary>Raised when breaks are logged or activity is cleared (not on the per-second app ticks).</summary>
    public event Action? Changed;

    public static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private string PathFor(string key) => Path.Combine(folder, key + ".json");

    public ActivityDayRecord Record(DateOnly day)
    {
        var key = Key(day);
        if (days.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var record = new ActivityDayRecord { Day = key };
        var path = PathFor(key);
        if (File.Exists(path))
        {
            if (Store.Read<ActivityDayRecord>(path) is { } saved)
            {
                record = saved;
            }
            else
            {
                // Don't let a later save overwrite a day we couldn't read.
                Store.BackUpUnreadable(path);
            }
        }
        days[key] = record;
        return record;
    }

    private ActivityDayRecord Today(DateTime now)
    {
        var record = Record(LocalTime.Date(now, clock.Zone));
        dirty.Add(record.Day);
        return record;
    }

    public void Log(Break b, BreakEventKind kind, double duration = 0, string detail = "")
    {
        if (!settings().TrackBreaks)
        {
            return;
        }
        var now = clock.UtcNow;
        Today(now).Events.Add(new BreakEvent
        {
            Date = now,
            BreakId = b.Id,
            BreakName = b.Name,
            Kind = kind,
            Duration = duration,
            Detail = detail,
        });
        Changed?.Invoke();
        SaveIfNeeded(force: true);
    }

    /// <summary>Called every second. <paramref name="frontApp"/> is the front app's file name and display name.</summary>
    public void Tick(DateTime now, bool idle, Func<(string Id, string Name)?> frontApp)
    {
        var previous = lastTick;
        lastTick = now;
        if (!settings().TrackApps)
        {
            return;
        }
        var gap = previous is { } p ? (now - p).TotalSeconds : 1;
        if (gap is >= 10 or < 0)
        {
            return;
        }
        var front = idle ? null : frontApp();
        var id = idle ? "" : front?.Id ?? "";
        var name = idle ? "Idle" : front?.Name ?? "Unknown";
        var apps = Today(now).Apps;
        if (apps.Count > 0 && apps[^1].AppId == id && (now - apps[^1].End).TotalSeconds < 10)
        {
            apps[^1].End = now;
        }
        else
        {
            apps.Add(new AppSpan { AppId = id, Name = name, Start = now.AddSeconds(-gap), End = now });
        }
        SaveIfNeeded(force: false);
    }

    public void SaveIfNeeded(bool force)
    {
        if (force || (clock.UtcNow - lastSave).TotalSeconds > 60)
        {
            Save();
        }
    }

    public void Save()
    {
        lastSave = clock.UtcNow;
        foreach (var key in dirty)
        {
            if (days.TryGetValue(key, out var record))
            {
                try
                {
                    Store.Write(record, PathFor(key));
                }
                catch (IOException)
                {
                    // Try again on the next save.
                    continue;
                }
            }
        }
        dirty.Clear();
        // Keep memory small: forget cached days other than today.
        var today = Key(LocalTime.Date(clock.UtcNow, clock.Zone));
        foreach (var key in days.Keys.Where(k => k != today).ToList())
        {
            days.Remove(key);
        }
    }

    /// <summary>Days with saved activity, newest first, always including today.</summary>
    public List<DateOnly> AvailableDays()
    {
        var keys = new HashSet<string> { Key(LocalTime.Date(clock.UtcNow, clock.Zone)) };
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                keys.Add(Path.GetFileNameWithoutExtension(file));
            }
        }
        return keys
            .Select(k => DateOnly.TryParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null)
            .OfType<DateOnly>()
            .OrderDescending()
            .ToList();
    }

    public void ClearAll()
    {
        days.Clear();
        dirty.Clear();
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                File.Delete(file);
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Totals for each of the last <paramref name="count"/> days, oldest first; all breaks when <paramref name="breakId"/> is null.</summary>
    public List<DayStats> Stats(int count, Guid? breakId)
    {
        var today = LocalTime.Date(clock.UtcNow, clock.Zone);
        var result = new List<DayStats>();
        for (var offset = count - 1; offset >= 0; offset--)
        {
            var day = today.AddDays(-offset);
            int postponed = 0, skipped = 0, done = 0;
            double duration = 0;
            foreach (var e in Record(day).Events.Where(e => breakId is null || e.BreakId == breakId))
            {
                switch (e.Kind)
                {
                    case BreakEventKind.Postponed:
                        postponed++;
                        break;
                    case BreakEventKind.Skipped:
                        skipped++;
                        break;
                    case BreakEventKind.Done or BreakEventKind.EndedEarly:
                        done++;
                        duration += e.Duration;
                        break;
                }
            }
            result.Add(new DayStats(day, postponed, skipped, done, duration));
        }
        return result;
    }
}
