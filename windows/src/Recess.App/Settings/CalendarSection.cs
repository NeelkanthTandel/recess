namespace Recess.App.Settings;

/// <summary>The Calendars section of General. Filled in when calendar support arrives.</summary>
internal static class CalendarSection
{
    public static void Add(Form f, SettingsContext ctx)
    {
        _ = ctx;
        var s = f.Section("Calendars");
        f.Hint(s, "Calendar-timed breaks and \"busy in a meeting\" rules arrive in a coming update.");
    }
}
