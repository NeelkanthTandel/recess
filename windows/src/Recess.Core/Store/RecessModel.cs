namespace Recess.Core;

/// <summary>The breaks and settings shared by the scheduler, the tray icon and the settings window.</summary>
public sealed class RecessModel
{
    private readonly Store? store;
    private bool dirty;

    public RecessModel(List<Break> breaks, AppSettings settings, Store? store = null)
    {
        Breaks = breaks;
        Settings = settings;
        this.store = store;
    }

    /// <summary>Loads from the store; a missing or unreadable file falls back to the defaults.</summary>
    public static RecessModel Load(Store store) =>
        new(store.LoadBreaks() ?? [Break.MakeNormal(), Break.MakeMicro()], store.LoadSettings() ?? new AppSettings(), store);

    /// <summary>In priority order: earlier breaks win over later ones.</summary>
    public List<Break> Breaks { get; }

    public AppSettings Settings { get; set; }

    /// <summary>The break on screen right now, if any.</summary>
    public Guid? ActiveBreakId { get; set; }

    /// <summary>Raised after anything changes; the tray icon and settings window refresh on it.</summary>
    public event Action? Changed;

    public void MarkChanged()
    {
        dirty = true;
        Changed?.Invoke();
    }

    public Break? Find(Guid id) => Breaks.Find(b => b.Id == id);

    public int IndexOf(Guid id) => Breaks.FindIndex(b => b.Id == id);

    public Break? FindByName(string name) => Breaks.Find(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Changes a break and marks the model changed. Does nothing if the break is gone.</summary>
    public void Update(Guid id, Action<Break> change)
    {
        if (Find(id) is { } b)
        {
            change(b);
            MarkChanged();
        }
    }

    /// <summary>The break due soonest, ignoring paused and disabled ones (and short ones if the tray shows only long breaks).</summary>
    public Break? NextBreak(DateTime now) => Breaks
        .Where(b => b.IsActive(now) && b.State.NextDue is not null && (!Settings.StatusOnlyLong || b.IsLong))
        .MinBy(b => b.State.NextDue);

    public Break? NextBreakAnyLength(DateTime now) => Breaks
        .Where(b => b.IsActive(now) && b.State.NextDue is not null)
        .MinBy(b => b.State.NextDue);

    public Break? LastDoneBreak => Breaks
        .Where(b => b.State.LastDone is not null && (!Settings.StatusOnlyLong || b.IsLong))
        .MaxBy(b => b.State.LastDone);

    public bool AllPaused(DateTime now) => Breaks.Count > 0 && Breaks.All(b => !b.Enabled || b.IsPaused(now));

    public string DateString(DateTime? date, DateTime now, TimeZoneInfo zone)
    {
        if (date is not { } d)
        {
            return "never";
        }
        if (d >= LocalTime.DistantFuture)
        {
            return "no upcoming events";
        }
        return Settings.RelativeDates ? Format.Relative(d, now) : Format.Absolute(d, now, zone);
    }

    /// <summary>Saves if anything changed since the last save. Called about once a second, which also throttles saving.</summary>
    public void SaveIfChanged()
    {
        if (dirty)
        {
            SaveNow();
        }
    }

    public void SaveNow()
    {
        dirty = false;
        store?.Save(Settings, Breaks);
    }
}
