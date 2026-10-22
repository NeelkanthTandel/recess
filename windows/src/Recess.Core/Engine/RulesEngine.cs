namespace Recess.Core;

/// <summary>Evaluates the user's rules. The first enabled rule that matches wins.</summary>
public sealed class RulesEngine(ICalendarSource calendar, IScriptConditions scripts)
{
    private bool Matches(Rule rule, IRuleEnvironment env, DateTime now)
    {
        var raw = rule.Condition switch
        {
            RuleCondition.FrontApp => env.FrontApp is { } front && rule.Apps.Contains(front, StringComparer.OrdinalIgnoreCase),
            RuleCondition.OpenApp => rule.Apps.Any(a => env.RunningApps.Contains(a)),
            RuleCondition.Focus => env.IsFocusOn,
            RuleCondition.Fullscreen => env.IsFullscreen,
            RuleCondition.Screensaver => env.IsScreensaver,
            RuleCondition.DisplayAsleep => env.IsDisplayAsleep,
            RuleCondition.CalendarEvent => calendar.IsBusy(rule.CalendarIdentifier, rule.CalendarFilter, now),
            RuleCondition.Script => rule.ScriptFilename.Length > 0 && scripts.Result(rule.ScriptFilename),
            RuleCondition.MicOrCamera => env.IsMicOrCameraInUse,
            _ => false,
        };
        return rule.Negate ? !raw : raw;
    }

    private Rule? Matching(IEnumerable<Rule> rules, IRuleEnvironment env, DateTime now, Func<Rule, bool> predicate) =>
        rules.FirstOrDefault(r => r.Enabled && predicate(r) && Matches(r, env, now));

    /// <summary>A postpone or skip rule that matches for a due break.</summary>
    public Rule? DueRule(Break b, IEnumerable<Rule> rules, IRuleEnvironment env, DateTime now) =>
        Matching(rules, env, now, r => r.Action.IsDueAction() && r.AppliesTo(b));

    public Rule? TreatAsIdle(IEnumerable<Rule> rules, IRuleEnvironment env, DateTime now) =>
        Matching(rules, env, now, r => r.Action == RuleAction.TreatAsNaturalBreak);

    public Rule? IgnoreIdle(IEnumerable<Rule> rules, IRuleEnvironment env, DateTime now) =>
        Matching(rules, env, now, r => r.Action == RuleAction.IgnoreNaturalBreak);

    /// <summary>"Front app is Zoom → postpone 5 minutes"</summary>
    public static string Describe(Rule rule, Func<string, string> appName)
    {
        var s = rule.Condition.Title() + (rule.Negate ? " is not" : " is");
        if (rule.Condition.NeedsApps())
        {
            s += " " + (rule.Apps.Count == 0 ? "(no apps)" : string.Join(", ", rule.Apps.Select(appName)));
        }
        else if (rule.Condition == RuleCondition.Script)
        {
            s += " true for " + (rule.ScriptFilename.Length == 0 ? "(no script)" : rule.ScriptFilename);
        }
        else
        {
            s += " active";
        }
        return s + " → " + rule.Action.Title();
    }
}
