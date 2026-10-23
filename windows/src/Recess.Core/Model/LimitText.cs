namespace Recess.Core;

public static class LimitText
{
    /// <summary>"2 postpones and 0 skips left this hour", or null when the break has no limits.</summary>
    public static string? Remaining(Break b)
    {
        var parts = new List<string>();
        if (b.PostponesRemaining is { } p)
        {
            parts.Add($"{p} postpone{(p == 1 ? "" : "s")} left {Period(b.Controls.PostponeLimitKind)}");
        }
        if (b.SkipsRemaining is { } k)
        {
            parts.Add($"{k} skip{(k == 1 ? "" : "s")} left {Period(b.Controls.SkipLimitKind)}");
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Period(LimitKind kind) => kind == LimitKind.PerDay ? "today" : $"this {kind.Title()}";
}
